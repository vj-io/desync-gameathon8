using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Desync.Audio.Editor
{
    /// Idempotent audio import + bank build. Run: tools/unity.sh exec audio Desync.Audio.Editor.AudioSetup.Run
    /// 1. applies import settings to every clip under Assets/Audio (all Vorbis, decompress-on-load: on WebGL the browser decodes
    ///    to an AudioBuffer, which keeps the four music stems sample-locked and the loop gapless; "compressed in memory"
    ///    plays through HTML media elements there and would drift/gap),
    /// 2. fills Assets/Audio/Resources/AudioBank.asset (clip field by file name),
    /// 3. registers music.* / sfx.* keys in AssetLib.
    public static class AudioSetup
    {
        public const string BankPath = "Assets/Audio/Resources/AudioBank.asset";

        // file name (no extension) -> AudioBank field, AssetLib key
        static readonly (string file, string field, string key)[] Map =
        {
            ("music_render", "musicRender", "music.render"), ("music_source", "musicSource", "music.source"),
            ("music_render_lp", "musicRenderLp", "music.render.lp"), ("music_source_lp", "musicSourceLp", "music.source.lp"),
            ("flip_source", "flipSource", "sfx.flip.source"), ("flip_render", "flipRender", "sfx.flip.render"), ("flip_reject", "flipReject", "sfx.flip.reject"),
            ("camera_tick", "cameraTick", "sfx.camera.tick"), ("trace_alarm", "traceAlarm", "sfx.alarm"),
            ("ice_windup", "iceWindup", "sfx.ice.windup"), ("ice_shriek", "iceShriek", "sfx.ice.shriek"),
            ("hack_tick", "hackTick", "sfx.hack.tick"), ("hack_wrong", "hackWrong", "sfx.hack.wrong"),
            ("hack_start", "hackStart", "sfx.hack.start"), ("hack_cancel", "hackCancel", "sfx.hack.cancel"),
            ("access_granted", "accessGranted", "sfx.access"), ("heartbeat_loop", "heartbeat", "sfx.heartbeat"),
            ("flatline", "flatline", "sfx.flatline"), ("death_crunch", "deathCrunch", "sfx.death"),
            ("room_clear", "roomClear", "sfx.roomclear"), ("reboot_wipe", "reboot", "sfx.reboot"),
            ("portal_open", "portalOpen", "sfx.portal"), ("finale_sting", "finaleSting", "sfx.finale"),
            ("hijack_on", "hijackOn", "sfx.hijack.on"), ("hijack_off", "hijackOff", "sfx.hijack.off"),
            ("glitch_beep", "glitchBeep", "sfx.glitch.beep"), ("ui_click", "uiClick", "sfx.ui.click"), ("ui_pop", "uiPop", "sfx.ui.pop"),
        };

        // spatialised (3D) cues: forced mono so the engine pans them
        static readonly HashSet<string> Spatial = new HashSet<string> { "camera_tick", "ice_windup", "ice_shriek" };

        public static void Run()
        {
            AssetDatabase.Refresh();
            var guids = AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Audio" });
            var clips = new Dictionary<string, AudioClip>();
            int reimported = 0;
            foreach (var g in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                var name = System.IO.Path.GetFileNameWithoutExtension(path);
                if (ApplyImport(path, name.StartsWith("music_"), Spatial.Contains(name))) reimported++;
                clips[name] = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            }

            System.IO.Directory.CreateDirectory("Assets/Audio/Resources");
            var bank = AssetDatabase.LoadAssetAtPath<AudioBank>(BankPath);
            if (!bank) { bank = ScriptableObject.CreateInstance<AudioBank>(); AssetDatabase.CreateAsset(bank, BankPath); }

            int missing = 0;
            foreach (var (file, field, key) in Map)
            {
                clips.TryGetValue(file, out var clip);
                if (!clip) { Debug.LogWarning("[DESYNC] audio: missing clip " + file); missing++; }
                typeof(AudioBank).GetField(field, BindingFlags.Public | BindingFlags.Instance).SetValue(bank, clip);
                AssetLib.Put(key, clip);
            }
            bank.footstepRender = Steps(clips, "footstep_render_", "sfx.footstep.render.");
            bank.footstepSource = Steps(clips, "footstep_source_", "sfx.footstep.source.");
            EditorUtility.SetDirty(bank);
            AssetDatabase.SaveAssets();

            Debug.Log($"[DESYNC] audio setup: {clips.Count} clips, {reimported} reimported, {missing} missing, bank at {BankPath}");
            foreach (var kv in clips)
                if (kv.Value) Debug.Log($"[DESYNC] audio clip {kv.Key}: {kv.Value.length:F3}s {kv.Value.frequency}Hz {kv.Value.channels}ch {kv.Value.samples} samples");
        }

        static AudioClip[] Steps(Dictionary<string, AudioClip> clips, string prefix, string keyPrefix)
        {
            var list = new List<AudioClip>();
            for (int i = 0; clips.TryGetValue(prefix + i, out var c); i++)
            {
                list.Add(c);
                AssetLib.Put(keyPrefix + i, c);
            }
            return list.ToArray();
        }

        /// Returns true if anything changed (and the asset was reimported).
        static bool ApplyImport(string path, bool music, bool forceMono)
        {
            var imp = AssetImporter.GetAtPath(path) as AudioImporter;
            if (!imp) return false;
            var s = imp.defaultSampleSettings;
            float q = music ? 0.5f : 0.6f;
            bool changed = s.loadType != AudioClipLoadType.DecompressOnLoad || s.compressionFormat != AudioCompressionFormat.Vorbis
                || !Mathf.Approximately(s.quality, q) || s.sampleRateSetting != AudioSampleRateSetting.PreserveSampleRate
                || !s.preloadAudioData || imp.forceToMono != forceMono || imp.loadInBackground || imp.ambisonic;
            if (!changed) return false;
            s.loadType = AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = q;
            s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            s.preloadAudioData = true;
            imp.defaultSampleSettings = s;
            imp.forceToMono = forceMono;
            imp.loadInBackground = false;
            imp.ambisonic = false;
            imp.SaveAndReimport();
            return true;
        }
    }
}
