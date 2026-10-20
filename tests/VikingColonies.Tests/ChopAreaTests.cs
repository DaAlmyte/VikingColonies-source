using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class ChopAreaTests
    {
        [Fact]
        public void Trees_count_only_inside_the_huts_work_area()
        {
            Assert.True(ChopArea.Counts(isTree: true, fromHut: 39f, radius: 40f, haveFelled: true, fromFelled: 2f));
            Assert.False(ChopArea.Counts(isTree: true, fromHut: 41f, radius: 40f, haveFelled: true, fromFelled: 2f));
        }

        [Fact]
        public void The_logs_of_a_tree_he_felled_count_where_they_landed_past_the_area()
        {
            Assert.True(ChopArea.Counts(isTree: false, fromHut: 52f, radius: 40f, haveFelled: true, fromFelled: 18f));
            Assert.True(ChopArea.IsLeftover(isTree: false, haveFelled: true, fromFelled: 18f));
        }

        [Fact]
        public void Other_logs_past_the_area_do_not_count()
        {
            Assert.False(ChopArea.Counts(isTree: false, fromHut: 52f, radius: 40f, haveFelled: true, fromFelled: 30f));
            Assert.False(ChopArea.Counts(isTree: false, fromHut: 52f, radius: 40f, haveFelled: false, fromFelled: 0f));
            Assert.False(ChopArea.IsLeftover(isTree: true, haveFelled: true, fromFelled: 1f));   // a standing tree is never a leftover
        }
    }
}
