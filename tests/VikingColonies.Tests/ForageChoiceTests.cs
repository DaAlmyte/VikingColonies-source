using System.Collections.Generic;
using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    // The forager's "What to pick" list: every wild kind that grows back (food or not), one tick each, all ticked to start.
    public class ForageChoiceTests
    {
        private const int Meadows = 1, Swamp = 2, BlackForest = 8, Plains = 16, Mistlands = 512;

        // As the game has them (vc_ui pickables, 2026-10-15).
        private static readonly PickableInfo[] Game =
        {
            new PickableInfo("RaspberryBush", "Raspberry", "$item_raspberries", 300f, isFood: true, isMaterial: false, biomes: Meadows),
            new PickableInfo("Pickable_Dandelion", "Dandelion", "$item_dandelion", 240f, isFood: false, isMaterial: true, biomes: Meadows),
            new PickableInfo("Pickable_Thistle", "Thistle", "$item_thistle", 240f, isFood: false, isMaterial: true, biomes: Swamp | BlackForest),
            new PickableInfo("BlueberryBush", "Blueberries", "$item_blueberries", 300f, isFood: true, isMaterial: false, biomes: BlackForest),
            new PickableInfo("Pickable_Mushroom", "Mushroom", "$item_mushroomcommon", 240f, isFood: true, isMaterial: false, biomes: Meadows | Swamp | BlackForest),
            new PickableInfo("Pickable_Mushroom_yellow", "MushroomYellow", "$item_mushroomyellow", 240f, isFood: true, isMaterial: false, biomes: 0),
            new PickableInfo("Pickable_Flint", "Flint", "$item_flint", 240f, isFood: false, isMaterial: true, biomes: Meadows),
            new PickableInfo("Pickable_Branch", "Wood", "$item_wood", 240f, isFood: false, isMaterial: true, biomes: Meadows | Swamp | BlackForest),
            new PickableInfo("Pickable_Stone", "Stone", "$item_stone", 0f, isFood: false, isMaterial: true, biomes: Meadows),
            new PickableInfo("Pickable_Mushroom_Magecap", "MushroomMagecap", "$item_magecap", 0f, isFood: true, isMaterial: false, biomes: Mistlands),
            new PickableInfo("Pickable_SeedCarrot", "CarrotSeeds", "$item_carrotseeds", 0f, isFood: false, isMaterial: true, biomes: BlackForest),
            new PickableInfo("Pickable_Carrot", "Carrot", "$item_carrot", 0f, isFood: true, isMaterial: false, biomes: 0),
            new PickableInfo("Pickable_DragonEgg", "DragonEgg", "$item_dragonegg", 480f, isFood: false, isMaterial: false, biomes: 0),
            new PickableInfo("CloudberryBush", "Cloudberry", "$item_cloudberries", 300f, isFood: true, isMaterial: false, biomes: Plains),
            new PickableInfo("VineAsh", "Vineberry", "$item_vineberry", 200f, isFood: true, isMaterial: false, biomes: 0),
            new PickableInfo("VineGreen", "Vineberry", "$item_vineberry", 200f, isFood: true, isMaterial: false, biomes: 0),
        };

        private static List<string> Items(IEnumerable<ForageKind> kinds) => kinds.Select(k => k.Item).ToList();

        [Fact]
        public void Every_wild_kind_that_grows_back_is_on_the_list_food_or_not()
        {
            List<string> items = Items(ForageCatalog.Kinds(Game));
            Assert.Contains("Raspberry", items);
            Assert.Contains("Dandelion", items);
            Assert.Contains("Thistle", items);
            Assert.Contains("MushroomYellow", items);   // grows only at certain spots (caves, ruins): on the list all the same
        }

        [Fact]
        public void What_does_not_grow_back_is_never_on_the_list()
        {
            // Picking it would strip the land for good: stones, the Mistlands mushrooms, wild seeds, a player's carrots.
            List<string> items = Items(ForageCatalog.Kinds(Game));
            Assert.DoesNotContain("Stone", items);
            Assert.DoesNotContain("MushroomMagecap", items);
            Assert.DoesNotContain("CarrotSeeds", items);
            Assert.DoesNotContain("Carrot", items);
        }

        [Fact]
        public void A_dragon_egg_is_not_a_foragers()
        {
            // The egg is a boss's offering.
            Assert.DoesNotContain("DragonEgg", Items(ForageCatalog.Kinds(Game)));
        }

        [Fact]
        public void Flint_and_branches_are_on_the_list_unticked_to_start()
        {
            // The player's decision of 2026-10-15: on the list, but each hut ticks them only if the player wants piles of them.
            List<ForageKind> kinds = ForageCatalog.Kinds(Game).ToList();
            ForageKind flint = kinds.Single(k => k.Item == "Flint");
            Assert.True(flint.OffByDefault);
            Assert.False(kinds.Single(k => k.Item == "Raspberry").OffByDefault);
            List<ForageLine> lines = ForageChoice.Lines(kinds, new HashSet<string>());
            Assert.False(lines.Single(l => l.Kind.Item == "Flint").Ticked);
            Assert.True(lines.Single(l => l.Kind.Item == "Raspberry").Ticked);
        }

        [Fact]
        public void Ticking_flint_saves_it_with_a_plus()
        {
            // The hut keeps one list of what the player changed: an unticked kind by name, a ticked off-to-start kind as "+name".
            List<ForageKind> kinds = ForageCatalog.Kinds(Game).ToList();
            ForageKind flint = kinds.Single(k => k.Item == "Flint"), dandelion = kinds.Single(k => k.Item == "Dandelion");
            Assert.Equal("+Flint", ForageChoice.Entry(flint));
            Assert.True(ForageChoice.EntrySaved(flint, ticked: true));
            Assert.Equal("Dandelion", ForageChoice.Entry(dandelion));
            Assert.True(ForageChoice.EntrySaved(dandelion, ticked: false));
            HashSet<string> saved = TickList.Parse("+Flint,Dandelion");
            List<ForageLine> lines = ForageChoice.Lines(kinds, saved);
            Assert.True(lines.Single(l => l.Kind.Item == "Flint").Ticked);
            Assert.False(lines.Single(l => l.Kind.Item == "Dandelion").Ticked);
        }

        [Fact]
        public void Two_plants_giving_the_same_item_are_one_kind()
        {
            List<ForageKind> kinds = ForageCatalog.Kinds(Game).ToList();
            Assert.Single(kinds, k => k.Item == "Vineberry");
        }

        [Fact]
        public void Kinds_come_in_the_order_of_the_biomes_from_the_meadows_on()
        {
            List<string> items = Items(ForageCatalog.Kinds(Game));
            Assert.True(items.IndexOf("Raspberry") < items.IndexOf("Blueberries"));
            Assert.True(items.IndexOf("Thistle") < items.IndexOf("Cloudberry"));   // the Black Forest before the plains
            Assert.Equal("Vineberry", items[items.Count - 1]);                     // what grows only at certain spots last ("MushroomYellow" < "Vineberry")
            ForageKind thistle = ForageCatalog.Kinds(Game).Single(k => k.Item == "Thistle");
            Assert.Equal(Swamp | BlackForest, thistle.Biomes);
            Assert.False(thistle.IsFood);
        }

        [Fact]
        public void Everything_is_ticked_until_the_player_unticks_it()
        {
            List<ForageKind> kinds = ForageCatalog.Kinds(Game).ToList();
            Assert.All(ForageChoice.Lines(kinds, new HashSet<string>()).Where(l => !l.Kind.OffByDefault), l => Assert.True(l.Ticked));
            List<ForageLine> lines = ForageChoice.Lines(kinds, TickList.Parse("Dandelion"));
            Assert.False(lines.Single(l => l.Kind.Item == "Dandelion").Ticked);
            Assert.True(lines.Single(l => l.Kind.Item == "Raspberry").Ticked);
        }

        [Fact]
        public void He_wants_an_item_only_when_its_kind_is_on_the_list_and_ticked()
        {
            List<ForageKind> catalog = ForageCatalog.Kinds(Game).ToList();
            Assert.True(ForageChoice.Wants("Dandelion", catalog, new HashSet<string>()));
            Assert.False(ForageChoice.Wants("Dandelion", catalog, TickList.Parse("Dandelion")));
            Assert.False(ForageChoice.Wants("Flint", catalog, new HashSet<string>()));   // off to start
            Assert.True(ForageChoice.Wants("Flint", catalog, TickList.Parse("+Flint")));
            Assert.False(ForageChoice.Wants("Stone", catalog, TickList.Parse("+Stone")));   // never on the list
            Assert.False(ForageChoice.Wants("", catalog, new HashSet<string>()));
        }

        [Fact]
        public void A_saved_list_reads_back_the_same()
        {
            string saved = TickList.Save(new[] { "Thistle", "Dandelion", "Thistle", "" });
            Assert.Equal("Dandelion,Thistle", saved);
            Assert.Equal(new[] { "Dandelion", "Thistle" }, TickList.Parse(saved).OrderBy(s => s).ToArray());
            Assert.Empty(TickList.Parse(null));
        }
    }
}
