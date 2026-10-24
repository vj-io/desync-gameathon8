using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Desync.Content;
using Desync.Enemies;
using Desync.Gen;
using Desync.Levels;
using Desync.Net;
using Desync.Player;
using Desync.Rules;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Desync.Flow
{
    /// The BREACH run: CAMPAIGN (acts 1-3: AUDITOR, COMPILER, ARCHITECT), QUICK (act 1), DAILY (campaign on the UTC-day
    /// seed), CLASSIC (the six original rooms, unchanged flow), and Deep Layer: a CAMPAIGN under heat mutators (DeepLayer.cs,
    /// BeginDeep / DeepRun.StartRequested). Run graph -> map choice -> node: room (built into the Breach scene) -> results ->
    /// exploit draft -> map; Black Market, Cooldown. Deaths spend a Backup or end the run; a boss offers cash-out or the next
    /// act, and its first kill is saved (RunUnlocks). Boss arenas: RunBosses. Writes RunState and raises RunEvents
    /// (RunContracts.cs); the UI reads and requests. Self-bootstraps.
    ///
    /// Phases: Map (choose a node) -> Room (playing, recap) -> Results (room results; on a boss: CashOut / GoDeeper, and
    /// ContinueRequested = GoDeeper, or the run's end after the final act) -> Draft (pick/skip/reroll) -> Map. Market and
    /// Cooldown return to Map with Leave (a Cooldown choice also returns). Over = run results (RunEnded); quitting -> None.
    [DefaultExecutionOrder(-110)]   // live Modifiers are written before the player, ScoreKeeper and RoomManager read them
    public class RunManager : MonoBehaviour
    {
        public static RunManager I;
        public const string StageScene = RoomBuilder.BreachScene;

        /// Builds a generated room under `parent` and calls `done(handle)` once it is playable; handle.Dispose() removes it.
        /// Default: Levels.RoomBuilder.BuildAsync. Tests swap in a stub.
        public static Action<RoomSpec, Transform, Action<IDisposable>> BuildRoom = (spec, parent, done) => RoomBuilder.BuildAsync(spec, parent, h => done(h));

        Catalog catalog, draftPool;
        ChunkLibrary chunks;
        Built built;
        ConditionContext ctx;
        RunNode node;
        RoomSpec spec;
        GameObject roomRoot;
        Transform roomHost;   // what the run adds to a room goes under the room handle, so its Dispose frees the meshes too
        IDisposable roomHandle;
        Camera backdrop;      // renders between rooms (map, draft, market, results of the run): the Breach stage has no camera
        AudioListener ears;   // and hears the music there
        readonly List<Mesh> zoneMeshes = new List<Mesh>();
        Mulberry32 draftRng;
        bool lost, draftRarePlus, nextAct, backupSold, staging;
        int bestChain, token;
        RunBosses.Arena bossArena;    // the boss node's arena (null elsewhere, or when the boss node fell back to a generated room)
        Catalog runPool;              // this run's draft / market pool: draftPool minus what the Deep Layer picks exclude
        HeatPlacement heatPlacement;  // Deep Layer threats the generator adds
        float deadline = -1f;         // Tight Deadline: room time of its +1 TRACE (-1 = off)
        bool deadlineDone;
        int grazeSurge;               // Redline Surge: the last cleared room's REDLINE exits = +GRAZE cap per ICE in the next room
        int ghostCacheHits;           // Ghost Cache exits this run (Fewer Choices pays every second one)

        /// The boss spawned in the current room ("auditor", "compiler", "architect"), null elsewhere.
        public string CurrentBoss { get; private set; }
        bool NoBackups => DeepRules.Has(DeepRun.Picks, "no_backups");

        /// A run room is being built or played (RoomManager hands deaths, continue and targets to the run).
        public bool RoomLive { get; private set; }
        /// Set at the death recap when no Backup was left: the recap ends in the run results, not a respawn.
        public bool RunLost => lost;
        /// A run is in progress (not before it, not on its results).
        public static bool Active => RunState.Phase != RunPhase.None && RunState.Phase != RunPhase.Over;
        public RoomSpec CurrentSpec => spec;
        public RunNode CurrentNode => node;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (I) return;
            var go = new GameObject("RunManager");
            DontDestroyOnLoad(go);
            I = go.AddComponent<RunManager>();
            I.backdrop = go.AddComponent<Camera>();
            I.backdrop.clearFlags = CameraClearFlags.SolidColor;
            I.backdrop.backgroundColor = GameConfig.I.fogColor;
            I.backdrop.cullingMask = 0;   // nothing but the background; the run screens draw over it
            I.backdrop.enabled = false;
            I.ears = go.AddComponent<AudioListener>();
            I.ears.enabled = false;
        }

        void OnEnable()
        {
            RunEvents.StartRun += OnStartRun;
            RunEvents.ChooseNode += OnChooseNode;
            RunEvents.PickExploit += OnPickExploit;
            RunEvents.RerollOffer += OnRerollOffer;
            RunEvents.Buy += OnBuy;
            RunEvents.RemoveExploit += OnRemoveExploit;
            RunEvents.ChooseCooldown += OnChooseCooldown;
            RunEvents.Leave += OnLeave;
            RunEvents.CashOut += OnCashOut;
            RunEvents.GoDeeper += OnGoDeeper;
            GameEvents.QuitRequested += OnQuit;
            GameEvents.StartRequested += OnClassicStart;
            GameEvents.RoomStarted += OnRoomStarted;
            GameEvents.RoomReset += OnRoomReset;
            DeepRun.StartRequested += OnStartDeep;
            BossEvents.MeltChanged += OnBossMelt;
            BossEvents.Ending += OnEnding;
        }

        void OnDisable()
        {
            RunEvents.StartRun -= OnStartRun;
            RunEvents.ChooseNode -= OnChooseNode;
            RunEvents.PickExploit -= OnPickExploit;
            RunEvents.RerollOffer -= OnRerollOffer;
            RunEvents.Buy -= OnBuy;
            RunEvents.RemoveExploit -= OnRemoveExploit;
            RunEvents.ChooseCooldown -= OnChooseCooldown;
            RunEvents.Leave -= OnLeave;
            RunEvents.CashOut -= OnCashOut;
            RunEvents.GoDeeper -= OnGoDeeper;
            GameEvents.QuitRequested -= OnQuit;
            GameEvents.StartRequested -= OnClassicStart;
            GameEvents.RoomStarted -= OnRoomStarted;
            GameEvents.RoomReset -= OnRoomReset;
            DeepRun.StartRequested -= OnStartDeep;
            BossEvents.MeltChanged -= OnBossMelt;
            BossEvents.Ending -= OnEnding;
        }

        /// Conditional exploit effects (chain>=, melt>=, trace>=, layer=) re-folded whenever one of their inputs moved.
        void Update()
        {
            SyncBackdrop();
            TickDeadline();
            if (!RoomLive || built == null || built.conditional.Count == 0) return;
            var c = Context();
            if (c.chain == ctx.chain && c.melt == ctx.melt && c.trace == ctx.trace && c.layer == ctx.layer) return;
            ctx = c;
            GameState.Mods = LiveMods(c);
        }

        /// The backdrop camera + listener are on during a run whenever no room camera is up (one listener at a time).
        void SyncBackdrop()
        {
            bool on = RunState.Phase != RunPhase.None && !(RoomLive && PlayerRef.Cam && PlayerRef.Cam.isActiveAndEnabled);
            if (!backdrop || backdrop.enabled == on) return;
            backdrop.enabled = on;
            ears.enabled = on;
        }

        void OnRoomStarted(int room) => SyncBackdrop();

        /// Tight Deadline: +1 TRACE once the room clock passes the tightened par, also while a TRACE runs (Sustain only
        /// counts a new one, so the running one is closed first: count + 1, timer refilled, TraceStarted, Alarm on the 3rd).
        void TickDeadline()
        {
            if (deadline < 0f || deadlineDone || !RoomLive || !GameState.Alive
                || GameState.Phase != GameState.PhaseKind.Playing || RoomClock.Time < deadline) return;
            deadlineDone = true;
            GameState.TraceActive = false;
            TraceSystem.Sustain();
        }

        /// Live Modifiers: the room's loadout + its conditional effects that hold now, + the boss phase's melt (BossEvents).
        Modifiers LiveMods(in ConditionContext c)
        {
            var m = Conditions.Apply(built.mods, built.conditional, c);
            return bossArena != null ? BossEvents.Fold(m).Clamped() : m;
        }

        void OnBossMelt()
        {
            if (RoomLive && built != null) GameState.Mods = LiveMods(ctx);
        }

        void OnRoomReset() => deadlineDone = false;   // every attempt gets its own deadline

        /// ARCHITECT ending (Exposed / Merged) -> SaveV3.unlocks "ending_exposed" / "ending_merged" (story shards, true ending).
        void OnEnding(string ending) { if (RunUnlocks.RecordEnding(SaveService.Data, ending)) SaveService.Save(); }

        ConditionContext Context() => new ConditionContext
        {
            chain = GameState.Chain, melt = GameState.Melt, trace = GameState.TraceCount, node = node.index + 1,
            layer = LayerState.InSource ? Layer.Source : Layer.Render,
        };

        // ---------- start / end ----------

        void OnStartRun(RunMode mode) => Begin(mode, mode == RunMode.Daily ? Rng.DailySeed(DateTime.UtcNow) : NewSeed());

        void OnStartDeep(int level, IReadOnlyList<MutatorPick> picks) => BeginDeep(level, picks, NewSeed());

        static uint NewSeed() => unchecked((uint)DateTime.UtcNow.Ticks ^ (uint)(DateTime.UtcNow.Ticks >> 32));

        /// Starts a run on `seed` (a run in progress is abandoned first). CLASSIC hands over to the six-room flow.
        public void Begin(RunMode mode, uint seed) => Begin(mode, seed, 0, null);

        /// Starts a Deep Layer CAMPAIGN at `level` under `picks` (null = DeepRules.Preset(level)). Refused (false, nothing
        /// changes) when the level is not open to this profile or the picks do not add up to it legally (DeepRules.Check).
        public bool BeginDeep(int level, IReadOnlyList<MutatorPick> picks, uint seed)
        {
            LoadContent();
            var p = picks ?? DeepRules.Preset(level);
            var errors = DeepRules.Check(level, p, catalog, RunUnlocks.MaxDeepLayer(SaveService.Data));
            if (errors.Count > 0)
            {
                Debug.LogWarning("[DESYNC] Deep Layer " + level + " refused: " + string.Join(", ", errors));
                return false;
            }
            Begin(RunMode.Campaign, seed, level, p);
            return true;
        }

        void Begin(RunMode mode, uint seed, int level, IReadOnlyList<MutatorPick> picks)
        {
            Abandon();
            ResetRun();
            RunState.Mode = mode;
            if (mode == RunMode.Classic) { GameEvents.StartRequested?.Invoke(0); return; }
            LoadContent();
            if (level > 0)
            {
                DeepRun.Level = level;
                DeepRun.Picks.AddRange(DeepRules.Canonical(picks));
            }
            runPool = draftPool;
            if (DeepRun.Level > 0)
            {
                runPool = new Catalog { mutators = draftPool.mutators };
                foreach (var e in draftPool.exploits) if (!DeepRules.Excludes(DeepRun.Picks, catalog, e.id)) runPool.exploits.Add(e);
            }
            heatPlacement = DeepRules.Placement(DeepRun.Picks, catalog);
            RunState.Seed = seed;
            RunState.Graph = Generator.GenerateRun(seed, DeepRun.Level);
            RunState.Act = 1;
            RunState.Backups = RunRules.StartBackups;
            SaveService.Data.stats.runs++;
            SaveService.Save();
            StartCoroutine(EnsureStage());
            SetPhase(RunPhase.Map);
        }

        /// Ends a run in progress as a loss without the results screen (quit to title, starting something else).
        public void Abandon()
        {
            if (Active) EndRun(false, true, false);
            else if (RunState.Phase == RunPhase.Over) SetPhase(RunPhase.None);
        }

        void OnQuit() => Abandon();

        void OnClassicStart(int room) => Abandon();   // level select / play again mid-run

        bool FinalAct => RunState.Act >= (RunState.Mode == RunMode.Quick ? 1 : RunRules.CampaignActs);

        void EndRun(bool won, bool died, bool announce = true)
        {
            ClearRoomState();   // first: the room is detached with no run modifiers and no carried melt
            TearDownRoom();
            int bank = RunRules.Bank(RunState.Fragments + DeepRules.Bonus(RunState.Fragments, DeepRun.Level), died);
            RunState.Fragments = bank;
            RunState.Won = won;
            var save = SaveService.Data;
            save.fragments += bank;
            if (won) save.stats.wins++;
            if (won && DeepRun.Level > 0) RunUnlocks.RecordDeepWin(save, DeepRun.Level);
            SaveService.Save();
            Cursor.lockState = CursorLockMode.None;
            SetPhase(announce ? RunPhase.Over : RunPhase.None);
            if (announce) RunEvents.RunEnded?.Invoke();
        }

        void ResetRun()
        {
            token++;
            StopAllCoroutines();
            staging = false;
            RunState.Mode = RunMode.Classic;
            RunState.Phase = RunPhase.None;
            RunState.Seed = 0;
            RunState.Graph = null;
            RunState.NodeId = null;
            RunState.Act = 1;
            RunState.Held.Clear();
            RunState.RoomMutators.Clear();
            RunState.Offer.Clear();
            RunState.Market.Clear();
            RunState.Backups = RunState.Cycles = RunState.Rerolls = RunState.Fragments = 0;
            RunState.MeltCarry = RunState.RunTime = 0f;
            RunState.RunScore = 0;
            RunState.Grades.Clear();
            RunState.Won = false;
            lost = draftRarePlus = nextAct = backupSold = false;
            PerkBinder.DeadManUsed = false;   // Dead Man's Switch re-arms for a fresh run
            bestChain = 0;
            node = null;
            spec = null;
            bossArena = null;
            CurrentBoss = null;
            DeepRun.Level = 0;
            DeepRun.Picks.Clear();
            heatPlacement = default;
            deadline = -1f;
            grazeSurge = ghostCacheHits = 0;
            ClearRoomState();
        }

        /// Nothing of a run leaks into rooms played outside one.
        void ClearRoomState()
        {
            built = null;
            GameState.Mods = Modifiers.Default;
            GameState.Flags.Clear();
            RunPerks.Clear();
            RoomManager.StartMelt = 0f;
        }

        void LoadContent()
        {
            if (catalog != null) return;
            catalog = Catalog.FromJson(Resources.Load<TextAsset>("Content/exploits").text, Resources.Load<TextAsset>("Content/mutators").text);
            draftPool = catalog;   // M3: draft and market draw from all 48 exploits (+ duos via Loadout rules), not just the M2 pool
            var docs = Resources.LoadAll<TextAsset>("Content/chunks");
            Array.Sort(docs, (a, b) => string.CompareOrdinal(a.name, b.name));   // the same order as ChunkLibrary.FromFolder
            chunks = new ChunkLibrary();
            foreach (var d in docs) chunks.chunks.AddRange(ChunkLibrary.FromJson(d.text).chunks);
        }

        void SetPhase(RunPhase p)
        {
            RunState.Phase = p;
            if (p != RunPhase.Room && p != RunPhase.Results) Cursor.lockState = CursorLockMode.None;   // menus need the pointer
            SyncBackdrop();
            RunEvents.PhaseChanged?.Invoke(p);
        }

        // ---------- map ----------

        /// The node `id` if it is a legal next step: from the current node's next list, or the act's first row at an act start.
        public static RunNode Choice(string id)
        {
            if (RunState.Graph == null || id == null) return null;
            var nodes = RunState.Graph.acts[RunState.Act - 1];
            RunNode cur = null;
            if (RunState.NodeId != null) foreach (var n in nodes) if (n.id == RunState.NodeId) cur = n;
            foreach (var n in nodes)
                if (n.id == id && (cur == null ? n.index == 0 : cur.next.Contains(id))) return n;
            return null;
        }

        void OnChooseNode(string id)
        {
            if (RunState.Phase != RunPhase.Map) return;
            var n = Choice(id);
            if (n == null) return;
            node = n;
            RunState.NodeId = n.id;
            RunState.RoomMutators.Clear();
            switch (n.type)
            {
                case NodeType.Market: OpenMarket(); break;
                case NodeType.Cooldown: SetPhase(RunPhase.Cooldown); break;
                default:   // Infil, Elite, Anomaly (an Infil room until M3), Boss
                    Cursor.lockState = CursorLockMode.Locked;   // inside the click / key handler that chose the node
                    SetPhase(RunPhase.Room);
                    StartCoroutine(EnterRoom(token));
                    break;
            }
        }

        // ---------- rooms ----------

        IEnumerator EnsureStage()
        {
            while (staging) yield return null;
            if (SceneManager.GetActiveScene().name == StageScene) yield break;
            var s = SceneManager.GetSceneByName(StageScene);
            if (s.IsValid() && s.isLoaded) { SceneManager.SetActiveScene(s); yield break; }
            staging = true;
            if (Application.CanStreamedLevelBeLoaded(StageScene)) yield return SceneManager.LoadSceneAsync(StageScene);
            else
            {
                SceneManager.SetActiveScene(SceneManager.CreateScene(StageScene));   // not in Build Settings (tests, early builds)
                for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
                {
                    var o = SceneManager.GetSceneAt(i);
                    if (o.name.StartsWith("Room_", StringComparison.Ordinal)) SceneManager.UnloadSceneAsync(o);   // a fallback room
                }
            }
            staging = false;
        }

        IEnumerator EnterRoom(int run)
        {
            yield return EnsureStage();
            if (run != token) yield break;
            PrepareRoom();
            RoomLive = true;
            if (spec.fallbackRoom != null) { SceneManager.LoadScene(spec.fallbackRoom); yield break; }   // RoomManager activates on load
            roomRoot = new GameObject("BreachRoom");   // in the stage scene; room space = world space
            BuildRoom(spec, roomRoot.transform, h => OnBuilt(h, run));
        }

        /// Mutators, room spec, Modifiers + flags (D-013), player perks and the starting melt for the chosen node.
        void PrepareRoom()
        {
            RunState.RoomMutators.AddRange(RunRules.RoomMutators(Rng.Substream(node.roomSeed, "mutators"), catalog, node));
            bool mirror = RunState.RoomMutators.Exists(m => m.id == "mirror");
            bossArena = node.type == NodeType.Boss ? RunBosses.For(node) : null;
            spec = bossArena != null ? bossArena.spec : RunRules.Room(node, mirror, chunks, DeepRun.Level, heatPlacement);
            // held exploits + room mutators + Deep Layer picks; flags gain "mut:<id>" and the DL >= 6 boss heat flag
            built = DeepRules.Build(RunState.Held, RunState.RoomMutators, DeepRun.Level, DeepRun.Picks, catalog, RunState.Mode == RunMode.Daily ? "daily" : null);
            built.mods.GrazeCapBonus += grazeSurge;   // Redline Surge, earned in the previous room
            grazeSurge = 0;
            GameState.Flags.Clear();
            foreach (var f in built.flags) GameState.Flags.Add(f);
            if (RunRules.AuditorDrones(node, spec)) GameState.Flags.Add(RunRules.AuditorDronesFlag);
            PerkBinder.Bind(RunState.Held, built.flags);
            RoomManager.StartMelt = built.flags.Contains("flag:cold_boot") ? 0f : RunState.MeltCarry;
            GameState.Seed = spec.seed;
            ctx = new ConditionContext { node = node.index + 1, melt = RoomManager.StartMelt };
            GameState.Mods = LiveMods(ctx);
        }

        void OnBuilt(IDisposable handle, int run)
        {
            if (run != token || !roomRoot) { handle?.Dispose(); return; }   // the run ended while the room was building
            roomHandle = handle;
            roomHost = handle is Component c && c ? c.transform : roomRoot.transform;
            SpawnDeadZones();
            EnemyFlags.Refresh();   // this room's flags, with its geometry in place, before any factory below reads them
            if (!RoomInfo.Current) Debug.LogError("[DESYNC] the built room has no RoomInfo");
            else if (bossArena != null)
            {
                RunBosses.Factories[bossArena.boss](roomHost, roomHost.TransformPoint(new Vector3(bossArena.at.x, 0f, bossArena.at.y)));
                CurrentBoss = bossArena.boss;
                if (bossArena.terminals > 0) RoomInfo.Current.terminalCount = bossArena.terminals;   // the arena's hacks open the exit
            }
            RoomManager.I.Activate();
        }

        /// RoomManager: the run room's id, medal time and par score. Generated rooms: tDev = par seconds / 1.6 (silver = par).
        /// OVERCLOCK tightens the medal times (gold included) by its value (15 %), Tight Deadline by its own (10/20/30 %);
        /// under Tight Deadline the room also gains a TRACE when its clock passes the tightened par (silver time).
        internal void Targets(RoomInfo info, out string id, out float tDev, out int parScore)
        {
            id = RunState.NodeId;
            if (spec == null || spec.fallbackRoom != null || spec.par <= 0f) RoomReports.RoomTargets(info.index, info.terminalCount, out tDev, out parScore);
            else
            {
                tDev = spec.par / 1.6f;
                parScore = RoomReports.FallbackPar(info.terminalCount);
            }
            float tight = DeepRules.Value(DeepRun.Picks, catalog, "flag:tight_deadline");
            tDev *= (1f - MutatorValue("flag:overclock")) * (1f - tight);
            deadline = tight > 0f && tDev > 0f ? Medals.Threshold(Medal.Silver, tDev) : -1f;
            deadlineDone = false;
        }

        /// The data value of a room mutator's flag effect (0 when no room mutator carries it).
        static float MutatorValue(string flag)
        {
            foreach (var m in RunState.RoomMutators)
                if (m.effects.Count > 0)
                    foreach (var f in m.effects[0])
                        if (f.stat == flag) return f.value;
            return 0f;
        }

        /// RoomManager, at the death recap: spend a Backup (the recap ends in a respawn) or mark the run lost.
        internal void OnDeath()
        {
            RunState.RunTime += RoomClock.Time;
            if (RunState.Backups > 0) RunState.Backups--;
            else lost = true;
        }

        internal void EndAfterDeath() => EndRun(false, true);

        /// RoomManager, as the room's results are built: banks the clear into the run; returns the run's share text.
        internal string RoomCleared(RoomReport r, int chain)
        {
            RunState.Grades.Add(r.grade);
            RunState.RunScore += r.total;
            RunState.RunTime += r.time;
            RunState.Fragments += RunRules.RoomFragments(r.medal, r.grade);
            RunState.Cycles += RunRules.RoomCycles(r.grade, node.type == NodeType.Elite);
            RunState.MeltCarry = GameState.Melt * DeepRules.CarryShare(DeepRun.Picks, catalog, RunRules.MeltCarryShare);   // Persistent Melt: 100 %
            bestChain = Math.Max(bestChain, chain);
            if (PerkBinder.GhostCache && chain >= 10 && RunRules.GhostCacheReroll(++ghostCacheHits, DeepRules.Has(DeepRun.Picks, "fewer_choices")))
                RunState.Rerolls++;   // Ghost Cache: exit a room with a chain >= 10
            grazeSurge = GameState.Flags.Contains("flag:redline_surge") && ScoreKeeper.I ? ScoreKeeper.I.Redlines : 0;
            if (node.type == NodeType.Boss && RunUnlocks.RecordKill(SaveService.Data, node.act)) SaveService.Save();   // first kill: act / Deep Layer unlocks
            SetPhase(RunPhase.Results);
            var g = new StringBuilder(RunState.Grades.Count * 2);
            foreach (var c in RunState.Grades) { if (g.Length > 0) g.Append(' '); g.Append(c); }
            return RoomReports.ShareText(DateTime.UtcNow, g.ToString(), RunState.RunTime, bestChain);
        }

        /// RoomManager, ContinueRequested on the room results.
        internal void ContinueFromResults()
        {
            if (RunState.Phase != RunPhase.Results) return;
            if (node.type != NodeType.Boss) { TearDownRoom(); StartCoroutine(EnsureStage()); OpenDraft(node.type == NodeType.Elite); }
            else if (FinalAct) EndRun(true, false);
            else Deeper();
        }

        void OnCashOut()
        {
            if (RunState.Phase == RunPhase.Results && node.type == NodeType.Boss) EndRun(FinalAct, false);
        }

        void OnGoDeeper()
        {
            if (RunState.Phase == RunPhase.Results && node.type == NodeType.Boss && !FinalAct) Deeper();
        }

        /// Next act: the boss's draft (Rare+), then the new act's first row.
        void Deeper()
        {
            TearDownRoom();
            StartCoroutine(EnsureStage());   // a fallback room's scene goes: the menus sit on the empty stage
            nextAct = true;
            OpenDraft(true);
        }

        void TearDownRoom()
        {
            RoomLive = false;
            if (PlayerRef.Cam && PlayerRef.Cam.TryGetComponent(out AudioListener l)) l.enabled = false;   // the backdrop's listener takes over
            if (RoomManager.I) RoomManager.I.Detach();
            RoomInfo.Current = null;
            roomHandle?.Dispose();
            roomHandle = null;
            if (roomRoot)
                foreach (var h in roomRoot.GetComponentsInChildren<RoomHandle>(true)) h.Dispose();   // still building: free what it made
            if (roomRoot) Destroy(roomRoot);
            roomRoot = null;
            roomHost = null;
            foreach (var m in zoneMeshes) if (m) Destroy(m);
            zoneMeshes.Clear();
            RunPerks.DeadZones.Clear();
            CurrentBoss = null;
            BossEvents.Reset();   // a boss's melt / desync split / Current must not outlive its room (Destroy lands at frame end)
        }

        /// DEAD_ZONES: 1..N striped floor strips (N = the mutator's value) where SPACE does nothing.
        void SpawnDeadZones()
        {
            int max = (int)MutatorValue("flag:dead_zones");
            if (max <= 0 || spec.tiles == null) return;
            var mat = AssetLib.Get<Material>("mat.glitch");
            foreach (var s in RunRules.DeadZoneStrips(spec, Rng.Substream(spec.seed, "deadzones"), max))
            {
                RunRules.StripMetres(s, out float x0, out float x1, out float z0, out float z1);
                var b = new Bounds();
                b.SetMinMax(roomHost.TransformPoint(new Vector3(x0, -1f, z0)), roomHost.TransformPoint(new Vector3(x1, 4f, z1)));
                RunPerks.DeadZones.Add(b);
                var go = new GameObject("DeadZone");
                go.transform.SetParent(roomHost, false);
                var mesh = Stripes(x0, x1, z0, z1);
                zoneMeshes.Add(mesh);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        /// Diagonal hazard stripes (0.25 m bars, 0.25 m gaps) across a floor rectangle, just above the floor.
        static Mesh Stripes(float x0, float x1, float z0, float z1)
        {
            const float Bar = 0.25f, Y = 0.03f;
            bool alongX = x1 - x0 >= z1 - z0;
            float len = alongX ? x1 - x0 : z1 - z0, width = alongX ? z1 - z0 : x1 - x0;
            int bars = Mathf.Max(1, Mathf.FloorToInt(len / (2f * Bar)));
            var v = new Vector3[bars * 4];
            var tri = new int[bars * 6];
            Vector3 P(float a, float w) => alongX ? new Vector3(x0 + a, Y, z0 + w) : new Vector3(x0 + w, Y, z0 + a);
            for (int i = 0; i < bars; i++)
            {
                float a = i * 2f * Bar, shear = Mathf.Min(Bar, len - a - Bar);   // slant, kept inside the strip
                v[i * 4] = P(a, 0f);
                v[i * 4 + 1] = P(a + shear, width);
                v[i * 4 + 2] = P(a + shear + Bar, width);
                v[i * 4 + 3] = P(a + Bar, 0f);
                int k = i * 6, q = i * 4;
                tri[k] = q; tri[k + 1] = q + 1; tri[k + 2] = q + 2; tri[k + 3] = q; tri[k + 4] = q + 2; tri[k + 5] = q + 3;
            }
            if (!alongX) Array.Reverse(tri);   // the swapped axes flip the winding: keep the faces pointing up
            var white = new Color32[v.Length];
            for (int i = 0; i < white.Length; i++) white[i] = new Color32(255, 255, 255, 255);   // the FX shaders tint by vertex colour
            var mesh = new Mesh { name = "DeadZoneStripes", vertices = v, colors32 = white };
            mesh.SetTriangles(tri, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ---------- draft ----------

        void OpenDraft(bool rarePlus)
        {
            draftRng = Rng.Substream(node.roomSeed, "draft");
            draftRarePlus = rarePlus;
            RollOffer();
            if (RunState.Offer.Count == 0) { AfterDraft(); return; }   // five held: nothing to offer
            SetPhase(RunPhase.Draft);
        }

        void RollOffer()
        {
            RunState.Offer.Clear();
            RunState.Offer.AddRange(Loadout.RollOffer(draftRng, runPool, RunState.Held, draftRarePlus, DeepRules.Has(DeepRun.Picks, "fewer_choices")));
        }

        void OnPickExploit(int i)
        {
            if (RunState.Phase != RunPhase.Draft) return;
            if (i != -1)
            {
                if (i < 0 || i >= RunState.Offer.Count || !Loadout.CanHold(RunState.Held, RunState.Offer[i])) return;
                RunState.Held.Add(RunState.Offer[i]);
            }
            RunState.Offer.Clear();
            AfterDraft();
        }

        void OnRerollOffer()
        {
            if (RunState.Phase != RunPhase.Draft || RunState.Rerolls <= 0) return;
            RunState.Rerolls--;
            RollOffer();
            RunEvents.PhaseChanged?.Invoke(RunPhase.Draft);   // same phase, new cards
        }

        void AfterDraft()
        {
            if (nextAct)
            {
                nextAct = false;
                RunState.Act++;
                RunState.NodeId = null;   // choose from the new act's first row
                backupSold = false;
            }
            SetPhase(RunPhase.Map);
        }

        // ---------- market / cooldown ----------

        void OpenMarket()
        {
            RunState.Market.Clear();
            RunState.Market.AddRange(RunRules.MarketStock(Rng.Substream(node.roomSeed, "market"), runPool, RunState.Held,
                                                          !NoBackups && !backupSold && RunState.Backups < RunRules.MaxBackups));
            for (int i = 0; i < RunState.Market.Count; i++)   // Poor Market: +50 %
            {
                var it = RunState.Market[i];
                it.price = DeepRules.Price(it.price, DeepRun.Picks, catalog);
                RunState.Market[i] = it;
            }
            SetPhase(RunPhase.Market);
        }

        /// Exploit, Backup (one per act, max held 2) or reroll token. A removal is bought with RemoveExploit(id).
        void OnBuy(int i)
        {
            if (RunState.Phase != RunPhase.Market || i < 0 || i >= RunState.Market.Count) return;
            var it = RunState.Market[i];
            if (it.sold || it.price > RunState.Cycles) return;
            switch (it.kind)
            {
                case "exploit":
                    var e = catalog.Exploit(it.exploitId);
                    if (e == null || !Loadout.CanHold(RunState.Held, e)) return;
                    RunState.Held.Add(e);
                    break;
                case "backup":
                    if (NoBackups || backupSold || RunState.Backups >= RunRules.MaxBackups) return;
                    RunState.Backups++;
                    backupSold = true;
                    break;
                case "reroll": RunState.Rerolls++; break;
                default: return;
            }
            Charge(i);
        }

        void OnRemoveExploit(string id)
        {
            if (RunState.Phase != RunPhase.Market) return;
            int i = RunState.Market.FindIndex(x => x.kind == "removal" && !x.sold);
            int h = RunState.Held.FindIndex(x => x.id == id);
            if (i < 0 || h < 0 || RunState.Market[i].price > RunState.Cycles) return;
            RunState.Held.RemoveAt(h);
            Charge(i);
        }

        void Charge(int i)
        {
            var it = RunState.Market[i];
            RunState.Cycles -= it.price;
            it.sold = true;
            RunState.Market[i] = it;
            RunEvents.PhaseChanged?.Invoke(RunPhase.Market);   // same phase, new stock state
        }

        void OnChooseCooldown(int i)
        {
            if (RunState.Phase != RunPhase.Cooldown) return;
            switch (i)
            {
                case 0: RunState.MeltCarry = 0f; break;
                case 1: if (NoBackups) return; RunState.Backups = Math.Min(RunRules.MaxBackups, RunState.Backups + 1); break;   // No Backups: not on offer
                case 2: RunState.Rerolls++; break;
                default: return;
            }
            SetPhase(RunPhase.Map);
        }

        void OnLeave()
        {
            if (RunState.Phase == RunPhase.Market || RunState.Phase == RunPhase.Cooldown) SetPhase(RunPhase.Map);
        }
    }
}
