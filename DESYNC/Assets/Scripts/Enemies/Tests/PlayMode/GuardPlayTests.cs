using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Desync.Enemies.Tests
{
    /// HALCYON security officers. Stand-in player and floor; the tests drive RoomClock themselves and tick the officers (and their
    /// flashlight cones, which run first in a real frame) through their Update, so a test covers seconds of room time in a frame.
    public class GuardPlayTests
    {
        const float Step = 1f / 60f;

        GameObject _world, _player;
        DeathCause? _killed;
        int _alerts, _windups, _chaseOn, _chaseOff;
        bool? _hit;
        readonly List<Alert> _states = new List<Alert>();
        readonly List<(Action cone, Action guard)> _ticks = new List<(Action, Action)>();

        [SetUp]
        public void SetUp()
        {
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
            GameState.Flags.Clear();
            EnemyFlags.Refresh();
            Decoys.ClearAll();

            _killed = null;
            _hit = null;
            _alerts = _windups = _chaseOn = _chaseOff = 0;
            _states.Clear();
            _ticks.Clear();
            GameEvents.PlayerKilled += OnKilled;
            EnemyEvents.Alerted += OnAlerted;
            EnemyEvents.GuardAlert += OnGuardAlert;
            EnemyEvents.GuardWindup += OnWindup;
            EnemyEvents.GuardStrike += OnStrike;
            EnemyEvents.GuardChase += OnChase;
        }

        [TearDown]
        public void TearDown()
        {
            GameEvents.PlayerKilled -= OnKilled;
            EnemyEvents.Alerted -= OnAlerted;
            EnemyEvents.GuardAlert -= OnGuardAlert;
            EnemyEvents.GuardWindup -= OnWindup;
            EnemyEvents.GuardStrike -= OnStrike;
            EnemyEvents.GuardChase -= OnChase;
            Object.Destroy(_world);
            Object.Destroy(_player);
            if (TraceSystem.I) Object.Destroy(TraceSystem.I.gameObject);
            Decoys.ClearAll();
            LayerState.Set(false);
            GameState.Alive = true;
            GameState.DemoMode = false;
            GameState.TraceActive = false;
            GameState.TraceCount = 0;
            RoomClock.Time = 0f;
        }

        void OnKilled(DeathCause c) => _killed ??= c;
        void OnAlerted(Vector3 _, Alert a) => _states.Add(a);
        void OnGuardAlert(Vector3 _) => _alerts++;
        void OnWindup(Vector3 _) => _windups++;
        void OnStrike(Vector3 _, bool hit) => _hit = hit;
        void OnChase(bool on) { if (on) _chaseOn++; else _chaseOff++; }

        // ------------------------------------------------------------------------------------------------ helpers

        Guard Make(Vector3[] wps, float phase = 0f, float yaw = 0f)
        {
            var g = EnemyFactory.Guard(_world.transform, wps, phase, yaw).GetComponent<Guard>();
            g.enabled = false;            // the test ticks it (cone first, as a frame would)
            g.cone.enabled = false;
            _ticks.Add((Bind(g.cone, "Update"), Bind(g, "Update")));
            Physics.SyncTransforms();
            return g;
        }

        GameObject Wall(Vector3 center, Vector3 size)
        {
            var w = GameObject.CreatePrimitive(PrimitiveType.Cube);
            w.transform.SetParent(_world.transform);
            w.transform.position = center;
            w.transform.localScale = size;
            w.layer = GameLayers.WallRender;
            Physics.SyncTransforms();
            return w;
        }

        /// Advance room time by `seconds` in `step`s, ticking every officer; stops early once `until` holds.
        void Tick(float seconds, Func<bool> until = null, float step = Step)
        {
            for (float t = 0f; t < seconds && (until == null || !until()); t += step)
            {
                RoomClock.Time += step;
                foreach (var (cone, guard) in _ticks) { cone(); guard(); }
            }
        }

        static void Flip(bool toSource)
        {
            LayerState.Set(toSource);
            GameEvents.Flipped?.Invoke(toSource);
        }

        void ResetRoom()
        {
            foreach (var r in _world.GetComponentsInChildren<IResettable>(true)) r.ResetState();
            if (TraceSystem.I) TraceSystem.I.ResetState();
            RoomClock.Time = 0f;
            GameState.Alive = true;
            LayerState.Set(false);
            _killed = null;
            _hit = null;
        }

        static float Flat(Vector3 a, Vector3 b) { a.y = b.y = 0f; return Vector3.Distance(a, b); }

        static Action Bind(object target, string method) =>
            (Action)Delegate.CreateDelegate(typeof(Action), target, target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance));

        // ------------------------------------------------------------------------------------------------ tests

        /// The patrol is a pure function of room time: two frame rates land on the same spots, and on the loop's own maths.
        [UnityTest]
        public IEnumerator PatrolIsDeterministic()
        {
            GameState.DemoMode = true;   // nobody is ever seen: patrol only
            var wps = new[] { new Vector3(0f, 0f, 0f), new Vector3(8f, 0f, 0f), new Vector3(8f, 0f, 6f) };
            var g = Make(wps, 1.5f);
            yield return null;
            float hover = GameConfig.I.droneHover;
            var first = new List<Vector3>();
            foreach (float step in new[] { 1f / 30f, 1f / 144f })
            {
                ResetRoom();
                var got = new List<Vector3>();
                int perSec = Mathf.RoundToInt(1f / step);
                for (int k = 1; k <= 9 * perSec; k++)
                {
                    RoomClock.Time = (float)k / perSec;
                    foreach (var (cone, guard) in _ticks) { cone(); guard(); }
                    if (k % perSec == 0) got.Add(g.transform.position);
                }
                for (int s = 0; s < got.Count; s++)
                {
                    Vector3 want = DronePatrol.Evaluate(wps, (1.5f + s + 1) * Guard.PatrolSpeed) - Vector3.up * hover;
                    Assert.Less(Vector3.Distance(got[s], want), 0.01f, $"t={s + 1}s at {step}s steps: on the loop");
                    if (first.Count > s) Assert.Less(Vector3.Distance(got[s], first[s]), 0.005f, $"t={s + 1}s: 30 vs 144 Hz");
                }
                if (first.Count == 0) first.AddRange(got);
            }
            Assert.AreEqual(Alert.Patrol, g.Alert);
            Assert.IsNull(_killed);
        }

        /// Seen -> "?" (0.4 s) -> "!" chase -> wind-up -> the strike kills (DeathCause.Guard, GUARD-1). The same wind-up interrupted
        /// by a flip to SOURCE freezes him; stepping out of reach and flipping back makes the swing miss.
        [UnityTest]
        public IEnumerator SpotChaseStrikeKillsOnlyInRender()
        {
            var g = Make(new[] { Vector3.zero });
            _player.transform.position = new Vector3(0f, 0f, 7f);
            yield return null;

            Tick(0.2f);
            Assert.AreEqual(Alert.Suspicious, g.Alert, "seen: suspicious first");
            Assert.IsTrue(g.markQ.activeSelf, "\"?\" over his head");
            Assert.IsFalse(g.IsChasing);
            Tick(0.3f);
            Assert.AreEqual(Alert.Hunting, g.Alert, "0.4 s later: the chase");
            Assert.IsTrue(g.markX.activeSelf, "\"!\" over his head");
            Assert.AreEqual(1, _alerts);
            Assert.AreEqual(1, _chaseOn);
            Assert.AreEqual(1, Guard.Chasing);

            Tick(4f, () => _killed != null);
            Assert.AreEqual(1, _windups, "telegraphed");
            Assert.AreEqual(true, _hit);
            Assert.AreEqual(DeathCause.Guard, _killed);
            Assert.AreEqual("GUARD-1", PlayerRef.LastKillerId);
            Assert.Less(Flat(g.transform.position, _player.transform.position), Guard.HitRange + 0.01f);

            // again, but flip to SOURCE inside the wind-up
            ResetRoom();
            _windups = 0;
            _player.transform.position = new Vector3(0f, 0f, 7f);
            Tick(4f, () => _windups > 0);
            Assert.AreEqual(1, _windups);
            Flip(true);
            Vector3 frozenAt = g.transform.position;
            Tick(3f);
            Assert.IsNull(_killed, "SOURCE: no strike");
            Assert.IsTrue(g.Frozen);
            Assert.AreEqual(frozenAt, g.transform.position, "frozen mid-pose");
            Assert.IsFalse(g.body.enabled, "the statue lets the player through");
            Assert.IsFalse(g.cone.Active, "and sees nothing");
            _player.transform.position = frozenAt + (_player.transform.position - frozenAt).normalized * 3f;   // out of reach
            Flip(false);
            Tick(0.5f, () => _hit != null);
            Assert.AreEqual(false, _hit, "back in RENDER he finishes the swing, and misses");
            Assert.IsNull(_killed);
            Assert.IsFalse(g.Frozen);
            Assert.IsTrue(g.body.enabled);
        }

        /// Chased, the player flips to SOURCE, phases through a wall and comes back out of sight: he resumes, cannot see or
        /// follow through the wall, searches the last-known position, and goes back to his post.
        [UnityTest]
        public IEnumerator FlipToSourceEscapesAndFreezesHim()
        {
            Wall(new Vector3(0f, 1.5f, 10f), new Vector3(16f, 3f, 0.4f));
            var g = Make(new[] { Vector3.zero });
            _player.transform.position = new Vector3(0f, 0f, 6f);
            yield return null;
            Tick(1f, () => g.IsChasing);
            Assert.IsTrue(g.IsChasing);
            Tick(0.4f);

            Flip(true);
            Vector3 frozenAt = g.transform.position;
            _player.transform.position = new Vector3(0f, 0f, 14f);   // through the wall in SOURCE
            Tick(2f);
            Assert.AreEqual(frozenAt, g.transform.position, "no move in SOURCE");
            Assert.IsTrue(g.IsChasing, "and no timers either: still after you");

            Flip(false);
            Tick(12f, () => g.Alert == Alert.Patrol && Flat(g.transform.position, Vector3.zero) < 0.05f);
            Assert.IsNull(_killed);
            Assert.IsFalse(g.IsChasing);
            Assert.AreEqual(Alert.Patrol, g.Alert, "back at his post");
            Assert.Less(Flat(g.transform.position, Vector3.zero), 0.05f);
            CollectionAssert.IsSubsetOf(new[] { Alert.Suspicious, Alert.Hunting, Alert.Return, Alert.Patrol }, _states);
            Assert.AreEqual(1, _chaseOff, "the HUD pulse ended");
            Assert.AreEqual(0, Guard.Chasing);
        }

        /// REBOOT: every officer back on his loop start, unalarmed, markers off, the chase pulse off.
        [UnityTest]
        public IEnumerator ResetRestoresPatrol()
        {
            var wps = new[] { new Vector3(-4f, 0f, 0f), new Vector3(4f, 0f, 0f) };
            var g = Make(wps);
            Vector3 start = g.transform.position;
            _player.transform.position = new Vector3(6f, 0f, 3f);   // ahead of him as he walks east
            yield return null;
            Tick(3f, () => g.IsChasing);
            Assert.IsTrue(g.IsChasing);
            Tick(0.5f);

            ResetRoom();
            Assert.AreEqual(Alert.Patrol, g.Alert);
            Assert.IsFalse(g.IsChasing);
            Assert.AreEqual(0, Guard.Chasing);
            Assert.AreEqual(1, _chaseOff);
            Assert.IsFalse(g.markQ.activeSelf);
            Assert.IsFalse(g.markX.activeSelf);
            Assert.IsFalse(g.glow.activeSelf);
            Assert.Less(Vector3.Distance(g.transform.position, start), 1e-4f, "back at the loop start");
            GameState.DemoMode = true;
            Tick(1f);
            Vector3 want = DronePatrol.Evaluate(wps, RoomClock.Time * Guard.PatrolSpeed) - Vector3.up * GameConfig.I.droneHover;
            Assert.Less(Vector3.Distance(g.transform.position, want), 0.01f, "and patrolling from room time 0");
        }

        /// A Noise decoy within earshot pulls a patrolling officer to it; when it ends he goes back to his loop.
        [UnityTest]
        public IEnumerator NoiseDecoyDistractsHim()
        {
            GameState.DemoMode = true;   // he never sees the player here
            var g = Make(new[] { Vector3.zero, new Vector3(0f, 0f, -6f) });
            _player.transform.position = new Vector3(30f, 0f, 30f);
            yield return null;
            Tick(0.5f);
            var noise = new Vector3(7f, 0f, 3f);
            Assert.Greater(Decoys.Spawn(noise, 4f, DecoyKind.Noise), 0);
            Tick(3.5f);
            Assert.AreEqual(Alert.Hunting, g.Alert, "investigating");
            Assert.Less(Flat(g.transform.position, noise), 0.6f, "went to the noise");
            Tick(1f);
            Assert.AreEqual(Alert.Return, g.Alert, "the noise ended");
            Tick(8f, () => g.Alert == Alert.Patrol);
            Assert.AreEqual(Alert.Patrol, g.Alert);
            Assert.IsNull(_killed);
        }

        /// He is a person: a wall between him and what he wants stops him; he never comes out the other side.
        [UnityTest]
        public IEnumerator WallsBlockThem()
        {
            GameState.DemoMode = true;
            Wall(new Vector3(0f, 1.5f, 3f), new Vector3(10f, 3f, 0.4f));
            var g = Make(new[] { Vector3.zero }, 0f, 90f);   // posted facing east
            _player.transform.position = new Vector3(30f, 0f, 30f);
            yield return null;
            Decoys.Spawn(new Vector3(0f, 0f, 6f), 6f, DecoyKind.Noise);   // heard through the wall
            float maxZ = -1f;
            for (int i = 0; i < 300; i++)
            {
                Tick(Step);
                maxZ = Mathf.Max(maxZ, g.transform.position.z);
            }
            Assert.AreEqual(Alert.Hunting, g.Alert, "he did hear it");
            Assert.Greater(maxZ, 1.5f, "walked up to the wall");
            Assert.Less(maxZ, 3f - 0.2f - Guard.Radius + 0.05f, "and stopped at it");

            // chasing: the player is seen, then walks north past the wall's end out of sight; he follows, never through it
            Decoys.ClearAll();
            ResetRoom();
            GameState.DemoMode = false;
            _player.transform.position = new Vector3(6.5f, 0f, 1f);
            Tick(1f, () => g.IsChasing);
            Assert.IsTrue(g.IsChasing);
            for (float t = 0f; t < 1f; t += Step)   // the player walks round the wall's end at 4 m/s, in RENDER
            {
                Vector3 p = _player.transform.position;
                _player.transform.position = p.z < 6f ? p + (p.x < 6.5f ? Vector3.right : Vector3.forward) * 4f * Step : p;
                Tick(Step);
            }
            for (int i = 0; i < 180 && _killed == null; i++)
            {
                Tick(Step);
                var q = g.transform.position;
                Assert.IsFalse(Mathf.Abs(q.x) < 5f && q.z > 2.5f && q.z < 3.5f, $"inside the wall at {q}");
            }
        }

        /// Three officers chasing at once allocate nothing per frame: 300 ticks (900 officer updates and their cones) in one frame
        /// add no more than the frame-to-frame noise of idle test frames (1 KB), where a single small allocation per update would
        /// add over 20 KB (best of three such frames). GC.GetAllocatedBytesForCurrentThread reads 0 under Unity's Mono, so the
        /// profiler's per-frame GC counter measures it.
        [UnityTest]
        public IEnumerator ThreeChasingAllocateNothing()
        {
            var gs = new[]
            {
                Make(new[] { new Vector3(-3f, 0f, 0f) }), Make(new[] { new Vector3(0f, 0f, 0f) }), Make(new[] { new Vector3(3f, 0f, 0f) }),
            };
            _player.transform.position = new Vector3(0f, 0f, 7f);
            yield return null;
            Tick(1f, () => gs[0].IsChasing && gs[1].IsChasing && gs[2].IsChasing);
            foreach (var g in gs) Assert.IsTrue(g.IsChasing);
            Assert.AreEqual(3, Guard.Chasing);
            void Burst(int n)
            {
                for (int i = 0; i < n; i++)
                {
                    _player.transform.position += Vector3.forward * (Guard.ChaseSpeed * Step);   // keeping their pace: the chase goes on
                    Tick(Step);
                }
            }
            Burst(30);   // warm-up: first calls, lazy strings
            var quiet = new List<MonoBehaviour>();   // the self-booting managers (UI, run, audio) are not ours
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude))
            {
                string ns = mb.GetType().Namespace ?? "";
                if (ns.StartsWith("Desync") && !ns.StartsWith("Desync.Enemies") && mb.enabled) { mb.enabled = false; quiet.Add(mb); }
            }
            try
            {
                using (var rec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame"))
                {
                    long idle = 0;
                    for (int i = 0; i < 22; i++)
                    {
                        yield return null;
                        if (i >= 2) idle = Math.Max(idle, rec.LastValue);
                    }
                    var junk = new byte[8][];   // instrument check: 128 KB shows in its frame
                    for (int i = 0; i < junk.Length; i++) junk[i] = new byte[16384];
                    yield return null;
                    Assert.Greater(rec.LastValue, idle + 65536, "the GC counter sees allocations");
                    GC.KeepAlive(junk);
                    // best of three bursts: an allocation per update shows in every one, the editor's occasional spike does not
                    long burst = long.MaxValue;
                    for (int k = 0; k < 3; k++)
                    {
                        Burst(300);
                        yield return null;
                        burst = Math.Min(burst, rec.LastValue);
                    }
                    Debug.Log($"[DESYNC] guards GC: idle frame max {idle} B, best burst frame (300 ticks x 3 officers) {burst} B");
                    Assert.IsTrue(rec.Valid, "GC counter unavailable");
                    Assert.LessOrEqual(burst, idle + 1024, "officer updates allocate");
                }
            }
            finally { foreach (var mb in quiet) if (mb) mb.enabled = true; }
            foreach (var g in gs) Assert.IsTrue(g.IsChasing, "all three still chasing");
            Assert.AreEqual(3, Guard.Chasing);
            foreach (var g in gs) Assert.IsFalse(g.Dormant);
            Assert.IsNull(_killed);
        }
    }
}
