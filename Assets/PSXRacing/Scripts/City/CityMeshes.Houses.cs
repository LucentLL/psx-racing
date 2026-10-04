using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    // ======================================================================
    //  LEFTOVER ITEM 6 (2026-10-03): THE DRIVEWAYS, CUT INTO THE GROUND.
    //
    //  Every driveway that reaches the tile (CityHouses.DrivewayOf) is laid
    //  INTO the lattice the way the parking lots are: each cell triangle it
    //  covers is split along the driveway's four sides, the part inside drawn
    //  in the ground mesh's PAVEMENT concrete and the rest left as ground, so
    //  the driveway lies on its triangle's plane - flush with the graded lot,
    //  no lift, no second surface - and is in the ground collider like the
    //  grass. The ground mesh already has the pavement slot wherever a tile
    //  has a paved cell or a verge apron; a suburban tile that had neither
    //  gains that one submesh (+1 draw at most). The street's verge across a
    //  driveway's mouth and its flared wings is poured concrete (the curb
    //  cut: ApronAt), and no street lamp stands on a driveway (TryLamp).
    // ======================================================================
    public static partial class CityMeshes
    {
        static readonly List<CityHouses.Driveway> tileDrives = new List<CityHouses.Driveway>(64);
        static readonly List<CityHouses.Driveway> driveHit = new List<CityHouses.Driveway>(8);
        static readonly List<List<Vector3>> drvCur = new List<List<Vector3>>(16), drvIn = new List<List<Vector3>>(16), drvOut = new List<List<Vector3>>(16);

        public static class DriveStats
        {
            public static long pieces; public static float m2;
            public static void Reset() { pieces = 0; m2 = 0f; }
        }

        /// <summary>The driveways this tile's ground meets (BuildGround, once
        /// per tile; also what ApronAt and TryLamp read for the rest of the build).</summary>
        static void PrepareTileDrives(CityMap map, Vector2 min, Vector2 max)
        {
            tileDrives.Clear();
            if (!CityHouses.DrivewaysOn) return;
            CityHouses.DrivewaysNearBound(map, min - Vector2.one * 4f, max + Vector2.one * 4f, tileDrives);
        }

        /// <summary>The driveways (of this tile's) whose box meets a cell, into
        /// <see cref="driveHit"/>; false when none.</summary>
        static bool DrivesInCell(float cx0, float cz0, float cx1, float cz1)
        {
            driveHit.Clear();
            if (tileDrives.Count == 0) return false;
            foreach (var d in tileDrives)
                if (d.max.x > cx0 && d.min.x < cx1 && d.max.y > cz0 && d.min.y < cz1) driveHit.Add(d);
            return driveHit.Count > 0;
        }

        /// <summary>One lattice cell with the driveways cut into it: false
        /// when none touches it (the caller lays the plain quad).</summary>
        static bool DrivewayCell(TileMeshes tm, Vector3 p00, Vector3 p01, Vector3 p11, Vector3 p10, bool paved)
        {
            var o = tm.origin;
            if (!DrivesInCell(p00.x + o.x, p00.z + o.z, p11.x + o.x, p11.z + o.z)) return false;
            var gb = GroundBucket(paved);
            for (int tri = 0; tri < 2; tri++)
            {
                // anticlockwise in plan: (00, 10, 11) and (00, 11, 01), as Bucket.Up draws the quad
                var first = NewPoly();
                if (tri == 0) { first.Add(p00); first.Add(p10); first.Add(p11); }
                else { first.Add(p00); first.Add(p11); first.Add(p01); }
                drvCur.Clear(); drvCur.Add(first);
                EmitDrivePieces(drvCur, gb, paved, o);
            }
            return true;
        }

        /// <summary>Pieces of the ground (tile frame, anticlockwise, on their
        /// triangles' planes) split by every driveway in <see cref="driveHit"/>:
        /// the driveways' parts into the pavement concrete, the rest into
        /// <paramref name="gb"/>. The pieces are consumed.</summary>
        static void EmitDrivePieces(List<List<Vector3>> pieces, Bucket gb, bool paved, Vector3 o)
        {
            var pb = buckets[(int)Slot.Pavement];
            foreach (var d in driveHit)
            {
                if (pieces.Count == 0) break;
                drvIn.Clear(); drvOut.Clear();
                var q = d.quad;
                SplitConvexQuad(pieces, new Vector2(q[0].x - o.x, q[0].y - o.z), new Vector2(q[1].x - o.x, q[1].y - o.z),
                                        new Vector2(q[2].x - o.x, q[2].y - o.z), new Vector2(q[3].x - o.x, q[3].y - o.z), drvIn, drvOut);
                foreach (var piece in drvIn)
                {
                    EmitGroundPiece(pb, piece, true, o);
                    DriveStats.pieces++; DriveStats.m2 += Mathf.Abs(PolyArea(piece));
                    FreePoly(piece);
                }
                pieces.Clear(); pieces.AddRange(drvOut);
            }
            foreach (var piece in pieces) { EmitGroundPiece(gb, piece, paved, o); FreePoly(piece); }
            pieces.Clear();
        }

        static void EmitGroundPiece(Bucket bk, List<Vector3> piece, bool paved, Vector3 o)
        {
            int v0 = bk.v.Count;
            foreach (var p in piece) { bk.v.Add(p); bk.uv.Add(GroundUV(paved, p.x + o.x, p.z + o.z)); }
            for (int k = 1; k + 1 < piece.Count; k++) { bk.t.Add(v0); bk.t.Add(v0 + k + 1); bk.t.Add(v0 + k); }
        }

        /// <summary>Is a plan point on a driveway or its curb cut (this tile
        /// build's)? The verge there is poured concrete.</summary>
        static bool DriveApronAt(float x, float z) =>
            tileDrives.Count > 0 && CityHouses.OnAny(tileDrives, new Vector2(x, z), 0f, true);

        /// <summary>Is a plan point within <paramref name="pad"/> of a
        /// driveway or its curb cut (this tile build's)?</summary>
        static bool OnDriveway(Vector2 p, float pad) =>
            tileDrives.Count > 0 && CityHouses.OnAny(tileDrives, p, pad, true);
    }
}
