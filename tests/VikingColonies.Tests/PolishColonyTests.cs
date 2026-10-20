using System.Collections.Generic;
using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>Polish sweep part 2 (0.19): the colony's leftovers.</summary>
    public class PolishColonyTests
    {
        [Fact]
        public void A_settler_out_of_sight_says_when_he_was_last_seen()
        {
            // The roadmap's v2 note: one only unloaded showed nothing about when he was last seen.
            Assert.Equal("$vc_far_away", CitizenLine.SeenToken(lastSeenDay: 12, today: 12));
            Assert.Equal("$vc_seen_day", CitizenLine.SeenToken(lastSeenDay: 10, today: 12));
            Assert.Equal("$vc_far_away", CitizenLine.SeenToken(lastSeenDay: 0, today: 12));    // never written: not known
            Assert.Equal("$vc_far_away", CitizenLine.SeenToken(lastSeenDay: 14, today: 12));   // the clock put back
        }

        private static ArrivalCandidate Spot(bool reachable, bool seen, float nearestPlayer) =>
            new ArrivalCandidate(new GroundPoint(0, 0), dry: true, reachable: reachable, nearestPlayer: nearestPlayer, seen: seen);

        [Fact]
        public void A_newcomer_comes_from_a_spot_no_player_is_looking_at()
        {
            // 0.16 review M4: the farthest spot was often right in front of the player who had just placed the hall.
            var spots = new[] { Spot(true, seen: true, 90f), Spot(true, seen: false, 40f), Spot(true, seen: false, 60f) };
            Assert.Equal(2, ArrivalSpot.Choose(spots));
            Assert.Equal(0, ArrivalSpot.Choose(new[] { Spot(true, true, 90f), Spot(true, true, 40f) }));   // all in view: the farthest
        }

        [Fact]
        public void Out_of_view_counts_before_the_far_ring()
        {
            var far = new[] { Spot(true, seen: true, 90f) };
            var near = new[] { Spot(true, seen: false, 30f) };
            Assert.Equal(1, ArrivalSpot.Choose(new[] { far, near }, founding: false));
            Assert.Equal(0, ArrivalSpot.Choose(new[] { far, new[] { Spot(false, false, 30f) } }, founding: false));
        }

        private const string Roster013 = "v=1\nid=abc|name=Ivar|job=farmer|work=farm1|status=$vc_status_planting|seen=12|pos=1,2,3|missing=-1|home=long1#2";

        [Fact]
        public void The_roster_keeps_each_members_mood_so_every_computer_agrees()
        {
            // 0.17 review M10: the colony's mood averaged the settlers loaded on each computer.
            ColonyRoster old = ColonyRoster.Decode(Roster013);
            Assert.Equal(Mood.Unknown, old.Get("abc").Mood);
            Assert.Equal(Roster013, old.Encode());   // a roster from before 0.19 reads and writes back the same
            old.Seen("abc", "Ivar", "farmer", "farm1", "$vc_status_planting", new[] { 1f, 2f, 3f }, 12, "long1#2", mood: 62);
            string written = old.Encode();
            Assert.Equal(Roster013 + "|mood=62", written);
            Assert.Equal(62, ColonyRoster.Decode(written).Get("abc").Mood);
            old.Seen("abc", "Ivar", "farmer", "farm1", "$vc_status_planting", new[] { 1f, 2f, 3f }, 13, "long1#2");   // not worked out here: kept
            Assert.Equal(62, old.Get("abc").Mood);
            Assert.NotEqual(ColonyRoster.Decode(Roster013).Signature(), old.Signature());   // a change of mood is saved
        }

        [Fact]
        public void A_cosy_bed_he_had_stays_while_its_area_is_not_loaded()
        {
            // 0.17 review M2: a working-out with the bed's area not loaded dropped the cosy reason for a minute.
            Assert.Equal(7, MoodLevels.LastComfort("$vc_mood_home:5,$vc_mood_cosy:10,$vc_mood_ate:5"));
            Assert.Equal(4, MoodLevels.LastComfort("$vc_mood_home:5,$vc_mood_cosy:5"));
            Assert.Equal(MoodInputs.Unknown, MoodLevels.LastComfort("$vc_mood_home:5"));
            Assert.Equal(MoodInputs.Unknown, MoodLevels.LastComfort(""));
        }

        [Fact]
        public void A_bush_a_planting_mod_makes_plantable_is_a_crop_that_costs_its_price()
        {
            // PlantEverything places a raspberry bush itself (no sapling), for 5 raspberries; it regrows after picking.
            var bush = new PlantInfo("RaspberryBush", "Raspberry", "$item_raspberries", "RaspberryBush", "Raspberry", isPickable: true, biomes: 0,
                growRadius: 0.5f, isFood: true, seedCount: 5);
            var carrot = new PlantInfo("sapling_carrot", "CarrotSeeds", "$item_carrot", "Pickable_Carrot", "Carrot", true, 1, 0.5f, isFood: true);
            List<CropInfo> crops = CropCatalog.Crops(new[] { carrot, bush }).ToList();
            Assert.Equal(2, crops.Count);
            CropInfo raspberries = crops.Single(c => c.Piece == "RaspberryBush");
            Assert.Equal(5, raspberries.SeedCount);
            Assert.Equal("RaspberryBush", raspberries.Grown);
            Assert.Equal(1, crops.Single(c => c.Piece == "sapling_carrot").SeedCount);
            Assert.True(CropChoice.WorthPlanting(raspberries, crops, item => 0));   // food: always worth it
        }

        [Fact]
        public void A_bush_starts_unticked_on_the_Farms_list_and_ticks_like_flint()
        {
            // Review of 0.19: ticked from the start, the Farm turned the colony's raspberries into bushes that repay them
            // only after weeks. Now the player opts in, as for the forager's flint.
            var bush = new CropInfo("RaspberryBush", "Raspberry", "RaspberryBush", "Raspberry", "$item_raspberries", 0, 0.5f, isFood: true, seedCount: 5);
            var carrot = new CropInfo("sapling_carrot", "CarrotSeeds", "Pickable_Carrot", "Carrot", "$item_carrot", 0, 0.5f, isFood: true);
            Assert.True(bush.IsBush);
            Assert.False(carrot.IsBush);
            var crops = new[] { carrot, bush };
            List<CropLine> fresh = CropChoice.Lines(crops, 0, new HashSet<string>());
            Assert.True(fresh.Single(l => l.Crop == carrot).Ticked);
            Assert.False(fresh.Single(l => l.Crop == bush).Ticked);
            Assert.Equal("+RaspberryBush", CropChoice.Entry(bush));
            Assert.True(CropChoice.EntrySaved(bush, ticked: true));
            Assert.False(CropChoice.EntrySaved(carrot, ticked: true));
            List<CropLine> opted = CropChoice.Lines(crops, 0, new HashSet<string> { "+RaspberryBush", "sapling_carrot" });
            Assert.True(opted.Single(l => l.Crop == bush).Ticked);
            Assert.False(opted.Single(l => l.Crop == carrot).Ticked);
        }
    }
}
