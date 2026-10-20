using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class EraTableTests
    {
        private static PieceCost Cost(string station, params string[] materials) => new PieceCost(station, materials);

        [Fact]
        public void Materials_belong_to_the_era_that_brings_them()
        {
            Assert.Equal(Era.Meadows, EraTable.OfItem("Wood"));
            Assert.Equal(Era.BlackForest, EraTable.OfItem("RoundLog"));
            Assert.Equal(Era.Swamp, EraTable.OfItem("Iron"));
            Assert.Equal(Era.Mountains, EraTable.OfItem("Crystal"));
            Assert.Equal(Era.Plains, EraTable.OfItem("Tar"));
            Assert.Equal(Era.Mistlands, EraTable.OfItem("BlackMarble"));
            Assert.Equal(Era.Ashlands, EraTable.OfItem("Grausten"));
            Assert.Equal(Era.DeepNorth, EraTable.OfItem("Frostwood"));
            Assert.Equal(Era.None, EraTable.OfItem("MyModPlank"));
        }

        [Fact]
        public void Stone_pieces_belong_to_the_iron_age_because_the_stonecutter_needs_iron()
        {
            Assert.Equal(Era.Swamp, EraTable.OfPiece(Cost("piece_stonecutter", "Stone")));
            Assert.Equal(Era.Meadows, EraTable.OfPiece(Cost("piece_workbench", "Wood")));
            Assert.Equal(Era.Plains, EraTable.OfPiece(Cost("piece_workbench", "Wood", "Tar")));
        }

        [Fact]
        public void Each_level_covers_a_band_of_eras()
        {
            Assert.Equal(Era.Meadows, EraTable.BandLast(1));
            Assert.Equal(Era.BlackForest, EraTable.BandFirst(2));
            Assert.Equal(Era.Swamp, EraTable.BandLast(2));
            Assert.Equal(Era.Mountains, EraTable.BandFirst(3));
            Assert.Equal(Era.Plains, EraTable.BandLast(3));
            Assert.Equal(Era.Mistlands, EraTable.BandLast(4));
            Assert.Equal(Era.Ashlands, EraTable.BandLast(5));
        }

        [Fact]
        public void A_design_that_uses_nothing_from_its_bands_first_era_gets_a_warning()
        {
            string warning = EraTable.Warning(3, new[] { Cost("piece_stonecutter", "Stone"), Cost("piece_workbench", "Wood") });
            Assert.NotNull(warning);
            Assert.Contains("the Mountains", warning);
            Assert.NotNull(EraTable.Warning(2, new[] { Cost("piece_workbench", "Wood") }));
        }

        [Fact]
        public void A_design_that_uses_its_bands_first_era_or_later_gets_none()
        {
            Assert.Null(EraTable.Warning(3, new[] { Cost("piece_stonecutter", "Stone"), Cost("piece_workbench", "Wood", "Tar") }));
            Assert.Null(EraTable.Warning(2, new[] { Cost("forge", "Wood", "Iron") }));
            Assert.Null(EraTable.Warning(2, new[] { Cost("piece_stonecutter", "BlackMarble") }));
        }

        [Fact]
        public void A_level_2_design_of_core_wood_opens_in_the_bronze_age()
        {
            // The review's "bronze gap": level 2 needed iron, so through the whole Black Forest age a colony's lumberjack
            // couldn't use a bronze axe on the birches and oaks of the Meadows. A level opens with its band's first era.
            Assert.Null(EraTable.Warning(2, new[] { Cost("piece_workbench", "RoundLog"), Cost("piece_workbench", "Wood") }));
            Assert.Equal(Era.BlackForest, EraTable.Opens(2));
        }

        [Fact]
        public void Level_1_designs_and_designs_of_only_modded_materials_get_no_warning()
        {
            Assert.Null(EraTable.Warning(1, new[] { Cost("piece_workbench", "Wood") }));
            Assert.Null(EraTable.Warning(4, new[] { Cost("mod_station", "MyModPlank") }));
        }
    }
}
