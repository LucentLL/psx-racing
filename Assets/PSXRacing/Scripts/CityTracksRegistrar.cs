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
            }
            // A SANCTIONED event's course is closed like a parade route (owner, 15:10): every road that
            // meets it shut clear of the race line. Not in a street race, not in free roam.
            if (CityRaceClosures.ForThisRace()) closeRoute = mode.routeId;
        }

        /// <summary>The closures, once CityMode.Awake has built the race line: every row keeps clear
        /// of the lanes the race actually drives (the TrackPath), not only of the map's pavement.</summary>
        void Start()
        {
            if (string.IsNullOrEmpty(closeRoute)) return;
            var map = CityMap.Get();
            if (map == null) return;
            var path = Object.FindAnyObjectByType<TrackPath>(FindObjectsInactive.Include);
            CityRaceClosures.Build(map, map.RouteById(closeRoute), transform, path);
        }
    }
}
