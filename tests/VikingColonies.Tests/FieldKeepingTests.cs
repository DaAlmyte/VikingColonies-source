using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>
    /// Final review of 0.20: what a field marker does about its Farm. "Leave" was undone within 5 s by auto-assign, and a
    /// Farm merely not loaded on this computer was taken for gone (its fields given away).
    /// </summary>
    public class FieldKeepingTests
    {
        [Fact]
        public void A_field_taken_from_its_farm_by_hand_stays_without_one()
        {
            Assert.Equal(FieldFarmStep.Keep, FieldKeeping.Next(farmSet: false, farmThere: false, farmGroundLoaded: true,
                missingLongEnough: false, overCapacity: false, byHand: true));
        }

        [Fact]
        public void A_field_without_a_farm_looks_for_one()
        {
            Assert.Equal(FieldFarmStep.Choose, FieldKeeping.Next(false, false, true, false, false, byHand: false));
        }

        [Fact]
        public void A_missing_farm_whose_ground_isnt_loaded_is_waited_for()
        {
            Assert.Equal(FieldFarmStep.Wait, FieldKeeping.Next(farmSet: true, farmThere: false, farmGroundLoaded: false,
                missingLongEnough: true, overCapacity: false, byHand: true));
        }

        [Fact]
        public void A_farm_missing_a_moment_is_waited_for()
        {
            Assert.Equal(FieldFarmStep.Wait, FieldKeeping.Next(true, false, true, missingLongEnough: false, overCapacity: false, byHand: false));
        }

        [Fact]
        public void A_farm_gone_where_its_ground_is_loaded_lets_its_field_go_even_one_assigned_by_hand()
        {
            Assert.Equal(FieldFarmStep.FarmGone, FieldKeeping.Next(true, false, true, missingLongEnough: true, overCapacity: false, byHand: true));
        }

        [Fact]
        public void A_field_beyond_its_farms_room_leaves_it_and_one_within_stays()
        {
            Assert.Equal(FieldFarmStep.Leave, FieldKeeping.Next(true, true, true, false, overCapacity: true, byHand: false));
            Assert.Equal(FieldFarmStep.Keep, FieldKeeping.Next(true, true, true, false, overCapacity: false, byHand: false));
        }
    }
}
