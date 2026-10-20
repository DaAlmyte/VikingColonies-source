using System;

namespace VikingColonies.Core
{
    /// <summary>
    /// Walking straight to a door's spot inside its hut (<see cref="DoorRules.WalkStraight"/>), something in the way stops
    /// him: a lip he hops over, or the hut's chest in its middle, which a hop doesn't clear (observation run 4, 0.20). Every
    /// second he checks he moved; the first time he didn't he hops, the next he steps aside for a moment and goes on
    /// straight. Two steps aside to one side (the first away from the hut's middle), then two to the other, and so on.
    /// </summary>
    public sealed class StraightWalk
    {
        public const double CheckSeconds = 1;
        /// <summary>Getting this far in a second counts as moving.</summary>
        public const float Progress = 0.25f;
        /// <summary>How long a step aside lasts.</summary>
        public const double AsideSeconds = 0.8;

        private float _x, _z;
        private double _checkAt;
        private double _asideUntil = double.NegativeInfinity;
        private int _stops;
        private int _asides;
        private int _firstSide = 1;

        /// <summary>The side of the step aside under way: 1 is his right, -1 his left.</summary>
        public int Side { get; private set; } = 1;

        /// <param name="firstSide">The side of his first step aside (<see cref="DoorRules.AsideFromMiddle"/>).</param>
        public void Start(float x, float z, double now, int firstSide)
        {
            _x = x;
            _z = z;
            _checkAt = now;
            _asideUntil = double.NegativeInfinity;
            _stops = 0;
            _asides = 0;
            _firstSide = firstSide >= 0 ? 1 : -1;
            Side = _firstSide;
        }

        /// <summary>He stands at (x, z): None to go on straight, Hop, or Sidestep (to <see cref="Side"/>).</summary>
        public UnstuckMove Next(float x, float z, double now)
        {
            if (now < _asideUntil) return UnstuckMove.Sidestep;
            if (now - _checkAt < CheckSeconds) return UnstuckMove.None;
            float dx = x - _x, dz = z - _z;
            bool moved = Math.Sqrt(dx * dx + dz * dz) >= Progress;
            _x = x;
            _z = z;
            _checkAt = now;
            if (moved)
            {
                _stops = 0;
                return UnstuckMove.None;
            }
            _stops++;
            if (_stops % 2 == 1) return UnstuckMove.Hop;
            Side = (_asides / 2) % 2 == 0 ? _firstSide : -_firstSide;
            _asides++;
            _asideUntil = now + AsideSeconds;
            return UnstuckMove.Sidestep;
        }
    }
}
