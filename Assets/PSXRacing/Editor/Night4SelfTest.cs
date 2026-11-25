using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// NIGHT 4 round 4b self-test (edit mode, no scene, no rendering, seconds):
    ///   1. a street lamp's view found the while-driving way (a background batch) is READ BACK through the
    ///      Update path alone - nothing renders, no camera - so no state of the game (paused, a menu over
    ///      the city, the photo view, a tool stepping frames) can leave a lamp dark or half found;
    ///   2. that view is right: a wall 4 m from the head shadows the ground behind it, the open side is clear.
    /// Writes PSXRacing_night4_selftest.txt; exit 0 pass, 1 fail.
    ///   Unity.exe -batchmode -projectPath &lt;sandbox&gt; -executeMethod PSXRacing.EditorTools.Night4SelfTest.Run -quit
    /// </summary>
    public static class Night4SelfTest
    {
        public static void Run()
        {
            ToolWatchdog.Arm("Night4SelfTest", 5f);
            var sb = new StringBuilder();
            int fails = 0;
            void Check(bool ok, string what) { sb.AppendLine((ok ? "  ok   " : "  FAIL ") + what); if (!ok) fails++; }
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.transform.position = new Vector3(0f, -0.5f, 0f); ground.transform.localScale = new Vector3(200f, 1f, 200f);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = new Vector3(4.5f, 10f, 0f); wall.transform.localScale = new Vector3(1f, 20f, 30f);
            bool was = StreetLights.Night4;
            StreetLights.Night4 = true;
            int h = StreetLights.Add(null, new Vector3(0f, 9f, 0f), 18f, Color.white, 1f, StreetLights.Kind.Street);
            StreetLights.MarkField(h, true);
            try
            {
                int ticks = StreetLights.N4TestBackgroundBatch(h, 400);
                Check(ticks > 0, "a background batch is read back through the Update path alone, nothing rendered (" + ticks + " ticks)");
                Check(!StreetLights.N4BatchOut, "no batch left out");
                StreetLights.N4ViewToward(h & 0xFFFF, new Vector3(20f, 0f, 0f), out _, out var toWall);
                StreetLights.N4ViewToward(h & 0xFFFF, new Vector3(-20f, 0f, 0f), out _, out var away);
                Check(toWall.x < 6f && toWall.y > 40f, "the ground behind a wall 4 m off is in its shadow (from " + toWall.x.ToString("0.0") + " m to " + toWall.y.ToString("0") + ")");
                Check(away.x >= StreetLights.N4Clear - 0.01f, "the open side is clear (" + away.x.ToString("0.0") + ")");
            }
            catch (System.Exception ex) { Check(false, "exception " + ex.GetType().Name + ": " + ex.Message); }
            finally
            {
                StreetLights.Remove(h);
                StreetLights.Night4 = was;
                Object.DestroyImmediate(ground); Object.DestroyImmediate(wall);
            }
            sb.Insert(0, "NIGHT 4 SELF-TEST: " + (fails == 0 ? "PASS" : fails + " FAILED") + "\n");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "PSXRacing_night4_selftest.txt"), sb.ToString());
            Debug.Log(sb.ToString());
            EditorApplication.Exit(fails == 0 ? 0 : 1);
        }
    }
}
