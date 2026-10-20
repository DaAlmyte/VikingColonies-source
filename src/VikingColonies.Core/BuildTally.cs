namespace VikingColonies.Core
{
    public enum BuildOutcome { Placed, Fell }

    /// <summary>Counts a builder's pieces and says which to write to the log: the first ten in detail, then a summary every fifty.</summary>
    public sealed class BuildTally
    {
        public const int DetailedEvents = 10;
        public const int SummaryEvery = 50;

        public int Placed { get; private set; }
        public int Fell { get; private set; }

        public SwingLogKind Record(BuildOutcome outcome)
        {
            if (outcome == BuildOutcome.Placed) Placed++;
            else Fell++;
            int events = Placed + Fell;
            if (events <= DetailedEvents) return SwingLogKind.Detail;
            return events % SummaryEvery == 0 ? SwingLogKind.Summary : SwingLogKind.None;
        }
    }
}
