using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// The configured wheel, read once a frame: what PlayerCarInput, the
    /// camera and the pause menu take from it. Nothing here touches the car -
    /// see <see cref="PlayerCarInput"/> for where it meets the physics.
    /// Buttons are edge-detected here, once a frame, so every reader sees the
    /// same press on the same frame.
    /// </summary>
    public static class WheelInput
    {
        static int frame = -1;
        static bool active;
        static float? steer;
        static float throttle, brake, clutch;
        static readonly bool[] held = new bool[7];
        static readonly bool[] was = new bool[7];

        enum B { ShiftUp, ShiftDown, Handbrake, LookBack, Reset, Camera, Pause }

        /// <summary>Overrides the "is this frame's reading fresh" stamp (tests).</summary>
        public static void Invalidate() { frame = -1; RawPads.Invalidate(); }

        static void Tick()
        {
            if (frame == Time.frameCount && Time.frameCount > 0) return;
            frame = Time.frameCount;
            var d = WheelConfig.Data;
            active = WheelConfig.Active;
            for (int i = 0; i < held.Length; i++) was[i] = held[i];
            if (!active)
            {
                steer = null; throttle = brake = clutch = 0f;
                for (int i = 0; i < held.Length; i++) held[i] = false;
                return;
            }
            steer = WheelConfig.SteerNow();
            throttle = WheelConfig.Read(d.throttle, d.pedalDeadPct, WheelConfig.Inverted(WheelConfig.InvThrottle));
            brake = WheelConfig.Read(d.brake, d.pedalDeadPct, WheelConfig.Inverted(WheelConfig.InvBrake));
            clutch = WheelConfig.Read(d.clutch, d.pedalDeadPct, WheelConfig.Inverted(WheelConfig.InvClutch));
            held[(int)B.ShiftUp] = WheelConfig.Held(d.shiftUp);
            held[(int)B.ShiftDown] = WheelConfig.Held(d.shiftDown);
            held[(int)B.Handbrake] = WheelConfig.Held(d.handbrake);
            held[(int)B.LookBack] = WheelConfig.Held(d.lookBack);
            held[(int)B.Reset] = WheelConfig.Held(d.reset);
            held[(int)B.Camera] = WheelConfig.Held(d.camera);
            held[(int)B.Pause] = WheelConfig.Held(d.pause);
        }

        static bool Pressed(B b) { Tick(); return held[(int)b] && !was[(int)b]; }
        static bool Held(B b) { Tick(); return held[(int)b]; }

        /// <summary>A wheel is set up, switched on, and its steering axis is live.</summary>
        public static bool Active { get { Tick(); return active && steer.HasValue; } }
        /// <summary>-1 (full left) .. 1 (full right) of the base's rotation.</summary>
        public static float Steer { get { Tick(); return steer ?? 0f; } }
        public static float Throttle { get { Tick(); return throttle; } }
        public static float Brake { get { Tick(); return brake; } }
        public static float Clutch { get { Tick(); return clutch; } }
        public static bool Handbrake => Held(B.Handbrake);
        public static bool LookBack => Held(B.LookBack);
        public static bool ShiftUpPressed => Pressed(B.ShiftUp);
        public static bool ShiftDownPressed => Pressed(B.ShiftDown);
        public static bool ResetPressed => Pressed(B.Reset);
        public static bool CameraPressed => Pressed(B.Camera);
        public static bool PausePressed => Pressed(B.Pause);

        /// <summary>
        /// The road-wheel angle (degrees) this wheel asks of <paramref name="car"/>,
        /// by STEER RATIO - see <see cref="WheelConfig"/>.
        /// </summary>
        public static float RoadDegrees(CarController car)
        {
            var d = WheelConfig.Data;
            float lockDeg = car != null ? car.maxSteerLowSpeedDeg : 34f;
            return WheelConfig.RoadDegrees(Steer, d.rotationDeg, d.ratioMode, d.linearityPct, lockDeg);
        }
    }
}
