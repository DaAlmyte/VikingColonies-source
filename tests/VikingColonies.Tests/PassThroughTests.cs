using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>
    /// The player, watching the release build (2026-10-19): "villagers keep pushing each other and me when they run into
    /// each other and me". Settlers pass through each other and through players, as followers do in other games' mods;
    /// monsters, animals and buildings still meet them.
    /// </summary>
    public class PassThroughTests
    {
        [Fact]
        public void Settlers_pass_through_settlers_and_players()
        {
            Assert.True(PassThrough.Between(aIsSettler: true, bIsSettler: true, bIsPlayer: false));
            Assert.True(PassThrough.Between(aIsSettler: true, bIsSettler: false, bIsPlayer: true));
            Assert.True(PassThrough.Between(aIsSettler: false, bIsSettler: true, bIsPlayer: false, aIsPlayer: true));
        }

        [Fact]
        public void Everything_else_still_meets()
        {
            Assert.False(PassThrough.Between(aIsSettler: true, bIsSettler: false, bIsPlayer: false));                  // a monster, a wolf
            Assert.False(PassThrough.Between(aIsSettler: false, bIsSettler: false, bIsPlayer: true, aIsPlayer: true)); // two players
            Assert.False(PassThrough.Between(aIsSettler: false, bIsSettler: false, bIsPlayer: false));                 // monsters
        }
    }
}
