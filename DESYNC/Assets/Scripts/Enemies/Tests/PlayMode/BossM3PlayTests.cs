using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Desync.Enemies.Tests
{
    /// M3 bosses: COMPILER wall swaps, compiled ICE, arena collapse and the layer exit; ARCHITECT gaze, strings, Desync and
    /// both endings; heat variants; no allocations per frame. Stand-in player; RoomClock is driven by the test.
    public class BossM3PlayTests
    {
        GameObject _world, _player;
        int _swaps, _windups, _exits, _alarms, _meltChanged, _severed;
        string _ending;
        DeathCause? _killed;

        [SetUp]
        public void SetUp()
        {
            Application.targetFrameRate = 60;
            _world = new GameObject("World");
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.SetParent(_world.transform);
            floor.transform.position = new Vector3(0f, -0.5f, 0f);
            floor.transform.localScale = new Vector3(80f, 1f, 80f);
            floor.layer = GameLayers.Floor;

            _player = new GameObject("FakePlayer");
            var head = new GameObject("Head").transform;
            head.SetParent(_player.transform, false);
            head.localPosition = new Vector3(0f, 1.6f, 0f);
            var chest = new GameObject("Chest").transform;
            chest.SetParent(_player.transform, false);
            chest.localPosition = new Vector3(0f, 1.2f, 0f);
            PlayerRef.Root = _player.transform;
            PlayerRef.Head = head;
            PlayerRef.Chest = chest;
            PlayerRef.LastKillerId = null;

            LayerState.Set(false);
            RoomClock.Time = 0f;
            GameState.Alive = true;
            GameState.DemoMode = GameState.Cinematic = GameState.InHack = GameState.TraceActive = false;
            GameState.TraceCount = 0;
            SetFlags();

            _swaps = _windups = _exits = _alarms = _meltChanged = _severed = 0;
            _ending = null;
            _killed = null;
            GameEvents.PlayerKilled += OnKilled;
            GameEvents.ExitReached += OnExit;
            GameEvents.IceWindup += OnWindup;
            GameEvents.Alarm += OnAlarm;
            BossEvents.WallSwap += OnSwap;
            BossEvents.MeltChanged += OnMelt;
            BossEvents.Ending += OnEnding;
            BossEvents.StringSevered += OnSevered;
        }

        [TearDown]
        public void TearDown()
        {
            GameEvents.PlayerKilled -= OnKilled;
            GameEvents.ExitReached -= OnExit;
            GameEvents.IceWindup -= OnWindup;
            GameEvents.Alarm -= OnAlarm;
            BossEvents.WallSwap -= OnSwap;
            BossEvents.MeltChanged -= OnMelt;
            BossEvents.Ending -= OnEnding;
            BossEvents.StringSevered -= OnSevered;
            Object.DestroyImmediate(_world);
            Object.DestroyImmediate(_player);
            if (TraceSystem.I) Object.DestroyImmediate(TraceSystem.I.gameObject);
            SetFlags();
            Breadcrumbs.Clear();
            LayerState.Set(false);
            GameState.Alive = true;
            GameState.TraceActive = false;
            GameState.TraceCount = 0;
            GameState.InHack = false;
        }

        void OnKilled(DeathCause c) => _killed ??= c;
        void OnExit() => _exits++;
        void OnWindup(Vector3 _) => _windups++;
        void OnAlarm() => _alarms++;
        void OnSwap() => _swaps++;
        void OnMelt() => _meltChanged++;
        void OnEnding(string e) => _ending = e;
        void OnSevered(int _) => _severed++;

        static void SetFlags(params string[] flags)
        {
            GameState.Flags.Clear();
            foreach (var f in flags) GameState.Flags.Add(f);
            EnemyFlags.Refresh();
        }

        /// Set the room clock and let one frame see it.
        static IEnumerator At(float t)
        {
            RoomClock.Time = t;
            yield return null;
        }

        void Reboot()
        {
            foreach (var r in _world.GetComponentsInChildren<IResettable>(true)) r.ResetState();
            RoomClock.Time = 0f;
            GameState.Alive = true;
            GameState.TraceCount = 0;
            GameState.TraceActive = false;
            LayerState.Set(false);
            _killed = null;
            PlayerRef.LastKillerId = null;
        }

        static void Hack(Terminal t)
        {
            t.Interact();
            for (int i = 0, n = t.SequenceLength; i < n; i++) t.Press(t.Expected(i));
            Assert.IsTrue(t.Done);
        }

        static BoxCollider[] Walls(Compiler c, int g) => c.wallGroups[g].GetComponentsInChildren<BoxCollider>(true);

        static void AssertGroup(Compiler c, int g, int layer, bool trigger, string msg)
        {
            foreach (var w in Walls(c, g))
            {
                Assert.AreEqual(layer, w.gameObject.layer, msg);
                Assert.AreEqual(trigger, w.isTrigger, msg);
            }
        }

        // ---------------------------------------------------------------------------------------------------- COMPILER

        [UnityTest]
        public IEnumerator CompilerWallsSwapLayersOnSchedule()
        {
            var c = EnemyFactory.Compiler(_world.transform, Vector3.zero).GetComponent<Compiler>();
            _player.transform.position = new Vector3(0f, 0f, -30f);
            yield return null;
            Assert.AreSame(c, Compiler.Current);
            AssertGroup(c, 0, GameLayers.WallRender, false, "N/S walls start solid in RENDER");
            AssertGroup(c, 1, GameLayers.Default, true, "E/W walls: solid in SOURCE, passable in RENDER");
            Assert.AreSame(c.renderSolidMat, c.wallRenderers[0].sharedMaterial);
            Assert.AreSame(c.ghostMat, c.wallRenderers[1].sharedMaterial);

            LayerState.Set(true);
            yield return null;
            AssertGroup(c, 1, GameLayers.Default, false, "solid while the player is in SOURCE");
            Assert.AreSame(c.sourceSolidMat, c.wallRenderers[1].sharedMaterial);

            yield return At(5.4f);
            Assert.AreEqual(0, _swaps);
            Assert.That(c.SwapIn, Is.InRange(0.01f, Compiler.SwapTelegraph), "telegraph second");
            yield return At(Compiler.SwapPeriod + 0.05f);
            Assert.AreEqual(1, _swaps, "swap every 6 s");
            AssertGroup(c, 0, GameLayers.Default, false, "N/S now solid in SOURCE");
            AssertGroup(c, 1, GameLayers.WallRender, false, "E/W now solid in RENDER");
            LayerState.Set(false);
            yield return null;
            AssertGroup(c, 0, GameLayers.Default, true, "and passable in RENDER");
            yield return At(2f * Compiler.SwapPeriod + 0.05f);
            Assert.AreEqual(2, _swaps);
            AssertGroup(c, 0, GameLayers.WallRender, false, "back");

            Reboot();
            yield return null;
            Assert.AreEqual(0, c.Swaps);
            AssertGroup(c, 0, GameLayers.WallRender, false, "REBOOT restores the start state");

            SetFlags("flag:boss_overclock");
            _swaps = 0;
            yield return At(Compiler.HeatSwapPeriod - 0.1f);
            Assert.AreEqual(0, _swaps);
            yield return At(Compiler.HeatSwapPeriod + 0.05f);
            Assert.AreEqual(1, _swaps, "heat: every 4 s");
            AssertGroup(c, 0, GameLayers.Default, true, "heat swap applied");
        }

        [UnityTest]
        public IEnumerator CompilerPhasesCompileIceFromGrazes()
        {
            var c = EnemyFactory.Compiler(_world.transform, Vector3.zero).GetComponent<Compiler>();
            _player.transform.position = new Vector3(-10f, 0f, -30f);
            yield return null;
            Assert.AreEqual(1, c.Phase);
            GameEvents.Grazed?.Invoke("ICE-90");
            Assert.AreEqual(0, _windups, "P1 grazes compile nothing");

            GameEvents.AccessGranted?.Invoke("N1");
            Assert.AreEqual(1, c.Phase);
            GameEvents.AccessGranted?.Invoke("N2");
            Assert.AreEqual(2, c.Phase, "<= 2 nodes left");
            Assert.AreEqual(Compiler.P2Melt, BossEvents.MeltSource, "SOURCE melt +2 %/s");
            Assert.AreEqual(0f, BossEvents.MeltRender);
            Assert.AreEqual(1, _meltChanged);

            LayerState.Set(true);
            yield return At(1f);
            GameEvents.Grazed?.Invoke("ICE-90");
            GameEvents.Grazed?.Invoke("ICE-90");
            Assert.AreEqual(1, _windups, "one compile at a time");
            yield return At(1f + Compiler.CompileWindup + 0.05f);
            Assert.AreEqual(1, CountActive(c.compiled), "a GRAZE compiled a Splitter");
            yield return null;
            Assert.IsTrue(c.compiled[0].hunter.gameObject.activeSelf, "out at its post");
            for (int i = 0; i < 5; i++)
            {
                GameEvents.Grazed?.Invoke("ICE-91");
                yield return At(RoomClock.Time + Compiler.CompileWindup + 0.05f);
            }
            Assert.AreEqual(Compiler.CompiledMax, CountActive(c.compiled), "pool cap");

            GameEvents.AccessGranted?.Invoke("N3");
            GameEvents.AccessGranted?.Invoke("N4");
            Assert.AreEqual(3, c.Phase, "all nodes");
            Assert.IsTrue(c.exit.gameObject.activeSelf);
            Assert.Less(Vector3.Distance(c.exit.position, new Vector3(Compiler.ExitSpots[0].x, 0f, Compiler.ExitSpots[0].y)), 1e-3f);
            Assert.AreEqual(Compiler.P2Melt, BossEvents.MeltSource, "P3 keeps the melt");

            Reboot();
            Assert.AreEqual(1, c.Phase);
            Assert.AreEqual(0f, BossEvents.MeltSource);
            Assert.AreEqual(0, CountActive(c.compiled));
            Assert.IsFalse(c.exit.gameObject.activeSelf);
        }

        static int CountActive(SplitterIce[] ss)
        {
            int n = 0;
            foreach (var s in ss) if (s.gameObject.activeSelf) n++;
            return n;
        }

        [UnityTest]
        public IEnumerator CompilerArenaHalvesThenFlipIntoTheExit()
        {
            var c = EnemyFactory.Compiler(_world.transform, Vector3.zero).GetComponent<Compiler>();
            _player.transform.position = new Vector3(-6f, 0f, -10f);   // south-west: gone at the first halving
            yield return null;
            GameEvents.ExitOpened?.Invoke();
            Assert.AreEqual(3, c.Phase);
            yield return At(Compiler.HalvePeriod - 0.5f);
            Assert.IsNull(_killed);
            Assert.That(c.HalveIn, Is.InRange(0.01f, Compiler.HalveTelegraph));
            GameEvents.Flipped?.Invoke(true);   // not in the exit: nothing
            Assert.AreEqual(0, _exits);
            yield return At(Compiler.HalvePeriod + 0.05f);
            Assert.AreEqual(1, c.Step);
            Assert.AreEqual(DeathCause.Glitch, _killed, "outside the live half");
            Assert.AreEqual(Compiler.KillerId, PlayerRef.LastKillerId);
            Assert.IsTrue(c.voids[0].activeSelf);

            Reboot();
            yield return null;
            Assert.AreEqual(0, c.Step);
            Assert.IsFalse(c.voids[0].activeSelf);
            _player.transform.position = new Vector3(3f, 0f, 9f);   // inside every region
            GameEvents.ExitOpened?.Invoke();
            for (int k = 1; k < Compiler.Regions.Length; k++)
            {
                yield return At(k * Compiler.HalvePeriod + 0.05f);
                Assert.AreEqual(k, c.Step);
                Assert.IsNull(_killed, "inside region " + k);
                Assert.Less(Vector3.Distance(c.exit.position, new Vector3(Compiler.ExitSpots[k].x, 0f, Compiler.ExitSpots[k].y)), 1e-3f,
                            "the exit moves into the live area");
            }
            _player.transform.position = c.exit.position + new Vector3(0.5f, 0f, 0.3f);
            yield return null;
            GameEvents.Flipped?.Invoke(false);
            Assert.AreEqual(1, _exits, "flipped into the exit");
            Assert.AreEqual(4, c.Phase);
            Assert.AreEqual(0f, BossEvents.MeltSource);
            yield return At(10f * Compiler.HalvePeriod);
            Assert.IsNull(_killed, "no collapse after the exit");

            Reboot();
            GameEvents.ExitOpened?.Invoke();
            yield return At(Compiler.Regions.Length * Compiler.HalvePeriod + 0.05f);
            Assert.AreEqual(DeathCause.Glitch, _killed, "nothing is left after the last region");
        }

        // ---------------------------------------------------------------------------------------------------- ARCHITECT

        Terminal[] _terms;

        ArchitectBoss BuildArchitect()
        {
            _terms = new Terminal[ArchitectBoss.Terminals];
            for (int i = 0; i < _terms.Length; i++)
                _terms[i] = EnemyFactory.Terminal(_world.transform, new Vector3(-6f + 6f * i, 0f, 4.75f), 180f, 5, i).GetComponent<Terminal>();
            return EnemyFactory.ArchitectBoss(_world.transform, Vector3.zero).GetComponent<ArchitectBoss>();
        }

        /// Stand `dist` m out along the gaze's current direction.
        void InGaze(ArchitectBoss a, float dist)
        {
            Vector3 dir = Quaternion.Euler(0f, 180f + a.GazeYaw, 0f) * Vector3.forward;
            Vector3 eye = a.eye.position;
            _player.transform.position = new Vector3(eye.x, 0f, eye.z) + dir * dist;
        }

        IEnumerator UnderGaze(ArchitectBoss a, float seconds, Func<bool> until = null)
        {
            for (float t = 0f; t < seconds && (until == null || !until()); t += 0.05f)
            {
                InGaze(a, 8f);
                RoomClock.Time += 0.05f;
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator ArchitectGazeKillsInSourceAndTracesInRender()
        {
            var a = BuildArchitect();
            _player.transform.position = new Vector3(0f, 0f, 10f);   // behind the face
            yield return null;
            Assert.AreSame(a, ArchitectBoss.Current);
            Assert.AreEqual(ArchitectBoss.Terminals + 1, a.strings.Length);

            yield return UnderGaze(a, 1.2f, () => GameState.TraceCount >= 2);
            Assert.AreEqual(2, GameState.TraceCount, "RENDER: +1 TRACE per 0.5 s under the gaze");
            Assert.IsNull(_killed);
            Assert.AreEqual(0, _alarms);
            yield return UnderGaze(a, 1f, () => _alarms > 0);
            Assert.AreEqual(1, _alarms, "the third step is the alarm");

            Reboot();
            LayerState.Set(true);
            _player.transform.position = new Vector3(0f, 0f, 10f);
            yield return null;
            Assert.IsNull(_killed, "out of sight behind it");
            yield return UnderGaze(a, 1f, () => _killed != null);
            Assert.AreEqual(DeathCause.Architect, _killed, "SOURCE: the gaze kills");
            Assert.AreEqual(ArchitectBoss.KillerId, PlayerRef.LastKillerId);
        }

        [UnityTest]
        public IEnumerator ArchitectStringsPhasesAndExposedEnding()
        {
            var a = BuildArchitect();
            _player.transform.position = new Vector3(0f, 0f, 10f);
            LayerState.Set(true);
            yield return null;
            foreach (var s in a.strings) Assert.IsNotNull(s);
            for (int i = 0; i < ArchitectBoss.Terminals; i++) Assert.IsTrue(a.strings[i].enabled, "strings show in SOURCE");
            Assert.IsFalse(a.strings[ArchitectBoss.Terminals].enabled, "no final string yet");
            LayerState.Set(false);
            Assert.IsFalse(a.strings[0].enabled, "hidden in RENDER");

            Hack(_terms[1]);
            Assert.AreEqual(2, a.Phase, "first string severed");
            Assert.AreEqual(1, _severed);
            foreach (var d in a.daemons) Assert.IsTrue(d.gameObject.activeSelf, "the Daemons take their posts");
            LayerState.Set(true);
            Assert.IsFalse(a.strings[1].enabled, "severed");
            Assert.IsTrue(a.strings[0].enabled);
            LayerState.Set(false);
            _player.transform.position = new Vector3(0f, 0f, 10f);
            yield return At(1f);

            Hack(_terms[0]);
            Hack(_terms[2]);
            Assert.AreEqual(3, a.Phase, "all strings");
            Assert.AreEqual(3, _severed);
            Assert.AreEqual(ArchitectBoss.MeltBonus, BossEvents.MeltSource);
            Assert.AreEqual(ArchitectBoss.MeltBonus, BossEvents.MeltRender);
            Assert.AreEqual(1f, Shader.GetGlobalFloat("_Desync"), "both layers on screen");
            Assert.AreEqual(ArchitectBoss.SurviveTime, a.SurviveRemaining);

            float p3 = RoomClock.Time;
            yield return At(p3 + 10f);
            Assert.AreEqual(ArchitectBoss.SurviveTime - 10f, a.SurviveRemaining, 0.05f);
            GameEvents.Flipped?.Invoke(true);   // before the final window: nothing
            Assert.IsNull(_ending);
            yield return At(p3 + ArchitectBoss.SurviveTime + 0.01f);
            Assert.AreEqual(0f, a.SurviveRemaining);
            Assert.AreEqual(ArchitectBoss.FinalTime, a.FinalRemaining, 0.05f);
            Assert.IsTrue(a.strings[ArchitectBoss.Terminals].enabled, "the final string ties you to it");
            float f0 = RoomClock.Time;
            yield return At(f0 + 1f);
            Assert.IsFalse(a.GapOpen);
            Assert.AreEqual(ArchitectBoss.GapLead - 1f, a.GapIn, 0.05f);
            yield return At(f0 + ArchitectBoss.GapLead + 0.1f);
            Assert.IsTrue(a.GapOpen, "the gaze shuts");
            Assert.IsFalse(a.gazeCone.enabled);
            GameEvents.Flipped?.Invoke(true);
            Assert.AreEqual(BossEvents.Exposed, _ending);
            Assert.AreEqual(4, _severed, "the final string");
            Assert.AreEqual(1, _exits, "the room's results follow");
            Assert.AreEqual(4, a.Phase);
            Assert.AreEqual(0f, BossEvents.MeltSource);
            Assert.AreEqual(0f, Shader.GetGlobalFloat("_Desync"));
        }

        [UnityTest]
        public IEnumerator ArchitectMergedEndingAndHeatGaps()
        {
            var a = BuildArchitect();
            _player.transform.position = new Vector3(0f, 0f, 10f);
            yield return null;
            GameEvents.ExitOpened?.Invoke();   // debug win: straight to P3
            Assert.AreEqual(3, a.Phase);
            yield return At(ArchitectBoss.SurviveTime + 0.01f);
            float f0 = RoomClock.Time;
            yield return At(f0 + 1f);
            int traces = GameState.TraceCount;
            GameEvents.Flipped?.Invoke(true);   // outside a gap
            Assert.IsNull(_ending, "a missed gap is not an ending");
            Assert.AreEqual(traces + 1, GameState.TraceCount, "a missed flip is +1 TRACE");
            yield return At(f0 + ArchitectBoss.FinalTime - 0.1f);
            Assert.IsNull(_ending);
            yield return At(f0 + ArchitectBoss.FinalTime + 0.05f);
            Assert.AreEqual(BossEvents.Merged, _ending, "refused the final flip until the timer ran out");
            Assert.AreEqual(1, _exits);

            Reboot();
            _ending = null;
            yield return null;
            Assert.AreEqual(1, a.Phase);
            Assert.AreEqual(-1f, a.FinalRemaining);
            SetFlags("flag:boss_overclock");
            GameEvents.ExitOpened?.Invoke();
            yield return At(ArchitectBoss.SurviveTime + 0.01f);
            f0 = RoomClock.Time;
            yield return At(f0 + ArchitectBoss.GapLead + 0.35f);
            Assert.IsFalse(a.GapOpen, "heat: shorter gaps (0.3 s)");
            yield return At(f0 + ArchitectBoss.GapLead + ArchitectBoss.GapPeriod + 0.2f);
            Assert.IsTrue(a.GapOpen);
        }

        // ---------------------------------------------------------------------------------------------------- budgets

        /// Both bosses mid-fight (COMPILER P3, ARCHITECT P2 with scrambled screens): a burst of 120 Update calls each, plus
        /// 120 LateUpdate passes over the scrambled screens, in one frame allocates no more than an idle frame.
        /// GC.GetAllocatedBytesForCurrentThread reads 0 under Unity's Mono, so the profiler's per-frame GC counter measures it.
        /// The LateUpdate passes run on a still clock: a redraw goes through TMP_Text.SetCharArray, which builds a debug string
        /// in the editor only (#if UNITY_EDITOR), as every terminal's own scrambled screen does; not a player-build cost.
        [UnityTest]
        public IEnumerator BossesDoNotAllocatePerFrame()
        {
            var c = EnemyFactory.Compiler(_world.transform, new Vector3(-40f, 0f, 0f)).GetComponent<Compiler>();
            var a = BuildArchitect();
            _player.transform.position = new Vector3(0f, 0f, 10f);
            LayerState.Set(true);
            yield return null;
            Hack(_terms[0]);
            Bind(c, "OnExitOpened")();   // the COMPILER only: the ARCHITECT stays in P2 (scrambled screens)
            Assert.AreEqual(3, c.Phase);
            Assert.AreEqual(2, a.Phase);
            Action cu = Bind(c, "Update"), au = Bind(a, "Update"), al = Bind(a, "LateUpdate");
            void Burst(int n)
            {
                for (int i = 0; i < n; i++)
                {
                    RoomClock.Time += 1f / 60f;
                    cu();
                    au();
                }
                al();   // draws once for the new tick, then the key check alone
                for (int i = 0; i < n; i++) al();
            }
            Burst(30);   // warm-up: first calls, lazy arrays
            var quiet = new System.Collections.Generic.List<MonoBehaviour>();   // the self-booting managers (UI, run, audio) are not ours
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude))
            {
                string ns = mb.GetType().Namespace ?? "";
                if (ns.StartsWith("Desync") && !ns.StartsWith("Desync.Enemies") && mb.enabled) { mb.enabled = false; quiet.Add(mb); }
            }
            try
            {
                using (var rec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame"))
                {
                    // Idle frames (bosses running once per frame) carry ~40-60 KB of test-harness noise; the recorder's first
                    // frame spikes, so it is skipped.
                    long idle = 0;
                    for (int i = 0; i < 22; i++)
                    {
                        RoomClock.Time += 1f / 60f;
                        yield return null;
                        if (i >= 2) idle = Math.Max(idle, rec.LastValue);
                    }
                    var junk = new byte[8][];   // instrument check: 128 KB shows in its frame
                    for (int i = 0; i < junk.Length; i++) junk[i] = new byte[16384];
                    yield return null;
                    Assert.Greater(rec.LastValue, idle + 65536, "the GC counter sees allocations");
                    GC.KeepAlive(junk);
                    Burst(120);
                    yield return null;
                    long burst = rec.LastValue;
                    Debug.Log($"[DESYNC] bosses GC: idle frame max {idle} B, burst frame {burst} B");
                    Assert.IsTrue(rec.Valid, "GC counter unavailable");
                    Assert.LessOrEqual(burst, idle, "boss updates allocate");
                }
            }
            finally { foreach (var mb in quiet) if (mb) mb.enabled = true; }
            Assert.IsTrue(GameState.Alive);
        }

        static Action Bind(object target, string method) =>
            (Action)Delegate.CreateDelegate(typeof(Action), target, target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance));
    }
}
