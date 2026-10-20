using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    // Seen: 40 jobless settlers all walking to one point by the Town Hall, jostling and hopping.
    public class JoblessSpotTests
    {
        [Fact]
        public void Jobless_settlers_spread_out_round_the_Town_Hall()
        {
            var hall = new GroundPoint(100f, 200f);
            var spots = Enumerable.Range(0, 40).Select(i => JoblessSpot.For(hall, "settler-" + i)).ToList();
            Assert.All(spots, s =>
            {
                float d = ColonyRules.Distance(s, hall);
                Assert.InRange(d, JoblessSpot.MinRadius - 0.01f, JoblessSpot.MaxRadius + 0.01f);
            });
            int distinct = spots.Select(s => (System.Math.Round(s.X), System.Math.Round(s.Z))).Distinct().Count();
            Assert.True(distinct >= 25, $"only {distinct} distinct spots for 40 settlers");
        }

        [Fact]
        public void A_settler_always_has_the_same_spot()
        {
            var hall = new GroundPoint(0f, 0f);
            Assert.Equal(JoblessSpot.For(hall, "ylva").X, JoblessSpot.For(hall, "ylva").X);
            Assert.Equal(JoblessSpot.For(hall, "ylva").Z, JoblessSpot.For(hall, "ylva").Z);
        }
    }
}
