using Desync.Gen;
using Desync.Rules;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Desync.Enemies.Tests
{
    /// M3 bosses at edit time: the COMPILER and ARCHITECT builds, their arenas (Content/bosses/compiler.json, architect.json)
    /// as valid RoomSpecs that agree with the bosses' offsets, the P3 area tables and the melt fold.
    public class BossM3EditTests
    {
        Transform _root;

        [SetUp] public void SetUp() => _root = new GameObject("BossM3TestRoot").transform;
        [TearDown] public void TearDown()
        {
            Object.DestroyImmediate(_root.gameObject);
            foreach (var t in Object.FindObjectsByType<TraceSystem>(FindObjectsInactive.Include)) Object.DestroyImmediate(t.gameObject);
        }

        [Test]
        public void CompilerBuildsAtEditTime()
        {
            var portal = EnemyFactory.ExitPortal(_root, new Vector3(0.25f, 0f, -8.25f), 0f);
            var c = EnemyFactory.Compiler(_root, Vector3.zero).GetComponent<Compiler>();
            Assert.IsFalse(portal.activeSelf, "the arena's own exit portal is switched off");
            Assert.AreEqual(2, c.scanners.Length);
            foreach (var s in c.scanners) Assert.IsFalse(s.bothLayers, "scanners see RENDER only");
            Assert.AreEqual(Compiler.ScanHeight, c.ring.position.y, 1e-4f);
            Assert.AreEqual(2, c.wallGroups.Length);
            for (int g = 0; g < 2; g++)
            {
                var cols = c.wallGroups[g].GetComponentsInChildren<BoxCollider>(true);
                Assert.AreEqual(2 * Compiler.Cells.Length, cols.Length, "two walls per cell per group");
                foreach (var w in cols) Assert.AreEqual(Compiler.WallHeight, w.size.y, 1e-4f);
                Assert.IsNotNull(c.wallRenderers[g]);
            }
            Assert.IsNotNull(c.renderSolidMat);
            Assert.IsNotNull(c.sourceSolidMat);
            Assert.IsNotNull(c.ghostMat);
            Assert.AreEqual(2, c.hunters.Length);
            for (int i = 0; i < 2; i++)
            {
                Assert.IsFalse(c.hunters[i].gameObject.activeSelf);
                Assert.AreEqual(Compiler.Loop.Length, c.hunters[i].patrol.Length);
                Assert.AreEqual(2 * i, c.hunters[i].startIndex, "opposite corners");
            }
            Assert.AreEqual(Compiler.CompiledMax, c.compiled.Length);
            foreach (var s in c.compiled) Assert.IsFalse(s.gameObject.activeSelf, "compiled Splitters wait for P2");
            Assert.AreEqual(Compiler.Regions.Length, c.voids.Length);
            foreach (var v in c.voids) Assert.IsFalse(v.activeSelf);
            Assert.IsFalse(c.exit.gameObject.activeSelf, "the layer exit waits for P3");
            Assert.AreEqual(1, c.Phase);
            Assert.AreEqual(1, Object.FindObjectsByType<TraceSystem>(FindObjectsInactive.Include).Length);
        }

        [Test]
        public void CompilerCollapseAreasTileTheArena()
        {
            Vector4 all = Compiler.Regions[0];
            float area = 0f;
            for (int k = 1; k <= Compiler.Regions.Length; k++)
            {
                Vector4 r = Compiler.Removed(k);
                area += (r.z - r.x) * (r.w - r.y);
                Vector2 mid = new Vector2((r.x + r.z) * 0.5f, (r.y + r.w) * 0.5f);
                Assert.IsTrue(Compiler.Inside(Compiler.Regions[k - 1], mid), "removed from what was live");
                if (k < Compiler.Regions.Length) Assert.IsFalse(Compiler.Inside(Compiler.Regions[k], mid), "and no longer live");
            }
            Assert.AreEqual((all.z - all.x) * (all.w - all.y), area, 1e-3f, "the removed areas add up to the arena");
            for (int k = 0; k < Compiler.Regions.Length; k++)
                Assert.IsTrue(Compiler.Inside(Compiler.Regions[k], Compiler.ExitSpots[k]), "exit " + k + " is in the live area");
        }

        [Test]
        public void ArchitectBuildsAtEditTime()
        {
            for (int i = 0; i < ArchitectBoss.Terminals; i++) EnemyFactory.Terminal(_root, new Vector3(-6f + 6f * i, 0f, 4.75f), 180f, 5, i);
            var portal = EnemyFactory.ExitPortal(_root, new Vector3(0.25f, 0f, -1.75f), 0f);
            var a = EnemyFactory.ArchitectBoss(_root, Vector3.zero).GetComponent<ArchitectBoss>();
            Assert.IsFalse(portal.activeSelf, "the arena's own exit portal is switched off");
            Assert.AreEqual(ArchitectBoss.Terminals, a.terminals.Length, "strings run to the terminals already in the room");
            Assert.AreEqual(ArchitectBoss.Terminals + 1, a.strings.Length, "plus the final string");
            foreach (var s in a.strings) Assert.IsFalse(s.enabled, "strings show in SOURCE only; the final one in P3");
            Assert.AreEqual(ArchitectBoss.Posts.Length, a.daemons.Length);
            foreach (var d in a.daemons)
            {
                Assert.IsFalse(d.gameObject.activeSelf, "P2 brings them");
                Assert.AreEqual(GameLayers.Ice, d.gameObject.layer);
            }
            Assert.IsNotNull(a.gazeCone);
            Assert.AreEqual(ArchitectBoss.EyeHeight, a.eye.position.y, 1e-4f);
            Assert.Less(Vector3.Angle(a.eye.forward, Vector3.back), ArchitectBoss.GazePitch + 0.1f, "the gaze starts looking south");
            Assert.AreEqual(1, a.Phase);
        }

        [Test]
        public void BossMeltFoldsIntoModifiers()
        {
            try
            {
                BossEvents.MeltSource = ArchitectBoss.MeltBonus;
                BossEvents.MeltRender = ArchitectBoss.MeltBonus;
                var m = BossEvents.Fold(Modifiers.Default);
                Assert.AreEqual(15f, m.SourceRise, "SOURCE +3 %/s");
                Assert.AreEqual(1f, m.RenderDrain, "RENDER: +3 %/s against the 4 %/s drain");
                BossEvents.MeltSource = Compiler.P2Melt;
                BossEvents.MeltRender = 0f;
                Assert.AreEqual(14f, BossEvents.Fold(Modifiers.Default).SourceRise);
            }
            finally { BossEvents.MeltSource = BossEvents.MeltRender = 0f; }
        }

        [Test]
        public void CompilerArenaIsAValidRoomSpecMatchingTheBoss()
        {
            var spec = Arena("compiler", Compiler.ArenaCenter, Compiler.Nodes);
            Vector2 c = Compiler.ArenaCenter;
            int n = 0;
            foreach (var e in spec.entities)
            {
                if (e.kind == EntityKind.Exit)
                {
                    Assert.AreEqual(c.x + Compiler.ExitSpots[0].x, e.x, 1e-4f, "the arena exit marks the first P3 exit");
                    Assert.AreEqual(c.y + Compiler.ExitSpots[0].y, e.z, 1e-4f);
                }
                if (e.kind != EntityKind.Terminal) continue;
                Assert.AreEqual(c.x + Compiler.Cells[n].x, e.x, 1e-4f, "one node per cell, in order");
                Assert.AreEqual(c.y + Compiler.Cells[n].y, e.z, 1e-4f);
                n++;
            }
            foreach (var cell in Compiler.Cells)   // the swap walls stand on open floor: they are the boss's, not the room's
                for (float x = cell.x - Compiler.CellHalf; x <= cell.x + Compiler.CellHalf + 0.01f; x += 0.5f)   // tile centres
                    for (float z = cell.y - Compiler.CellHalf; z <= cell.y + Compiler.CellHalf + 0.01f; z += 0.5f)
                    {
                        bool wall = Mathf.Abs(Mathf.Abs(x - cell.x) - Compiler.CellHalf) < 0.1f || Mathf.Abs(Mathf.Abs(z - cell.y) - Compiler.CellHalf) < 0.1f;
                        if (wall) Assert.AreEqual('.', Tile(spec, c.x + x, c.y + z), $"swap wall tile at {x},{z}");
                    }
            for (int i = 0; i < Compiler.Loop.Length; i++)
                Assert.AreEqual(i % 2 == 0 ? 'I' : 'P', Tile(spec, c.x + Compiler.Loop[i].x, c.y + Compiler.Loop[i].y));
            foreach (var o in Compiler.ExitSpots) Assert.AreNotEqual('X', Tile(spec, c.x + o.x, c.y + o.y), "exit spots on open floor");
            Assert.AreEqual('X', Tile(spec, c.x, c.y), "the core is solid");
        }

        [Test]
        public void ArchitectArenaIsAValidRoomSpecMatchingTheBoss()
        {
            var spec = Arena("architect", ArchitectBoss.ArenaCenter, ArchitectBoss.Terminals);
            Vector2 c = ArchitectBoss.ArenaCenter;
            Assert.AreEqual('X', Tile(spec, c.x, c.y), "the face's plinth");
            Assert.AreEqual('.', Tile(spec, c.x, c.y - ArchitectBoss.EyeForward), "the gaze starts in front of it");
            foreach (var o in ArchitectBoss.Posts) Assert.AreEqual('I', Tile(spec, c.x + o.x, c.y + o.y));
            foreach (var e in spec.entities)
                if (e.kind == EntityKind.Terminal) Assert.Greater(e.z, c.y, "the puppet-string terminals are behind the face");
        }

        /// The P3 split (BossDesync.hlsl, included by PostFX.shader) compiles for Metal (editor) and GLES3 (WebGL2).
        [Test]
        public void PostFxWithTheDesyncSplitCompiles()
        {
            var shader = Shader.Find("Desync/PostFX");
            Assert.IsNotNull(shader);
            Assert.IsFalse(UnityEditor.ShaderUtil.ShaderHasError(shader), "PostFX has errors");
            var pass = UnityEditor.ShaderUtil.GetShaderData(shader).GetSubshader(0).GetPass(0);
            foreach (var plat in new[] { (UnityEditor.Rendering.ShaderCompilerPlatform.Metal, UnityEditor.BuildTarget.StandaloneOSX),
                                         (UnityEditor.Rendering.ShaderCompilerPlatform.GLES3x, UnityEditor.BuildTarget.WebGL) })
            {
                var r = pass.CompileVariant(UnityEditor.Rendering.ShaderType.Fragment, new string[0], plat.Item1, plat.Item2);
                string msg = "";
                foreach (var m in r.Messages) msg += m.message + "\n";
                Assert.IsTrue(r.Success, plat.Item1 + ": " + msg);
            }
        }

        /// Shape, entity tiles, boss marker and Generator.Validate shared by both arenas.
        static RoomSpec Arena(string id, Vector2 center, int terminals)
        {
            var text = Resources.Load<TextAsset>("Content/bosses/" + id).text;
            var spec = JsonConvert.DeserializeObject<RoomSpec>(text);
            Assert.IsNull(spec.fallbackRoom);
            Assert.AreEqual(spec.gridH * 16, spec.tiles.Length);
            foreach (var row in spec.tiles) Assert.AreEqual(spec.gridW * 16, row.Length);
            int t = 0, spawns = 0, exits = 0;
            foreach (var e in spec.entities)
            {
                if (e.kind == EntityKind.Terminal) t++;
                if (e.kind == EntityKind.Spawn) spawns++;
                if (e.kind == EntityKind.Exit) exits++;
                Assert.AreEqual(e.kind == EntityKind.Spawn ? 'S' : e.kind == EntityKind.Exit ? 'E' : 'T', Tile(spec, e.x, e.z), e.id);
            }
            Assert.AreEqual(terminals, t);
            Assert.AreEqual(1, spawns);
            Assert.AreEqual(1, exits);
            var boss = JObject.Parse(text)["boss"];
            Assert.AreEqual(id, (string)boss["id"]);
            Assert.AreEqual(center.x, (float)boss["x"]);
            Assert.AreEqual(center.y, (float)boss["z"]);

            var v = Generator.Validate(spec);
            Debug.Log($"[DESYNC] {id} arena: best path {v.bestPathSeconds:0.0} s, melt peak {v.minMeltPeak:0} %, par {spec.par}");
            Assert.IsTrue(v.reachable);
            Assert.IsEmpty(v.reasons, string.Join(",", v.reasons));
            return spec;
        }

        /// Tile char under a room-space point (x east, z = -south; 0.5 m tiles, row 0 = north).
        static char Tile(RoomSpec s, float x, float z) => s.tiles[Mathf.FloorToInt(-z / 0.5f)][Mathf.FloorToInt(x / 0.5f)];
    }
}
