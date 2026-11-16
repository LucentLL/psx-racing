using PSXRacing.LifeSim;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// THE SIDE-ROAD CLOSURES OF A LEGAL EVENT (owner, 2026-11-16: "I never
    /// asked for barricades or road construction. They can be repurposed for
    /// 'legal' race events, like a parade."). A stage's barricades across its
    /// side-road mouths stand only while a SANCTIONED event runs on it - the
    /// way a parade route is closed. Free roam, street races and every other
    /// drive leave the side roads open. The barricades are this object's first
    /// child, saved inactive; nothing of them is ever on the race line.
    /// </summary>
    public class EventClosures : MonoBehaviour
    {
        void Awake()
        {
            if (transform.childCount == 0) return;
            transform.GetChild(0).gameObject.SetActive(SanctionedEventRunning());
        }

        /// <summary>A life-sim event race whose event is a sanctioned (closed-road) one.</summary>
        public static bool SanctionedEventRunning()
        {
            if (!RaceHandoff.FromLifeSim || RaceHandoff.FreeRoam || string.IsNullOrEmpty(RaceHandoff.EventKey)) return false;
            string id = RaceHandoff.EventKey;
            int hash = id.IndexOf('#');
            if (hash >= 0) id = id.Substring(0, hash);
            var ev = Events.ById(id);
            return ev != null && !ev.street;
        }
    }
}
