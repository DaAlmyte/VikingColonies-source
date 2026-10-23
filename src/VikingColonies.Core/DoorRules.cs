using System;
using System.Collections.Generic;
using System.Linq;

namespace VikingColonies.Core
{
    /// <summary>Which door a settler goes through on his way, from which side, and when he closes it behind him.</summary>
    /// <summary>What he does about a player standing in the doorway (0.20.1).</summary>
    public enum DoorwayChoice { Wait, PressOn, GiveUp }

    /// <summary>Where he is, going through a door (<see cref="DoorRules.Crossing"/>).</summary>
    public enum DoorCrossing { NotYet, Through, Round }

    /// <summary>
    /// A door's doorway, from its shape: its middle (at its floor; a door may hinge off its middle), the way through, its
    /// floor's height and how tall it is.
    /// </summary>
    public struct Doorway
    {
        public Doorway(Vec3 middle, Vec3 forward, float floorY, float height)
        {
            Middle = middle;
            Forward = forward;
            FloorY = floorY;
            Height = height;
        }

        public Vec3 Middle { get; }
        public Vec3 Forward { get; }
        public float FloorY { get; }
        public float Height { get; }
    }

    public static class DoorRules
    {
        /// <summary>He closes a door he went through once he is this far past it.</summary>
        public const float ClosePast = 2f;
        /// <summary>A door he opened but never went through (his goal changed) is closed once he is this far from it.</summary>
        public const float CloseAway = 4f;
        /// <summary>Doors this close to him or to where he is going are considered.</summary>
        public const float SearchRange = 25f;
        /// <summary>A closed door this close ahead of a walking settler is opened.</summary>
        public const float AheadReach = 1.6f;
        /// <summary>...and he must be within this far of the doorway's middle line, not walking past beside it.</summary>
        public const float AheadSideways = 0.8f;
        /// <summary>He stands this far in front of the doorway to open it.</summary>
        public const float StandOff = 1.2f;
        /// <summary>Where he may stand to go through, nearest first: further out when the near spot is out of reach (a stair, a bump).</summary>
        public static readonly float[] StandOffs = { StandOff, 2f, 2.8f };
        /// <summary>He steps through to this far beyond the doorway.</summary>
        public const float StepBeyond = 1.5f;
        /// <summary>While another settler goes through, he waits this far to the side of his spot, out of the way.</summary>
        public const float WaitAside = 1.4f;

        /// <summary>
        /// With walls on both sides of the door (a gate in a fence), he waits straight back from it instead: this far out
        /// (<paramref name="i"/>: the 1st, 2nd… waiter on that side), clear of the spot the one going the other way steps
        /// out onto (self-test run 21 of 0.12: he waited on it, and the two waited for each other).
        /// </summary>
        public static float BackOff(int i) => StepBeyond + 1.5f + i * 0.9f;
        /// <summary>Off the doorway's middle line by more than this, he lines up in front of it before stepping through.</summary>
        public const float LineUpSideways = 0.35f;
        /// <summary>Once lined up, he lines up again only if pushed further aside than this.</summary>
        public const float LineUpSlack = 0.8f;
        /// <summary>He opens the door from no farther than this (a path that ended round the corner isn't at the door).</summary>
        public const float OpenReach = StandOff + 0.8f;
        /// <summary>Someone this close to the doorway's middle, measured across the floor, is in the doorway.</summary>
        public const float DoorwayReach = 1.3f;
        /// <summary>A door leads towards his goal when its far side gets him at least this much closer to it.</summary>
        public const float CloserBy = 1f;
        /// <summary>A door that didn't move when asked is asked again after this long (it ignores requests while swinging).</summary>
        public const float AskAgainSeconds = 1f;

        /// <summary>A door this close to one of the colony's huts is part of the colony (a hut's own door, past the colony's edge).</summary>
        public const float HutDoorReach = 12f;

        /// <summary>Inside the colony's area: near the Town Hall, or by one of the colony's huts.</summary>
        public static bool InColonyArea(float fromHall, float radius, float fromNearestHut) => fromHall <= radius || fromNearestHut <= HutDoorReach;

        /// <summary>A door's approach takes at least this long before it can end on time alone (the old flat limit).</summary>
        public const float ApproachSeconds = 12f;
        /// <summary>Getting no closer to the door's spot this long ends the approach.</summary>
        public const float ApproachStallSeconds = 8f;
        /// <summary>The approach is allowed a second for each this many metres to the door's spot...</summary>
        public const float ApproachPace = 1.2f;
        /// <summary>...and never more than this.</summary>
        public const float ApproachMaxSeconds = 90f;

        /// <summary>
        /// Whether he gives up walking to a door's spot: he has got no closer for <see cref="ApproachStallSeconds"/>, or
        /// the time for the way there is up (12 s and a second for each 1.2 m, at most 90 s). The bug hunt of 1.4.1: walking
        /// well to a door 40-60 m off, settlers were cut off at a flat 12 s, and the door was "tried lately" after.
        /// </summary>
        public static bool ApproachGivesUp(float elapsed, float sinceCloser, float startDistance) =>
            sinceCloser > ApproachStallSeconds
            || elapsed > Math.Min(ApproachMaxSeconds, ApproachSeconds + Math.Max(0f, startDistance) / ApproachPace);

        /// <summary>A door this close to him is the door of the room he stands in.</summary>
        public const float LeaveByReach = 3.5f;

        /// <summary>
        /// He may leave by this door wherever the colony's area ends: it is right beside him, the door of the room he is in,
        /// and he is on his way home into the colony (full runs of 16 and 19 October: inside a building whose door stood
        /// past the colony's edge, a settler was sent at far doors all night and never by this one). On a way elsewhere a
        /// stranger's door outside the colony stays his to pass by (the review of 1.4.1). A key or another's ward still
        /// keeps it shut (MayOpen).
        /// </summary>
        public static bool MayLeaveBy(float fromSettler, bool goalInColony, bool underRoof = false, bool nearestToHim = false,
                                      bool sheltered = false) =>
            (goalInColony || underRoof)   // under a roof: inside something, whatever his goal (the second review)
            && (fromSettler <= LeaveByReach || sheltered && nearestToHim && fromSettler <= RoomReach);

        /// <summary>
        /// Inside a room, not only under a roof: covered round as the game counts a player sheltered (the review of the
        /// game-check fixes: our huts' 2 m eaves and porches put a settler under a roof, outside).
        /// </summary>
        public static bool Sheltered(bool underRoof, float cover) => underRoof && cover >= ShelteredCover;

        /// <summary>The game's cover for "sheltered".</summary>
        public const float ShelteredCover = 0.8f;

        /// <summary>
        /// Under a roof, the door nearest him this far off is the door of the room he is in, across it (B1's game check in
        /// 1.4.1: in a building the colony doesn't know, Leif stood 5.7 m from its door and stayed in all night).
        /// </summary>
        public const float RoomReach = 12f;

        /// <summary>Within this of a door, a hut's shape may say which side he starts from even when the walking map reaches neither.</summary>
        public const float HutSideReach = 6f;

        /// <summary>
        /// Whether a hut's shape may decide the side he starts from (SideFromHut): when he is inside that door's own hut (a
        /// level-5 Longhouse is 21 m long: the review of 1.4.1), the walking map reaches a side of the door (a hut turned off
        /// the compass gets it wrong, 0.20), or he is by it. Not for a far door neither side of which he can walk to from
        /// outside its hut: he isn't there to go through it (Signe's night, 19 October: sent 23 m to the Longhouse's door
        /// from inside another building). Out in the open (no roof over him) the shape still decides: 1.4.1's full run, a
        /// builder 18 m from the Farm's yard gate, which the walking map reached on neither side, left a pole unmended.
        /// </summary>
        public static bool HutSideApplies(bool frontReachable, bool backReachable, float fromDoor, bool insideItsHut = false,
                                          bool outInTheOpen = false) =>
            insideItsHut || outInTheOpen || frontReachable || backReachable || fromDoor <= HutSideReach;

        /// <summary>The colony owner may open it: it is inside the colony's area, needs no key, and no one else's ward covers it.</summary>
        public static bool MayOpen(bool inColonyArea, bool needsKey, bool otherWard) => inColonyArea && !needsKey && !otherWard;

        /// <summary>Which side of a door a point is on: +1 ahead along its forward axis, -1 behind it, 0 within 5 cm of the doorway.</summary>
        public static int SideOf(Vec3 door, Vec3 forward, Vec3 point)
        {
            float along = Vec3.Dot((point - door).Flat, forward.Flat.Normalized);
            return along > 0.05f ? 1 : along < -0.05f ? -1 : 0;
        }

        /// <summary>The door's open state when opened from this side: the game's UseDoor opens it away from its user, and opened
        /// from behind (side -1) its state is +1.</summary>
        public static int OpenStateFrom(int side) => -side;

        /// <summary>
        /// An open door swings towards this side: it was opened from the other one, and its leaf stands in the doorway for
        /// anyone coming from here. Opening it again does nothing; he closes it and opens it from his side (0.20.1, as
        /// Valheim Villages' door handler).
        /// </summary>
        public static bool SwingsTowards(int state, int side) => state != 0 && side != 0 && state != OpenStateFrom(side);

        /// <summary>
        /// The way he is going: towards the first point of his path more than 0.3 m away, else the way he faces. After a hop
        /// or a step aside he may face sideways at a closed gate he is about to walk into (full self-test run 6, 0.20).
        /// </summary>
        public static Vec3 WayHeading(Vec3 settler, IReadOnlyList<Vec3> path, Vec3 facing)
        {
            if (path != null)
                foreach (Vec3 point in path)
                {
                    Vec3 way = (point - settler).Flat;
                    if (way.Length > 0.3f) return way.Normalized;
                }
            return facing;
        }

        /// <summary>A player standing in a doorway is waited for this long, then he goes on through (0.20.1, as Hearthfolk).</summary>
        public const float PlayerWaitSeconds = 2f;

        public static bool PressOnPastPlayer(float waited) => waited >= PlayerWaitSeconds;

        /// <summary>
        /// A player stands in the doorway: a monster there too (the player may hide it, being found first) means giving the
        /// door up at once; else he waits <see cref="PlayerWaitSeconds"/>, then goes on through (review of 0.20.1).
        /// </summary>
        public static DoorwayChoice PlayerInDoorway(bool hostileThere, float waited) =>
            hostileThere ? DoorwayChoice.GiveUp : PressOnPastPlayer(waited) ? DoorwayChoice.PressOn : DoorwayChoice.Wait;

        /// <summary>
        /// He closes a door open towards him and opens it from his side: one he didn't open himself, once a passing, and only
        /// with nobody in the doorway (review of 0.20.1: it closed on a player standing there).
        /// </summary>
        public static bool MayReswing(bool openedIt, bool reswung, int state, int side, bool doorwayClear) =>
            !openedIt && !reswung && doorwayClear && SwingsTowards(state, side);

        /// <summary>A point on the given side of the door, this far from it along its forward axis, at the door's height.</summary>
        public static Vec3 OnSide(Vec3 door, Vec3 forward, int side, float distance) => door + forward.Flat.Normalized * (side * distance);

        /// <summary>
        /// Whether to close a door he opened: he went through it and is 2 m past, or he never did and is 4 m away — and
        /// nobody stands in the doorway, and no other settler is going through it.
        /// </summary>
        public static bool ShouldClose(bool wentThrough, float distance, bool doorwayClear, bool othersPassing) =>
            doorwayClear && !othersPassing && distance >= (wentThrough ? ClosePast : CloseAway);

        /// <summary>A door this far outside a hut's footprint still counts as one of its own (it stands in the wall line).</summary>
        public const float OwnDoorMargin = 0.5f;

        /// <summary>
        /// The doors worth trying (0.18; self-test run 42: from a plot between two huts the farmer went through the
        /// neighbour's door to reach the Farm's chest), given each hut's site (its pieces' outline grown by
        /// <paramref name="siteMargin"/>): while he is inside a hut and his goal isn't, that hut's own doors, or every door
        /// when none of its are among them; else, while the goal is inside a hut and he isn't, only that hut's own doors, even
        /// none (another hut's door never leads into this one). Else every door. "Inside" is the outline itself, not the
        /// margin round it (review: a settler pressed against the wall counted as in). In a building the colony doesn't know
        /// (no outline), the door of his room (<paramref name="roomDoor"/>, -1 for none) stays among them: his way out comes
        /// first (B1's game check in 1.4.1: on her way to the Longhouse, Signe's way out was dropped and she stayed in).
        /// </summary>
        public static List<int> OwnDoors(IReadOnlyList<Vec3> doors, Vec3 settler, Vec3 goal, IReadOnlyList<Footprint> huts, float siteMargin = 0f,
                                         int roomDoor = -1)
        {
            var all = new List<int>();
            for (int i = 0; i < doors.Count; i++) all.Add(i);
            if (huts == null) return all;
            foreach (Footprint site in huts)
            {
                if (site == null) continue;
                Footprint inside = site.Grow(-siteMargin);
                if (inside.Contains(settler) && !inside.Contains(goal))
                {
                    List<int> own = OwnOf(doors, all, site, siteMargin);
                    return own.Count > 0 ? own : all;
                }
            }
            foreach (Footprint site in huts)
            {
                if (site == null) continue;
                Footprint inside = site.Grow(-siteMargin);
                if (!inside.Contains(goal) || inside.Contains(settler)) continue;
                List<int> own = OwnOf(doors, all, site, siteMargin);
                bool inKnownHut = huts.Any(h => h != null && h.Grow(-siteMargin).Contains(settler));
                // Never a known hut's own door from outside it (under its porch: the review of the game-check fixes).
                bool aHutsDoor = roomDoor >= 0 && roomDoor < doors.Count
                                 && huts.Any(h => h != null && OwnOf(doors, new List<int> { roomDoor }, h, siteMargin).Count > 0);
                if (roomDoor >= 0 && roomDoor < doors.Count && !own.Contains(roomDoor) && !inKnownHut && !aHutsDoor) own.Add(roomDoor);
                return own;
            }
            return all;
        }

        /// <summary>
        /// The side of a hut's own door a settler starts from, from the hut's shape: inside its site, the side its middle is
        /// on; going in from outside, the other. 0 when no hut decides (the door isn't the hut's, or neither he nor his goal
        /// is inside one): the walking map does. Worker polish (0.20): inside a hut turned off the compass the walking map
        /// said the door's outside spots were reachable from inside and the inside ones not.
        /// </summary>
        public static int SideFromHut(Vec3 door, Vec3 forward, Vec3 settler, Vec3 goal, IReadOnlyList<Footprint> huts, float siteMargin = 0f)
        {
            if (huts == null) return 0;
            foreach (Footprint site in huts)
            {
                if (site == null || !site.Grow(OwnDoorMargin - siteMargin).Contains(door)) continue;
                int middle = SideOf(door, forward, site.Centre);
                if (middle == 0) continue;
                // Inside it: within its outline and on its middle's side of the door (under a porch in front of the door,
                // also within the outline, he is outside: final review of 0.20).
                if (Inside(site, door, forward, settler, siteMargin)) return middle;
                // Going in from outside every hut; inside another he leaves that one by its own door first (full self-test
                // run 3: sent at this door from inside another hut, he walked into its wall).
                bool inAnother = huts.Any(h => h != null && h != site && h.Grow(-siteMargin).Contains(settler));
                if (!inAnother && site.Grow(-siteMargin).Contains(goal)) return -middle;
            }
            return 0;
        }

        /// <summary>He is inside this hut: within its outline and on the side of its door its middle is on.</summary>
        private static bool Inside(Footprint site, Vec3 door, Vec3 forward, Vec3 settler, float siteMargin) =>
            site.Grow(-siteMargin).Contains(settler) && SideOf(door, forward, settler) == SideOf(door, forward, site.Centre);

        /// <summary>
        /// He stands inside the hut this door belongs to. There the walking map may give him no way at all to the door's
        /// spot (observation run 2, 0.20: the farmer stood still 3 m from it in the turned Farm), so he walks there straight.
        /// </summary>
        public static bool InsideItsHut(Vec3 door, Vec3 forward, Vec3 settler, IReadOnlyList<Footprint> huts, float siteMargin = 0f) =>
            HutAround(door, forward, settler, huts, siteMargin) != null;

        /// <summary>The site of the hut this door belongs to, when he stands inside it (not under a porch before it); null otherwise.</summary>
        public static Footprint HutAround(Vec3 door, Vec3 forward, Vec3 settler, IReadOnlyList<Footprint> huts, float siteMargin = 0f)
        {
            if (huts == null) return null;
            foreach (Footprint site in huts)
                if (site != null && site.Grow(OwnDoorMargin - siteMargin).Contains(door) && Inside(site, door, forward, settler, siteMargin)) return site;
            return null;
        }

        /// <summary>
        /// The side he first steps aside to when something stops his straight walk to a door's spot inside its hut: away
        /// from the hut's middle, where its chest stands in our huts (<see cref="StraightWalk"/>). 1 is his right, -1 his left.
        /// </summary>
        public static int AsideFromMiddle(Vec3 settler, Vec3 spot, Vec3 middle)
        {
            float wayX = spot.X - settler.X, wayZ = spot.Z - settler.Z;
            float onRight = (middle.X - settler.X) * wayZ - (middle.Z - settler.Z) * wayX;   // his right is (wayZ, -wayX)
            return onRight > 0f ? -1 : 1;
        }

        /// <summary>Inside its hut, a path that brings him no closer to the door's spot for this long is a wrong one.</summary>
        public const float StraightAfterSeconds = 2f;

        /// <summary>
        /// Inside the door's own hut he walks straight to its spot when the paths give him no way there, or lead him nowhere
        /// nearer for <see cref="StraightAfterSeconds"/> (observation run 3, 0.20: the walking map said the outside was
        /// reachable from inside the turned Farm, and her path led into the wall). Outside, the paths are right.
        /// </summary>
        public static bool WalkStraight(bool insideItsHut, bool wayEnded, float secondsNotCloser) =>
            insideItsHut && (wayEnded || secondsNotCloser >= StraightAfterSeconds);

        /// <summary>Its doors: within <see cref="OwnDoorMargin"/> of its pieces' outline (a neighbour's a metre off is not one).</summary>
        private static List<int> OwnOf(IReadOnlyList<Vec3> doors, List<int> all, Footprint site, float siteMargin)
        {
            Footprint walls = site.Grow(OwnDoorMargin - siteMargin);
            var own = new List<int>();
            foreach (int i in all)
                if (walls.Contains(doors[i])) own.Add(i);
            return own;
        }

        /// <summary>
        /// The door to go through to reach a goal he can't reach: near him or near the goal, with the shortest way from
        /// him to the door to the goal. -1 when there is none.
        /// </summary>
        public static int ChooseDoor(IReadOnlyList<Vec3> doors, Vec3 settler, Vec3 goal)
        {
            int best = -1;
            float bestWay = float.MaxValue;
            for (int i = 0; i < doors.Count; i++)
            {
                float toDoor = Vec3.Distance(settler.Flat, doors[i].Flat), toGoal = Vec3.Distance(doors[i].Flat, goal.Flat);
                if (toDoor > SearchRange && toGoal > SearchRange) continue;
                if (toDoor + toGoal >= bestWay) continue;
                best = i;
                bestWay = toDoor + toGoal;
            }
            return best;
        }

        /// <summary>
        /// A closed door he is walking through: right ahead (within reach, near the doorway's middle line), heading through
        /// it (less than 60° off straight), and his goal beyond it. A door he only walks past, along a wall, is not.
        /// </summary>
        public static bool IsOnWayThrough(Vec3 settler, Vec3 heading, Vec3 door, Vec3 forward, Vec3 goal)
        {
            Vec3 axis = forward.Flat.Normalized, toDoor = (door - settler).Flat;
            if (heading.Flat.Length < 0.01f || axis.Length < 0.01f || toDoor.Length > AheadReach) return false;
            int side = SideOf(door, forward, settler);
            if (side == 0 || SideOf(door, forward, goal) != -side) return false;
            Vec3 sideways = toDoor - axis * Vec3.Dot(toDoor, axis);
            if (sideways.Length > AheadSideways) return false;
            return Vec3.Dot(heading.Flat.Normalized, axis * -side) >= 0.5f;
        }

        /// <summary>
        /// Whether "no door leads there" is worth its log line: a door by the goal was weighed (the goal may be shut in), or
        /// the settler is under a roof (inside, the door is the way out). Out in the open with only doors by the settler,
        /// it's routine: a log just off the walking map, the door of their own hut nearby (1.4.1's colony watch, 119 of 123).
        /// </summary>
        /// <param name="gapFromHere">How far from the goal, flat, the way from where the settler stands ends (NaN: not
        /// measured). Within <see cref="WithinReach"/> and <see cref="ReachHeight"/> it's no news: the way goes right up to
        /// the goal (1.4.2's colony watch, 90 of 91: a lumberjack after logs between huts, "from here 0,0 m").</param>
        /// <param name="heightOff">How far above (+) or below the way's end the goal is (NaN: not measured).</param>
        public static bool ExplainNoDoor(bool doorNearGoal, bool underRoof, float gapFromHere = float.NaN, float heightOff = float.NaN) =>
            (doorNearGoal || underRoof) && !(gapFromHere <= WithinReach && Math.Abs(heightOff) <= ReachHeight);

        /// <summary>
        /// A way from here ending this close to the goal, flat, goes right up to it (ExplainNoDoor). Behind a wall it ends
        /// further off, a settler's width and the wall away (1.4.3's review: a chest on the inside of a hut's wall).
        /// </summary>
        public const float WithinReach = 0.3f;

        /// <summary>And this close in height: a chest hung on a wall is in reach, the floor above (2 m up) isn't.</summary>
        public const float ReachHeight = 1.5f;

        /// <summary>
        /// Through the door he gets to his goal: beyond it the way goes all the way there, or at least ends
        /// <see cref="CloserBy"/> nearer the goal than his own way does. (A goal out of reach for other reasons isn't helped
        /// by walking into a building.)
        /// </summary>
        public static bool LeadsTowards(float gapFromHere, float gapFromBeyond, bool reachesFromBeyond) =>
            reachesFromBeyond || gapFromBeyond <= gapFromHere - CloserBy;

        /// <summary>The side he goes through from: the one he can walk to when he can't walk to the other; 0 when both or neither.</summary>
        public static int SideToUse(bool frontReachable, bool backReachable) =>
            frontReachable == backReachable ? 0 : frontReachable ? 1 : -1;

        /// <summary>
        /// How long a door isn't tried again for the same goal: 30 s after going through it failed; a few seconds when it
        /// only didn't fit from where he stood (from elsewhere it may).
        /// </summary>
        public static float RetryAfter(bool failedPassing) => failedPassing ? 30f : 5f;

        /// <summary>
        /// A path that ends short of the goal's reach, though near enough to count as arriving (a chest behind a wall),
        /// first looks for a door to go through.
        /// </summary>
        public static bool TryDoorFirst(bool pathEnded, float distance, float stopDistance) => pathEnded && distance > stopDistance;

        /// <summary>Faster than this, someone in the doorway is walking through it, not standing in it.</summary>
        public const float MovingSpeed = 0.3f;

        /// <summary>
        /// Someone in the doorway he waits out as a turn, however long it takes him (within the time a passing gets): a
        /// player walking through, or a settler going through the door, even standing still (he is waiting his own turn:
        /// seen, a settler stepping out waited for the spot the one outside stood on, and the one outside gave up). A wolf
        /// or a boar there, a player standing still, or a settler just standing there (review: he held a household
        /// outside for a minute) isn't. <paramref name="settler"/>: a settler who is himself going through a door.
        /// </summary>
        public static bool TakesATurn(bool person, bool settler, bool hostile, float speed) =>
            !hostile && (settler || (person && speed > MovingSpeed));

        /// <summary>How long he stands aside, out of the way, for a settler in the doorway.</summary>
        public const float MakeWaySeconds = 3f;

        /// <summary>A passing with no progress this long is over (however often the door was closed on him).</summary>
        public const double PassSeconds = 25;
        /// <summary>A whole passing, turns waited out included, is over after this long: past the longest approach (90 s) with
        /// time to open the door and step through (the whole-mod review of 1.4.1: 60 s cut the longer approach off).</summary>
        public const double MaxPassSeconds = 120;

        /// <summary>Waiting turns starts the clock for progress again, but not the one for the whole passing.</summary>
        public static bool PassingOver(double sinceStart, double sinceProgress) => sinceProgress > PassSeconds || sinceStart > MaxPassSeconds;

        /// <summary>A monster in the doorway is never waited for: he gives that door up at once.</summary>
        public static bool GiveUpAtOnce(bool hostile) => hostile;

        /// <summary>A way that ends this far short of the goal, or more, makes him look for a door at once.</summary>
        public const float EarlyLookGap = 2f;

        /// <summary>
        /// A way that ends well short of the goal (a wall between, a closed door) has him look for a door before he walks
        /// it, not first at its end, which can be a corner of the hut he is in.
        /// </summary>
        public static bool LookForDoorNow(float wayEndGap) => wayEndGap >= EarlyLookGap;

        /// <summary>Looking early, only doors this near him count: going through one has to fit the time a passing gets.</summary>
        public const float EarlyLookReach = 12f;

        public static bool NearEnoughToLookEarly(float doorFromHim) => doorFromHim <= EarlyLookReach;

        /// <summary>
        /// Where he steps next while going through an open door from <paramref name="startSide"/>: standing off to the side
        /// of the doorway he first lines up in front of its middle (a straight step from beside it would hit the wall), then
        /// straight through to beyond it.
        /// </summary>
        /// <param name="linedUp">He has lined up once already: a little drift (a stair's edge, a gate post) doesn't send him
        /// back, only being pushed well aside (<see cref="LineUpSlack"/>).</param>
        public static Vec3 ThroughStep(Vec3 door, Vec3 forward, int startSide, Vec3 settler, bool linedUp = false)
        {
            Vec3 axis = forward.Flat.Normalized, offset = (settler - door).Flat;
            float along = Vec3.Dot(offset, axis);
            float sideways = (offset - axis * along).Length;
            bool beforeDoorway = along * startSide > 0.3f;
            return beforeDoorway && sideways > (linedUp ? LineUpSlack : LineUpSideways)
                ? OnSide(door, forward, startSide, StandOff)
                : OnSide(door, forward, -startSide, StepBeyond);
        }

        /// <summary>He stands at the door, close enough to open it.</summary>
        public static bool ReadyToOpen(float distanceToDoor) => distanceToDoor <= OpenReach;

        /// <summary>He stands at the door, close enough to open it, having come to a spot <paramref name="standOff"/> out.</summary>
        public static bool ReadyToOpen(float distanceToDoor, float standOff) => distanceToDoor <= standOff + (OpenReach - StandOff);

        /// <summary>
        /// He stands at the door to open it: close enough (as from a spot <paramref name="standOff"/> out), in front of its
        /// doorway rather than along its wall (this matters from the spots further out), and at its height (1.4.1's release
        /// run: a settler opened a blocked hut's door from 3.5 m along its wall, then from its roof).
        /// </summary>
        public static bool ReadyToOpen(Doorway door, Vec3 settler, float standOff) =>
            ReadyToOpen(Vec3.Distance(settler.Flat, door.Middle.Flat), standOff) && Sideways(door, settler) <= OpenReach
            && AtDoorHeight(door, settler.Y);

        /// <summary>A step through stays within this of the doorway's middle, sideways (a hut's door or gate is 2 m wide).</summary>
        public const float ThroughSideways = 1.5f;

        /// <summary>
        /// Where he is, going through a door from <paramref name="startSide"/>: not past it yet, through its doorway, or past
        /// its line beside or above the doorway (round the end of its wall, over its roof: 1.4.1's release run, where each
        /// such "pass" started the clock for giving a blocked hut up again). Up on a hop he is judged once he lands.
        /// </summary>
        public static DoorCrossing Crossing(Doorway door, int startSide, Vec3 settler, bool onGround)
        {
            if (SideOf(door.Middle, door.Forward, settler) != -startSide || Vec3.Distance(settler.Flat, door.Middle.Flat) < StandOff)
                return DoorCrossing.NotYet;
            if (Sideways(door, settler) > ThroughSideways) return DoorCrossing.Round;
            if (AtDoorHeight(door, settler.Y)) return DoorCrossing.Through;
            return onGround ? DoorCrossing.Round : DoorCrossing.NotYet;
        }

        /// <summary>Feet may be this far above a doorway's floor at least (a step up, a hop)...</summary>
        public const float AboveFloor = 1.2f;
        /// <summary>... or as far as a settler still fits under its top (a tall gate in a slope); this is a settler's height.</summary>
        public const float SettlerHeight = 1.8f;
        /// <summary>Feet may be this far below a doorway's floor (a stair's foot, the farthest spot out).</summary>
        public const float BelowFloor = 3f;

        /// <summary>
        /// Feet at a doorway's height: from a stair's foot below its floor up to a step above it, or as high as a settler
        /// still fits under a tall gate; not on a roof or a floor above it (the review of 1.4.2's door rules: judged from
        /// the doorway's floor, as a door's pivot may be at its floor, 1 m up or 2 m up).
        /// </summary>
        public static bool AtDoorHeight(Doorway door, float feetY) =>
            feetY >= door.FloorY - BelowFloor && feetY <= door.FloorY + Math.Max(AboveFloor, door.Height - SettlerHeight);

        /// <summary>How far a point is from a doorway's middle line, across it (flat).</summary>
        private static float Sideways(Doorway door, Vec3 point)
        {
            Vec3 axis = door.Forward.Flat.Normalized, offset = (point - door.Middle).Flat;
            return (offset - axis * Vec3.Dot(offset, axis)).Length;
        }

        /// <summary>Someone in the doorway or waiting by it: near its middle across the floor, at its height (not a floor above).</summary>
        public static bool InDoorway(Vec3 door, Vec3 person) =>
            Vec3.Distance(person.Flat, door.Flat) <= DoorwayReach && person.Y >= door.Y - 2.5f && person.Y <= door.Y + 1f;

        /// <summary>A door that didn't move is asked again once this long has passed since asking.</summary>
        public static bool AskAgain(float sinceAsked) => sinceAsked >= AskAgainSeconds;
    }
}
