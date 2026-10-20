namespace VikingColonies.Core
{
    /// <summary>What a settler does about a chest he wants to change: use it, wait, ask its owner, or take it over.</summary>
    public enum StorageStep { Use, Busy, Wait, Ask, Take }

    /// <summary>
    /// A chest owned by another computer is asked for: its owner sends its latest contents and hands it over, as the game
    /// does when a player opens a chest. Taking it over directly could save an old copy over a friend's change.
    /// </summary>
    public sealed class StorageClaim
    {
        public const float AskAgainAfter = 1f;   // nothing came back: ask again
        public const int AsksBeforeTaking = 5;   // the owner never answers (gone, or no such request on his side): take it over

        private long _askedOwner;   // 0: no asking under way
        private int _asks;
        private float _lastAsked;

        /// <param name="isOwner">This computer owns the chest.</param>
        /// <param name="openHere">The chest is open on this computer.</param>
        /// <param name="openOnNetwork">The chest's shared "in use" flag is set: a player on another computer has it open, or
        /// had it when he crashed. Its owner is asked all the same and decides; it is never taken over.</param>
        /// <param name="owner">The computer that owns it; 0 when none does (nobody then holds newer contents).</param>
        public StorageStep Next(bool isOwner, bool openHere, bool openOnNetwork, long owner, float now)
        {
            if (isOwner || openHere || owner == 0L)
            {
                _askedOwner = 0L;
                return isOwner ? StorageStep.Use : openHere ? StorageStep.Busy : StorageStep.Take;
            }
            if (owner != _askedOwner)   // first ask, or the chest changed hands since: ask its owner now
            {
                _askedOwner = owner;
                _asks = 0;
            }
            else if (now - _lastAsked < AskAgainAfter) return StorageStep.Wait;
            if (_asks >= AsksBeforeTaking && !openOnNetwork)
            {
                _askedOwner = 0L;
                return StorageStep.Take;
            }
            if (!openOnNetwork) _asks++;   // asks a player had it open for don't count: the owner answered them
            _lastAsked = now;
            return StorageStep.Ask;
        }
    }
}
