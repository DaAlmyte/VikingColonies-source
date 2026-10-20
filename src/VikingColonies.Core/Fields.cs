using System;
using System.Collections.Generic;
using System.Linq;

namespace VikingColonies.Core
{
    /// <summary>
    /// Worker polish (0.20): a field the player marks with a field marker (MineColonies' farmer works fields marked with a
    /// scarecrow): a square of plots round the marker. Its sizes, plots per side.
    /// </summary>
    public static class FieldSizes
    {
        /// <summary>Up to 11×11 (0.20.1), MineColonies' largest field.</summary>
        public static readonly int[] All = { 3, 5, 7, 9, 11 };
        /// <summary>A new marker's field.</summary>
        public const int New = 5;

        /// <summary>A saved size made one of <see cref="All"/>: odd and between 3 and 11; nonsense (below 3) is a new field's.</summary>
        public static int Valid(int size)
        {
            if (size < All[0]) return New;
            if (size > All[All.Length - 1]) return All[All.Length - 1];
            return size % 2 == 1 ? size : size + 1;
        }

        public static int Bigger(int size) => All.FirstOrDefault(s => s > Valid(size)) is int bigger && bigger > 0 ? bigger : All[All.Length - 1];

        public static int Smaller(int size) => All.LastOrDefault(s => s < Valid(size)) is int smaller && smaller > 0 ? smaller : All[0];
    }

    /// <summary>A field's plots: <c>size × size</c> points a plot apart, turned with the marker, the marker's own left out.</summary>
    public static class FieldGrid
    {
        public static List<Vec3> Plots(Vec3 centre, Quat rotation, int size, float spacing)
        {
            int half = FieldSizes.Valid(size) / 2;
            var plots = new List<Vec3>();
            for (int i = -half; i <= half; i++)
                for (int j = -half; j <= half; j++)
                    if (i != 0 || j != 0) plots.Add(centre + rotation.Rotate(new Vec3(i * spacing, 0f, j * spacing)));
            return plots;
        }

        /// <summary>The field's outline: half a plot outside its outer plots (for showing its square on the ground).</summary>
        public static Vec3[] Corners(Vec3 centre, Quat rotation, int size, float spacing)
        {
            float half = (FieldSizes.Valid(size) / 2 + 0.5f) * spacing;
            return new Footprint(centre, rotation, half, half).Corners();
        }
    }

    /// <summary>One plot the farmer works and what grows there: <see cref="Crop"/> "" is the Farm's list (in turn).</summary>
    public sealed class PlotRule
    {
        public Vec3 At;
        /// <summary>The planted piece of the crop for this plot; "" = the Farm's ticked crops in turn.</summary>
        public string Crop = "";
        /// <summary>The field marker's id.</summary>
        public string FieldId = "";
    }

    /// <summary>A Farm as field assignment sees it: where it stands, its level, how many fields it works, whether it stands.</summary>
    public sealed class FarmSlot
    {
        public string Id = "";
        public Vec3 At;
        public int Level;
        public int Assigned;
        public bool Finished;
    }

    /// <summary>Which Farm works a field: one field per Farm level (MineColonies' numbers, 1 to 5).</summary>
    public static class FieldAssignment
    {
        public static int Capacity(int level) => Math.Max(1, Math.Min(HutLevels.MaxLevel, level));

        /// <summary>
        /// A Farm works fields once it is built, and while it is being upgraded (its farmer works on, as for hiring; final
        /// review of 0.20: an upgrading Farm refused fields).
        /// </summary>
        public static bool Works(bool isBuilt, bool isUpgrading) => isBuilt || isUpgrading;

        public static bool HasRoom(FarmSlot farm) => farm != null && farm.Finished && farm.Assigned < Capacity(farm.Level);

        /// <summary>The nearest standing Farm with room for one more field; "" when none.</summary>
        public static string Choose(IReadOnlyList<FarmSlot> farms, Vec3 field)
        {
            FarmSlot best = null;
            double bestDistance = double.MaxValue;
            foreach (FarmSlot farm in farms ?? new List<FarmSlot>())
            {
                if (!HasRoom(farm)) continue;
                double d = Math.Sqrt((farm.At.X - field.X) * (farm.At.X - field.X) + (farm.At.Z - field.Z) * (farm.At.Z - field.Z));
                if (d >= bestDistance) continue;
                best = farm;
                bestDistance = d;
            }
            return best != null ? best.Id : "";
        }
    }

    /// <summary>One line of a field's crop list: <see cref="Key"/> "" is the Farm's list (<see cref="Crop"/> null).</summary>
    public sealed class FieldCropLine
    {
        public string Key = "";
        public CropInfo Crop;
        /// <summary>It grows where the field is (its biome).</summary>
        public bool Grows;
        public bool Chosen;
    }

    /// <summary>A field's crop list: the Farm's list first (it always grows), then each crop, greyed where it doesn't grow.</summary>
    public static class FieldCrops
    {
        public static List<FieldCropLine> Lines(IEnumerable<CropInfo> crops, int fieldBiome, string chosen)
        {
            List<CropInfo> all = (crops ?? Enumerable.Empty<CropInfo>()).ToList();
            string pick = all.Any(c => c.Piece == chosen) ? chosen : "";   // a crop a mod no longer gives: the Farm's list
            var lines = new List<FieldCropLine> { new FieldCropLine { Key = "", Crop = null, Grows = true, Chosen = pick.Length == 0 } };
            foreach (CropInfo crop in all)
                lines.Add(new FieldCropLine
                {
                    Key = crop.Piece,
                    Crop = crop,
                    Grows = crop.Biomes == 0 || fieldBiome == 0 || (crop.Biomes & fieldBiome) != 0,
                    Chosen = crop.Piece == pick && pick.Length > 0,
                });
            return lines;
        }
    }

    /// <summary>What a field marker does about its Farm, on each of its owner's checks.</summary>
    public enum FieldFarmStep { Keep, Wait, Leave, FarmGone, Choose }

    /// <summary>
    /// A field marker and its Farm (final review of 0.20). A field the player took from its Farm, or gave one by hand, is
    /// left as he set it: auto-assign only moves fields nobody chose for (it undid "Leave" within 5 s). A missing Farm is
    /// judged gone only where its ground is loaded on this computer (an unloaded one gave its fields away), and then the
    /// field may go to another Farm by itself, whoever chose its Farm.
    /// </summary>
    public static class FieldKeeping
    {
        /// <param name="farmSet">It has a Farm.</param>
        /// <param name="farmThere">That Farm is loaded here, in its colony.</param>
        /// <param name="farmGroundLoaded">The ground where that Farm stands (or the colony's, when it wasn't seen) is loaded here.</param>
        /// <param name="missingLongEnough">The Farm was missing, with its ground loaded, on enough checks in a row.</param>
        /// <param name="overCapacity">More fields than the Farm has room for, and this one among the last.</param>
        /// <param name="byHand">The player assigned it, or took it from its Farm.</param>
        public static FieldFarmStep Next(bool farmSet, bool farmThere, bool farmGroundLoaded, bool missingLongEnough, bool overCapacity, bool byHand)
        {
            if (!farmSet) return byHand ? FieldFarmStep.Keep : FieldFarmStep.Choose;
            if (farmThere) return overCapacity ? FieldFarmStep.Leave : FieldFarmStep.Keep;
            return farmGroundLoaded && missingLongEnough ? FieldFarmStep.FarmGone : FieldFarmStep.Wait;
        }
    }
}
