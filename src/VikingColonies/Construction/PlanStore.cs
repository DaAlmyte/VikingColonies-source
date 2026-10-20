using VikingColonies.Core;

namespace VikingColonies.Construction
{
    /// <summary>Plans are saved with the hut gzip-compressed (<see cref="PlanBlob"/>), so big buildings don't bloat its network object.</summary>
    internal static class PlanStore
    {
        public static byte[] Pack(string text) => PlanBlob.Pack(text);

        /// <summary>"" for unreadable data (the hut then counts as built). Never throws.</summary>
        public static string Unpack(byte[] blob)
        {
            if (PlanBlob.TryUnpack(blob, out string text, out string problem)) return text;
            Log.Warn($"A saved building plan couldn't be read ({problem}); that hut counts as built.");
            return "";
        }
    }
}
