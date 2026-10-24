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
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Desync.Flow
{
    /// The BREACH run: CAMPAIGN (acts 1-2), QUICK (act 1), DAILY (campaign on the UTC-day seed), CLASSIC (the six original
    /// rooms, unchanged flow). Run graph -> map choice -> node: room (built into the Breach scene) -> results -> exploit
    /// draft -> map; Black Market, Cooldown. Deaths spend a Backup or end the run; a boss offers cash-out or the next act.
    /// Writes RunState and raises RunEvents (RunContracts.cs); the UI reads and requests. Self-bootstraps.
    ///
    /// Phases: Map (choose a node) -> Room (playing, recap) -> Results (room results; on a boss: CashOut / GoDeeper, and
    /// ContinueRequested = GoDeeper, or the run's end after the final act) -> Draft (pick/skip/reroll) -> Map. Market and
    /// Cooldown return to Map with Leave (a Cooldown choice also returns). Over = run results (RunEnded); quitting -> None.
    [DefaultExecutionOrder(-110)]   // live Modifiers are written before the player, ScoreKeeper and RoomManager read them
    public class RunManager : MonoBehaviour
    {
        public static RunManager I;
        public const string StageScene = RoomBuilder.BreachScene;
        const int AuditorTerminals = 3;   // the arena's three terminals open the uplink (the exit)

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
        bool lost, draftRarePlus, nextAct, backupSold, staging, arena;
        int bestChain, token;

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
        }

        /// Conditional exploit effects (chain>=, melt>=, trace>=, layer=) re-folded whenever one of their inputs moved.
        void Update()
        {
            SyncBackdrop();
            if (!RoomLive || built == null || built.conditional.Count == 0) return;
            var c = Context();
            if (c.chain == ctx.chain && c.melt == ctx.melt && c.trace == ctx.trace && c.layer == ctx.layer) return;
            ctx = c;
            GameState.Mods = Conditions.Apply(built.mods, built.conditional, c);
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

        ConditionContext Context() => new ConditionContext
        {
            chain = GameState.Chain, melt = GameState.Melt, trace = GameState.TraceCount, node = node.index + 1,
            layer = LayerState.InSource ? Layer.Source : Layer.Render,
        };

        // ---------- start / end ----------

        void OnStartRun(RunMode mode) =>
            Begin(mode, mode == RunMode.Daily ? Rng.DailySeed(DateTime.UtcNow) : unchecked((uint)DateTime.UtcNow.Ticks ^ (uint)(DateTime.UtcNow.Ticks >> 32)));

        /// Starts a run on `seed` (a run in progress is abandoned first). CLASSIC hands over to the six-room flow.
        public void Begin(RunMode mode, uint seed)
        {
            Abandon();
            ResetRun();
            RunState.Mode = mode;
            if (mode == RunMode.Classic) { GameEvents.StartRequested?.Invoke(0); return; }
            LoadContent();
            RunState.Seed = seed;
            RunState.Graph = Generator.GenerateRun(seed, 0);
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
            int bank = RunRules.Bank(RunState.Fragments, died);
            RunState.Fragments = bank;
            RunState.Won = won;
            var save = SaveService.Data;
            save.fragments += bank;
            if (won) save.stats.wins++;
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
            bestChain = 0;
            node = null;
            spec = null;
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
            draftPool = RunRules.DraftPool(catalog);
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
            bool mirror = false;
            var picks = new List<MutatorPick>(RunState.RoomMutators.Count);
            foreach (var m in RunState.RoomMutators)
            {
                mirror |= m.id == "mirror";
                picks.Add(new MutatorPick(m.id, 1));
            }
            arena = false;
            spec = node.type == NodeType.Boss ? BossArena() : RunRules.Room(node, mirror, chunks);
            built = Loadout.Build(RunState.Held, picks, catalog, RunState.Mode == RunMode.Daily ? "daily" : null);
            GameState.Flags.Clear();
            foreach (var f in built.flags) GameState.Flags.Add(f);
            foreach (var m in RunState.RoomMutators) GameState.Flags.Add("mut:" + m.id);
            RunPerks.Clear();
            foreach (var e in RunState.Held)
                foreach (var f in e.effects)
                    switch (f.stat)
                    {
                        case "flag:heat_sink": RunPerks.HeatSinkTo = f.value; break;
                        case "flag:coolant_leak": RunPerks.CoolantLeak = f.value; break;
                        case "flag:heat_exchange": RunPerks.HeatExchange = f.value; break;
                        case "flag:packet_loss": RunPerks.PacketLoss = true; break;
                    }
            RoomManager.StartMelt = built.flags.Contains("flag:cold_boot") ? 0f : RunState.MeltCarry;
            GameState.Seed = spec.seed;
            ctx = new ConditionContext { node = node.index + 1, melt = RoomManager.StartMelt };
            GameState.Mods = Conditions.Apply(built.mods, built.conditional, ctx);
        }

        void OnBuilt(IDisposable handle, int run)
        {
            if (run != token || !roomRoot) { handle?.Dispose(); return; }   // the run ended while the room was building
            roomHandle = handle;
            roomHost = handle is Component c && c ? c.transform : roomRoot.transform;
            SpawnDeadZones();
            EnemyFlags.Refresh();   // this room's flags, with its geometry in place, before any factory below reads them
            if (!RoomInfo.Current) Debug.LogError("[DESYNC] the built room has no RoomInfo");
            else if (node.type == NodeType.Boss && arena)
            {
                EnemyFactory.Auditor(roomHost, roomHost.TransformPoint(new Vector3(Auditor.ArenaCenter.x, 0f, Auditor.ArenaCenter.y)));
                RoomInfo.Current.terminalCount = AuditorTerminals;
            }
            RoomManager.I.Activate();
        }

        /// RoomManager: the run room's id, medal time and par score. Generated rooms: tDev = par seconds / 1.6 (silver = par).
        /// OVERCLOCK tightens the medal times (gold included) by its value (15 %).
        internal void Targets(RoomInfo info, out string id, out float tDev, out int parScore)
        {
            id = RunState.NodeId;
            if (spec == null || spec.fallbackRoom != null || spec.par <= 0f) RoomReports.RoomTargets(info.index, info.terminalCount, out tDev, out parScore);
            else
            {
                tDev = spec.par / 1.6f;
                parScore = RoomReports.FallbackPar(info.terminalCount);
            }
            tDev *= 1f - MutatorValue("flag:overclock");
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
            RunState.MeltCarry = GameState.Melt * RunRules.MeltCarryShare;
            bestChain = Math.Max(bestChain, chain);
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
        }

        RoomSpec BossArena()
        {
            var ta = Resources.Load<TextAsset>("Content/bosses/auditor");
            if (ta)
            {
                try
                {
                    var s = JsonConvert.DeserializeObject<RoomSpec>(ta.text);
                    if (s?.tiles != null && s.tiles.Length > 0)
                    {
                        s.seed = node.roomSeed;
                        s.act = node.act;
                        s.node = node.index;
                        s.entities ??= new List<EntitySpawn>();
                        if (s.gridW <= 0) s.gridW = s.tiles[0].Length / 16;
                        if (s.gridH <= 0) s.gridH = s.tiles.Length / 16;
                        arena = true;
                        return s;
                    }
                }
                catch (Exception e) { Debug.LogWarning("[DESYNC] bosses/auditor.json unreadable: " + e.Message); }
            }
            Debug.LogWarning("[DESYNC] no AUDITOR arena data yet: the boss node plays a generated room");
            return Generator.GenerateRoom(node.roomSeed, node.act, node.index, 0, chunks);
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
            RunState.Offer.AddRange(Loadout.RollOffer(draftRng, draftPool, RunState.Held, draftRarePlus));
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
            RunState.Market.AddRange(RunRules.MarketStock(Rng.Substream(node.roomSeed, "market"), draftPool, RunState.Held,
                                                          !backupSold && RunState.Backups < RunRules.MaxBackups));
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
                    if (backupSold || RunState.Backups >= RunRules.MaxBackups) return;
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
                case 1: RunState.Backups = Math.Min(RunRules.MaxBackups, RunState.Backups + 1); break;
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
