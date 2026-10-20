using System;
using System.Collections.Generic;
using System.Linq;

namespace VikingColonies.Core
{
    /// <summary>Valheim's eras in the order the game opens them (the numbers only order them).</summary>
    public enum Era { None = 0, Meadows = 1, BlackForest = 2, Swamp = 3, Mountains = 4, Plains = 5, Mistlands = 6, Ashlands = 7, DeepNorth = 8 }

    /// <summary>What a building piece costs: the crafting station it needs and its materials (item prefab names).</summary>
    public sealed class PieceCost
    {
        public PieceCost(string station, IEnumerable<string> materials)
        {
            Station = station ?? "";
            Materials = materials != null ? materials.ToList() : new List<string>();
        }

        public string Station { get; }
        public IReadOnlyList<string> Materials { get; }
    }

    /// <summary>
    /// Which era vanilla materials and crafting stations belong to, the band of eras each hut level covers, and the
    /// gentle warning for a saved design that uses nothing from the era its level opens with (its band's first). Modded
    /// or unknown materials have no era, so they never cause a warning.
    /// A level opens with its band's first era, so its tools are there for the whole band. Seen in the 2026-10 review:
    /// level 2 opened with iron (its band's last era), and through the whole Black Forest age a colony couldn't use a
    /// bronze axe on the birches and oaks of the Meadows (the "bronze gap").
    /// </summary>
    public static class EraTable
    {
        private static readonly Dictionary<string, Era> s_items = new Dictionary<string, Era>(StringComparer.OrdinalIgnoreCase)
        {
            { "Wood", Era.Meadows }, { "Stone", Era.Meadows }, { "Flint", Era.Meadows }, { "Resin", Era.Meadows },
            { "LeatherScraps", Era.Meadows }, { "DeerHide", Era.Meadows }, { "Raspberry", Era.Meadows }, { "Dandelion", Era.Meadows },
            { "Feathers", Era.Meadows }, { "Honey", Era.Meadows },
            { "RoundLog", Era.BlackForest }, { "FineWood", Era.BlackForest }, { "Copper", Era.BlackForest }, { "Tin", Era.BlackForest },
            { "Bronze", Era.BlackForest }, { "BronzeNails", Era.BlackForest }, { "GreydwarfEye", Era.BlackForest }, { "Coal", Era.BlackForest },
            { "SurtlingCore", Era.BlackForest }, { "Thistle", Era.BlackForest }, { "Blueberries", Era.BlackForest }, { "TrollHide", Era.BlackForest },
            { "BoneFragments", Era.BlackForest },
            { "Iron", Era.Swamp }, { "IronNails", Era.Swamp }, { "ElderBark", Era.Swamp }, { "Guck", Era.Swamp }, { "Chain", Era.Swamp },
            { "Bloodbag", Era.Swamp }, { "Ooze", Era.Swamp }, { "WitheredBone", Era.Swamp }, { "Turnip", Era.Swamp },
            { "Silver", Era.Mountains }, { "Obsidian", Era.Mountains }, { "Crystal", Era.Mountains }, { "WolfPelt", Era.Mountains },
            { "WolfClaw", Era.Mountains }, { "WolfFang", Era.Mountains }, { "FreezeGland", Era.Mountains }, { "Onion", Era.Mountains },
            { "BlackMetal", Era.Plains }, { "Tar", Era.Plains }, { "LinenThread", Era.Plains }, { "Needle", Era.Plains },
            { "LoxPelt", Era.Plains }, { "Barley", Era.Plains }, { "Flax", Era.Plains }, { "Cloudberry", Era.Plains },
            { "BlackMarble", Era.Mistlands }, { "YggdrasilWood", Era.Mistlands }, { "Eitr", Era.Mistlands }, { "BlackCore", Era.Mistlands },
            { "ScaleHide", Era.Mistlands }, { "Carapace", Era.Mistlands }, { "JuteRed", Era.Mistlands }, { "JuteBlue", Era.Mistlands },
            { "MechanicalSpring", Era.Mistlands }, { "Softtissue", Era.Mistlands },
            { "Blackwood", Era.Ashlands }, { "Grausten", Era.Ashlands }, { "FlametalNew", Era.Ashlands }, { "Flametal", Era.Ashlands },
            { "CharredBone", Era.Ashlands }, { "AskHide", Era.Ashlands }, { "Charredskull", Era.Ashlands }, { "CharcoalResin", Era.Ashlands },
            { "CelestialFeather", Era.Ashlands }, { "MoltenCore", Era.Ashlands },
            { "Frostwood", Era.DeepNorth }, { "Gold", Era.DeepNorth }, { "NornThread", Era.DeepNorth }, { "MooseHide", Era.DeepNorth },
            { "Ice", Era.DeepNorth }, { "FrostCore", Era.DeepNorth },
        };

        private static readonly Dictionary<string, Era> s_stations = new Dictionary<string, Era>(StringComparer.OrdinalIgnoreCase)
        {
            { "piece_workbench", Era.Meadows }, { "forge", Era.BlackForest }, { "piece_stonecutter", Era.Swamp },
            { "piece_artisanstation", Era.Plains }, { "blackforge", Era.Mistlands }, { "piece_magetable", Era.Mistlands },
        };

        private static readonly Era[] s_bandFirst = { Era.None, Era.Meadows, Era.BlackForest, Era.Mountains, Era.Mistlands, Era.Ashlands };
        private static readonly Era[] s_bandLast = { Era.None, Era.Meadows, Era.Swamp, Era.Plains, Era.Mistlands, Era.Ashlands };

        public static Era OfItem(string item) => item != null && s_items.TryGetValue(item, out Era era) ? era : Era.None;

        public static Era OfStation(string station) => station != null && s_stations.TryGetValue(station, out Era era) ? era : Era.None;

        /// <summary>The latest era among the piece's station and materials: the earliest it can be built.</summary>
        public static Era OfPiece(PieceCost cost)
        {
            Era latest = OfStation(cost.Station);
            foreach (string material in cost.Materials)
            {
                Era era = OfItem(material);
                if ((int)era > (int)latest) latest = era;
            }
            return latest;
        }

        public static Era BandFirst(int level) => s_bandFirst[HutLevels.Clamp(level)];

        public static Era BandLast(int level) => s_bandLast[HutLevels.Clamp(level)];

        /// <summary>The era a level opens with: its band's first, so the level's tools are there for the whole band.</summary>
        public static Era Opens(int level) => BandFirst(level);

        /// <summary>
        /// Null, or a note that a level-<paramref name="level"/> design uses nothing from the era it opens with (so it can be
        /// built before that progression). Pieces with no known era don't count either way.
        /// </summary>
        public static string Warning(int level, IEnumerable<PieceCost> pieces)
        {
            level = HutLevels.Clamp(level);
            if (level <= 1) return null;
            Era need = Opens(level);
            int latest = 0;
            foreach (PieceCost cost in pieces) latest = Math.Max(latest, (int)OfPiece(cost));
            if (latest == 0 || latest >= (int)need) return null;
            return $"this level-{level} design uses nothing from {Name(need)}, so it can be built before that progression (saved anyway).";
        }

        public static string Name(Era era)
        {
            switch (era)
            {
                case Era.Meadows: return "the Meadows";
                case Era.BlackForest: return "the Black Forest";
                case Era.Swamp: return "the Swamp";
                case Era.Mountains: return "the Mountains";
                case Era.Plains: return "the Plains";
                case Era.Mistlands: return "the Mistlands";
                case Era.Ashlands: return "the Ashlands";
                case Era.DeepNorth: return "the Deep North";
                default: return "no era";
            }
        }
    }
}
