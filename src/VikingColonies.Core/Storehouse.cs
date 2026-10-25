using System.Collections.Generic;
using System.Linq;

namespace VikingColonies.Core
{
    /// <summary>
    /// The Storehouse (1.5): one per colony, its own chest and every plain chest standing inside it are the colony's main
    /// store (docs/superpowers/specs/2026-10-21-vikingcolonies-storehouse-design.md).
    /// </summary>
    public static class StorehouseRules
    {
        /// <summary>How often a storehouse looks for the chests standing inside it.</summary>
        public const float ScanSeconds = 5f;

        /// <summary>Why a storehouse can't be placed here: one per colony first, then a Builder's Hut to build it.</summary>
        public static RedReason PlaceRed(int otherStorehousesInColony, bool hasBuilderHut)
        {
            if (otherStorehousesInColony > 0) return RedReason.OnlyOneStorehouse;
            if (!hasBuilderHut) return RedReason.NeedsBuilderHut;
            return RedReason.None;
        }

        /// <summary>A chest lower than this under the storehouse's floor stands under it, not in it.</summary>
        public const float UnderTheFloor = 0.3f;

        /// <summary>
        /// A chest joins the store when it stands inside the storehouse's walls as a placed piece, on its floor (not on the
        /// ground under a raised one: the follow-ups' review), and is neither another colony building's own chest, nor a
        /// personal chest, nor on a cart or a ship.
        /// </summary>
        public static bool Joins(bool insideRoom, bool isPiece, bool isColonyBuilding, bool isPersonal, bool onVehicle, float aboveFloor = 0f) =>
            insideRoom && isPiece && !isColonyBuilding && !isPersonal && !onVehicle && aboveFloor >= -UnderTheFloor;

        /// <summary>A piece runs through another when they overlap by more than this every way (touching doesn't count).</summary>
        public const float Through = 0.1f;

        /// <summary>
        /// A player's chest standing inside a storehouse stays through its upgrade (the spec: the player's own chests there
        /// don't block it) when no piece of the new level runs through it and something of the new level, or the ground,
        /// still holds it up. Else it is in the way, as any other building's piece.
        /// </summary>
        public static bool ChestStaysThroughUpgrade(PieceBox chest, IReadOnlyList<PieceBox> newPieces, bool onGround)
        {
            if (InTheWayOf(chest, newPieces) >= 0) return false;
            bool held = onGround;
            if (newPieces != null)
                foreach (PieceBox piece in newPieces)
                    if (RemovalOrder.RestsOn(chest, piece)) held = true;
            return held;
        }

        /// <summary>The first new piece that runs through the chest, or -1.</summary>
        public static int InTheWayOf(PieceBox chest, IReadOnlyList<PieceBox> newPieces)
        {
            if (newPieces == null) return -1;
            for (int i = 0; i < newPieces.Count; i++)
                if (RunsThrough(chest, newPieces[i])) return i;
            return -1;
        }

        private static bool RunsThrough(PieceBox a, PieceBox b) =>
            System.Math.Min(a.Max.X, b.Max.X) - System.Math.Max(a.Min.X, b.Min.X) > Through
            && System.Math.Min(a.Max.Y, b.Max.Y) - System.Math.Max(a.Min.Y, b.Min.Y) > Through
            && System.Math.Min(a.Max.Z, b.Max.Z) - System.Math.Max(a.Min.Z, b.Min.Z) > Through;

        /// <summary>Which chests a settler tries first (lower first): their own hut, the storehouse, the Town Hall, the rest.</summary>
        public static int Rank(bool isOwnHut, bool isStorehouse, bool isTownHall)
        {
            if (isOwnHut) return 0;
            if (isStorehouse) return 1;
            if (isTownHall) return 2;
            return 3;
        }
    }

    /// <summary>What the Town Hall's overview says about the storehouse (1.5.0, the review round's pick).</summary>
    public enum StorehouseWarning
    {
        None,
        /// <summary>No chest inside it has an empty slot: more chests inside, or a higher level.</summary>
        Full,
        /// <summary>Couriers hold loads they can't put away while it has room: the way in may be blocked.</summary>
        Holding,
    }

    public static class StorehouseWarnings
    {
        /// <summary>
        /// Only while couriers carry for the colony (with nobody carrying, the player fills the store and sees it). A courier
        /// holding a load is a courier, should the roster not have their hire yet.
        /// </summary>
        public static StorehouseWarning For(bool hasStorehouse, bool storeFull, int couriers, int holding)
        {
            if (!hasStorehouse || System.Math.Max(couriers, holding) <= 0) return StorehouseWarning.None;
            if (storeFull) return StorehouseWarning.Full;
            return holding > 0 ? StorehouseWarning.Holding : StorehouseWarning.None;
        }
    }

    /// <summary>One place's chests and what they hold, for the Stock tab.</summary>
    public sealed class ChestCount
    {
        public ChestCount(string where, IReadOnlyDictionary<string, int> items)
        {
            Where = where ?? "";
            Items = items ?? new Dictionary<string, int>();
        }

        public string Where { get; }
        public IReadOnlyDictionary<string, int> Items { get; }
    }

    /// <summary>One line of the Stock tab: an item, the colony's total, and the place holding the most.</summary>
    public sealed class StockLine
    {
        public string Item = "";
        public int Total;
        public string MostAt = "";
        public int MostCount;
    }

    /// <summary>The colony's stock across its chests: most first, then by name; where most lies (the first given on a tie).</summary>
    public static class ColonyStock
    {
        public static List<StockLine> Totals(IEnumerable<ChestCount> chests)
        {
            var lines = new Dictionary<string, StockLine>();
            if (chests != null)
                foreach (ChestCount chest in chests)
                {
                    if (chest == null) continue;
                    foreach (KeyValuePair<string, int> item in chest.Items)
                    {
                        if (string.IsNullOrEmpty(item.Key) || item.Value <= 0) continue;
                        StockLine line;
                        if (!lines.TryGetValue(item.Key, out line))
                        {
                            line = new StockLine { Item = item.Key };
                            lines[item.Key] = line;
                        }
                        line.Total += item.Value;
                        if (item.Value > line.MostCount)
                        {
                            line.MostCount = item.Value;
                            line.MostAt = chest.Where;
                        }
                    }
                }
            return lines.Values.OrderByDescending(l => l.Total).ThenBy(l => l.Item, System.StringComparer.Ordinal).ToList();
        }
    }
}
