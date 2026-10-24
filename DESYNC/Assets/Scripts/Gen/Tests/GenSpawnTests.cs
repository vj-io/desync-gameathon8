using System.Linq;
using NUnit.Framework;
using Desync.Gen;

// The player's first frame in a generated room: the spawn faces open floor, not a wall.
public class GenSpawnTests
{
    static ChunkLibrary Lib => _lib ??= ChunkLibrary.FromFolder("Assets/Resources/Content/chunks");
    static ChunkLibrary _lib;

    /// 200 seeds per act: every tile from the spawn to 2 m ahead along its yaw is walkable in RENDER (not '#', 'X', 'A' or void).
    [TestCase(1), TestCase(2), TestCase(3)]
    public void Spawn_FacesOpenFloor_2mAhead(int act)
    {
        int checkedRooms = 0;
        foreach (uint seed in Enumerable.Range(0, 200).Select(i => (uint)(i * 2654435761u + 3)))
        {
            var room = Generator.GenerateRoom(seed, act, (int)(seed % 4), 0, Lib);
            if (room.fallbackRoom != null) continue;
            checkedRooms++;
            var s = room.entities.Single(e => e.kind == EntityKind.Spawn);
            Assert.AreEqual(0f, s.yaw % 90f, $"seed {seed}: axis-aligned facing");
            int k = (int)(s.yaw / 90f) % 4;
            int dr = new[] { -1, 0, 1, 0 }[k], dc = new[] { 0, 1, 0, -1 }[k];
            int r = (int)(-s.z / 0.5f), c = (int)(s.x / 0.5f);
            for (int step = 1; step <= 4; step++)   // 4 tiles = 2 m
            {
                int rr = r + dr * step, cc = c + dc * step;
                char ch = rr >= 0 && rr < room.tiles.Length && cc >= 0 && cc < room.tiles[rr].Length ? room.tiles[rr][cc] : ' ';
                Assert.IsFalse("#XA ".IndexOf(ch) >= 0, $"act {act} seed {seed}: '{ch}' {step * 0.5f} m ahead of the spawn (yaw {s.yaw})");
            }
        }
        Assert.Greater(checkedRooms, 150);
    }
}
