using System;
using System.Collections.Generic;

namespace VikingColonies.Core
{
    /// <summary>
    /// Holds the result of an expensive world scan and repeats the scan only when it is older than the maximum age
    /// (or the clock went backwards, as when a new world session starts). Shared by every settler.
    /// </summary>
    public sealed class ScanCache<T>
    {
        private readonly Func<List<T>> _scan;
        private readonly float _maxAgeSeconds;
        private List<T> _items = new List<T>();
        private double _scannedAt = double.NegativeInfinity;

        public ScanCache(Func<List<T>> scan, float maxAgeSeconds)
        {
            _scan = scan;
            _maxAgeSeconds = maxAgeSeconds;
        }

        public IReadOnlyList<T> Get(double now)
        {
            if (now - _scannedAt >= _maxAgeSeconds || now < _scannedAt)
            {
                _items = _scan() ?? new List<T>();
                _scannedAt = now;
            }
            return _items;
        }

        public void Invalidate() => _scannedAt = double.NegativeInfinity;
    }
}
