using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// THE DYNAMIC SKY's runtime half (the owner, 2026-09-26: "a dynamic skybox
    /// like the github repo", as a switch - SkyModePrefs). The picture is drawn
    /// by PSX/Sky in its computed mode (Shaders/PSXAtmosphere.cginc); this sets
    /// what that shader reads and keeps the rest of the game looking at the same
    /// sky:
    ///
    ///   * THE SUN WHERE IT REALLY IS. Each hour has one light: the sun by day,
    ///     the MOON at night. The atmosphere needs the sun even when it is down -
    ///     just under the horizon at dusk (the blue hour), well under it at
    ///     night - and the moon drawn where the night's light comes from.
    ///   * CLOUD by the weather: a scatter of fair-weather cumulus when clear,
    ///     a closed deck in rain, fog or snow; drifting on a wind.
    ///   * REFLECTIONS. The car paint, the wet road and the sea read the sky
    ///     through PSXSkyIn's panorama globals; they are pointed at a 256 x 128
    ///     equirect of the computed sky (Hidden/PSX/SkyEquirect), re-rendered
    ///     every few seconds so the clouds in the bonnet drift too.
    ///   * THE HORIZON RING, from the computed sky - the scattering is evaluated
    ///     here on the CPU at eight bearings, the ratios made exactly the way
    ///     tools/sky/bake_horizon_rings.py makes the photographs'.
    /// </summary>
    public static class DynamicSky
    {
        /// <summary>Per hour (Dawn, Morning, Noon, Afternoon, Sunset, Dusk,
        /// Night): how bright the computed sky is drawn.</summary>
        static readonly float[] ExposureByHour = { 1.6f, 1.05f, 0.85f, 1.0f, 1.5f, 2.6f, 2.4f };
        /// <summary>Cloud cover by weather (Clear, Fog, Rain, Snow).</summary>
        static readonly float[] CoverByWeather = { 0.38f, 0.72f, 0.88f, 0.82f };
        const float RefreshSeconds = 3f;

        static RenderTexture pano;
        static Material equirect;
        static Driver driver;

        public static void Apply(int hour, Weather weather, Light sun, TimeOfDay.Preset p)
        {
            Vector3 light = sun != null ? -sun.transform.forward : -(Quaternion.Euler(p.sunEuler) * Vector3.forward);
            light.Normalize();
            string name = TimeOfDay.At(hour).name;
            bool night = name == "NIGHT";
            bool dusk = name == "DUSK";
            Vector3 flat = new Vector3(light.x, 0f, light.z);
            if (flat.sqrMagnitude < 1e-6f) flat = Vector3.right;
            flat.Normalize();
            // The sun, even when it is down.
            Vector3 sunTrue = night ? (-flat * Mathf.Cos(0.44f) + Vector3.down * Mathf.Sin(0.44f))   // 25 deg under, opposite the moon
                            : dusk ? (flat * Mathf.Cos(0.07f) + Vector3.down * Mathf.Sin(0.07f))     // 4 deg under: the blue hour
                            : light;
            Shader.SetGlobalVector("_PSXSunTrue", sunTrue.normalized);
            Shader.SetGlobalVector("_PSXMoonDir", night ? new Vector4(light.x, light.y, light.z, 1f)
                                                        : new Vector4(0f, -1f, 0f, 0f));
            int w = Mathf.Clamp((int)weather, 0, CoverByWeather.Length - 1);
            Shader.SetGlobalFloat("_PSXCloudCover", CoverByWeather[w]);
            Shader.SetGlobalVector("_PSXCloudWind", new Vector4(0.0035f, 0.0012f, 0f, 0f));
            float exposure = ExposureByHour[Mathf.Clamp(hour, 0, ExposureByHour.Length - 1)] *
                             Seasons.SkyMul(weather);
            Shader.SetGlobalFloat("_PSXDynExposure", exposure);

            // The ring, from the scattering.
            Shader.SetGlobalVectorArray("_PSXFogRing", Ring(sunTrue.normalized, exposure));
            Shader.SetGlobalFloat("_PSXFogRingOn", TimeOfDay.FogRingAmount);

            RenderPanorama();
            if (driver == null && Application.isPlaying)
            {
                var go = new GameObject("DynamicSky");
                Object.DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                driver = go.AddComponent<Driver>();
            }
            if (driver != null) driver.enabled = true;
        }

        /// <summary>Back to the photograph: the reflection globals are set by
        /// TimeOfDay.ApplySky itself; this only stops the refresh.</summary>
        public static void Stop()
        {
            if (driver != null) driver.enabled = false;
        }

        static void RenderPanorama()
        {
            if (equirect == null)
            {
                var sh = Shader.Find("Hidden/PSX/SkyEquirect");
                if (sh == null) return;
                equirect = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            }
            if (pano == null)
            {
                pano = new RenderTexture(256, 128, 0, RenderTextureFormat.ARGB32)
                {
                    wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear,
                    useMipMap = true, autoGenerateMips = true, name = "DynamicSkyPano",
                };
                pano.Create();
            }
            Graphics.Blit(null, pano, equirect);
            Shader.SetGlobalTexture("_PSXSkyTex", pano);
            Shader.SetGlobalFloat("_PSXSkyAmount", 1f);
            Shader.SetGlobalFloat("_PSXSkyRotation", 0f);
            Shader.SetGlobalFloat("_PSXSkyTint", 0f);
            Shader.SetGlobalFloat("_PSXSkyExposure", 1f);
        }

        class Driver : MonoBehaviour
        {
            float next;
            void Update()
            {
                if (Time.unscaledTime < next) return;
                next = Time.unscaledTime + RefreshSeconds;
                if (SkyModePrefs.Dynamic) RenderPanorama();
            }
        }

        // ---- the scattering, on the CPU, for the ring --------------------------
        // A straight port of AtmoScatter in PSXAtmosphere.cginc (without the
        // clouds): eight calls per hour applied.
        const float RGround = 6360e3f, RTop = 6420e3f, HR = 7994f, HM = 1200f, G = 0.76f, BetaM = 21e-6f;
        static readonly Vector3 BetaR = new Vector3(5.8e-6f, 13.5e-6f, 33.1e-6f);

        static float RaySphereFar(Vector3 o, Vector3 d, float r)
        {
            float b = Vector3.Dot(o, d);
            float c = Vector3.Dot(o, o) - r * r;
            float h = b * b - c;
            return h < 0f ? -1f : -b + Mathf.Sqrt(h);
        }

        static Vector3 Exp(Vector3 v) => new Vector3(Mathf.Exp(v.x), Mathf.Exp(v.y), Mathf.Exp(v.z));

        static Vector3 Scatter(Vector3 dir, Vector3 sunDir)
        {
            var o = new Vector3(0f, RGround + 2f, 0f);
            dir.y = Mathf.Max(dir.y, 0f); dir.Normalize();
            float seg = RaySphereFar(o, dir, RTop) / 8f;
            float mu = Vector3.Dot(dir, sunDir);
            float phaseR = 3f / (16f * Mathf.PI) * (1f + mu * mu);
            float g2 = G * G;
            float phaseM = 3f / (8f * Mathf.PI) * ((1f - g2) * (1f + mu * mu)) /
                           ((2f + g2) * Mathf.Pow(Mathf.Max(1f + g2 - 2f * G * mu, 1e-4f), 1.5f));
            float odR = 0f, odM = 0f;
            Vector3 sumR = Vector3.zero, sumM = Vector3.zero;
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = o + dir * (seg * (i + 0.5f));
                float h = p.magnitude - RGround;
                float hr = Mathf.Exp(-h / HR) * seg, hm = Mathf.Exp(-h / HM) * seg;
                odR += hr; odM += hm;
                float segL = RaySphereFar(p, sunDir, RTop) / 3f;
                float odRL = 0f, odML = 0f; bool lit = true;
                for (int j = 0; j < 3; j++)
                {
                    Vector3 q = p + sunDir * (segL * (j + 0.5f));
                    float hl = q.magnitude - RGround;
                    if (hl < 0f) { lit = false; break; }
                    odRL += Mathf.Exp(-hl / HR) * segL;
                    odML += Mathf.Exp(-hl / HM) * segL;
                }
                if (!lit) continue;
                Vector3 tau = BetaR * (odR + odRL) + Vector3.one * (BetaM * 1.1f * (odM + odML));
                Vector3 att = Exp(-tau);
                sumR += att * hr; sumM += att * hm;
            }
            return 22f * (Vector3.Scale(sumR, BetaR) * phaseR + sumM * (BetaM * phaseM));
        }

        static Vector4[] Ring(Vector3 sunDir, float exposure)
        {
            var c = new Vector3[8];
            Vector3 mean = Vector3.zero;
            float el = 3f * Mathf.Deg2Rad;
            for (int k = 0; k < 8; k++)
            {
                float az = ((k + 0.5f) / 8f - 0.5f) * 2f * Mathf.PI;
                var d = new Vector3(Mathf.Cos(el) * Mathf.Cos(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Sin(az));
                Vector3 s = Scatter(d, sunDir) * exposure;
                c[k] = new Vector3(1f - Mathf.Exp(-s.x), 1f - Mathf.Exp(-s.y), 1f - Mathf.Exp(-s.z));
                mean += c[k] / 8f;
            }
            var ring = new Vector4[8];
            for (int k = 0; k < 8; k++)
            {
                var rat = new Vector3(c[k].x / Mathf.Max(mean.x, 1e-6f), c[k].y / Mathf.Max(mean.y, 1e-6f),
                                      c[k].z / Mathf.Max(mean.z, 1e-6f));
                float lum = Mathf.Max(0.2126f * rat.x + 0.7152f * rat.y + 0.0722f * rat.z, 1e-6f);
                float keep = Mathf.Pow(lum, 0.3f) / lum;
                ring[k] = new Vector4(Mathf.Clamp(rat.x * keep, 0.7f, 1.35f), Mathf.Clamp(rat.y * keep, 0.7f, 1.35f),
                                      Mathf.Clamp(rat.z * keep, 0.7f, 1.35f), 1f);
            }
            return ring;
        }
    }
}
