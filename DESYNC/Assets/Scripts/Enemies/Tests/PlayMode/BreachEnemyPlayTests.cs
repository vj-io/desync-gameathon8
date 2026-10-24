using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Desync.Enemies.Tests
{
    /// BREACH enemies: AUDITOR phases and uplink timer, Tracker ICE trail, generated-room stats, run-flag hooks.
    /// Stand-in player; RoomClock is driven by the test (no room is loaded).
    public class BreachEnemyPlayTests
    {
        GameObject _world, _player;
        int _forced, _spotted;
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
            GameState.GlitchCountdown = -1f;
            SetFlags();

            _forced = _spotted = 0;
            _killed = null;
            GameEvents.GlitchForcedFlip += OnForced;
            GameEvents.PlayerKilled += OnKilled;
            GameEvents.Spotted += OnSpotted;
        }

        [TearDown]
        public void TearDown()
        {
            GameEvents.GlitchForcedFlip -= OnForced;
            GameEvents.PlayerKilled -= OnKilled;
            GameEvents.Spotted -= OnSpotted;
            Object.Destroy(_world);
            Object.Destroy(_player);
            if (TraceSystem.I) Object.Destroy(TraceSystem.I.gameObject);
            SetFlags();
            RoomClock.Running = false;
            Breadcrumbs.Clear();
            LayerState.Set(false);
            GameState.Alive = true;
            GameState.TraceActive = false;
            GameState.GlitchCountdown = -1f;
        }

        void OnForced() => _forced++;
        void OnKilled(DeathCause c) => _killed ??= c;
        void OnSpotted(Vector3 _) => _spotted++;

        static void SetFlags(params string[] flags)
        {
            GameState.Flags.Clear();
            foreach (var f in flags) GameState.Flags.Add(f);
            EnemyFlags.Refresh();
        }

        static IEnumerator Run(float seconds, System.Func<bool> until = null)
        {
            float end = Time.time + seconds;
            while (Time.time < end && (until == null || !until()))
            {
                RoomClock.Time += Time.deltaTime;
                yield return null;
            }
        }

        /// Jump the room clock by `dt` and let one frame see it.
        static IEnumerator Jump(float dt)
        {
            RoomClock.Time += dt;
            yield return null;
        }

        static float Yaw(Transform t) => Mathf.DeltaAngle(0f, t.localEulerAngles.y);

        [UnityTest]
        public IEnumerator AuditorPhasesEscalateAndP3ForcesFlips()
        {
            var a = EnemyFactory.Auditor(_world.transform, Vector3.zero).GetComponent<Auditor>();
            _player.transform.position = new Vector3(0f, 0f, -30f);
            yield return null;
            Assert.AreEqual(1, a.Phase);
            Assert.AreSame(a, Auditor.Current);
            foreach (var z in a.glitchZones) Assert.IsFalse(z.activeSelf);

            float a0 = a.RingAngle;
            yield return Jump(1f);
            Assert.AreEqual(Auditor.BaseTurn, Mathf.DeltaAngle(a0, a.RingAngle), 0.5f, "P1 ring speed");

            GameEvents.AccessGranted?.Invoke("T1");
            Assert.AreEqual(2, a.Phase, "<= 2 terminals left");
            foreach (var z in a.glitchZones) Assert.IsTrue(z.activeSelf, "glitch zones appear in P2");
            a0 = a.RingAngle;
            yield return Jump(1f);
            Assert.AreEqual(Auditor.BaseTurn * 1.4f, Mathf.DeltaAngle(a0, a.RingAngle), 0.5f, "P2 sweep +40 %");

            GameEvents.AccessGranted?.Invoke("T2");
            Assert.AreEqual(2, a.Phase);
            GameEvents.AccessGranted?.Invoke("T3");
            Assert.AreEqual(3, a.Phase, "final terminal");
            Assert.AreEqual(Auditor.UplinkTime, a.UplinkRemaining);

            float t3 = RoomClock.Time;
            RoomClock.Time = t3 + 3.7f;
            yield return null;
            Assert.AreEqual(0, _forced);
            Assert.That(GameState.GlitchCountdown, Is.InRange(0.01f, Auditor.FlipTelegraph), "screen-tear telegraph before the flip");
            RoomClock.Time = t3 + 4.05f;
            yield return null;
            Assert.AreEqual(1, _forced, "forced flip every 4 s");
            Assert.AreEqual(-1f, GameState.GlitchCountdown);
            RoomClock.Time = t3 + 8.05f;
            yield return null;
            Assert.AreEqual(2, _forced);
            Assert.AreEqual(Auditor.UplinkTime - 8.05f, a.UplinkRemaining, 0.1f);
        }

        [UnityTest]
        public IEnumerator AuditorUplinkTimerFailsThenSucceeds()
        {
            var a = EnemyFactory.Auditor(_world.transform, Vector3.zero).GetComponent<Auditor>();
            _player.transform.position = new Vector3(0f, 0f, -30f);
            yield return null;

            GameEvents.ExitOpened?.Invoke();   // all terminals done (or the debug win): uplink open
            Assert.AreEqual(3, a.Phase);
            yield return Jump(Auditor.UplinkTime - 0.1f);
            Assert.IsNull(_killed);
            yield return Jump(0.2f);
            Assert.AreEqual(DeathCause.TraceTimer, _killed, "too slow to the uplink");
            Assert.AreEqual(Auditor.KillerId, PlayerRef.LastKillerId);

            foreach (var r in a.GetComponentsInChildren<IResettable>(true)) r.ResetState();   // REBOOT
            RoomClock.Time = 0f;
            GameState.Alive = true;
            _killed = null;
            yield return null;
            Assert.AreEqual(1, a.Phase);
            Assert.AreEqual(-1f, a.UplinkRemaining);
            foreach (var z in a.glitchZones) Assert.IsFalse(z.activeSelf);

            GameEvents.ExitOpened?.Invoke();
            yield return Jump(10f);
            GameEvents.ExitReached?.Invoke();   // made it
            Assert.AreEqual(4, a.Phase);
            Assert.AreEqual(-1f, a.UplinkRemaining);
            int flips = _forced;
            yield return Jump(15f);
            yield return Jump(15f);
            Assert.IsNull(_killed, "no timeout after the uplink");
            Assert.AreEqual(flips, _forced, "forced flips stop at the uplink");
        }

        [UnityTest]
        public IEnumerator AuditorHuntersPatrolInSourceAndHeatSeesSource()
        {
            SetFlags("mut:ice_swarm", "flag:boss_overclock");
            var a = EnemyFactory.Auditor(_world.transform, Vector3.zero).GetComponent<Auditor>();
            _player.transform.position = new Vector3(0f, 0f, -30f);
            yield return null;
            foreach (var h in a.hunters) Assert.IsFalse(h.gameObject.activeSelf, "out from the first flip on");

            LayerState.Set(true);
            yield return null;
            foreach (var h in a.hunters) Assert.IsTrue(h.gameObject.activeSelf, "two hunters + one for ICE SWARM");
            var h0 = a.hunters[0];
            float before = Vector3.Distance(h0.transform.position, h0.patrol[1]);
            yield return Run(0.5f);
            Assert.Less(Vector3.Distance(h0.transform.position, h0.patrol[1]), before - 0.5f, "walks its loop");
            Assert.IsFalse(h0.Hunting);

            _player.transform.position = h0.transform.position + new Vector3(0f, 0f, -3f);
            yield return Run(0.1f);
            Assert.IsTrue(h0.Hunting, "chases on sight");

            LayerState.Set(false);   // RENDER: frozen, not despawned
            yield return Run(GameConfig.I.iceDespawn + 0.3f);
            Assert.IsTrue(h0.gameObject.activeSelf);

            RoomClock.Time = 5.5f;   // heat window: last second of every 6
            yield return null;
            foreach (var s in a.scanners) Assert.IsTrue(s.bothLayers, "heat: the sweep sees SOURCE");
            RoomClock.Time = 6.5f;
            yield return null;
            foreach (var s in a.scanners) Assert.IsFalse(s.bothLayers);
        }

        [UnityTest]
        public IEnumerator TrackerFollowsBreadcrumbTrail()
        {
            int stings = 0;
            Vector3 stingAt = default;
            System.Action<Vector3> onSting = p => { stings++; stingAt = p; };
            EnemyEvents.TrackerSpawned += onSting;
            try
            {
                var nest = EnemyFactory.TrackerIce(_world.transform, new Vector3(0f, 0f, -2f)).GetComponent<TrackerNest>();
                var tracker = nest.hunter;
                _player.transform.position = Vector3.zero;
                LayerState.Set(true);
                yield return null;
                yield return Jump(GameConfig.I.iceSpawnInterval + 0.05f);
                Assert.AreEqual(1, stings, "audio sting raised once on spawn");
                Assert.Less(Vector3.Distance(stingAt, nest.transform.position), 0.01f);
                Assert.IsTrue(tracker.gameObject.activeSelf);

                // walk 8 m north then 8 m east at 5 m/s (crumbs every 0.1 s), then stand still
                const float Leg = 8f, Speed = 5f;
                float walked = 0f, nearCorner = float.MaxValue, worstCut = 0f;
                float end = Time.time + 9f;
                while (Time.time < end && _killed == null)
                {
                    float dt = Time.deltaTime;
                    walked = Mathf.Min(2f * Leg, walked + Speed * dt);
                    _player.transform.position = walked <= Leg ? new Vector3(0f, 0f, walked) : new Vector3(walked - Leg, 0f, Leg);
                    RoomClock.Time += dt;
                    yield return null;
                    Vector3 t = tracker.transform.position;
                    nearCorner = Mathf.Min(nearCorner, new Vector2(t.x, t.z - Leg).magnitude);
                    if (t.z < Leg - 1f) worstCut = Mathf.Max(worstCut, Mathf.Abs(t.x));
                }
                Assert.Less(nearCorner, 0.6f, "walks through the corner the player turned at");
                Assert.Less(worstCut, 0.6f, "never cuts the corner toward the player");
                Assert.AreEqual(DeathCause.Ice, _killed, "catches a player who stops");
            }
            finally { EnemyEvents.TrackerSpawned -= onSting; }
        }

        [UnityTest]
        public IEnumerator TrackerWaitsFrozenInRenderAndPicksUpTheNewTrail()
        {
            var nest = EnemyFactory.TrackerIce(_world.transform, new Vector3(0f, 0f, -6f)).GetComponent<TrackerNest>();
            var tracker = nest.hunter;
            _player.transform.position = Vector3.zero;
            LayerState.Set(true);
            yield return null;
            yield return Jump(GameConfig.I.iceSpawnInterval + 0.05f);
            Assert.IsTrue(tracker.gameObject.activeSelf);

            LayerState.Set(false);
            yield return Run(GameConfig.I.iceDespawn + 0.3f);
            Assert.IsTrue(tracker.gameObject.activeSelf, "a tracker is not despawned in RENDER");
            yield return Jump(Breadcrumbs.Life + 0.5f);   // every crumb expires
            Assert.AreEqual(Breadcrumbs.Next, Breadcrumbs.Oldest, "trail cold");

            _player.transform.position = new Vector3(20f, 0f, 0f);
            LayerState.Set(true);
            Vector3 p0 = tracker.transform.position;
            yield return Run(0.5f);
            Assert.Greater(Vector3.Distance(p0, tracker.transform.position), 0.5f, "back in SOURCE: heads for the new trail");
            Assert.IsNull(_killed);
        }

        [UnityTest]
        public IEnumerator GeneratedRoomStatOverloads()
        {
            var fixedCam = EnemyFactory.FixedCamera(_world.transform, new Vector3(0f, 2.5f, 0f), 0f);
            var pivot = fixedCam.GetComponent<CameraSweep>().pivot;
            _player.transform.position = new Vector3(0f, 0f, 11f);   // beyond the classic 8 m
            yield return Run(0.2f, () => _spotted > 0);
            Assert.AreEqual(1, _spotted, "fixed camera sees 12 m");
            yield return Jump(2f);
            Assert.AreEqual(0f, Yaw(pivot), 0.01f, "fixed camera does not move");
            Object.Destroy(fixedCam);
            _player.transform.position = new Vector3(0f, 0f, -30f);

            RoomClock.Time = 0f;
            var sweep = EnemyFactory.SweepCamera(_world.transform, new Vector3(20f, 2.5f, 0f), 0f).GetComponent<CameraSweep>();
            yield return null;
            Assert.AreEqual(-30f, Yaw(sweep.pivot), 0.5f);
            yield return Jump(4f);
            Assert.AreEqual(30f, Yaw(sweep.pivot), 0.5f, "one way in 4 s (8 s period)");

            RoomClock.Time = 0f;
            var drone = EnemyFactory.Drone(_world.transform, new[] { new Vector3(-5f, 0f, 20f), new Vector3(5f, 0f, 20f) }, 0f, 3f);
            RoomClock.Time = 5f / 3f;
            yield return null;
            Assert.AreEqual(0f, drone.transform.position.x, 0.05f, "3 m/s");

            RoomClock.Time = 0f;
            EnemyFactory.GlitchZone(_world.transform, new Vector3(-20f, 0f, -20f), 1.5f, 1f, 0.5f);
            _player.transform.position = new Vector3(-20f, 0f, -20f);
            yield return Run(0.3f);
            Assert.AreEqual(-1f, GameState.GlitchCountdown, "no tear before the telegraph");
            yield return Run(0.35f);
            Assert.That(GameState.GlitchCountdown, Is.InRange(0f, 0.5f), "0.5 s telegraph");
            yield return Run(1f, () => _forced > 0);
            Assert.AreEqual(1, _forced);
            Assert.That(RoomClock.Time, Is.InRange(0.9f, 1.15f), "force-flip after 1.0 s inside");
        }

        /// AUDITOR in P3 with its hunters out, a tracker on the trail, FLICKER + GAZE running: no managed allocations per frame.
        [UnityTest]
        public IEnumerator BreachEnemiesDoNotAllocatePerFrame()
        {
            var disabled = new System.Collections.Generic.List<MonoBehaviour>();
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude))
            {
                string ns = mb.GetType().Namespace ?? "";
                if (ns.StartsWith("Desync") && !ns.StartsWith("Desync.Enemies") && mb.enabled) { mb.enabled = false; disabled.Add(mb); }
            }
            try
            {
                SetFlags("mut:flicker", "mut:gaze", "mut:ice_swarm", "flag:boss_overclock", "flag:log_wipe");
                RoomClock.Running = true;
                var a = EnemyFactory.Auditor(_world.transform, Vector3.zero).GetComponent<Auditor>();
                EnemyFactory.TrackerIce(_world.transform, new Vector3(30f, 0f, -30f));   // out and walking, far from the player
                EnemyFactory.SweepCamera(_world.transform, new Vector3(-20f, 2.5f, -20f), 0f);
                _player.transform.position = new Vector3(14f, 0f, 14f);
                LayerState.Set(true);
                yield return null;
                yield return Jump(GameConfig.I.iceSpawnInterval + 0.05f);
                GameEvents.ExitOpened?.Invoke();
                yield return Run(0.5f);

                long worst = 0;
                for (int f = 0; f < 90; f++)
                {
                    long before = System.GC.GetAllocatedBytesForCurrentThread();
                    RoomClock.Time += Time.deltaTime;
                    yield return null;
                    long d = System.GC.GetAllocatedBytesForCurrentThread() - before;
                    if (d > worst) worst = d;
                }
                Debug.Log($"[DESYNC] breach enemies GC per frame, worst = {worst} B");
                Assert.AreEqual(3, a.Phase);
                Assert.IsTrue(GameState.Alive);
                Assert.AreEqual(0, worst, "managed bytes allocated in one frame");
            }
            finally { foreach (var mb in disabled) if (mb) mb.enabled = true; }
        }

        [UnityTest]
        public IEnumerator RunFlagsChangeEnemies()
        {
            var drone = EnemyFactory.Drone(_world.transform, new[] { Vector3.zero }, 0f, 3f);
            var dc = drone.GetComponent<DronePatrol>().cone;
            var cam = EnemyFactory.FixedCamera(_world.transform, new Vector3(0f, 2.5f, -12f), 180f).GetComponentInChildren<VisionCone>();
            var sweep = EnemyFactory.SweepCamera(_world.transform, new Vector3(25f, 2.5f, -25f), 0f).GetComponent<CameraSweep>();
            _player.transform.position = new Vector3(0f, 0f, 9.4f);
            yield return Run(0.2f);
            Assert.AreEqual(10f, dc.Range);
            Assert.Greater(dc.Dwell, 0f, "drone sees 9 m ahead");
            Assert.IsTrue(cam.Active);

            SetFlags("flag:signal_jam", "mut:blind_cams", "mut:spotlight", "flag:log_wipe");
            yield return Run(0.1f);
            Assert.AreEqual(8f, dc.Range, 1e-4f, "SIGNAL JAM: drones -20 % range");
            Assert.AreEqual(0f, dc.Dwell, "out of the jammed range");
            Assert.IsFalse(cam.Active, "BLIND CAMS: cameras off");
            Assert.IsFalse(cam.coneRenderer.enabled);
            Assert.IsTrue(dc.Active, "drones still see");

            RoomClock.Time = 2f;
            yield return null;
            Assert.AreEqual(sweep.OffsetAt(2f * EnemyFlags.SpotlightSpeed), Yaw(sweep.pivot), 0.5f, "SPOTLIGHT: sweep 30 % faster");

            _player.transform.position = new Vector3(-30f, 0f, 30f);
            yield return null;
            GameState.TraceActive = true;
            GameState.TraceRemaining = 6f;
            yield return Jump(1f);
            Assert.AreEqual(6f - EnemyFlags.LogWipeDecay, GameState.TraceRemaining, 0.02f, "LOG WIPE: TRACE decays 25 % faster");

            SetFlags("mut:flicker");
            RoomClock.Running = true;   // hazards run only while a room clock does
            float t0 = RoomClock.Time;
            RoomClock.Time = t0 + EnemyFlags.FlickerPeriod - 0.5f;
            yield return null;
            Assert.That(GameState.GlitchCountdown, Is.InRange(0.01f, EnemyFlags.FlickerTelegraph), "FLICKER telegraph");
            RoomClock.Time = t0 + EnemyFlags.FlickerPeriod + 0.05f;
            yield return null;
            Assert.AreEqual(1, _forced, "FLICKER: forced flip every 6 s");

            SetFlags("mut:gaze");
            var gaze = Object.FindAnyObjectByType<RoomHazards>().GetComponentInChildren<VisionCone>();
            Assert.IsNotNull(gaze);
            Assert.IsTrue(gaze.bothLayers);
            RoomClock.Time = 5f;
            yield return null;
            Assert.IsFalse(gaze.Active, "between sweeps");
            RoomClock.Time = EnemyFlags.GazePeriod + 1f;
            yield return null;
            Assert.IsTrue(gaze.Active, "GAZE sweeps every 12 s");
        }
    }
}
