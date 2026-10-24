using System.Collections;
using System.Collections.Generic;
using Desync.Content;
using Desync.Flow;
using Desync.Gen;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Desync.UI.Tests
{
    /// M3 integration fixes in the UI: the COMPILER collapse recap line and the Cooldown's +1 BACKUP under No Backups.
    public class M3FixesUITests
    {
        GameObject _camGo;
        readonly List<MonoBehaviour> _paused = new List<MonoBehaviour>();
        readonly List<int> _cool = new List<int>();

        void OnCool(int i) => _cool.Add(i);

        [UnitySetUp]
        public IEnumerator Up()
        {
            _camGo = new GameObject("FixesTestCam", typeof(Camera));
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
                if (mb && mb.enabled && (mb.GetType().Name == "GameManager" || mb.GetType().Name == "RunManager" || mb.GetType().Name == "RoomManager"))
                { mb.enabled = false; _paused.Add(mb); }   // live Flow would act on the requests
            RunEvents.ChooseCooldown += OnCool;
            yield return null;
            Assert.IsNotNull(UIManager.I, "UIManager did not self-bootstrap");
        }

        [TearDown]
        public void Down()
        {
            RunEvents.ChooseCooldown -= OnCool;
            _cool.Clear();
            DeepRun.Picks.Clear();
            RunState.Phase = RunPhase.None; RunState.Graph = null; RunState.Backups = 0; RunState.Mode = RunMode.Classic;
            RunEvents.PhaseChanged?.Invoke(RunPhase.None);
            if (UIManager.I) { UIManager.I.SetMode(UIManager.Mode.Title, true); UIManager.I.SetMode(UIManager.Mode.None, true); }
            if (_camGo) Object.Destroy(_camGo);
            foreach (var mb in _paused) if (mb) mb.enabled = true;
            _paused.Clear();
        }

        [Test]
        public void Recap_CompilerCollapse_ReadsSystemCollapse()
        {
            Assert.AreEqual("SYSTEM COLLAPSE", RecapScreen.CauseText(DeathCause.Glitch, Desync.Enemies.Compiler.KillerId));
            Assert.AreEqual("GLITCH ZONE", RecapScreen.CauseText(DeathCause.Glitch, "GLITCH-1"));
            Assert.AreEqual("GLITCH ZONE", RecapScreen.CauseText(DeathCause.Glitch, null));
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Cooldown_NoBackups_GreysOutTheBackup()
        {
            DeepRun.Picks.Add(new MutatorPick("no_backups", 1));
            RunState.Mode = RunMode.Campaign; RunState.Graph = Generator.GenerateRun(13u, 0); RunState.Act = 1; RunState.NodeId = null;
            RunState.Backups = 0;
            RunState.Phase = RunPhase.Cooldown;
            RunEvents.PhaseChanged?.Invoke(RunPhase.Cooldown);
            yield return null; yield return null;

            var card = GameObject.Find("Cooldown/Panel/Option1");
            Assert.IsTrue(card && card.activeInHierarchy, "+1 BACKUP card");
            Assert.IsTrue(card.transform.Find("Shade").GetComponent<Image>().enabled, "greyed out");
            var foot = card.transform.Find("Foot").GetComponent<TMP_Text>(); foot.ForceMeshUpdate();
            Assert.AreEqual("NO BACKUPS", foot.GetParsedText());
            ExecuteEvents.Execute(card, new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerDownHandler);
            Assert.IsEmpty(_cool, "a click asks for nothing");
            Assert.IsFalse(GameObject.Find("Cooldown/Panel/Option0").transform.Find("Shade").GetComponent<Image>().enabled, "the other options stay open");
        }
    }
}
