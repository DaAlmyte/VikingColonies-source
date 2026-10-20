namespace VikingColonies.Core
{
    public enum SettleState
    {
        Moving,
        Settled,
        GaveUp,
    }

    /// <summary>
    /// Waiting for logs to stop moving: settled once the fastest log nearby has stayed at or under StillSpeed for
    /// StillSeconds; given up after GiveUpSeconds (a log rocking forever mustn't stop the work). Times are in seconds.
    /// </summary>
    public sealed class LogSettle
    {
        public const float StillSpeed = 0.3f;
        public const double StillSeconds = 1.0;
        public const double GiveUpSeconds = 15.0;

        private readonly double _start;
        private double _stillSince = double.NaN;

        public LogSettle(double start)
        {
            _start = start;
        }

        public static bool IsMoving(float speed) => speed > StillSpeed;

        /// <summary>How far above the water surface a log's middle may ride on a wave and still count as afloat.</summary>
        public const float AfloatMargin = 0.5f;

        /// <summary>
        /// Afloat: the log's middle is at the water surface or below. Being near water isn't enough — a log lying on the
        /// shore or falling down a slope by the sea is on land.
        /// </summary>
        public static bool IsAfloat(float centreHeight, float liquidLevel) => centreHeight < liquidLevel + AfloatMargin;

        /// <summary>A log afloat bobs on the waves for ever: it doesn't count, or the wait would never end.</summary>
        public static bool IsMoving(float speed, bool afloat) => !afloat && IsMoving(speed);

        public SettleState Update(float fastestSpeed, double now)
        {
            if (IsMoving(fastestSpeed)) _stillSince = double.NaN;
            else if (double.IsNaN(_stillSince)) _stillSince = now;
            if (!double.IsNaN(_stillSince) && now - _stillSince >= StillSeconds) return SettleState.Settled;
            return now - _start >= GiveUpSeconds ? SettleState.GaveUp : SettleState.Moving;
        }
    }
}
