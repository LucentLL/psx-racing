using UnityEngine;

namespace PSXRacing.EditorTools
{
    public static partial class PSXRacingBuilder
    {
        /// <summary>
        /// Rebuild ONE venue's scene in a sandbox whose scenes are older than
        /// its scripts (the weather matrix's mountain frames: a stale scene
        /// wears no season dress and loses textures the builder has since
        /// regenerated). The preamble of <see cref="Build"/> minus the car
        /// models, the city props and the build-settings list.
        /// </summary>
        public static bool BuildOneVenue(string id)
        {
            TrackCatalog.TrackDef def = null;
            foreach (var d in TrackCatalog.Scened) if (d.id == id) def = d;
            if (def == null || def.city) { Debug.LogError("[BuildOneVenue] no non-city venue " + id); return false; }
            EditionParking.RecoverIfNeeded();
            var editionWas = Edition.Simulated;
            Edition.Simulate(EditionKind.All);
            try
            {
                EnsureFolders();
                GenerateTrackTextures();
                ConfigureTextureImporters();
                ConfigureSkyImporters();
                EnsureRoadLayer();
                psxLit = Shader.Find("PSX/Lit");
                if (psxLit == null) { Debug.LogError("[BuildOneVenue] PSX/Lit not found"); return false; }
                string path = BuildTrack(def);
                Debug.Log("[BuildOneVenue] built " + path);
                return true;
            }
            finally { Edition.Simulate(editionWas); }
        }
    }
}
