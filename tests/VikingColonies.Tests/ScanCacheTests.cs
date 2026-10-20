using System.Collections.Generic;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class ScanCacheTests
    {
        private int _scans;

        private ScanCache<int> Cache() => new ScanCache<int>(() =>
        {
            _scans++;
            return new List<int> { _scans };
        }, 2f);

        [Fact]
        public void The_first_read_scans()
        {
            Assert.Equal(new[] { 1 }, Cache().Get(10));
            Assert.Equal(1, _scans);
        }

        [Fact]
        public void Reads_within_the_age_reuse_the_scan()
        {
            var cache = Cache();
            cache.Get(10);
            cache.Get(11.9);
            Assert.Equal(1, _scans);
        }

        [Fact]
        public void Reads_after_the_age_rescan()
        {
            var cache = Cache();
            cache.Get(10);
            Assert.Equal(new[] { 2 }, cache.Get(12));
        }

        [Fact]
        public void A_clock_that_goes_backwards_rescans()
        {
            var cache = Cache();
            cache.Get(500);
            cache.Get(3);   // a new session started the clock again
            Assert.Equal(2, _scans);
        }

        [Fact]
        public void Invalidate_forces_a_rescan()
        {
            var cache = Cache();
            cache.Get(10);
            cache.Invalidate();
            cache.Get(10.5);
            Assert.Equal(2, _scans);
        }

        [Fact]
        public void A_scan_that_returns_nothing_counts_as_empty()
        {
            var cache = new ScanCache<int>(() => null, 2f);
            Assert.Empty(cache.Get(0));
        }
    }
}
