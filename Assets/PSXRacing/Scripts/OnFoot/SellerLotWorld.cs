using UnityEngine;
using UnityEngine.SceneManagement;
using PSXRacing.LifeSim;

namespace PSXRacing.OnFoot
{
    /// <summary>
    /// Dresses the seller's street for one particular advert, and puts the car
    /// on the driveway.
    ///
    /// The same split every walk-in scene in this project uses: the STREET is
    /// baked because it is the same street for everybody, and what is standing
    /// on it comes out of the save. What is new here is that the save entry is
    /// a car the player does not own — see <see cref="Viewing"/> — so the whole
    /// room is built around a phantom that will either be adopted or forgotten.
    ///
    /// The dressing is SEEDED off the advert rather than rolled, so driving
    /// back tomorrow finds the same houses. A street that changed shape between
    /// two visits would read as the game losing its place, and the player is
    /// meant to be building a picture of one specific car in one specific
    /// driveway.
    ///
    /// NO SELLER MODEL, at the owner's instruction. The car is the thing you
    /// walk up to and the car is what you talk to — which is also how RG2's own
    /// private-sale loop works: its overlay header is the literal string
    /// "PRIVATE SELLER" and there is no name, no phone and no face anywhere in
    /// either of its two codebases.
    /// </summary>
    public class SellerLotWorld : MonoBehaviour
    {
        [System.Serializable]
        public class PlotHouses
        {
            /// <summary>Every house this plot could be wearing, all baked and
            /// all disabled. One is switched on per visit.</summary>
            public GameObject[] variants = new GameObject[0];
        }

        [Header("Wired by the scene builder")]
        public Transform[] plots = new Transform[0];
        public PlotHouses[] houses = new PlotHouses[0];
        public Transform player;
        public FootScreen screen;

        LifeState S => LifeSimManager.State;

        Viewing visit;
        FootTarget carHook, streetHook;
        bool built;

        void Start() => PreviewBuild();

        /// <summary>
        /// Fill the street.
        ///
        /// Named for the tool that needs it public, the same way
        /// <see cref="GarageWorld.PreviewBuild"/> is: AddComponent does not run
        /// Start outside play mode, so a reference shot of this scene would
        /// otherwise be a photograph of five empty plots. Idempotent, so the
        /// two callers cannot double the contents.
        /// </summary>
        public void PreviewBuild()
        {
            if (built) return;
            built = true;

            visit = Viewings.ByKey(S, S.activeViewing);

            // A seed the ADVERT owns, not the clock. Same advert, same street.
            int seed = string.IsNullOrEmpty(S.activeViewing)
                ? 12345 : S.activeViewing.GetHashCode();
            var rng = new System.Random(seed);

            for (int i = 0; i < houses.Length; i++)
            {
                var set = houses[i];
                if (set == null || set.variants == null || set.variants.Length == 0) continue;
                int pick = rng.Next(set.variants.Length);
                for (int v = 0; v < set.variants.Length; v++)
                    if (set.variants[v] != null) set.variants[v].SetActive(v == pick);
            }

            // Always the middle plot. There is a neighbour either side however
            // the dice fall, which is what keeps a lone house on an empty
            // street from reading as a test level.
            int plotIndex = plots.Length / 2;
            var plot = plots.Length > 0 ? plots[Mathf.Clamp(plotIndex, 0, plots.Length - 1)] : null;
            if (plot == null) return;

            var carSpot = plot.Find("CarSpot");
            var stand = plot.Find("Stand");

            if (visit != null && visit.car != null && carSpot != null)
            {
                var spec = CarCatalog.Get(visit.car.specId);
                var def = CarShell.DefFor(spec);
                if (def != null)
                {
                    int skin = CarShell.SkinFor(def, spec, visit.key.GetHashCode());
                    CarShell.Spawn(carSpot, def, skin, out Vector3 roof);

                    var hookGO = new GameObject("SellerCar");
                    hookGO.transform.SetParent(carSpot, false);
                    // Off the tarmac. A car's transform sits between its axles
                    // at road height, and a hook that aims THERE is a hook the
                    // ground itself stands in front of — the exact failure the
                    // garage bays hit and the reason every car hook aims at the
                    // roof line.
                    var focus = new GameObject("Focus");
                    focus.transform.SetParent(carSpot, false);
                    focus.transform.localPosition = roof;

                    carHook = hookGO.AddComponent<FootTarget>();
                    carHook.range = 4.8f;
                    carHook.focus = focus.transform;
                    // See-through for the sight test, or the car blocks itself:
                    // the shell carries a box collider you walk around, so a ray
                    // cast at its roof from beside it hits the car.
                    carHook.ignoreRoot = carSpot;
                }
            }

            // Somewhere to stand that is not on the car. Also the way out.
            if (stand != null)
            {
                var go = new GameObject("Kerb");
                go.transform.SetParent(stand, false);
                go.transform.localPosition = new Vector3(0f, 1.2f, -3.5f);
                streetHook = go.AddComponent<FootTarget>();
                streetHook.range = 3.4f;
                streetHook.onUse = () => GoHome(visit != null ? "viewing" : "market");
            }

            // Put the player on the plot the visit is actually on. Through the
            // CharacterController rather than around it: writing transform
            // .position under a live CC is a fight the CC wins on the next
            // Move, and the player ends up back where the scene was baked.
            if (player != null && stand != null)
            {
                var cc = player.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                player.SetPositionAndRotation(stand.position, stand.rotation);
                if (cc != null) cc.enabled = true;
                var walk = player.GetComponent<FirstPersonWalk>();
                if (walk != null) walk.SnapYawToTransform();
            }

            RefreshLabels();
        }

        /// <summary>Rewrite the prompts from the visit as it stands. Called on
        /// arrival and after anything changes what the player knows.</summary>
        public void RefreshLabels()
        {
            if (carHook != null)
            {
                var listing = Viewings.ListingFor(S, visit);
                int known = Viewings.KnownFaults(visit);
                carHook.title = visit != null && visit.car != null
                    ? visit.car.displayName.ToUpperInvariant() : "A CAR FOR SALE";
                carHook.detail = visit == null || listing == null
                    ? "The advert has gone."
                    : listing.odoMiles.ToString("N0") + " mi   ·   asking " +
                      MenuKit.Money(visit.askPrice) +
                      (visit.offerPrice < visit.askPrice
                          ? "   ·   they will take " + MenuKit.Money(visit.offerPrice) : "") +
                      (known > 0 ? "   ·   " + known + " known problem" + (known == 1 ? "" : "s")
                                 : visit.lookedOver ? "   ·   nothing found yet" : "");
                carHook.action = "TALK TO THE SELLER";
                carHook.onUse = () => GoHome("viewing");
                // The second verb, and the same one a car in the player's own
                // garage carries: getting UNDER it rather than into it.
                carHook.action2 = "GET UNDER IT";
                carHook.onUse2 = OpenInspection;
            }

            if (streetHook != null)
            {
                streetHook.title = "THE STREET";
                streetHook.detail = "Your own car is at the kerb.";
                streetHook.action = "DRIVE OFF";
            }

            screen?.Invalidate();
        }

        /// <summary>
        /// Get under somebody else's car.
        ///
        /// The whole component map, on a phantom — which works only because a
        /// visit carries a real OwnedCar. The slot is spent by
        /// <see cref="Inspection.Enter"/>'s own day latch, so a second look on
        /// the same afternoon is free and tomorrow's costs again.
        /// </summary>
        void OpenInspection()
        {
            if (visit == null || visit.car == null) return;
            bool wasOpen = Inspection.OpenToday(S, visit.car);
            Inspection.Enter(S, visit.car);
            LifeHomeScreen.PendingInspectCar = visit.car.id;
            // FINISH INSPECTION walks back out here, not into a menu the player
            // never opened.
            LifeHomeScreen.InspectReturnScene = TrackCatalog.SellerLotSceneIndex;
            screen?.Toast(wasOpen ? "BACK UNDER IT" : "HAVING A PROPER LOOK");
            GoHome("inspect");
        }

        void GoHome(string tab)
        {
            LifeHomeScreen.PendingTab = tab;
            LifeSimManager.Save();
            // A browser keeps pointer lock across a scene load, so without this
            // the player arrives at a menu they cannot click on.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            SceneManager.LoadScene(0);
        }
    }
}
