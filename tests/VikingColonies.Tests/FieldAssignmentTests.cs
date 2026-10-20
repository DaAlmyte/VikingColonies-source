using System.Collections.Generic;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>Worker polish (0.20): which Farm works a field; one field per Farm level, as in MineColonies.</summary>
    public class FieldAssignmentTests
    {
        private static FarmSlot Farm(string id, float x, int level, int assigned, bool finished = true) =>
            new FarmSlot { Id = id, At = new Vec3(x, 0, 0), Level = level, Assigned = assigned, Finished = finished };

        [Fact]
        public void Capacity_is_the_level()
        {
            Assert.Equal(1, FieldAssignment.Capacity(1));
            Assert.Equal(3, FieldAssignment.Capacity(3));
            Assert.Equal(5, FieldAssignment.Capacity(5));
            Assert.Equal(1, FieldAssignment.Capacity(0));
            Assert.Equal(5, FieldAssignment.Capacity(9));
        }

        [Fact]
        public void A_new_field_goes_to_the_nearest_farm_with_room()
        {
            var farms = new List<FarmSlot> { Farm("near", 5, 1, 1), Farm("far", 30, 2, 1) };
            Assert.Equal("far", FieldAssignment.Choose(farms, new Vec3(0, 0, 0)));
            Assert.False(FieldAssignment.HasRoom(farms[0]));
            Assert.True(FieldAssignment.HasRoom(farms[1]));
        }

        // Final review of 0.20: a Farm being upgraded refused fields ("it works all the fields it has room for").
        [Fact]
        public void A_farm_being_upgraded_works_its_fields()
        {
            Assert.True(FieldAssignment.Works(isBuilt: false, isUpgrading: true));
            Assert.True(FieldAssignment.Works(isBuilt: true, isUpgrading: false));
            Assert.False(FieldAssignment.Works(isBuilt: false, isUpgrading: false));
        }

        [Fact]
        public void An_unfinished_farm_gets_no_field()
        {
            var farms = new List<FarmSlot> { Farm("building", 5, 1, 0, finished: false) };
            Assert.Equal("", FieldAssignment.Choose(farms, new Vec3(0, 0, 0)));
        }

        [Fact]
        public void No_farm_with_room_leaves_it_unassigned()
        {
            var farms = new List<FarmSlot> { Farm("full", 5, 1, 1) };
            Assert.Equal("", FieldAssignment.Choose(farms, new Vec3(0, 0, 0)));
            Assert.Equal("", FieldAssignment.Choose(new List<FarmSlot>(), new Vec3(0, 0, 0)));
        }

        [Fact]
        public void Fields_of_a_gone_farm_go_to_one_with_room()
        {
            // The field's own Farm isn't among those standing: the next one with room takes it.
            var farms = new List<FarmSlot> { Farm("other", 12, 1, 0) };
            Assert.Equal("other", FieldAssignment.Choose(farms, new Vec3(0, 0, 0)));
        }
    }
}
