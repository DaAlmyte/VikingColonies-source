using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>
    /// The security sweep of 1.4.1: some texts are made from a pattern another computer sends or a save holds (a message
    /// token, a settler's activity). A pattern like "{5}" made string.Format throw; it never does now.
    /// </summary>
    public class SafeTextTests
    {
        [Fact]
        public void A_good_pattern_is_filled_in()
        {
            Assert.Equal("Ragnar has come back", SafeText.Format("{0} has come back", "Ragnar"));
            Assert.Equal("2 of 3", SafeText.Format("{0} of {1}", 2, 3));
        }

        [Fact]
        public void A_bad_pattern_is_shown_as_it_is_not_thrown()
        {
            Assert.Equal("{5} oops", SafeText.Format("{5} oops", "Ragnar"));
            Assert.Equal("{ broken", SafeText.Format("{ broken", "x"));
            Assert.Equal("}{", SafeText.Format("}{"));
            Assert.Equal("", SafeText.Format(null, "x"));
        }

        [Fact]
        public void A_pattern_that_would_fill_memory_is_not_filled_in()
        {
            // The review of 1.4.1: "{0,999999}" a few hundred times throws nothing and builds hundreds of MB.
            string wide = string.Concat(System.Linq.Enumerable.Repeat("{0,999999}", 300));
            string shown = SafeText.Format(wide, "x");
            Assert.True(shown.Length <= SafeText.MaxPattern);
            Assert.Equal("{0,999999}", SafeText.Format("{0,999999}", "x"));
            Assert.Equal("  x", SafeText.Format("{0,3}", "x"));   // a small width is a pattern like any
            Assert.True(SafeText.Format(new string('a', 5000)).Length <= SafeText.MaxPattern);
        }
    }
}
