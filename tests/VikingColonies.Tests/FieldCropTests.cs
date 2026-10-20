using System.Collections.Generic;
using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>Worker polish (0.20): a field's crop list: the Farm's list first, then each crop, greyed where it doesn't grow.</summary>
    public class FieldCropTests
    {
        private const int Meadows = 1, BlackForest = 8, Plains = 16;
        private static readonly CropInfo Carrot = new CropInfo("sapling_carrot", "CarrotSeeds", "Pickable_Carrot", "Carrot", "$item_carrot", BlackForest | Meadows | Plains, 0.5f, isFood: true);
        private static readonly CropInfo Barley = new CropInfo("sapling_barley", "Barley", "Pickable_Barley", "Barley", "$item_barley", Plains, 0.5f);
        private static readonly List<CropInfo> Crops = new List<CropInfo> { Carrot, Barley };

        [Fact]
        public void The_farms_list_comes_first_and_always_grows()
        {
            List<FieldCropLine> lines = FieldCrops.Lines(Crops, Meadows, "");
            Assert.Equal(3, lines.Count);
            Assert.Equal("", lines[0].Key);
            Assert.Null(lines[0].Crop);
            Assert.True(lines[0].Grows);
            Assert.True(lines[0].Chosen);
        }

        [Fact]
        public void A_crop_that_doesnt_grow_there_is_offered_greyed()
        {
            List<FieldCropLine> lines = FieldCrops.Lines(Crops, Meadows, "");
            Assert.False(lines.Single(l => l.Key == "sapling_barley").Grows);
            Assert.True(lines.Single(l => l.Key == "sapling_carrot").Grows);
        }

        [Fact]
        public void The_chosen_crop_is_ticked()
        {
            List<FieldCropLine> lines = FieldCrops.Lines(Crops, Meadows, "sapling_carrot");
            Assert.Equal(new[] { "sapling_carrot" }, lines.Where(l => l.Chosen).Select(l => l.Key));
        }

        [Fact]
        public void An_unknown_saved_crop_falls_back_to_the_farms_list()
        {
            List<FieldCropLine> lines = FieldCrops.Lines(Crops, Meadows, "sapling_from_a_mod_gone");
            Assert.Equal(new[] { "" }, lines.Where(l => l.Chosen).Select(l => l.Key));
        }
    }
}
