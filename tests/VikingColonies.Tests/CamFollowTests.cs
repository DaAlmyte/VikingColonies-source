using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>vc_cam follow (1.4.2, for the store pictures): where the camera sits around a settler.</summary>
    public class CamFollowTests
    {
        private static void Near(float x, float y, float z, Vec3 v)
        {
            Assert.Equal(x, v.X, 3);
            Assert.Equal(y, v.Y, 3);
            Assert.Equal(z, v.Z, 3);
        }

        [Fact]
        public void Angle_0_is_in_front_of_the_settler_looking_at_their_face()
        {
            Near(0f, 2f, 6f, CamFollow.Offset(facingYaw: 0f, distance: 6f, height: 2f, angle: 0f));
        }

        [Fact]
        public void Angle_180_is_behind_and_90_to_their_right()
        {
            Near(0f, 2f, -6f, CamFollow.Offset(0f, 6f, 2f, 180f));
            Near(6f, 2f, 0f, CamFollow.Offset(0f, 6f, 2f, 90f));
        }

        [Fact]
        public void It_turns_with_the_settler()
        {
            Near(6f, 1f, 0f, CamFollow.Offset(90f, 6f, 1f, 0f));
            Near(-4f, 3f, 0f, CamFollow.Offset(90f, 4f, 3f, 180f));
        }

        [Fact]
        public void A_settler_is_found_by_name_or_by_job()
        {
            Assert.True(CamFollow.Matches("ingrid", "Ingrid", "builder"));
            Assert.True(CamFollow.Matches("Lumberjack", "Ulf", "lumberjack"));
            Assert.False(CamFollow.Matches("guard", "Ulf", "lumberjack"));
            Assert.False(CamFollow.Matches("", "Ulf", "lumberjack"));
        }

        [Fact]
        public void Hud_or_tags_as_the_last_word_keeps_the_screen_with_name_tags_and_speech()
        {
            Assert.True(CamFollow.KeepsHud(new[] { "follow", "Runa", "6", "hud" }));
            Assert.True(CamFollow.KeepsHud(new[] { "builder", "0", "2", "5", "TAGS" }));
            Assert.False(CamFollow.KeepsHud(new[] { "follow", "Runa", "6" }));
            Assert.False(CamFollow.KeepsHud(new string[0]));
            Assert.False(CamFollow.KeepsHud(null));
        }

        [Fact]
        public void Talking_follows_whoever_spoke_last_not_a_settler_by_that_name()
        {
            Assert.True(CamFollow.FollowsLastSpeaker("talking"));
            Assert.True(CamFollow.FollowsLastSpeaker("Talking"));
            Assert.False(CamFollow.FollowsLastSpeaker("Torvald"));
            Assert.False(CamFollow.Matches("talking", "Torvald", "builder"));
        }

        [Fact]
        public void Any_is_anyone_and_jobless_is_a_settler_without_a_job()
        {
            Assert.True(CamFollow.Matches("any", "Ulf", "lumberjack"));
            Assert.True(CamFollow.Matches("Jobless", "Runa", ""));
            Assert.True(CamFollow.Matches("jobless", "Runa", null));
            Assert.False(CamFollow.Matches("jobless", "Ulf", "lumberjack"));
        }
    }
}
