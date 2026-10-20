using System;

namespace VikingColonies.Core
{
    public enum UnstuckMove { None, Hop, Sidestep }

    /// <summary>
    /// A settler whose walk doesn't move him: his way is open on the game's paths, but something the paths don't know
    /// stops him (a log, a lip, another settler). After a few seconds he hops, as a player would; a little later he steps
    /// to the side, the other side each time. After two rounds he stops trying (something blocks him for good: his job's
    /// own patience decides). Moving again, or a pause in walking, starts it all over.
    /// </summary>
    public sealed class Unstuck
    {
        /// <summary>Getting this far from where the clock started counts as moving.</summary>
        public const float MoveDistance = 0.5f;
        public const double HopAfter = 3;
        public const double SidestepAfter = 5;
        /// <summary>How long a step to the side lasts.</summary>
        public const float SidestepSeconds = 0.5f;
        /// <summary>This near his goal he isn't stuck, just among others there (a crowd at the Town Hall, at a door).</summary>
        public const float NearGoal = 3f;
        /// <summary>Rounds of a hop and a step aside before he stops trying.</summary>
        public const int MaxRounds = 2;
        /// <summary>No check for this long means he stopped walking for a while (arrived, idle, working).</summary>
        private const double PauseSeconds = 1;

        private float _x, _z;
        private double _since = double.NegativeInfinity;
        private double _lastCheck = double.NegativeInfinity;
        private bool _hopped;
        private int _nextSide = 1;
        private int _stepSide = 1;
        private bool _stepping;
        private int _rounds;

        /// <summary>The side of the step aside just asked for, or of the next one: 1 is his right, -1 his left.</summary>
        public int Side => _stepping ? _stepSide : _nextSide;

        /// <summary>He is walking and stands at (x, z): what to do now to get going again.</summary>
        /// <param name="toGoal">How far his goal is (near it, nothing is done).</param>
        public UnstuckMove Check(float x, float z, double now, float toGoal = float.MaxValue)
        {
            bool paused = now - _lastCheck > PauseSeconds || toGoal < NearGoal;   // near the goal the clock doesn't run
            _lastCheck = now;
            _stepping = false;
            float dx = x - _x, dz = z - _z;
            if (paused || Math.Sqrt(dx * dx + dz * dz) > MoveDistance)
            {
                _x = x;
                _z = z;
                _since = now;
                _hopped = false;
                _rounds = 0;
                return UnstuckMove.None;
            }
            if (_rounds >= MaxRounds) return UnstuckMove.None;
            double still = now - _since;
            if (!_hopped && still >= HopAfter)
            {
                _hopped = true;
                return UnstuckMove.Hop;
            }
            if (still >= SidestepAfter)
            {
                _stepping = true;
                _rounds++;
                _stepSide = _nextSide;
                _nextSide = -_nextSide;
                _since = now;
                _hopped = false;
                return UnstuckMove.Sidestep;
            }
            return UnstuckMove.None;
        }
    }
}
