using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace PSXRacing
{
    /// <summary>One controller as plain numbers: what the WHEEL SETUP captures
    /// from and what <see cref="WheelInput"/> reads. Axes are -1..1 (a pedal
    /// may rest at either end), buttons 0..1.</summary>
    public sealed class RawPad
    {
        /// <summary>The device's own name - the Gamepad API id in a browser,
        /// the product string elsewhere. Bindings are kept against it.</summary>
        public string id = "";
        /// <summary>0 for the first device with this id, 1 for a second
        /// identical one, and so on.</summary>
        public int ord;
        /// <summary>Mapped as a standard pad (an ordinary controller).</summary>
        public bool standard;
        public float[] axes = new float[0];
        public float[] buttons = new float[0];

        public float Axis(int i) => i >= 0 && i < axes.Length ? axes[i] : 0f;
        public float Button(int i) => i >= 0 && i < buttons.Length ? buttons[i] : 0f;
    }

    /// <summary>
    /// EVERY CONTROLLER THE GAME CAN SEE, as raw axes and buttons - the layer a
    /// steering wheel, pedals, a handbrake or a button box are read through.
    ///
    /// The ordinary path (PlayerCarInput's <c>Gamepad.current</c>) only knows
    /// standard pads: a wheel has no "left stick", so the Input System either
    /// ignores it or guesses at a layout, and the guess is different on every
    /// platform. This reads the numbers instead and lets the player say what
    /// each one is (<see cref="WheelCapture"/>).
    ///
    ///   WebGL   : navigator.getGamepads() through PSXGamepadIO.jslib, once a
    ///             frame - every pad, standard or not, by its browser id.
    ///   elsewhere (the editor, a desktop build): every Input System device
    ///             that is not a keyboard, mouse, touchscreen or standard
    ///             gamepad (HID joysticks: wheels, pedals, shifters), with its
    ///             axis and button controls in the device's own order.
    ///
    /// <see cref="Override"/> replaces both, for the self-tests.
    /// </summary>
    public static class RawPads
    {
        static readonly List<RawPad> pads = new List<RawPad>();
        static int polledFrame = -1;

        /// <summary>A scripted set of pads that replaces the real ones (tests).</summary>
        public static List<RawPad> Override;

        /// <summary>The pads as of this frame (polled on first use each frame).</summary>
        public static IReadOnlyList<RawPad> All
        {
            get { Poll(); return Override ?? pads; }
        }

        public static RawPad Find(string id, int ord)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var all = All;
            for (int i = 0; i < all.Count; i++)
                if (all[i].ord == ord && all[i].id == id) return all[i];
            return null;
        }

        /// <summary>A short name for a menu: the browser's "(Vendor: ... Product: ...)"
        /// and "(STANDARD GAMEPAD ...)" tails cut off, upper case.</summary>
        public static string ShortName(string id, int max = 18)
        {
            if (string.IsNullOrEmpty(id)) return "NONE";
            string s = id;
            int p = s.IndexOf('(');
            if (p > 2) s = s.Substring(0, p);
            s = s.Trim().ToUpperInvariant();
            if (s.Length == 0) s = "CONTROLLER";
            return s.Length > max ? s.Substring(0, max).TrimEnd() : s;
        }

        public static void Poll()
        {
            if (polledFrame == Time.frameCount && Time.frameCount > 0) return;
            polledFrame = Time.frameCount;
            if (Override != null) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            PollBrowser();
#else
            PollInputSystem();
#endif
        }

        /// <summary>Force a re-read on the next access (a test that changed
        /// device state within one frame).</summary>
        public static void Invalidate() => polledFrame = -1;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int PSXPadPoll(float[] buf, int cap);
        [DllImport("__Internal")] static extern string PSXPadId(int index);

        const int Cap = 2048;
        static readonly float[] buf = new float[Cap];
        static int gen = -1;
        static readonly Dictionary<int, string> idByIndex = new Dictionary<int, string>();

        static void PollBrowser()
        {
            int g;
            try { g = PSXPadPoll(buf, Cap); }
            catch { pads.Clear(); return; }
            bool idsChanged = g != gen;
            gen = g;
            if (idsChanged) idByIndex.Clear();
            int n = Mathf.Clamp((int)buf[0], 0, 16), w = 1;
            while (pads.Count > n) pads.RemoveAt(pads.Count - 1);
            for (int p = 0; p < n; p++)
            {
                if (w + 4 > Cap) break;
                int index = (int)buf[w], na = (int)buf[w + 2], nb = (int)buf[w + 3];
                bool std = buf[w + 1] > 0.5f;
                w += 4;
                if (p >= pads.Count) pads.Add(new RawPad());
                var pad = pads[p];
                if (!idByIndex.TryGetValue(index, out var id))
                {
                    try { id = PSXPadId(index) ?? ""; } catch { id = ""; }
                    idByIndex[index] = id;
                }
                pad.id = id;
                pad.standard = std;
                if (pad.axes.Length != na) pad.axes = new float[na];
                if (pad.buttons.Length != nb) pad.buttons = new float[nb];
                for (int a = 0; a < na; a++) pad.axes[a] = buf[w + a];
                w += na;
                for (int b = 0; b < nb; b++) pad.buttons[b] = buf[w + b];
                w += nb;
            }
            AssignOrdinals(pads);
        }
#endif

        // ---- the Input System backend --------------------------------------

        sealed class DeviceMap
        {
            public InputDevice device;
            public readonly List<AxisControl> axes = new List<AxisControl>();
            public readonly List<ButtonControl> buttons = new List<ButtonControl>();
        }

        static readonly List<DeviceMap> maps = new List<DeviceMap>();
        static int mappedChange = -1;
        static int deviceChanges;
        static bool hooked;

        /// <summary>A controller the raw layer reads: not a keyboard, mouse,
        /// pen or touchscreen, and not a pad the ordinary path already reads.</summary>
        public static bool IsRawDevice(InputDevice d)
        {
            if (d == null || !d.added) return false;
            if (d is Keyboard || d is Pointer || d is Gamepad) return false;
            if (d is Joystick) return true;
            var iface = d.description.interfaceName ?? "";
            return iface == "HID";
        }

        static void PollInputSystem()
        {
            if (!hooked)
            {
                hooked = true;
                InputSystem.onDeviceChange += (d, c) => deviceChanges++;
            }
            if (mappedChange != deviceChanges) Remap();
            while (pads.Count > maps.Count) pads.RemoveAt(pads.Count - 1);
            for (int i = 0; i < maps.Count; i++)
            {
                if (i >= pads.Count) pads.Add(new RawPad());
                var m = maps[i];
                var pad = pads[i];
                pad.id = DeviceId(m.device);
                pad.standard = false;
                if (pad.axes.Length != m.axes.Count) pad.axes = new float[m.axes.Count];
                if (pad.buttons.Length != m.buttons.Count) pad.buttons = new float[m.buttons.Count];
                for (int a = 0; a < m.axes.Count; a++) pad.axes[a] = m.axes[a].ReadValue();
                for (int b = 0; b < m.buttons.Count; b++) pad.buttons[b] = m.buttons[b].ReadValue();
            }
            AssignOrdinals(pads);
        }

        public static string DeviceId(InputDevice d)
        {
            string p = d.description.product;
            return string.IsNullOrEmpty(p) ? d.name : p;
        }

        static void Remap()
        {
            mappedChange = deviceChanges;
            maps.Clear();
            foreach (var d in InputSystem.devices)
            {
                if (!IsRawDevice(d)) continue;
                var m = new DeviceMap { device = d };
                foreach (var c in d.allControls)
                {
                    if (c.synthetic) continue;
                    if (c is ButtonControl b)
                    {
                        // A stick's up / down / left / right are its axes again.
                        if (c.parent is StickControl) continue;
                        m.buttons.Add(b);
                    }
                    else if (c is AxisControl a && c.children.Count == 0)
                        m.axes.Add(a);
                }
                maps.Add(m);
            }
        }

        /// <summary>Force the device list to be rebuilt (a test added one).</summary>
        public static void Rescan() { deviceChanges++; Invalidate(); }

        static void AssignOrdinals(List<RawPad> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                int o = 0;
                for (int j = 0; j < i; j++) if (list[j].id == list[i].id) o++;
                list[i].ord = o;
            }
        }
    }
}
