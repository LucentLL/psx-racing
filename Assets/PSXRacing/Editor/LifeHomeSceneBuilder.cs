using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PSXRacing.LifeSim;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Generates the LifeHome menu scene. Nearly empty on disk by design: a
    /// camera to clear the backbuffer and one LifeHomeScreen component that
    /// builds the whole UI at runtime — same philosophy as the race scene,
    /// where the builder owns everything and nothing is hand-authored.
    /// </summary>
    public static class LifeHomeSceneBuilder
    {
        public const string ScenePath = "Assets/PSXRacing/Scenes/LifeHome.unity";

        [MenuItem("PSX Racing/Build Home Scene")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGO = new GameObject("Camera");
            camGO.tag = "MainCamera";
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.10f, 0.06f, 0.14f);
            cam.cullingMask = 0;
            camGO.AddComponent<AudioListener>();

            var home = new GameObject("LifeHome");
            home.AddComponent<LifeHomeScreen>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[LifeHome] Scene saved: " + ScenePath);
        }
    }
}
