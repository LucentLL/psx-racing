using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Photograph the engine hoist: once in the room, then four times round it
    /// with the room hidden.
    ///
    /// Two passes, because the two questions are different. "Is the crane in
    /// the right place and is the engine on the hook" needs the garage in
    /// shot. "Does a photographed engine hold up as you walk round it" needs
    /// the garage OUT of shot — a one-car garage is 3.10 m across, so three of
    /// four orbit positions are inside a wall and photograph plasterboard.
    ///
    /// The stock PSX Racing/Capture Garage cameras cannot answer either: their
    /// positions predate the house model and now look at lawn and siding.
    /// </summary>
    public static class HoistPreview
    {
        [MenuItem("PSX Racing/Preview Engine Hoist")]
        public static void Run()
        {
            var scene = EditorSceneManager.OpenScene("Assets/PSXRacing/Scenes/Garage.unity",
                                                     OpenSceneMode.Single);
            var hoist = GameObject.Find("EngineHoist");
            if (hoist == null) { Debug.LogError("[Hoist] no EngineHoist in the garage scene"); return; }

            Transform sprite = null;
            foreach (var t in hoist.GetComponentsInChildren<Transform>(true))
                if (t.name == "EngineSprite") sprite = t;
            if (sprite == null) { Debug.LogError("[Hoist] the crane has no engine on it"); return; }

            var mr = sprite.GetComponent<MeshRenderer>();
            var bb = sprite.GetComponent<PSXRacing.OnFoot.AtlasBillboard>();
            Debug.Log("[Hoist] engine at " + sprite.position.ToString("0.00") +
                      "  size " + sprite.localScale.ToString("0.00") +
                      "  tex " + (mr != null && mr.sharedMaterial != null
                                  ? (mr.sharedMaterial.mainTexture != null
                                     ? mr.sharedMaterial.mainTexture.name : "NONE")
                                  : "NO MATERIAL") +
                      "  views " + (bb != null ? bb.viewOffsets.Length : 0));

            // The shader reads globals; without them every surface is black.
            Shader.SetGlobalFloat("_PSXFogNear", 60f);
            Shader.SetGlobalFloat("_PSXFogFar", 200f);
            Shader.SetGlobalColor("_PSXFogColor", new Color(0.5f, 0.52f, 0.58f));
            Shader.SetGlobalFloat("_PSXSnap", 0f);
            Shader.SetGlobalColor("_PSXAmbient", new Color(0.66f, 0.66f, 0.70f));
            Shader.SetGlobalVector("_PSXLightDir", new Vector4(-0.4f, 0.8f, -0.3f, 0f).normalized);
            Shader.SetGlobalColor("_PSXLightColor", new Color(0.85f, 0.83f, 0.78f));

            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                                      "Screenshots", "Hoist");
            Directory.CreateDirectory(dir);
            var at = sprite.position;

            // ---- in the room, from the doorway ----
            FaceCamera(sprite, bb, mr, new Vector3(0f, 0f, -1f));
            var from = at + new Vector3(0.15f, 0.30f, -2.6f);
            Shoot(dir, "hoist_inroom", from, Quaternion.LookRotation(at - from, Vector3.up));

            // ---- turntable, everything but the crane hidden ----
            var hidden = new List<Renderer>();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (r.transform.IsChildOf(hoist.transform)) continue;
                if (!r.enabled) continue;
                r.enabled = false;
                hidden.Add(r);
            }
            try
            {
                for (int i = 0; i < 4; i++)
                {
                    float a = i * 90f * Mathf.Deg2Rad;
                    var dirv = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                    FaceCamera(sprite, bb, mr, dirv);
                    var pos = at + dirv * 1.9f + Vector3.up * 0.12f;
                    Shoot(dir, "hoist_" + (i * 90), pos, Quaternion.LookRotation(at - pos, Vector3.up));
                }
            }
            finally { foreach (var r in hidden) if (r != null) r.enabled = true; }

            Debug.Log("[Hoist] wrote 5 shots to " + dir);
            EditorSceneManager.CloseScene(scene, false);
        }

        /// <summary>
        /// Stand the billboard up for a viewer in direction <paramref name="toCam"/>
        /// and select the atlas cell by hand.
        ///
        /// Edit mode never runs LateUpdate, so without this every shot
        /// photographs whichever cell was last baked into the material and the
        /// turntable proves nothing. It mirrors AtlasBillboard exactly, INCLUDING
        /// the LookRotation(-toCam): Unity's Quad primitive shows its -Z face, so
        /// pointing +Z at the camera turns the sprite's back to it and back-face
        /// culling makes the engine vanish entirely — which is how the first
        /// version of this file reported an empty hook.
        /// </summary>
        static void FaceCamera(Transform sprite, PSXRacing.OnFoot.AtlasBillboard bb,
                               MeshRenderer mr, Vector3 toCam)
        {
            sprite.rotation = Quaternion.LookRotation(-toCam, Vector3.up);
            if (bb == null || mr == null || bb.viewOffsets == null || bb.viewOffsets.Length == 0) return;

            Vector3 fwd = sprite.parent != null
                ? sprite.parent.TransformDirection(bb.facing) : bb.facing;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            float ang = Vector3.SignedAngle(fwd.normalized, toCam.normalized, Vector3.up);
            int n = bb.viewOffsets.Length;
            int v = Mathf.FloorToInt(Mathf.Repeat(ang / 360f * n + 0.5f, n));

            var mpb = new MaterialPropertyBlock();
            mr.GetPropertyBlock(mpb);
            mpb.SetVector("_MainTex_ST", new Vector4(
                bb.cellSize.x, bb.cellSize.y, bb.viewOffsets[v].x, bb.viewOffsets[v].y));
            mr.SetPropertyBlock(mpb);
        }

        static void Shoot(string dir, string name, Vector3 pos, Quaternion rot)
        {
            var go = new GameObject("~hoistCam");
            var cam = go.AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(pos, rot);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.42f, 0.45f, 0.52f);
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 200f;
            cam.fieldOfView = 55f;

            var rt = new RenderTexture(960, 540, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(960, 540, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(go);
        }
    }
}
