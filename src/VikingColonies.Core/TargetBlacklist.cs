using System.Collections.Generic;

namespace VikingColonies.Core
{
    /// <summary>Remembers targets to skip for a while (e.g. a tree the settler couldn't reach).</summary>
    public sealed class TargetBlacklist
    {
        private readonly Dictionary<string, double> _blockedUntil = new Dictionary<string, double>();
        private readonly double _durationSeconds;

        public TargetBlacklist(double durationSeconds) { _durationSeconds = durationSeconds; }

        public void Add(string id, double now) => _blockedUntil[id] = now + _durationSeconds;

        public bool IsBlocked(string id, double now)
        {
            if (id == null || !_blockedUntil.TryGetValue(id, out double until)) return false;
            if (now < until) return true;
            _blockedUntil.Remove(id);
            return false;
        }
    }
}
