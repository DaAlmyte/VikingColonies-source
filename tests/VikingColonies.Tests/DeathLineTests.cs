using System.Globalization;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class DeathLineTests
    {
        private static DeathFacts Ingrid() => new DeathFacts
        {
            Name = "Ingrid",
            Job = "lumberjack",
            Hut = "lumberjack_hut b1e10ec6 (level 1)",
            Colony = "2673f07e",
            Position = new Vec3(317f, 28.3f, 57.6f),
            Cause = "hit by a falling tree",
            HitType = "Tree",
            Damage = 87f,
            DamageType = "Blunt",
            SecondsSinceHit = 0.1,
        };

        [Fact]
        public void A_death_line_names_the_settler_job_hut_colony_place_and_cause()
        {
            Assert.Equal("Settler died: Ingrid, lumberjack at lumberjack_hut b1e10ec6 (level 1), colony 2673f07e, " +
                         "at (317.0, 28.3, 57.6): hit by a falling tree [last hit 0.1 s before: Tree, no attacker, 87 damage, mostly blunt]",
                DeathLine.Format(Ingrid()));
        }

        [Fact]
        public void A_jobless_settler_without_a_colony_or_a_last_hit()
        {
            var facts = new DeathFacts { Name = "Sigrid", Position = new Vec3(1f, 2f, 3f), Cause = "unknown cause" };
            Assert.Equal("Settler died: Sigrid, jobless, no colony, at (1.0, 2.0, 3.0): unknown cause [no last hit]", DeathLine.Format(facts));
        }

        [Fact]
        public void An_attacker_and_an_unknown_time()
        {
            DeathFacts facts = Ingrid();
            facts.Cause = "killed by Greydwarf";
            facts.HitType = "EnemyHit";
            facts.Attacker = "Greydwarf";
            facts.DamageType = "Slash";
            facts.SecondsSinceHit = -1;
            Assert.EndsWith("killed by Greydwarf [last hit: EnemyHit, by Greydwarf, 87 damage, mostly slash]", DeathLine.Format(facts));
        }

        [Fact]
        public void The_lines_use_dots_whatever_the_computers_language()
        {
            CultureInfo before = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
                Assert.Contains("(317.0, 28.3, 57.6)", DeathLine.Format(Ingrid()));
                Assert.Contains("0.1 s before", DeathLine.Format(Ingrid()));
            }
            finally
            {
                CultureInfo.CurrentCulture = before;
            }
        }

        [Fact]
        public void A_close_call_says_whether_the_setting_blocked_it()
        {
            Assert.Equal("Ingrid was hit by a falling tree or log: 31 damage (blocked: SettlersSafeFromTrees)",
                DeathLine.CloseCall("Ingrid", 31.2f, blocked: true, healthLeft: 100f));
            Assert.Equal("Ingrid was hit by a falling tree or log: 31 damage, 69 health left",
                DeathLine.CloseCall("Ingrid", 31.2f, blocked: false, healthLeft: 68.8f));
        }
    }
}
