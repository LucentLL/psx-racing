using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// NIGHT 3 (the city-night round, 2026-11-23): lamp light that is there at
    /// every distance the street is drawn. PSX_NIGHT3=0: c96b5178's night.
    /// </summary>
    public static partial class StreetLights
    {
        /// <summary>The round's one switch (live: the approach tool flips it in-run).</summary>
        public static bool Night3 = System.Environment.GetEnvironmentVariable("PSX_NIGHT3") != "0";

        /// <summary>The lit city-street lamp (a field lamp) nearest <paramref name="p"/>
        /// in plan, within <paramref name="maxM"/> (tools: the approach curves).</summary>
        public static bool NearestFieldLamp(Vector3 p, float maxM, out Vector3 head)
        {
            head = default;
            float best = maxM * maxM; bool found = false;
            for (int i = 0; i < high; i++)
            {
                ref Entry e = ref entries[i];
                if (!e.used || !e.field || e.kind != Kind.Street) continue;
                float dx = e.pos.x - p.x, dz = e.pos.z - p.z, d2 = dx * dx + dz * dz;
                if (d2 < best) { best = d2; head = e.pos; found = true; }
            }
            return found;
        }
    }
}
