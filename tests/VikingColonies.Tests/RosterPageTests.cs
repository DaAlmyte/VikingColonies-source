using System.Collections.Generic;
using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    // The Town Hall's Citizens tab: ten names a page, sorted by name; the citizen you chose stays chosen while the
    // list changes, and drops when he's gone.
    public class RosterPageTests
    {
        private static List<RosterEntry> Roster(params string[] names) =>
            names.Select((n, i) => new RosterEntry { CitizenId = "id" + i, Name = n }).ToList();

        [Fact]
        public void Rows_are_sorted_by_name_ten_a_page()
        {
            // Given backwards (Z first), so a page cut before sorting would start with the wrong names.
            List<RosterEntry> roster = Roster(Enumerable.Range(0, 23).Select(i => "Settler" + (char)('Z' - i)).ToArray());
            IReadOnlyList<RosterEntry> first = RosterPage.Rows(roster, 0, 10);
            Assert.Equal(10, first.Count);
            Assert.Equal("SettlerD", first[0].Name);
            Assert.Equal("SettlerM", first[9].Name);
            Assert.Equal("SettlerX", RosterPage.Rows(roster, 2, 10)[0].Name);
            Assert.Equal(3, RosterPage.Rows(roster, 2, 10).Count);
            Assert.Equal(3, RosterPage.Pages(roster.Count, 10));
        }

        [Fact]
        public void Names_sort_regardless_of_capitals()
        {
            List<RosterEntry> roster = Roster("bjorn", "Astrid", "Cnut");
            Assert.Equal(new[] { "Astrid", "bjorn", "Cnut" }, RosterPage.Rows(roster, 0, 10).Select(e => e.Name).ToArray());
        }

        [Fact]
        public void An_empty_roster_still_has_one_page()
        {
            Assert.Equal(1, RosterPage.Pages(0, 10));
            Assert.Empty(RosterPage.Rows(new List<RosterEntry>(), 0, 10));
        }

        [Fact]
        public void A_page_beyond_the_end_comes_back_to_the_last()
        {
            Assert.Equal(1, RosterPage.ClampPage(5, 15, 10));
            Assert.Equal(0, RosterPage.ClampPage(-1, 15, 10));
            Assert.Equal(0, RosterPage.ClampPage(3, 0, 10));
        }

        [Fact]
        public void The_chosen_citizen_stays_chosen_while_on_the_roster_and_drops_when_gone()
        {
            List<RosterEntry> roster = Roster("Astrid", "Ivar");
            Assert.Equal("id1", RosterPage.KeepChoice("id1", roster));
            roster.RemoveAt(1);   // Ivar died or left
            Assert.Equal("", RosterPage.KeepChoice("id1", roster));
            Assert.Equal("", RosterPage.KeepChoice("", roster));
        }
    }
}
