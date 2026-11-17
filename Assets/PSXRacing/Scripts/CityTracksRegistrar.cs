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
        void Awake()
        {
            var map = CityMap.Get();
            if (map == null) return;
            CityTracks.Register(map);

            var mode = GetComponent<CityMode>();
            if (mode == null) mode = Object.FindAnyObjectByType<CityMode>(FindObjectsInactive.Include);
            if (mode == null || string.IsNullOrEmpty(mode.routeId)) return;   // free roam: the routes only
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
                // THE VENUE'S LAPS, not the host's: the scene was built with its own venue's lap count
                // (UptownLoop's 1), so a hosted 2-lap circuit raced one lap under a "2 LAPS" menu line.
                // PSX_CITY_TRACK_LAPS=0: the host scene's count, as before.
                if (venue.loop && System.Environment.GetEnvironmentVariable("PSX_CITY_TRACK_LAPS") != "0")
                {
                    var rm = Object.FindAnyObjectByType<RaceManager>(FindObjectsInactive.Include);
                    if (rm != null) rm.totalLaps = Mathf.Max(1, venue.laps);
                }
            }
            // NO CLOSURES (owner 2026-11-16 ~21:00: "I'm not a fan of the road construction barriers for
            // races. Drivers can just go around them."): no race and no free roam builds a barricade. A
            // city venue with a checkpoint file races as a checkpoint race (Checkpoints/CheckpointRace).
        }
    }
}
