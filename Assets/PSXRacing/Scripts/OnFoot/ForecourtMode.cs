using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PSXRacing.OnFoot
{
    /// <summary>
    /// Getting out of the car at the pumps, and everything that follows from
    /// it: filling the tank by hand, walking into the shop, and getting back
    /// in.
    ///
    /// Fuelling used to be a control you held from the driver's seat, which
    /// worked and read as a menu. It is now the thing it actually is — you stop,
    /// you shut the engine off, you get out, and the exhaust ticks while you
    /// stand there. The ceremony is not decoration: it is the reason the
    /// forecourt is a place rather than a trigger volume, and it is what makes
    /// the shop worth putting a door on.
    ///
    /// The first-person kit is exactly the one the walk-in garage uses —
    /// <see cref="FirstPersonWalk"/>, <see cref="FootInteractor"/>,
    /// <see cref="FootTarget"/>, <see cref="FootScreen"/>,
    /// <see cref="FootTouchPanel"/>. That is why those five stopped being
    /// called Garage-anything: a garage was simply the first room they were
    /// asked to stand in.
    ///
    /// The RACE CAMERA is borrowed rather than replaced. It carries
    /// PSXCameraOutput — the render texture, the dither blit, the whole
    /// picture — so a second camera would mean a second framebuffer and a HUD
    /// pointing at the wrong one. ChaseCamera is switched off, the camera is
    /// parented to the walker's head, and both are handed back on the way in.
    /// </summary>
    public class ForecourtMode : MonoBehaviour
    {
        [Header("Wired by the scene builder")]
        public CarController playerCar;
        public Camera raceCamera;
        public ChaseCamera chase;
        public PlayerCarInput carInput;
        public EngineAudio engine;

        /// <summary>True from the moment the driver's door shuts behind them to
        /// the moment it shuts in front of them. Read by the HUD and by
        /// StuckRecovery, both of which have nothing useful to say about a car
        /// with nobody in it.</summary>
        public static bool OnFoot { get; private set; }

        /// <summary>The IN-CAR line — what to press, and why you would. The
        /// fuelling numbers are <see cref="GasPump"/>'s; this is only the
        /// invitation.</summary>
        public static string Prompt { get; private set; }

        /// <summary>Below this the car counts as stopped. Same threshold the
        /// pump uses, and for the same reason: you cannot open a door at
        /// speed.</summary>
        const float StopKmh = 4.5f;

        enum Phase { InCar, GettingOut, Afoot, GettingIn }
        Phase phase = Phase.InCar;

        // the rig
        GameObject walker;
        Transform head;
        FirstPersonWalk walk;
        FootInteractor interactor;
        FootScreen screen;
        FootTouchPanel touchPanel;
        StoreScreen store;

        FootTarget pumpTarget, storeTarget, carTarget;
        Transform camHome;
        AudioSource carAudio;
        float engineVolume = 1f;
        bool drivingPanelWasVisible;

        void Awake()
        {
            OnFoot = false;
            Prompt = null;
        }

        void Start()
        {
            if (playerCar != null)
            {
                carAudio = playerCar.gameObject.AddComponent<AudioSource>();
                carAudio.playOnAwake = false;
                carAudio.spatialBlend = 1f;
                carAudio.rolloffMode = AudioRolloffMode.Linear;
                carAudio.maxDistance = 40f;
            }
            if (engine != null) engineVolume = engine.masterVolume;
        }

        void Update()
        {
            if (phase == Phase.InCar) TickInCar();
            else if (phase == Phase.Afoot) TickAfoot();
        }

        // ------------------------------------------------------------------
        //  in the car
        // ------------------------------------------------------------------
        void TickInCar()
        {
            Prompt = null;
            if (playerCar == null || !GasPump.AtPump || PauseMenu.IsOpen) return;
            // Not on the grid and not after the flag — the two windows where the
            // player does not have the car.
            if (carInput != null && !carInput.inputEnabled) return;

            var tank = playerCar.GetComponent<FuelTank>();
            // Nothing to stop for. A full car crossing its own forecourt every
            // lap does not need to be asked whether it wants fuel.
            if (tank != null && tank.percent >= 99.5f) return;

            if (Mathf.Abs(playerCar.speedKmh) > StopKmh)
            {
                Prompt = "PUMP — STOP TO FILL UP";
                return;
            }

            Prompt = UseControlName() + " TO GET OUT AND FUEL";
            if (UsePressed()) StartCoroutine(GetOut());
        }

        // ------------------------------------------------------------------
        //  on foot
        // ------------------------------------------------------------------
        void TickAfoot()
        {
            if (store != null && store.IsOpen) { GasPump.WalkerAtNozzle = false; return; }

            // The pump fills only for somebody standing at it. GasPump keeps the
            // whole transaction — wallet, tank, save — so that there is one
            // implementation of buying fuel and not two.
            GasPump.WalkerAtNozzle = interactor != null && interactor.Current == pumpTarget;

            RefreshLabels();
        }

        void RefreshLabels()
        {
            var tank = playerCar != null ? playerCar.GetComponent<FuelTank>() : null;

            if (pumpTarget != null)
            {
                pumpTarget.title = "FUEL PUMP";
                pumpTarget.detail = tank == null ? ""
                    : tank.percent >= 99.5f
                        ? "The tank is full."
                        : "Tank " + Mathf.FloorToInt(tank.percent) + "%   ·   " +
                          HoldName() + " to fill it";
                // No ACTION on purpose. An action would consume the same key the
                // pump reads to fill, and a nozzle is a thing you hold, not a
                // thing you press once.
                pumpTarget.action = "";
            }

            if (carTarget != null)
            {
                carTarget.title = "YOUR CAR";
                carTarget.detail = tank == null ? ""
                    : "Tank " + Mathf.FloorToInt(tank.percent) + "%";
                carTarget.action = "GET IN AND DRIVE";
            }

            if (storeTarget != null)
            {
                storeTarget.title = "6TWELVE";
                storeTarget.detail = "Coffee, food, and somewhere to stand out of the rain.";
                storeTarget.action = "GO INSIDE";
            }
        }

        // ------------------------------------------------------------------
        //  the ceremony
        // ------------------------------------------------------------------
        /// <summary>
        /// Out of the car. Engine off first, because everything else is a
        /// consequence of it: the exhaust only ticks once it has stopped
        /// burning, and the door only opens once the car is parked.
        /// </summary>
        IEnumerator GetOut()
        {
            phase = Phase.GettingOut;
            Prompt = null;

            if (carInput != null) carInput.inputEnabled = false;
            playerCar.throttleInput = 0f;
            playerCar.brakeInput = 0f;
            playerCar.handbrakeInput = true;

            if (engine != null)
            {
                engine.PlayShutdown();
                // The shutdown sample rides its own one-shot source; masterVolume
                // scales the running band loops. Silencing them here is what
                // makes "off" mean off rather than "idling quietly".
                engine.masterVolume = 0f;
            }
            Play(FootAudio.Crackle, 0.55f);

            yield return new WaitForSeconds(0.6f);
            Play(FootAudio.DoorOpen, 0.9f);
            yield return new WaitForSeconds(0.55f);
            Play(FootAudio.DoorClose, 1f);
            yield return new WaitForSeconds(0.25f);

            EnsureRig();
            PlaceWalker();
            TakeCamera();
            SetDrivingPanel(false);
            walker.SetActive(true);
            if (screen != null) screen.show = true;
            OnFoot = true;
            phase = Phase.Afoot;
            RefreshLabels();
        }

        /// <summary>Back in. The view returns first so the press feels
        /// answered; the doors and the starter play over the top of it, which
        /// is the order they happen in when you actually get into a car.
        /// </summary>
        IEnumerator GetIn()
        {
            phase = Phase.GettingIn;
            GasPump.WalkerAtNozzle = false;
            OnFoot = false;

            if (screen != null) screen.show = false;
            if (walker != null) walker.SetActive(false);
            ReleaseCamera();
            SetDrivingPanel(drivingPanelWasVisible);

            Play(FootAudio.DoorOpen, 0.9f);
            yield return new WaitForSeconds(0.5f);
            Play(FootAudio.DoorClose, 1f);
            yield return new WaitForSeconds(0.35f);

            if (engine != null)
            {
                engine.masterVolume = engineVolume;
                engine.PlayStartup();
            }
            yield return new WaitForSeconds(0.55f);

            playerCar.handbrakeInput = false;
            if (carInput != null) carInput.inputEnabled = true;
            phase = Phase.InCar;
        }

        void Play(AudioClip clip, float volume)
        {
            if (carAudio == null || clip == null) return;
            carAudio.PlayOneShot(clip, volume);
        }

        // ------------------------------------------------------------------
        //  the rig
        // ------------------------------------------------------------------
        void EnsureRig()
        {
            if (walker != null) return;

            walker = new GameObject("Walker");
            var body = walker.AddComponent<CharacterController>();
            body.height = 1.75f;
            body.radius = 0.3f;
            body.center = new Vector3(0f, 0.9f, 0f);
            body.slopeLimit = 55f;
            // Low, for the same reason the garage's is: a car's collider starts
            // about 22 cm off the ground and the default step height walks you
            // onto the bonnet.
            body.stepOffset = 0.15f;

            var headGO = new GameObject("Head");
            headGO.transform.SetParent(walker.transform, false);
            headGO.transform.localPosition = new Vector3(0f, 1.62f, 0f);
            head = headGO.transform;

            walk = walker.AddComponent<FirstPersonWalk>();
            walk.head = head;

            interactor = walker.AddComponent<FootInteractor>();
            interactor.eye = head;

            var ui = new GameObject("ForecourtUI");
            ui.transform.SetParent(transform, false);
            touchPanel = ui.AddComponent<FootTouchPanel>();
            touchPanel.walker = walk;
            touchPanel.interactor = interactor;

            screen = ui.AddComponent<FootScreen>();
            screen.interactor = interactor;
            screen.walker = walk;
            screen.panel = touchPanel;
            screen.place = "FORECOURT";
            // The race HUD is already printing the lap, the position and the
            // fuel bar. A second header quoting the wallet and the date over
            // the top of it would be two games talking at once.
            screen.showWallet = false;
            screen.show = false;

            store = ui.AddComponent<StoreScreen>();
            store.onClosed = () => { if (walk != null) walk.enabled = true; };

            BuildTargets();
            walker.SetActive(false);
        }

        void BuildTargets()
        {
            // The pump the car is actually parked at, not the first one in the
            // scene: the station carries several and they are metres apart.
            Transform pump = NearestNamed("Pump");
            Transform door = NearestNamed("StoreDoor");

            pumpTarget = MakeTarget(pump, "PumpTarget", 4.2f);
            storeTarget = MakeTarget(door, "StoreTarget", 4.2f);
            carTarget = MakeTarget(playerCar != null ? playerCar.transform : null, "CarTarget", 3.6f);

            if (storeTarget != null) storeTarget.onUse = OpenStore;
            if (carTarget != null) carTarget.onUse = () =>
            {
                if (phase == Phase.Afoot) StartCoroutine(GetIn());
            };
        }

        /// <summary>The nearest object with this name to the parked car. The
        /// forecourt has ten pump volumes and one shop; which pump matters is
        /// decided by where the player stopped.</summary>
        Transform NearestNamed(string name)
        {
            if (playerCar == null) return null;
            Transform best = null;
            float bestD = float.MaxValue;
            foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (t.name != name) continue;
                float d = (t.position - playerCar.transform.position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = t; }
            }
            return best;
        }

        FootTarget MakeTarget(Transform where, string name, float range)
        {
            if (where == null) return null;
            var go = new GameObject(name);
            go.transform.SetParent(where, false);
            var t = go.AddComponent<FootTarget>();
            t.range = range;
            return t;
        }

        /// <summary>Beside the driver's door, facing the car. Left-hand side:
        /// this game's cars are left-hand drive and the shell it is wearing does
        /// not change which side the driver got out of.</summary>
        void PlaceWalker()
        {
            var car = playerCar.transform;
            Vector3 at = car.position - car.right * 1.35f + car.forward * 0.2f;
            at.y = car.position.y + 0.4f;
            walker.transform.SetPositionAndRotation(
                at, Quaternion.LookRotation(car.position - at, Vector3.up));
        }

        void TakeCamera()
        {
            if (raceCamera == null) return;
            if (chase != null) chase.enabled = false;
            camHome = raceCamera.transform.parent;
            raceCamera.transform.SetParent(head, false);
            raceCamera.transform.localPosition = Vector3.zero;
            raceCamera.transform.localRotation = Quaternion.identity;
        }

        void ReleaseCamera()
        {
            if (raceCamera == null) return;
            raceCamera.transform.SetParent(camHome, false);
            if (chase != null) chase.enabled = true;
        }

        /// <summary>The wheel and pedals are for somebody sitting down. Hidden
        /// while the player is out, and put back exactly as they were found —
        /// a desktop player never had them and must not be given them.</summary>
        void SetDrivingPanel(bool show)
        {
            var touch = TouchControls.Instance;
            if (touch == null) return;
            if (show == false) drivingPanelWasVisible = touch.Visible;
            touch.SetVisible(show);
        }

        void OpenStore()
        {
            if (store == null) return;
            if (walk != null) walk.enabled = false;
            store.Open();
        }

        // ------------------------------------------------------------------
        //  controls
        // ------------------------------------------------------------------
        static bool UsePressed()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.fKey.wasPressedThisFrame) return true;
            var pad = Gamepad.current;
            if (pad != null && pad.buttonSouth.wasPressedThisFrame) return true;
            var touch = TouchControls.Instance;
            return touch != null && touch.Visible && touch.ActionPressed;
        }

        static string UseControlName()
        {
            if (TouchControls.Instance != null && TouchControls.Instance.Visible)
                return "TAP FUEL (TOP RIGHT)";
            return Gamepad.current != null ? "PRESS X / A" : "PRESS F";
        }

        static string HoldName()
        {
            if (TouchControls.Instance != null && TouchControls.Instance.Visible) return "hold USE";
            return Gamepad.current != null ? "hold X / A" : "hold F";
        }
    }
}
