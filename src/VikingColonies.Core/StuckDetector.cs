using System;

namespace VikingColonies.Core
{
    /// <summary>Says "stuck" when the distance to a goal hasn't shrunk by minProgress for timeout seconds.</summary>
    public sealed class StuckDetector
    {
        private readonly float _minProgress;
        private readonly double _timeout;
        private float _best = float.MaxValue;
        private double _lastProgress = double.NaN;

        public StuckDetector(float minProgressMeters = 0.5f, double timeoutSeconds = 10)
        {
            _minProgress = minProgressMeters;
            _timeout = timeoutSeconds;
        }

        public void Reset()
        {
            _best = float.MaxValue;
            _lastProgress = double.NaN;
        }

        public bool Update(float distance, double now)
        {
            if (double.IsNaN(_lastProgress) || distance < _best - _minProgress)
            {
                _best = Math.Min(_best, distance);
                _lastProgress = now;
                return false;
            }
            return now - _lastProgress >= _timeout;
        }
    }
}
