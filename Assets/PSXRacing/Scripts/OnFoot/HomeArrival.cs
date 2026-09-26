using System.Collections;
using UnityEngine;

namespace PSXRacing.OnFoot
{
    /// <summary>
    /// Arriving on your own street ON FOOT (<see cref="HomeWalk"/>): the car
    /// with the keys parked in the garage, and the player on the drive looking
    /// up at the house - the picture the walk-in scene opened on, in the one
    /// house there is now.
    ///
    /// Waits a frame and a physics step first, so the scene's own start-up
    /// (the handoff putting the right car on the rig, CityMode standing it on
    /// the drive) has finished before the car is moved; a teleport under that
    /// is a teleport it undoes. With no car of the player's at home the
    /// scene's car is stood down: switched off, not hidden, so nothing can
    /// drive it and nothing can bump into it.
    /// </summary>
    public class HomeArrival : MonoBehaviour
    {
        [Header("Wired by the scene builder")]
        public CarController car;
        /// <summary>Bay 0: the garage, nose out of the door.</summary>
        public Transform garageBay;
        /// <summary>Where the walker starts, and which way they face.</summary>
        public Transform walkerStart;
        public ForecourtMode forecourt;

        /// <summary>The arrival has finished placing things (or there was no
        /// arrival to make). GarageWorld waits on it to fill the bays.</summary>
        public static bool Settled { get; private set; }

        /// <summary>A car's root stands this far over the ground it will
        /// settle onto - the height the scene's own spawn uses.</summary>
        const float SpawnLift = 0.35f;

        bool onFoot, noCar;

        void Awake()
        {
            Settled = false;
            // Read ONCE and cleared: a flag that outlived this scene would
            // turn the next drive home into a walk.
            onFoot = RaceHandoff.ArriveOnFoot;
            noCar = RaceHandoff.NoCar;
            RaceHandoff.ArriveOnFoot = false;
            RaceHandoff.NoCar = false;
        }

        IEnumerator Start()
        {
            yield return null;
            yield return new WaitForFixedUpdate();
            if (onFoot)
            {
                if (noCar || car == null)
                {
                    if (car != null) car.gameObject.SetActive(false);
                }
                else if (garageBay != null)
                {
                    car.TeleportTo(garageBay.position + Vector3.up * SpawnLift, garageBay.rotation);
                    car.handbrakeInput = true;
                }
                if (forecourt != null && walkerStart != null)
                    forecourt.BeginAfoot(walkerStart.position, walkerStart.rotation);
                // Let it settle onto its springs before GarageWorld measures
                // it: the lift takes the car up from the height it RESTS at.
                if (!noCar && car != null)
                    for (int i = 0; i < 40; i++) yield return new WaitForFixedUpdate();
            }
            Settled = true;
        }
    }
}
