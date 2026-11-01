using System;

namespace VikingColonies.Core
{
    /// <summary>One walk of a settler as the walk watch saw it, from setting off to arriving (or turning to another goal).</summary>
    public struct WalkEnd
    {
        public Vec3 From, Goal, At;
        public float Walked, Straight, LongestStop, Seconds;
        /// <summary>It ended by the settler's own arrival, not by turning to another goal or stopping short.</summary>
        public bool Reached;
        public bool StoodStill;

        /// <summary>
        /// A long way round: arrived, the fair way at least <see cref="WalkWatch.MinStraight"/>, and walked over it by more
        /// than a walk should. Through a hut's door the fair way is known well (<see cref="WalkCheck.FairWay"/>): the walking
        /// check's quarter and 3 m. Out in the open it is the straight line, which trees and rocks lengthen: half again and 8 m.
        /// </summary>
        public bool LongWay(float fairWay, bool throughDoor) =>
            Reached && fairWay >= WalkWatch.MinStraight
            && Walked > (throughDoor ? fairWay * WalkCheck.DetourFactor + WalkCheck.DetourSlack : fairWay * WalkWatch.LongWayFactor + WalkWatch.LongWaySlack);
    }

    /// <summary>
    /// vc_walkwatch (1.5.1, the player: "I just want it to feel smooth everything"): every walk of a living colony, judged as
    /// a watching player's eye would: a long way round (<see cref="WalkEnd.LongWay"/>), or standing still over
    /// <see cref="StillAllowed"/> on the way, away from a door. Fed a settler's place every moment and their own arrivals; a
    /// walk ends when they arrive, stop walking or set off somewhere else. A gap in the watching or a jump (a rescue, another
    /// computer running them a while) drops the walk unjudged (its review).
    /// </summary>
    public sealed class WalkWatch
    {
        /// <summary>A walk shorter than this, fairly, isn't judged for its way (round a table to a seat).</summary>
        public const float MinStraight = 5f;
        public const float LongWayFactor = 1.5f;
        public const float LongWaySlack = 8f;
        /// <summary>Standing still longer than this on the way, away from a door, is noticed.</summary>
        public const float StillAllowed = 2f;
        /// <summary>A goal this far from the last is another walk.</summary>
        public const float NewGoal = 1f;
        /// <summary>Unwatched longer than this, the walk isn't known: dropped.</summary>
        public const float MaxGap = 1f;
        /// <summary>A step longer than this between two looks is a jump, not walking: dropped.</summary>
        public const float MaxStep = 3f;

        private bool _open;
        private Vec3 _from, _goal, _last;
        private float _started, _lastTime;
        private WalkTrace _trace;

        /// <param name="arrivedAt">The goal of an arrival of theirs since the last look (null: none). The look may miss the
        /// moment between arriving and the next errand (its review: the lumberjack drops their load and is off at once).</param>
        public WalkEnd? Sample(float time, Vec3 at, bool walking, Vec3 goal, bool nearDoor, Vec3? arrivedAt = null)
        {
            if (_open && (time - _lastTime > MaxGap || Flat(at, _last) > MaxStep)) _open = false;   // a gap or a jump
            WalkEnd? ended = null;
            if (_open && arrivedAt.HasValue && Flat(arrivedAt.Value, _goal) <= NewGoal)
            {
                _trace.Add(time, at, nearDoor);
                ended = End(at, reached: true);
                _open = false;
            }
            else if (_open && (!walking || Flat(goal, _goal) > NewGoal))
            {
                ended = End(_last, reached: false);
                _open = false;
            }
            if (!walking) return ended;
            if (!_open)
            {
                _open = true;
                _from = at;
                _goal = goal;
                _started = time;
                _trace = new WalkTrace();
            }
            _trace.Add(time, at, nearDoor);
            _last = at;
            _lastTime = time;
            return ended;
        }

        private WalkEnd End(Vec3 at, bool reached) => new WalkEnd
        {
            From = _from,
            Goal = _goal,
            At = at,
            Walked = _trace.Walked,
            Straight = Flat(_from, _goal),
            LongestStop = _trace.LongestStop,
            Seconds = _lastTime - _started,
            Reached = reached,
            StoodStill = _trace.LongestStop > StillAllowed,
        };

        internal static float Flat(Vec3 a, Vec3 b) => (float)Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));
    }
}
