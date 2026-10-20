using System;
using System.Collections.Generic;
using System.Linq;

namespace VikingColonies.Core
{
    public static class BuildSequence
    {
        /// <summary>The order to try pieces in: lowest first (in 10 cm steps), then nearest the middle.</summary>
        public static List<int> Order(IReadOnlyList<PlannedPiece> pieces, Vec3 centre) => pieces
            .Where(p => p.IsValid)
            .OrderBy(p => (int)Math.Round(p.Position.Y * 10f))
            .ThenBy(p => Vec3.Distance(p.Position.Flat, centre.Flat))
            .ThenBy(p => p.Index)
            .Select(p => p.Index)
            .ToList();
    }

    /// <summary>
    /// Walks the building order looking for the next piece to put up: open (not done, not skipped) and one that will
    /// hold. Checks are costly, so only a few are tried per call; the next call carries on where this one stopped.
    /// </summary>
    public sealed class BuildCursor
    {
        private readonly IReadOnlyList<int> _order;
        private int _position;
        private int _failedSinceSuccess;

        public BuildCursor(IReadOnlyList<int> order) { _order = order ?? new int[0]; }

        /// <summary>Every open piece has been tried since the last success and none will hold.</summary>
        public bool Stuck { get; private set; }

        /// <summary>The piece most recently tried (for "no support for …" messages); -1 before any.</summary>
        public int LastTried { get; private set; } = -1;

        public int Next(Func<int, bool> isOpen, Func<int, bool> canPlace, int maxTries)
        {
            int open = _order.Count(isOpen);
            if (open == 0)
            {
                Stuck = false;
                return -1;
            }
            int tries = 0;
            for (int step = 0; step < _order.Count && tries < maxTries; step++)
            {
                int index = _order[_position];
                _position = (_position + 1) % _order.Count;
                if (!isOpen(index)) continue;
                tries++;
                LastTried = index;
                if (canPlace(index))
                {
                    Restart();
                    return index;
                }
                _failedSinceSuccess++;
            }
            Stuck = _failedSinceSuccess >= open;
            return -1;
        }

        /// <summary>Something changed (a piece went up or fell): start again from the lowest piece.</summary>
        public void Restart()
        {
            _position = 0;
            _failedSinceSuccess = 0;
            Stuck = false;
        }
    }
}
