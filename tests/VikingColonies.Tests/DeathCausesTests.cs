using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class DeathCausesTests
    {
        [Fact]
        public void An_attacker_names_the_killer_whatever_the_kind_of_hit()
        {
            DeathCause cause = DeathCauses.From("EnemyHit", "$enemy_greydwarf", "Slash");
            Assert.Equal(DeathCauses.KilledBy, cause.Token);
            Assert.Equal("$enemy_greydwarf", cause.Arg);
            Assert.Equal(DeathCauses.KilledBy, DeathCauses.From("Tree", "Bob", "Blunt").Token);
        }

        [Fact]
        public void An_attack_whose_attacker_is_gone_is_attacked()
        {
            Assert.Equal(DeathCauses.Attacked, DeathCauses.From("EnemyHit", "", "Slash").Token);
            Assert.Equal(DeathCauses.Attacked, DeathCauses.From("PlayerHit", "", "Pierce").Token);
            Assert.Equal("", DeathCauses.From("EnemyHit", "", "Slash").Arg);
        }

        [Theory]
        [InlineData("Tree", DeathCauses.Tree)]
        [InlineData("Impact", DeathCauses.Crushed)]
        [InlineData("Structural", DeathCauses.Crushed)]
        [InlineData("Cart", DeathCauses.Crushed)]
        [InlineData("Boat", DeathCauses.Crushed)]
        [InlineData("Stalagtite", DeathCauses.Crushed)]
        [InlineData("DrawBridge", DeathCauses.Crushed)]
        [InlineData("Fall", DeathCauses.Fall)]
        [InlineData("Drowning", DeathCauses.Drowned)]
        [InlineData("Burning", DeathCauses.Burned)]
        [InlineData("CinderFire", DeathCauses.Burned)]
        [InlineData("AshlandsLava", DeathCauses.Burned)]
        [InlineData("Incinerator", DeathCauses.Burned)]
        [InlineData("Freezing", DeathCauses.Froze)]
        [InlineData("Poisoned", DeathCauses.Poisoned)]
        [InlineData("Smoke", DeathCauses.Smoke)]
        [InlineData("Turret", DeathCauses.Shot)]
        [InlineData("Catapult", DeathCauses.Shot)]
        [InlineData("EdgeOfWorld", DeathCauses.Edge)]
        public void Each_kind_of_hit_has_its_cause(string hitType, string token)
        {
            DeathCause cause = DeathCauses.From(hitType, "", "Blunt");
            Assert.Equal(token, cause.Token);
            Assert.Equal("", cause.Arg);
        }

        [Theory]
        [InlineData("Fire", DeathCauses.Burned)]
        [InlineData("Frost", DeathCauses.Froze)]
        [InlineData("Poison", DeathCauses.Poisoned)]
        [InlineData("Lightning", DeathCauses.Lightning)]
        [InlineData("Blunt", DeathCauses.Unknown)]
        [InlineData("Spirit", DeathCauses.Unknown)]
        public void Other_hits_without_an_attacker_go_by_their_main_damage(string damageType, string token)
        {
            Assert.Equal(token, DeathCauses.From("Undefined", "", damageType).Token);
            Assert.Equal(token, DeathCauses.From("Self", "", damageType).Token);
        }

        [Fact]
        public void No_last_hit_is_an_unknown_cause()
        {
            Assert.Equal(DeathCauses.Unknown, DeathCauses.From("", "", "").Token);
            Assert.Equal(DeathCauses.Unknown, DeathCauses.From(null, null, null).Token);
        }

        [Fact]
        public void Every_token_is_listed_once()
        {
            Assert.Equal(15, DeathCauses.All.Length);
            Assert.Equal(DeathCauses.All.Length, DeathCauses.All.Distinct().Count());
            Assert.All(DeathCauses.All, t => Assert.StartsWith("$vc_death_", t));
        }
    }
}
