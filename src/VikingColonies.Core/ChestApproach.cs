using System;
using System.Collections.Generic;

namespace VikingColonies.Core
{
    public enum ChestStep
    {
        /// <summary>He takes from (or puts into) the chest from where he stands.</summary>
        Take,
        /// <summary>A wall stands between: he goes round to a side of the chest with nothing between first.</summary>
        GoRound,
    }

    /// <summary>Where he walks for a chest: its open side, or the chest itself.</summary>
    public enum ChestGoal { OpenSide, Chest }

    /// <summary>Which side of a chest he stands on (its front faces into a hut in the mod's designs).</summary>
    public static class ChestSides
    {
        /// <summary>
        /// Behind the chest: more than 60 degrees past its sides, seen from its front (a player's report, 1.4.3: behind the
        /// Builder's Hut's back wall, by its chest). Flat offsets from the chest, and its facing.
        /// </summary>
        public static bool Behind(float fromX, float fromZ, float forwardX, float forwardZ)
        {
            double from = Math.Sqrt(fromX * fromX + fromZ * fromZ), forward = Math.Sqrt(forwardX * forwardX + forwardZ * forwardZ);
            if (from < 0.1 || forward < 0.001) return false;
            return (fromX * forwardX + fromZ * forwardZ) / (from * forward) < -0.5;
        }
    }

    /// <summary>
    /// A settler within reach of a chest (the old huts' upgrade test, 2026-10-19: a builder took from the Builder's Hut's
    /// chest through its back wall, standing in the woodpile strip behind it). Through a wall he goes round to its open
    /// side first. What he found is kept for each chest until he leaves the site (the fix pass's review: once round, a post
    /// still counting as a wall sent him round again every other frame, and a chest whose open side he couldn't reach cost
    /// a stuck wait on every fetch; the whole-mod review, M3: a fetch goes to two chests by turns, and one chest's memory
    /// was lost at the other).
    /// </summary>
    public sealed class ChestRound
    {
        // Planned: the first look's plan (its open side, or the chest itself), checked once on arrival; Direct: nothing between.
        private const int Round = 1, Failed_ = 2, PlannedSide = 3, PlannedChest = 4, Direct = 5;
        private readonly Dictionary<string, int> _chests = new Dictionary<string, int>();

        /// <summary>
        /// He got there: through a wall he goes round first, until he has been round or found he can't. A first look's plan is
        /// checked here once (its review: the look may come before the walls stand, or while a piece blocks every side), and
        /// settled when nothing stands between.
        /// </summary>
        public ChestStep AtReach(string chest, bool wallBetween)
        {
            int state = State(chest);
            if (state != 0 && state != PlannedSide && state != PlannedChest) return ChestStep.Take;
            if (wallBetween) return ChestStep.GoRound;
            if (state != 0) _chests[chest] = state == PlannedSide ? Round : Direct;
            return ChestStep.Take;
        }

        /// <summary>Whether a wall between him and the chest still matters when he gets there.</summary>
        public bool ChecksOnArrival(string chest)
        {
            int state = State(chest);
            return state == 0 || state == PlannedSide || state == PlannedChest;
        }

        /// <summary>He found the chest's open side: he goes there for it from now on.</summary>
        public void WentRound(string chest)
        {
            if (chest != null) _chests[chest] = Round;
        }

        /// <summary>He couldn't get to its open side: he takes from where he can, and doesn't try again for it.</summary>
        public void Failed(string chest)
        {
            if (chest != null) _chests[chest] = Failed_;
        }

        /// <summary>He goes to this chest's open side, not to the chest itself.</summary>
        public bool GoingRound(string chest) => State(chest) == Round || State(chest) == PlannedSide;

        /// <summary>Nothing learned about this chest yet on this site.</summary>
        public bool IsNew(string chest) => State(chest) == 0;

        /// <summary>
        /// Where he walks for a chest: a chest new to him with an open side, straight to that side (a player's report,
        /// 1.4.3: the walk to the chest itself ended behind the hut's back wall, and he went round to the door from there,
        /// almost every fetch); one already gone round to, its open side; else the chest, as before. Looked for once, not
        /// on every step of the walk; the plan is checked once on arrival (<see cref="AtReach"/>).
        /// </summary>
        public ChestGoal FirstGoal(string chest, bool hasOpenSide)
        {
            if (chest != null && State(chest) == 0) _chests[chest] = hasOpenSide ? PlannedSide : PlannedChest;
            return GoingRound(chest) ? ChestGoal.OpenSide : ChestGoal.Chest;
        }

        /// <summary>He left the site: every chest is looked at afresh.</summary>
        public void Leave() => _chests.Clear();

        /// <summary>
        /// A look for a site: every chest is looked at afresh only when he held a site or takes a new one. A look that finds
        /// none keeps what he learned (the review round's bug hunt: an idle builder putting leftovers back looked every 5 s,
        /// each look forgot the chest he couldn't get round to, and he circled his hut for an hour).
        /// </summary>
        public void Relook(bool heldSite, bool newSite)
        {
            if (heldSite || newSite) Leave();
        }

        private int State(string chest) => chest != null && _chests.TryGetValue(chest, out int state) ? state : 0;
    }
}
