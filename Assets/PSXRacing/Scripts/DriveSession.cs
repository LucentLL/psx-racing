using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// The one place that knows whether "driving" is being managed by a
    /// RaceManager (circuits, strips) or a CityMode (Charlotte free roam).
    ///
    /// Everything that used to reach for RaceManager.Instance to respawn a
    /// car or ask "is the session live" goes through here instead, so the
    /// city did not have to grow a fake RaceManager and the circuits did not
    /// have to learn about the city.
    /// </summary>
    public static class DriveSession
    {
        /// <summary>Car responds to input and watchdogs may act.</summary>
        public static bool Live
        {
            get
            {
                var rm = RaceManager.Instance;
                if (rm != null) return rm.State == RaceManager.RaceState.Racing;
                var city = City.CityMode.Instance;
                return city != null && city.Live;
            }
        }

        /// <summary>Put a car back on the road, whichever world this is.</summary>
        public static void Respawn(CarController car)
        {
            if (car == null) return;
            var city = City.CityMode.Instance;
            if (city != null) { city.Respawn(car); return; }
            RaceManager.Instance?.RespawnCar(car);
        }

        // ==================================================================
        //  Where a recovered car actually goes
        // ==================================================================

        /// <summary>
        /// How high above the target the surface probe starts. Tall enough to
        /// clear a bridge parapet and a kerb, short enough that it cannot find
        /// the deck of a road crossing overhead.
        /// </summary>
        const float ProbeUp = 3.0f;

        /// <summary>
        /// Seat a car on the road at <paramref name="centre"/>, facing
        /// <paramref name="rot"/>, IF there is room for it there.
        ///
        /// Returns false when something solid is already standing in that spot,
        /// which is the case the caller has to handle by trying somewhere else.
        /// The old recovery had no such test: it dropped the car on the nearest
        /// waypoint and, if the reason the car was stuck was a barrier or a
        /// pier sitting on that part of the line, dropped it straight back into
        /// the thing it was stuck on. That is what "respawns in the same place
        /// to remain stuck" was.
        ///
        /// The height comes from a downward probe rather than from the
        /// waypoint, because the waypoint is the road DATUM: the tarmac ribbon
        /// rides 12 cm over it, a bridge deck is its own surface again, and a
        /// car placed by the datum on a deck starts life inside the deck.
        /// </summary>
        public static bool TryPlace(CarController car, Vector3 centre, Quaternion rot)
        {
            var box = car.GetComponent<BoxCollider>();
            Vector3 half = box != null ? box.size * 0.5f : new Vector3(0.86f, 0.5f, 2.05f);
            Vector3 offset = box != null ? box.center : new Vector3(0f, 0.72f, 0.05f);

            // Surface first, so the clearance test is run where the car will
            // actually be rather than where the centreline says it is.
            //
            // DefaultRaycastLayers, not everything: the cars are on layer 2
            // (Ignore Raycast), and probing onto the ROOF of the car already
            // parked at this station would seat the recovery a metre and a half
            // in the air. Whether that station is occupied is the next test's
            // job, and it answers correctly either way.
            float y = centre.y;
            if (Physics.Raycast(centre + Vector3.up * ProbeUp, Vector3.down,
                                out var hit, ProbeUp * 2f, Physics.DefaultRaycastLayers,
                                QueryTriggerInteraction.Ignore))
                y = hit.point.y;
            Vector3 origin = new Vector3(centre.x, y + CarController.ResetLift, centre.z);

            // Shrunk slightly: the box is meant to detect a WALL in the way, not
            // to argue with the tarmac it is about to stand on or with a kerb
            // clipping one corner.
            var hits = Physics.OverlapBox(origin + rot * offset, half * 0.82f, rot,
                                          ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                if (h == null) continue;
                // Its own body, and its own wheels, are not obstacles.
                if (h.transform == car.transform || h.transform.IsChildOf(car.transform)) continue;

                // A concave mesh collider is a SURFACE, not an obstacle: the
                // road, the ground, a bridge deck and the forecourt are all
                // one, and all four are things you are meant to be standing on.
                // The obstacle audit draws exactly this line for exactly this
                // reason. Everything that can actually block a car here —
                // barriers, buildings, piers, props, other cars — is a box, a
                // capsule or a convex hull.
                var mc = h as MeshCollider;
                if (mc != null && !mc.convex) continue;

                // And of what remains, anything that does not reach up past the
                // car's floor is something it drives over rather than into.
                //
                // This test is SECOND on purpose. Collider.bounds is a world
                // AABB, so asking it about the ground mesh returns a box that
                // reaches the highest hill on the circuit — the ground would
                // read as an obstacle at every station, every candidate would
                // be rejected, and the search would fall through to the old
                // behaviour without ever saying so.
                if (h.bounds.max.y < y + 0.35f) continue;
                return false;
            }

            car.ResetTo(new Vector3(centre.x, y, centre.z), rot);
            return true;
        }
    }
}
