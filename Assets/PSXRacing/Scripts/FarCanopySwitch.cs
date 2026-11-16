using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// THE FAR CANOPY's land switch (linncove, owner 2026-11-16: "a solution to
    /// the barren mountains in the distance on mountain maps"). On a stage
    /// horizon's root (PSXRacingBuilder.Horizon): the apron is built twice - at
    /// 480 m as it always was, and at the wide grid's own 240 m - and this shows
    /// one at load: the fine one with the far canopy, the coarse one with
    /// PSX_FARCANOPY=0 (LookChoices.FarCanopy), so that switch is the land
    /// exactly as it was. The canopy's shading and the ridge fringe follow the
    /// same switch in PSX/Lit (_PSXFarCanopyOff, set by StageCulling). Tools call
    /// <see cref="ApplyAll"/> (OnEnable does not run in edit mode).
    /// </summary>
    public class FarCanopySwitch : MonoBehaviour
    {
        public GameObject coarse, fine;

        void OnEnable() => Apply();

        public void Apply()
        {
            bool on = LookChoices.FarCanopy;
            if (fine != null) fine.SetActive(on);
            if (coarse != null) coarse.SetActive(!on || fine == null);
        }

        public static int ApplyAll()
        {
            int n = 0;
            foreach (var s in FindObjectsByType<FarCanopySwitch>(FindObjectsInactive.Include)) { s.Apply(); n++; }
            return n;
        }
    }
}
