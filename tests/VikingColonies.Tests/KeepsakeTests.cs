using System.Collections.Generic;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    // The colony keeps its citizens' records (0.10): a keepsake per settler on the Town Hall, so one whose body was lost
    // can be brought back as himself (from the 2026-10 review: "citizen data lives on the body").
    public class KeepsakeTests
    {
        private static CitizenRecord Ivar() => new CitizenRecord
        {
            Id = "abc", Name = "Ivar", Model = 0, Hair = "Hair3", Beard = "Beard2", ColonyId = "col1", ColonyPos = new[] { 1f, 2f, 3f },
            Workplace = "hut1", WorkplacePos = new[] { 4f, 5f, 6f }, Job = "lumberjack", Woodcutting = 12f, WoodcuttingXp = 0.4f,
            Home = "long1#3", HomePos = new[] { 7f, 8f, 9f }, Status = "$vc_status_chopping", Carry = "Wood:12", Hungry = true,
        };

        [Fact]
        public void A_keepsake_keeps_who_he_is_and_forgets_what_he_was_doing()
        {
            CitizenRecord kept = CitizenRecord.Decode(Keepsake.Of(Ivar()));
            Assert.Equal("abc", kept.Id);
            Assert.Equal("Ivar", kept.Name);
            Assert.Equal("Hair3", kept.Hair);
            Assert.Equal("lumberjack", kept.Job);
            Assert.Equal("hut1", kept.Workplace);
            Assert.Equal("long1#3", kept.Home);
            Assert.Equal(12f, kept.Woodcutting);
            Assert.Equal("", kept.Status);
            Assert.Equal("", kept.Carry);
            Assert.Equal(0f, kept.WoodcuttingXp);
            Assert.False(kept.Hungry);
        }

        [Fact]
        public void A_keepsake_changes_only_when_who_he_is_changes()
        {
            // So the Town Hall rewrites its keepsakes on a new job, home or skill level, not on every step or axe hit.
            CitizenRecord a = Ivar(), b = Ivar();
            b.Status = "$vc_status_delivering";
            b.Carry = "";
            b.WoodcuttingXp = 0.9f;
            b.Hungry = false;
            Assert.Equal(Keepsake.Of(a), Keepsake.Of(b));
            b.Woodcutting = 13f;
            Assert.NotEqual(Keepsake.Of(a), Keepsake.Of(b));
        }

        [Fact]
        public void The_keepsakes_read_back_the_same_and_keep_only_the_colony_s_members()
        {
            var keep = new Keepsakes();
            Assert.True(keep.Set("abc", Keepsake.Of(Ivar())));
            Assert.False(keep.Set("abc", Keepsake.Of(Ivar())));   // nothing new
            keep.Set("def", "v=2;name=Astrid|the;bold");
            Keepsakes back = Keepsakes.Decode(keep.Encode());
            Assert.Equal("Ivar", CitizenRecord.Decode(back.Get("abc")).Name);
            Assert.Equal("v=2;name=Astrid|the;bold", back.Get("def"));
            Assert.True(back.Retain(new HashSet<string> { "abc" }));
            Assert.Null(back.Get("def"));
            Assert.Equal(0, Keepsakes.Decode(null).Count);
            Assert.Equal(0, Keepsakes.Decode("broken|||").Count);
        }
    
        [Fact]   // Review Focus: a keepsake keeps the 0.22 skills' levels, not their practice
        public void Keepsake_keeps_skill_levels_not_practice()
        {
            CitizenRecord r = Ivar();
            r.Farming = 5f; r.FarmingXp = 0.7f; r.Crafting = 2f; r.CraftingXp = 0.3f;
            CitizenRecord kept = CitizenRecord.Decode(Keepsake.Of(r));
            Assert.Equal(5f, kept.Farming);
            Assert.Equal(0f, kept.FarmingXp);
            Assert.Equal(2f, kept.Crafting);
            Assert.Equal(0f, kept.CraftingXp);
        }
}
}
