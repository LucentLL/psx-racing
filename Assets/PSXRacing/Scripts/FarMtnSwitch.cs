using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// THE FAR MOUNTAIN TEMPLATE's switch (farmtn, owner 2026-11-17: Bat Cave's
    /// hill - grey rock faces and the canopy - as the template for every map's
    /// distant mountains). On the template's root (PSXRacingBuilder.FarMtn):
    /// the rock laid over the land where it is really steep shows at load with
    /// <see cref="LookChoices.FarMtnTemplate"/>, and PSX/Lit paints the far
    /// land's rock (_FarRock materials) unless the global _PSXFarMtnOff is 1.
    /// Tools call <see cref="ApplyAll"/> (OnEnable does not run in edit mode).
    /// </summary>
    public class FarMtnSwitch : MonoBehaviour
    {
        static readonly int OffId = Shader.PropertyToID("_PSXFarMtnOff");
        public GameObject rock;

        void OnEnable() => Apply();

        public void Apply()
        {
            bool on = LookChoices.FarMtnTemplate;
            if (rock != null) rock.SetActive(on);
            Shader.SetGlobalFloat(OffId, on ? 0f : 1f);
        }

        public static int ApplyAll()
        {
            int n = 0;
            foreach (var s in FindObjectsByType<FarMtnSwitch>(FindObjectsInactive.Include)) { s.Apply(); n++; }
            if (n == 0) Shader.SetGlobalFloat(OffId, 0f);
            return n;
        }
    }
}
