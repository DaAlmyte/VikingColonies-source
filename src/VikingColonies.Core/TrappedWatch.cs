using System;
using System.Collections.Generic;

namespace VikingColonies.Core
{
    /// <summary>
    /// The last resort for a settler boxed in for good (seen: a builder between his hut's old walls and the new ones round
    /// them tried for an hour to get out): one who keeps trying to walk without getting out of a small patch of ground is
    /// moved to a free spot by the Town Hall. Hops and steps aside come first (<see cref="Unstuck"/>), and his job tries
    /// other goals meanwhile. Other colony mods do the same (MineColonies moves stuck citizens; Hearthfolk's residents are
    /// "no longer permanently stuck").
    /// Only the time he spends following a way he can't get along counts (<see cref="CountsAsTrying"/>): not standing at
    /// the end of his way on purpose (a lumberjack waiting by his hut with a full chest), not jostling by his goal, not
    /// waiting or working in between, nor going through a door (a settler shut in by a blocked door is moved out after a
    /// few minutes rather than two). The game's paths can't tell: in a gap too narrow for them, a way "starts" outside it.
    /// Nobody is moved at night or in a raid, nor more than twice in ten minutes (a goal he can never reach).
    /// The backstop (1.4.0, the player: "simple fail safes like teleporting the settler if they have been stuck"): wanting
    /// to be somewhere more than a patch away for <see cref="BackstopAfter"/> without getting out of the patch counts too,
    /// whatever he does on the way (a door tried again and again, a way that ends short): a builder in a hut being rebuilt
    /// tried a door 53 m off for 25 minutes, and none of that counted as trying to walk.
    /// </summary>
    public sealed class TrappedWatch
    {
        /// <summary>Getting this far from where the clock started counts as getting somewhere.</summary>
        public const float Patch = 4f;
        /// <summary>Trapped this long (walking), with no player near enough to see him moved ...</summary>
        public const double MoveAfter = 120;
        /// <summary>... or this long, whoever is near.</summary>
        public const double MoveAnywayAfter = 300;
        /// <summary>A player this near would see him moved.</summary>
        public const float SeenFrom = 30f;
        /// <summary>At most <see cref="MaxRescues"/> moves within this long.</summary>
        public const double RescueWindow = 600;
        public const int MaxRescues = 2;
        /// <summary>Wedged inside a hut this long (walking) he is moved to just inside its door: nobody sees it behind the walls.</summary>
        public const double InsideHutAfter = 30;
        /// <summary>Wanting to be elsewhere this long, in one patch, whatever he does on the way: the backstop.</summary>
        public const double BackstopAfter = 240;
        /// <summary>
        /// The backstop's patch is wider: one going to and fro round a small place he can't get out of (in and out of a
        /// hut's door, round a fenced yard; the old huts' test: 8 m for 40 minutes) is just as stuck.
        /// </summary>
        public const float BackstopPatch = 10f;
        /// <summary>Walks further apart than this are separate tries: the time in between isn't counted.</summary>
        private const double Gap = 1;

        private float _x, _z, _wx, _wz;
        private bool _anchored, _wantAnchored;
        private double _lastWalk = double.NegativeInfinity, _lastWant = double.NegativeInfinity;
        private readonly List<double> _rescues = new List<double>();

        /// <summary>How long he has tried to walk without getting out of the patch, in seconds.</summary>
        public double WalkedFor { get; private set; }

        /// <summary>How long he has wanted to be somewhere else without getting out of the patch (the backstop), in seconds.</summary>
        public double WantedFor { get; private set; }

        /// <summary>He has tried long enough that it's time to look whether he can be moved out.</summary>
        public bool Due => WalkedFor >= MoveAfter || WantedFor >= BackstopAfter;

        /// <summary>Stuck so long that he is moved even where a player could see it.</summary>
        public bool Overdue => WalkedFor >= MoveAnywayAfter || WantedFor >= MoveAnywayAfter;

        /// <summary>
        /// His walk counts as trying to get somewhere: he follows a way he hasn't come to the end of (he isn't standing at
        /// its end on purpose), and he isn't among others by his goal.
        /// </summary>
        public static bool CountsAsTrying(bool wayEnded, float toGoal) => !wayEnded && toGoal >= Unstuck.NearGoal;

        /// <summary>He is walking somewhere and isn't there yet, at (x, z).</summary>
        public void Walking(float x, float z, double now)
        {
            if (!_anchored || Distance(x, z) > Patch)
            {
                _x = x;   // only the walking clock: the backstop keeps its own, wider patch
                _z = z;
                _anchored = true;
                WalkedFor = 0;
                _lastWalk = now;
                return;
            }
            double step = now - _lastWalk;
            if (step > 0 && step <= Gap) WalkedFor += step;
            _lastWalk = now;
        }

        /// <summary>His goal is more than a patch away (the backstop): counted whatever he does on the way there.</summary>
        public void Wanting(float x, float z, double now)
        {
            if (!_wantAnchored || Distance(x, z, _wx, _wz) > BackstopPatch)
            {
                _wx = x;
                _wz = z;
                _wantAnchored = true;
                WantedFor = 0;
                _lastWant = now;
                return;
            }
            double step = now - _lastWant;
            if (step > 0 && step <= Gap) WantedFor += step;
            _lastWant = now;
        }

        /// <summary>
        /// A move to the Town Hall is only worth it from further than the backstop's patch (the third rerun: a guard whose
        /// post couldn't be reached stood by the Town Hall, and was "moved" 3.6 m).
        /// </summary>
        public static bool WorthMoving(float fromX, float fromZ, float toX, float toZ) => Distance(fromX, fromZ, toX, toZ) > BackstopPatch;

        /// <summary>He got where he was going, or there's nothing to do now: the clock starts again from here.</summary>
        public void Arrived(float x, float z) => Start(x, z);

        /// <summary>He was moved out at <paramref name="now"/>: the clock starts again from where he is now.</summary>
        public void Moved(float x, float z, double now)
        {
            _rescues.Add(now);
            Start(x, z);
        }

        /// <param name="nearestPlayer">How far the nearest player is from him.</param>
        /// <param name="indoors">He is meant to be indoors (night, a raid, an attack).</param>
        public bool ShouldMove(float nearestPlayer, bool indoors, double now)
        {
            if (indoors) return false;
            _rescues.RemoveAll(t => now - t >= RescueWindow);
            if (_rescues.Count >= MaxRescues) return false;
            return Overdue || (Due && nearestPlayer > SeenFrom);
        }

        /// <summary>
        /// Inside a hut (worker polish, 0.20: a farmer wedged where she spent the night in a turned Farm): after
        /// <see cref="InsideHutAfter"/>, whoever is near; the same limit of moves as <see cref="ShouldMove"/>.
        /// </summary>
        public bool ShouldMoveInsideHut(double now)
        {
            _rescues.RemoveAll(t => now - t >= RescueWindow);
            return _rescues.Count < MaxRescues && WalkedFor >= InsideHutAfter;
        }

        /// <summary>
        /// Up on a roof for a few seconds (<see cref="RoofRules"/>): put down at once, whoever is near, within the same
        /// limit of moves; the walking and wanting clocks don't count up there.
        /// </summary>
        public bool ShouldMoveOffRoof(double now)
        {
            _rescues.RemoveAll(t => now - t >= RescueWindow);
            return _rescues.Count < MaxRescues;
        }

        private void Start(float x, float z)
        {
            _x = _wx = x;
            _z = _wz = z;
            _anchored = _wantAnchored = true;
            WalkedFor = 0;
            WantedFor = 0;
        }

        private float Distance(float x, float z) => Distance(x, z, _x, _z);

        private static float Distance(float x, float z, float ax, float az)
        {
            float dx = x - ax, dz = z - az;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
