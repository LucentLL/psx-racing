using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// THE OWNER'S TRAFFIC VEHICLES (2026-11-15): the "UAA - City Props -
    /// Vehicles" pack as traffic shells. Resources/TrafficKit.asset, written
    /// by Editor/TrafficUaaBaker.cs from the gitignored store files
    /// (tools/store-assets.ps1) in a PC content pass (PSX_PC_CONTENT=1) and
    /// deleted by any other pass - so a checkout or a build without the pack
    /// has no kit, and <see cref="TrafficModels"/> then deals today's models.
    ///
    /// Each entry is one vehicle: a <see cref="CarModelDef"/> like every other
    /// shell's (so CarShell, CarLights and both traffic systems take it as it
    /// is), plus what a four-wheel car def cannot say: its kind (what road it
    /// belongs on), a dual rear wheel, more axles, and the far level.
    /// </summary>
    public class TrafficKit : ScriptableObject
    {
        public const string ResourcePath = "TrafficKit";

        [System.Serializable]
        public class Entry
        {
            public string key;
            /// <summary>TrafficModels.Car .. Rig.</summary>
            public int kind;
            public CarModelDef def;
            /// <summary>Body and every wheel in one mesh, the body's frame:
            /// the far level (one draw call for a car a hundred metres off).</summary>
            public Mesh farMesh;
            /// <summary>The rear axle's own wheel (a dual), and half its
            /// track where that is not the front's. Null / 0: as the front.</summary>
            public Mesh rearWheelMesh;
            public float rearHalfTrack;
            /// <summary>Hubs of the axles past the second (a rig's second
            /// drive axle and its trailer's), car frame: x &lt; 0 the left.</summary>
            public Vector3[] extraWheels;
            public float length, width, height;
            public int bodyTris, wheelTris;
        }

        public Entry[] entries;
        /// <summary>The pack version and the day of the bake, for the log.</summary>
        public string builtFrom;
    }
}
