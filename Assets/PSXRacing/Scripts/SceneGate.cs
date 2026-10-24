using UnityEngine;
using UnityEngine.SceneManagement;
using PSXRacing.City;

namespace PSXRacing
{
    /// <summary>
    /// The one door into a venue's scene (2026-10-24, one build again): a
    /// Charlotte scene waits for the city's data (<see cref="CharlotteData"/>,
    /// its own download since the editions were united), behind a LOADING
    /// CHARLOTTE screen, when the background fetch the game started at boot
    /// has not finished yet. Every other scene, and every Charlotte scene once
    /// the data is here (or its fetch failed: the city then reports what is
    /// missing), loads at once, exactly as SceneManager.LoadScene did.
    /// </summary>
    public static class SceneGate
    {
        /// <summary>Load the scene at build index <paramref name="idx"/>,
        /// waiting for Charlotte's data first if it is a Charlotte scene.</summary>
        public static void Load(int idx)
        {
            if (!IsCity(idx) || CharlotteData.Ready || CharlotteData.Failed) { SceneManager.LoadScene(idx); return; }
            CharlotteData.Request();
            var go = new GameObject("CharlotteGate");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<Waiter>().idx = idx;
        }

        /// <summary>Is the scene at <paramref name="idx"/> one that builds
        /// Charlotte: the free-roam city, or any city venue's scene (by the
        /// catalog's rule, <see cref="TrackCatalog.TrackDef.city"/>).</summary>
        public static bool IsCity(int idx)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(idx);
            if (!string.IsNullOrEmpty(path) && System.IO.Path.GetFileNameWithoutExtension(path) == "Charlotte") return true;
            for (int i = 0; i < TrackCatalog.Count; i++)
                if (TrackCatalog.All[i].city && TrackCatalog.SceneIndex(i) == idx) return true;
            return false;
        }

        sealed class Waiter : MonoBehaviour
        {
            public int idx;
            GUIStyle style;

            void Update()
            {
                if (!CharlotteData.Ready && !CharlotteData.Failed) return;
                SceneManager.LoadScene(idx);
                Destroy(gameObject);
            }

            void OnGUI()
            {
                // the GT2 menu's amber on charcoal, over whatever was on screen
                GUI.depth = -1000;
                var old = GUI.color;
                GUI.color = new Color(0.08f, 0.08f, 0.09f, 1f);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = old;
                if (style == null) style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                style.fontSize = Mathf.Max(20, Screen.height / 22);
                style.normal.textColor = new Color(1f, 0.71f, 0.2f);
                GUI.Label(new Rect(0, 0, Screen.width, Screen.height), $"LOADING CHARLOTTE  {Mathf.RoundToInt(CharlotteData.Progress * 100f)}%", style);
            }
        }
    }
}
