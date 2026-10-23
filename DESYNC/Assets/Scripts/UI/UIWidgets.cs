using System;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Desync.UI
{
    /// Base of every full-screen layer. Build() creates the root (stretched, with a CanvasGroup) then Create() fills it.
    public abstract class UIPanel
    {
        public RectTransform Root;
        public CanvasGroup Group;
        public bool IsOn => Root && Root.gameObject.activeSelf;

        public void Build(Transform parent, string name)
        {
            Root = UIKit.Obj(parent, name);
            UIKit.Stretch(Root);
            Group = Root.gameObject.AddComponent<CanvasGroup>();
            Create();
            Root.gameObject.SetActive(false);
        }

        protected abstract void Create();
        public virtual void Show() { Root.gameObject.SetActive(true); Group.alpha = 1f; }
        public virtual void Hide() { Root.gameObject.SetActive(false); }
        public virtual void Tick(float dt) { }
    }

    /// Neon button. Acts on pointer DOWN so a browser pointer-lock request happens inside the user gesture.
    public sealed class UIButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler
    {
        public Action Clicked;
        public Image Fill, Frame;
        public TMP_Text Label;
        public Color Base = UIKit.Cyan;
        public Color Hot = UIKit.Magenta;
        public bool Selected;
        Tween _tw;

        public static UIButton Create(Transform parent, string name, string label, Anc a, Vector2 pos, Vector2 size, float fontSize, Color color, Action onClick, TMP_FontAsset font = null)
        {
            var bg = UIKit.Img(parent, name, new Color(UIKit.Ink.r, UIKit.Ink.g, UIKit.Ink.b, 0.85f));
            UIKit.Place(bg.rectTransform, a, pos, size);
            bg.raycastTarget = true;
            var fr = UIKit.Img(bg.transform, "Frame", color, UIKit.Frame, Image.Type.Sliced);
            UIKit.Stretch(fr.rectTransform);
            var t = UIKit.Text(bg.transform, "Label", font ? font : UIKit.Title, fontSize, color, TextAlignmentOptions.Center, label);
            t.fontStyle = FontStyles.Bold;
            UIKit.Stretch(t.rectTransform);
            var b = bg.gameObject.AddComponent<UIButton>();
            b.Fill = bg; b.Frame = fr; b.Label = t; b.Base = color; b.Clicked = onClick;
            b.Hot = color == UIKit.Magenta ? UIKit.Cyan : UIKit.Magenta;
            return b;
        }

        public void SetSelected(bool on) { Selected = on; Paint(false); }

        void Paint(bool hover)
        {
            var c = hover ? Hot : Base;
            Frame.color = c; Label.color = Selected ? UIKit.Ink : c;
            Fill.color = Selected ? new Color(Base.r, Base.g, Base.b, 0.95f) : (hover ? new Color(c.r, c.g, c.b, 0.22f) : new Color(UIKit.Ink.r, UIKit.Ink.g, UIKit.Ink.b, 0.85f));
        }

        public void OnPointerEnter(PointerEventData e) { Paint(true); UIKit.Kill(ref _tw); _tw = Tween.Scale(transform, 1.06f, 0.14f, Ease.OutBack, useUnscaledTime: true); }
        public void OnPointerExit(PointerEventData e) { Paint(false); UIKit.Kill(ref _tw); _tw = Tween.Scale(transform, 1f, 0.12f, Ease.OutQuad, useUnscaledTime: true); }

        public void OnPointerDown(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;
            UIKit.Kill(ref _tw);
            transform.localScale = Vector3.one * 0.93f;
            _tw = Tween.Scale(transform, 0.93f, 1.06f, 0.22f, Ease.OutBack, useUnscaledTime: true);
            Clicked?.Invoke();
        }

        void OnDisable() { UIKit.Kill(ref _tw); transform.localScale = Vector3.one; if (Fill) Paint(false); }
    }

    /// Toggle with ON/OFF visuals; Set() changes the value silently (no onValueChanged).
    public sealed class NeonToggle : MonoBehaviour
    {
        public Toggle T; public Image On; public TMP_Text State;
        public void Set(bool v) { T.SetIsOnWithoutNotify(v); Paint(v); }
        public void Paint(bool v) { State.SetText(v ? "ON" : "OFF"); State.color = v ? UIKit.Ink : UIKit.Cyan; On.enabled = v; }
    }

    public static class UIControls
    {
        /// Horizontal slider built from stock uGUI parts (drag / click handled by Slider itself).
        public static Slider NewSlider(Transform parent, string name, Anc a, Vector2 pos, float width, float min, float max, float value, UnityAction<float> onChange)
        {
            var rt = UIKit.Place(UIKit.Obj(parent, name), a, pos, new Vector2(width, 36f));
            var hit = rt.gameObject.AddComponent<Image>(); hit.color = new Color(0, 0, 0, 0); hit.raycastTarget = true;
            var track = UIKit.Img(rt, "Track", new Color(1f, 1f, 1f, 0.12f));
            var tr = UIKit.Place(track.rectTransform, Anc.C, Vector2.zero, new Vector2(width, 8f));
            tr.anchorMin = new Vector2(0f, 0.5f); tr.anchorMax = new Vector2(1f, 0.5f); tr.sizeDelta = new Vector2(0f, 8f);

            var fillArea = UIKit.Obj(rt, "Fill Area");
            fillArea.anchorMin = new Vector2(0f, 0.5f); fillArea.anchorMax = new Vector2(1f, 0.5f); fillArea.sizeDelta = new Vector2(0f, 8f); fillArea.anchoredPosition = Vector2.zero;
            var fill = UIKit.Img(fillArea, "Fill", UIKit.Cyan);
            fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = Vector2.one; fill.rectTransform.sizeDelta = Vector2.zero;

            var slide = UIKit.Obj(rt, "Handle Slide Area");
            UIKit.Stretch(slide, 9f, 0f, 9f, 0f);
            var handle = UIKit.Img(slide, "Handle", UIKit.Paper);
            handle.rectTransform.sizeDelta = new Vector2(18f, 0f);

            var s = rt.gameObject.AddComponent<Slider>();
            s.fillRect = fill.rectTransform; s.handleRect = handle.rectTransform; s.targetGraphic = handle;
            s.direction = Slider.Direction.LeftToRight; s.transition = Selectable.Transition.None;
            s.minValue = min; s.maxValue = max; s.SetValueWithoutNotify(value);
            s.onValueChanged.AddListener(onChange);
            s.navigation = new Navigation { mode = Navigation.Mode.None };
            return s;
        }

        /// Row toggle: label on the left, ON/OFF switch on the right.
        public static NeonToggle NewToggle(Transform parent, string name, string label, Anc a, Vector2 pos, float width, bool value, UnityAction<bool> onChange)
        {
            var rt = UIKit.Place(UIKit.Obj(parent, name), a, pos, new Vector2(width, 46f));
            var hit = rt.gameObject.AddComponent<Image>(); hit.color = new Color(0, 0, 0, 0); hit.raycastTarget = true;
            var lab = UIKit.Text(rt, "Label", UIKit.Hud, 28f, UIKit.Paper, TextAlignmentOptions.Left, label);
            UIKit.Place(lab.rectTransform, Anc.L, Vector2.zero, new Vector2(width - 120f, 46f));

            var box = UIKit.Img(rt, "Box", new Color(UIKit.Ink.r, UIKit.Ink.g, UIKit.Ink.b, 0.9f));
            UIKit.Place(box.rectTransform, Anc.R, Vector2.zero, new Vector2(104f, 38f));
            var fr = UIKit.Img(box.transform, "Frame", UIKit.Cyan, UIKit.Frame, Image.Type.Sliced); UIKit.Stretch(fr.rectTransform);
            var on = UIKit.Img(box.transform, "On", new Color(UIKit.Cyan.r, UIKit.Cyan.g, UIKit.Cyan.b, 0.9f)); UIKit.Stretch(on.rectTransform, 4, 4, 4, 4);
            var st = UIKit.Text(box.transform, "State", UIKit.Hud, 24f, UIKit.Ink, TextAlignmentOptions.Center, "OFF");
            st.fontStyle = FontStyles.Bold; UIKit.Stretch(st.rectTransform);

            var t = rt.gameObject.AddComponent<Toggle>();
            t.targetGraphic = hit; t.transition = Selectable.Transition.None; t.toggleTransition = Toggle.ToggleTransition.None;
            t.navigation = new Navigation { mode = Navigation.Mode.None };
            var n = rt.gameObject.AddComponent<NeonToggle>();
            n.T = t; n.On = on; n.State = st;
            n.Set(value);
            t.onValueChanged.AddListener(v => { n.Paint(v); onChange(v); });
            return n;
        }
    }
}
