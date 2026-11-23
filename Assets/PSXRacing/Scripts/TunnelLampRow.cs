using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// (parkwaytunnel, 2026-11-23) The wall lamps of one stage tunnel, as the
    /// stage builder placed their fixtures (PSXRacingBuilder.Tunnel2.cs): one
    /// row along the left wall of the forward bore, about 2.7 m up, every
    /// 9 m - the owner's reference picture of a Parkway tunnel. Data only: the
    /// light (pools, glints on a wet road, halos) is made at runtime from these
    /// heads by <see cref="TunnelLamps"/>, at every hour.
    /// </summary>
    public class TunnelLampRow : MonoBehaviour
    {
        /// <summary>World positions of the lamps' light (a little in front of
        /// each fixture's lens).</summary>
        public Vector3[] heads = new Vector3[0];
    }
}
