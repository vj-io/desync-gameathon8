using System;
using System.Collections;
using Desync.Rules;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Desync.QA
{
    /// Results flow: ExitReached -> RoomReportReady (fields consistent with Rules.Medals / Rules.Scoring) -> wait for the player
    /// -> RestartRequested retries in place, ContinueRequested loads the next room. No auto-advance.
    public class RoomReportTests : QaFixture
    {
        [Serializable] class RoomRow { public int index; public float tDev; public float parScore; }
        [Serializable] class RoomTable { public RoomRow[] rooms; }

        /// Row of Resources/Content/rooms.json, or null while the file is not there.
        static RoomRow Row(int room)
        {
            var ta = Resources.Load<TextAsset>("Content/rooms");
            if (!ta) return null;
            var t = JsonUtility.FromJson<RoomTable>(ta.text);
            if (t?.rooms != null) foreach (var r in t.rooms) if (r.index == room) return r;
            return null;
        }

        /// One perfect hack opens the exit; the room clock is pinned to `roomTime`, then the exit is reached.
        IEnumerator ClearRoom(float roomTime)
        {
            GameEvents.HackFinished?.Invoke(true);
            GameEvents.AccessGranted?.Invoke("MEMO");
            Assert.IsTrue(GameState.ExitOpen, "the only terminal done must open the exit");
            RoomClock.Time = roomTime;
            int n = reports.Count;
            GameEvents.ExitReached?.Invoke();
            yield return Until(() => reports.Count > n, 3f, "RoomReportReady after ExitReached");
        }

        IEnumerator Retry()
        {
            int n = resets;
            GameEvents.RestartRequested?.Invoke();
            yield return Until(() => resets > n, 1f, "RoomReset after RestartRequested");
        }

        [UnityTest]
        public IEnumerator Report_FieldsAgreeWithMedalsAndScoring()
        {
            float tDev = GameState.RoomTDev;
            Assert.Greater(tDev, 0f, "RoomManager must publish GameState.RoomTDev (rooms.json tDev, else the GameConfig fallback)");
            var row = Row(0);
            if (row != null) Assert.AreEqual(row.tDev, tDev, 0.01f, "RoomTDev comes from rooms.json");

            float t = tDev * 1.1f;   // inside the GOLD bracket (<= 1.25 x tDev)
            yield return ClearRoom(t);
            var r = reports[0];

            Assert.AreEqual(0, r.room);
            Assert.AreEqual(GameState.PhaseKind.Results, GameState.Phase);
            Assert.AreEqual(t, r.time, 0.5f, "report.time = room clock at the exit");
            Assert.AreEqual(Medals.For(r.time, tDev), r.medal, "medal = Medals.For(time, tDev)");
            Assert.AreEqual(Medal.Gold, r.medal);
            Assert.AreEqual(Scoring.TimeBonus(r.time, Medals.Threshold(Medal.Gold, tDev)), r.timeBonus, 1f, "time bonus = (gold - t) x 40");
            Assert.Greater(r.timeBonus, 0);
            Assert.AreEqual(r.eventsScore + r.timeBonus, r.total, "total = events + time bonus");
            Assert.GreaterOrEqual(r.eventsScore, Scoring.HackPerfectPts + Scoring.NoTraceRoomPts + Scoring.RoomClearPts, "perfect hack + no-TRACE + room clear");
            Assert.AreEqual(GameState.Score, r.eventsScore, "the HUD's live score is the report's event score");
            Assert.GreaterOrEqual(r.peakMultiplier, 1f);

            if (row != null && row.parScore > 0f) Assert.AreEqual(Scoring.Grade(r.total, Mathf.RoundToInt(row.parScore)), r.grade, "grade = Scoring.Grade(total, parScore)");
            else Assert.GreaterOrEqual("SABC".IndexOf(r.grade), 0, "grade is one of S/A/B/C");

            Assert.IsTrue(r.nextTarget == null || r.nextTarget.Length > 0, "nextTarget is null when maxed, never empty");
            StringAssert.IsMatch(@"^DESYNC // \d{4}-\d{2}-\d{2} // [SABC]( [SABC])* // \d+:\d{2}\.\d{2} // GHOST x\d+ // desync-nine\.vercel\.app$", r.shareText, "share text format");

            Assert.AreEqual(1, cleared.Count, "RoomCleared is still raised (compatibility)");
            Assert.AreEqual(r.grade, cleared[0].grade);
            Assert.AreEqual(r.medal, cleared[0].medal);
            Assert.AreEqual(r.room, cleared[0].room);
        }

        [UnityTest]
        public IEnumerator Report_MedalFollowsTheDevTimeBrackets()
        {
            float tDev = GameState.RoomTDev;
            Assert.Greater(tDev, 0f);
            var cases = new[] { (0.5f, Medal.Sync), (1.1f, Medal.Gold), (1.4f, Medal.Silver), (2.0f, Medal.Bronze) };
            for (int i = 0; i < cases.Length; i++)
            {
                yield return ClearRoom(tDev * cases[i].Item1);
                Assert.AreEqual(cases[i].Item2, reports[i].medal, $"{cases[i].Item1} x tDev");
                Assert.AreEqual(Medals.For(reports[i].time, tDev), reports[i].medal);
                if (i < cases.Length - 1) yield return Retry();
            }
        }

        [UnityTest]
        public IEnumerator Retry_ResetsTheRoomInPlace_Under300Ms()
        {
            yield return ClearRoom(10f);
            GameState.LastRestartMs = -1f;
            yield return Retry();

            Assert.AreEqual(scene, SceneManager.GetActiveScene(), "retry resets in place, no scene load");
            Assert.AreEqual(GameState.PhaseKind.Playing, GameState.Phase);
            Assert.IsTrue(GameState.Alive);
            Assert.IsFalse(GameState.Cinematic, "control returned");
            Assert.AreEqual(1f, Time.timeScale);
            Assert.IsFalse(GameState.ExitOpen);
            Assert.AreEqual(0, GameState.Score);
            Assert.AreEqual(0, GameState.Flips);
            Assert.Less(RoomClock.Time, 0.5f, "room clock restarted");
            Assert.Less(Vector3.Distance(player.transform.position, Vector3.zero), 0.1f, "player back at spawn");
            Assert.Greater(GameState.LastRestartMs, 0f, "Retry measures GameState.LastRestartMs too");
            Assert.Less(GameState.LastRestartMs, 300f);
        }

        [UnityTest]
        public IEnumerator Results_WaitForTheKey_NoAutoAdvance()
        {
            yield return ClearRoom(10f);
            float end = Time.realtimeSinceStartup + 3.5f;   // the old grade card advanced after 2.5 s
            while (Time.realtimeSinceStartup < end) yield return null;
            Assert.AreEqual(scene, SceneManager.GetActiveScene(), "still in the cleared room");
            Assert.AreEqual(GameState.PhaseKind.Results, GameState.Phase, "results stay up until R / SPACE");
        }

        [UnityTest]
        public IEnumerator Continue_LoadsTheNextRoom()
        {
            yield return ClearRoom(10f);
            GameEvents.ContinueRequested?.Invoke();
            yield return Until(() => SceneManager.GetActiveScene().name == SceneFlow.Room(1), 10f, "Room_02 to load after Continue");
            yield return Frames(2);
            Assert.AreEqual(1, GameState.RoomIndex, "RoomManager is running room 2");
            Assert.AreEqual(GameState.PhaseKind.Playing, GameState.Phase);
        }
    }
}
