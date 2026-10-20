using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>
    /// 1.4.1, A7: a player's grave (or a crate from a sunk ship) on a site is no building piece, and the 1.4.0 scan would
    /// have called it "a rock nothing can break". It goes once it is emptied: the site is red and says so.
    /// </summary>
    public class GraveOnSiteTests
    {
        [Fact]
        public void A_grave_or_a_crate_turns_the_site_red()
        {
            var findings = new SiteFindings();
            findings.AddRed(RedReason.Grave);
            SiteVerdict verdict = SiteCheck.Judge(findings);
            Assert.Equal(SiteColour.Red, verdict.Colour);
            Assert.Equal(RedReason.Grave, verdict.Red);
        }

        [Fact]
        public void The_grave_is_named_before_a_rock_and_after_another_building()
        {
            var rock = new SiteFindings();
            rock.AddRed(RedReason.Unbreakable);
            rock.AddRed(RedReason.Grave);
            Assert.Equal(RedReason.Grave, SiteCheck.Judge(rock).Red);   // the one the player can clear first
            var building = new SiteFindings();
            building.AddRed(RedReason.Grave);
            building.AddRed(RedReason.OtherBuilding);
            Assert.Equal(RedReason.OtherBuilding, SiteCheck.Judge(building).Red);
        }
    }
}
