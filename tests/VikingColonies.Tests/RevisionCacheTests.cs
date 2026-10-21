using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>1.4.2's speed polish: saved data read again only when it has changed (the whole-mod review of 1.4.1).</summary>
    public class RevisionCacheTests
    {
        [Fact]
        public void It_reads_once_for_each_revision()
        {
            int reads = 0;
            var cache = new RevisionCache<string>();
            Assert.Equal("a", cache.Get(1, () => { reads++; return "a"; }));
            Assert.Equal("a", cache.Get(1, () => { reads++; return "b"; }));
            Assert.Equal(1, reads);
            Assert.Equal("c", cache.Get(2, () => { reads++; return "c"; }));
            Assert.Equal(2, reads);
        }

        [Fact]
        public void Forgotten_it_reads_again()
        {
            int reads = 0;
            var cache = new RevisionCache<string>();
            cache.Get(5, () => { reads++; return "x"; });
            cache.Forget();
            cache.Get(5, () => { reads++; return "y"; });
            Assert.Equal(2, reads);
        }
    }
}
