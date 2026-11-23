using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// THE ROW OF POOLS TO THE DISTANCE (the lightlook round, 2026-11-23). The
    /// lamp field (StreetLights.Field.cs) lights a city street out to 200 m of
    /// the eye and no further (PSXLamps.cginc FIELD_END_M: the window is 512 m
    /// and steps 32 m), so past the hand-over the road was black under a row of
    /// lit heads - the owner's reference shows the road lit by the row all the
    /// way out. The FAR FIELD is the same splat of the same lamps (the same
    /// pool, glow, head height and mercury share per lamp, the same gains) in a
    /// coarser, wider texture: <see cref="FarRes"/> texels of
    /// <see cref="FarTexelM"/> m, 2,048 m across, stepping
    /// <see cref="FarSnapM"/> m. The shader takes it exactly where the near
    /// field lets go (the complement of the near fade, 160-200 m), so the
    /// brightness and colour agree at the hand-over by construction, and fades
    /// it out by <see cref="FarEndM"/> - inside the streamed ring (on PC 1 km),
    /// so a tile's lamps arriving at the ring's edge never show.
    ///
    /// NO POP: the window snaps to whole texels, so a step re-samples the same
    /// world-space splat (a pool never changes when the window moves); a step
    /// is built over frames behind the shown one (the near field's slicing);
    /// a lamp change re-builds only when the lamp is inside the faded-in
    /// circle. PSX_LIGHT_LOOK_FARPOOLS=0: off (the field ends at 200 m).
    /// </summary>
    public static partial class StreetLights
    {
        public const int FarRes = 256;
        public const float FarTexelM = 8f;
        const float FarSnapM = 128f;
        /// <summary>The far field is whole to this many metres of the eye and
        /// gone by <see cref="FarEndM"/> (the window's half less the snap's
        /// half, less the glow's reach).</summary>
        public static float FarFullM = 760f, FarEndM = 900f;

        static Texture2D farTex, farBack;
        static float[] facc;
        static ushort[] fhalfs;
        static Vector2 farOrigin = new Vector2(float.NaN, float.NaN);
        static float farBaseY;
        static int farVersion, farBuiltVersion = -1;
        static bool farBuiltOn;
        static Vector3 farShape = new Vector3(-1f, -1f, -1f);
        static bool fPend;
        static int fStage, fLamp, fRow, fLamps, fPendVersion, fPendFrame = -1;
        static Vector2 fPendOrigin;
        static float fPendBaseY;
        /// <summary>Lamps on the far field after the last build (tests, meter).</summary>
        public static int FarLamps { get; private set; }

        static readonly int FarTexId = Shader.PropertyToID("_PSXLampFar");
        static readonly int FarSTId = Shader.PropertyToID("_PSXLampFarST");
        static readonly int FarYId = Shader.PropertyToID("_PSXLampFarY");
        static readonly Unity.Profiling.ProfilerMarker BuildFarMarker = new Unity.Profiling.ProfilerMarker("StreetLights.BuildFarField");

        static bool FarWanted => LightLook.On && LightLook.FarPools && NoPop && FieldEnabled;

        /// <summary>A field lamp at <paramref name="pos"/> changed: re-build the
        /// far field if it can be seen there (inside the fade's end plus its
        /// glow of the last eye).</summary>
        static void FarTouched(Vector3 pos)
        {
            if (float.IsNaN(farOrigin.x)) { farVersion++; return; }
            float half = FarRes * FarTexelM * 0.5f;
            float dx = pos.x - (farOrigin.x + half), dz = pos.z - (farOrigin.y + half);
            float r = FarEndM + FarSnapM + FieldGlowM;
            if (dx * dx + dz * dz < r * r) farVersion++;
        }

        static void FarOff() => Shader.SetGlobalVector(FarSTId, Vector4.zero);

        static void PushFarField(Vector3 eye, bool mayRebuild)
        {
            if (!FarWanted || !FieldActive) { FarOff(); return; }
            float span = FarRes * FarTexelM;
            var origin = new Vector2(Mathf.Round(eye.x / FarSnapM) * FarSnapM - span * 0.5f,
                                     Mathf.Round(eye.z / FarSnapM) * FarSnapM - span * 0.5f);
            var shapeF = new Vector3(FieldPoolM, FieldPoolGain, FieldGlowGain);
            bool farPendReshape = shapeF != farShape;
            if (farPendReshape) { farShape = shapeF; farVersion++; }
            if ((mayRebuild || farPendReshape) && (origin != farOrigin || farBuiltVersion != farVersion || farBuiltOn != StreetOn))
            {
                long pc = PerfClock.On ? PerfClock.Now() : 0;
                bool left = farPendReshape || farTex == null || float.IsNaN(farOrigin.x) || FarEyeOff(eye) > FarRes * FarTexelM * 0.5f - FarEndM - FarTexelM;
                if (Application.isPlaying && !left && farBuiltOn == StreetOn && FieldSliced)
                {
                    using (BuildFarMarker.Auto()) StepFar(origin, Mathf.Round(eye.y));
                }
                else
                {
                    fPend = false;
                    farOrigin = origin; farBaseY = Mathf.Round(eye.y);
                    farBuiltVersion = farVersion; farBuiltOn = StreetOn;
                    using (BuildFarMarker.Auto()) FarLamps = StreetOn ? BuildFar(origin, farBaseY) : 0;
                }
                if (PerfClock.On) PerfClock.Add(PerfClock.Slot.LampField, pc);
            }
            if (farTex == null || FarLamps == 0) { FarOff(); return; }
            Shader.SetGlobalTexture(FarTexId, farTex);
            Shader.SetGlobalVector(FarSTId, new Vector4(farOrigin.x, farOrigin.y, 1f / span, 1f));
            Shader.SetGlobalVector(FarYId, new Vector4(farBaseY, FarEndM, 1f / Mathf.Max(1f, FarEndM - FarFullM), 0f));
        }

        static float FarEyeOff(Vector3 eye)
        {
            float half = FarRes * FarTexelM * 0.5f;
            return Mathf.Max(Mathf.Abs(eye.x - (farOrigin.x + half)), Mathf.Abs(eye.z - (farOrigin.y + half)));
        }

        static Texture2D NewFarTex() => new Texture2D(FarRes, FarRes, TextureFormat.RGBAHalf, false, true)
        {
            name = "PSXLampFarField", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave,
        };

        static int BuildFar(Vector2 origin, float baseY)
        {
            int n4 = FarRes * FarRes * 4;
            if (facc == null || facc.Length != n4) { facc = new float[n4]; fhalfs = new ushort[n4]; }
            System.Array.Clear(facc, 0, n4);
            if (farTex == null) farTex = NewFarTex();
            int lamps = 0;
            SplatFar(origin, baseY, 0, high, ref lamps, double.MaxValue);
            for (int i = 0; i < n4; i++) fhalfs[i] = facc[i] == 0f ? (ushort)0 : Mathf.FloatToHalf(facc[i]);
            farTex.SetPixelData(fhalfs, 0);
            farTex.Apply(false, false);
            return lamps;
        }

        /// <summary>One frame's share of the far window being built (the near
        /// field's StepPending, for the far texture).</summary>
        static void StepFar(Vector2 origin, float baseY)
        {
            if (fPendFrame == Time.frameCount) return;
            fPendFrame = Time.frameCount;
            int n4 = FarRes * FarRes * 4;
            if (!fPend || fPendOrigin != origin)
            {
                fPend = true; fStage = 0; fLamp = 0; fRow = 0; fLamps = 0;
                fPendOrigin = origin; fPendBaseY = baseY; fPendVersion = farVersion;
                if (facc == null || facc.Length != n4) { facc = new float[n4]; fhalfs = new ushort[n4]; }
            }
            var clock = System.Diagnostics.Stopwatch.StartNew();
            const double sliceMs = 0.75;
            if (fStage == 0)
            {
                int rows = FarRes / 4;
                System.Array.Clear(facc, fRow * FarRes * 4, rows * FarRes * 4);
                fRow += rows;
                if (fRow < FarRes) return;
                fRow = 0; fStage = 1;
            }
            if (fStage == 1)
            {
                fLamp = SplatFar(fPendOrigin, fPendBaseY, fLamp, high, ref fLamps, sliceMs - clock.Elapsed.TotalMilliseconds);
                if (fLamp < high) return;
                fStage = 2;
                if (clock.Elapsed.TotalMilliseconds >= sliceMs) return;
            }
            if (fStage == 2)
            {
                while (fRow < FarRes && clock.Elapsed.TotalMilliseconds < sliceMs)
                {
                    int r1 = Mathf.Min(FarRes, fRow + 32);
                    for (int i = fRow * FarRes * 4, e = r1 * FarRes * 4; i < e; i++) fhalfs[i] = facc[i] == 0f ? (ushort)0 : Mathf.FloatToHalf(facc[i]);
                    fRow = r1;
                }
                if (fRow < FarRes) return;
                fStage = 3;
                return;
            }
            if (farBack == null) farBack = NewFarTex();
            farBack.SetPixelData(fhalfs, 0);
            farBack.Apply(false, false);
            (farTex, farBack) = (farBack, farTex);
            farOrigin = fPendOrigin; farBaseY = fPendBaseY; farBuiltVersion = fPendVersion; farBuiltOn = StreetOn;
            FarLamps = fLamps;
            fPend = false;
        }

        /// <summary>The near field's splat (SplatLamps), at the far texel.</summary>
        static int SplatFar(Vector2 origin, float baseY, int from, int to, ref int lamps, double budgetMs)
        {
            float inv = 1f / FarTexelM;
            float rp = FieldPoolM, rg = FieldGlowM;
            int reach = Mathf.CeilToInt(Mathf.Max(rp, rg) * inv);
            float irp2 = 1f / (rp * rp), irg2 = 1f / (rg * rg);
            var clock = budgetMs < double.MaxValue ? System.Diagnostics.Stopwatch.StartNew() : null;
            int i = from;
            for (; i < to; i++)
            {
                if (clock != null && ((i - from) & 31) == 31 && clock.Elapsed.TotalMilliseconds >= budgetMs) break;
                ref Entry e = ref entries[i];
                if (!e.used || !e.field || !e.on || e.kind != Kind.Street || e.intensity <= 0f) continue;
                if (e.owned && e.owner == null) continue;
                float cx = (e.pos.x - origin.x) * inv - 0.5f, cz = (e.pos.z - origin.y) * inv - 0.5f;
                if (cx < -reach || cz < -reach || cx > FarRes + reach || cz > FarRes + reach) continue;
                lamps++;
                float pool = e.intensity * FieldPoolGain, glow = e.intensity * FieldGlowGain;
                float dy = e.pos.y - baseY, merc = e.mercury ? glow : 0f;
                int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - reach)), x1 = Mathf.Min(FarRes - 1, Mathf.CeilToInt(cx + reach));
                int z0 = Mathf.Max(0, Mathf.FloorToInt(cz - reach)), z1 = Mathf.Min(FarRes - 1, Mathf.CeilToInt(cz + reach));
                for (int z = z0; z <= z1; z++)
                {
                    float dz = (z - cz) * FarTexelM;
                    int row = z * FarRes;
                    for (int x = x0; x <= x1; x++)
                    {
                        float dx = (x - cx) * FarTexelM;
                        float d2 = dx * dx + dz * dz;
                        float g = 1f - d2 * irg2;
                        if (g <= 0f) continue;
                        g *= g;
                        float p = 1f - d2 * irp2;
                        p = p > 0f ? p * p : 0f;
                        int o = (row + x) * 4;
                        facc[o] += pool * p;
                        facc[o + 1] += glow * g;
                        facc[o + 2] += glow * g * dy;
                        facc[o + 3] += merc * g;
                    }
                }
            }
            return i;
        }
    }
}

namespace PSXRacing
{
    public static partial class StreetLights
    {
        /// <summary>(lightlook, tools) The field lamps nearest the eye: distance, the
        /// viewport point of the head, its intensity, and the owner's name - to see which
        /// lamps light a stretch of road and whether their heads are in the picture.</summary>
        public static string DiagnoseNearest(Camera cam, int count)
        {
            if (cam == null) return "";
            var list = new System.Collections.Generic.List<(float d, int i)>();
            Vector3 eye = cam.transform.position;
            for (int i = 0; i < high; i++)
            {
                ref Entry e = ref entries[i];
                if (!e.used || !e.field || e.kind != Kind.Street) continue;
                list.Add(((e.pos - eye).magnitude, i));
            }
            list.Sort((a, b) => a.d.CompareTo(b.d));
            var sb = new System.Text.StringBuilder();
            for (int k = 0; k < list.Count && k < count; k++)
            {
                ref Entry e = ref entries[list[k].i];
                Vector3 vp = cam.WorldToViewportPoint(e.pos);
                string owner = e.owner != null ? (e.owner as Component != null ? ((Component)e.owner).transform.parent?.name + "/" + ((Component)e.owner).name : e.owner.name) : "-";
                sb.Append("      lamp ").Append(list[k].d.ToString("0")).Append(" m, view ").Append(vp.x.ToString("0.00")).Append(",").Append(vp.y.ToString("0.00"))
                  .Append(vp.z > 0f && vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f ? " IN" : " out").Append(", h ").Append(e.pos.y.ToString("0"))
                  .Append(", int ").Append(e.intensity.ToString("0.00")).Append(e.mercury ? " merc" : "").Append(", ").Append(owner).Append("\n");
            }
            return sb.ToString();
        }
    }
}
