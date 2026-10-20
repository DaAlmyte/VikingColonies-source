using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class StorageAccessTests
    {
        // Review bug 2: taking a chest over directly could save an old copy over a friend's change. The settler asks the
        // chest's owner to hand it over with its latest contents, as the game does when a player opens a chest.
        private const long Owner = 7L, OtherOwner = 8L;

        [Fact]
        public void A_chest_owned_elsewhere_is_asked_for_then_waited_for()
        {
            var claim = new StorageClaim();
            Assert.Equal(StorageStep.Ask, claim.Next(isOwner: false, openHere: false, openOnNetwork: false, owner: Owner, now: 10f));
            Assert.Equal(StorageStep.Wait, claim.Next(false, false, false, Owner, 10.5f));
            Assert.Equal(StorageStep.Ask, claim.Next(false, false, false, Owner, 11f));   // nothing came back: ask again
            Assert.Equal(StorageStep.Use, claim.Next(true, false, false, Owner, 11.2f));  // handed over
        }

        [Fact]
        public void An_owner_who_never_answers_five_asks_is_taken_over()
        {
            var claim = new StorageClaim();
            for (int i = 0; i < StorageClaim.AsksBeforeTaking; i++)
                Assert.Equal(StorageStep.Ask, claim.Next(false, false, false, Owner, i));
            Assert.Equal(StorageStep.Take, claim.Next(false, false, false, Owner, StorageClaim.AsksBeforeTaking));
        }

        // Review of the fix: a settler who retries every 5-10 s must still ask, not take the chest over.
        [Fact]
        public void A_slow_caller_asks_again_rather_than_taking_over()
        {
            var claim = new StorageClaim();
            Assert.Equal(StorageStep.Ask, claim.Next(false, false, false, Owner, 0f));
            Assert.Equal(StorageStep.Ask, claim.Next(false, false, false, Owner, 10f));
        }

        [Fact]
        public void When_the_chest_changed_hands_since_he_asked_he_asks_the_new_owner_afresh()
        {
            var claim = new StorageClaim();
            for (int i = 0; i < StorageClaim.AsksBeforeTaking; i++) claim.Next(false, false, false, Owner, i);
            Assert.Equal(StorageStep.Ask, claim.Next(false, false, false, OtherOwner, 10f));
        }

        [Fact]
        public void A_chest_nobody_owns_is_taken_at_once_and_one_open_here_is_busy()
        {
            Assert.Equal(StorageStep.Take, new StorageClaim().Next(false, false, false, owner: 0L, now: 0f));
            Assert.Equal(StorageStep.Busy, new StorageClaim().Next(false, openHere: true, openOnNetwork: false, owner: Owner, now: 0f));
        }

        // A player who crashed with the chest open leaves its "in use" flag behind: the settler still asks, and the chest's
        // owner decides by whether a player really has it open there.
        [Fact]
        public void A_chest_marked_open_elsewhere_is_asked_for()
        {
            Assert.Equal(StorageStep.Ask, new StorageClaim().Next(false, false, openOnNetwork: true, owner: Owner, now: 0f));
        }

        [Fact]
        public void Once_the_player_closes_it_the_settler_asks_again_rather_than_taking_it()
        {
            var claim = new StorageClaim();
            for (int i = 0; i < StorageClaim.AsksBeforeTaking * 2; i++) claim.Next(false, false, openOnNetwork: true, owner: Owner, now: i);
            Assert.Equal(StorageStep.Ask, claim.Next(false, false, openOnNetwork: false, owner: Owner, now: 20f));
        }

        [Fact]
        public void A_chest_marked_open_elsewhere_is_never_taken_over()
        {
            var claim = new StorageClaim();
            for (int i = 0; i < StorageClaim.AsksBeforeTaking * 3; i++)
                Assert.NotEqual(StorageStep.Take, claim.Next(false, false, openOnNetwork: true, owner: Owner, now: i));
        }

        [Fact]
        public void Once_handed_over_the_next_claim_starts_afresh()
        {
            var claim = new StorageClaim();
            claim.Next(false, false, false, Owner, 0f);
            claim.Next(true, false, false, Owner, 1f);
            Assert.Equal(StorageStep.Ask, claim.Next(false, false, false, Owner, 30f));
            Assert.Equal(StorageStep.Wait, claim.Next(false, false, false, Owner, 30.5f));
        }
    }
}
