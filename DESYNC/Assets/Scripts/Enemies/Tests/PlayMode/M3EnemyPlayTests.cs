using System.Collections;
using System.Collections.Generic;
using Desync.TestKit;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Desync.Enemies.Tests
{
    /// M3 enemies (Lantern, Sentinel, Auditor Drone, Splitter, Daemon, Watcher, puppet string), the perception ladder, decoys
    /// and BLACKOUT. Stand-in player; the test drives RoomClock in fixed steps (no room is loaded).
    public class M3EnemyPlayTests
    {
        const float Step = 1f / 30f;

        GameObject _world, _player;
        int _spotted;
        DeathCause? _killed;
        readonly List<Alert> _alerts = new List<Alert>();

        [SetUp]
        public void SetUp()
        {
            Application.targetFrameRate = 60;
            _world = new GameObject("World");
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.SetParent(_world.transform);
            floor.transform.position = new Vector3(0f, -0.5f, 0f);
            floor.transform.localScale = new Vector3(120f, 1f, 120f);
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
            Decoys.ClearAll();

            _spotted = 0;
            _killed = null;
            _alerts.Clear();
            GameEvents.PlayerKilled += OnKilled;
            GameEvents.Spotted += OnSpotted;
        }

        [TearDown]
        public void TearDown()
        {
            GameEvents.PlayerKilled -= OnKilled;
            GameEvents.Spotted -= OnSpotted;
            EnemyEvents.Alerted -= OnAlert;
            Object.Destroy(_world);
            Object.Destroy(_player);
            if (TraceSystem.I) Object.Destroy(TraceSystem.I.gameObject);
            SetFlags();
            Decoys.ClearAll();
            RoomClock.Running = false;
            LayerState.Set(false);
            GameState.Alive = true;
            GameState.DemoMode = false;
            GameState.TraceActive = false;
            GameState.TraceCount = 0;
        }

        void OnKilled(DeathCause c) => _killed ??= c;
        void OnSpotted(Vector3 _) => _spotted++;
        void OnAlert(Vector3 _, Alert a) => _alerts.Add(a);

        static void SetFlags(params string[] flags)
        {
            GameState.Flags.Clear();
            foreach (var f in flags) GameState.Flags.Add(f);
            EnemyFlags.Refresh();
        }

        /// Advance RoomClock by `seconds` in fixed steps, one frame per step (stops early once `until` holds).
        static IEnumerator Sim(float seconds, System.Func<bool> until = null)
        {
            for (float t = 0f; t < seconds && (until == null || !until()); t += Step)
            {
                RoomClock.Time += Step;
                yield return null;
            }
        }

        static float Flat(Vector3 a, Vector3 b) { a.y = b.y = 0f; return Vector3.Distance(a, b); }

        static void Flip(bool toSource)
        {
            LayerState.Set(toSource);
            GameEvents.Flipped?.Invoke(toSource);
        }

        [UnityTest]
        public IEnumerator LanternBeamTurnsAndTracesOnlyInRender()
        {
            var go = EnemyFactory.Lantern(_world.transform, new[] { Vector3.zero });
            var spin = go.GetComponent<LanternBeam>();
            var cone = go.GetComponentInChildren<VisionCone>();
            Assert.AreEqual(LanternBeam.Range, cone.range);
            Assert.IsNull(go.GetComponent<HijackedDrone>(), "not hijackable");
            Assert.IsTrue(LanternBeam.LanternNearest(new Vector3(3f, 0f, 4f), out var hum), "audio hum query");
            Assert.Less(Vector3.Distance(hum, go.transform.position), 1e-4f);
            _player.transform.position = new Vector3(8f, 0f, 0f);   // beyond 7 m
            RoomClock.Time = 1.5f;
            yield return null;
            Assert.AreEqual(90f, spin.beam.eulerAngles.y, 0.5f, "a quarter turn in 1.5 s: 6 s period");

            yield return Sim(LanternBeam.Period + 0.2f);
            Assert.AreEqual(0, _spotted, "out of range");

            _player.transform.position = new Vector3(5f, 0f, 0f);
            LayerState.Set(true);
            yield return Sim(LanternBeam.Period + 0.2f);
            Assert.AreEqual(0, _spotted, "blind in SOURCE");
            Assert.IsFalse(cone.coneRenderer.enabled);

            LayerState.Set(false);
            yield return Sim(LanternBeam.Period + 0.2f, () => GameState.TraceActive);
            Assert.Greater(_spotted, 0);
            Assert.IsTrue(GameState.TraceActive, "the passing beam dwells long enough to TRACE");

            foreach (var r in go.GetComponentsInChildren<IResettable>(true)) r.ResetState();
            RoomClock.Time = 0f;
            yield return null;
            Assert.AreEqual(0f, Mathf.DeltaAngle(0f, spin.beam.eulerAngles.y), 0.5f, "reset: beam back to its start");
            Assert.AreEqual(Alert.Patrol, go.GetComponent<DronePatrol>().Alert);
        }

        [UnityTest]
        public IEnumerator SentinelPulseKillsOnlyInSourceWithinItsRadius()
        {
            int charges = 0, pulses = 0;
            System.Action<Vector3> onCharge = _ => charges++, onPulse = _ => pulses++;
            EnemyEvents.SentinelCharge += onCharge;
            EnemyEvents.SentinelPulse += onPulse;
            try
            {
                var sen = EnemyFactory.Sentinel(_world.transform, Vector3.zero).GetComponent<Sentinel>();
                _player.transform.position = new Vector3(3f, 0f, 0f);
                yield return null;
                Assert.IsFalse(sen.charge.gameObject.activeSelf);

                RoomClock.Time = Sentinel.Period - 0.5f;
                yield return null;
                Assert.IsTrue(sen.Charging, "0.6 s telegraph");
                Assert.AreEqual(1, charges);
                Assert.IsTrue(sen.charge.gameObject.activeSelf);
                Assert.AreEqual(Sentinel.PulseRadius * (1f - 0.5f / Sentinel.Telegraph), sen.charge.localScale.x, 0.01f, "ring grows to the kill radius");

                RoomClock.Time = Sentinel.Period + 0.05f;
                yield return null;
                Assert.AreEqual(1, pulses);
                Assert.IsNull(_killed, "RENDER: harmless");

                LayerState.Set(true);
                _player.transform.position = new Vector3(Sentinel.PulseRadius + 0.5f, 0f, 0f);
                RoomClock.Time = 2f * Sentinel.Period + 0.05f;
                yield return null;
                Assert.AreEqual(2, pulses);
                Assert.IsNull(_killed, "outside the radius");

                _player.transform.position = new Vector3(Sentinel.PulseRadius - 0.5f, 0f, 0f);
                RoomClock.Time = 3f * Sentinel.Period - 0.2f;
                yield return null;
                Assert.IsNull(_killed, "no kill before the pulse");
                RoomClock.Time = 3f * Sentinel.Period + 0.05f;
                yield return null;
                Assert.AreEqual(DeathCause.Ice, _killed);
                Assert.AreEqual("SENTINEL-1", PlayerRef.LastKillerId);

                sen.ResetState();
                RoomClock.Time = 0f;
                GameState.Alive = true;
                _killed = null;
                yield return null;
                Assert.IsFalse(sen.Charging);
                Assert.IsFalse(sen.charge.gameObject.activeSelf);
                RoomClock.Time = 0.5f;
                yield return null;
                Assert.IsNull(_killed, "reset: back on its schedule from room start");
            }
            finally
            {
                EnemyEvents.SentinelCharge -= onCharge;
                EnemyEvents.SentinelPulse -= onPulse;
            }
        }

        [UnityTest]
        public IEnumerator AuditorDroneComesAtTraceTwoChasesTheLastSeenPointAndLeavesWithTheTrace()
        {
            int dispatched = 0, recalled = 0;
            System.Action<Vector3, bool> onDrone = (_, on) => { if (on) dispatched++; else recalled++; };
            EnemyEvents.AuditorDrone += onDrone;
            try
            {
                _player.transform.position = Vector3.zero;
                TraceSystem.Seen(PlayerRef.Position);   // the room's TraceSystem
                TraceSystem.Sustain();
                Assert.AreEqual(0, dispatched, "no flag: none");

                SetFlags("flag:auditor_drones");
                GameState.TraceActive = false;
                GameState.TraceCount = 0;
                TraceSystem.Sustain();
                Assert.AreEqual(0, dispatched, "TRACE 1: none");
                GameState.TraceActive = false;
                TraceSystem.Sustain();
                Assert.AreEqual(1, dispatched, "TRACE 2: one");
                var d = Object.FindAnyObjectByType<AuditorDrone>();
                Assert.IsNotNull(d);
                Assert.AreEqual(AuditorDrone.ArriveDistance, Flat(d.transform.position, Vector3.zero), 0.05f, "appears just out of sight");

                _player.transform.position = new Vector3(30f, 0f, 30f);   // gone from the last-seen point
                yield return null;
                float before = Flat(d.transform.position, Vector3.zero);
                yield return Sim(0.5f);
                Assert.AreEqual(before - AuditorDrone.Speed * 0.5f, Flat(d.transform.position, Vector3.zero), 0.25f, "5 m/s to the last-seen point");
                yield return Sim(1.5f);
                Assert.Less(Flat(d.transform.position, Vector3.zero), 0.1f);

                LayerState.Set(true);
                _player.transform.position = d.transform.position - Vector3.up * GameConfig.I.droneHover;
                yield return Sim(0.2f);
                Assert.IsNull(_killed, "SOURCE: harmless");
                Assert.IsFalse(d.body.gameObject.activeSelf, "hidden in SOURCE");
                LayerState.Set(false);
                yield return Sim(0.1f);
                Assert.AreEqual(DeathCause.Drone, _killed, "kills on touch in RENDER");
                Assert.AreEqual(AuditorDrone.KillerId, PlayerRef.LastKillerId);
                GameState.Alive = true;

                GameState.TraceActive = false;   // the TRACE ends
                yield return null;
                Assert.IsFalse(d.gameObject.activeSelf);
                Assert.AreEqual(1, recalled);

                SetFlags("mut:auditor_protocol");
                GameState.TraceCount = 0;
                TraceSystem.Sustain();
                Assert.AreEqual(2, dispatched, "Auditor Protocol: from TRACE 1");
                d.ResetState();
                Assert.IsFalse(d.gameObject.activeSelf, "reset");
            }
            finally { EnemyEvents.AuditorDrone -= onDrone; }
        }

        [UnityTest]
        public IEnumerator SplitterBreaksIntoTwoFastMinisOnGraze()
        {
            int splits = 0;
            System.Action<Vector3> onSplit = _ => splits++;
            EnemyEvents.SplitterSplit += onSplit;
            try
            {
                var split = EnemyFactory.SplitterIce(_world.transform, Vector3.zero).GetComponent<SplitterIce>();
                _player.transform.position = new Vector3(20f, 0f, 0f);
                yield return Sim(0.2f);
                Assert.IsFalse(split.hunter.gameObject.activeSelf, "out from the first SOURCE on");

                LayerState.Set(true);
                yield return Sim(0.2f);
                Assert.IsTrue(split.hunter.gameObject.activeSelf);
                Assert.Less(Flat(split.hunter.transform.position, Vector3.zero), 0.05f, "waits at its post");

                GameEvents.Grazed?.Invoke("ICE-999");
                yield return null;
                Assert.AreEqual(0, splits, "another ICE's graze");
                GameEvents.Grazed?.Invoke(split.hunter.Id);
                yield return null;
                Assert.AreEqual(1, splits);
                Assert.IsFalse(split.hunter.gameObject.activeSelf);
                foreach (var m in split.minis) Assert.IsTrue(m.gameObject.activeSelf);

                float x0 = split.minis[0].transform.position.x;
                yield return Sim(0.5f);
                Assert.AreEqual(SplitterIce.MiniSpeed * 0.5f, split.minis[0].transform.position.x - x0, 0.3f, "minis run at 6 m/s");

                LayerState.Set(false);
                yield return Sim(2f);
                Assert.IsTrue(split.minis[0].gameObject.activeSelf, "frozen in RENDER, not despawned");
                yield return Sim(SplitterIce.MiniLife);
                foreach (var m in split.minis) Assert.IsFalse(m.gameObject.activeSelf, "8 s lifetime");
                Assert.IsFalse(split.hunter.gameObject.activeSelf, "the split one stays gone");

                foreach (var r in split.GetComponentsInChildren<IResettable>(true)) r.ResetState();
                RoomClock.Time = 0f;
                LayerState.Set(true);
                yield return Sim(0.1f);
                Assert.IsTrue(split.hunter.gameObject.activeSelf, "reset: back at its post");
                Assert.Less(Flat(split.hunter.transform.position, Vector3.zero), 0.05f);
                Assert.IsNull(_killed);
            }
            finally { EnemyEvents.SplitterSplit -= onSplit; }
        }

        [UnityTest]
        public IEnumerator DaemonCrossesIntoRenderOnlyWhenFlippedNear()
        {
            var crossed = new List<bool>();
            System.Action<Vector3, bool> onCross = (_, on) => crossed.Add(on);
            EnemyEvents.DaemonCrossed += onCross;
            try
            {
                var dm = EnemyFactory.Daemon(_world.transform, Vector3.zero).GetComponent<Daemon>();
                _player.transform.position = new Vector3(3f, 0f, 0f);
                yield return Sim(0.5f);
                Assert.Less(Flat(dm.transform.position, Vector3.zero), 0.01f, "RENDER: dormant");
                Assert.IsFalse(dm.Hunting);

                Flip(true);
                yield return Sim(0.2f);
                Assert.IsTrue(dm.Hunting, "a SOURCE hunter");
                Assert.Greater(Flat(dm.transform.position, Vector3.zero), 0.5f);

                _player.transform.position = dm.transform.position + new Vector3(Daemon.CrossRange + 1f, 0f, 0f);
                Flip(false);
                Vector3 p0 = dm.transform.position;
                yield return Sim(0.5f);
                Assert.IsFalse(dm.Crossed, "flipped too far away");
                Assert.Less(Flat(dm.transform.position, p0), 0.01f, "frozen in RENDER");

                Flip(true);
                _player.transform.position = dm.transform.position + new Vector3(Daemon.CrossRange - 1f, 0f, 0f);
                Flip(false);
                Assert.IsTrue(dm.Crossed, "flipped within 4 m: it follows");
                yield return Sim(2f, () => _killed != null);
                Assert.AreEqual(DeathCause.Ice, _killed, "lethal in RENDER");
                Assert.AreEqual("DAEMON-1", PlayerRef.LastKillerId);
                CollectionAssert.AreEqual(new[] { true }, crossed);

                // REBOOT, then a wall between: crossed, but it never walks through a RENDER wall and drops back after LoseTime
                foreach (var r in dm.GetComponentsInChildren<IResettable>(true)) r.ResetState();
                RoomClock.Time = 0f;
                GameState.Alive = true;
                _killed = null;
                LayerState.Set(false);
                yield return null;
                Assert.IsFalse(dm.Crossed);
                Assert.Less(Flat(dm.transform.position, Vector3.zero), 0.01f, "reset: at its post");
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.transform.SetParent(_world.transform);
                wall.transform.position = new Vector3(1.5f, 1.5f, 0f);
                wall.transform.localScale = new Vector3(0.3f, 3f, 4f);
                wall.layer = GameLayers.WallRender;
                Physics.SyncTransforms();
                _player.transform.position = new Vector3(3f, 0f, 0f);
                Flip(true);
                Flip(false);
                Assert.IsTrue(dm.Crossed);
                yield return Sim(1f);
                Assert.Less(Flat(dm.transform.position, Vector3.zero), 0.01f, "the wall stops it in RENDER");
                yield return Sim(Daemon.LoseTime);
                Assert.IsFalse(dm.Crossed, "lost: back to SOURCE-only");
                Assert.IsNull(_killed);
            }
            finally { EnemyEvents.DaemonCrossed -= onCross; }
        }

        [UnityTest]
        public IEnumerator WatcherSeesBothLayersAndIsNotACamera()
        {
            SetFlags("mut:blind_cams");
            int cues = 0;
            System.Action<Vector3> onCue = _ => cues++;
            EnemyEvents.WatcherSpotted += onCue;
            var w = EnemyFactory.Watcher(_world.transform, new Vector3(0f, 3f, 0f), 0f);
            var cone = w.GetComponentInChildren<VisionCone>();
            Assert.IsTrue(cone.bothLayers);
            Assert.AreEqual(EnemyFactory.WatcherRange, cone.Range, 1e-4f);
            _player.transform.position = new Vector3(0f, 0f, 4f);

            LayerState.Set(true);
            yield return Sim(1f, () => GameState.TraceActive);
            Assert.Greater(_spotted, 0, "sees SOURCE; BLIND CAMS leaves it on");
            Assert.AreEqual(1, cues, "WatcherSpotted once per sighting");
            EnemyEvents.WatcherSpotted -= onCue;
            Assert.IsTrue(GameState.TraceActive);
            Assert.IsTrue(cone.coneRenderer.enabled, "beam shown in SOURCE");

            GameState.TraceActive = false;
            GameState.TraceCount = 0;
            LayerState.Set(false);
            yield return Sim(1f, () => GameState.TraceActive);
            Assert.IsTrue(GameState.TraceActive, "and RENDER");

            _player.transform.position = new Vector3(0f, 0f, -6f);   // behind it
            int seen = _spotted;
            yield return Sim(0.5f);
            Assert.AreEqual(seen, _spotted, "a fixed beam");
        }

        [UnityTest]
        public IEnumerator PuppetStringIsCollectedOnceAndOnlyInSource()
        {
            var got = new List<string>();
            System.Action<string> onString = got.Add;
            EnemyEvents.PuppetString += onString;
            try
            {
                var ps = EnemyFactory.PuppetString(_world.transform, Vector3.zero, "PS-7").GetComponent<PuppetStringPickup>();
                _player.transform.position = Vector3.zero;
                yield return Sim(0.1f);
                Assert.AreEqual(0, got.Count, "RENDER: not there");
                Assert.IsFalse(ps.visual.gameObject.activeSelf);

                LayerState.Set(true);
                yield return Sim(0.1f);
                CollectionAssert.AreEqual(new[] { "PS-7" }, got);
                Assert.IsTrue(ps.Taken);
                Assert.IsFalse(ps.visual.gameObject.activeSelf, "taken");
                yield return Sim(0.2f);
                Assert.AreEqual(1, got.Count, "once");

                ps.ResetState();
                _player.transform.position = new Vector3(5f, 0f, 0f);
                yield return null;
                Assert.IsFalse(ps.Taken);
                Assert.IsTrue(ps.visual.gameObject.activeSelf, "reset: back, shown in SOURCE");
            }
            finally { EnemyEvents.PuppetString -= onString; }
        }

        [UnityTest]
        public IEnumerator DecoyPoolReusesSlotsAndNoisePullsTheNearestDrone()
        {
            _player.transform.position = new Vector3(-40f, 0f, -40f);
            int first = Decoys.Spawn(new Vector3(1f, 0f, 0f), 2f, DecoyKind.Silhouette);
            Assert.Greater(first, 0);
            Assert.AreEqual(1, Decoys.Count);
            var host = Object.FindAnyObjectByType<DecoyHost>();
            Assert.IsNotNull(host);
            int objects = host.GetComponentsInChildren<Transform>(true).Length;

            for (int i = 1; i < Decoys.Capacity; i++) Assert.Greater(Decoys.Spawn(new Vector3(30f + i, 0f, 30f), 5f, DecoyKind.Echo), first);
            Assert.AreEqual(Decoys.Capacity, Decoys.Count);
            Assert.AreEqual(-1, Decoys.Spawn(Vector3.zero, 1f, DecoyKind.Noise), "pool full");

            Assert.IsTrue(Decoys.Nearest(new Vector3(1.5f, 0f, 0f), 1f, out var pos, out var kind));
            Assert.AreEqual(DecoyKind.Silhouette, kind);
            Assert.Less(Vector3.Distance(pos, new Vector3(1f, 0f, 0f)), 1e-4f);
            Assert.IsFalse(Decoys.Nearest(new Vector3(-10f, 0f, 0f), 1f, out _, out _));
            int live = 0;
            for (int i = 0; i < Decoys.Capacity; i++)
                if (Decoys.TryGet(i, out var p, out var k, out float left))
                {
                    live++;
                    if (k == DecoyKind.Silhouette) { Assert.Less(Vector3.Distance(p, new Vector3(1f, 0f, 0f)), 1e-4f); Assert.AreEqual(2f, left, 1e-4f); }
                }
            Assert.AreEqual(Decoys.Capacity, live, "per-slot access for HUD markers");
            Assert.IsFalse(Decoys.TryGet(Decoys.Capacity, out _, out _, out _));

            RoomClock.Time += 2.1f;
            yield return null;
            Assert.AreEqual(Decoys.Capacity - 1, Decoys.Count, "lifetime on RoomClock");
            Assert.Greater(Decoys.Spawn(Vector3.zero, 1f, DecoyKind.Silhouette), 0, "the expired slot is reused");
            Assert.AreEqual(objects, host.GetComponentsInChildren<Transform>(true).Length, "no new objects");

            GameEvents.RoomReset?.Invoke();
            Assert.AreEqual(0, Decoys.Count, "cleared on REBOOT");
            Decoys.Spawn(Vector3.zero, 5f, DecoyKind.Echo);
            var room = new GameObject("Room").AddComponent<RoomInfo>();   // the room changes (dispose / next room)
            Assert.AreEqual(0, Decoys.Count, "a decoy ends with its room");
            Object.Destroy(room.gameObject);
            yield return null;

            var near = EnemyFactory.Drone(_world.transform, new[] { new Vector3(5f, 0f, 0f) }, 0f, 3f).GetComponent<DronePatrol>();
            var far = EnemyFactory.Drone(_world.transform, new[] { new Vector3(-12f, 0f, 0f) }, 0f, 3f).GetComponent<DronePatrol>();
            yield return null;
            Assert.Greater(Decoys.Spawn(Vector3.zero, 3f, DecoyKind.Noise), 0);
            yield return Sim(0.5f);
            Assert.AreEqual(Alert.Hunting, near.Alert, "the nearest drone answers the noise");
            Assert.AreEqual(5f - 1.5f, Flat(near.transform.position, Vector3.zero), 0.2f, "flies to it at its speed");
            Assert.AreEqual(Alert.Patrol, far.Alert);
            Assert.AreEqual(12f, Flat(far.transform.position, Vector3.zero), 0.05f);
            Decoys.ClearAll();
            Assert.AreEqual(0, Decoys.Count);
        }

        [UnityTest]
        public IEnumerator DronePerceptionLadderIsTelegraphed()
        {
            EnemyEvents.Alerted += OnAlert;
            var dp = EnemyFactory.Drone(_world.transform, new[] { new Vector3(-6f, 0f, 0f), new Vector3(6f, 0f, 0f) }, 0f, 3f)
                                 .GetComponent<DronePatrol>();
            _player.transform.position = new Vector3(-40f, 0f, 40f);
            yield return Sim(0.2f);
            Assert.AreEqual(Alert.Patrol, dp.Alert);

            Vector3 at = dp.transform.position;
            _player.transform.position = new Vector3(at.x + 4f, 0f, 0f);   // ahead on its loop
            yield return Sim(0.5f, () => dp.Alert == Alert.Suspicious);
            Assert.AreEqual(Alert.Suspicious, dp.Alert);
            at = dp.transform.position;
            yield return Sim(0.15f);
            Assert.AreEqual(Alert.Suspicious, dp.Alert, "suspicious for 0.25 s");
            Assert.Less(Flat(dp.transform.position, at), 0.01f, "stops on its loop");
            yield return Sim(0.15f);
            Assert.AreEqual(Alert.Tracing, dp.Alert);
            yield return Sim(0.5f, () => GameState.TraceActive);
            Assert.IsTrue(GameState.TraceActive);

            Vector3 seen = PlayerRef.Position;
            _player.transform.position = new Vector3(-40f, 0f, 40f);   // gone
            yield return Sim(0.1f);
            Assert.AreEqual(Alert.Hunting, dp.Alert, "lost during a TRACE: hunts");
            yield return Sim(3f, () => Flat(dp.transform.position, seen) < 0.05f);
            Assert.Less(Flat(dp.transform.position, seen), 0.05f, "to the last-seen point");
            yield return Sim(DronePatrol.HoldTime + 0.2f);
            Assert.AreEqual(Alert.Return, dp.Alert);
            yield return Sim(3f, () => dp.Alert == Alert.Patrol);
            Assert.AreEqual(Alert.Patrol, dp.Alert);
            Assert.Less(Flat(dp.transform.position, at), 0.05f, "back where it left the loop");
            CollectionAssert.AreEqual(new[] { Alert.Suspicious, Alert.Tracing, Alert.Hunting, Alert.Return, Alert.Patrol }, _alerts);
            Assert.IsNull(_killed);
        }

        [UnityTest]
        public IEnumerator CameraTelegraphsPerceptionWithoutStoppingItsSweep()
        {
            var cam = EnemyFactory.SweepCamera(_world.transform, new Vector3(0f, 2.5f, 0f), 0f);
            var sweep = cam.GetComponent<CameraSweep>();
            EnemyEvents.Alerted += OnAlert;
            _player.transform.position = new Vector3(0f, 0f, -20f);
            RoomClock.Time = 2f;   // sweep centre; one frame out of sight so the cone's clock catches up
            yield return null;
            _player.transform.position = new Vector3(0f, 0f, 5f);
            yield return Sim(1f, () => sweep.Alert == Alert.Tracing);
            Assert.AreEqual(Alert.Tracing, sweep.Alert);
            Assert.AreEqual(sweep.OffsetAt(RoomClock.Time), Mathf.DeltaAngle(0f, sweep.pivot.localEulerAngles.y), 0.5f, "never stops");
            yield return Sim(0.5f, () => GameState.TraceActive);

            _player.transform.position = new Vector3(0f, 0f, -20f);
            yield return Sim(0.1f);
            Assert.AreEqual(Alert.Hunting, sweep.Alert, "on alert while its TRACE runs");
            GameState.TraceActive = false;
            yield return Sim(0.1f);
            Assert.AreEqual(Alert.Patrol, sweep.Alert);
            CollectionAssert.AreEqual(new[] { Alert.Suspicious, Alert.Tracing, Alert.Hunting, Alert.Return, Alert.Patrol }, _alerts);
        }

        [UnityTest]
        public IEnumerator BlackoutDimsLightsWithoutCompoundingAndRestores()
        {
            var cam = EnemyFactory.FixedCamera(_world.transform, new Vector3(0f, 2.5f, 0f), 0f);
            var spot = cam.GetComponentInChildren<Light>();
            float lit = spot.intensity;
            Color ambient = RenderSettings.ambientLight;
            try
            {
                SetFlags("mut:blackout");
                Assert.AreEqual(lit * EnemyFlags.BlackoutScale, spot.intensity, 1e-4f, "RENDER spots -40 %");
                Assert.AreEqual(ambient.g * EnemyFlags.BlackoutScale, RenderSettings.ambientLight.g, 1e-4f, "ambient -40 %");
                EnemyFlags.Refresh();   // REBOOT
                Assert.AreEqual(lit * EnemyFlags.BlackoutScale, spot.intensity, 1e-4f, "no compounding");
                Assert.AreEqual(ambient.g * EnemyFlags.BlackoutScale, RenderSettings.ambientLight.g, 1e-4f);
                SetFlags();
                Assert.AreEqual(lit, spot.intensity, 1e-4f, "restored");
                Assert.AreEqual(ambient.g, RenderSettings.ambientLight.g, 1e-4f);
            }
            finally { RenderSettings.ambientLight = ambient; }
            yield return null;
        }

        static object _sink;

        /// Every M3 enemy active (Lantern, Sentinel, split Splitter, crossed Daemon, Watcher, puppet string, Auditor Drone,
        /// all three decoys and a drone answering the noise): no managed allocations per frame, in RENDER then SOURCE.
        [UnityTest]
        public IEnumerator M3EnemiesDoNotAllocatePerFrame()
        {
            var disabled = new List<MonoBehaviour>();
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude))
            {
                string ns = mb.GetType().Namespace ?? "";
                if (ns.StartsWith("Desync") && !ns.StartsWith("Desync.Enemies") && mb.enabled) { mb.enabled = false; disabled.Add(mb); }
            }
            try
            {
                SetFlags("flag:auditor_drones", "mut:blackout");
                var t = _world.transform;
                EnemyFactory.Lantern(t, new[] { new Vector3(-15f, 0f, 0f), new Vector3(-15f, 0f, 6f) });
                EnemyFactory.Sentinel(t, new Vector3(0f, 0f, -15f));
                var split = EnemyFactory.SplitterIce(t, new Vector3(20f, 0f, 20f)).GetComponent<SplitterIce>();
                var dm = EnemyFactory.Daemon(t, new Vector3(3f, 0f, 0f)).GetComponent<Daemon>();
                EnemyFactory.Watcher(t, new Vector3(15f, 3f, 0f), 270f);
                EnemyFactory.PuppetString(t, new Vector3(-10f, 0f, -10f), "PS-1");
                EnemyFactory.Drone(t, new[] { new Vector3(0f, 0f, 10f) }, 0f, 3f);
                _player.transform.position = Vector3.zero;

                Flip(true);
                yield return Sim(0.2f);
                GameEvents.Grazed?.Invoke(split.hunter.Id);
                Flip(false);   // within 4 m of the Daemon: it crosses
                Assert.IsTrue(dm.Crossed);
                TraceSystem.Seen(PlayerRef.Position);
                TraceSystem.Sustain();
                GameState.TraceActive = false;
                TraceSystem.Sustain();   // TRACE 2: the Auditor Drone comes
                Decoys.Spawn(new Vector3(1f, 0f, 1f), 10f, DecoyKind.Silhouette);
                Decoys.Spawn(new Vector3(-1f, 0f, 1f), 10f, DecoyKind.Echo);
                Decoys.Spawn(new Vector3(0f, 0f, 2f), 10f, DecoyKind.Noise);
                _player.transform.position = new Vector3(-30f, 0f, 30f);   // away from everything; the Daemon follows
                yield return Sim(0.2f);
                Assert.IsNotNull(Object.FindAnyObjectByType<AuditorDrone>());
                foreach (var m in split.minis) Assert.IsTrue(m.gameObject.activeSelf);

                // every enemy's Update + LateUpdate, counted exactly (Allocs: a real frame also carries the runner's own)
                Assert.Greater(Allocs.Count(() => _sink = new byte[64]), 0, "the instrument sees a known allocation");
                int extra = 0;
                string who = "";
                for (int pass = 0; pass < 2; pass++)
                {
                    if (pass == 1) { Flip(true); yield return Sim(0.2f); }
                    var frame = Allocs.Frame("Desync.Enemies");   // bound again: the layer change may wake others
                    RoomClock.Time += Step; frame.Run();   // the bound calls' first run
                    for (int f = 0; f < 40; f++) { RoomClock.Time += Step; extra += frame.Count(); }   // 1.3 s of room time: inside the Daemon's 2 s lose window
                    who += (pass == 0 ? "RENDER: " : "; SOURCE: ") + frame.Offenders;
                    if (pass == 0) Assert.IsTrue(dm.Crossed && dm.Hunting, "measured with the Daemon crossed and chasing");
                }
                Debug.Log($"[DESYNC] M3 enemies: allocations in 80 frames = {extra}");
                Assert.IsTrue(GameState.Alive);
                Assert.AreEqual(0, extra, "managed allocations in enemy frames: " + who);
            }
            finally { foreach (var mb in disabled) if (mb) mb.enabled = true; }
        }
    }
}
