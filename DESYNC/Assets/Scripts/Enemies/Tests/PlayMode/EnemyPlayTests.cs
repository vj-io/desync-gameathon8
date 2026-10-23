using System.Collections;
using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.TestTools;

namespace Desync.Enemies.Tests
{
    /// Behaviour checks with a stand-in player; RoomClock is ticked by the test (no RoomManager room is loaded).
    public class EnemyPlayTests
    {
        GameObject _world, _player;
        int _spotted, _traceStarted, _alarm, _ticks, _fails, _windup, _spawned, _forced;
        DeathCause? _killed;
        string _granted;

        [SetUp]
        public void SetUp()
        {
            Application.targetFrameRate = 60;
            _world = new GameObject("World");
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.SetParent(_world.transform);
            floor.transform.position = new Vector3(0f, -0.5f, 0f);
            floor.transform.localScale = new Vector3(60f, 1f, 60f);
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

            LayerState.Set(false);
            RoomClock.Time = 0f;
            GameState.Alive = true;
            GameState.DemoMode = GameState.Cinematic = GameState.InHack = GameState.TraceActive = false;
            GameState.TraceCount = 0;

            _spotted = _traceStarted = _alarm = _ticks = _fails = _windup = _spawned = _forced = 0;
            _killed = null;
            _granted = null;
            GameEvents.Spotted += OnSpotted;
            GameEvents.TraceStarted += OnTrace;
            GameEvents.Alarm += OnAlarm;
            GameEvents.HackTick += OnTick;
            GameEvents.HackFailed += OnFail;
            GameEvents.AccessGranted += OnGranted;
            GameEvents.IceWindup += OnWindup;
            GameEvents.IceSpawned += OnSpawned;
            GameEvents.PlayerKilled += OnKilled;
            GameEvents.GlitchForcedFlip += OnForced;
        }

        [TearDown]
        public void TearDown()
        {
            GameEvents.Spotted -= OnSpotted;
            GameEvents.TraceStarted -= OnTrace;
            GameEvents.Alarm -= OnAlarm;
            GameEvents.HackTick -= OnTick;
            GameEvents.HackFailed -= OnFail;
            GameEvents.AccessGranted -= OnGranted;
            GameEvents.IceWindup -= OnWindup;
            GameEvents.IceSpawned -= OnSpawned;
            GameEvents.PlayerKilled -= OnKilled;
            GameEvents.GlitchForcedFlip -= OnForced;
            Object.Destroy(_world);
            Object.Destroy(_player);
            if (TraceSystem.I) Object.Destroy(TraceSystem.I.gameObject);
            LayerState.Set(false);
            GameState.Alive = true;
            GameState.InHack = GameState.TraceActive = false;
        }

        void OnSpotted(Vector3 _) => _spotted++;
        void OnTrace(int n) => _traceStarted = n;
        void OnAlarm() => _alarm++;
        void OnTick(int _) => _ticks++;
        void OnFail() => _fails++;
        void OnGranted(string s) => _granted = s;
        void OnWindup(Vector3 _) => _windup++;
        void OnSpawned(Vector3 _) => _spawned++;
        void OnKilled(DeathCause c) => _killed ??= c;
        void OnForced() => _forced++;

        /// Advance RoomClock in step with real frames (agents and frame timing stay realistic).
        static IEnumerator Run(float seconds, System.Func<bool> until = null)
        {
            float end = Time.time + seconds;
            while (Time.time < end && (until == null || !until()))
            {
                RoomClock.Time += Time.deltaTime;
                yield return null;
            }
        }

        static GameObject Wall(Transform parent, Vector3 pos, Vector3 size)
        {
            var w = GameObject.CreatePrimitive(PrimitiveType.Cube);
            w.transform.SetParent(parent);
            w.transform.position = pos;
            w.transform.localScale = size;
            w.layer = GameLayers.WallRender;
            Physics.SyncTransforms();   // scene walls are static from load; a wall made mid-test must be synced before queries
            return w;
        }

        [UnityTest]
        public IEnumerator CameraSpotsTracesAndRespectsWallsAndLayers()
        {
            float half = GameConfig.I.cameraSweepPeriod * 0.5f;   // phase so the sweep sits at centre yaw at t=0
            var cam = EnemyFactory.Camera(_world.transform, new Vector3(0f, 2.5f, 0f), 0f, 20f, half);
            _player.transform.position = new Vector3(0f, 0f, 4f);
            var cone = cam.GetComponentInChildren<VisionCone>();

            // SOURCE: blind and hidden
            LayerState.Set(true);
            yield return Run(0.3f);
            Assert.AreEqual(0, _spotted);
            Assert.IsFalse(cone.coneRenderer.enabled);
            Assert.IsFalse(cone.spot.enabled);

            // wall in between: never sees through it
            LayerState.Set(false);
            var wall = Wall(_world.transform, new Vector3(0f, 1.5f, 2f), new Vector3(4f, 3f, 0.3f));
            yield return Run(0.3f);
            Assert.AreEqual(0, _spotted);
            Object.Destroy(wall);
            yield return null;

            // in the open: Spotted, then TRACE after traceDwell
            RoomClock.Time = 0f;
            yield return Run(0.2f);
            Assert.AreEqual(1, _spotted);
            Assert.IsFalse(GameState.TraceActive);
            yield return Run(0.6f, () => GameState.TraceActive);
            Assert.IsTrue(GameState.TraceActive);
            Assert.AreEqual(1, GameState.TraceCount);
            Assert.AreEqual(1, _traceStarted);
            Assert.AreSame(cone.traceMat, cone.coneRenderer.sharedMaterial, "cones turn red while traced");

            // demo mode: never detect
            GameState.DemoMode = true;
            int before = _spotted;
            _player.transform.position = new Vector3(0f, 0f, 5f);
            yield return Run(0.2f);
            Assert.AreEqual(before, _spotted);
            GameState.DemoMode = false;

            // TRACE expires after traceDuration with nobody watching
            _player.transform.position = new Vector3(0f, 0f, -10f);
            RoomClock.Time += GameConfig.I.traceDuration + 0.1f;
            yield return null;
            yield return null;
            Assert.IsFalse(GameState.TraceActive);
        }

        [UnityTest]
        public IEnumerator TerminalHackFlow()
        {
            var go = EnemyFactory.Terminal(_world.transform, new Vector3(0f, 0f, 1.5f), 180f, 0, 0);
            var t = go.GetComponent<Terminal>();
            yield return null;
            Assert.IsTrue(t.CanInteract);
            t.Interact();
            Assert.IsTrue(GameState.InHack);
            Assert.IsFalse(t.CanInteract);

            t.Press((t.Expected(0) + 1) % 4);   // wrong
            Assert.AreEqual(1, _fails);
            Assert.AreEqual(0, t.Step);
            t.Press(t.Expected(0));              // inside the lockout: ignored
            Assert.AreEqual(0, _ticks);
            RoomClock.Time += GameConfig.I.terminalWrongLockout + 0.01f;

            int n = GameConfig.I.promptLengthByRoom[0];
            for (int i = 0; i < n; i++) t.Press(t.Expected(i));
            Assert.AreEqual(n, _ticks);
            Assert.AreEqual(GameConfig.I.terminalLines[0], _granted);
            Assert.IsFalse(GameState.InHack);
            Assert.IsTrue(t.Done);
            Assert.IsFalse(t.CanInteract);

            t.ResetState();
            Assert.IsFalse(t.Done);
            Assert.IsTrue(t.CanInteract);

            // walking out of range cancels
            t.Interact();
            _player.transform.position = new Vector3(0f, 0f, -10f);
            yield return null;
            yield return null;
            Assert.IsFalse(t.Hacking);
            Assert.IsFalse(GameState.InHack);
        }

        [UnityTest]
        public IEnumerator IceSpawnsOnlyInSourceDespawnsInRenderAndKills()
        {
            var surf = _world.AddComponent<NavMeshSurface>();
            surf.layerMask = 1 << GameLayers.Floor;
            surf.collectObjects = CollectObjects.Children;
            surf.BuildNavMesh();
            EnemyFactory.IceVent(_world.transform, new Vector3(9f, 0f, 0f));
            var c = GameConfig.I;

            yield return Run(c.iceSpawnInterval + 0.3f);    // RENDER: nothing
            Assert.AreEqual(0, _spawned);

            LayerState.Set(true);
            yield return Run(c.iceSpawnInterval + 0.3f, () => _spawned > 0);
            Assert.AreEqual(1, _windup);
            Assert.AreEqual(1, _spawned);
            Assert.AreEqual(1, IceHunter.Alive);

            LayerState.Set(false);                           // back to RENDER: frozen, harmless, gone after iceDespawn
            yield return Run(c.iceDespawn + 0.3f);
            Assert.AreEqual(0, IceHunter.Alive);
            Assert.IsNull(_killed);

            LayerState.Set(true);                            // chase in SOURCE -> kill on touch
            yield return Run(c.iceSpawnInterval + 4f, () => _killed != null);
            Assert.AreEqual(DeathCause.Ice, _killed);
        }

        [UnityTest]
        public IEnumerator DronePatrolIsDeterministicAndOnlyLethalInRender()
        {
            var c = GameConfig.I;
            var go = EnemyFactory.Drone(_world.transform, new[] { new Vector3(-5f, 0f, 10f), new Vector3(5f, 0f, 10f) }, 0f);
            _player.transform.position = new Vector3(0f, 0f, -20f);
            RoomClock.Time = 5f / c.droneSpeed;              // 5 m along A->B
            yield return null;
            Assert.AreEqual(0f, go.transform.position.x, 0.05f);
            Assert.AreEqual(c.droneHover, go.transform.position.y, 0.15f);
            RoomClock.Time = 15f / c.droneSpeed;             // 15 m: at B and 5 m back
            yield return null;
            Assert.AreEqual(0f, go.transform.position.x, 0.05f);

            LayerState.Set(true);                            // SOURCE: hidden + harmless
            _player.transform.position = new Vector3(go.transform.position.x, 0f, 10f);
            yield return null;
            yield return null;
            Assert.IsNull(_killed);
            Assert.IsFalse(go.GetComponent<DronePatrol>().body.gameObject.activeSelf);

            LayerState.Set(false);
            yield return null;
            yield return null;
            Assert.AreEqual(DeathCause.Drone, _killed);

            // restart restores the identical start state
            foreach (var r in go.GetComponentsInChildren<IResettable>(true)) r.ResetState();
            RoomClock.Time = 0f;
            yield return null;
            Assert.AreEqual(-5f, go.transform.position.x, 0.05f);
        }

        [UnityTest]
        public IEnumerator HijackedDroneOrbitsBlindsCamerasAndExpires()
        {
            var c = GameConfig.I;
            _player.transform.position = Vector3.zero;
            var drone = EnemyFactory.Drone(_world.transform, new[] { new Vector3(-8f, 0f, -8f), new Vector3(-8f, 0f, -12f) }, 0f);
            var pad = EnemyFactory.HijackPad(_world.transform, new Vector3(0f, 0f, 1.2f), 180f, drone, 3).GetComponent<HijackPad>();
            yield return null;
            pad.Interact();
            for (int i = 0; i < 3; i++) pad.Press(pad.Expected(i));
            var h = drone.GetComponent<HijackedDrone>();
            Assert.IsTrue(h.Active);
            Assert.IsTrue(GameState.HijackActive);
            Assert.AreEqual(GameLayers.HijackedDrone, drone.layer);
            Assert.IsFalse(pad.CanInteract);

            yield return Run(2.5f);
            Vector3 d = drone.transform.position - _player.transform.position;
            d.y = 0f;
            Assert.AreEqual(HijackedDrone.OrbitRadius, d.magnitude, 0.3f, "orbits the player");
            Assert.IsNull(_killed);

            var cam = EnemyFactory.Camera(_world.transform, drone.transform.position + drone.transform.forward * 2f, 0f, 0f, 0f);
            var cone = cam.GetComponentInChildren<VisionCone>();
            Assert.IsTrue(HijackedDrone.Jams(cone), "camera inside the drone's cone is jammed");

            RoomClock.Time += c.hijackDuration;
            yield return null;
            yield return null;
            Assert.IsFalse(h.Active);
            Assert.IsFalse(GameState.HijackActive);
            Assert.AreEqual(GameLayers.Drone, drone.layer);
        }

        [UnityTest]
        public IEnumerator FinaleLooksUpSplitsFacesAndCompletes()
        {
            bool completed = false;
            System.Action onDone = () => completed = true;
            GameEvents.GameCompleted += onDone;
            var arch = EnemyFactory.Architect(_world.transform, new Vector3(0f, 0f, 6f)).GetComponent<Architect>();
            var b1 = EnemyFactory.Billboard(_world.transform, new Vector3(-6f, 2f, 6f), 90f, new Vector2(3f, 1.5f)).GetComponent<Billboard>();
            var b2 = EnemyFactory.Billboard(_world.transform, new Vector3(6f, 2f, 6f), 270f, new Vector2(3f, 1.5f)).GetComponent<Billboard>();
            yield return null;
            Assert.IsFalse(arch.strings.activeSelf);
            LayerState.Set(true);
            Assert.IsTrue(arch.strings.activeSelf, "strings show in SOURCE");
            LayerState.Set(false);

            GameEvents.FinaleStarted?.Invoke();
            Assert.IsTrue(GameState.Cinematic);
            float t0 = Time.time;
            yield return Run(10f, () => completed);
            GameEvents.GameCompleted -= onDone;
            Assert.IsTrue(completed);
            float took = Time.time - t0;
            Assert.That(took, Is.InRange(5f, 8.5f), "finale length");
            Assert.Less(PlayerRef.Head.localEulerAngles.x, 359f);
            Assert.Greater(PlayerRef.Head.localEulerAngles.x, 270f, "player looks up at the face");
            Assert.IsTrue(b1.face.activeSelf && b2.face.activeSelf, "board faces on every billboard");
            Assert.IsFalse(arch.face.activeSelf);
            arch.ResetState();
            Assert.IsFalse(GameState.Cinematic);
        }

        /// Zero managed allocations per frame from the enemy systems (other workstreams' behaviours disabled).
        [UnityTest]
        public IEnumerator EnemySystemsDoNotAllocatePerFrame()
        {
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude))
            {
                string ns = mb.GetType().Namespace ?? "";
                if (ns.StartsWith("Desync") && !ns.StartsWith("Desync.Enemies")) mb.enabled = false;
            }
            var surf = _world.AddComponent<NavMeshSurface>();
            surf.layerMask = 1 << GameLayers.Floor;
            surf.collectObjects = CollectObjects.Children;
            surf.BuildNavMesh();
            var enemies = new GameObject("Enemies").transform;
            enemies.SetParent(_world.transform);
            EnemyFactory.Camera(enemies, new Vector3(0f, 2.5f, -3f), 180f, 20f, 0f);
            EnemyFactory.Drone(enemies, new[] { new Vector3(-6f, 0f, 8f), new Vector3(6f, 0f, 8f) }, 0f);
            EnemyFactory.IceVent(enemies, new Vector3(12f, 0f, 12f));
            var term = EnemyFactory.Terminal(enemies, new Vector3(0f, 0f, 1.5f), 180f, 4, 6).GetComponent<Terminal>();
            EnemyFactory.GlitchZone(enemies, new Vector3(-10f, 0f, -10f), 2f);
            EnemyFactory.ExitPortal(enemies, new Vector3(0f, 0f, -12f), 0f);
            EnemyFactory.Architect(enemies, new Vector3(0f, 0f, 20f));
            EnemyFactory.Billboard(enemies, new Vector3(5f, 2f, 20f), 270f, new Vector2(3f, 1.5f));
            _player.transform.position = Vector3.zero;

            // warm up: TRACE, an ICE out and chasing, a hack in progress with the scrambled SOURCE screen
            yield return Run(0.5f);
            LayerState.Set(true);
            term.Interact();
            yield return Run(GameConfig.I.iceSpawnInterval + 0.3f, () => _spawned > 0);
            Assert.AreEqual(1, _spawned);
            yield return Run(0.3f);

            long worst = 0;
            for (int f = 0; f < 90; f++)
            {
                long before = System.GC.GetAllocatedBytesForCurrentThread();
                RoomClock.Time += Time.deltaTime;
                yield return null;
                long d = System.GC.GetAllocatedBytesForCurrentThread() - before;
                if (d > worst) worst = d;
            }
            Debug.Log($"[DESYNC] enemies GC per frame, worst = {worst} B");
            Assert.AreEqual(0, worst, "managed bytes allocated in one frame");
        }

        [UnityTest]
        public IEnumerator GlitchZoneForcesFlipOnExpiry()
        {
            EnemyFactory.GlitchZone(_world.transform, new Vector3(0f, 0f, 0f), 2f);
            _player.transform.position = new Vector3(0.5f, 0f, 0.5f);
            yield return Run(0.2f);
            Assert.Greater(GameState.GlitchCountdown, 0f);
            yield return Run(GameConfig.I.glitchZoneTime + 0.2f, () => _forced > 0);
            Assert.AreEqual(1, _forced);
            _player.transform.position = new Vector3(10f, 0f, 0f);
            yield return null;
            yield return null;
            Assert.AreEqual(-1f, GameState.GlitchCountdown);
        }
    }
}
