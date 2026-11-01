using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class BlueprintBackupsTests
    {
        // ---- The copy's name: <file>.blueprint.bak, then .bak2, .bak3 …, the highest the newest ----

        [Fact]
        public void The_first_copy_is_the_file_with_bak_on_the_end()
        {
            Assert.Equal("builder_hut.blueprint.bak", BlueprintBackups.CopyName("builder_hut.blueprint", new string[0]));
            Assert.Equal("builder_hut.blueprint.bak", BlueprintBackups.CopyName("builder_hut.blueprint", new[] { "builder_hut.blueprint" }));
        }

        [Fact]
        public void The_next_copy_goes_one_past_the_highest_number()
        {
            Assert.Equal("farm_3.blueprint.bak2", BlueprintBackups.CopyName("farm_3.blueprint", new[] { "farm_3.blueprint.bak" }));
            Assert.Equal("farm_3.blueprint.bak3", BlueprintBackups.CopyName("farm_3.blueprint", new[] { "farm_3.blueprint.bak", "farm_3.blueprint.bak2" }));
        }

        [Fact]
        public void A_gap_is_never_filled_so_the_highest_stays_the_newest()
        {
            Assert.Equal("farm.blueprint.bak4", BlueprintBackups.CopyName("farm.blueprint", new[] { "farm.blueprint.bak", "farm.blueprint.bak3" }));
            Assert.Equal("farm.blueprint.bak3", BlueprintBackups.CopyName("farm.blueprint", new[] { "farm.blueprint.bak2" }));
        }

        [Fact]
        public void Other_files_dont_count_and_case_is_ignored_as_windows_does()
        {
            var names = new[] { "farm_2.blueprint.bak7", "farm.blueprint.bakx", "farm.blueprint.tmp", "Farm.Blueprint.BAK2" };
            Assert.Equal("farm.blueprint.bak3", BlueprintBackups.CopyName("farm.blueprint", names));
        }

        [Fact]
        public void No_copy_name_past_the_most_copies()
        {
            Assert.Null(BlueprintBackups.CopyName("farm.blueprint", new[] { "farm.blueprint.bak" + BlueprintBackups.MaxCopies }));
            Assert.NotNull(BlueprintBackups.CopyName("farm.blueprint", new[] { "farm.blueprint.bak" + (BlueprintBackups.MaxCopies - 1) }));
        }

        [Fact]
        public void The_copy_never_ends_in_blueprint_so_it_is_never_used_as_a_design()
        {
            Assert.False(BlueprintBackups.CopyName("farm.blueprint", new string[0]).EndsWith(".blueprint"));
            Assert.False(BlueprintBackups.CopyName("farm.blueprint", new[] { "farm.blueprint.bak" }).EndsWith(".blueprint"));
        }

        // ---- Whether a copy is needed ----

        [Fact]
        public void A_design_with_other_lines_is_kept()
        {
            Assert.True(BlueprintBackups.KeepOld(new[] { "#Name:farm", "piece_a" }, new[] { "#Name:farm", "piece_b" }));
            Assert.True(BlueprintBackups.KeepOld(new[] { "#Name:farm", "piece_a" }, new[] { "#Name:farm", "piece_a", "piece_b" }));
        }

        [Fact]
        public void The_same_lines_again_keep_no_copy()
        {
            Assert.False(BlueprintBackups.KeepOld(new[] { "#Name:farm", "piece_a" }, new[] { "#Name:farm", "piece_a" }));
        }

        [Fact]
        public void A_file_that_could_not_be_read_is_kept()
        {
            Assert.True(BlueprintBackups.KeepOld(null, new[] { "#Name:farm" }));
        }

        // ---- The save itself: nothing saved before is ever lost ----

        private static readonly string[] A = { "#Name:farm", "piece_a" };
        private static readonly string[] B = { "#Name:farm", "piece_b" };
        private static readonly string[] C = { "#Name:farm", "piece_c" };

        [Fact]
        public void A_first_save_writes_the_design_and_keeps_no_copy()
        {
            var folder = new FakeFolder();
            DesignSave save = BlueprintBackups.Save(folder, "farm.blueprint", A);
            Assert.True(save.Saved);
            Assert.Null(save.Kept);
            Assert.Null(save.Problem);
            Assert.Equal(A, folder.Files["farm.blueprint"]);
            Assert.Equal(new[] { "farm.blueprint" }, folder.Files.Keys.ToArray());   // no .tmp left
        }

        [Fact]
        public void Saving_over_another_design_keeps_the_old_one_as_a_copy()
        {
            var folder = new FakeFolder();
            folder.Files["farm.blueprint"] = A.ToList();
            DesignSave save = BlueprintBackups.Save(folder, "farm.blueprint", B);
            Assert.True(save.Saved);
            Assert.Equal("farm.blueprint.bak", save.Kept);
            Assert.Equal(A, folder.Files["farm.blueprint.bak"]);
            Assert.Equal(B, folder.Files["farm.blueprint"]);
            Assert.False(folder.Files.ContainsKey("farm.blueprint.tmp"));
        }

        [Fact]
        public void The_design_just_replaced_is_always_in_the_highest_copy()
        {
            var folder = new FakeFolder();
            BlueprintBackups.Save(folder, "farm.blueprint", A);
            BlueprintBackups.Save(folder, "farm.blueprint", B);
            DesignSave third = BlueprintBackups.Save(folder, "farm.blueprint", C);
            Assert.Equal("farm.blueprint.bak2", third.Kept);
            Assert.Equal(A, folder.Files["farm.blueprint.bak"]);
            Assert.Equal(B, folder.Files["farm.blueprint.bak2"]);
            Assert.Equal(C, folder.Files["farm.blueprint"]);
        }

        [Fact]
        public void The_very_same_lines_again_write_nothing_and_keep_no_copy()
        {
            var folder = new FakeFolder();
            folder.Files["farm.blueprint"] = A.ToList();
            DesignSave save = BlueprintBackups.Save(folder, "farm.blueprint", A.ToArray());
            Assert.True(save.Saved);
            Assert.Null(save.Kept);
            Assert.Equal(0, folder.Writes);   // a write that failed half-way would have damaged the only copy
            Assert.Single(folder.Files);
        }

        [Fact]
        public void An_old_design_that_cant_be_read_is_kept_all_the_same()
        {
            var folder = new FakeFolder { FailRead = true };
            folder.Files["farm.blueprint"] = A.ToList();
            DesignSave save = BlueprintBackups.Save(folder, "farm.blueprint", A);
            Assert.True(save.Saved);
            Assert.Equal("farm.blueprint.bak", save.Kept);
            Assert.Equal(A, folder.Files["farm.blueprint.bak"]);
        }

        [Fact]
        public void When_the_copy_fails_nothing_is_saved_and_the_old_design_stays()
        {
            var folder = new FakeFolder { FailCopy = true };
            folder.Files["farm.blueprint"] = A.ToList();
            DesignSave save = BlueprintBackups.Save(folder, "farm.blueprint", B);
            Assert.False(save.Saved);
            Assert.Null(save.Kept);
            Assert.Contains("nothing was saved", save.Problem);
            Assert.Equal(A, folder.Files["farm.blueprint"]);
            Assert.Equal(new[] { "farm.blueprint" }, folder.Files.Keys.ToArray());   // the .tmp tidied away
        }

        [Fact]
        public void When_the_new_design_cant_be_written_nothing_changes()
        {
            var folder = new FakeFolder { FailWrite = true };
            folder.Files["farm.blueprint"] = A.ToList();
            DesignSave save = BlueprintBackups.Save(folder, "farm.blueprint", B);
            Assert.False(save.Saved);
            Assert.Contains("nothing was saved", save.Problem);
            Assert.Equal(A, folder.Files["farm.blueprint"]);
            Assert.Equal(new[] { "farm.blueprint" }, folder.Files.Keys.ToArray());   // no copy, no .tmp
        }

        [Fact]
        public void When_the_new_design_cant_take_its_place_both_are_still_there_and_the_console_says_where()
        {
            var folder = new FakeFolder { FailMove = true };
            folder.Files["farm.blueprint"] = A.ToList();
            DesignSave save = BlueprintBackups.Save(folder, "farm.blueprint", B);
            Assert.False(save.Saved);
            Assert.Equal("farm.blueprint.bak", save.Kept);
            Assert.Equal(A, folder.Files["farm.blueprint.bak"]);
            Assert.Equal(B, folder.Files["farm.blueprint.tmp"]);
            Assert.Contains("farm.blueprint.tmp", save.Problem);
            Assert.Contains("farm.blueprint.bak", save.Problem);
        }

        [Fact]
        public void Past_the_most_copies_nothing_is_saved()
        {
            var folder = new FakeFolder();
            folder.Files["farm.blueprint"] = A.ToList();
            folder.Files["farm.blueprint.bak" + BlueprintBackups.MaxCopies] = A.ToList();
            DesignSave save = BlueprintBackups.Save(folder, "farm.blueprint", B);
            Assert.False(save.Saved);
            Assert.Equal(A, folder.Files["farm.blueprint"]);
            Assert.False(folder.Files.ContainsKey("farm.blueprint.tmp"));
        }

        /// <summary>A design folder in memory, with names that ignore case as Windows' do, and each step able to fail.</summary>
        private sealed class FakeFolder : IDesignFolder
        {
            public readonly Dictionary<string, List<string>> Files = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            public bool FailRead, FailWrite, FailCopy, FailMove;
            public int Writes;

            public IEnumerable<string> Names() => Files.Keys.ToList();
            public bool Exists(string name) => Files.ContainsKey(name);

            public IList<string> ReadLines(string name)
            {
                if (FailRead) throw new IOException("locked");
                return Files[name].ToList();
            }

            public void WriteLines(string name, IList<string> lines)
            {
                Writes++;
                if (FailWrite)
                {
                    Files[name] = lines.Take(1).ToList();   // half-written
                    throw new IOException("disk full");
                }
                Files[name] = lines.ToList();
            }

            public void Copy(string from, string to)
            {
                if (FailCopy) throw new UnauthorizedAccessException("read-only");
                if (Files.ContainsKey(to)) throw new IOException("exists");
                Files[to] = Files[from].ToList();
            }

            public void Delete(string name) => Files.Remove(name);

            public void Move(string from, string to)
            {
                if (FailMove) throw new IOException("in use");
                if (Files.ContainsKey(to)) throw new IOException("exists");
                Files[to] = Files[from];
                Files.Remove(from);
            }
        }
    }
}
