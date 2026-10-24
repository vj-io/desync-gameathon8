using System;
using Desync.Flow;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Utilities;

namespace Desync.UI
{
    /// Key remapping for the game's own verbs: move (4 directions), flip, hack (interact), restart, pause and the map / goals key.
    /// Overrides are applied to the existing GameInput / UIKit actions (Core stays untouched) and saved one string per slot as
    /// "a11y_bind_<id>_k" (keyboard) / "a11y_bind_<id>_p" (pad) in SaveV3.settings; a slot at its default has no key at all.
    /// FLIP is one action with one keyboard key and one pad button: no other verb can take its key, because binding a key that
    /// another slot already uses swaps the two instead of duplicating it.
    public static class Rebinds
    {
        public sealed class Slot
        {
            public readonly string Id, Label;
            public readonly Func<InputAction> Action;
            public readonly string Part;      // composite part name ("Up") for the move directions, else null
            public readonly bool PadRebindable;
            public Slot(string id, string label, Func<InputAction> action, string part, bool pad) { Id = id; Label = label; Action = action; Part = part; PadRebindable = pad; }
        }

        public static readonly Slot[] Slots =
        {
            new Slot("up", "MOVE FORWARD", () => GameInput.Move, "Up", false),
            new Slot("down", "MOVE BACK", () => GameInput.Move, "Down", false),
            new Slot("left", "MOVE LEFT", () => GameInput.Move, "Left", false),
            new Slot("right", "MOVE RIGHT", () => GameInput.Move, "Right", false),
            new Slot("flip", "FLIP LAYER", () => GameInput.Flip, null, true),
            new Slot("hack", "HACK", () => GameInput.Interact, null, true),
            new Slot("restart", "RESTART ROOM", () => GameInput.Restart, null, true),
            new Slot("pause", "PAUSE", () => GameInput.Pause, null, true),
            new Slot("map", "MAP / GOALS", () => UIKit.GoalsKey, null, false),
        };

        /// Raised after any change (the HUD key labels repaint); Version lets a screen that is not subscribed notice it.
        public static Action Changed;
        public static int Version { get; private set; }

        static void Notify() { Version++; Changed?.Invoke(); }

        public static Slot Find(string id) { foreach (var s in Slots) if (s.Id == id) return s; return null; }

        static string Key(Slot s, bool pad) => A11y.BindPrefix + s.Id + (pad ? "_p" : "_k");

        /// Index of the binding this slot edits on this device, or -1 (no such binding: a move direction on the pad).
        static int Index(Slot s, bool pad)
        {
            if (pad && !s.PadRebindable) return -1;
            var a = s.Action();
            var b = a.bindings;
            for (int i = 0; i < b.Count; i++)
            {
                if (s.Part != null) { if (b[i].isPartOfComposite && b[i].name == s.Part) return i; continue; }
                if (b[i].isComposite || b[i].isPartOfComposite) continue;
                if (b[i].path.StartsWith(pad ? "<Gamepad>" : "<Keyboard>", StringComparison.Ordinal)) return i;
            }
            return -1;
        }

        /// The control path in force ("<Keyboard>/space"), or null when the slot has none on that device.
        public static string Current(Slot s, bool pad) { int i = Index(s, pad); return i < 0 ? null : s.Action().bindings[i].effectivePath; }

        public static string Default(Slot s, bool pad) { int i = Index(s, pad); return i < 0 ? null : s.Action().bindings[i].path; }

        public static bool IsDefault(Slot s, bool pad) => string.Equals(Current(s, pad), Default(s, pad), StringComparison.Ordinal);

        /// True for a path the slot may take: a keyboard key for the keyboard column, a gamepad button for the pad column.
        public static bool Accepts(bool pad, string path) =>
            !string.IsNullOrEmpty(path) && path.StartsWith(pad ? "<Gamepad>/" : "<Keyboard>/", StringComparison.Ordinal)
            && (pad || path != "<Keyboard>/escape")   // Esc stays the "cancel" of the capture and of the pause flow
            && !path.Contains("Stick") && !path.Contains("stick") && !path.EndsWith("Trigger", StringComparison.Ordinal);

        /// Binds `path` to the slot. A key another slot already holds on that device moves to this slot's old key (swap), so no
        /// two verbs ever share a key. False (nothing changed) when the path is not allowed, or the swap would hand Esc to a verb
        /// that cannot have it. `swapped` is the slot that took this slot's old key, or null. Saves and raises Changed.
        public static bool Set(Slot s, bool pad, string path, out Slot swapped)
        {
            swapped = null;
            int i = Index(s, pad);
            if (i < 0 || string.IsNullOrEmpty(path)) return false;
            if (!Accepts(pad, path) && !string.Equals(path, s.Action().bindings[i].path, StringComparison.Ordinal)) return false;
            string old = s.Action().bindings[i].effectivePath;
            if (string.Equals(old, path, StringComparison.OrdinalIgnoreCase)) return true;
            foreach (var o in Slots)
            {
                if (o == s) continue;
                int j = Index(o, pad);
                if (j < 0 || !string.Equals(o.Action().bindings[j].effectivePath, path, StringComparison.OrdinalIgnoreCase)) continue;
                if (!Accepts(pad, old) && !string.Equals(old, o.Action().bindings[j].path, StringComparison.Ordinal)) return false;
                Write(o, pad, j, old);
                swapped = o;
                break;
            }
            Write(s, pad, i, path);
            Notify();
            return true;
        }

        static void Write(Slot s, bool pad, int index, string path)
        {
            var a = s.Action();
            if (string.Equals(path, a.bindings[index].path, StringComparison.Ordinal))
            {
                a.RemoveBindingOverride(index);
                A11y.Set(Key(s, pad), null);
            }
            else
            {
                a.ApplyBindingOverride(index, path);
                A11y.Set(Key(s, pad), path);
            }
        }

        public static void Reset(Slot s, bool pad)
        {
            if (Index(s, pad) >= 0) Set(s, pad, Default(s, pad), out _);
        }

        /// Every slot back to its default key and button.
        public static void ResetAll()
        {
            foreach (var s in Slots)
                for (int d = 0; d < 2; d++)
                {
                    int i = Index(s, d == 1);
                    if (i < 0) continue;
                    s.Action().RemoveBindingOverride(i);
                    A11y.Set(Key(s, d == 1), null);
                }
            Notify();
        }

        /// Applies the saved overrides (boot, and after the profile is replaced). A bad stored value is ignored.
        public static void LoadAll()
        {
            foreach (var s in Slots)
                for (int d = 0; d < 2; d++)
                {
                    bool pad = d == 1;
                    int i = Index(s, pad);
                    if (i < 0) continue;
                    string v = A11y.Get(Key(s, pad));
                    var a = s.Action();
                    if (!string.IsNullOrEmpty(v) && Accepts(pad, v)) a.ApplyBindingOverride(i, v);
                    else a.RemoveBindingOverride(i);
                }
            Notify();
        }

        /// "SPACE", "LEFT ARROW", "A" ... for a control path; "-" for none.
        public static string Label(string path)
        {
            if (string.IsNullOrEmpty(path)) return "-";
            int slash = path.IndexOf('/');
            string ctrl = slash >= 0 ? path.Substring(slash + 1) : path;
            switch (ctrl)
            {
                case "buttonSouth": return "A";
                case "buttonEast": return "B";
                case "buttonWest": return "X";
                case "buttonNorth": return "Y";
                case "escape": return "ESC";
                case "start": return "START";
                case "select": return "SELECT";
                case "leftShoulder": return "LB";
                case "rightShoulder": return "RB";
                case "leftStickPress": return "L3";
                case "rightStickPress": return "R3";
                case "dpad/up": return "D-UP";
                case "dpad/down": return "D-DOWN";
                case "dpad/left": return "D-LEFT";
                case "dpad/right": return "D-RIGHT";
            }
            string h = InputControlPath.ToHumanReadableString(path, InputControlPath.HumanReadableStringOptions.OmitDevice);
            return string.IsNullOrEmpty(h) ? ctrl.ToUpperInvariant() : h.ToUpperInvariant();
        }

        public static string Label(Slot s, bool pad) => Label(Current(s, pad));

        /// `s` with "SPACE" naming the flip key as bound now: for story prompts and skip hints whose SPACE means FLIP (not the
        /// fixed menu confirm). Allocates when the key is rebound: call when the text is shown.
        public static string FlipText(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            string key = Label(Find("flip"), false);
            return key == "SPACE" ? s : s.Replace("SPACE", key);
        }

        /// "WASD" while the four directions are at their defaults, else their keys joined ("I J K L").
        public static string MoveLabel()
        {
            var u = Find("up"); var d = Find("down"); var l = Find("left"); var r = Find("right");
            if (IsDefault(u, false) && IsDefault(d, false) && IsDefault(l, false) && IsDefault(r, false)) return "WASD";
            return Label(u, false) + " " + Label(l, false) + " " + Label(d, false) + " " + Label(r, false);
        }

        // ------------------------------------------------------------------ capture of the next key / button
        static IDisposable capture;
        static string captured;
        /// The last control the capture saw and what it made of it (diagnostics for the tests).
        public static string LastSeen { get; private set; }
        public static bool Capturing => capture != null;

        /// Starts waiting for the next press on the keyboard (pad = false) or a gamepad (pad = true). Mouse and other devices are
        /// ignored; Esc on the keyboard cancels. The result is read with TakeCapture on a later frame (the input event that
        /// delivered it must not rebind actions from inside its own callback).
        public static void Capture(bool pad)
        {
            Cancel();
            captured = null;
            capture = InputSystem.onAnyButtonPress.Call(c =>
            {
                string path = PathOf(c, pad);
                LastSeen = c.path + " -> " + (path ?? "ignored");
                if (path == null) return;
                captured = !pad && path == "<Keyboard>/escape" ? "" : path;   // "" = cancelled
                capture?.Dispose(); capture = null;
            });
        }

        /// The finished capture: true once, with the binding path ("" when cancelled).
        public static bool TakeCapture(out string path)
        {
            path = captured;
            if (path == null) return false;
            captured = null;
            return true;
        }

        public static void Cancel() { capture?.Dispose(); capture = null; captured = null; }

        /// "<Keyboard>/space", "<Gamepad>/buttonSouth" for a control on the wanted device kind, else null.
        public static string PathOf(InputControl c, bool pad)
        {
            if (c == null || !(c is ButtonControl)) return null;
            var dev = c.device;
            if (pad ? !(dev is Gamepad) : !(dev is Keyboard)) return null;
            return (pad ? "<Gamepad>/" : "<Keyboard>/") + c.path.Substring(dev.path.Length + 1);
        }
    }
}
