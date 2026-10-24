using System;
using System.Collections.Generic;
using Desync.Enemies;
using Desync.Gen;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Desync.Flow
{
    /// Boss arenas. Content/bosses/<id>.json is a RoomSpec (the generator's legend) plus "boss": {"id", "x", "z"}: where the
    /// boss stands, in room space (x east, z = -south). Each act's boss (RunUnlocks.Boss: auditor, compiler, architect) is
    /// built by its entry in Factories. An act whose arena file or factory is missing plays the AUDITOR in its arena instead
    /// (logged once), so a run never breaks while a boss is unfinished.
    public static class RunBosses
    {
        public const string Fallback = "auditor";

        /// Boss factories by id: (room host, world position of the arena's boss point) -> the boss root.
        public static readonly Dictionary<string, Func<Transform, Vector3, GameObject>> Factories = new Dictionary<string, Func<Transform, Vector3, GameObject>>
        {
            { "auditor", EnemyFactory.Auditor },
            { "compiler", EnemyFactory.Compiler },
            { "architect", EnemyFactory.ArchitectBoss },
        };

        public sealed class Arena
        {
            public string boss;        // the boss this arena spawns (the fallback's id when the act's own is missing)
            public RoomSpec spec;      // seeded with the node: seed, act, node
            public Vector2 at;         // boss point, room space (x, z)
            public int terminals;      // Terminal entities: the hacks that open the exit
        }

        static readonly HashSet<string> warned = new HashSet<string>();

        /// The arena of `node`'s act, or null when not even the AUDITOR arena can be read (the node then plays a generated room).
        public static Arena For(RunNode node)
        {
            string id = RunUnlocks.Boss(node.act);
            var a = Factories.ContainsKey(id) ? Load(id, node) : null;
            if (a == null && id != Fallback)
            {
                if (warned.Add(id)) Debug.LogWarning("[DESYNC] boss '" + id + "' has no " + (Factories.ContainsKey(id) ? "arena data" : "factory") + " yet: act " + node.act + " plays the AUDITOR");
                a = Load(Fallback, node);
            }
            if (a == null && warned.Add(Fallback)) Debug.LogWarning("[DESYNC] no AUDITOR arena data: boss nodes play a generated room");
            return a;
        }

        static Arena Load(string id, RunNode node)
        {
            var ta = Resources.Load<TextAsset>("Content/bosses/" + id);
            if (!ta) return null;
            try
            {
                var o = JObject.Parse(ta.text);
                var s = o.ToObject<RoomSpec>();
                if (s?.tiles == null || s.tiles.Length == 0) return null;
                s.seed = node.roomSeed;
                s.act = node.act;
                s.node = node.index;
                s.entities ??= new List<EntitySpawn>();
                if (s.gridW <= 0) s.gridW = s.tiles[0].Length / 16;
                if (s.gridH <= 0) s.gridH = s.tiles.Length / 16;
                var b = o["boss"];
                var at = b != null && b["x"] != null && b["z"] != null
                    ? new Vector2((float)b["x"], (float)b["z"])
                    : new Vector2(s.tiles[0].Length * 0.25f, -s.tiles.Length * 0.25f);   // no boss point: the arena's middle
                int terminals = 0;
                foreach (var e in s.entities) if (e.kind == EntityKind.Terminal) terminals++;
                return new Arena { boss = id, spec = s, at = at, terminals = terminals };
            }
            catch (Exception e)
            {
                Debug.LogWarning("[DESYNC] bosses/" + id + ".json unreadable: " + e.Message);
                return null;
            }
        }
    }
}
