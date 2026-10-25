using System.Collections.Generic;
using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>The storehouse's Needs tab (1.5, part 3): who is short of what, what's on its way, what to bring.</summary>
    public class NeedsListTests
    {
        private static float FoodValue(string item) => item == "Raspberry" ? 1f : item == "CookedMeat" ? 3f : 0f;

        private static CourierSite Site(string id, float x, Dictionary<string, int>? spare = null, params KeepEntry[] shortOf) => new CourierSite
            { Id = id, At = new GroundPoint(x, 0), Spare = spare ?? new Dictionary<string, int>(), Short = shortOf.ToList() };

        private static CourierSite Store(Dictionary<string, int>? spare = null, params KeepEntry[] shortOf)
        {
            CourierSite store = Site("store", 0, spare, shortOf);
            store.IsStorehouse = true;
            return store;
        }

        private static KeepEntry Need(string item, int amount) => new KeepEntry { Item = item, Amount = amount };

        private static Dictionary<string, int> Of(params object[] pairs)
        {
            var d = new Dictionary<string, int>();
            for (int i = 0; i < pairs.Length; i += 2) d[(string)pairs[i]] = (int)pairs[i + 1];
            return d;
        }

        private static NeedsReport Report(IEnumerable<CourierSite> sites, params KeyValuePair<string, CourierTask>[] claimed) =>
            NeedsList.Of(sites.ToList(), claimed, FoodValue);

        private static KeyValuePair<string, CourierTask> Carrying(string courier, string from, string to, string item, int amount, string? need = null) =>
            new KeyValuePair<string, CourierTask>(courier, new CourierTask { Kind = CourierTaskKind.Deliver, From = from, To = to, Item = item, Need = need ?? item, Amount = amount });

        [Fact]
        public void A_need_with_a_courier_on_it_is_coming()
        {
            NeedLine line = Report(new[] { Store(Of("Wood", 40)), Site("hut", 5, null, Need("Wood", 20)) },
                Carrying("ulf", "store", "hut", "Wood", 20)).Lines.Single();
            Assert.Equal("hut", line.Site);
            Assert.Equal("Wood", line.Item);
            Assert.Equal(20, line.Amount);
            Assert.Equal(20, line.Coming);
            Assert.Equal("ulf", line.Courier);
            Assert.Equal(0, line.Waiting);
            Assert.Equal(0, line.Missing);
        }

        [Fact]
        public void A_need_the_store_can_fill_is_waiting()
        {
            NeedLine line = Report(new[] { Store(Of("Wood", 40)), Site("hut", 5, null, Need("Wood", 20)) }).Lines.Single();
            Assert.Equal(20, line.Waiting);
            Assert.Equal(0, line.Coming);
            Assert.Equal(0, line.Missing);
        }

        [Fact]   // Review Focus 3
        public void Needs_from_a_hut_with_the_switch_off_are_elsewhere()
        {
            CourierSite farm = Site("farm", 5, Of("Raspberry", 30));
            farm.TakeRest = false;
            NeedLine line = Report(new[] { Store(), farm, Site("tower", 10, null, Need("Raspberry", 10)) }).Lines.Single();
            Assert.Equal(10, line.Elsewhere);
            Assert.Equal("farm", line.ElsewhereAt);
            Assert.Equal(0, line.Waiting);
            Assert.Equal(0, line.Missing);
        }

        [Fact]
        public void A_need_only_in_the_town_hall_is_elsewhere()
        {
            CourierSite hall = Site("hall", 5, Of("Wood", 60));
            hall.IsTownHall = true;
            NeedsReport report = Report(new[] { Store(), hall, Site("hut", 10, null, Need("Wood", 20)) });
            Assert.Equal(20, report.Lines.Single().Elsewhere);
            Assert.Equal("hall", report.Lines.Single().ElsewhereAt);
            Assert.Empty(report.Bring);
        }

        [Fact]
        public void A_need_nobody_has_is_brought()
        {
            NeedsReport report = Report(new[] { Store(), Site("hut", 5, null, Need("Iron", 40)) });
            Assert.Equal(40, report.Lines.Single().Missing);
            KeepEntry bring = report.Bring.Single();
            Assert.Equal("Iron", bring.Item);
            Assert.Equal(40, bring.Amount);
        }

        [Fact]
        public void Two_needs_dont_count_one_spare_twice()
        {
            NeedsReport report = Report(new[] { Store(Of("Wood", 30)), Site("a", 5, null, Need("Wood", 20)), Site("b", 10, null, Need("Wood", 20)) });
            Assert.Equal(20, report.Lines[0].Waiting);
            Assert.Equal(10, report.Lines[1].Waiting);
            Assert.Equal(10, report.Lines[1].Missing);
            Assert.Equal(10, report.Bring.Single().Amount);
        }

        [Fact]
        public void A_full_chest_has_no_room()
        {
            CourierSite hut = Site("hut", 5, null, Need("Wood", 20));
            hut.CanTake = item => false;
            NeedsReport report = Report(new[] { Store(Of("Wood", 40)), hut });
            Assert.True(report.Lines.Single().NoRoom);
            Assert.Equal(0, report.Lines.Single().Missing);
            Assert.Empty(report.Bring);
        }

        [Fact]   // Review Focus 5
        public void Any_food_need_counts_spare_food_once()
        {
            NeedsReport report = Report(new[] { Store(Of("Raspberry", 5, "CookedMeat", 5, "Wood", 50)),
                Site("a", 5, null, Need(StockList.AnyFood, 8)), Site("b", 10, null, Need(StockList.AnyFood, 4)) });
            Assert.Equal(8, report.Lines[0].Waiting);
            Assert.Equal(2, report.Lines[1].Waiting);
            Assert.Equal(2, report.Lines[1].Missing);
            Assert.Equal(StockList.AnyFood, report.Bring.Single().Item);
            Assert.Equal(2, report.Bring.Single().Amount);
        }

        [Fact]
        public void A_storehouse_need_is_filled_from_its_own_store()
        {
            NeedLine line = Report(new[] { Store(Of("Raspberry", 20), Need("Raspberry", 5)) }).Lines.Single();
            Assert.Equal(5, line.Waiting);
            Assert.Equal(0, line.Missing);
        }

        [Fact]
        public void Bring_is_most_first()
        {
            NeedsReport report = Report(new[] { Store(), Site("hut", 5, null, Need("Iron", 10), Need("Bronze", 40), Need("Copper", 10)) });
            Assert.Equal(new[] { "Bronze", "Copper", "Iron" }, report.Bring.Select(b => b.Item));
        }

        [Fact]   // its review: two buildings with one id (a copying mod) threw on every refresh
        public void Two_buildings_with_one_id_dont_break_the_list()
        {
            NeedsReport report = Report(new[] { Store(Of("Wood", 10)), Site("twin", 5, Of("Wood", 5), Need("Stone", 5)), Site("twin", 6, null, Need("Wood", 5)) });
            Assert.Equal(2, report.Lines.Count);
        }

        [Fact]
        public void Partly_coming_the_rest_waits_or_is_brought()
        {
            NeedLine line = Report(new[] { Store(Of("Iron", 10)), Site("hut", 5, null, Need("Iron", 100)) },
                Carrying("ulf", "store", "hut", "Iron", 50)).Lines.Single();
            Assert.Equal(50, line.Coming);
            Assert.Equal(10, line.Waiting);
            Assert.Equal(40, line.Missing);
        }
    }
}
