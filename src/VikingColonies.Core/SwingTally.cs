namespace VikingColonies.Core
{
    public enum SwingResult { HitTarget, HitOther, HitNothing }

    public enum SwingLogKind { None, Detail, Summary }

    /// <summary>Counts a settler's axe swings and says which to write to the log: the first ten in detail, then a summary every fifty.</summary>
    public sealed class SwingTally
    {
        public const int DetailedSwings = 10;
        public const int SummaryEvery = 50;

        public int Swings { get; private set; }
        public int HitTarget { get; private set; }
        public int HitOther { get; private set; }
        public int HitNothing { get; private set; }

        public SwingLogKind Record(SwingResult result)
        {
            Swings++;
            if (result == SwingResult.HitTarget) HitTarget++;
            else if (result == SwingResult.HitOther) HitOther++;
            else HitNothing++;
            if (Swings <= DetailedSwings) return SwingLogKind.Detail;
            return Swings % SummaryEvery == 0 ? SwingLogKind.Summary : SwingLogKind.None;
        }
    }
}
