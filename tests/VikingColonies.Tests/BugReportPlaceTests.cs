using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>1.6: where the bug report file is, in words, and opening its folder (Report a problem's window).</summary>
    public class BugReportPlaceTests
    {
        [Fact]
        public void The_account_folder_is_shortened_on_windows_case_blind_with_back_slashes()
        {
            Assert.Equal(@"%USERPROFILE%\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Default\BepInEx\VikingColonies-bugreport.txt",
                BugReportPlace.Shown(@"c:\users\DaAlmyte/AppData/Roaming/r2modmanPlus-local/Valheim/profiles/Default/BepInEx\VikingColonies-bugreport.txt",
                    @"C:\Users\DaAlmyte", true));
        }

        [Fact]
        public void Only_at_a_folder_boundary()
        {
            Assert.Equal(@"C:\Users\DaAlmytea\BepInEx\VikingColonies-bugreport.txt",
                BugReportPlace.Shown(@"C:\Users\DaAlmytea\BepInEx\VikingColonies-bugreport.txt", @"C:\Users\DaAlmyte", true));
            Assert.Equal("%USERPROFILE%", BugReportPlace.Shown(@"C:\Users\DaAlmyte", @"C:\Users\DaAlmyte\", true));
        }

        [Fact]
        public void Off_windows_the_home_is_a_tilde_and_case_counts()
        {
            Assert.Equal("~/.config/r2modmanPlus-local/BepInEx/VikingColonies-bugreport.txt",
                BugReportPlace.Shown("/home/daalmyte/.config/r2modmanPlus-local/BepInEx/VikingColonies-bugreport.txt", "/home/daalmyte", false));
            Assert.Equal("/home/DaAlmyte/BepInEx/x.txt", BugReportPlace.Shown("/home/DaAlmyte/BepInEx/x.txt", "/home/daalmyte", false));
        }

        [Fact]
        public void Elsewhere_or_without_a_home_it_is_as_it_was()
        {
            Assert.Equal(@"D:\Games\Valheim\BepInEx\VikingColonies-bugreport.txt",
                BugReportPlace.Shown(@"D:\Games\Valheim\BepInEx\VikingColonies-bugreport.txt", @"C:\Users\DaAlmyte", true));
            Assert.Equal(@"D:\Games\BepInEx\x.txt", BugReportPlace.Shown(@"D:\Games\BepInEx\x.txt", null, true));
            Assert.Equal(@"D:\Games\BepInEx\x.txt", BugReportPlace.Shown(@"D:\Games\BepInEx\x.txt", "", true));
            Assert.Equal("", BugReportPlace.Shown(null, @"C:\Users\DaAlmyte", true));
        }

        [Fact]
        public void An_error_has_the_account_folder_shortened_wherever_it_is()
        {
            Assert.Equal(@"Access to the path '%USERPROFILE%\BepInEx\VikingColonies-bugreport.txt' is denied.",
                BugReportPlace.Scrubbed(@"Access to the path 'c:\users\daalmyte\BepInEx\VikingColonies-bugreport.txt' is denied.", @"C:\Users\DaAlmyte", true));
            Assert.Equal("Disk full at ~/x and ~/y", BugReportPlace.Scrubbed("Disk full at /home/f/x and /home/f/y", "/home/f/", false));
            Assert.Equal("no path here", BugReportPlace.Scrubbed("no path here", null, true));
            Assert.Equal("", BugReportPlace.Scrubbed(null, @"C:\Users\DaAlmyte", true));
        }

        [Fact]
        public void An_error_is_scrubbed_only_at_a_folder_boundary_and_with_either_slash()
        {
            Assert.Equal(@"no room in C:\Users\DaAlmytea\x", BugReportPlace.Scrubbed(@"no room in C:\Users\DaAlmytea\x", @"C:\Users\DaAlmyte", true));
            Assert.Equal(@"'%USERPROFILE%/BepInEx/x.txt' and %USERPROFILE%.", BugReportPlace.Scrubbed(@"'C:/Users/DaAlmyte/BepInEx/x.txt' and C:\Users\DaAlmyte.", @"C:\Users\DaAlmyte", true));
        }

        [Fact]
        public void Under_proton_the_linux_home_is_shortened_too()
        {
            // The game thinks it's on Windows, with Wine's own user folder, while the mod manager's profile is in the Linux home.
            Assert.Equal(@"~\.config\r2modmanPlus-local\Valheim\profiles\Default\BepInEx\VikingColonies-bugreport.txt",
                BugReportPlace.Shown(@"Z:\home\deck\.config\r2modmanPlus-local\Valheim\profiles\Default\BepInEx\VikingColonies-bugreport.txt", @"C:\users\steamuser", true));
            Assert.Equal(@"Access to '~\.config\x.txt' is denied", BugReportPlace.Scrubbed(@"Access to 'Z:\home\deck\.config\x.txt' is denied", @"C:\users\steamuser", true));
            Assert.Equal(@"Z:\homes\deck\x.txt", BugReportPlace.Shown(@"Z:\homes\deck\x.txt", @"C:\users\steamuser", true));
        }

        [Fact]
        public void A_network_folders_address_and_a_bare_file_name()
        {
            Assert.Equal("file:////server/share%201/BepInEx/", BugReportPlace.FolderAddress(@"\\server\share 1\BepInEx\VikingColonies-bugreport.txt"));
            Assert.Equal("\"\"", BugReportPlace.ExplorerArguments("VikingColonies-bugreport.txt", false));   // Explorer then opens its own start page
        }

        [Fact]
        public void File_explorer_selects_the_file_or_opens_its_folder_when_it_isnt_there()
        {
            Assert.Equal("/select,\"C:\\Users\\a b\\BepInEx\\VikingColonies-bugreport.txt\"",
                BugReportPlace.ExplorerArguments(@"C:\Users\a b/BepInEx\VikingColonies-bugreport.txt", true));
            Assert.Equal("\"C:\\Users\\a b\\BepInEx\"",
                BugReportPlace.ExplorerArguments(@"C:\Users\a b\BepInEx\VikingColonies-bugreport.txt", false));
        }

        [Fact]
        public void The_folders_address_escapes_each_part_and_keeps_the_drive()
        {
            Assert.Equal("file:///C:/Users/a%20b/x%23y%25/BepInEx/",
                BugReportPlace.FolderAddress(@"C:\Users\a b\x#y%\BepInEx\VikingColonies-bugreport.txt"));
            Assert.Equal("file:///home/a%20b/BepInEx/", BugReportPlace.FolderAddress("/home/a b/BepInEx/VikingColonies-bugreport.txt"));
        }

        [Fact]
        public void The_files_name_and_folder()
        {
            Assert.Equal("VikingColonies-bugreport.txt", BugReportPlace.FileName);
            Assert.Equal(@"C:\x\BepInEx", BugReportPlace.Folder(@"C:\x\BepInEx\VikingColonies-bugreport.txt"));
            Assert.Equal("/x/BepInEx", BugReportPlace.Folder("/x/BepInEx/VikingColonies-bugreport.txt"));
            Assert.Equal("", BugReportPlace.Folder("VikingColonies-bugreport.txt"));
        }
    }
}
