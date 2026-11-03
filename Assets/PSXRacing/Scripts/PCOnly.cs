using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// Shown only where a desktop GPU draws it (<see cref="PlatformProfile.FullForest"/>:
    /// the PC profile, and since 2026-11-03 a browser on a desktop): the
    /// mountain stages' far forest, fill and crown cards, which a phone cannot
    /// yet afford. Switched off as the scene loads everywhere else; the
    /// editor's scene view still shows it. (The name stays: every stage scene
    /// serializes the component.)
    /// </summary>
    public class PCOnly : MonoBehaviour
    {
        void Awake()
        {
            if (!PlatformProfile.FullForest) gameObject.SetActive(false);
        }
    }
}
