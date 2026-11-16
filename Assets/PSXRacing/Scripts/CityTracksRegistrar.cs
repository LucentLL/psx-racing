using UnityEngine;
using PSXRacing.City;

namespace PSXRacing
{
    /// <summary>
    /// On every Charlotte scene (PSXRacingBuilder.BuildCityScene adds it to the CityWorld object), before
    /// anything else wakes: hands Resources/charlotte_tracks.json's races to the city map
    /// (<see cref="CityTracks.Register"/>), so the free-roam start gates and the race find them; and, in
    /// a race scene raced as one of those venues, points the scene at it. A venue from that file has no
    /// scene of its own: it races in its host's (TrackDef.hostScene, a loop in UptownLoop's, a course
    /// with ends in TryonSprint's), the way the deck runs race in TryonSprint's. CityMode reads its
    /// routeId in its own Awake (it builds the path and the grid there), so this runs first and swaps
    /// the route, the names and the TrackPath's settings for the venue's.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    public class CityTracksRegistrar : MonoBehaviour
    {
        void Awake()
        {
            var map = CityMap.Get();
            if (map == null) return;
            CityTracks.Register(map);

            var mode = GetComponent<CityMode>();
            if (mode == null) mode = Object.FindFirstObjectByType<CityMode>(FindObjectsInactive.Include);
            if (mode == null || string.IsNullOrEmpty(mode.routeId)) return;   // free roam: the routes are enough
            var venue = TrackCatalog.At(RaceHandoff.TrackIndex);
            if (venue == null || !venue.IsCityRace || venue.IsDeckRun || venue.cityRoute == mode.routeId) return;
            if (!CityTracks.Has(venue.cityRoute) || map.RouteById(venue.cityRoute) == null) return;
            Debug.Log("[CityTracks] " + venue.id + " races in this scene on route " + venue.cityRoute + " (was " + mode.routeId + ")");
            mode.routeId = venue.cityRoute;
            mode.venueName = venue.name;
            mode.routeLabel = venue.dragLabel ?? "";
            var path = Object.FindFirstObjectByType<TrackPath>(FindObjectsInactive.Include);
            if (path != null)
            {
                path.roadWidth = venue.roadWidth;
                path.pointToPoint = !venue.loop;
                path.finishIndex = venue.FinishIndex;
                path.dragLabel = venue.dragLabel ?? "";
            }
            // the closed course: every road that meets the route shut a few metres up from the junction
            CityRaceClosures.Build(map, map.RouteById(venue.cityRoute), transform);
        }
    }
}
