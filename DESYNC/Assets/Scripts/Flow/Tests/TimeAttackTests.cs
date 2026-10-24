using System;
using System.Collections.Generic;
using System.Diagnostics;
using Desync.Gen;
using Desync.Player;
using Desync.Rules;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Desync.Flow.Tests
{
    /// Time-Attack rules: the 42 room ids, fixed-seed generation and dev times, the ghost recording and its stored form,
    /// medal / best / ghost records and the save-size bound.
    public class TimeAttackTests
    {
        static ChunkLibrary _lib;
        static ChunkLibrary Lib => _lib ??= ChunkLibrary.FromFolder("Assets/Resources/Content/chunks");

        // ---------- rooms ----------

        [Test]
        public void Rooms_Are_42_With_Stable_Ids()
        {
            var all = TimeAttackRooms.All;
            Assert.AreEqual(42, all.Count);
            var ids = new HashSet<string>();
            foreach (var r in all) Assert.IsTrue(ids.Add(r.id), "duplicate id " + r.id);
            for (int i = 0; i < 6; i++)
            {
                Assert.AreEqual("r" + (i + 1), all[i].id);
                Assert.IsTrue(all[i].Classic);
                Assert.AreEqual(i, all[i].classic);
            }
            Assert.AreEqual("ta_a1_01", all[6].id);
            Assert.AreEqual("ta_a1_12", all[17].id);
            Assert.AreEqual("ta_a3_12", all[41].id);
            Assert.AreEqual(3, all[41].act);
            Assert.AreEqual(3, all[41].Node, "rooms 10-12 of an act are node 3");
            Assert.AreEqual(0, all[6].Node);
            Assert.AreSame(all[20], TimeAttackRooms.Find(all[20].id));
            Assert.IsNull(TimeAttackRooms.Find("ta_a4_01"));
            Assert.AreEqual("A2-05", TimeAttackRooms.Find("ta_a2_05").Label);
            StringAssert.StartsWith("R03 · ", TimeAttackRooms.Find("r3").Label);
        }

        [Test]
        public void Generated_Rooms_Are_Fixed_Seed_Never_Fall_Back_And_Have_Dev_Times()
        {
            var sw = new Stopwatch();
            var table = new System.Text.StringBuilder("[DESYNC] time-attack dev times:");
            double worst = 0;
            foreach (var r in TimeAttackRooms.All)
            {
                if (r.Classic) continue;
                sw.Restart();
                float tDev = TimeAttackRooms.TDev(r);
                worst = Math.Max(worst, sw.Elapsed.TotalMilliseconds);
                var spec = TimeAttackRooms.Spec(r);
                Assert.IsNull(spec.fallbackRoom, r.id + " fell back to a classic room");
                Assert.AreEqual(Rng.HashStr(r.id), spec.seed, r.id + ": seed = FNV-1a of the id");
                Assert.AreEqual(r.act, spec.act);
                Assert.AreEqual(r.Node, spec.node);
                var again = Generator.GenerateRoom(Rng.HashStr(r.id), r.act, r.Node, 0, Lib);
                Assert.AreEqual(TimeAttackRooms.Layout(spec), TimeAttackRooms.Layout(again), r.id + ": same id, same room");
                var v = Generator.Validate(spec);
                Assert.Greater(v.bestPathSeconds, 0f, r.id);
                Assert.AreEqual(TimeAttackRooms.DevTime(v.bestPathSeconds), tDev, 1e-4f, r.id);
                Assert.AreEqual(0f, tDev * 2f % 1f, 1e-4f, r.id + ": dev time in 0.5 s steps");
                Assert.AreSame(spec, TimeAttackRooms.Spec(r), "cached");
                table.Append(' ').Append(r.id.Substring(3)).Append('=').Append(tDev.ToString("0.0"));
            }
            Debug.Log(table + " (worst first call " + worst.ToString("0.0") + " ms)");
            Assert.AreEqual(10f, TimeAttackRooms.TDev(TimeAttackRooms.Find("r1")), 1e-4f, "classic rooms read rooms.json");
        }

        [Test]
        public void Dev_Time_Is_Best_Path_Times_1_3_In_Half_Seconds()
        {
            // D-010's classic numbers: fastest 7.5, 17.2, 17.5, 29.9, 26.8, 20.5 -> 10.0, 22.5, 23.0, 39.0, 35.0, 26.5
            float[] fastest = { 7.5f, 17.2f, 17.5f, 29.9f, 26.8f, 20.5f }, dev = { 10f, 22.5f, 23f, 39f, 35f, 26.5f };
            for (int i = 0; i < fastest.Length; i++) Assert.AreEqual(dev[i], TimeAttackRooms.DevTime(fastest[i]), 1e-4f, "fastest " + fastest[i]);
        }

        [Test]
        public void Layout_Hash_Follows_The_Tiles()
        {
            var a = new RoomSpec { tiles = new[] { "....", ".##." } };
            var b = new RoomSpec { tiles = new[] { "....", ".#.." } };
            Assert.AreNotEqual(TimeAttackRooms.Layout(a), TimeAttackRooms.Layout(b));
            Assert.AreEqual(TimeAttackRooms.Layout(a), TimeAttackRooms.Layout(new RoomSpec { tiles = new[] { "....", ".##." } }));
        }

        // ---------- ghost track ----------

        /// A run at 5 m/s around a 6 m circle, turning, flipping layer every 2 s.
        static GhostTrack Run(int samples, int capacity = GhostTrack.RecordCapacity)
        {
            var t = new GhostTrack(capacity);
            for (int i = 0; i < samples; i++)
            {
                float s = i * GhostTrack.Step, a = s * 5f / 6f;
                t.Add(new Vector3(10f + 6f * Mathf.Cos(a), 0.08f, -12f + 6f * Mathf.Sin(a)), a * Mathf.Rad2Deg + 90f, (int)(s / 2f) % 2 == 1);
            }
            return t;
        }

        /// Compares the two a quarter of the way into every stored interval (away from sample boundaries, where float noise
        /// could pick the neighbouring sample's layer).
        static void AssertClose(GhostTrack src, GhostTrack got, float posTol, string what)
        {
            for (int k = 0; k < got.Count - 1; k++)
            {
                float t = (k + 0.25f) * got.Interval;
                Assert.IsTrue(src.Sample(t, out var p0, out float y0, out bool l0), what);
                Assert.IsTrue(got.Sample(t, out var p1, out float y1, out bool l1), what);
                Assert.Less(Vector2.Distance(new Vector2(p0.x, p0.z), new Vector2(p1.x, p1.z)), posTol, what + " position at " + t);
                Assert.AreEqual(p0.y, p1.y, 1e-4f, what + " height");
                Assert.Less(Mathf.Abs(Mathf.DeltaAngle(y0, y1)), 360f / 32f / 2f + 0.01f, what + " yaw at " + t);
                Assert.AreEqual(l0, l1, what + " layer at " + t);
            }
        }

        [Test]
        public void Ghost_Round_Trips_At_20_Hz_Within_A_Centimetre()
        {
            var src = Run(800);   // 40 s
            string text = src.Encode(0xABCD1234u, 39.95f);
            var got = new GhostTrack(GhostTrack.MaxStored);
            Assert.IsTrue(got.Decode(text, 0xABCD1234u));
            Assert.AreEqual(800, got.Count);
            Assert.AreEqual(GhostTrack.Step, got.Interval, 1e-6f, "20 Hz kept");
            Assert.AreEqual(39.95f, got.RunTime, 1e-4f);
            AssertClose(src, got, 0.015f, "20 Hz");   // <= 1 cm per axis
            Assert.LessOrEqual(text.Length, (24 + 2 * 800 + 2) / 3 * 4, "2 bytes a sample + header, base64");
        }

        [Test]
        public void Long_Runs_Keep_Every_Nth_Sample_And_Stay_Under_The_Size_Cap()
        {
            var src = Run(3000);   // 150 s
            string text = src.Encode(7u, 150f);
            var got = new GhostTrack(GhostTrack.MaxStored);
            Assert.IsTrue(got.Decode(text, 7u));
            Assert.AreEqual(1000, got.Count, "stride 3");
            Assert.AreEqual(3f * GhostTrack.Step, got.Interval, 1e-6f);
            AssertClose(src, got, 0.055f, "stride 3");   // <= 3 cm per axis + the coarser chord

            var max = Run(GhostTrack.RecordCapacity);   // 10 minutes: the most one attempt records
            string big = max.Encode(7u, 600f);
            Assert.LessOrEqual(big.Length, (24 + 2 * GhostTrack.MaxStored + 2) / 3 * 4, "one ghost never exceeds ~3.2 KB of text");
            Assert.IsTrue(got.Decode(big, 7u));
            Assert.LessOrEqual(got.Count, GhostTrack.MaxStored);
        }

        [Test]
        public void Ghost_Catches_Up_After_A_Jump_And_Refuses_Other_Layouts_And_Junk()
        {
            var src = new GhostTrack(64);
            for (int i = 0; i < 40; i++) src.Add(new Vector3(i < 10 ? 0f : 3f, 0f, 0f), 0f, false);   // a 3 m jump at sample 10
            var got = new GhostTrack(64);
            Assert.IsTrue(got.Decode(src.Encode(1u, 2f), 1u));
            got.Sample(39 * GhostTrack.Step, out var end, out _, out _);
            Assert.AreEqual(3f, end.x, 0.011f, "caught up within the run (0.3 m a sample)");

            Assert.IsFalse(got.Decode(src.Encode(1u, 2f), 2u), "another layout");
            Assert.AreEqual(0, got.Count);
            Assert.IsFalse(got.Decode(null, 1u));
            Assert.IsFalse(got.Decode("not base64 !!", 1u));
            Assert.IsFalse(got.Decode(Convert.ToBase64String(new byte[10]), 1u), "too short");
            var small = new GhostTrack(8);
            Assert.IsFalse(small.Decode(src.Encode(1u, 2f), 1u), "more samples than the track holds");
            Assert.IsNull(new GhostTrack(4).Encode(1u, 1f), "nothing recorded");
        }

        [Test]
        public void Ghost_Record_Full_Buffer_Stops_And_Sample_Holds_The_Last_Point()
        {
            var t = new GhostTrack(3);
            Assert.IsTrue(t.Add(Vector3.zero, 0f, false));
            Assert.IsTrue(t.Add(Vector3.right, 90f, true));
            Assert.IsTrue(t.Add(Vector3.right * 2f, 180f, true));
            Assert.IsFalse(t.Add(Vector3.right * 3f, 0f, false), "full");
            Assert.AreEqual(2f * GhostTrack.Step, t.Duration, 1e-6f);
            t.Sample(0.5f * GhostTrack.Step, out var mid, out float yaw, out bool src);
            Assert.AreEqual(0.5f, mid.x, 1e-5f); Assert.AreEqual(45f, yaw, 1e-3f); Assert.IsFalse(src, "layer of the sample before t");
            t.Sample(99f, out var end, out _, out bool srcEnd);
            Assert.AreEqual(2f, end.x, 1e-5f); Assert.IsTrue(srcEnd);
            t.Clear();
            Assert.IsFalse(t.Sample(0f, out _, out _, out _));
        }

        [Test]
        public void Recording_And_Playback_Allocate_Nothing()
        {
            var rec = new GhostTrack(GhostTrack.RecordCapacity);
            var play = new GhostTrack(GhostTrack.MaxStored);
            Assert.IsTrue(play.Decode(Run(600).Encode(3u, 30f), 3u));
            TestDelegate run = () =>
            {
                rec.Clear();
                for (int i = 0; i < 2000; i++)
                {
                    rec.Add(new Vector3(i, 0f, -i), i, (i & 1) == 0);
                    play.Sample(i * 0.013f, out _, out _, out _);
                }
            };
            run();   // warm up
            Assert.That(() => { _sink = new byte[64]; }, Is.AllocatingGCMemory(), "the instrument sees a known allocation");
            Assert.That(run, Is.Not.AllocatingGCMemory());
        }

        static object _sink;

        [Test]
        public void Debug_Commands_Open_Time_Attack_And_Endless_Overclock()
        {
            var go = new GameObject("DebugBridgeTest");
            var bridge = go.AddComponent<DebugBridge>();
            bool ta = TimeAttackRooms.DebugOpen;
            try
            {
                var save = SaveMigrator.Default();
                bridge.Cmd("ta off");
                Assert.IsFalse(TimeAttackRooms.Unlocked(save));
                bridge.Cmd("ta");
                Assert.IsTrue(TimeAttackRooms.Unlocked(save), "ta = ta on");
                bridge.Cmd("overclock on");
                Assert.IsTrue(OverclockRun.Unlocked(save), "no DESYNC ending needed");
                bridge.Cmd("overclock off");
                Assert.IsFalse(OverclockRun.Unlocked(save));
            }
            finally { TimeAttackRooms.DebugOpen = ta; OverclockRun.DebugUnlock = false; UnityEngine.Object.DestroyImmediate(go); }
        }

        // ---------- records ----------

        static RoomReport Rep(float time, Medal m, int total = 3000, char grade = 'A') => new RoomReport { room = -1, time = time, medal = m, total = total, grade = grade };

        [Test]
        public void Generated_Room_Records_Keep_The_Best_And_One_Medal_Key()
        {
            var save = SaveMigrator.Default();
            var r = TimeAttackRooms.Find("ta_a1_07");
            TimeAttackRooms.Record(save, r, Rep(40f, Medal.Silver, 3000, 'A'));
            Assert.AreEqual(Medal.Silver, TimeAttackRooms.MedalOf(save, r));
            CollectionAssert.Contains(save.unlocks, "ta_a1_07_silver");
            TimeAttackRooms.Record(save, r, Rep(45f, Medal.Bronze, 3500, 'S'));   // slower, lower medal, better score and grade
            Assert.AreEqual(40f, TimeAttackRooms.BestTime(save, r));
            Assert.AreEqual(3500, save.best["ta_a1_07"].score);
            Assert.AreEqual("S", save.best["ta_a1_07"].grade);
            Assert.AreEqual(Medal.Silver, TimeAttackRooms.MedalOf(save, r));
            TimeAttackRooms.Record(save, r, Rep(30f, Medal.Sync));
            Assert.AreEqual(Medal.Sync, TimeAttackRooms.MedalOf(save, r));
            CollectionAssert.DoesNotContain(save.unlocks, "ta_a1_07_silver", "an upgrade replaces the key");
            CollectionAssert.Contains(save.unlocks, "ta_a1_07_sync");
            Assert.AreEqual(1, save.unlocks.FindAll(k => k.StartsWith("ta_a1_07_")).Count);
            Assert.AreEqual(30f, TimeAttackRooms.BestTime(save, r));
            Assert.IsFalse(save.medals.ContainsKey("ta_a1_07"), "generated medals live in unlocks, not medals");
            Assert.AreEqual(1, TimeAttackRooms.SyncCount(save));

            var classic = TimeAttackRooms.Find("r2");
            TimeAttackRooms.Record(save, classic, Rep(20f, Medal.Gold));
            Assert.IsFalse(save.best.ContainsKey("r2"), "classic rooms are recorded by NetHost (SaveService.RecordRoom)");
            save.medals["r2"] = "G";
            Assert.AreEqual(Medal.Gold, TimeAttackRooms.MedalOf(save, classic));
            save.medals["r2"] = "SYNC";
            Assert.AreEqual(Medal.Sync, TimeAttackRooms.MedalOf(save, classic));
            Assert.AreEqual(1, TimeAttackRooms.SyncCount(save), "SYNC count is over the 36 generated rooms");
        }

        [Test]
        public void Locked_Without_The_Meta_Unlock_Except_In_Debug()
        {
            var save = SaveMigrator.Default();
            Assert.IsFalse(TimeAttackRooms.Unlocked(save, false));
            Assert.IsTrue(TimeAttackRooms.Unlocked(save, true));
            save.unlocks.Add(TimeAttackRooms.UnlockKey);
            Assert.IsTrue(TimeAttackRooms.Unlocked(save, false));
            Assert.AreEqual("unlock_timeattack", TimeAttackRooms.UnlockKey);
        }

        [Test]
        public void Ghosts_Are_Kept_For_The_Ten_Most_Recent_Rooms()
        {
            var save = SaveMigrator.Default();
            for (int i = 1; i <= 12; i++) TimeAttackRooms.StoreGhost(save, "ta_a1_" + i.ToString("00"), "g" + i);
            Assert.IsNull(TimeAttackRooms.LoadGhost(save, "ta_a1_01"), "oldest dropped");
            Assert.IsNull(TimeAttackRooms.LoadGhost(save, "ta_a1_02"));
            Assert.AreEqual("g3", TimeAttackRooms.LoadGhost(save, "ta_a1_03"));
            TimeAttackRooms.StoreGhost(save, "ta_a1_03", "g3b");   // improved again: now the most recent
            TimeAttackRooms.StoreGhost(save, "r1", "c1");
            Assert.AreEqual("g3b", TimeAttackRooms.LoadGhost(save, "ta_a1_03"));
            Assert.IsNull(TimeAttackRooms.LoadGhost(save, "ta_a1_04"), "the oldest is now 04");
            int n = 0;
            foreach (var k in save.settings.Keys) if (k.StartsWith(TimeAttackRooms.GhostPrefix)) n++;
            Assert.AreEqual(TimeAttackRooms.MaxGhosts, n);
            StringAssert.EndsWith("ta_a1_03,r1", save.settings[TimeAttackRooms.GhostOrderKey]);
        }

        [Test]
        public void Fully_Played_Time_Attack_Save_Stays_Under_64_KB()
        {
            // SAVE-05's full profile + every Time-Attack record: 36 SYNC keys, 42 bests, the largest possible ghost in 10 rooms.
            var s = SaveMigrator.Default();
            s.playerId = Guid.NewGuid().ToString();
            for (int i = 0; i < 58; i++) s.unlocks.Add("exploit_" + i);
            for (int i = 0; i < 30; i++) s.shards.Add("shard_" + i);
            for (int i = 0; i < 42; i++) { s.medals["room_" + i] = "SYNC"; s.best["room_" + i] = new RoomBest { time = 41.27f, score = 12345, grade = "S" }; }
            for (int i = 0; i < 365; i++) s.dailyBest[new DateTime(2026, 1, 1).AddDays(i).ToString("yyyy-MM-dd")] = 99999;
            var big = Run(GhostTrack.RecordCapacity).Encode(0xFFFFFFFFu, 599.99f);
            foreach (var r in TimeAttackRooms.All)
            {
                s.best[r.id] = new RoomBest { time = 599.99f, score = 99999, grade = "S" };
                if (!r.Classic) TimeAttackRooms.Record(s, r, Rep(1f, Medal.Sync));
                TimeAttackRooms.StoreGhost(s, r.id, big);
            }
            int size = SaveMigrator.Serialize(s).Length;
            Debug.Log("[DESYNC] fully played time-attack save: " + size + " chars");
            Assert.Less(size, 65536);
            Assert.AreEqual(36, TimeAttackRooms.SyncCount(s));
        }
    }
}
