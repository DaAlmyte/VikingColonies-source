using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class DeathNoticeTests
    {
        private static readonly GroundPoint Hall = new GroundPoint(0f, 0f);
        private static readonly GroundPoint Death = new GroundPoint(100f, 0f);

        [Fact]
        public void A_player_in_the_colony_area_is_told()
        {
            Assert.True(DeathNotice.ShouldTell(new GroundPoint(0f, 49f), Death, true, Hall, 50f));
            Assert.False(DeathNotice.ShouldTell(new GroundPoint(0f, -51f), Death, true, Hall, 50f));
        }

        [Fact]
        public void A_player_near_where_it_happened_is_told()
        {
            Assert.True(DeathNotice.ShouldTell(new GroundPoint(140f, 0f), Death, true, Hall, 50f));
            Assert.True(DeathNotice.ShouldTell(new GroundPoint(140f, 0f), Death, false, Hall, 50f));   // no Town Hall known
        }

        [Fact]
        public void A_player_far_from_both_is_not_told()
        {
            Assert.False(DeathNotice.ShouldTell(new GroundPoint(-200f, 0f), Death, true, Hall, 50f));
            Assert.False(DeathNotice.ShouldTell(new GroundPoint(0f, 10f), Death, false, Hall, 50f));   // the hall's area doesn't count without a hall
        }

        [Fact]
        public void Only_centre_and_chat_shows_the_centre_message()
        {
            Assert.True(DeathNotice.ShowsCentre(DeathMessageStyle.CentreAndChat));
            Assert.False(DeathNotice.ShowsCentre(DeathMessageStyle.ChatOnly));
        }

        [Theory]
        [InlineData("Ingrid", "Lumberjack", "Ingrid the Lumberjack")]
        [InlineData("Ingrid", "", "Ingrid")]
        [InlineData("", "Builder", "Builder")]
        [InlineData("", "", "Jobless")]
        public void The_display_name(string name, string title, string expected)
        {
            Assert.Equal(expected, NorseNames.Display(name, title, "Jobless"));
        }
    }
}
