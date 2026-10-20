using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>
    /// The 1.4.0 review's deferred minors (8 and 9, 1.4.1 A8): the camp's waiting reason was stored in its owner's language,
    /// so everyone else read it in theirs; and a red camp site was surveyed again every 15 s.
    /// </summary>
    public class CampWaitTests
    {
        [Fact]
        public void The_waiting_reason_is_stored_as_its_key_with_its_number_and_read_back()
        {
            string stored = CampWait.Store("$vc_camp_needs_builder", 3);
            Assert.True(CampWait.Read(stored, out string token, out int number));
            Assert.Equal("$vc_camp_needs_builder", token);
            Assert.Equal(3, number);
        }

        [Fact]
        public void A_reason_without_a_number_reads_back_as_itself()
        {
            Assert.False(CampWait.Read(CampWait.Store("$vc_site_red_unbreakable"), out string token, out int number));
            Assert.Equal("$vc_site_red_unbreakable", token);
            Assert.Equal(0, number);
        }

        [Fact]
        public void Text_stored_by_1_4_0_reads_back_as_itself()
        {
            Assert.False(CampWait.Read("A rock nothing can break stands where it goes", out string token, out _));
            Assert.Equal("A rock nothing can break stands where it goes", token);
            Assert.False(CampWait.Read("", out token, out _));
            Assert.Equal("", token);
        }

        [Fact]
        public void A_red_camp_site_is_looked_at_again_after_a_minute_not_every_15_seconds()
        {
            Assert.False(CampRules.LookAgain(lastRedAt: 100.0, now: 100.0 + CampRules.RedRecheckSeconds - 1));
            Assert.True(CampRules.LookAgain(lastRedAt: 100.0, now: 100.0 + CampRules.RedRecheckSeconds));
            Assert.True(CampRules.LookAgain(lastRedAt: double.NegativeInfinity, now: 5.0));   // never red: look now
            Assert.True(CampRules.RedRecheckSeconds >= 60.0);
        }
    }
}
