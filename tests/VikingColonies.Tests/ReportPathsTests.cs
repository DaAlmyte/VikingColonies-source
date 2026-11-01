using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>The security sweep of 27 Oct 2026: a bug report's lines never name the Windows account.</summary>
    public class ReportPathsTests
    {
        private const string BepInEx = @"C:\Users\Someone\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Default\BepInEx";
        private const string Game = @"D:\Steam\steamapps\common\Valheim";
        private const string Home = @"C:\Users\Someone";

        private static string Scrub(string line) => ReportPaths.Scrub(line, BepInEx, Game, Home);

        [Fact]
        public void The_mods_and_the_games_folders_and_the_home_folder_are_shortened()
        {
            Assert.Equal(@"Couldn't read <BepInEx>\config\VikingColonies\blueprints\farm.blueprint", Scrub(@"Couldn't read " + BepInEx + @"\config\VikingColonies\blueprints\farm.blueprint"));
            Assert.Equal(@"<game>\valheim_Data", Scrub(Game + @"\valheim_Data"));
            Assert.Equal(@"~\Desktop\x.txt", Scrub(Home + @"\Desktop\x.txt"));
        }

        [Theory]
        [InlineData(@"c:\users\someone\appdata\roaming\r2modmanplus-local\valheim\profiles\default\bepinex\x.dll", @"<BepInEx>\x.dll")]   // any case
        [InlineData("C:/Users/Someone/AppData/Roaming/r2modmanPlus-local/Valheim/profiles/Default/BepInEx/x.dll", "<BepInEx>/x.dll")]   // forward slashes
        [InlineData("C:/Users/Someone/Desktop", "~/Desktop")]
        public void A_folder_in_another_case_or_with_forward_slashes_is_shortened_too(string line, string scrubbed)
        {
            Assert.Equal(scrubbed, Scrub(line));
        }

        [Theory]
        [InlineData(@"at E:\Users\Other\mods\a.cs:12", @"at ~\mods\a.cs:12")]
        [InlineData("'C:/users/Other Name/x'", "'~/x'")]
        public void Any_other_users_folder_loses_its_name(string line, string scrubbed)
        {
            Assert.Equal(scrubbed, ReportPaths.Scrub(line, "", "", ""));
        }

        [Fact]
        public void A_folder_is_shortened_only_where_it_ends()
        {
            Assert.Equal(@"E:\Home\meadow\x", ReportPaths.Scrub(@"E:\Home\meadow\x", "", "", @"E:\Home\me"));
            Assert.Equal(@"~\x and ~", ReportPaths.Scrub(@"E:\Home\me\x and E:\Home\me", "", "", @"E:\Home\me"));
        }

        [Theory]
        [InlineData("/")]
        [InlineData(@"D:\")]
        [InlineData("   ")]
        public void A_root_or_empty_folder_shortens_nothing(string home)
        {
            Assert.Equal(@"D:\Games and /tmp", ReportPaths.Scrub(@"D:\Games and /tmp", "", "", home));
        }

        [Fact]
        public void Matching_is_the_same_in_every_language()
        {
            System.Globalization.CultureInfo before = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");
                Assert.Equal(@"<game>\x", ReportPaths.Scrub(@"E:\GAMES\IRIS\x", "", @"E:\Games\iris", ""));
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = before;
            }
        }

        [Theory]
        [InlineData(null, "")]
        [InlineData("", "")]
        [InlineData("Bjorn walked to the Farm at (10.0, 32.5, -4.0)", "Bjorn walked to the Farm at (10.0, 32.5, -4.0)")]
        public void Other_lines_stay_as_they_are(string line, string scrubbed)
        {
            Assert.Equal(scrubbed, ReportPaths.Scrub(line, null, null, null));
        }
    }
}
