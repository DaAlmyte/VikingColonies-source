using System.Collections.Generic;
using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class TheDayTests
    {
        [Theory]
        [InlineData(0.3f, false, DayPhase.Day)]
        [InlineData(0.69f, false, DayPhase.Day)]
        [InlineData(0.70f, false, DayPhase.Evening)]
        [InlineData(0.749f, false, DayPhase.Evening)]
        [InlineData(0.76f, true, DayPhase.Night)]
        [InlineData(0.2f, true, DayPhase.Night)]
        [InlineData(0.72f, true, DayPhase.Night)]   // the game says night: night it is
        public void The_day_has_its_phases(float fraction, bool night, DayPhase expected)
        {
            Assert.Equal(expected, TheDay.Phase(fraction, night));
        }
    }

    public class GatheringTests
    {
        private static readonly GroundPoint Hall = new GroundPoint(0, 0);

        [Fact]
        public void A_roofed_burning_fire_comes_first_then_the_nearest_to_the_Town_Hall()
        {
            var fires = new[]
            {
                new GatheringFire("near", new GroundPoint(5, 0), burning: true, roofed: false),
                new GatheringFire("roofed", new GroundPoint(20, 0), burning: true, roofed: true),
                new GatheringFire("cold", new GroundPoint(2, 0), burning: false, roofed: true),
            };
            Assert.Equal(1, Gathering.Place(fires, Hall, 50f));
            Assert.Equal(0, Gathering.Place(fires.Where(f => f.Id != "roofed").ToList(), Hall, 50f));
        }

        // 1.4.1: the Longhouse's fire pit (under its roof) drew the colony's evening indoors, where the places round it fall
        // among the walls and beds and nobody sat. A hut's own hearth is its own; the colony gathers elsewhere.
        [Fact]
        public void A_fire_inside_one_of_the_colonys_huts_is_not_the_colonys_evening_fire()
        {
            var fires = new[]
            {
                new GatheringFire("pit", new GroundPoint(8, 0), burning: true, roofed: true, inHut: true),
                new GatheringFire("open", new GroundPoint(30, 0), burning: true, roofed: false),
            };
            Assert.Equal(1, Gathering.Place(fires, Hall, 50f));
            Assert.Equal(-1, Gathering.Place(fires.Take(1).ToList(), Hall, 50f));   // only the pit: at the Town Hall
        }

        [Fact]
        public void The_camps_fire_still_comes_first()
        {
            var fires = new[]
            {
                new GatheringFire("open", new GroundPoint(3, 0), burning: true, roofed: false),
                new GatheringFire("camp", new GroundPoint(2, 2), burning: true, roofed: false, atHall: true),
            };
            Assert.Equal(1, Gathering.Place(fires, Hall, 50f));
        }

        [Fact]
        public void With_no_fire_burning_inside_the_colony_they_gather_at_the_Town_Hall()
        {
            var fires = new[]
            {
                new GatheringFire("cold", new GroundPoint(2, 0), burning: false, roofed: false),
                new GatheringFire("far", new GroundPoint(80, 0), burning: true, roofed: false),
            };
            Assert.Equal(-1, Gathering.Place(fires, Hall, 50f));
            Assert.Equal(-1, Gathering.Place(new GatheringFire[0], Hall, 50f));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(6)]
        [InlineData(30)]
        [InlineData(60)]
        public void The_ring_round_the_fire_keeps_everyone_a_metre_apart(int count)
        {
            var centre = new GroundPoint(10, -4);
            List<GroundPoint> spots = Enumerable.Range(0, count).Select(i => Gathering.Ring(centre, i, count)).ToList();
            foreach (GroundPoint s in spots)
            {
                float d = ColonyRules.Distance(s, centre);
                Assert.InRange(d, Gathering.RingInner - 0.01f, Gathering.RingInner + 10f);
            }
            for (int a = 0; a < count; a++)
                for (int b = a + 1; b < count; b++)
                    Assert.True(ColonyRules.Distance(spots[a], spots[b]) >= 0.99f, $"spots {a} and {b} are too close");
            if (count <= 6) Assert.All(spots, s => Assert.InRange(ColonyRules.Distance(s, centre), 2f, 3.5f));
        }

        [Fact]
        public void Each_settler_has_his_own_place_in_the_ring_from_his_id()
        {
            var ids = new[] { "c", "a", "b" };
            Assert.Equal(0, Gathering.Index("a", ids));
            Assert.Equal(2, Gathering.Index("c", ids));
            Assert.Equal(-1, Gathering.Index("x", ids));
        }
    }

    public class FireFuelTests
    {
        [Theory]
        [InlineData(1f, 10f, 50, 5)]    // to past half: 5 - 1 + 1
        [InlineData(5f, 10f, 50, 0)]    // half full: nothing
        [InlineData(7f, 10f, 50, 0)]
        [InlineData(0f, 10f, 12, 2)]    // ten stay in the chests
        [InlineData(0f, 10f, 10, 0)]
        [InlineData(0f, 1f, 50, 1)]     // never more than it holds
        [InlineData(4.5f, 10f, 50, 2)]
        public void He_brings_enough_to_take_the_fire_past_half(float fuel, float max, int inChests, int expected)
        {
            Assert.Equal(expected, FireFuel.ToFetch(fuel, max, inChests));
        }
    }

    public class BreakfastTests
    {
        [Fact]
        public void He_eats_at_the_nearest_chest_with_food_the_Town_Hall_first_when_as_near()
        {
            var me = new GroundPoint(0, 0);
            var chests = new[]
            {
                new BreakfastChest("hut", new GroundPoint(5, 0), townHall: false, hasFood: true),
                new BreakfastChest("hall", new GroundPoint(0, 5), townHall: true, hasFood: true),
                new BreakfastChest("empty", new GroundPoint(1, 0), townHall: false, hasFood: false),
            };
            Assert.Equal("hall", Breakfast.Chest(chests, me));
            Assert.Equal("hut", Breakfast.Chest(chests.Where(c => c.Id != "hall"), me));
            Assert.Equal("", Breakfast.Chest(chests.Where(c => c.Id == "empty"), me));
        }
    }

    public class EveningAndFireLeisureTests
    {
        [Fact]
        public void A_fire_to_tend_comes_first_by_weight()
        {
            var choices = new LeisureChoices(seat: true, fire: true, partner: true, wet: false, tend: true);
            Assert.Equal(5, FreeTimeRules.Weight(Leisure.TendFire, choices));
            Assert.Equal(Leisure.TendFire, FreeTimeRules.Pick(choices, 0.0));
            Assert.Equal(Leisure.TendFire, FreeTimeRules.Pick(choices, 0.32));   // 5 of 15
            Assert.Equal(Leisure.Seat, FreeTimeRules.Pick(choices, 0.34));
            Assert.Equal(0, FreeTimeRules.Weight(Leisure.TendFire, new LeisureChoices(true, true, true, false)));
        }

        [Fact]
        public void The_evening_is_never_picked_in_free_time_but_has_its_words()
        {
            var choices = new LeisureChoices(true, true, true, false, true);
            Assert.Equal(0, FreeTimeRules.Weight(Leisure.Evening, choices));
            Assert.Equal("$vc_doing_evening", ActivityWords.Token(Leisure.Evening));
            Assert.Equal("$vc_doing_tending", ActivityWords.Token(Leisure.TendFire));
        }

        [Fact]
        public void In_the_evening_they_talk_about_the_evening()
        {
            var context = new TalkContext { Evening = true };
            Assert.Equal(TalkTopic.Evening, SmallTalk.Topic(context, () => 0.1));
            Assert.True(SmallTalk.Count(TalkTopic.Evening, "") >= 4);
        }
    }
}
