using Desync.Flow;
using Desync.Net;
using UnityEngine;
using UnityEngine.UI;

namespace Desync.UI
{
    /// The flip effect cosmetic (Flow/Cosmetics.cs): earned in the Deep Layer, worn from the unlock screen. A small pool of UI images
    /// (rings, shards, streaks) that UIManager keeps above the HUD and feeds from GameEvents.Flipped. Nothing here allocates per flip or
    /// per frame, nothing blocks input (no raycast targets), and it draws only the palette's own hues (green for SOURCE, cyan for RENDER,
    /// or SYNC gold): never a threat hue. REDUCE FX and REDUCE FLASHING switch it off, like every other full-screen effect.
    public sealed class FlipFxLayer : UIPanel
    {
        const int Pool = 40;
        const float RingSize = 1000f;
        enum Kind : byte { None, Ring, Shard, Streak }

        readonly Image[] _img = new Image[Pool];
        readonly RectTransform[] _rt = new RectTransform[Pool];
        readonly Kind[] _kind = new Kind[Pool];
        readonly float[] _age = new float[Pool], _delay = new float[Pool], _life = new float[Pool], _from = new float[Pool], _to = new float[Pool],
                         _alpha = new float[Pool], _angle = new float[Pool];
        readonly Color[] _col = new Color[Pool];
        int _alive;

        /// Images in use right now (test hook).
        internal int Alive => _alive;

        protected override void Create()
        {
            for (int i = 0; i < Pool; i++)
            {
                var im = UIKit.Img(Root, "Fx" + i, Color.white);
                im.raycastTarget = false; im.enabled = false;
                UIKit.Place(im.rectTransform, Anc.C, Vector2.zero, new Vector2(10f, 10f));
                _img[i] = im; _rt[i] = im.rectTransform;
            }
        }

        /// The colour the effect `id` draws a flip in: SYNC gold, or the layer's own hue (the palette's, so colour-blind mode follows).
        internal static Color Tint(string id, bool toSource) => id == "sync_gold" ? UIKit.MedalColor(3) : toSource ? UIKit.Green : UIKit.Cyan;

        public void OnFlipped(bool toSource)
        {
            if (!Root || !UIKit.Flashes) return;
            var c = Cosmetics.Equipped(CosmeticSlot.Flip);
            if (c == null) return;
            var col = Tint(c.id, toSource);
            switch (c.id)
            {
                case "ripple": Ring(col, 0f, 0.42f, 0.10f, 1.00f, 0.55f); break;
                case "shards": Shards(col, 0f, 0.34f, 70f, 380f, 0.8f); break;
                case "afterglow": Ring(col, 0f, 0.5f, 0.15f, 1.0f, 0.35f); Ring(col, 0.12f, 0.5f, 0.15f, 1.25f, 0.22f); break;
                case "streak": Streak(col, 0f, 0.28f, 1500f, 0.5f, -1f); Streak(col, 0f, 0.28f, 1500f, 0.5f, 1f); break;
                case "sync_gold":   // the DL 30 trail: a ring, a streak each way and a burst of shards
                    Ring(col, 0f, 0.5f, 0.10f, 1.10f, 0.6f);
                    Streak(col, 0f, 0.36f, 1700f, 0.6f, -1f); Streak(col, 0f, 0.36f, 1700f, 0.6f, 1f);
                    Shards(col, 0.04f, 0.4f, 90f, 440f, 0.85f);
                    break;
            }
        }

        int Take()
        {
            for (int i = 0; i < Pool; i++) if (_kind[i] == Kind.None) return i;
            return -1;
        }

        void Start(int i, Kind k, Color col, float delay, float life, float from, float to, float alpha, float angle)
        {
            _kind[i] = k; _col[i] = col; _age[i] = 0f; _delay[i] = delay; _life[i] = life; _from[i] = from; _to[i] = to; _alpha[i] = alpha; _angle[i] = angle;
            _alive++;
        }

        void Ring(Color col, float delay, float life, float s0, float s1, float alpha)
        {
            int i = Take(); if (i < 0) return;
            var im = _img[i]; im.sprite = UIKit.Ring; im.type = Image.Type.Simple;
            _rt[i].sizeDelta = new Vector2(RingSize, RingSize); _rt[i].anchoredPosition = Vector2.zero; _rt[i].localRotation = Quaternion.identity;
            Start(i, Kind.Ring, col, delay, life, s0, s1, alpha, 0f);
        }

        void Shards(Color col, float delay, float life, float r0, float r1, float alpha)
        {
            for (int k = 0; k < 8; k++)
            {
                int i = Take(); if (i < 0) return;
                var im = _img[i]; im.sprite = UIKit.White; im.type = Image.Type.Simple;
                _rt[i].sizeDelta = new Vector2(46f, 7f); _rt[i].localScale = Vector3.one;
                Start(i, Kind.Shard, col, delay, life, r0, r1, alpha, k * 45f);
            }
        }

        /// A bar from the centre sideways (dir -1 left, +1 right) that grows to `length` and fades.
        void Streak(Color col, float delay, float life, float length, float alpha, float dir)
        {
            int i = Take(); if (i < 0) return;
            var im = _img[i]; im.sprite = UIKit.White; im.type = Image.Type.Simple;
            _rt[i].sizeDelta = new Vector2(10f, 5f); _rt[i].localRotation = Quaternion.identity; _rt[i].localScale = Vector3.one;
            Start(i, Kind.Streak, col, delay, life, 0f, length * dir, alpha, 0f);
        }

        public override void Tick(float dt)
        {
            if (_alive == 0) return;
            for (int i = 0; i < Pool; i++)
            {
                var k = _kind[i];
                if (k == Kind.None) continue;
                float age = _age[i] += dt;
                if (age < _delay[i]) continue;
                float t = (age - _delay[i]) / _life[i];
                if (t >= 1f) { _img[i].enabled = false; _kind[i] = Kind.None; _alive--; continue; }
                float e = 1f - (1f - t) * (1f - t);   // ease out
                float a = _alpha[i] * (1f - t) * (1f - t);
                var c = _col[i]; c.a = a;
                var im = _img[i]; im.color = c; im.enabled = true;
                var rt = _rt[i];
                switch (k)
                {
                    case Kind.Ring: rt.localScale = Vector3.one * Mathf.Lerp(_from[i], _to[i], e); break;
                    case Kind.Shard:
                    {
                        float r = Mathf.Lerp(_from[i], _to[i], e), rad = _angle[i] * Mathf.Deg2Rad;
                        rt.anchoredPosition = new Vector2(Mathf.Cos(rad) * r, Mathf.Sin(rad) * r);
                        rt.localRotation = Quaternion.Euler(0f, 0f, _angle[i]);
                        break;
                    }
                    case Kind.Streak:
                    {
                        float len = Mathf.Lerp(_from[i], _to[i], e);
                        rt.sizeDelta = new Vector2(Mathf.Abs(len), 5f);
                        rt.anchoredPosition = new Vector2(len * 0.5f, 0f);
                        break;
                    }
                }
            }
        }

        public override void Hide()
        {
            for (int i = 0; i < Pool; i++) { _kind[i] = Kind.None; if (_img[i]) _img[i].enabled = false; }
            _alive = 0;
            base.Hide();
        }
    }
}
