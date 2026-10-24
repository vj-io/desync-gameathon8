using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Desync.Gen;
using UnityEngine;
using UnityEngine.AI;
using Debug = UnityEngine.Debug;

namespace Desync.Levels
{
    /// A generated room built by RoomBuilder.BuildAsync (component on the room root). Dispose() destroys the room and
    /// frees what the build created: every mesh in it that was not already loaded when the build started (geometry,
    /// entity meshes, text) and the NavMesh data. Use Dispose, not Destroy, to swap rooms.
    public class RoomHandle : MonoBehaviour, IDisposable
    {
        public RoomSpec Spec { get; internal set; }
        public LevelRoomInfo Info { get; internal set; }
        public GameObject Player { get; internal set; }
        public bool Ready { get; private set; }
        public int Tris { get; internal set; }
        /// Upper bound: one per material slot of every enabled renderer (batching not counted).
        public int DrawCalls { get; internal set; }
        /// Build cost on the main thread: frames used, the most work done in one of them, and the wall time to Ready.
        public int Frames { get; private set; }
        public float WorstFrameMs { get; private set; }
        /// The build step that ended the worst frame.
        public string WorstStep { get; private set; }
        public float BuildMs { get; private set; }

        internal string Step;
        internal NavMeshData NavData;
        internal HashSet<Mesh> AssetMeshes;
        bool _disposed;

        /// Drives the build: runs work items until SliceMs is used up, then yields a frame; waits out AsyncOperations.
        internal IEnumerator Run(IEnumerator work, Action<RoomHandle> done)
        {
            var total = Stopwatch.StartNew();
            yield return null;   // a room disposed in the caller's frame is gone (with its TraceSystem) before sensors look for one
            var slice = Stopwatch.StartNew();
            while (work.MoveNext())
            {
                if (work.Current is AsyncOperation op)
                {
                    EndFrame(slice);
                    while (!op.isDone) yield return null;
                    slice.Restart();
                }
                else if (slice.Elapsed.TotalMilliseconds >= RoomBuilder.SliceMs)
                {
                    EndFrame(slice);
                    yield return null;
                    slice.Restart();
                }
            }
            EndFrame(slice);
            BuildMs = (float)total.Elapsed.TotalMilliseconds;
            Ready = true;
            Debug.Log($"[DESYNC] LEVELS room {Spec.seed} act {Spec.act} node {Spec.node}: tris {Tris}, draw calls <= {DrawCalls}, " +
                      $"built in {BuildMs:0} ms over {Frames} frames, worst frame {WorstFrameMs:0.0} ms ({WorstStep})");
            done?.Invoke(this);
        }

        void EndFrame(Stopwatch slice)
        {
            Frames++;
            float ms = (float)slice.Elapsed.TotalMilliseconds;
            if (ms <= WorstFrameMs) return;
            WorstFrameMs = ms;
            WorstStep = Step;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopAllCoroutines();
            var freed = new HashSet<Mesh>();
            foreach (var f in GetComponentsInChildren<MeshFilter>(true)) Free(f.sharedMesh, freed);
            foreach (var s in GetComponentsInChildren<SkinnedMeshRenderer>(true)) Free(s.sharedMesh, freed);
            if (NavData) Destroy(NavData);
            Destroy(gameObject);
        }

        void Free(Mesh m, HashSet<Mesh> freed)
        {
            if (m && (AssetMeshes == null || !AssetMeshes.Contains(m)) && freed.Add(m)) Destroy(m);
        }
    }
}
