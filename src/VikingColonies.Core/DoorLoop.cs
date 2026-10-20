namespace VikingColonies.Core
{
    /// <summary>
    /// A settler going through the same door towards the same goal again and again without getting any closer: the door is
    /// no way there (the real builders' run, 2026-10-18: a builder stuck on a roof went through the door below 92 times on
    /// his way to another hut; each pass reset his job's stuck clock, so the job never gave up and he kept its order).
    /// Only passes that follow one another count: a pass towards another goal, a long gap or arriving anywhere starts a new
    /// trip (the review of 2026-10-19: three ordinary trips to one chest within 5 minutes had counted as a loop).
    /// </summary>
    public sealed class DoorLoop
    {
        /// <summary>This many passes in a row for one goal, none after the first that brought him nearer, and the door is no help.</summary>
        public const int Passes = 3;
        /// <summary>A pass counts as getting nearer when it ends this much closer to the goal than any pass before.</summary>
        public const float Nearer = 1f;
        /// <summary>Passes further apart than this (seconds) are separate trips.</summary>
        public const double LoopGap = 30.0;
        /// <summary>How long (seconds) a door that is no help isn't taken for that goal.</summary>
        public const double NoWayFor = 300.0;

        private string _key;
        private int _passes;
        private float _best;
        private double _last;

        /// <summary>
        /// He went through a door (<paramref name="key"/>: the door and the goal) and is now <paramref name="distance"/>
        /// from his goal. True when the door brings him no closer: he should stop taking it for this goal.
        /// </summary>
        public bool Passed(string key, float distance, double now)
        {
            if (key != _key || now - _last > LoopGap)
            {
                _key = key;
                _passes = 0;
                _best = float.MaxValue;
            }
            if (distance < _best - Nearer)
            {
                _best = distance;
                _passes = 1;
            }
            else
            {
                _passes++;
            }
            _last = now;
            if (_passes < Passes) return false;
            _key = null;
            return true;
        }

        /// <summary>He got where he was going: the passes so far were a trip, not a loop.</summary>
        public void Arrived() => _key = null;
    }
}
