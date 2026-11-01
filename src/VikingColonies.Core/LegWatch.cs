using System;

namespace VikingColonies.Core
{
    /// <summary>
    /// A straight leg to a hut's door or round its corner (1.5.1, <see cref="HutWay"/>): walked straight while it takes him
    /// on, given up for the game's own way once he stands against something the look along it missed (a post, a low eave).
    /// </summary>
    public sealed class LegWatch
    {
        /// <summary>Standing this long on a straight leg, he is against something.</summary>
        public const double StuckSeconds = 1.0;

        /// <summary>Getting this far from where the clock started counts as getting on.</summary>
        public const float Progress = 0.3f;

        /// <summary>No look for this long: his walk stopped meanwhile (waiting his turn), and the clock starts again.</summary>
        private const double PauseSeconds = 2.0;

        private float _x, _z;
        private double _since = double.NegativeInfinity;
        private double _lastLook = double.NegativeInfinity;

        /// <summary>A new walk near a hut (each door approach, each going round a hut just left): its clock starts at the next look.</summary>
        public void Start() => _since = double.NegativeInfinity;

        /// <summary>He is on a straight leg, standing at (x, z): whether he has been against something too long.</summary>
        public bool Stuck(float x, float z, double now)
        {
            bool fresh = double.IsNegativeInfinity(_since) || now - _lastLook > PauseSeconds;
            _lastLook = now;
            float dx = x - _x, dz = z - _z;
            if (fresh || dx * dx + dz * dz > Progress * Progress)
            {
                _x = x;
                _z = z;
                _since = now;
                return false;
            }
            return now - _since > StuckSeconds;
        }
    }
}
