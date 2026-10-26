using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// "Turn the wheel fully left, then right" - the WHEEL SETUP's capture,
    /// as a state machine over <see cref="RawPads"/> so it can be driven by a
    /// real wheel, a simulated Input System device or a scripted list of pads.
    ///
    ///   STEER    the axis that moves furthest from where it started is the
    ///            wheel; the way it went first is LEFT. Done once it has been
    ///            to both sides and come back to the middle. Its two extremes
    ///            and its centre are the binding.
    ///   PEDALS   the axis (or analog button) that moves furthest, steering
    ///            excluded; its extreme is FULL and where it settles when let
    ///            go is REST - so an inverted pedal, or a combined
    ///            throttle/brake axis resting in the middle, needs no switch.
    ///   BUTTONS  the first button pressed (or an axis pushed past half, for a
    ///            handbrake lever). Waiting it out with nothing pressed CLEARS
    ///            the binding - the way to unbind one.
    /// </summary>
    public sealed class WheelCapture
    {
        public enum Target { None, Steer, Throttle, Brake, Clutch, ShiftUp, ShiftDown, Handbrake, LookBack, Reset, Camera, Pause }
        public enum Phase { Idle, Left, Right, Centre, Press, Release, Button, ButtonRelease, Done, Failed }

        public Target target { get; private set; }
        public Phase phase { get; private set; } = Phase.Idle;
        /// <summary>What the last capture ended with, for the row's value.</summary>
        public string result = "";
        public bool Busy => phase != Phase.Idle && phase != Phase.Done && phase != Phase.Failed;

        public const float SteerTimeout = 25f, PedalTimeout = 15f, ButtonTimeout = 8f;
        const float SteerLock = 0.5f, PedalLock = 0.35f, ButtonLock = 0.6f, AxisAsButton = 0.6f;

        sealed class Snap { public string id; public int ord; public float[] axes, buttons; }
        readonly List<Snap> baseline = new List<Snap>();
        float timer, settle;

        // the locked candidate
        string candDev; int candOrd, candIndex = -1; bool candButton;
        float firstSign, lo, hi, fullV, restV;

        public void Begin(Target t)
        {
            target = t;
            timer = 0f; settle = 0f;
            candDev = null; candIndex = -1; candButton = false;
            result = "";
            baseline.Clear();
            foreach (var p in RawPads.All)
                baseline.Add(new Snap { id = p.id, ord = p.ord, axes = (float[])p.axes.Clone(), buttons = (float[])p.buttons.Clone() });
            switch (t)
            {
                case Target.Steer: phase = Phase.Left; break;
                case Target.Throttle: case Target.Brake: case Target.Clutch: phase = Phase.Press; break;
                case Target.None: phase = Phase.Idle; break;
                default: phase = Phase.Button; break;
            }
        }

        public void Cancel()
        {
            if (Busy) { phase = Phase.Idle; result = "CANCELLED"; }
        }

        Snap Base(RawPad p)
        {
            foreach (var s in baseline) if (s.id == p.id && s.ord == p.ord) return s;
            // A pad that appeared mid-capture (Chrome shows one only after its
            // first input): its first reading is its baseline.
            var n = new Snap { id = p.id, ord = p.ord, axes = (float[])p.axes.Clone(), buttons = (float[])p.buttons.Clone() };
            baseline.Add(n);
            return n;
        }

        static float At(float[] a, int i) => i >= 0 && i < a.Length ? a[i] : 0f;

        /// <summary>An input that already means something else: never a candidate.</summary>
        bool Excluded(RawPad p, bool button, int index)
        {
            var d = WheelConfig.Data;
            if (!button && target != Target.Steer && d.steer.Bound && d.steer.dev == p.id && d.steer.ord == p.ord && d.steer.index == index)
                return true;
            if (target >= Target.ShiftUp)
            {
                foreach (var b in new[] { d.throttle, d.brake, d.clutch })
                    if (b.Bound && b.dev == p.id && b.ord == p.ord && b.button == button && b.index == index) return true;
            }
            return false;
        }

        float Live()
        {
            var p = RawPads.Find(candDev, candOrd);
            if (p == null) return float.NaN;
            return candButton ? p.Button(candIndex) : p.Axis(candIndex);
        }

        /// <summary>Advance the capture; unscaled time (the menu runs paused).</summary>
        public void Tick(float dt)
        {
            if (!Busy) return;
            timer += dt;
            switch (phase)
            {
                case Phase.Left: case Phase.Right: case Phase.Centre: TickSteer(dt); break;
                case Phase.Press: case Phase.Release: TickPedal(dt); break;
                case Phase.Button: case Phase.ButtonRelease: TickButton(dt); break;
            }
        }

        void TickSteer(float dt)
        {
            if (phase == Phase.Left)
            {
                if (timer > SteerTimeout) { Fail("NOTHING TURNED"); return; }
                float best = SteerLock; RawPad bp = null; int bi = -1; float bd = 0f;
                foreach (var p in RawPads.All)
                {
                    var b = Base(p);
                    for (int i = 0; i < p.axes.Length; i++)
                    {
                        float dv = p.axes[i] - At(b.axes, i);
                        if (Mathf.Abs(dv) > best && !Excluded(p, false, i)) { best = Mathf.Abs(dv); bp = p; bi = i; bd = dv; }
                    }
                }
                if (bp == null) return;
                candDev = bp.id; candOrd = bp.ord; candIndex = bi; candButton = false;
                firstSign = Mathf.Sign(bd);
                restV = At(Base(bp).axes, bi);
                lo = hi = bp.axes[bi];
                phase = Phase.Right;
                return;
            }
            float v = Live();
            if (float.IsNaN(v)) { Fail("WHEEL UNPLUGGED"); return; }
            lo = Mathf.Min(lo, v); hi = Mathf.Max(hi, v);
            if (timer > SteerTimeout) { Fail("ONLY ONE WAY"); return; }
            float leftEnd = firstSign > 0f ? hi : lo, rightEnd = firstSign > 0f ? lo : hi;
            if (phase == Phase.Right)
            {
                if ((v - restV) * firstSign < -SteerLock) phase = Phase.Centre;
                return;
            }
            // Centre: back within 15% of the span of the middle, for 0.3 s.
            float mid = (leftEnd + rightEnd) * 0.5f, span = Mathf.Abs(leftEnd - rightEnd);
            if (Mathf.Abs(v - mid) < 0.15f * span * 0.5f + 0.02f) settle += dt; else settle = 0f;
            if (settle < 0.3f) return;
            var s = WheelConfig.Data.steer;
            s.dev = candDev; s.ord = candOrd; s.index = candIndex;
            s.left = leftEnd; s.right = rightEnd; s.centre = v;
            WheelConfig.Save();
            Finish("AXIS " + (candIndex + 1));
        }

        void TickPedal(float dt)
        {
            if (phase == Phase.Press)
            {
                if (timer > PedalTimeout) { Fail("NOTHING PRESSED"); return; }
                float best = PedalLock; RawPad bp = null; int bi = -1; bool bb = false;
                foreach (var p in RawPads.All)
                {
                    var b = Base(p);
                    for (int i = 0; i < p.axes.Length; i++)
                    {
                        float dv = Mathf.Abs(p.axes[i] - At(b.axes, i));
                        if (dv > best && !Excluded(p, false, i)) { best = dv; bp = p; bi = i; bb = false; }
                    }
                    for (int i = 0; i < p.buttons.Length; i++)
                    {
                        float dv = Mathf.Abs(p.buttons[i] - At(b.buttons, i));
                        if (dv > best && !Excluded(p, true, i)) { best = dv; bp = p; bi = i; bb = true; }
                    }
                }
                if (bp == null) return;
                candDev = bp.id; candOrd = bp.ord; candIndex = bi; candButton = bb;
                float bv = bb ? At(Base(bp).buttons, bi) : At(Base(bp).axes, bi);
                float cv = bb ? bp.Button(bi) : bp.Axis(bi);
                firstSign = Mathf.Sign(cv - bv);
                fullV = cv; restV = bv;
                phase = Phase.Release;
                settle = 0f;
                return;
            }
            float v = Live();
            if (float.IsNaN(v)) { Fail("PEDAL UNPLUGGED"); return; }
            if ((v - fullV) * firstSign > 0f) fullV = v;          // further in
            if ((v - restV) * firstSign < 0f) restV = v;          // further out (true rest)
            float travel = Mathf.Abs(fullV - restV);
            bool released = Mathf.Abs(v - fullV) > 0.75f * travel;
            if (!released) { settle = 0f; if (timer > PedalTimeout + 10f) Fail("NOT LET GO"); return; }
            settle += dt;
            if (settle < 0.25f) return;
            var bnd = PedalBinding(target);
            bnd.dev = candDev; bnd.ord = candOrd; bnd.index = candIndex; bnd.button = candButton;
            bnd.rest = restV; bnd.full = fullV;
            WheelConfig.Save();
            Finish(bnd.Label);
        }

        void TickButton(float dt)
        {
            if (phase == Phase.Button)
            {
                if (timer > ButtonTimeout)
                {
                    var b0 = ButtonBinding(target);
                    if (b0 != null) { b0.Clear(); WheelConfig.Save(); }
                    phase = Phase.Done; result = "CLEARED";
                    return;
                }
                foreach (var p in RawPads.All)
                {
                    var b = Base(p);
                    for (int i = 0; i < p.buttons.Length; i++)
                        if (p.buttons[i] > ButtonLock && At(b.buttons, i) < 0.4f && !Excluded(p, true, i))
                        { Lock(p, true, i, 0f, 1f); return; }
                    for (int i = 0; i < p.axes.Length; i++)
                    {
                        float dv = p.axes[i] - At(b.axes, i);
                        if (Mathf.Abs(dv) > AxisAsButton && !Excluded(p, false, i))
                        { Lock(p, false, i, At(b.axes, i), p.axes[i]); return; }
                    }
                }
                return;
            }
            float v = Live();
            if (float.IsNaN(v)) { Fail("UNPLUGGED"); return; }
            if (!candButton && (v - fullV) * firstSign > 0f) fullV = v;
            bool down = candButton ? v > 0.4f : Mathf.Abs(v - restV) > 0.5f * Mathf.Abs(fullV - restV);
            if (down) { settle = 0f; return; }
            settle += dt;
            if (settle < 0.1f) return;
            var bnd = ButtonBinding(target);
            bnd.dev = candDev; bnd.ord = candOrd; bnd.index = candIndex; bnd.button = candButton;
            bnd.rest = candButton ? 0f : restV; bnd.full = candButton ? 1f : fullV;
            WheelConfig.Save();
            Finish(bnd.Label);
        }

        void Lock(RawPad p, bool button, int i, float rest, float full)
        {
            candDev = p.id; candOrd = p.ord; candIndex = i; candButton = button;
            restV = rest; fullV = full; firstSign = Mathf.Sign(full - rest);
            settle = 0f;
            phase = Phase.ButtonRelease;
        }

        void Finish(string what) { phase = Phase.Done; result = what; }
        void Fail(string why) { phase = Phase.Failed; result = why; }

        public static WheelBinding PedalBinding(Target t)
        {
            var d = WheelConfig.Data;
            switch (t)
            {
                case Target.Throttle: return d.throttle;
                case Target.Brake: return d.brake;
                case Target.Clutch: return d.clutch;
            }
            return null;
        }

        public static WheelBinding ButtonBinding(Target t)
        {
            var d = WheelConfig.Data;
            switch (t)
            {
                case Target.ShiftUp: return d.shiftUp;
                case Target.ShiftDown: return d.shiftDown;
                case Target.Handbrake: return d.handbrake;
                case Target.LookBack: return d.lookBack;
                case Target.Reset: return d.reset;
                case Target.Camera: return d.camera;
                case Target.Pause: return d.pause;
            }
            return PedalBinding(t);
        }

        /// <summary>The row's short value while this capture runs.</summary>
        public string Prompt
        {
            get
            {
                switch (phase)
                {
                    case Phase.Left: return "TURN LEFT";
                    case Phase.Right: return "NOW RIGHT";
                    case Phase.Centre: return "CENTRE IT";
                    case Phase.Press: return "PRESS IT";
                    case Phase.Release: return "LET GO";
                    case Phase.Button: return "PRESS ONE";
                    case Phase.ButtonRelease: return "LET GO";
                }
                return result;
            }
        }

        /// <summary>The help line's instruction while this capture runs (null when idle).</summary>
        public string Help
        {
            get
            {
                switch (phase)
                {
                    case Phase.Left: return "Turn the wheel fully LEFT, to its stop. Escape cancels.";
                    case Phase.Right: return "Now turn it fully RIGHT, to the other stop.";
                    case Phase.Centre: return "Now bring it back to the CENTRE and let it rest there.";
                    case Phase.Press: return "Press the pedal all the way down, then let it go. Nothing else.";
                    case Phase.Release: return "Now let it go all the way.";
                    case Phase.Button: return "Press the button (or pull the lever) you want. Wait " + Mathf.CeilToInt(ButtonTimeout) + " seconds to clear it instead.";
                    case Phase.ButtonRelease: return "Got it - let go.";
                    case Phase.Failed: return "Not caught: " + result + ". Press the row to try again.";
                }
                return null;
            }
        }
    }
}
