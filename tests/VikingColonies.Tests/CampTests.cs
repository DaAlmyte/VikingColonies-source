using System.Collections.Generic;
using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    // The Town Hall's camp (huts round 2, 2026-10-18): built by the builder, grown with the hall, the colony's evenings.
    public class CampTests
    {
        [Fact]
        public void The_camp_follows_the_hall_one_level_at_a_time()
        {
            Assert.Equal(2, CampRules.NextLevel(3, 1, false, 5));
            Assert.Equal(3, CampRules.NextLevel(3, 2, false, 5));
            Assert.Equal(0, CampRules.NextLevel(3, 3, false, 5));   // caught up
            Assert.Equal(0, CampRules.NextLevel(3, 1, true, 5));    // the builder is still on it
            Assert.Equal(0, CampRules.NextLevel(5, 0, false, 5));   // no camp yet: only the player starts one
            Assert.Equal(0, CampRules.NextLevel(9, 5, false, 5));   // five levels at most
        }

        // The review of 2026-10-19: with the hall at 2 and the Builder's Hut at 1, the camp ordered a level no builder may
        // build; the order was never taken, and with the plan already switched, its repairs stopped too.
        [Fact]
        public void The_camp_waits_for_a_builders_hut_high_enough_as_huts_do()
        {
            Assert.Equal(0, CampRules.NextLevel(2, 1, false, 1));
            Assert.True(CampRules.WaitsForBuilder(2, 1, false, 1));
            Assert.Equal(2, CampRules.NextLevel(2, 1, false, 2));
            Assert.False(CampRules.WaitsForBuilder(2, 1, false, 2));
            Assert.False(CampRules.WaitsForBuilder(2, 2, false, 1));   // caught up with the hall: it waits for nothing
            Assert.False(CampRules.WaitsForBuilder(3, 1, true, 1));    // still building: not waiting yet
            Assert.True(CampRules.WaitsForBuilder(3, 1, false, 0));    // no Builder's Hut at all
        }

        [Fact]
        public void A_builder_takes_the_huts_orders_before_the_camps()
        {
            var options = new[] { new OrderOption(false, 1, false, later: true), new OrderOption(false, 9, false) };
            Assert.Equal(1, OrderChoice.Pick(options));
            Assert.Equal(0, OrderChoice.Pick(new[] { new OrderOption(false, 1, false, later: true) }));
            options = new[] { new OrderOption(false, 1, false, first: 5, later: true), new OrderOption(false, 9, false) };
            Assert.Equal(0, OrderChoice.Pick(options));           // Do first wins
        }

        [Fact]
        public void The_work_orders_tab_lists_the_camp_after_the_huts_unless_marked_first()
        {
            var rows = new[]
            {
                new WorkOrderRow { BuildingId = "camp", Created = 1, Later = true },
                new WorkOrderRow { BuildingId = "hut", Created = 9 },
            };
            Assert.Equal(new[] { "hut", "camp" }, WorkOrderList.Sorted(rows).Select(r => r.BuildingId).ToArray());
            rows[0].First = 3;
            Assert.Equal(new[] { "camp", "hut" }, WorkOrderList.Sorted(rows).Select(r => r.BuildingId).ToArray());
        }

        [Fact]
        public void The_colony_gathers_at_the_camps_fire_before_a_roofed_one_nearer_the_hall()
        {
            var hall = new GroundPoint(0, 0);
            var fires = new List<GatheringFire>
            {
                new GatheringFire("hearth", new GroundPoint(3, 0), true, true),
                new GatheringFire("camp", new GroundPoint(0, 6), true, false, atHall: true),
            };
            Assert.Equal(1, Gathering.Place(fires, hall, 50f));
            fires[1] = new GatheringFire("camp", new GroundPoint(0, 6), false, false, atHall: true);   // out
            Assert.Equal(0, Gathering.Place(fires, hall, 50f));
        }

        [Fact]
        public void The_ring_round_a_bonfire_stands_clear_of_it()
        {
            Assert.Equal(Gathering.RingInner, Gathering.InnerFor(0.7f));     // a fire pit: as before
            float inner = Gathering.InnerFor(2.2f);                           // a bonfire is about 4.4 m across
            Assert.Equal(3.0f, inner, 3);
            GroundPoint spot = Gathering.Ring(new GroundPoint(0, 0), 0, 1, inner);
            Assert.Equal(3.0, System.Math.Sqrt(spot.X * spot.X + spot.Z * spot.Z), 3);
        }
    }
}
