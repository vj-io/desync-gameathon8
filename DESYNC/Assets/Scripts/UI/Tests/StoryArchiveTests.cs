using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Desync.Enemies;
using Desync.Flow;
using Desync.Net;
using Desync.TestKit;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Desync.UI.Tests
{
    /// The shard ARCHIVE, the collect toast and the three ARCHITECT ending sequences (played before the room's results, with
    /// camera motion, layer flips and, for Desync, both layers at once; everything put back after). A throwaway save profile.
    public class StoryArchiveTests
    {
        const string SaveKey = "desync.test.archive";
        static readonly int DesyncId = Shader.PropertyToID("_Desync");
        GameObject _camGo, _head;
        readonly List<MonoBehaviour> _paused = new List<MonoBehaviour>();   // switched off in Up, back on in Down

        static T Screen<T>(string field) where T : class =>
            typeof(UIManager).GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(UIManager.I) as T;

        static void Click(string path)
        {
            var go = GameObject.Find(path);
            Assert.IsTrue(go && go.activeInHierarchy, "not found / not visible: " + path);
            ExecuteEvents.Execute(go, new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerDownHandler);
        }

        static RoomReport Report() => new RoomReport { room = -1, eventsScore = 1800, peakMultiplier = 2f, timeBonus = 300, total = 2100, time = 60f, medal = Rules.Medal.Bronze, grade = 'A', shareText = "x" };

        [UnitySetUp]
        public IEnumerator Up()
        {
            _camGo = new GameObject("ArchiveTestCam", typeof(Camera));
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
                if (mb && mb.enabled && mb.GetType().Name == "GameManager") { mb.enabled = false; _paused.Add(mb); }
            SaveService.Unload(SaveKey); PlayerPrefs.DeleteKey(SaveKey);
            LayerState.Set(false); Shader.SetGlobalFloat(DesyncId, 0f);
            yield return null;
            Assert.IsNotNull(UIManager.I, "UIManager did not self-bootstrap");
        }

        [TearDown]
        public void Down()
        {
            if (UIManager.I) UIManager.I.SetMode(UIManager.Mode.None, true);
            PlayerRef.Head = null;
            if (_head) Object.Destroy(_head);
            if (_camGo) Object.Destroy(_camGo);
            LayerState.Set(false); Shader.SetGlobalFloat(DesyncId, 0f);
            PlayerPrefs.DeleteKey(SaveKey);
            SaveService.Unload();
            foreach (var mb in _paused) if (mb) mb.enabled = true;
            _paused.Clear();
        }

        [UnityTest]
        public IEnumerator Archive_ShowsShardsByAct_TheReader_EndingsSeen_AndWhatTheTrueEndingNeeds()
        {
            var save = SaveService.Data;
            foreach (var id in new[] { "shard_a1_01", "shard_a1_02", "shard_a2_01" }) save.unlocks.Add(id);
            save.unlocks.Add("ending_exposed");
            save.settings[Endings.KillsKey] = "2";
            UIManager.I.SetMode(UIManager.Mode.Title, true);
            UIManager.I.OpenArchive();
            var archive = Screen<ShardArchive>("_archive");
            Assert.IsTrue(archive.IsOn);
            Assert.AreEqual(3, archive.Collected);
            Assert.AreEqual(0, archive.Focus, "focus on the first shard found");
            Assert.AreEqual(ShardBook.Content.Find("shard_a1_01").title, archive.ReaderTitle);
            StringAssert.Contains("2 / 3", archive.NeedText);
            StringAssert.Contains("3 / 30", archive.NeedText);
            Assert.AreEqual("SEEN", GameObject.Find("DesyncUI/Canvas/ShardArchive/Panel/Ending0/State").GetComponent<TMPro.TMP_Text>().text);
            Assert.AreEqual("NOT YET", GameObject.Find("DesyncUI/Canvas/ShardArchive/Panel/Ending2/State").GetComponent<TMPro.TMP_Text>().text);
            Click("DesyncUI/Canvas/ShardArchive/Panel/Slot10");   // act 2, shard 1
            Assert.AreEqual(10, archive.Focus);
            Assert.AreEqual(ShardBook.Content.Find("shard_a2_01").title, archive.ReaderTitle);
            Click("DesyncUI/Canvas/ShardArchive/Panel/Slot5");    // not found yet
            Assert.AreEqual("NOT FOUND YET", archive.ReaderTitle);
            yield return null;
            Click("DesyncUI/Canvas/ShardArchive/Panel/Back");
            Assert.IsFalse(archive.IsOn, "BACK closes");
        }

        [UnityTest]
        public IEnumerator Toast_OnCollect_ThenGone()
        {
            SaveService.Data.unlocks.Add("shard_a3_04");
            UIManager.I.SetMode(UIManager.Mode.Room, true);
            ShardEvents.Collected?.Invoke("shard_a3_04");
            var toast = Screen<ShardToast>("_toast");
            Assert.IsTrue(toast.IsOn);
            Assert.AreEqual(ShardBook.Content.Find("shard_a3_04").title, GameObject.Find("DesyncUI/Canvas/ShardToast/Box/Title").GetComponent<TMPro.TMP_Text>().text);
            StringAssert.StartsWith("PUPPET STRING 01 / 30", GameObject.Find("DesyncUI/Canvas/ShardToast/Box/Line").GetComponent<TMPro.TMP_Text>().text);
            for (int i = 0; i < 80 && toast.IsOn; i++) UIManager.I.TickAll(0.05f);
            Assert.IsFalse(toast.IsOn, "a short toast");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Endings_EachPlaysItsOwnSequence_BeforeTheResults_ThenPutsEverythingBack(
            [Values("Exposed", "Merged", "Desync")] string name)
        {
            _head = new GameObject("FakeHead");
            _head.transform.SetPositionAndRotation(new Vector3(1f, 1.6f, 2f), Quaternion.Euler(10f, 30f, 0f));
            PlayerRef.Head = _head.transform;
            var pos0 = _head.transform.position;
            var rot0 = _head.transform.rotation;
            UIManager.I.SetMode(UIManager.Mode.Room, true);
            var seq = Screen<EndingSequence>("_endingSeq");
            var results = Screen<ResultsScreen>("_results");

            BossEvents.Ending?.Invoke(name);
            GameEvents.RoomReportReady?.Invoke(Report());   // the boss raises ExitReached in the same frame
            Assert.IsTrue(seq.IsOn, name + " plays first");
            Assert.IsFalse(results.IsOn, "the results wait for it");
            Assert.AreEqual(name.ToLowerInvariant(), seq.Ending);

            float moved = 0f; bool source = false, split = false;
            for (int i = 0; i < 1200 && !seq.Waking; i++)
            {
                UIManager.I.TickAll(0.05f);
                moved = Mathf.Max(moved, Vector3.Distance(_head.transform.position, pos0));
                source |= LayerState.InSource;
                split |= Shader.GetGlobalFloat(DesyncId) > 0.5f;
            }
            Assert.IsTrue(seq.Waking, "reaches its closing prompt");
            Assert.Greater(moved, 1f, "the camera moves");
            Assert.AreEqual(name != "Merged", source, "Exposed and Desync show SOURCE");
            Assert.AreEqual(name == "Desync", split, "only the true ending shows both layers at once");
            Assert.IsFalse(results.IsOn);

            seq.Finish();
            Assert.IsFalse(seq.IsOn);
            Assert.IsTrue(results.IsOn, "then the room's results");
            Assert.IsFalse(LayerState.InSource);
            Assert.AreEqual(0f, Shader.GetGlobalFloat(DesyncId));
            Assert.AreEqual(pos0, _head.transform.position, "camera put back");
            Assert.AreEqual(rot0.eulerAngles, _head.transform.rotation.eulerAngles);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Endings_AReportWithoutAnEnding_ShowsTheResultsAtOnce()
        {
            UIManager.I.SetMode(UIManager.Mode.Room, true);
            BossEvents.Ending?.Invoke("Exposed");
            yield return null;   // armed in an earlier frame: not this report's ending
            GameEvents.RoomReportReady?.Invoke(Report());
            Assert.IsFalse(Screen<EndingSequence>("_endingSeq").IsOn);
            Assert.IsTrue(Screen<ResultsScreen>("_results").IsOn);
        }

        /// Same reading rule as STORY: every caption types out and stays readable, flips are not closer than a flash gap.
        [Test]
        public void Endings_PacedLikeTheStory()
        {
            foreach (var id in Endings.All)
            {
                var beats = ShardBook.Content.Ending(id).beats;
                float lastFlash = -99f;
                foreach (var b in beats)
                {
                    if ((b.kind == "head" || b.kind == "sys") && b.hold < 90f)
                        Assert.GreaterOrEqual(b.hold, StoryScript.MinHold(b.text, b.kind), id + ": " + b.text);
                    if (b.kind == "layer" || b.kind == "flash")
                    {
                        Assert.GreaterOrEqual(b.t - lastFlash, StoryScript.MinFlashGap - 1e-3f, id + " flash at " + b.t);
                        lastFlash = b.t;
                    }
                }
            }
        }

        [UnityTest]
        public IEnumerator Endings_NoGarbagePerFrame_BetweenCues()
        {
            UIManager.I.SetMode(UIManager.Mode.Room, true);
            var seq = Screen<EndingSequence>("_endingSeq");
            _head = new GameObject("FakeHead");
            PlayerRef.Head = _head.transform;
            Assert.IsTrue(seq.Play("Exposed", null));
            for (int i = 0; i < 12; i++) seq.Tick(0.05f);   // past the first cues (0.0, 0.3): the sys line types, the camera turns
            Assert.Greater(Allocs.Count(() => _sink = new byte[64]), 0, "the instrument sees a known allocation");
            Action tick = () => seq.Tick(0.05f);
            int extra = 0;
            for (int i = 0; i < 30; i++) extra += Allocs.Beyond(tick);   // 0.6 s .. 2.1 s: no cue fires
            seq.Finish(false);
            Assert.AreEqual(0, extra, "allocations in 30 frames between cues");
            yield return null;
        }

        static object _sink;
    }
}
