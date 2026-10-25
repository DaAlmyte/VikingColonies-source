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
        private const int Round = 1, Failed_ = 2;
        private readonly Dictionary<string, int> _chests = new Dictionary<string, int>();

        public ChestStep AtReach(string chest, bool wallBetween) =>
            wallBetween && State(chest) == 0 ? ChestStep.GoRound : ChestStep.Take;

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
        public bool GoingRound(string chest) => State(chest) == Round;

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
