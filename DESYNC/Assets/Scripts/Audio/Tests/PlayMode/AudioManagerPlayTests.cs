using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;

namespace Desync.Audio.Tests
{
    public class AudioManagerPlayTests
    {
        const float G = 0.55f;   // AudioManager.MusicGain
        AudioManager m;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            LayerState.Set(false);
            GameState.Melt = 0f; GameState.InHack = false; GameState.Paused = false; GameState.Alive = true; GameState.GlitchCountdown = -1f;
            if (!UnityEngine.Object.FindAnyObjectByType<AudioListener>()) new GameObject("TestListener").AddComponent<AudioListener>();
            if (!AudioManager.I) new GameObject("AudioManager").AddComponent<AudioManager>();
            m = AudioManager.I;
            float t = Time.realtimeSinceStartup;
            while (!m.MusicPlaying && Time.realtimeSinceStartup - t < 15f) yield return null;
            Assert.IsTrue(m.MusicPlaying, "music never started");
            yield return new WaitForSecondsRealtime(0.4f);
        }

        [TearDown]
        public void Teardown()
        {
            LayerState.Set(false);
            GameState.Melt = 0f; GameState.InHack = false; GameState.Paused = false; GameState.Alive = true;
        }

        float V(int stem) => m.MusicSource(stem).volume;

        [UnityTest]
        public IEnumerator FourStemsPlayInLockstepOnRender()
        {
            for (int i = 0; i < 4; i++) Assert.IsTrue(m.MusicSource(i).isPlaying, "stem " + i);
            int len = m.MusicSource(0).clip.samples;
            for (int i = 1; i < 4; i++)
            {
                int d = Mathf.Abs(m.MusicSource(i).timeSamples - m.MusicSource(0).timeSamples);
                d = Mathf.Min(d, len - d);
                Assert.Less(d, 2400, "stem " + i + " drift (samples)");
            }
            Assert.AreEqual(G, V(0), 0.02f);
            Assert.AreEqual(0f, V(1), 0.01f);
            Assert.AreEqual(0f, V(2), 0.01f);
            Assert.AreEqual(0f, V(3), 0.01f);
            yield break;
        }

        [UnityTest]
        public IEnumerator FlipCrossfadesRenderToSourceOverStemCrossfade()
        {
            LayerState.Set(true);
            yield return new WaitForSecondsRealtime(GameConfig.I.stemCrossfade * 0.5f);
            Assert.That(V(1), Is.InRange(0.02f, G - 0.02f), "mid-crossfade");
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.AreEqual(G, V(1), 0.02f);
            Assert.AreEqual(0f, V(0), 0.01f);
            LayerState.Set(false);
            yield return new WaitForSecondsRealtime(0.4f);
            Assert.AreEqual(G, V(0), 0.02f);
        }

        [UnityTest]
        public IEnumerator MeltBlendsTowardsLowPassedStems()
        {
            GameState.Melt = 40f;
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.AreEqual(0f, V(2), 0.01f, "below 50 % no low-pass");
            GameState.Melt = 100f;
            yield return new WaitForSecondsRealtime(1.2f);
            Assert.AreEqual(G, V(2), 0.02f, "full low-pass at 100 %");
            Assert.AreEqual(0f, V(0), 0.01f);
            GameState.Melt = 75f;
            yield return new WaitForSecondsRealtime(1.2f);
            Assert.AreEqual(G * 0.5f, V(2), 0.03f, "half way at 75 %");
            Assert.AreEqual(G * 0.5f, V(0), 0.03f);
        }

        [UnityTest]
        public IEnumerator HackDucksMusicByConfiguredDb()
        {
            GameState.InHack = true;
            yield return new WaitForSecondsRealtime(0.6f);
            Assert.AreEqual(G * Mathf.Pow(10f, GameConfig.I.hackDuckDb / 20f), V(0), 0.015f);
            GameState.InHack = false;
            yield return new WaitForSecondsRealtime(0.6f);
            Assert.AreEqual(G, V(0), 0.02f);
        }

        [UnityTest]
        public IEnumerator PauseLowersButDoesNotStopMusic()
        {
            GameState.Paused = true;
            yield return new WaitForSecondsRealtime(0.8f);
            Assert.Less(V(0), G * 0.5f);
            Assert.Greater(V(0), 0.01f);
            Assert.IsTrue(m.MusicSource(0).isPlaying);
        }

        [UnityTest]
        public IEnumerator HeartbeatRunsWhileMeltWarnsAndStopsAfter()
        {
            GameState.Melt = 90f;
            GameEvents.MeltWarning?.Invoke(true);
            yield return new WaitForSecondsRealtime(0.8f);
            Assert.IsTrue(m.transform.Find("heartbeat").GetComponent<AudioSource>().isPlaying);
            Assert.Greater(m.transform.Find("heartbeat").GetComponent<AudioSource>().pitch, 1.2f, "tempo climbs with melt");
            GameState.Melt = 0f;
            GameEvents.MeltWarning?.Invoke(false);
            yield return new WaitForSecondsRealtime(0.8f);
            Assert.IsFalse(m.transform.Find("heartbeat").GetComponent<AudioSource>().isPlaying);
        }

        [UnityTest]
        public IEnumerator EventStormNeverExceedsSixteenVoices()
        {
            for (int i = 0; i < 40; i++)
            {
                var p = new Vector3(i % 7, 0f, i % 5);
                GameEvents.CameraTick?.Invoke(p);
                GameEvents.IceWindup?.Invoke(p);
                GameEvents.IceSpawned?.Invoke(p);
                GameEvents.Footstep?.Invoke();
                GameEvents.HackTick?.Invoke(i % 5);
                GameEvents.Flipped?.Invoke(i % 2 == 0);
                GameEvents.TraceStarted?.Invoke(1 + i % 3);
            }
            GameState.Melt = 90f;
            GameEvents.MeltWarning?.Invoke(true);
            for (int f = 0; f < 6; f++)
            {
                yield return null;
                Assert.LessOrEqual(m.PlayingVoices(), 16, "voices");
            }
            Assert.Greater(m.PlayingVoices(), 4);
            GameEvents.MeltWarning?.Invoke(false);
        }

        [UnityTest]
        public IEnumerator HackTickPitchIsOneSemitonePerStep()
        {
            GameEvents.HackTick?.Invoke(0);
            GameEvents.HackTick?.Invoke(3);
            yield return null;
            int found = 0;
            foreach (var s in m.GetComponentsInChildren<AudioSource>())
            {
                if (!s.isPlaying || s.clip == null || s.clip.name != "hack_tick") continue;
                Assert.IsTrue(Mathf.Approximately(s.pitch, 1f) || Mathf.Approximately(s.pitch, Mathf.Pow(2f, 3f / 12f)), "pitch " + s.pitch);
                found++;
            }
            Assert.AreEqual(2, found);
        }

        [UnityTest]
        public IEnumerator FootstepsSwitchWithLayer()
        {
            GameEvents.Footstep?.Invoke();
            yield return null;
            Assert.IsTrue(PlayingClipStartsWith("footstep_render_"));
            LayerState.Set(true);
            yield return new WaitForSecondsRealtime(0.4f);
            GameEvents.Footstep?.Invoke();
            yield return null;
            Assert.IsTrue(PlayingClipStartsWith("footstep_source_"));
        }

        bool PlayingClipStartsWith(string prefix)
        {
            foreach (var s in m.GetComponentsInChildren<AudioSource>())
                if (s.isPlaying && s.clip != null && s.clip.name.StartsWith(prefix, StringComparison.Ordinal)) return true;
            return false;
        }

        /// GC.GetAllocatedBytesForCurrentThread reads 0 under Unity's Mono, so measure with the profiler's per-frame GC counter instead:
        /// run a burst of Update() + SFX events inside ONE frame and compare that frame with idle baseline frames.
        [UnityTest]
        public IEnumerator UpdateAndEventPathsAllocateNothing()
        {
            using (var rec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame"))
            {
                var upd = (Action)Delegate.CreateDelegate(typeof(Action), m, typeof(AudioManager).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance));
                void Burst(int n)
                {
                    for (int i = 0; i < n; i++)
                    {
                        GameState.Melt = 50f + (i % 50);
                        GameState.InHack = (i & 64) != 0;
                        GameState.GlitchCountdown = i % 90 < 60 ? 3f - (i % 60) * 0.05f : -1f;
                        upd();
                        GameEvents.Footstep?.Invoke();
                        GameEvents.HackTick?.Invoke(i % 5);
                        GameEvents.CameraTick?.Invoke(Vector3.one * i);
                        GameEvents.IceSpawned?.Invoke(Vector3.zero);
                        GameEvents.Flipped?.Invoke((i & 1) == 0);
                    }
                    GameState.GlitchCountdown = -1f; GameState.InHack = false;
                }

                Burst(50);                       // warm-up: first-call JIT, lazy statics
                for (int i = 0; i < 5; i++) yield return null;
                Assert.IsTrue(rec.Valid, "GC counter unavailable");

                long baseline = 0;
                for (int i = 0; i < 20; i++) { yield return null; baseline = Math.Max(baseline, rec.LastValue); }

                var junk = new byte[8][];        // instrument sanity: 8 x 16 KB must be visible in the frame counter
                for (int i = 0; i < 8; i++) junk[i] = new byte[16384];
                yield return null;
                long seen = rec.LastValue;
                GC.KeepAlive(junk);
                Assert.Greater(seen, baseline + 100000, "GC counter does not register a 128 KB allocation (baseline " + baseline + ", saw " + seen + ")");

                // Call AudioManager's own handlers directly (other systems also subscribe to GameEvents and may allocate; not our budget).
                T H<T>(string name) where T : Delegate => (T)Delegate.CreateDelegate(typeof(T), m, typeof(AudioManager).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance));
                var foot = H<Action>("OnFootstep");
                var tick = H<Action<int>>("OnHackTick");
                var cam = H<Action<Vector3>>("OnCameraTick");
                var ice = H<Action<Vector3>>("OnIceSpawned");
                var flip = H<Action<bool>>("OnFlipped");
                var wind = H<Action<Vector3>>("OnIceWindup");
                var cleared = H<Action<RoomResult>>("OnRoomCleared");
                var popt = H<Action<string, Color>>("OnPopText");
                var parts = new (string name, Action a)[]
                {
                    ("Update", () => { for (int i = 0; i < 500; i++) { GameState.Melt = 50f + (i % 50); GameState.InHack = (i & 64) != 0; GameState.GlitchCountdown = i % 90 < 60 ? 3f - (i % 60) * 0.05f : -1f; upd(); } GameState.InHack = false; GameState.GlitchCountdown = -1f; }),
                    ("Footstep", () => { for (int i = 0; i < 500; i++) foot(); }),
                    ("HackTick", () => { for (int i = 0; i < 500; i++) tick(i % 5); }),
                    ("CameraTick", () => { for (int i = 0; i < 500; i++) cam(Vector3.one * i); }),
                    ("IceSpawned+Windup", () => { for (int i = 0; i < 500; i++) { ice(Vector3.zero); wind(Vector3.one); } }),
                    ("Flipped", () => { for (int i = 0; i < 500; i++) flip((i & 1) == 0); }),
                    ("RoomCleared+Pop", () => { for (int i = 0; i < 100; i++) { cleared(new RoomResult { grade = 'S' }); popt("GHOST x3", Color.white); } }),
                };
                long burst = 0;
                string worst, worstName = "";
                foreach (var (name, a) in parts)
                {
                    yield return null;
                    a();
                    yield return null;
                    worst = name + " " + rec.LastValue + " B";   // cheap: only used in the failure message
                    if (rec.LastValue > burst) { burst = rec.LastValue; worstName = worst; }
                }
                Assert.LessOrEqual(burst, baseline + 512, "audio Update + SFX handlers allocated: worst part " + worstName + ", idle frame max " + baseline + " B");
            }
        }
    }
}
