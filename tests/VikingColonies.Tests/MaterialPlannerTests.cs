using System.Collections.Generic;
using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class MaterialPlannerTests
    {
        private static IReadOnlyDictionary<string, int> Needs(params (string item, int n)[] items) =>
            items.ToDictionary(i => i.item, i => i.n);

        private static CarryLoad Carrying(params (string item, int n)[] items)
        {
            var load = new CarryLoad();
            foreach (var (item, n) in items) load.Add(item, n);
            return load;
        }

        [Fact]
        public void What_he_carries_is_not_fetched_again()
        {
            MaterialPlan plan = MaterialPlanner.Plan(new[] { Needs(("Wood", 4)) }, Carrying(("Wood", 3)), 50,
                new[] { Needs(("Wood", 10)) });
            Assert.Single(plan.Steps);
            Assert.Equal(1, plan.Steps[0].Amount);
            Assert.Empty(plan.Missing);
        }

        [Fact]
        public void The_first_store_is_emptied_before_the_next()
        {
            MaterialPlan plan = MaterialPlanner.Plan(new[] { Needs(("Wood", 6)) }, new CarryLoad(), 50,
                new[] { Needs(("Wood", 2)), Needs(("Wood", 10)) });
            Assert.Equal(new[] { (0, 2), (1, 4) }, plan.Steps.Select(s => (s.Store, s.Amount)));
        }

        [Fact]
        public void He_fetches_for_several_pieces_while_the_load_stays_within_his_capacity()
        {
            var upcoming = new[] { Needs(("Wood", 2)), Needs(("Wood", 2)), Needs(("Wood", 2)) };
            MaterialPlan plan = MaterialPlanner.Plan(upcoming, new CarryLoad(), 5, new[] { Needs(("Wood", 100)) });
            Assert.Equal(4, plan.Steps.Sum(s => s.Amount));
        }

        [Fact]
        public void A_piece_bigger_than_his_capacity_is_still_fetched_whole()
        {
            MaterialPlan plan = MaterialPlanner.Plan(new[] { Needs(("Stone", 8)), Needs(("Wood", 1)) }, new CarryLoad(), 5,
                new[] { Needs(("Stone", 20), ("Wood", 5)) });
            Assert.Equal(8, plan.Steps.Where(s => s.Item == "Stone").Sum(s => s.Amount));
            Assert.DoesNotContain(plan.Steps, s => s.Item == "Wood");
        }

        [Fact]
        public void Missing_is_what_the_next_piece_lacks_everywhere()
        {
            var upcoming = new[] { Needs(("FineWood", 6), ("Wood", 1)), Needs(("Stone", 5)) };
            MaterialPlan plan = MaterialPlanner.Plan(upcoming, new CarryLoad(), 50, new[] { Needs(("Wood", 9), ("FineWood", 2)) });
            Assert.Equal(new Dictionary<string, int> { { "FineWood", 4 } }, plan.Missing);
        }

        [Fact]
        public void He_can_build_once_he_carries_everything_the_piece_needs()
        {
            Assert.True(MaterialPlanner.CanBuild(Needs(("Wood", 2)), Carrying(("Wood", 2))));
            Assert.False(MaterialPlanner.CanBuild(Needs(("Wood", 2), ("Stone", 1)), Carrying(("Wood", 2))));
        }
    }
}
