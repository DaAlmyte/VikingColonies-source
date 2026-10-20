using System;
using System.Collections.Generic;
using System.Linq;

namespace VikingColonies.Core
{
    public enum FarmerAction { Unemployed, Rest, Deposit, WaitStorageFull, Gather, Harvest, Plant, Cultivate, WaitNeedsCultivator, WaitNeedsSeeds, WaitGrowing, WaitNoField }

    /// <summary>Everything the farmer knows right now. The game side fills this in every tick.</summary>
    public struct FarmerSituation
    {
        public bool HasHut;
        /// <summary>The Farm has a field with plots (0.20.1: a Farm works only its fields, MineColonies' way).</summary>
        public bool HasField;
        public bool IsNight;
        public int CarryTotal;
        public int CarryCapacity;
        public bool StorageFull;
        /// <summary>What he just picked lies on the ground by the crop.</summary>
        public bool DropsNearby;
        /// <summary>A ripe crop stands on one of his plots.</summary>
        public bool RipeCrop;
        /// <summary>One of his plots is cultivated and empty.</summary>
        public bool PlantablePlot;
        /// <summary>A seed of a ticked crop is in the Farm's chest or in his hands.</summary>
        public bool SeedToPlant;
        /// <summary>One of his plots isn't cultivated yet.</summary>
        public bool UncultivatedPlot;
        /// <summary>A Cultivator is in the Farm's chest (he works with a copy).</summary>
        public bool HasCultivator;
        /// <summary>Seeds he took to plant (in <see cref="CarryTotal"/>): not a load while a plot waits for them (0.20).</summary>
        public int SeedsInHand;
    }

    /// <summary>
    /// Picks the farmer's next action (stateless, like the other jobs): home at night or with a full load; gather what
    /// dropped; harvest what's ripe; plant an empty cultivated plot; cultivate a new one; carry the rest home; else wait
    /// and say why (no Cultivator, no seeds, crops growing).
    /// </summary>
    public static class FarmerBrain
    {
        public static FarmerAction Decide(FarmerSituation s)
        {
            // Seeds he holds for waiting plots aren't harvest; with no plot to plant they go back to the chest (0.20).
            int load = Math.Max(0, s.CarryTotal - (s.PlantablePlot ? Math.Min(Math.Max(0, s.SeedsInHand), s.CarryTotal) : 0));
            if (!s.HasHut) return FarmerAction.Unemployed;
            if (s.StorageFull && load > 0) return FarmerAction.WaitStorageFull;
            if (s.IsNight) return load > 0 ? FarmerAction.Deposit : FarmerAction.Rest;
            if (load >= Math.Max(1, s.CarryCapacity)) return FarmerAction.Deposit;
            if (!s.HasField) return load > 0 ? FarmerAction.Deposit : FarmerAction.WaitNoField;
            if (s.DropsNearby) return FarmerAction.Gather;
            if (s.RipeCrop) return FarmerAction.Harvest;
            if (s.PlantablePlot && s.SeedToPlant) return FarmerAction.Plant;
            if (s.UncultivatedPlot && s.HasCultivator) return FarmerAction.Cultivate;
            if (load > 0) return FarmerAction.Deposit;
            if (s.UncultivatedPlot) return FarmerAction.WaitNeedsCultivator;
            if (s.PlantablePlot) return FarmerAction.WaitNeedsSeeds;
            return FarmerAction.WaitGrowing;
        }
    }

    /// <summary>
    /// How many seeds the farmer takes from the Farm's chest in one visit (worker polish, 0.20: watching a colony, he walked to
    /// the chest for every plot): enough for every plot waiting for that crop, whole plots only (a bush takes its price),
    /// never more than <see cref="Max"/> in hand or than the chest holds.
    /// </summary>
    public static class SeedBatch
    {
        public const int Max = 20;

        /// <summary>With no seed taken (a player has the Farm's chest open, or it is being handed over) he looks again this much later.</summary>
        public const float RetrySeconds = 2f;

        /// <summary>
        /// When he looks over his plots again after a seed fetch that fell short: a little later when he took none, at once
        /// when he took some (another crop may have its seeds). The whole-mod review of 1.4.1: at once every time, he scanned
        /// every plot every frame while a player stood at the chest.
        /// </summary>
        public static float LookAgainIn(int taken) => taken == 0 ? RetrySeconds : 0f;

        public static int ToTake(int plotsWanting, int seedCount, int inChest, int inHand)
        {
            int each = Math.Max(1, seedCount);
            int want = Math.Max(0, plotsWanting * each - Math.Max(0, inHand));
            int room = Math.Max(0, Max - Math.Max(0, inHand));
            int take = Math.Min(want, Math.Min(room, Math.Max(0, inChest)));
            return take - take % each;
        }
    }

    /// <summary>A field's plots: how far apart, when a crop stands on one, and which field a spot belongs to.</summary>
    public static class FieldPlots
    {
        /// <summary>A crop standing this near a plot is on it (his; the plant's growing replaces the planted piece).</summary>
        public const float OnPlotTolerance = 0.4f;
        public const float Spacing = 1.25f;

        private static double Flat(Vec3 a, Vec3 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));

        /// <summary>
        /// The plots of <paramref name="earlier"/>, then those of <paramref name="later"/> not on a kept plot's spot (within
        /// <see cref="OnPlotTolerance"/> twice): each spot belongs to one rule only, the first field's (0.20).
        /// </summary>
        public static List<PlotRule> Merge(IEnumerable<PlotRule> earlier, IEnumerable<PlotRule> later)
        {
            var kept = new List<PlotRule>();
            double near = OnPlotTolerance * 2f;
            foreach (PlotRule rule in (earlier ?? Enumerable.Empty<PlotRule>()).Concat(later ?? Enumerable.Empty<PlotRule>()))
                if (rule != null && !kept.Any(k => Flat(k.At, rule.At) <= near)) kept.Add(rule);
            return kept;
        }

        public static bool OnPlot(Vec3 at, IEnumerable<Vec3> plots) =>
            plots.Any(p => (p.X - at.X) * (p.X - at.X) + (p.Z - at.Z) * (p.Z - at.Z) <= OnPlotTolerance * OnPlotTolerance);
    }

    /// <summary>
    /// The farmer's plots in a grid (0.25, review of 0.20): the plot a thing stands on, and the plots something stands
    /// near, in one look per thing instead of one per plot (a level-5 Farm has about 600 plots). The same answers as
    /// <see cref="FieldPlots.OnPlot"/> and the farmer's "something within twice the tolerance" check.
    /// </summary>
    public sealed class PlotIndex
    {
        private readonly IReadOnlyList<Vec3> _plots;
        private readonly float _tolerance;
        private readonly float _cell;
        private readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();
        private readonly bool[] _taken;

        public PlotIndex(IReadOnlyList<Vec3> plots, float tolerance)
        {
            _plots = plots ?? new List<Vec3>();
            _tolerance = tolerance;
            _cell = System.Math.Max(0.01f, tolerance * 2f);   // both look-ups reach at most one cell over
            _taken = new bool[_plots.Count];
            for (int i = 0; i < _plots.Count; i++)
            {
                long key = Key(Cell(_plots[i].X), Cell(_plots[i].Z));
                if (!_cells.TryGetValue(key, out List<int> list)) _cells[key] = list = new List<int>();
                list.Add(i);
            }
        }

        public int Count => _plots.Count;

        /// <summary>The nearest plot within the tolerance of this point (as FieldPlots.OnPlot), or -1.</summary>
        public int IndexOf(Vec3 at)
        {
            int best = -1;
            float bestSq = _tolerance * _tolerance;
            int cx = Cell(at.X), cz = Cell(at.Z);
            for (int x = cx - 1; x <= cx + 1; x++)
                for (int z = cz - 1; z <= cz + 1; z++)
                {
                    if (!_cells.TryGetValue(Key(x, z), out List<int> list)) continue;
                    foreach (int i in list)
                    {
                        Vec3 p = _plots[i];
                        float sq = (p.X - at.X) * (p.X - at.X) + (p.Z - at.Z) * (p.Z - at.Z);
                        if (sq <= bestSq && (best < 0 || sq < bestSq || i < best))
                        {
                            best = i;
                            bestSq = sq;
                        }
                    }
                }
            return best;
        }

        /// <summary>Something stands here: every plot within twice the tolerance is taken (or resting).</summary>
        public void MarkStanding(Vec3 at)
        {
            double reach = _tolerance * 2.0;
            int cx = Cell(at.X), cz = Cell(at.Z);
            for (int x = cx - 1; x <= cx + 1; x++)
                for (int z = cz - 1; z <= cz + 1; z++)
                {
                    if (!_cells.TryGetValue(Key(x, z), out List<int> list)) continue;
                    foreach (int i in list)
                    {
                        Vec3 p = _plots[i];
                        double dx = p.X - at.X, dz = p.Z - at.Z;
                        if (dx * dx + dz * dz <= reach * reach) _taken[i] = true;
                    }
                }
        }

        public bool IsTaken(int plot) => plot >= 0 && plot < _taken.Length && _taken[plot];

        public void ClearStanding() => System.Array.Clear(_taken, 0, _taken.Length);

        private int Cell(float v) => (int)System.Math.Floor(v / _cell);

        private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
    }

    /// <summary>A plant the Cultivator can plant, as the game describes it (the game side reads these from its data).</summary>
    public sealed class PlantInfo
    {
        public PlantInfo(string piece, string seed, string nameToken, string grown, string yieldItem, bool isPickable, int biomes, float growRadius,
            bool isVine = false, bool isFood = false, int seedCount = 1)
        {
            SeedCount = System.Math.Max(1, seedCount);
            IsVine = isVine;
            IsFood = isFood;
            Piece = piece;
            Seed = seed;
            NameToken = nameToken;
            Grown = grown;
            YieldItem = yieldItem;
            IsPickable = isPickable;
            Biomes = biomes;
            GrowRadius = growRadius;
        }

        public string Piece { get; }
        public string Seed { get; }
        public string NameToken { get; }
        public string Grown { get; }
        public string YieldItem { get; }
        /// <summary>It grows into something picked (a crop), not a tree.</summary>
        public bool IsPickable { get; }
        public int Biomes { get; }
        public float GrowRadius { get; }
        /// <summary>A vine climbing something (Ashlands vineberries): not for a field's plots.</summary>
        public bool IsVine { get; }
        /// <summary>What it gives can be eaten.</summary>
        public bool IsFood { get; }
        /// <summary>How many of the seed planting it takes: 1 for a crop, a bush's price for a bush (0.19).</summary>
        public int SeedCount { get; }
    }

    /// <summary>A crop the farmer can grow: plant this piece with that seed, it grows into a crop that gives this item.</summary>
    public sealed class CropInfo
    {
        public CropInfo(string piece, string seed, string grown, string yield, string nameToken, int biomes, float growRadius, bool isFood = false,
            int seedCount = 1)
        {
            SeedCount = System.Math.Max(1, seedCount);
            IsFood = isFood;
            Piece = piece;
            Seed = seed;
            Grown = grown;
            Yield = yield;
            NameToken = nameToken;
            Biomes = biomes;
            GrowRadius = growRadius;
        }

        public string Piece { get; }
        public string Seed { get; }
        public string Grown { get; }
        public string Yield { get; }
        public string NameToken { get; }
        /// <summary>The game's biome flags it grows in; 0 for anywhere.</summary>
        public int Biomes { get; }
        public float GrowRadius { get; }
        /// <summary>What it gives can be eaten (a carrot, not carrot seeds).</summary>
        public bool IsFood { get; }
        /// <summary>
        /// How many of the seed planting it takes: 1 for a crop; for a bush a planting mod makes plantable (PlantEverything:
        /// planted once, picked each time it regrows), its price, as 5 raspberries (0.19).
        /// </summary>
        public int SeedCount { get; }

        /// <summary>A bush a planting mod places as it is (it is its own crop): off on the Farm's list until the player ticks it.</summary>
        public bool IsBush => Piece == Grown;
    }

    /// <summary>The crops: every Cultivator plant that grows into something picked, with a seed and a crop.</summary>
    public static class CropCatalog
    {
        public static IEnumerable<CropInfo> Crops(IEnumerable<PlantInfo> plants) =>
            plants
                .Where(p => p != null && p.IsPickable && !p.IsVine && !string.IsNullOrEmpty(p.Seed) && !string.IsNullOrEmpty(p.Grown) && !string.IsNullOrEmpty(p.YieldItem))
                .GroupBy(p => p.Piece, StringComparer.Ordinal)
                .Select(g => g.First())
                .Select(p => new CropInfo(p.Piece, p.Seed, p.Grown, p.YieldItem, p.NameToken, p.Biomes, p.GrowRadius, p.IsFood, p.SeedCount))
                .ToList();
    }

    /// <summary>One line of the Farm's "What to grow" list.</summary>
    public sealed class CropLine
    {
        public CropInfo Crop;
        /// <summary>It grows where the Farm stands.</summary>
        public bool Offered;
        public bool Ticked;
    }

    /// <summary>The Farm's list: every crop that grows here, ticked until the player unticks it; planted in turn.</summary>
    public static class CropChoice
    {
        public static List<CropLine> Lines(IEnumerable<CropInfo> crops, int hutBiome, ICollection<string> unticked) =>
            crops.Select(c =>
            {
                bool grows = c.Biomes == 0 || hutBiome == 0 || (c.Biomes & hutBiome) != 0;
                return new CropLine { Crop = c, Offered = grows, Ticked = grows && unticked.Contains(Entry(c)) == c.IsBush };
            }).ToList();

        /// <summary>
        /// The name a change to this crop is saved under: a crop unticked by its name, a bush ticked as "+name" (review of 0.19:
        /// ticked from the start, the Farm turned the colony's raspberries into bushes that repay them only after weeks).
        /// </summary>
        public static string Entry(CropInfo crop) => crop.IsBush ? "+" + crop.Piece : crop.Piece;

        /// <summary>Whether the crop's entry is in the saved list once it is <paramref name="ticked"/> (or not).</summary>
        public static bool EntrySaved(CropInfo crop, bool ticked) => crop.IsBush ? ticked : !ticked;

        /// <summary>A seed crop is planted while the Farm has fewer than this of the seed it gives.</summary>
        public const int SeedReserve = 6;

        /// <summary>
        /// A seed crop (it grows into another crop's seed and nothing to eat, as the carrot-seed crop) is planted only
        /// while that seed runs low in the Farm (<paramref name="stock"/>); any other crop always (review of 0.12: seed
        /// crops ate the carrots). A food crop always counts as food, though a carrot is also a seed.
        /// </summary>
        public static bool WorthPlanting(CropInfo crop, IEnumerable<CropInfo> crops, Func<string, int> stock) =>
            crop.IsFood || !crops.Any(c => c != crop && c.Seed == crop.Yield) || stock(crop.Yield) < SeedReserve;

        /// <summary>The next ticked crop, from <paramref name="turn"/> on, whose seed is at hand; null if none (turn unchanged).</summary>
        public static CropInfo Next(IReadOnlyList<CropInfo> ticked, Func<string, bool> hasSeed, ref int turn)
        {
            int count = ticked.Count;
            for (int i = 0; i < count; i++)
            {
                int index = ((turn % count) + count + i) % count;
                if (!hasSeed(ticked[index].Seed)) continue;
                turn = index + 1;
                return ticked[index];
            }
            return null;
        }
    }
    /// <summary>
    /// A plot where the crop can't be planted waits 10 minutes, then 30, then an hour each time it fails again (watching
    /// after 0.21 and 0.22: the farmer fetched a fresh seed for the same plot every 10 minutes).
    /// </summary>
    public static class PlotRetry
    {
        public static float Seconds(int failures) => failures >= 3 ? 3600f : failures == 2 ? 1800f : 600f;
    }
}
