using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// STATIC GRIP (owner 2026-11-17: "cars slide on banked roads. Even when
    /// resting in place at the start of a race. This means tires don't
    /// actually grip the roads."). The contact-patch model's own arithmetic
    /// (CarController.PatchForce / StaticGripWeight), then the sim: a
    /// front-heavy car standing on a 10% bank in the game's own hold must not
    /// move or turn, and with nothing pressed on a bank with no grade must
    /// stay put too. Only with PSX_STATIC_GRIP on (its default); with it off
    /// the section says so and checks nothing, so a before run is not failed
    /// for being the before.
    /// </summary>
    public static partial class LifeSimSelfTest
    {
        static void TestStaticGrip()
        {
            Line("static grip:");
            if (!CarController.StaticGripOn) { Line("  (PSX_STATIC_GRIP=0: the slip model alone, as before - not checked)"); return; }

            // The weight: all patch at rest, all slip model from 2 m/s.
            Check(Mathf.Approximately(CarController.StaticGripWeight(0f), 1f) &&
                  Mathf.Approximately(CarController.StaticGripWeight(0.5f), 1f) &&
                  CarController.StaticGripWeight(2f) == 0f && CarController.StaticGripWeight(30f) == 0f,
                  "the patch model has the tyre at rest and hands it to the slip model by 2 m/s",
                  CarController.StaticGripWeight(1f).ToString("0.00") + " at 1 m/s");

            // Steady rolling: the patch force is the slip model's linear branch
            // (C x tan slip) - the hand-over is seamless because they agree.
            const float Fz = 3000f, C = 11f * Fz, dt = 1f / 60f;
            float s = 0f, f = 0f;
            for (int i = 0; i < 600; i++) f = CarController.PatchForce(ref s, 0.02f, 1.5f, C, Fz / 9.81f, 1.25f * Fz, dt);
            float slipModel = -C * Mathf.Atan2(0.02f, 1.5f);
            Check(Mathf.Abs(f - slipModel) < 0.02f * Mathf.Abs(slipModel),
                  "rolling at 1.5 m/s with a 0.76 deg slip the patch gives the slip model's force",
                  f.ToString("0") + " N vs " + slipModel.ToString("0") + " N");

            // At rest a deflection is held, not bled away: no creep.
            s = 0.003f;
            for (int i = 0; i < 1200; i++) CarController.PatchForce(ref s, 0f, 0f, C, Fz / 9.81f, 1.25f * Fz, dt);
            Check(Mathf.Abs(s - 0.003f) < 1e-6f, "a patch at rest keeps its deflection (static friction, no creep)", s.ToString("0.000000"));

            // Past the circle it slides: the force is the cap, the deflection held at the limit.
            s = 0f;
            for (int i = 0; i < 120; i++) f = CarController.PatchForce(ref s, 1f, 0f, C, Fz / 9.81f, 900f, dt);
            Check(Mathf.Abs(Mathf.Abs(f) - 900f) < 1f && Mathf.Abs(s) <= 900f / (C / 0.30f) + 1e-6f,
                  "a patch asked for more than its circle slides at the circle", f.ToString("0") + " N");

            // The sim: a 60:40 car on a 10% bank (GripSim). The player's hold
            // and no pedals on a level-grade bank: under 1 cm and a tenth of a
            // degree in 20 s.
            GripSim.Reading[] r;
            try { r = GripSim.RestCases(0.10f, 0f); }
            catch (System.Exception e) { Check(false, "the grip sim runs", e.GetType().Name + ": " + e.Message); return; }
            foreach (var x in r)
            {
                if (x.name != "hold (player)" && x.name != "hold (AI)" && x.name != "no pedals" && x.name != "parking brake") continue;
                float d = Mathf.Sqrt(x.sideM * x.sideM + x.foreM * x.foreM);
                Check(d < 0.01f && Mathf.Abs(x.yawDeg) < 0.1f,
                      "\"" + x.name + "\" on a 10% bank: the car stays where it was left",
                      (d * 1000f).ToString("0.0") + " mm, " + x.yawDeg.ToString("0.00") + " deg in 20 s");
            }
        }
    }
}
