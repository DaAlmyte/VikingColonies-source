using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>Couriers' rules (1.5, the storehouse's part 2): docs/superpowers/specs/2026-10-21-vikingcolonies-storehouse-design.md.</summary>
    public class CourierTests
    {
        [Fact]
        public void Couriers_by_level_are_the_guards_count()
        {
            Assert.Equal(new[] { 1, 1, 2, 2, 3 }, Enumerable.Range(1, 5).Select(CourierRules.Couriers));
        }

        [Fact]
        public void A_courier_carries_50_at_level_1_and_25_more_a_level()
        {
            Assert.Equal(50, CourierRules.Load(1, 50, 25));
            Assert.Equal(150, CourierRules.Load(5, 50, 25));
            Assert.Equal(150, CourierRules.Load(9, 50, 25));   // no level past 5
        }

        [Fact]   // Review Focus 4: a storehouse with no courier yet changes nothing
        public void Free_time_carrying_stops_only_once_a_courier_is_hired()
        {
            Assert.True(CourierRules.FreeTimeCarries(storehouseHasCourier: false));
            Assert.False(CourierRules.FreeTimeCarries(storehouseHasCourier: true));
        }

        [Fact]
        public void A_lumberjack_keeps_ten_of_each_seed_it_replants()
        {
            var working = WorkingStock.OfLumberjack(new[] { "PineCone", "BeechSeeds", "PineCone" });
            Assert.Equal(10, working["PineCone"]);
            Assert.Equal(10, working["BeechSeeds"]);
            Assert.Equal(2, working.Count);
        }

        [Fact]
        public void Couriers_learn_Hauling_and_keep_it_through_saving()
        {
            Assert.Equal(WorkSkill.Hauling, WorkSkills.ForJob(SaveKeys.CourierJob));
            var r = new CitizenRecord { Id = "a", Name = "Sigrid", Hauling = 12f, HaulingXp = 0.25f };
            CitizenRecord copy = CitizenRecord.Decode(r.Encode());
            Assert.Equal(12f, copy.Level(WorkSkill.Hauling));
            Assert.Equal(0.25f, copy.HaulingXp);
            Assert.DoesNotContain("hauling", new CitizenRecord { Id = "b", Name = "Ulf" }.Encode());   // none: not saved
        }
    }
}
