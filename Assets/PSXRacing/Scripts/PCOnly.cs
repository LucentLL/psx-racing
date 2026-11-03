using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// Shown only in the PC profile (<see cref="PlatformProfile"/>): the
    /// mountain stages' far forest, which a desktop GPU draws and the web
    /// build and phones cannot yet afford. Switched off as the scene loads
    /// everywhere else; the editor's scene view still shows it.
    /// </summary>
    public class PCOnly : MonoBehaviour
    {
        void Awake()
        {
            if (!PlatformProfile.PC) gameObject.SetActive(false);
        }
    }
}
