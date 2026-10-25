using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>
    /// The designs the mod ships (1.4: the new huts, levels 1 to 5, and the Town Hall's camp, made by tools/blueprints/vcbp
    /// and judged by vcbp's checks): each reads cleanly, marks its chest once, lays out round it, and has a door (the
    /// camp is open); no two of its pieces stand in the same spot.
    /// </summary>
    public class ShippedBlueprintTests
    {
        private static readonly string[] Huts = { "builder_hut", "lumberjack_hut", "forager_hut", "farm", "longhouse", "watchtower", "townhall", "storehouse" };
        private static readonly Dictionary<string, string> Chests = new Dictionary<string, string>
        {
            { "builder_hut", "vc_hut_builder" }, { "lumberjack_hut", "vc_hut_lumberjack" }, { "forager_hut", "vc_hut_forager" },
            { "farm", "vc_hut_farm" }, { "longhouse", "vc_hut_longhouse" }, { "watchtower", "vc_hut_watchtower" }, { "storehouse", "vc_hut_storehouse" },
            { "townhall", SaveKeys.TownHallPiece },
        };

        public static IEnumerable<object[]> Designs() =>
            from hut in Huts from level in Enumerable.Range(1, 5) select new object[] { hut, level };

        private static string Folder()
        {
            string? dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "VikingColonies.slnx"))) dir = Path.GetDirectoryName(dir);
            Assert.NotNull(dir);
            return Path.Combine(dir!, "src", "VikingColonies", "Blueprints");
        }

        private static BlueprintFile Load(string hut, int level) =>
            BlueprintFile.Parse(File.ReadAllLines(Path.Combine(Folder(), (level == 1 ? hut : $"{hut}_{level}") + ".blueprint")));

        [Theory]
        [MemberData(nameof(Designs))]
        public void Each_design_reads_cleanly_marks_its_chest_once_and_lays_out_round_it(string hut, int level)
        {
            BlueprintFile blueprint = Load(hut, level);
            Assert.Empty(blueprint.Problems);
            Assert.Single(blueprint.Pieces, p => p.Name == Chests[hut]);
            Assert.True(BlueprintPlan.TryPlace(blueprint, Chests[hut], Vec3.Zero, Quat.Identity, out List<PlannedPiece> plan, out string problem), problem);
            Assert.NotEmpty(plan);
            // No two the same in one spot, turned the same way (a gable's crossed beams share a spot, turned opposite ways).
            Assert.Equal(plan.Count, plan.Select(p => $"{p.Prefab} {p.Position} {p.Rotation.X:0.00} {p.Rotation.Y:0.00} {p.Rotation.Z:0.00} {p.Rotation.W:0.00}").Distinct().Count());
        }

        [Theory]
        [MemberData(nameof(Designs))]
        public void Each_hut_has_a_door_and_the_camp_is_open(string hut, int level)
        {
            BlueprintPlan.TryPlace(Load(hut, level), Chests[hut], Vec3.Zero, Quat.Identity, out List<PlannedPiece> plan, out _);
            bool door = plan.Any(p => p.Prefab == "wood_door" || p.Prefab == "wood_gate");
            Assert.Equal(hut != "townhall", door);
        }

        [Fact]
        public void Every_hut_has_its_five_levels_and_nothing_else_is_shipped()
        {
            var expected = new HashSet<string>(Designs().Select(d => (int)d[1] == 1 ? (string)d[0] : $"{d[0]}_{d[1]}"));
            var shipped = new HashSet<string>(Directory.GetFiles(Folder(), "*.blueprint").Select(Path.GetFileNameWithoutExtension)!);
            Assert.Equal(expected.OrderBy(x => x), shipped.OrderBy(x => x));
        }
    }
}
