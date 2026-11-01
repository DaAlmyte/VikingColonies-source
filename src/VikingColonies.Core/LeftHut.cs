using System;

namespace VikingColonies.Core
{
    /// <summary>
    /// Out of a hut by its door to a goal behind it (1.5.1's walk check: the game's own way round a Builder's Hut's corner
    /// went 3 m wide): round its walls close by (<see cref="HutWay"/>), then the game's own way once the goal is in sight.
    /// </summary>
    public sealed class LeftHut
    {
        /// <summary>Going round the hut just left lasts this long at most (he walks on elsewhere by then).</summary>
        public const double ForSeconds = 20;

        /// <summary>A goal this far from the one he left for is another walk.</summary>
        public const float SameGoal = 1.5f;

        /// <summary>This far out from the hut's walls he is away from it (moved by a rescue): it is forgotten.</summary>
        public const float AwayFrom = 10f;

        private Footprint _walls;
        private Vec3 _goal;
        private double _at;

        /// <summary>He went through the door of the hut with these walls on the way to the goal, and stands here now.</summary>
        public void Left(Footprint walls, Vec3 me, Vec3 goal, double now)
        {
            bool outward = walls != null && !walls.Contains(me) && !walls.Contains(goal);
            _walls = outward ? walls : null;
            _goal = goal;
            _at = now;
        }

        /// <summary>Where he heads next for the goal: a corner round the hut just left, or the goal (and the hut is forgotten).</summary>
        public Vec3 Next(Vec3 me, Vec3 goal, double now)
        {
            if (_walls == null) return goal;
            float dx = goal.X - _goal.X, dz = goal.Z - _goal.Z;
            float ox = me.X - _walls.Centre.X, oz = me.Z - _walls.Centre.Z, away = Math.Max(_walls.HalfX, _walls.HalfZ) + AwayFrom;
            if (now - _at > ForSeconds || dx * dx + dz * dz > SameGoal * SameGoal || ox * ox + oz * oz > away * away)
            {
                _walls = null;
                return goal;
            }
            Vec3 next = HutWay.Next(me, goal, _walls);
            if (Math.Abs(next.X - goal.X) < 0.01f && Math.Abs(next.Z - goal.Z) < 0.01f) _walls = null;   // round it: as the game's way goes
            return next;
        }

        /// <summary>A corner he couldn't get to: the game's own way from here.</summary>
        public void Forget() => _walls = null;
    }
}
