using System.Collections.Generic;
using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    // People and growth: the beds in a colony's Longhouses are its homes.
    public class HomesTests
    {
        private static HomeBed Bed(string id, float x) => new HomeBed(id, new GroundPoint(x, 0f));
        private static HomeSeeker Settler(string id, float x, string home = "") => new HomeSeeker(id, new GroundPoint(x, 0f), home);

        [Fact]
        public void A_settler_out_of_sight_keeps_his_bed()
        {
            // Seen in review: a settler beyond the loaded area looked homeless to the Longhouse, and his bed went to another.
            var roster = new ColonyRoster();
            roster.Seen("away", "Ulf", "", "", "", new[] { 90f, 0f, 0f }, 1, home: "near");
            roster.Seen("here", "Ylva", "", "", "", new[] { 0f, 0f, 0f }, 1);
            roster.Seen("gone", "Ase", "", "", "", new[] { 0f, 0f, 0f }, 1, home: "far");
            roster.MarkMissing("gone", 1);
            var loaded = new[] { Settler("here", 0f) };
            List<HomeSeeker> all = loaded.Concat(Homes.OutOfSight(roster.Entries, new HashSet<string> { "here" })).ToList();
            var homes = Homes.Assign(new[] { Bed("near", 1f), Bed("far", 30f) }, all);
            Assert.Equal("near", homes["away"]);
            Assert.Equal("far", homes["here"]);     // a missing settler's bed is free
            Assert.False(homes.ContainsKey("gone"));
        }

        [Fact]
        public void A_homeless_settler_takes_the_nearest_free_bed()
        {
            var homes = Homes.Assign(new[] { Bed("far", 50f), Bed("near", 5f) }, new[] { Settler("ylva", 0f) });
            Assert.Equal("near", homes["ylva"]);
        }

        [Fact]
        public void A_settler_keeps_his_bed()
        {
            var homes = Homes.Assign(new[] { Bed("a", 50f), Bed("b", 1f) }, new[] { Settler("ylva", 0f, home: "a") });
            Assert.Equal("a", homes["ylva"]);
        }

        [Fact]
        public void Nobody_shares_a_bed_and_extra_settlers_stay_homeless()
        {
            var settlers = new[] { Settler("a", 0f), Settler("b", 1f), Settler("c", 2f) };
            var homes = Homes.Assign(new[] { Bed("x", 0f), Bed("y", 3f) }, settlers);
            var taken = homes.Values.Where(v => v.Length > 0).ToList();
            Assert.Equal(2, taken.Count);
            Assert.Equal(taken.Count, taken.Distinct().Count());
            Assert.Equal(1, homes.Values.Count(v => v.Length == 0));
        }

        [Fact]
        public void A_settler_whose_bed_is_gone_finds_another_or_is_homeless()
        {
            var homes = Homes.Assign(new[] { Bed("new", 4f) }, new[] { Settler("ylva", 0f, home: "gone"), Settler("ulf", 9f, home: "gone2") });
            Assert.Equal("new", homes["ylva"]);   // the nearer one
            Assert.Equal("", homes["ulf"]);
        }

        [Fact]
        public void Two_settlers_claiming_one_bed_leave_it_to_one()
        {
            var homes = Homes.Assign(new[] { Bed("x", 0f), Bed("y", 20f) }, new[] { Settler("b", 0f, home: "x"), Settler("a", 0f, home: "x") });
            Assert.Equal("x", homes["a"]);   // the lower id keeps it
            Assert.Equal("y", homes["b"]);
        }

        [Fact]
        public void Nothing_changes_when_nothing_changed()
        {
            var beds = new[] { Bed("x", 0f), Bed("y", 10f) };
            var first = Homes.Assign(beds, new[] { Settler("a", 1f), Settler("b", 9f) });
            var again = Homes.Assign(beds, new[] { Settler("a", 1f, first["a"]), Settler("b", 9f, first["b"]) });
            Assert.Equal(first, again);
        }
    }
}
