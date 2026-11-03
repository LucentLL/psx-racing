// The planner harness (run.ps1): the arms' Planner driven through steering
// sequences at 60 Hz, the wheel limited to its visual top rate. Exit 1 on a fail.
using System;
using UnityEngine;

public sealed partial class CockpitArms
{
    public static bool Contact = true;
}

static class H
{
    const float Dt = 1f / 60f;
    static string verbose = Environment.GetEnvironmentVariable("PH_VERBOSE") ?? "";
    static int fails;

    static int Main()
    {
        var p = new CockpitArms.Planner();
        // the chain battery's sequences (CockpitArmsShots.ChainBattery)
        Seq(p, "big right turn", 2.2f, t => -Math.Min(450f, 300f * t), true);
        Seq(p, "unwind", 1.6f, t => Math.Min(0f, -450f + 750f * t), false, rest: true);
        Seq(p, "big left turn", 3.0f, t => Math.Min(450f, 180f * t), true);
        Seq(p, "unwind (left)", 1.6f, t => Math.Max(0f, 450f - 750f * t), false, rest: true);
        Seq(p, "handbrake reach", 2.2f, t => 90f, true, brake: t => t < 1.4f);
        Seq(p, "sweep +/-450", 10f, t => t < 2.5f ? 180f * t : t < 7.5f ? 450f - 180f * (t - 2.5f) : -450f + 180f * (t - 7.5f), true);
        // the self-test's
        Seq(p, "self right 180", 3.2f, t => -Math.Min(450f, 180f * t), true);
        Seq(p, "self unwind", 2.2f, t => Math.Min(0f, -450f + 750f * t), false, rest: true);
        Seq(p, "self left 180", 3.2f, t => Math.Min(450f, 180f * t), true);
        Seq(p, "left 120 hold", 1.2f, t => Math.Min(120f, 240f * t), true);
        bool lFree = p.L.mode == CockpitArms.Mode.Away && p.L.free;
        Seq(p, "  + handbrake", 1.5f, t => 120f, false, brake: t => true);
        bool hb = lFree && p.R.mode == CockpitArms.Mode.Away && p.R.brake && p.L.mode == CockpitArms.Mode.Grip;
        Console.WriteLine($"  handbrake mid-corner: left off before {lFree}, R {p.R.mode} brake {p.R.brake}, L {p.L.mode} -> {(hb ? "ok" : "FAIL")}");
        if (!hb) fails++;
        // fast slaloms (drift flicks at the wheel's top rate)
        Seq(p, "flick 1080", 3f, t => 400f * (float)Math.Sin(t * 4.0), true);
        Seq(p, "random", 12f, t => Noise(t) * 450f, true);
        Console.WriteLine(fails == 0 ? "PLANNER OK" : "PLANNER FAILS " + fails);
        return fails == 0 ? 0 : 1;
    }

    static float Noise(float t)
    {
        double v = 0.55 * Math.Sin(t * 1.3) + 0.3 * Math.Sin(t * 2.9 + 1.0) + 0.15 * Math.Sin(t * 6.1 + 2.0);
        return (float)v;
    }

    static void Seq(CockpitArms.Planner p, string name, float sec, Func<float, float> theta, bool fresh,
                    Func<float, bool> brake = null, bool rest = false)
    {
        if (fresh) p.Reset(theta(0f));
        bool bothOff = false; float closest = 999f; int crossed = 0, slide = 0, lets0 = p.LetGos, back0 = p.Returns;
        string firstX = "";
        int n = (int)Math.Round(sec / Dt);
        float last = theta(0f);
        for (int i = 1; i <= n; i++)
        {
            float t = i * Dt;
            float th = theta(t);
            // the visual wheel's top rate (CockpitInterior.TickWheel)
            th = last + Mathf.Clamp(th - last, -1080f * Dt, 1080f * Dt);
            last = th;
            p.Step(Dt, th, false, brake != null && brake(t));
            bool lOn = p.L.mode != CockpitArms.Mode.Away, rOn = p.R.mode != CockpitArms.Mode.Away;
            if (!lOn && !rOn) bothOff = true;
            if (lOn && rOn)
            {
                closest = Math.Min(closest, Math.Abs(Mathf.DeltaAngle(p.L.alpha, p.R.alpha)));
                if (CockpitArms.Planner.Crossed(p.L.alpha, p.R.alpha))
                {
                    if (crossed == 0) firstX = $" first X f{i} wheel {th:0}: {St(p.L)} | {St(p.R)}";
                    crossed++;
                }
            }
            if (p.L.mode == CockpitArms.Mode.Slide || p.R.mode == CockpitArms.Mode.Slide) slide++;
            if (verbose == name) Console.WriteLine($"    f{i:000} wheel {th:0} w {p.Omega:0} | {St(p.L)} | {St(p.R)}");
        }
        bool ok = !bothOff && closest >= 35f && crossed == 0;
        string restS = "";
        if (rest)
        {
            float offL = Math.Abs(Mathf.DeltaAngle(p.L.alpha, CockpitArms.Planner.RestAlpha(true)));
            float offR = Math.Abs(Mathf.DeltaAngle(p.R.alpha, CockpitArms.Planner.RestAlpha(false)));
            bool r = p.L.mode == CockpitArms.Mode.Grip && p.R.mode == CockpitArms.Mode.Grip && offL <= 21f && offR <= 21f;
            restS = $", at rest after: {r} (off L {offL:0} R {offR:0}, L {p.L.mode} R {p.R.mode})";
            ok &= r;
        }
        if (!ok) fails++;
        Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {name,-18} both off {bothOff}, closest {closest:0} deg, crossed {crossed} frames, slide {slide} frames, let go {p.LetGos - lets0}, back {p.Returns - back0}{restS}{firstX}");
    }

    static string St(CockpitArms.Hand h) =>
        $"{(h.left ? "L" : "R")} {h.mode}/{h.phase}{(h.free ? " free" : "")} a {h.alpha:0} c {CockpitArms.Planner.Climb(h.left, h.alpha):0}";
}
