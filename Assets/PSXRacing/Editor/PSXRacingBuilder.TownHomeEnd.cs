using System.Collections.Generic;
using UnityEngine;
using PSXRacing;
using PSXRacing.OnFoot;
using PSXRacing.Town;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE HOME END: your street, your house and the neighbours, built onto
    /// the WEST end of the town map and joined to its main street by a road.
    ///
    /// The owner, 2026-11-14: "I want Town and Home/Neighborhood merged onto a
    /// single map. This area will be purely for testing in debugging." Then:
    /// "add the neighborhood to one end of the large town map. don't rewire
    /// the town."
    ///
    /// So nothing of the town moves. The street is built by the street map's
    /// own builder (PSXRacingBuilder.Neighborhood.cs, the same functions and
    /// the same numbers, so the house you walk round is still that house) in
    /// its own frame, BEFORE any of the town exists — its ground probes
    /// (GroundYAt, the garage bays' overlap test) would otherwise find the
    /// town's lawn under them — and then the whole street is turned a quarter
    /// round and moved as one transform, so that it runs on along the main
    /// street's own line: its junction end meets the end of the main street
    /// through HomeEndRoadM of new road, level with the town, and your house is
    /// up the hill at the far west end. What the town gives up for it is its
    /// west boundary wall and its west zone line, which stood across that road.
    ///
    /// Three things in the street's builder answer to <see cref="nbMerged"/>:
    /// past the street's end the road profile stops rolling and the land's
    /// relief fades out, so the junction end lies flat at the town's own lawn
    /// height; the lawn lattice stops at the town lawn's west edge rather than
    /// running a hundred metres on under it; and the run-off verge at the end
    /// of the street is not built, because the road carries on there.
    ///
    /// PSX_TOWN_HOMEEND=0 when the scene is built: the town as it was, with
    /// both zone lines and its west wall. The runtime reads the same switch
    /// (TownLayout) to decide which scene HOME is.
    /// </summary>
    public static partial class PSXRacingBuilder
    {
        static bool TownHomeEndOn => System.Environment.GetEnvironmentVariable("PSX_TOWN_HOMEEND") != "0";

        /// <summary>True while BuildTownScene is building the merged map: the
        /// town's kerbs taper at the west end and its west wall and line are
        /// left out (BuildTownKerbs, BuildTownBounds).</summary>
        static bool townHomeEndBuild;

        /// <summary>True while the street is being built for the merged map —
        /// read by NbRoadY, NbLandY, SolveNbLattice, BuildNbGround and
        /// BuildNbPlots. Never true for Neighborhood.unity's own build.</summary>
        static bool nbMerged;

        /// <summary>The new road from the end of the main street to the end of
        /// your street. Long enough that the street's lawn lattice (which runs
        /// a run-off past its end) stops exactly where the town's lawn begins.</summary>
        const float HomeEndRoadM = 110f;
        /// <summary>A quarter turn: the street's "up the hill" (+z in its own
        /// frame) becomes west (-x), so its junction end faces the town.</summary>
        const float HomeEndYawDeg = -90f;
        /// <summary>Half of BuildTownGround's lawn slab, TownStreetHalf * 2 + 200
        /// wide and centred on x = 0.</summary>
        const float TownGroundHalfX = TownStreetHalf + 100f;

        /// <summary>World x where the new road meets the end of your street.</summary>
        static float HomeEndRoadWestX => -TownRoadHalf - HomeEndRoadM;

        /// <summary>The street frame's origin in the town: its end of the road
        /// at HomeEndRoadWestX, its crown on the main street's line (z 0), and
        /// its road surface at the end level with the town's tarmac.</summary>
        static Vector3 HomeEndOffset =>
            new Vector3(HomeEndRoadWestX + NbStreetEnd, -NbRoadY(NbStreetEnd), -HomeStreetX);

        static Quaternion HomeEndRotation => Quaternion.Euler(0f, HomeEndYawDeg, 0f);

        /// <summary>A point in the street's own frame, in the town's.</summary>
        static Vector3 HomeEndToWorld(Vector3 native) => HomeEndRotation * native + HomeEndOffset;

        /// <summary>The street-frame z whose town x is the town lawn's west edge:
        /// where the merged lawn lattice stops (SolveNbLattice).</summary>
        static float HomeEndLatticeMinZ => HomeEndOffset.x + TownGroundHalfX;

        /// <summary>The neighbours' doors a delivery can go to, their markers and
        /// their addresses, filled by BuildNbPlots through NbDropDoor while
        /// <see cref="nbMerged"/> is set.</summary>
        class HomeEndParts
        {
            public Transform root, spawn, walkStart;
            public GarageSceneBuilder.HomeRooms rooms;
            public Transform[] respawns;
            public readonly List<Transform> doors = new List<Transform>();
            public readonly List<GameObject> markers = new List<GameObject>();
            public readonly List<string> names = new List<string>();
        }
        static HomeEndParts nbDropSink;

        /// <summary>
        /// Build the street in its own frame and move it into the town. Called
        /// FIRST by BuildTownScene, on an empty scene: see the class note.
        /// </summary>
        static HomeEndParts BuildHomeEnd(TownMats m)
        {
            var parts = new HomeEndParts();
            nbMerged = true;
            nbDropSink = parts;
            // ITS OWN MESH ASSETS. Every slab and kerb here is saved by name
            // into Generated/, and Neighborhood.unity saves the same names: the
            // merged street's ground is a different shape at its junction end,
            // so whichever scene was built last would otherwise quietly reshape
            // the other's lawn — and its collider with it.
            string prefix = WorldKit.MeshPrefix;
            WorldKit.MeshPrefix = "TownHome_";
            try
            {
                nbGarageChecked = false;
                nbPlotEdges = null;
                townHomeHouse = null;

                var root = new GameObject("HomeEnd");
                parts.root = root.transform;
                BuildNbGround(root.transform, m);
                parts.spawn = BuildTownHome(root.transform, m);
                BuildNbPlots(root.transform, m);

                // The walk-in rooms round your own house, exactly as the street
                // map lays them out (BuildNeighborhoodScene).
                if (townHomeHouse != null)
                {
                    parts.rooms = GarageSceneBuilder.BuildHomeRooms(root.transform, townHomeHouse,
                                                                    townHomeDoorX, townHomeDoorZ);
                    SettleHomeBays(parts.rooms.bays);
                    var start = new GameObject("WalkInStart");
                    start.transform.SetParent(root.transform, false);
                    float sz = townHomeDoorZ - 8.3f;
                    start.transform.SetPositionAndRotation(
                        new Vector3(townHomeDoorX, GroundYAt(townHomeDoorX, sz) + 0.2f, sz),
                        Quaternion.LookRotation(Vector3.forward, Vector3.up));
                    parts.walkStart = start.transform;
                }
                parts.respawns = BuildNbRespawns(root.transform);

                // AND INTO THE TOWN, as one transform.
                root.transform.SetPositionAndRotation(HomeEndOffset, HomeEndRotation);
                Physics.SyncTransforms();
                Log("[Town] home end: your street turned " + HomeEndYawDeg.ToString("0") +
                    " deg and moved to " + HomeEndOffset.ToString("0.00") + "; " + parts.doors.Count +
                    " doors a delivery can go to; the road in is " + HomeEndRoadM.ToString("0") +
                    " m, x " + HomeEndRoadWestX.ToString("0") + " to " + (-TownRoadHalf).ToString("0"));
            }
            finally
            {
                nbMerged = false;
                nbDropSink = null;
                WorldKit.MeshPrefix = prefix;
            }
            return parts;
        }

        /// <summary>
        /// A neighbour's FRONT DOOR, as somewhere an order can go: an anchor a
        /// step in front of the facade on the half of it away from the garage,
        /// facing the street, and the marker that says "this one" — a pin over
        /// the roof and a row of dots on the tarmac in front, in pizza-box
        /// orange so they are never read as the blue zone line. The marker is
        /// built OFF; TownWorld turns on the one the order is for.
        /// </summary>
        static void NbDropDoor(Transform parent, string plotTag, int pi, int side, float plotZ, float padY)
        {
            if (nbDropSink == null) return;
            var door = TownAnchor(parent, "DropDoor" + plotTag,
                new Vector3(HomeStreetX + side * (NbGarageFaceOut - 2.0f), padY + 1.1f, plotZ - side * 1.6f),
                new Vector3(-side, 0f, 0f));

            var marker = new GameObject("DropMarker" + plotTag);
            marker.transform.SetParent(parent, false);
            var mat = MakeMat("DropKnot", null, tint: DropKnotTint);
            mat.SetFloat("_Emission", 1f);
            Vector3 d = door.position;
            // The pin: a big diamond well clear of the roof, two smaller ones
            // pointing down at the door. Turned about the street's across axis
            // so each one shows the street a diamond, not a square.
            DropKnot(marker.transform, mat, new Vector3(d.x, padY + 12.2f, d.z), 1.7f, Vector3.right);
            DropKnot(marker.transform, mat, new Vector3(d.x, padY + 10.3f, d.z), 0.8f, Vector3.right);
            DropKnot(marker.transform, mat, new Vector3(d.x, padY + 9.3f, d.z), 0.55f, Vector3.right);
            // The dots: on the tarmac by this side's kerb, in front of the
            // house — where to stop. The zone line's size and lift.
            float kx = HomeStreetX + side * (NbKerbOut - 0.9f);
            for (int i = -2; i <= 2; i++)
            {
                float kz = plotZ + i * 1.5f;
                DropKnot(marker.transform, mat,
                    new Vector3(kx, NbRoadY(kz) + NbStreetLift + ZoneKnotLift, kz), ZoneKnotSize, Vector3.forward);
            }
            marker.SetActive(false);

            // Odd numbers down one side, even down the other, climbing away
            // from the main road the way a street is numbered.
            int number = 100 + 12 * pi + (side < 0 ? 1 : 2);
            nbDropSink.doors.Add(door);
            nbDropSink.markers.Add(marker);
            nbDropSink.names.Add(number + " " + TownLayout.StreetShort);
        }

        /// <summary>Pizza-box orange: no lamp, sign or line in town uses it.</summary>
        static readonly Color DropKnotTint = new Color(1.00f, 0.52f, 0.08f);

        static void DropKnot(Transform parent, Material mat, Vector3 at, float size, Vector3 axis)
        {
            var k = GameObject.CreatePrimitive(PrimitiveType.Cube);
            k.name = "Knot";
            Object.DestroyImmediate(k.GetComponent<Collider>());
            k.transform.SetParent(parent, false);
            k.transform.position = at;
            k.transform.rotation = Quaternion.AngleAxis(45f, axis);
            k.transform.localScale = Vector3.one * size;
            var mr = k.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        /// <summary>
        /// THE ROAD IN: the main street carried on west, at its own width and
        /// height, to the end of your street — and the boundary round the home
        /// end, which the town's west wall used to be. World coordinates; built
        /// after the town's ground. Returns two respawns on it, eastbound.
        /// </summary>
        static Transform[] BuildHomeEndRoad(Transform parent, TownMats m)
        {
            var road = new GameObject("HomeRoad");
            road.transform.SetParent(parent, false);
            float x0 = HomeEndRoadWestX, x1 = -TownRoadHalf;
            // The main street's tarmac, carried on: same width, same height,
            // same world-anchored texture, so the join at x1 is not a seam.
            WorldKit.GridSlab(road.transform, "HomeRoadSlab", new Vector3((x0 + x1) * 0.5f, TownRoadY, 0f),
                x1 - x0, TownRoadW, 4f, m.road, true, 12f, WorldKit.RoadLayer);
            // Grass verges graded up to both sides, and to the two metres of
            // its west end either side of your street (which is 9 m to this
            // road's 11, and carries on from the middle of that edge).
            float street = HomeRoadW * 0.5f + WorldKit.KerbWidthM;
            WorldKit.FeatherRect(road.transform, "HomeRoadVerge", x0, x1,
                -TownRoadW * 0.5f, TownRoadW * 0.5f, TownRoadY,
                WorldKit.SlabEdge.MinZ | WorldKit.SlabEdge.MaxZ | WorldKit.SlabEdge.MinX,
                TownGroundAt, m.grass, 16f,
                new WorldKit.SlabCut(WorldKit.SlabEdge.MinX, -street, street));

            // ---- the boundary ----
            var b = new GameObject("HomeEndBounds");
            b.transform.SetParent(parent, false);
            void Wall(string name, Vector3 at, Vector3 size)
            {
                var go = new GameObject(name);
                go.transform.SetParent(b.transform, false);
                go.transform.position = at;
                go.layer = WorldKit.SolidLayer;
                go.AddComponent<BoxCollider>().size = size;
            }
            // Behind your house, where the street map's north wall stood.
            float xW = HomeEndToWorld(new Vector3(HomeStreetX, 0f, TownHouseZ + 26f)).x;
            float len = x1 - xW, mid = (x1 + xW) * 0.5f;
            // Eighty metres tall about y 10: the street climbs eleven metres to
            // the house and its land rolls several more either side.
            Wall("HomeEndN", new Vector3(mid, 10f, NbHalfWidth), new Vector3(len, 80f, 1f));
            Wall("HomeEndS", new Vector3(mid, 10f, -NbHalfWidth), new Vector3(len, 80f, 1f));
            Wall("HomeEndW", new Vector3(xW, 10f, 0f), new Vector3(1f, 80f, NbHalfWidth * 2f));
            // What is left of the town's own west wall: the step between its
            // north wall (z TownBoundsN) and the home end's (z NbHalfWidth).
            // The south walls already line up (TownBoundsS is -NbHalfWidth).
            Wall("W", new Vector3(x1, 3f, (NbHalfWidth + TownBoundsN) * 0.5f),
                 new Vector3(1f, 6f, TownBoundsN - NbHalfWidth));

            var list = new List<Transform>();
            for (int i = 0; i < 2; i++)
            {
                var go = new GameObject("RespawnHomeRoad" + i);
                go.transform.SetParent(road.transform, false);
                go.transform.SetPositionAndRotation(
                    new Vector3(Mathf.Lerp(x0, x1, 0.3f + 0.4f * i), TownRoadY + TownRespawnLift, -TownRoadW * 0.25f),
                    Quaternion.LookRotation(Vector3.right, Vector3.up));
                list.Add(go.transform);
            }
            return list.ToArray();
        }

        /// <summary>
        /// The roads, as plan polylines for the HUD's map: the main street and
        /// the road in as one line, your street, and its turning head. The town
        /// has no road graph for the map to read, so it is handed these
        /// (TownWorld.roadPoints, HudMinimap.SetLocalRoads).
        /// </summary>
        static void HomeEndRoads(out Vector2[] points, out int[] runs)
        {
            var p = new List<Vector2>();
            var r = new List<int>();
            void Run(IList<Vector3> w)
            {
                foreach (var v in w) p.Add(new Vector2(v.x, v.z));
                r.Add(w.Count);
            }
            Run(new[] { new Vector3(TownRoadHalf, 0f, 0f), new Vector3(HomeEndRoadWestX, 0f, 0f) });
            Run(new[] { HomeEndToWorld(new Vector3(HomeStreetX, 0f, NbStreetEnd)),
                        HomeEndToWorld(new Vector3(HomeStreetX, 0f, NbBulbThroatZ)) });
            var ring = new List<Vector3>();
            for (int k = 0; k <= 16; k++)
            {
                float a = k / 16f * Mathf.PI * 2f;
                ring.Add(HomeEndToWorld(new Vector3(HomeStreetX + NbBulbR * Mathf.Sin(a), 0f,
                                                    NbBulbCz + NbBulbR * Mathf.Cos(a))));
            }
            Run(ring);
            points = p.ToArray();
            runs = r.ToArray();
        }

        /// <summary>
        /// The home end's systems, on the town's GameSystems: the walk-in rooms
        /// (GarageWorld, embedded, beside the real car), the arrival that stands
        /// a walker on the drive, and the drop doors and map roads on the town's
        /// own TownWorld. Exactly what BuildNeighborhoodScene wires, less the
        /// junction line — the road carries on into town now.
        /// </summary>
        static void WireHomeEnd(HomeEndParts parts, GameObject systems, TownWorld world,
                                ForecourtMode forecourt, CarController player)
        {
            world.homeDoor = null;
            world.dropDoors = parts.doors.ToArray();
            world.dropMarkers = parts.markers.ToArray();
            world.dropNames = parts.names.ToArray();
            HomeEndRoads(out world.roadPoints, out world.roadRuns);
            forecourt.footWallet = true;

            var rooms = parts.rooms;
            if (rooms == null)
            {
                Log("[Town] WARN: no house at the home end - no walk-in rooms");
                return;
            }
            var home = systems.AddComponent<GarageWorld>();
            home.embedded = true;
            home.liveCar = player;
            home.bays = rooms.bays;
            home.partsRack = rooms.rack;
            home.toolBoard = rooms.board;
            home.workbench = rooms.bench;
            home.exitDoor = rooms.door;
            home.fridge = rooms.fridge;
            home.beds = rooms.beds;
            home.crateAnchor = rooms.crateAnchor;
            home.toolAnchor = rooms.toolAnchor;
            home.crateMaterial = rooms.crateMat;
            home.toolMaterial = rooms.toolMat;
            home.rigMaterial = rooms.rigMat;

            var arrival = systems.AddComponent<HomeArrival>();
            arrival.car = player;
            arrival.garageBay = rooms.bays[0];
            arrival.walkerStart = parts.walkStart;
            arrival.forecourt = forecourt;
            Log("[Town] home end rooms: " + rooms.bays.Length + " bays, " + rooms.beds.Length + " beds");
        }
    }
}
