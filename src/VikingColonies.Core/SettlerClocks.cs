namespace VikingColonies.Core
{
    /// <summary>
    /// The job's last errand before night (a lumberjack's load to the hut, a courier's put-back) is waited for a minute at
    /// most; then the settler goes in with it still in hand, and it stays given up until the morning (the review round of
    /// 2026-10-23: one who couldn't reach the hut with a load never went in, all night).
    /// </summary>
    public sealed class FinishingClock
    {
        public const float Limit = 60f;

        private float _since = -1f;
        private bool _gaveUp;

        /// <summary>Whether to ask the job at all: not once given up (until <see cref="Reset"/>).</summary>
        public bool Asks => !_gaveUp;

        /// <summary>The job says it is still finishing, or done: whether to wait for it now.</summary>
        public bool Wait(bool stillFinishing, float now)
        {
            if (_gaveUp) return false;
            if (!stillFinishing)
            {
                _since = -1f;
                return false;
            }
            if (_since < 0f) _since = now;
            if (now - _since < Limit) return true;
            _gaveUp = true;
            return false;
        }

        /// <summary>The morning: the next evening's errand is waited for again.</summary>
        public void Reset()
        {
            _since = -1f;
            _gaveUp = false;
        }
    }

    /// <summary>
    /// A change saved at once when the last save was a while ago, else kept and saved once the gap is over (the review round
    /// of 2026-10-23: two statuses taking turns every tick each saved the settler's whole record, about 100 times a second,
    /// sent to every other player).
    /// </summary>
    public sealed class SaveThrottle
    {
        public const float Gap = 0.5f;

        private float _last = float.NegativeInfinity;
        private bool _waiting;

        /// <summary>A change now: true when it is to be saved at once (then it counts as saved).</summary>
        public bool SaveNow(float now)
        {
            if (now - _last >= Gap)
            {
                Saved(now);
                return true;
            }
            _waiting = true;
            return false;
        }

        /// <summary>A change kept for later, whose time has come: save it, then call <see cref="Saved"/>.</summary>
        public bool Due(float now) => _waiting && now - _last >= Gap;

        /// <summary>Saved (by this or by any other save of the whole record).</summary>
        public void Saved(float now)
        {
            _last = now;
            _waiting = false;
        }

        /// <summary>
        /// A change kept back is dropped: another computer runs the settler now, or the record was loaded afresh (its review:
        /// saved after the settler came back, it wrote this computer's old copy over the other's).
        /// </summary>
        public void Forget() => _waiting = false;
    }
}
