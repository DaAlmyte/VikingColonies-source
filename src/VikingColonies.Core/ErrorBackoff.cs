using System;

namespace VikingColonies.Core
{
    /// <summary>
    /// What a settler does after an error in his work: he pauses for a growing time (5 s, 10 s, 20 s … up to a minute)
    /// and starts over; a minute of work without errors brings the pause back to 5 s. Errors are reported at most once a
    /// minute, with how many there were, so a broken job doesn't flood the log.
    /// </summary>
    public sealed class ErrorBackoff
    {
        public const float FirstPause = 5f;
        public const float MaxPause = 60f;
        public const float CalmAfter = 60f;
        public const float ReportEvery = 60f;

        private float _pauseUntil = float.NegativeInfinity;
        private float _nextPause = FirstPause;
        private float _lastReport = float.NegativeInfinity;
        private int _unreported;

        /// <summary>How long the last error paused him, in seconds.</summary>
        public float LastPause { get; private set; }

        public bool Paused(float now) => now < _pauseUntil;

        /// <summary>Records an error at <paramref name="now"/>; returns how many errors to report now (0: stay quiet).</summary>
        public int Fail(float now)
        {
            if (now - _pauseUntil > CalmAfter) _nextPause = FirstPause;
            LastPause = _nextPause;
            _pauseUntil = now + LastPause;
            _nextPause = Math.Min(MaxPause, _nextPause * 2f);
            _unreported++;
            if (now - _lastReport < ReportEvery) return 0;
            _lastReport = now;
            int count = _unreported;
            _unreported = 0;
            return count;
        }
    }
}
