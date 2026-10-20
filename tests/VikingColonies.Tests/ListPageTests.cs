using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    // A hut's tick lists (what to grow, pick, replant) in pages: mods add crops, plants and trees, and a list that only
    // showed its first 16 left the rest out of reach (review before 1.0).
    public class ListPageTests
    {
        [Fact]
        public void A_long_list_shows_in_pages()
        {
            int[] rows = Enumerable.Range(0, 30).ToArray();
            Assert.Equal(Enumerable.Range(12, 12).ToArray(), ListPage.Slice(rows, 1, 12).ToArray());
            Assert.Equal(Enumerable.Range(24, 6).ToArray(), ListPage.Slice(rows, 2, 12).ToArray());
            Assert.Equal(Enumerable.Range(24, 6).ToArray(), ListPage.Slice(rows, 9, 12).ToArray());   // beyond the end: the last page
            Assert.Equal(Enumerable.Range(0, 12).ToArray(), ListPage.Slice(rows, -1, 12).ToArray());
            Assert.Empty(ListPage.Slice(new int[0], 0, 12));
        }

        [Fact]
        public void A_page_number_stays_on_a_page_that_exists()
        {
            Assert.Equal(2, ListPage.Clamp(5, 30, 12));
            Assert.Equal(0, ListPage.Clamp(3, 5, 12));
            Assert.Equal(3, ListPage.Pages(30, 12));
            Assert.Equal(1, ListPage.Pages(0, 12));
        }
    }
}
