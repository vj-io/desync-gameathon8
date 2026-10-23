using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Desync
{
    /// Keyed registry of shared assets (Resources/AssetLib.asset). Producers register in editor setup scripts with Put();
    /// consumers call AssetLib.Get<T>(key) and must tolerate null (fallback visuals/silence). Key list: CONTRACT.md.
    public class AssetLib : ScriptableObject
    {
        [Serializable] public struct Entry { public string key; public Object value; }
        public List<Entry> entries = new List<Entry>();

        static AssetLib _i;
        Dictionary<string, Object> _map;

        public static AssetLib I => _i ? _i : (_i = Resources.Load<AssetLib>("AssetLib"));

        public static T Get<T>(string key) where T : Object
        {
            var lib = I;
            if (!lib) return null;
            if (lib._map == null)
            {
                lib._map = new Dictionary<string, Object>();
                foreach (var e in lib.entries) if (!string.IsNullOrEmpty(e.key)) lib._map[e.key] = e.value;
            }
            return lib._map.TryGetValue(key, out var v) ? v as T : null;
        }

#if UNITY_EDITOR
        public const string Path = "Assets/Resources/AssetLib.asset";

        public static AssetLib LoadOrCreate()
        {
            var lib = UnityEditor.AssetDatabase.LoadAssetAtPath<AssetLib>(Path);
            if (lib) return lib;
            System.IO.Directory.CreateDirectory("Assets/Resources");
            lib = CreateInstance<AssetLib>();
            UnityEditor.AssetDatabase.CreateAsset(lib, Path);
            return lib;
        }

        /// Upsert one key. Only touch keys your workstream owns (see CONTRACT.md).
        public static void Put(string key, Object value)
        {
            var lib = LoadOrCreate();
            int i = lib.entries.FindIndex(e => e.key == key);
            var entry = new Entry { key = key, value = value };
            if (i >= 0) lib.entries[i] = entry; else lib.entries.Add(entry);
            lib._map = null;
            UnityEditor.EditorUtility.SetDirty(lib);
            UnityEditor.AssetDatabase.SaveAssetIfDirty(lib);
        }
#endif
    }
}
