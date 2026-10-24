using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Desync.Enemies;
using Desync.Flow;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;
using RuleEvent = Desync.Rules.EventType;

namespace Desync.Audio.Tests
{
    /// M3 audio: adaptive music (act sets, tension and TRACE layers), the new cues, and the 16-voice cap with all of it running.
    /// The cue handlers are called directly (not through the global events) so another system's subscriber cannot interfere;
    /// WiringIsComplete checks the subscriptions themselves.
    public class AudioAdaptivePlayTests
    {
        const float G = 0.55f;   // AudioManager.MusicGain
        const BindingFlags NonPublic = BindingFlags.NonPublic | BindingFlags.Instance;
        AudioManager m;
        AudioBank bank;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            ResetState();
            if (!UnityEngine.Object.FindAnyObjectByType<AudioListener>()) new GameObject("TestListener").AddComponent<AudioListener>();
            if (!AudioManager.I) new GameObject("AudioManager").AddComponent<AudioManager>();
            m = AudioManager.I;
            bank = Resources.Load<AudioBank>("AudioBank");
            float t = Time.realtimeSinceStartup;
            // an earlier test may have left the music on another act, a layer fading out or a cue still ringing
            while ((!m.MusicPlaying || m.Act != 0 || m.ActSwapping || !m.MusicSource(0).isPlaying || LayersPlaying() || PooledPlaying())
                   && Time.realtimeSinceStartup - t < 25f) yield return null;
            Assert.IsTrue(m.MusicPlaying && m.Act == 0, "music never settled on act 1");
            yield return new WaitForSecondsRealtime(0.3f);
        }

        [UnityTearDown]
        public IEnumerator Teardown()
        {
            ResetState();   // title: act 1; wait for the swap back so the next fixture starts on a settled mix
            float t = Time.realtimeSinceStartup;
            while ((!m.MusicPlaying || m.Act != 0 || m.ActSwapping) && Time.realtimeSinceStartup - t < 25f) yield return null;
        }

        static void ResetState()
        {
            LayerState.Set(false);
            GameState.Melt = 0f; GameState.InHack = false; GameState.Paused = false; GameState.Alive = true; GameState.GlitchCountdown = -1f;
            GameState.IceAlive = 0; GameState.TraceActive = false; GameState.TraceCount = 0; GameState.Combo = 0;
            RunState.Phase = RunPhase.None; RunState.Act = 1;
        }

        float V(int stem) => m.MusicSource(stem).volume;

        IEnumerator Wait(float s) { yield return new WaitForSecondsRealtime(s); }

        void Call(string handler, params object[] args) => typeof(AudioManager).GetMethod(handler, NonPublic).Invoke(m, args);

        bool LayersPlaying() => m.MusicSource(AudioManager.TensionStem).isPlaying || m.MusicSource(AudioManager.TraceAStem).isPlaying || m.MusicSource(AudioManager.TraceBStem).isPlaying;

        bool PooledPlaying()
        {
            foreach (var s in m.GetComponentsInChildren<AudioSource>())
                if (s.isPlaying && s.name.StartsWith("sfx", StringComparison.Ordinal)) return true;
            return false;
        }

        /// Waits (up to 6 s) until no pooled cue is ringing, so a clip heard next is the one just triggered.
        IEnumerator Quiet()
        {
            float t = Time.realtimeSinceStartup;
            while (PooledPlaying() && Time.realtimeSinceStartup - t < 6f) yield return null;
        }

        /// Pitches of every voice currently playing the named clip.
        List<float> Pitches(string clip)
        {
            var l = new List<float>();
            foreach (var s in m.GetComponentsInChildren<AudioSource>())
                if (s.isPlaying && s.clip != null && s.clip.name == clip) l.Add(s.pitch);
            return l;
        }

        bool Playing(string clip) => Pitches(clip).Count > 0;

        // ------------------------------------------------------------------ wiring
        [UnityTest]
        public IEnumerator WiringIsComplete()
        {
            var subscribed = new List<string>();
            void Check(Type owner, string field)
            {
                var f = owner.GetField(field, BindingFlags.Public | BindingFlags.Static);
                Assert.IsNotNull(f, owner.Name + "." + field + " no longer exists");
                var d = f.GetValue(null) as Delegate;
                bool found = false;
                if (d != null) foreach (var x in d.GetInvocationList()) if (ReferenceEquals(x.Target, m)) found = true;
                Assert.IsTrue(found, owner.Name + "." + field + " is not wired to AudioManager");
                subscribed.Add(owner.Name + "." + field);
            }
            foreach (var f in new[] { "Scored", "Grazed" }) Check(typeof(GameEvents), f);
            foreach (var f in new[] { "TrackerSpawned", "AuditorPhase", "Alerted", "SentinelCharge", "SentinelPulse", "SplitterSplit", "DaemonCrossed", "AuditorDrone", "PuppetString" })
                Check(typeof(EnemyEvents), f);
            foreach (var f in new[] { "Phase", "WallSwap", "StringSevered", "Ending" }) Check(typeof(BossEvents), f);
            foreach (var f in new[] { "PhaseChanged", "ChooseNode", "PickExploit", "Buy", "RemoveExploit", "ChooseCooldown", "Leave", "CashOut", "GoDeeper" })
                Check(typeof(RunEvents), f);
            Assert.AreEqual(24, subscribed.Count);
            yield break;
        }

        // ------------------------------------------------------------------ layers
        [UnityTest]
        public IEnumerator TensionLayerFadesInWithMeltInStepWithTheStemsAndStopsAtSilence()
        {
            var tension = m.MusicSource(AudioManager.TensionStem);
            GameState.Melt = 50f;
            yield return Wait(0.5f);
            Assert.IsFalse(tension.isPlaying, "no tension at 50 % melt");

            GameState.Melt = 70f;
            yield return Wait(1.6f);
            Assert.IsTrue(tension.isPlaying);
            Assert.AreEqual(G * 0.8f * 0.5f, tension.volume, 0.03f, "half way at 70 %");
            int len = tension.clip.samples, d = Mathf.Abs(tension.timeSamples - m.MusicSource(0).timeSamples);
            Assert.Less(Mathf.Min(d, len - d), 4800, "the layer started on the stems' position (samples)");

            GameState.Melt = 100f;
            yield return Wait(1.6f);
            Assert.AreEqual(G * 0.8f, tension.volume, 0.03f, "full at 90 % and above");

            GameState.Melt = 0f;
            yield return Wait(2.6f);
            Assert.IsFalse(tension.isPlaying, "the layer stops at silence and gives its voice back");
        }

        [UnityTest]
        public IEnumerator LayerGainsRampWithoutJumps()
        {
            var tension = m.MusicSource(AudioManager.TensionStem);
            GameState.Melt = 90f;
            float last = 0f, step = 0f, t = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t < 1.8f)
            {
                yield return null;
                float v = tension.volume;
                if (!tension.isPlaying) continue;   // not audible yet
                Assert.GreaterOrEqual(v, last - 1e-4f, "rising only");
                step = Mathf.Max(step, v - last);
                last = v;
            }
            Assert.AreEqual(G * 0.8f, last, 0.03f);
            Assert.Less(step, 0.12f, "no frame jumps the layer in");
        }

        [UnityTest]
        public IEnumerator TraceLayersFollowTheTraceLevel()
        {
            var a = m.MusicSource(AudioManager.TraceAStem);
            var b = m.MusicSource(AudioManager.TraceBStem);
            GameState.TraceActive = true;
            GameState.TraceCount = 1;
            yield return Wait(0.6f);
            Assert.IsTrue(a.isPlaying);
            Assert.AreEqual(G * 0.7f * 0.7f, a.volume, 0.03f, "level 1: scanner pulse, softly");
            Assert.IsFalse(b.isPlaying, "the klaxon waits for level 2");

            GameState.TraceCount = 2;
            yield return Wait(0.6f);
            Assert.IsTrue(b.isPlaying);
            Assert.AreEqual(G * 0.7f, a.volume, 0.03f);
            Assert.AreEqual(G * 0.65f * 0.7f, b.volume, 0.03f);

            GameState.TraceCount = 3;
            yield return Wait(0.6f);
            Assert.AreEqual(G * 0.65f, b.volume, 0.03f, "level 3: everything at full");

            GameState.TraceActive = false;
            yield return Wait(1.6f);
            Assert.IsFalse(a.isPlaying);
            Assert.IsFalse(b.isPlaying);
        }

        // ------------------------------------------------------------------ act sets
        [UnityTest]
        public IEnumerator ActChangeDipsToSilenceSwapsTheSetAndNeverAddsVoices()
        {
            int baseline = m.PlayingVoices();
            int maxVoices = baseline;

            foreach (int act in new[] { 2, 3 })
            {
                RunState.Phase = RunPhase.Map;
                RunState.Act = act;
                float t = Time.realtimeSinceStartup, minStem = G;
                while ((m.Act != act - 1 || m.ActSwapping) && Time.realtimeSinceStartup - t < 25f)
                {
                    yield return null;
                    maxVoices = Mathf.Max(maxVoices, m.PlayingVoices());
                    minStem = Mathf.Min(minStem, V(0));
                }
                Assert.AreEqual(act - 1, m.Act, "act " + act + " set never arrived");
                Assert.Less(minStem, 0.05f, "the music dips to silence while the stems swap");
                yield return Wait(0.8f);
                for (int i = 0; i < AudioManager.StemCount; i++)
                {
                    Assert.AreSame(bank.Stem(act - 1, i), m.MusicSource(i).clip, "stem " + i + " clip");
                    Assert.IsTrue(m.MusicSource(i).isPlaying, "stem " + i);
                }
                Assert.AreEqual(G, V(0), 0.03f, "back up to the RENDER level");
                Assert.AreNotEqual(AudioDataLoadState.Loaded, bank.Stem(act - 2, 0).loadState, "the set that was left is unloaded");
            }
            Assert.LessOrEqual(maxVoices, baseline, "an act change swaps the stems, it never stacks a second set");

            RunState.Phase = RunPhase.None;   // title: back to act 1
            float t0 = Time.realtimeSinceStartup;
            while ((m.Act != 0 || m.ActSwapping) && Time.realtimeSinceStartup - t0 < 25f) yield return null;
            Assert.AreEqual(0, m.Act);
            Assert.AreSame(bank.musicRender, m.MusicSource(0).clip);
        }

        [UnityTest]
        public IEnumerator LayersRestartInStepAfterAnActSwap()
        {
            GameState.Melt = 95f;
            yield return Wait(1.4f);
            Assert.IsTrue(m.MusicSource(AudioManager.TensionStem).isPlaying);
            RunState.Phase = RunPhase.Map;
            RunState.Act = 2;
            float t = Time.realtimeSinceStartup;
            while ((m.Act != 1 || m.ActSwapping) && Time.realtimeSinceStartup - t < 25f) yield return null;
            yield return Wait(1.6f);
            var tension = m.MusicSource(AudioManager.TensionStem);
            Assert.IsTrue(tension.isPlaying, "the layer comes back after the swap");
            int len = tension.clip.samples, d = Mathf.Abs(tension.timeSamples - m.MusicSource(0).timeSamples);
            Assert.Less(Mathf.Min(d, len - d), 4800, "and on the new stems' position");
        }

        // ------------------------------------------------------------------ cues
        [UnityTest]
        public IEnumerator ScorePopsClimbOneSemitonePerChainStep()
        {
            Call("OnScored", new ScorePop { type = RuleEvent.CleanFlip, points = 50, multiplier = 1f });
            Call("OnScored", new ScorePop { type = RuleEvent.CleanFlip, points = 50, multiplier = 2f });
            yield return null;
            var p = Pitches("score_pop");
            Assert.AreEqual(2, p.Count);
            p.Sort();
            Assert.AreEqual(1f, p[0], 1e-4f);
            Assert.AreEqual(Mathf.Pow(2f, 4f / 12f), p[1], 1e-4f);
            yield return Quiet();
            Call("OnScored", new ScorePop { type = RuleEvent.ChainBreak, points = 0, multiplier = 1f });
            Call("OnScored", new ScorePop { type = RuleEvent.RoomClear, points = 1000, multiplier = 1f });
            yield return null;
            Assert.IsFalse(Playing("score_pop"), "chain breaks and room clears have their own cues");
        }

        [UnityTest]
        public IEnumerator RedlineGetsItsWhooshAndTheMusicDucksForTheSlowMotion()
        {
            Call("OnScored", new ScorePop { type = RuleEvent.Redline, points = 250, multiplier = 1f });
            Call("OnPopText", "REDLINE", Color.red);
            yield return null;
            Assert.IsTrue(Playing("redline_whoosh"));
            Assert.IsFalse(Playing("ui_pop"), "the REDLINE pop text must not add a second cue");
            yield return Wait(0.35f);
            Assert.Less(V(0), G * 0.7f, "music ducked");
            yield return Wait(1.2f);
            Assert.AreEqual(G, V(0), 0.03f, "and back");
        }

        [UnityTest]
        public IEnumerator GrazeWhooshComesFromTheNearestIce()
        {
            GameState.IceAlive = 1;
            GameState.NearestIce = new Vector3(0.5f, 0.6f, 0.3f);
            Call("OnGrazed", "ICE-1");
            yield return null;
            Assert.IsTrue(Playing("graze_whoosh"));
            foreach (var s in m.GetComponentsInChildren<AudioSource>())
                if (s.isPlaying && s.clip != null && s.clip.name == "graze_whoosh")
                {
                    Assert.AreEqual(1f, s.spatialBlend);
                    Assert.AreEqual(0f, Vector3.Distance(s.transform.position, GameState.NearestIce), 1e-4f);
                }
        }

        [UnityTest]
        public IEnumerator TrackerAndAuditorStingersPlayAndPhaseFourStaysQuiet()
        {
            Call("OnTrackerSpawned", new Vector3(2f, 0f, 2f));
            yield return null;
            Assert.IsTrue(Playing("tracker_sting"));
            yield return Quiet();
            Call("OnAuditorPhase", 2);
            yield return null;
            Assert.IsTrue(Playing("auditor_p2"));
            yield return Quiet();
            Call("OnAuditorPhase", 3);
            yield return null;
            Assert.IsTrue(Playing("auditor_p3"));
            yield return Quiet();
            Call("OnAuditorPhase", 4);
            Call("OnPopText", "UPLINK OPEN // 20 S", Color.red);
            Call("OnPopText", "AUDIT ESCALATED", Color.magenta);
            Call("OnPopText", "GHOST x3", Color.cyan);
            yield return null;
            Assert.IsFalse(PooledPlaying(), "phase 4 and the stinger pop texts add nothing");
            Call("OnPopText", "COPIED", Color.green);
            yield return null;
            Assert.IsTrue(Playing("ui_pop"), "other pop texts still get the pop");
        }

        [UnityTest]
        public IEnumerator PerceptionCuesFireOncePerStateAndTracingIsLeftToTheTraceAlarm()
        {
            var p = new Vector3(3f, 1f, 3f);
            Call("OnAlerted", p, Alert.Suspicious);
            Call("OnAlerted", p, Alert.Suspicious);   // a second sensor in the same instant
            Call("OnAlerted", p, Alert.Tracing);
            Call("OnAlerted", p, Alert.Patrol);
            yield return null;
            Assert.AreEqual(1, Pitches("alert_suspicious").Count);
            Assert.AreEqual(1, CountPlaying(), "Tracing and Patrol are silent");
            Call("OnAlerted", p, Alert.Hunting);
            Call("OnAlerted", p, Alert.Return);
            yield return null;
            Assert.IsTrue(Playing("alert_hunting"));
            Assert.IsTrue(Playing("alert_return"));
            yield return Wait(0.3f);
            Call("OnAlerted", p, Alert.Suspicious);
            yield return null;
            Assert.IsTrue(Playing("alert_suspicious"), "the next change is audible again");
        }

        int CountPlaying()
        {
            int n = 0;
            foreach (var s in m.GetComponentsInChildren<AudioSource>())
                if (s.isPlaying && s.name.StartsWith("sfx", StringComparison.Ordinal)) n++;
            return n;
        }

        [UnityTest]
        public IEnumerator EveryEnemyAndBossEventHasItsCue()
        {
            var p = new Vector3(1f, 0f, 1f);
            var cases = new List<(Action fire, string clip)>
            {
                (() => Call("OnSentinelCharge", p), "sentinel_charge"),
                (() => Call("OnSentinelPulse", p), "sentinel_pulse"),
                (() => Call("OnSplitterSplit", p), "splitter_split"),
                (() => Call("OnDaemonCrossed", p, true), "daemon_cross"),
                (() => Call("OnAuditorDrone", p, true), "tracker_sting"),
                (() => Call("OnPuppetString", "PUPPET-1"), "access_granted"),
                (() => Call("OnWallSwap"), "wall_swap"),
                (() => Call("OnBossPhase", "COMPILER", 2), "auditor_p2"),
                (() => Call("OnStringSevered", 1), "access_granted"),
                (() => Call("OnBossEnding", BossEvents.Exposed), "finale_sting"),
                (() => m.PlayEnemyCue(EnemyCue.WatcherBeam, p), "watcher_beam"),
            };
            foreach (var (fire, clip) in cases)
            {
                yield return Quiet();
                fire();
                yield return null;
                Assert.IsTrue(Playing(clip), clip);
            }
            yield return Quiet();
            Call("OnAuditorDrone", p, false);   // recalled: silent
            yield return null;
            Assert.IsFalse(PooledPlaying());
        }

        [UnityTest]
        public IEnumerator RunFlowCues()
        {
            var cases = new List<(Action fire, string clip)>
            {
                (() => Call("OnRunPhase", RunPhase.Map), "map_open"),
                (() => Call("OnChooseNode", "a1n0"), "map_choose"),
                (() => Call("OnPickExploit", 0), "draft_pick"),
                (() => Call("OnPickExploit", -1), "ui_click"),
                (() => Call("OnBuy", 0), "market_buy"),
                (() => Call("OnRemoveExploit", "clean_handshake"), "market_buy"),
                (() => Call("OnRunPhase", RunPhase.Cooldown), "cooldown"),
                (() => Call("OnChooseCooldown", 0), "map_choose"),
            };
            foreach (var (fire, clip) in cases)
            {
                yield return Quiet();
                fire();
                yield return null;
                Assert.IsTrue(Playing(clip), clip);
            }
        }

        [UnityTest]
        public IEnumerator DraftDecryptTicksRiseAnOctaveAndStopWhenTheDraftEnds()
        {
            Call("OnRunPhase", RunPhase.Draft);
            var seen = new List<float>();
            float t = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t < 1.3f)
            {
                yield return null;
                foreach (float pitch in Pitches("decrypt_tick"))
                    if (seen.Count == 0 || pitch > seen[seen.Count - 1] + 1e-4f) seen.Add(pitch);
            }
            Assert.GreaterOrEqual(seen.Count, 8, "ticks heard (some may fall between frames)");
            Assert.Less(seen[0], 1.3f, "starts near the bottom");
            Assert.Greater(seen[seen.Count - 1], 1.85f, "ends near the octave");
            Assert.LessOrEqual(seen[seen.Count - 1], 2.0001f);

            Call("OnRunPhase", RunPhase.Draft);   // reroll: the reveal restarts
            yield return Wait(0.3f);
            Call("OnRunPhase", RunPhase.Map);     // phase over: no more ticks
            yield return Wait(0.2f);
            for (int i = 0; i < 20; i++)
            {
                yield return null;
                Assert.IsFalse(Playing("decrypt_tick"), "ticks went on after the draft ended");
            }
        }

        [UnityTest]
        public IEnumerator PickingEndsTheDecryptTicks()
        {
            Call("OnRunPhase", RunPhase.Draft);
            yield return Wait(0.2f);
            Call("OnPickExploit", 1);
            yield return Wait(0.2f);
            for (int i = 0; i < 20; i++)
            {
                yield return null;
                Assert.IsFalse(Playing("decrypt_tick"));
            }
        }

        // ------------------------------------------------------------------ the 16-voice cap with everything running
        [UnityTest]
        public IEnumerator EveryNewCueFiringAtOnceStaysInsideTheVoiceCapWithAllLoopsRunning()
        {
            GameState.Melt = 95f;                         // tension at full, heartbeat on
            GameEvents.MeltWarning?.Invoke(true);
            GameState.TraceActive = true; GameState.TraceCount = 3;   // both TRACE layers at full
            foreach (bool source in new[] { true, false })
            {
                LayerState.Set(source);
                GameState.IceAlive = source ? 1 : 0;
                GameState.NearestIce = new Vector3(3f, 0.6f, 2f);
                float t = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t < 1.8f) { if (!source) m.LanternNear(new Vector3(4f, 1f, 4f)); yield return null; }
                for (int i = 0; i < AudioManager.MusicVoices; i++) Assert.IsTrue(m.MusicSource(i).isPlaying, "music voice " + i);
                if (source) Assert.IsTrue(m.IceHum.isPlaying, "ICE hum"); else Assert.IsTrue(m.LanternHum.isPlaying, "Lantern hum");
                int loops = m.PlayingVoices();   // 7 music + heartbeat + one hum = 9 (pooled cues: none left)
                Assert.GreaterOrEqual(loops, 9);
                int maxVoices = 0;

                for (int round = 0; round < 3; round++)
                {
                    FireEverything(round);
                    for (int f = 0; f < 8; f++)
                    {
                        if (!source) m.LanternNear(new Vector3(4f, 1f, 4f));
                        yield return null;
                        int v = m.PlayingVoices();
                        maxVoices = Mathf.Max(maxVoices, v);
                        Assert.LessOrEqual(v, AudioManager.VoiceCap, "voices (round " + round + ", frame " + f + ")");
                    }
                }
                Assert.Greater(maxVoices, loops, "the cues sounded");
                Assert.AreEqual(AudioManager.VoiceCap, maxVoices, "and filled the cap exactly");
                for (int i = 0; i < AudioManager.MusicVoices; i++) Assert.IsTrue(m.MusicSource(i).isPlaying, "music voice " + i + " survived the storm");
                if (source) Assert.IsTrue(m.IceHum.isPlaying, "ICE hum must survive the storm (own voice, not stolen)");
                else Assert.IsTrue(m.LanternHum.isPlaying, "Lantern hum must survive the storm");
                Assert.IsTrue(m.transform.Find("heartbeat").GetComponent<AudioSource>().isPlaying, "heartbeat survived the storm");
                yield return Quiet();
            }
            GameEvents.MeltWarning?.Invoke(false);
        }

        void FireEverything(int seed)
        {
            var p = new Vector3(seed, 0f, 2f);
            GameEvents.CameraTick?.Invoke(p); GameEvents.IceWindup?.Invoke(p); GameEvents.IceSpawned?.Invoke(p);
            GameEvents.Footstep?.Invoke(); GameEvents.HackTick?.Invoke(seed); GameEvents.Flipped?.Invoke(seed % 2 == 0);
            GameEvents.TraceStarted?.Invoke(1 + seed % 2);
            foreach (var t in new[] { RuleEvent.CleanFlip, RuleEvent.Graze, RuleEvent.Redline, RuleEvent.Hack, RuleEvent.HackPerfect, RuleEvent.PuppetString })
                Call("OnScored", new ScorePop { type = t, points = 100, multiplier = 1f + seed });
            Call("OnGrazed", "ICE-1");
            Call("OnPopText", "COPIED", Color.white);
            Call("OnTrackerSpawned", p); Call("OnAuditorPhase", 2); Call("OnAuditorPhase", 3);
            foreach (Alert a in Enum.GetValues(typeof(Alert))) Call("OnAlerted", p, a);
            Call("OnSentinelCharge", p); Call("OnSentinelPulse", p); Call("OnSplitterSplit", p);
            Call("OnDaemonCrossed", p, true); Call("OnAuditorDrone", p, true); Call("OnPuppetString", "P-1");
            foreach (EnemyCue c in Enum.GetValues(typeof(EnemyCue))) m.PlayEnemyCue(c, p);
            Call("OnWallSwap"); Call("OnBossPhase", "ARCHITECT", 3); Call("OnStringSevered", 2); Call("OnBossEnding", BossEvents.Merged);
            Call("OnRunPhase", RunPhase.Cooldown); Call("OnRunPhase", RunPhase.Map);
            Call("OnChooseNode", "a1n0"); Call("OnPickExploit", 0); Call("OnBuy", 0); Call("OnRemoveExploit", "x"); Call("OnChooseCooldown", 0);
            Call("OnRunPhase", RunPhase.Draft);   // leave a decrypt reveal running: its 16 ticks are part of the storm
        }

        [UnityTest]
        public IEnumerator LanternHumFollowsTheLanternInRenderAndFadesWhenTheCallsStop()
        {
            var h = m.LanternHum;
            Assert.IsNotNull(h.clip, "bank.lanternHum missing - run AudioSetup.Run");
            Assert.IsTrue(h.loop);
            Assert.AreEqual(1f, h.spatialBlend);
            var p = new Vector3(5f, 1.6f, -3f);
            float t = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t < 0.8f) { m.LanternNear(p); yield return null; }
            Assert.IsTrue(h.isPlaying);
            Assert.Greater(h.volume, 0.2f);
            Assert.AreEqual(0f, Vector3.Distance(h.transform.position, p), 1e-3f);

            LayerState.Set(true);   // the Lantern is a RENDER enemy: silent in SOURCE
            t = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t < 1.0f) { m.LanternNear(p); yield return null; }
            Assert.IsFalse(h.isPlaying, "SOURCE: silent");

            LayerState.Set(false);
            t = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t < 0.8f) { m.LanternNear(p); yield return null; }
            Assert.IsTrue(h.isPlaying);
            yield return Wait(1.0f);
            Assert.IsFalse(h.isPlaying, "no lantern near: faded out and stopped");
        }

        // ------------------------------------------------------------------ zero GC
        /// Same method as AudioManagerPlayTests.UpdateAndEventPathsAllocateNothing: the profiler's per-frame GC counter, one
        /// burst of handler calls inside a frame compared with idle frames. The M3 handlers and the layer / act / decrypt / uplink
        /// paths of Update must not allocate.
        [UnityTest]
        public IEnumerator M3HandlersAndLayerPathsAllocateNothing()
        {
            GameState.IceAlive = 1;
            LayerState.Set(true);
            GameState.TraceActive = true;
            using (var rec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame"))
            {
                T H<T>(string name) where T : Delegate => (T)Delegate.CreateDelegate(typeof(T), m, typeof(AudioManager).GetMethod(name, NonPublic));
                var upd = H<Action>("Update");
                var scored = H<Action<ScorePop>>("OnScored");
                var grazed = H<Action<string>>("OnGrazed");
                var tracker = H<Action<Vector3>>("OnTrackerSpawned");
                var phase = H<Action<int>>("OnAuditorPhase");
                var alerted = H<Action<Vector3, Alert>>("OnAlerted");
                var sentinel = H<Action<Vector3>>("OnSentinelPulse");
                var daemon = H<Action<Vector3, bool>>("OnDaemonCrossed");
                var runPhase = H<Action<RunPhase>>("OnRunPhase");
                var buy = H<Action<int>>("OnBuy");
                var wall = H<Action>("OnWallSwap");

                void Burst(int n)
                {
                    for (int i = 0; i < n; i++)
                    {
                        GameState.Melt = 50f + (i % 50);
                        GameState.TraceCount = 1 + i % 3;
                        GameState.NearestIce = Vector3.one * (i % 15);
                        upd();
                        scored(new ScorePop { type = i % 4 == 0 ? RuleEvent.Graze : RuleEvent.CleanFlip, multiplier = 1f + (i % 8) * 0.5f });
                        grazed("ICE-1");
                        tracker(Vector3.one * i);
                        alerted(Vector3.one * i, (Alert)(i % 5));
                        sentinel(Vector3.zero);
                        daemon(Vector3.zero, (i & 1) == 0);
                        phase(2 + i % 3);
                        buy(0);
                        wall();
                        m.LanternNear(Vector3.one * i);
                        m.PlayEnemyCue((EnemyCue)(i % 5), Vector3.one);
                    }
                    runPhase(RunPhase.Draft);
                    for (int i = 0; i < 20; i++) upd();
                    runPhase(RunPhase.Map);
                }

                Burst(50);   // warm-up: first-call JIT, lazy statics
                for (int i = 0; i < 5; i++) yield return null;
                Assert.IsTrue(rec.Valid, "GC counter unavailable");

                long baseline = 0;
                for (int i = 0; i < 20; i++) { yield return null; baseline = Math.Max(baseline, rec.LastValue); }

                var junk = new byte[8][];   // instrument sanity: 8 x 16 KB must show in the frame counter
                for (int i = 0; i < 8; i++) junk[i] = new byte[16384];
                yield return null;
                long seen = rec.LastValue;
                GC.KeepAlive(junk);
                Assert.Greater(seen, baseline + 100000, "GC counter does not register a 128 KB allocation (baseline " + baseline + ", saw " + seen + ")");

                var parts = new (string name, Action a)[]
                {
                    ("Update with layers, hums, ticks", () => { for (int i = 0; i < 300; i++) { GameState.Melt = 50f + (i % 50); GameState.TraceCount = 1 + i % 3; upd(); } }),
                    ("OnScored", () => { for (int i = 0; i < 300; i++) scored(new ScorePop { type = RuleEvent.Graze, multiplier = 1f + i % 8 }); }),
                    ("OnGrazed + OnTrackerSpawned", () => { for (int i = 0; i < 300; i++) { grazed("ICE-1"); tracker(Vector3.one); } }),
                    ("OnAlerted + OnAuditorPhase", () => { for (int i = 0; i < 300; i++) { alerted(Vector3.one, (Alert)(i % 5)); phase(2 + i % 3); } }),
                    ("OnSentinelPulse + OnDaemonCrossed", () => { for (int i = 0; i < 300; i++) { sentinel(Vector3.zero); daemon(Vector3.zero, true); } }),
                    ("PlayEnemyCue + LanternNear", () => { for (int i = 0; i < 300; i++) { m.PlayEnemyCue((EnemyCue)(i % 5), Vector3.one); m.LanternNear(Vector3.one); } }),
                    ("Run cues + decrypt ticks", () => { for (int i = 0; i < 100; i++) { runPhase(RunPhase.Draft); for (int k = 0; k < 20; k++) upd(); buy(0); wall(); } runPhase(RunPhase.Map); }),
                };
                long burst = 0;
                string worstName = "";
                foreach (var (name, a) in parts)
                {
                    yield return null;
                    a();
                    yield return null;
                    if (rec.LastValue > burst) { burst = rec.LastValue; worstName = name + " " + rec.LastValue + " B"; }
                }
                Assert.LessOrEqual(burst, baseline + 512, "M3 audio paths allocated: worst part " + worstName + ", idle frame max " + baseline + " B");
            }
        }
    }
}
