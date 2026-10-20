using System;
using System.Collections.Generic;
using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class CallingsTests
    {
        private static readonly string[] Jobs = { "lumberjack", "builder", "forager", "farmer" };

        [Fact]
        public void A_newcomer_brings_the_trade_an_empty_hut_needs_more_often_than_not()
        {
            int farmers = 0;
            for (int seed = 0; seed < 1000; seed++)
                if (Callings.Pick(new Random(seed), Jobs, new[] { "farmer" }) == "farmer") farmers++;
            Assert.InRange(farmers, 600, 760);   // 60% for the open hut, plus its share of the rest
        }

        [Fact]
        public void Without_an_empty_hut_any_trade_comes_and_one_in_five_has_none()
        {
            var seen = new Dictionary<string, int>();
            for (int seed = 0; seed < 2000; seed++)
            {
                string c = Callings.Pick(new Random(seed), Jobs, new string[0]);
                seen[c] = (seen.TryGetValue(c, out int n) ? n : 0) + 1;
            }
            Assert.All(Jobs, j => Assert.True(seen.ContainsKey(j)));
            Assert.InRange(seen[Callings.None], 320, 480);
        }

        [Fact]
        public void The_first_settlers_bring_a_builder()
        {
            for (int seed = 0; seed < 50; seed++)
            {
                IReadOnlyList<string> founders = Callings.Founders(new Random(seed), 4, Jobs);
                Assert.Equal(4, founders.Count);
                Assert.Equal("builder", founders[0]);
            }
            Assert.Empty(Callings.Founders(new Random(1), 0, Jobs));
        }

        [Fact]
        public void An_older_settler_gets_a_calling_from_his_id_that_never_changes()
        {
            Assert.Equal(Callings.FromId("abc", Jobs), Callings.FromId("abc", Jobs));
            Assert.Contains(Callings.FromId("xyz", Jobs), Jobs.Concat(new[] { Callings.None }));
            var differing = new HashSet<string>(Enumerable.Range(0, 50).Select(i => Callings.FromId("id" + i, Jobs)));
            Assert.True(differing.Count >= 3);
        }

        [Fact]
        public void In_his_calling_he_works_faster()
        {
            Assert.True(Callings.Matches("farmer", "farmer"));
            Assert.False(Callings.Matches("farmer", "forager"));
            Assert.False(Callings.Matches(Callings.None, "farmer"));
            Assert.False(Callings.Matches("", ""));
        }
    }

    public class TraitsTests
    {
        [Fact]
        public void Each_settler_has_a_trait_from_his_id_and_all_eight_occur()
        {
            Assert.Equal(Traits.FromId("abc"), Traits.FromId("abc"));
            var seen = new HashSet<Trait>(Enumerable.Range(0, 400).Select(i => Traits.FromId("settler" + i)));
            Assert.Equal(8, seen.Count);
        }

        [Fact]
        public void A_trait_changes_one_plain_thing()
        {
            Assert.Equal(1.1f, Traits.WorkFactor(Trait.Hardworking), 3);
            Assert.Equal(0.9f, Traits.WorkFactor(Trait.Lazy), 3);
            Assert.Equal(1f, Traits.WorkFactor(Trait.Cheerful), 3);
            Assert.Equal(6f, Traits.FleeRange(Trait.Brave, 12f), 3);
            Assert.Equal(20f, Traits.FleeRange(Trait.Timid, 12f), 3);
            Assert.Equal(12f, Traits.FleeRange(Trait.Quiet, 12f), 3);
        }

        [Fact]
        public void Free_time_follows_the_trait()
        {
            Assert.Equal(4, Traits.LeisureWeight(Trait.Chatty, Leisure.Chat, 2));
            Assert.Equal(0, Traits.LeisureWeight(Trait.Quiet, Leisure.Chat, 2));
            Assert.Equal(5, Traits.LeisureWeight(Trait.Quiet, Leisure.Seat, 3));
            Assert.Equal(0, Traits.LeisureWeight(Trait.Lazy, Leisure.Stroll, 1));
            Assert.Equal(0, Traits.LeisureWeight(Trait.Lazy, Leisure.Chat, 2));
            Assert.Equal(3, Traits.LeisureWeight(Trait.Grumpy, Leisure.Seat, 3));
            Assert.Equal(0.5f, Traits.TalkEvery(Trait.Chatty), 3);
            Assert.Equal(2f, Traits.TalkEvery(Trait.Quiet), 3);
            Assert.Equal(1f, Traits.TalkEvery(Trait.Brave), 3);
        }

        [Fact]
        public void The_pick_follows_his_trait()
        {
            var choices = new LeisureChoices(seat: true, fire: false, partner: true, wet: false);
            // Lazy: only the seat or sitting down (no chat, no stroll).
            Assert.All(new[] { 0.0, 0.3, 0.6, 0.99 }, r => Assert.Contains(FreeTimeRules.Pick(choices, r, Trait.Lazy), new[] { Leisure.Seat, Leisure.SitDown }));
            // Quiet: never a chat.
            Assert.All(Enumerable.Range(0, 20).Select(i => i / 20.0), r => Assert.NotEqual(Leisure.Chat, FreeTimeRules.Pick(choices, r, Trait.Quiet)));
            // No trait given: as before.
            Assert.Equal(FreeTimeRules.Pick(choices, 0.5), FreeTimeRules.Pick(choices, 0.5, null));
        }

        [Fact]
        public void A_trait_is_saved_by_name_and_read_back()
        {
            foreach (Trait t in Enum.GetValues(typeof(Trait)).Cast<Trait>())
                Assert.Equal(t, Traits.Parse(Traits.Name(t), Trait.Quiet == t ? Trait.Brave : Trait.Quiet));
            Assert.Equal(Trait.Brave, Traits.Parse("nonsense", Trait.Brave));
        }
    }

    public class OriginsTests
    {
        [Fact]
        public void Where_he_came_from_is_one_of_ten_and_from_his_id_for_older_settlers()
        {
            Assert.Equal(10, Origins.All.Count);
            Assert.All(Origins.All, o => Assert.StartsWith("$vc_origin_", o));
            Assert.Equal(Origins.FromId("abc"), Origins.FromId("abc"));
            Assert.Contains(Origins.Pick(new Random(3)), Origins.All);
        }
    }

    public class WorkPaceTests
    {
        [Fact]
        public void Hunger_calling_and_trait_multiply_and_never_go_below_a_tenth()
        {
            Assert.Equal(1f, WorkPace.Factor(false, 0.5f, false, 0.2f, Trait.Cheerful), 3);
            Assert.Equal(1.2f, WorkPace.Factor(false, 0.5f, true, 0.2f, Trait.Cheerful), 3);
            Assert.Equal(1.32f, WorkPace.Factor(false, 0.5f, true, 0.2f, Trait.Hardworking), 3);
            Assert.Equal(0.45f, WorkPace.Factor(true, 0.5f, false, 0.2f, Trait.Lazy), 3);
            Assert.Equal(0.1f, WorkPace.Factor(true, 0.05f, false, 0f, Trait.Lazy), 3);
        }
    }

    public class ArrivalSpotTests
    {
        [Fact]
        public void He_comes_from_the_valid_spot_farthest_from_every_player()
        {
            var spots = new[]
            {
                new ArrivalCandidate(new GroundPoint(35, 0), dry: true, reachable: true, nearestPlayer: 10f),
                new ArrivalCandidate(new GroundPoint(-35, 0), dry: true, reachable: true, nearestPlayer: 60f),
                new ArrivalCandidate(new GroundPoint(0, 35), dry: false, reachable: true, nearestPlayer: 90f),
                new ArrivalCandidate(new GroundPoint(0, -35), dry: true, reachable: false, nearestPlayer: 99f),
            };
            Assert.Equal(1, ArrivalSpot.Choose(spots));
            Assert.Equal(-1, ArrivalSpot.Choose(spots.Where(s => !s.Dry || !s.Reachable).ToList()));
        }

        private static ArrivalCandidate Spot(bool dry, bool reachable, float nearestPlayer) =>
            new ArrivalCandidate(new GroundPoint(0, 0), dry, reachable, nearestPlayer);

        [Fact]
        public void Both_rings_are_tried_for_a_way_in_before_dry_ground_will_do()
        {
            // Review I2: a far ring with no way in fell back to dry ground there before the near ring was tried.
            var far = new[] { Spot(true, false, 50f), Spot(true, false, 60f) };
            var near = new[] { Spot(true, true, 5f), Spot(true, false, 80f) };
            Assert.Equal(2, ArrivalSpot.Choose(new[] { far, near }, founding: true));
            Assert.Equal(2, ArrivalSpot.Choose(new[] { far, near }, founding: false));
        }

        [Fact]
        public void Dry_ground_with_no_way_in_will_do_only_for_a_new_Town_Hall()
        {
            // Right after a Town Hall goes up the game may not have its paths out there yet; later "no way in" is real
            // (an island, a closed palisade), and he appears by the Town Hall instead.
            var far = new[] { Spot(true, false, 50f), Spot(false, false, 99f) };
            var near = new[] { Spot(true, false, 70f) };
            Assert.Equal(-1, ArrivalSpot.Choose(new[] { far, near }, founding: false));
            Assert.Equal(0, ArrivalSpot.Choose(new[] { far, near }, founding: true));
            Assert.Equal(-1, ArrivalSpot.Choose(new[] { new[] { Spot(false, false, 9f) } }, founding: true));
        }

        [Fact]
        public void The_spots_ring_the_Town_Hall()
        {
            IReadOnlyList<GroundPoint> ring = ArrivalSpot.Directions(new GroundPoint(10, 10), 35f, 16);
            Assert.Equal(16, ring.Count);
            Assert.All(ring, p => Assert.Equal(35f, ColonyRules.Distance(p, new GroundPoint(10, 10)), 2));
        }
    }

    public class WalkInWatchTests
    {
        [Fact]
        public void Only_the_time_spent_walking_in_counts()
        {
            // Review M2: the clock ran on while breakfast, the evening or a monster held him up, and he was then placed by
            // the Town Hall at once.
            var watch = new WalkInWatch();
            watch.Walk(100f);
            Assert.False(watch.TimedOut);
            watch.Walk(70f);
            Assert.False(watch.TimedOut);
            watch.Walk(10f);
            Assert.True(watch.TimedOut);
            watch.Reset();
            Assert.False(watch.TimedOut);
            Assert.Equal(0f, watch.Walked);
            watch.Walk(-5f);
            Assert.Equal(0f, watch.Walked);
        }
    }

    public class WardrobeTests
    {
        [Fact]
        public void Each_trade_has_its_clothes_and_the_jobless_wear_rags()
        {
            Assert.Equal(new[] { "ArmorRagsChest", "ArmorRagsLegs" }, Wardrobe.For(""));
            Assert.Contains("HelmetLeather", Wardrobe.For("builder"));
            Assert.Contains("ArmorLeatherChest", Wardrobe.For("lumberjack"));
            Assert.Contains("ArmorTrollLeatherLegs", Wardrobe.For("forager"));
            Assert.Contains("ArmorRagsChest", Wardrobe.For("farmer"));
            Assert.Equal(Wardrobe.For(""), Wardrobe.For("a job from another mod"));
        }
    }
}
