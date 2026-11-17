using UnityEngine;
using PSXRacing.City;

namespace PSXRacing
{
    /// <summary>
    /// On every Charlotte scene (PSXRacingBuilder.BuildCityScene adds it to the CityWorld object), before
    /// anything else wakes: hands the races of Resources/CityTracks/*.json to the city map
    /// (<see cref="CityTracks.Register"/>), so the free-roam start gates and the race find them; and, in
    /// a race scene raced as one of those venues, points the scene at it. A venue from those files has no
    /// scene of its own: it races in its host's (TrackDef.hostScene, a loop in UptownLoop's, a course
    /// with ends in TryonSprint's), the way the deck runs race in TryonSprint's. CityMode reads its
    /// routeId in its own Awake (it builds the path and the grid there), so this runs first and swaps
    /// the route, the names and the TrackPath's settings for the venue's.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    public class CityTracksRegistrar : MonoBehaviour
    {
        /// <summary>The route a sanctioned event closes this race (set in Awake, built in Start).</summary>
        string closeRoute;
        /// <summary>The hosted course whose rivals' lines are held to its narrowest road (Start).</summary>
        string fitRoute;

        void Awake()
        {
            CityRaceClosures.ClearLast();   // nothing from an earlier race stands in this one
            var map = CityMap.Get();
            if (map == null) return;
            CityTracks.Register(map);

            var mode = GetComponent<CityMode>();
            if (mode == null) mode = Object.FindAnyObjectByType<CityMode>(FindObjectsInactive.Include);
            if (mode == null || string.IsNullOrEmpty(mode.routeId)) return;   // free roam: the routes, and no closures
            var venue = TrackCatalog.At(RaceHandoff.TrackIndex);
            if (venue == null || !venue.IsCityRace || venue.IsDeckRun) return;  // a deck run builds its own lap
            if (venue.cityRoute != mode.routeId && CityTracks.Has(venue.cityRoute) && map.RouteById(venue.cityRoute) != null)
            {
                Debug.Log("[CityTracks] " + venue.id + " races in this scene on route " + venue.cityRoute + " (was " + mode.routeId + ")");
                mode.routeId = venue.cityRoute;
                mode.venueName = venue.name;
                mode.routeLabel = venue.dragLabel ?? "";
                var path = Object.FindAnyObjectByType<TrackPath>(FindObjectsInactive.Include);
                if (path != null)
                {
                    path.roadWidth = venue.roadWidth;
                    path.pointToPoint = !venue.loop;
                    path.finishIndex = venue.FinishIndex;
                    path.dragLabel = venue.dragLabel ?? "";
                }
                // A hosted LOOP races its own laps: the host scene's RaceManager was built with the host's
                // (UptownLoop: 1), so a 3-lap track raced one lap while its menu, blurb and fuel gate said 3.
                // PSX_CITY_TRACK_LAPS=0: the host's laps, as before.
                if (venue.loop && System.Environment.GetEnvironmentVariable("PSX_CITY_TRACK_LAPS") != "0")
                {
                    var rm = Object.FindAnyObjectByType<RaceManager>(FindObjectsInactive.Include);
                    if (rm != null) rm.totalLaps = Mathf.Max(1, venue.laps);
                }
                // PSX_CITY_TRACK_SPREAD=0: every rival keeps its built line, as before (FitSpread)
                if (System.Environment.GetEnvironmentVariable("PSX_CITY_TRACK_SPREAD") != "0") fitRoute = venue.cityRoute;
            }
            // A SANCTIONED event's course is closed like a parade route (owner, 15:10): every road that
            // meets it shut clear of the race line. Not in a street race, not in free roam.
            if (CityRaceClosures.ForThisRace()) closeRoute = mode.routeId;
        }

        /// <summary>The closures, once CityMode.Awake has built the race line: every row keeps clear
        /// of the lanes the race actually drives (the TrackPath), not only of the map's pavement.</summary>
        void Start()
        {
            var map = CityMap.Get();
            if (map == null) return;
            if (!string.IsNullOrEmpty(fitRoute)) FitSpread(map, map.RouteById(fitRoute));
            if (string.IsNullOrEmpty(closeRoute)) return;
            var path = Object.FindAnyObjectByType<TrackPath>(FindObjectsInactive.Include);
            CityRaceClosures.Build(map, map.RouteById(closeRoute), transform, path);
        }

        /// <summary>
        /// THE FIELD'S SPREAD ON A ONE-LANE ROAD. On a one-way venue each rival drives its own line, a fixed
        /// offset off the race line (PSXRacingBuilder.CarSetups: up to 1.6 m). A hosted course can use a road
        /// narrower than that: H3's Remount Road bridge over I-77 is a 4.3 m one-lane carriageway, and its
        /// Skyline, 1.6 m left of the line, put its wheels into the bridge barrier at 100 km/h. Every offset
        /// is held to what the course's narrowest road (15 m or longer) holds. A course whose roads all hold
        /// the spread (I485West's 6.1 m ramps, every street circuit) is unchanged.
        /// </summary>
        static void FitSpread(CityMap map, CityMap.Route route)
        {
            if (route == null || route.edges == null || map.edges == null) return;
            float narrow = float.MaxValue;
            string where = "";
            foreach (int ei in route.edges)
            {
                if (ei < 0 || ei >= map.edges.Length) continue;
                var e = map.edges[ei];
                if (e.length >= 15f && e.width > 0f && e.width < narrow) { narrow = e.width; where = e.name ?? "a ramp"; }
            }
            if (narrow == float.MaxValue) return;
            float max = Mathf.Max(0f, narrow * 0.5f - SpreadCarHalfM - SpreadAirM);
            int held = 0;
            foreach (var ai in Object.FindObjectsByType<AIDriver>(FindObjectsInactive.Include))
            {
                float was = ai.lateralOffset;
                ai.lateralOffset = Mathf.Clamp(was, -max, max);
                if (ai.lateralOffset != was && ai.gameObject.activeInHierarchy) held++;
            }
            if (held > 0)
                Debug.Log("[CityTracks] " + route.id + ": " + held + " rivals' lines held to +-" + max.ToString("0.0") +
                          " m (the narrowest road " + narrow.ToString("0.0") + " m: " + where + ")");
        }
        const float SpreadCarHalfM = 1.0f, SpreadAirM = 0.15f;
    }
}
