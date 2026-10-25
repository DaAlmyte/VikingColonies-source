using System;

namespace VikingColonies.Core
{
    /// <summary>
    /// What a saved byte array decodes to, worked out again only when its bytes change, not whenever the array is a new one
    /// (the review round of 2026-10-23: a hut another computer owns arrives with its plan in a new array at every update, and
    /// the builder, seeing a "new plan", took the site afresh every time and never placed a piece).
    /// </summary>
    public sealed class ContentCache<T> where T : class
    {
        private byte[] _blob;
        private T _value;

        /// <returns>The decoded value; null for a null array.</returns>
        public T Get(byte[] blob, Func<byte[], T> decode)
        {
            if (blob == null)
            {
                _blob = null;
                _value = null;
                return null;
            }
            if (ReferenceEquals(blob, _blob)) return _value;
            if (_blob != null && Same(blob, _blob))
            {
                _blob = blob;
                return _value;
            }
            _blob = blob;
            _value = decode(blob);
            return _value;
        }

        private static bool Same(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }
    }
}
