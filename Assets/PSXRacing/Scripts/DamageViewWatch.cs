using UnityEngine;
using PSXRacing.LifeSim;

namespace PSXRacing
{
    /// <summary>
    /// DamageView's watch on one shown model: every half second (InvokeRepeating,
    /// never Update) the shown car's rows signature; a change - a repair at the
    /// mechanic or in the garage, a bench switch, a wash - dresses it again at once.
    /// </summary>
    public sealed class DamageViewWatch : MonoBehaviour
    {
        public const float Every = 0.5f;
        Renderer body;
        Vector3 centre, size;
        OwnedCar car;
        int sig;
        bool running;

        public OwnedCar Car => car;

        public void Set(Renderer b, Vector3 c, Vector3 s, OwnedCar oc)
        {
            body = b; centre = c; size = s; car = oc;
            sig = DamageView.Signature(oc);
            if (!running && Application.isPlaying && isActiveAndEnabled)
            {
                InvokeRepeating(nameof(CheckNow), Every, Every);
                running = true;
            }
        }

        public void Stop()
        {
            car = null;
            if (running) { CancelInvoke(nameof(CheckNow)); running = false; }
        }

        /// <summary>True when the rows had changed and the model was dressed again.</summary>
        public bool CheckNow()
        {
            if (car == null || body == null) return false;
            if (DamageView.Signature(car) == sig) return false;
            DamageView.Dress(transform, body, centre, size, car);
            return true;
        }

        void OnEnable()
        {
            if (car != null && !running && Application.isPlaying)
            {
                CheckNow();
                InvokeRepeating(nameof(CheckNow), Every, Every);
                running = true;
            }
        }

        void OnDisable()
        {
            if (running) { CancelInvoke(nameof(CheckNow)); running = false; }
        }
    }
}
