using System;
using UnityEngine;

namespace Desync.Player
{
    /// A recorded run for Time-Attack ghosts: feet position, yaw and layer sampled at 20 Hz into fixed arrays (no allocation
    /// while recording or playing back), plus the compact text form a profile stores.
    ///
    /// Stored form (base64): a 24-byte header (version, stride, sample count, room layout hash, clear time, start x/y/z)
    /// then 2 bytes per sample: dx and dz as 5-bit signed steps of 2 cm x stride, yaw in 32 steps, 1 bit for SOURCE.
    /// Steps are taken from the reconstructed position, so rounding never drifts; a jump faster than 6 m/s is caught up over
    /// the next samples. Height is the start height (rooms are flat). Runs longer than MaxStored samples keep every
    /// stride-th sample, so one ghost never exceeds ~3.2 KB of text.
    public sealed class GhostTrack
    {
        public const float Hz = 20f, Step = 1f / Hz;
        /// Recording buffer: 10 minutes at 20 Hz. A longer attempt simply stops recording.
        public const int RecordCapacity = 12000;
        /// Most samples one stored ghost keeps (60 s at full rate).
        public const int MaxStored = 1200;
        public const byte Version = 1;
        const int Header = 24, StepMax = 15, StepMin = -16, YawSteps = 32;
        const float Unit = 0.02f, YawStep = 360f / YawSteps;

        readonly float[] _x, _y, _z, _yaw;
        readonly bool[] _src;

        public int Count { get; private set; }
        /// Seconds between samples (Step, or Step x stride for a decoded long run).
        public float Interval { get; private set; } = Step;
        /// Clear time of the stored run (0 while recording).
        public float RunTime { get; private set; }
        public int Capacity => _x.Length;
        public float Duration => Count > 1 ? (Count - 1) * Interval : 0f;

        public GhostTrack(int capacity)
        {
            _x = new float[capacity]; _y = new float[capacity]; _z = new float[capacity]; _yaw = new float[capacity];
            _src = new bool[capacity];
        }

        public void Clear() { Count = 0; Interval = Step; RunTime = 0f; }

        /// Appends one sample; false when the buffer is full.
        public bool Add(Vector3 feet, float yaw, bool source)
        {
            if (Count >= _x.Length) return false;
            _x[Count] = feet.x; _y[Count] = feet.y; _z[Count] = feet.z; _yaw[Count] = yaw; _src[Count] = source;
            Count++;
            return true;
        }

        /// The ghost at time t (seconds from the room start): position and yaw interpolated, layer of the sample before t.
        /// Past the end it holds the last sample. False when empty.
        public bool Sample(float t, out Vector3 feet, out float yaw, out bool source)
        {
            feet = default; yaw = 0f; source = false;
            if (Count == 0) return false;
            float f = Mathf.Max(0f, t / Interval);
            int i = (int)f;
            if (i >= Count - 1)
            {
                i = Count - 1;
                feet = new Vector3(_x[i], _y[i], _z[i]); yaw = _yaw[i]; source = _src[i];
                return true;
            }
            float k = f - i;
            feet = new Vector3(Mathf.Lerp(_x[i], _x[i + 1], k), Mathf.Lerp(_y[i], _y[i + 1], k), Mathf.Lerp(_z[i], _z[i + 1], k));
            yaw = Mathf.LerpAngle(_yaw[i], _yaw[i + 1], k);
            source = _src[i];
            return true;
        }

        /// The stored form of this recording. `layout` identifies the room geometry (a ghost of another layout is refused on
        /// decode), `runTime` is the clear time it belongs to. Allocates: call once per saved best, never per frame.
        public string Encode(uint layout, float runTime)
        {
            if (Count == 0) return null;
            int stride = (Count + MaxStored - 1) / MaxStored;
            int n = (Count + stride - 1) / stride;
            var b = new byte[Header + 2 * n];
            b[0] = Version;
            b[1] = (byte)stride;
            b[2] = (byte)n; b[3] = (byte)(n >> 8);
            PutU32(b, 4, layout);
            PutF32(b, 8, runTime);
            PutF32(b, 12, _x[0]); PutF32(b, 16, _y[0]); PutF32(b, 20, _z[0]);
            float u = Unit * stride, rx = _x[0], rz = _z[0];
            for (int k = 0; k < n; k++)
            {
                int i = k * stride;
                int dx = Quant(_x[i] - rx, u), dz = Quant(_z[i] - rz, u);
                rx += dx * u; rz += dz * u;
                int yaw = Mathf.RoundToInt(Mathf.Repeat(_yaw[i], 360f) / YawStep) & (YawSteps - 1);
                int v = (dx & 31) | (dz & 31) << 5 | yaw << 10 | (_src[i] ? 1 << 15 : 0);
                b[Header + 2 * k] = (byte)v; b[Header + 2 * k + 1] = (byte)(v >> 8);
            }
            return Convert.ToBase64String(b);
        }

        /// Loads a stored ghost. False (and empty) for null, unreadable or other-version text, a different `layout`, or more
        /// samples than this track holds.
        public bool Decode(string text, uint layout)
        {
            Clear();
            if (string.IsNullOrEmpty(text)) return false;
            byte[] b;
            try { b = Convert.FromBase64String(text); }
            catch (FormatException) { return false; }
            if (b.Length < Header || b[0] != Version || b[1] == 0 || GetU32(b, 4) != layout) return false;
            int stride = b[1], n = b[2] | b[3] << 8;
            if (n == 0 || n > _x.Length || b.Length != Header + 2 * n) return false;
            float u = Unit * stride, x = GetF32(b, 12), y = GetF32(b, 16), z = GetF32(b, 20);
            for (int k = 0; k < n; k++)
            {
                int v = b[Header + 2 * k] | b[Header + 2 * k + 1] << 8;
                x += Signed5(v) * u; z += Signed5(v >> 5) * u;
                _x[k] = x; _y[k] = y; _z[k] = z; _yaw[k] = (v >> 10 & (YawSteps - 1)) * YawStep; _src[k] = (v & 1 << 15) != 0;
            }
            Count = n;
            Interval = Step * stride;
            RunTime = GetF32(b, 8);
            return true;
        }

        static int Quant(float d, float u) => Mathf.Clamp(Mathf.RoundToInt(d / u), StepMin, StepMax);
        static int Signed5(int v) { v &= 31; return v >= 16 ? v - 32 : v; }

        static void PutU32(byte[] b, int at, uint v) { b[at] = (byte)v; b[at + 1] = (byte)(v >> 8); b[at + 2] = (byte)(v >> 16); b[at + 3] = (byte)(v >> 24); }
        static uint GetU32(byte[] b, int at) => (uint)(b[at] | b[at + 1] << 8 | b[at + 2] << 16 | b[at + 3] << 24);
        static void PutF32(byte[] b, int at, float v) => PutU32(b, at, unchecked((uint)BitConverter.SingleToInt32Bits(v)));
        static float GetF32(byte[] b, int at) => BitConverter.Int32BitsToSingle(unchecked((int)GetU32(b, at)));
    }
}
