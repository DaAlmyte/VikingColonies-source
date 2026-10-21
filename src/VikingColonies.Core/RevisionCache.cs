using System;

namespace VikingColonies.Core
{
    /// <summary>
    /// Saved data decoded once for each revision of what holds it (a ZDO's DataRevision), not every time it is asked for
    /// (1.4.2's speed polish; the whole-mod review of 1.4.1: every settler decoded the colony's keepsakes every 2 s).
    /// What it hands out is shared: read it, don't change it.
    /// </summary>
    public sealed class RevisionCache<T> where T : class
    {
        private T _value;
        private uint _revision;

        public T Get(uint revision, Func<T> read)
        {
            if (_value == null || revision != _revision)
            {
                _value = read();
                _revision = revision;
            }
            return _value;
        }

        public void Forget() => _value = null;
    }
}
