using System.Collections.Generic;

namespace VikingColonies.Core
{
    /// <summary>
    /// What an upgrade changes: pieces the same in both designs (same kind, within 5 cm and 2°) are kept; the old
    /// design's other pieces are taken away; the new design's other pieces are built.
    /// </summary>
    public sealed class DesignDiff
    {
        public const float SamePlace = 0.05f;
        public const float SameTurn = 2f;

        /// <summary>Indices in the new plan whose piece the old design already has.</summary>
        public List<int> Kept { get; } = new List<int>();
        /// <summary>Indices in the new plan to build.</summary>
        public List<int> Build { get; } = new List<int>();
        /// <summary>Indices in the old plan to take away.</summary>
        public List<int> TakeAway { get; } = new List<int>();

        public static DesignDiff Compare(IReadOnlyList<PlannedPiece> oldPlan, IReadOnlyList<PlannedPiece> newPlan)
        {
            var diff = new DesignDiff();
            var used = new bool[oldPlan.Count];
            for (int n = 0; n < newPlan.Count; n++)
            {
                int match = -1;
                if (newPlan[n].IsValid)
                    for (int o = 0; o < oldPlan.Count && match < 0; o++)
                        if (!used[o] && Same(oldPlan[o], newPlan[n])) match = o;
                if (match >= 0)
                {
                    used[match] = true;
                    diff.Kept.Add(n);
                }
                else diff.Build.Add(n);
            }
            for (int o = 0; o < oldPlan.Count; o++)
                if (!used[o] && oldPlan[o].IsValid) diff.TakeAway.Add(o);
            return diff;
        }

        public static bool Same(PlannedPiece a, PlannedPiece b) =>
            a.Prefab == b.Prefab && SamePiece(Vec3.Distance(a.Position, b.Position), Quat.Angle(a.Rotation, b.Rotation));

        /// <summary>
        /// A piece this far (metres) and this much turned (degrees) from a planned one is that piece: the one rule for the
        /// diff and for knowing a standing piece (the real builders' run on ship-huts, 2026-10-19: a standing piece was
        /// known within 10 cm, the diff told pieces 10 cm apart apart, and the builder took his new item stand for the old
        /// one and took it down). A placed piece stands exactly where its plan put it.
        /// </summary>
        public static bool SamePiece(float distance, float angle) => distance <= SamePlace && angle <= SameTurn;
    }
}
