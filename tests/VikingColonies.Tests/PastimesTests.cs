using System;
using System.Collections.Generic;
using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class PastimesTests
    {
        private static PastimeChoices At(bool target = false, bool dummy = false, bool table = false, bool pole = false, bool fire = false,
            bool trophy = false, bool mapTable = false, bool tame = false, bool partner = false, bool wet = false, bool roofedProps = false,
            bool evening = false) =>
            new PastimeChoices(target, dummy, table, pole, fire, trophy, mapTable, tame, partner, wet, roofedProps, evening);

        private static readonly PastimeChoices Everything = At(true, true, true, true, true, true, true, true, partner: true);

        [Fact]
        public void Each_pastime_needs_its_piece()
        {
            var c = new PastimeChoices(target: false, dummy: true, table: false, pole: false, fire: false, trophy: false, mapTable: false,
                tame: false, partner: false, wet: false, roofedProps: false, evening: false);
            Assert.Equal(0, PastimeRules.Weight(Pastime.Archery, c));
            Assert.True(PastimeRules.Weight(Pastime.WeaponPractice, c) > 0);
            Assert.Equal(0, PastimeRules.Weight(Pastime.Map, c));
            Assert.Equal(0, PastimeRules.Weight(Pastime.Admire, c));
            Assert.Equal(0, PastimeRules.Weight(Pastime.PoleDance, c));
            Assert.Equal(0, PastimeRules.Weight(Pastime.PetTame, c));
            Assert.True(PastimeRules.Weight(Pastime.Map, At(mapTable: true)) > 0);
            Assert.True(PastimeRules.Weight(Pastime.Admire, At(trophy: true)) > 0);
            Assert.True(PastimeRules.Weight(Pastime.PoleDance, At(pole: true)) > 0);
            Assert.True(PastimeRules.Weight(Pastime.PetTame, At(tame: true)) > 0);
        }

        [Fact]
        public void A_board_game_needs_a_table_and_a_partner()
        {
            Assert.Equal(0, PastimeRules.Weight(Pastime.BoardGame, At(table: true)));
            Assert.Equal(0, PastimeRules.Weight(Pastime.BoardGame, At(partner: true)));
            Assert.True(PastimeRules.Weight(Pastime.BoardGame, At(table: true, partner: true)) > 0);
            Assert.Equal(0, PastimeRules.Weight(Pastime.Toast, At(table: true)));
            Assert.True(PastimeRules.Weight(Pastime.Toast, At(table: true, partner: true)) > 0);
            Assert.Equal(0, PastimeRules.Weight(Pastime.Spar, At()));
            Assert.True(PastimeRules.Weight(Pastime.Spar, At(partner: true)) > 0);
        }

        [Fact]
        public void A_story_needs_a_fire_and_someone_to_listen()
        {
            Assert.Equal(0, PastimeRules.Weight(Pastime.Story, At(fire: true)));
            Assert.Equal(0, PastimeRules.Weight(Pastime.Story, At(partner: true)));
            Assert.True(PastimeRules.Weight(Pastime.Story, At(fire: true, partner: true)) > 0);
        }

        [Fact]
        public void Dancing_at_the_fire_is_for_the_evening()
        {
            Assert.Equal(0, PastimeRules.Weight(Pastime.FireDance, At(fire: true)));
            Assert.Equal(0, PastimeRules.Weight(Pastime.FireDance, At(evening: true)));
            Assert.True(PastimeRules.Weight(Pastime.FireDance, At(fire: true, evening: true)) > 0);
        }

        [Fact]
        public void Stretching_needs_nothing_and_is_possible_in_rain()
        {
            Assert.True(PastimeRules.Weight(Pastime.Stretch, At()) > 0);
            Assert.True(PastimeRules.Weight(Pastime.Stretch, At(wet: true)) > 0);
        }

        [Fact]
        public void In_rain_only_roofed_pieces_count_and_nobody_naps()
        {
            PastimeChoices open = At(target: true, table: true, partner: true, tame: true, wet: true, roofedProps: false);
            Assert.Equal(0, PastimeRules.Weight(Pastime.Archery, open));
            Assert.Equal(0, PastimeRules.Weight(Pastime.BoardGame, open));
            Assert.Equal(0, PastimeRules.Weight(Pastime.Spar, open));
            Assert.Equal(0, PastimeRules.Weight(Pastime.PetTame, open));
            Assert.Equal(0, PastimeRules.Weight(Pastime.Nap, open));
            PastimeChoices roofed = At(table: true, partner: true, wet: true, roofedProps: true);
            Assert.True(PastimeRules.Weight(Pastime.BoardGame, roofed) > 0);
            Assert.Equal(0, PastimeRules.Weight(Pastime.Nap, roofed));
            Assert.True(PastimeRules.Weight(Pastime.Nap, At()) > 0);
        }

        /// <summary>
        /// The player's look at 1.6's game check: sparring settlers lunged into each other, a napper slid, a dancer drifted,
        /// one slid while cheering, and one jumped as they stood up from the ground by the fire.
        /// </summary>
        [Fact]
        public void Only_a_jobs_swing_slides_a_settler()
        {
            Assert.True(PastimeRules.StaysPut(isSettler: true, inAttack: true, Pastime.Spar, posed: false));   // practice blows
            Assert.True(PastimeRules.StaysPut(isSettler: true, inAttack: true, Pastime.WeaponPractice, posed: false));
            Assert.True(PastimeRules.StaysPut(isSettler: true, inAttack: false, Pastime.None, posed: true));   // a nap, a dance, sitting
            Assert.True(PastimeRules.StaysPut(isSettler: true, inAttack: false, Pastime.None, posed: false));   // a cheer, standing up
            Assert.False(PastimeRules.StaysPut(isSettler: true, inAttack: true, Pastime.None, posed: false));   // a lumberjack's swing is the game's own
            Assert.False(PastimeRules.StaysPut(isSettler: false, inAttack: false, Pastime.Spar, posed: true));   // players are unchanged
        }

        [Fact]
        public void Pieces_weigh_more_than_idling()
        {
            Assert.Equal(3, PastimeRules.Weight(Pastime.Archery, Everything));
            Assert.Equal(2, PastimeRules.Weight(Pastime.Stretch, Everything));
            Assert.Equal(2, PastimeRules.Weight(Pastime.Spar, Everything));
            Assert.Equal(1, PastimeRules.Weight(Pastime.Nap, Everything));
            Assert.Equal(1, PastimeRules.Weight(Pastime.PetTame, Everything));
        }

        [Theory]
        [InlineData(Trait.Lazy, Pastime.Nap)]
        [InlineData(Trait.Chatty, Pastime.BoardGame)]
        [InlineData(Trait.Brave, Pastime.Spar)]
        [InlineData(Trait.Timid, Pastime.Map)]
        [InlineData(Trait.Hardworking, Pastime.Archery)]
        [InlineData(Trait.Cheerful, Pastime.PoleDance)]
        [InlineData(Trait.Grumpy, Pastime.WeaponPractice)]
        [InlineData(Trait.Quiet, Pastime.Admire)]
        public void Each_trait_has_its_favourite(Trait trait, Pastime favourite)
        {
            Assert.Equal(favourite, PastimeRules.Favourite(trait));
        }

        [Fact]
        public void The_favourite_is_chosen_twice_as_often()
        {
            Assert.Equal(2 * PastimeRules.Weight(Pastime.Archery, Everything), PastimeRules.WeightFor(Pastime.Archery, Everything, Trait.Hardworking));
            Assert.Equal(PastimeRules.Weight(Pastime.Archery, Everything), PastimeRules.WeightFor(Pastime.Archery, Everything, Trait.Lazy));
            Assert.Equal(0, PastimeRules.WeightFor(Pastime.Archery, At(), Trait.Hardworking));
        }

        [Fact]
        public void A_cheerful_settler_loves_any_dance()
        {
            PastimeChoices evening = At(fire: true, evening: true);
            Assert.True(PastimeRules.IsDance(Pastime.FireDance));
            Assert.True(PastimeRules.IsDance(Pastime.PoleDance));
            Assert.False(PastimeRules.IsDance(Pastime.Stretch));
            Assert.Equal(2 * PastimeRules.Weight(Pastime.FireDance, evening), PastimeRules.WeightFor(Pastime.FireDance, evening, Trait.Cheerful));
        }

        [Fact]
        public void Nothing_at_hand_picks_stretching_or_a_nap_not_none()
        {
            for (double roll = 0; roll < 1; roll += 0.05)
                Assert.Contains(PastimeRules.Pick(At(), roll, Trait.Grumpy), new[] { Pastime.Stretch, Pastime.Nap });
            Assert.Equal(Pastime.Stretch, PastimeRules.Pick(At(wet: true), 0.99, Trait.Lazy));
        }

        [Fact]
        public void Each_possible_pastime_gets_picked_by_some_roll()
        {
            PastimeChoices all = At(true, true, true, true, true, true, true, true, partner: true, evening: true);
            var seen = new HashSet<Pastime>();
            for (int i = 0; i < 1000; i++) seen.Add(PastimeRules.Pick(all, i / 1000.0, Trait.Quiet));
            foreach (Pastime p in Enum.GetValues(typeof(Pastime)))
                if (p != Pastime.None) Assert.Contains(p, seen);
        }

        [Fact]
        public void No_pastime_is_a_job()
        {
            string[] names = Enum.GetNames(typeof(Pastime));
            foreach (string job in new[] { "Fish", "Hunt", "Farm", "Gather", "Spin", "Bee", "Repair" })
                Assert.DoesNotContain(names, n => n.IndexOf(job, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        [Fact]
        public void A_piece_holds_so_many()
        {
            Assert.Equal(2, PastimeRules.Capacity(Prop.Target));
            Assert.Equal(2, PastimeRules.Capacity(Prop.Dummy));
            Assert.Equal(6, PastimeRules.Capacity(Prop.Pole));
            Assert.Equal(1, PastimeRules.Capacity(Prop.MapTable));
            Assert.Equal(2, PastimeRules.Capacity(Prop.Trophy));
            Assert.Equal(1, PastimeRules.Capacity(Prop.Tame));
        }

        [Fact]
        public void Each_pastime_has_its_piece_and_players()
        {
            Assert.Equal(Prop.Target, PastimeRules.PropOf(Pastime.Archery));
            Assert.Equal(Prop.Dummy, PastimeRules.PropOf(Pastime.WeaponPractice));
            Assert.Equal(Prop.Table, PastimeRules.PropOf(Pastime.BoardGame));
            Assert.Equal(Prop.Table, PastimeRules.PropOf(Pastime.Toast));
            Assert.Equal(Prop.Pole, PastimeRules.PropOf(Pastime.PoleDance));
            Assert.Equal(Prop.Fire, PastimeRules.PropOf(Pastime.Story));
            Assert.Equal(Prop.Fire, PastimeRules.PropOf(Pastime.FireDance));
            Assert.Equal(Prop.None, PastimeRules.PropOf(Pastime.Stretch));
            Assert.Equal(2, PastimeRules.MinPlayers(Pastime.BoardGame));
            Assert.Equal(2, PastimeRules.MaxPlayers(Pastime.BoardGame));
            Assert.Equal(2, PastimeRules.MaxPlayers(Pastime.Spar));
            Assert.Equal(2, PastimeRules.MinPlayers(Pastime.Toast));
            Assert.Equal(4, PastimeRules.MaxPlayers(Pastime.Toast));
            Assert.Equal(2, PastimeRules.MinPlayers(Pastime.Story));
            Assert.Equal(1, PastimeRules.MinPlayers(Pastime.Archery));
        }

        [Fact]
        public void A_pastime_lasts_a_minute_or_two()
        {
            Assert.Equal(60f, PastimeRules.DoSeconds(Pastime.Archery, 0));
            Assert.Equal(120f, PastimeRules.DoSeconds(Pastime.Archery, 1));
        }

        [Fact]
        public void Each_pastime_has_its_hover_word()
        {
            Assert.Equal("$vc_doing_archery", ActivityWords.Token(Pastime.Archery));
            var tokens = Enum.GetValues(typeof(Pastime)).Cast<Pastime>().Where(p => p != Pastime.None).Select(ActivityWords.Token).ToList();
            Assert.Equal(tokens.Count, tokens.Distinct().Count());
            Assert.All(tokens, t => Assert.StartsWith("$vc_doing_", t));
            Assert.Equal("", ActivityWords.Token(Pastime.None));
        }
            // ---- Their last pastimes, for the mood (Task 2) ----

        [Fact]
        public void Their_last_five_pastimes_are_remembered_newest_last()
        {
            List<string> last = new List<string>();
            foreach (Pastime p in new[] { Pastime.Archery, Pastime.Nap, Pastime.Map, Pastime.Spar, Pastime.Toast, Pastime.Stretch })
                last = PastimeMemory.Add(last, p, 3);
            Assert.Equal(PastimeMemory.Remembered, last.Count);
            Assert.Equal("Nap@3", last[0]);
            Assert.Equal("Stretch@3", last[4]);
        }

        [Fact]
        public void Three_kinds_in_the_last_five_lift_the_mood_by_five()
        {
            List<string> last = new List<string>();
            foreach (Pastime p in new[] { Pastime.Archery, Pastime.Archery, Pastime.Nap, Pastime.Map, Pastime.Nap })
                last = PastimeMemory.Add(last, p, 5);
            Assert.Equal(3, PastimeMemory.Distinct(last, 5));
            var i = new MoodInputs { BedComfort = MoodInputs.Unknown, DistinctPastimes = PastimeMemory.Distinct(last, 5) };
            Assert.Contains(Mood.Compute(i).Reasons, r => r.Token == "$vc_mood_varied_free" && r.Change == 5);
            Assert.Equal(55, Mood.Compute(i).Value);
        }

        [Fact]
        public void Four_kinds_lift_it_by_ten()
        {
            var i = new MoodInputs { BedComfort = MoodInputs.Unknown, DistinctPastimes = 4 };
            Assert.Contains(Mood.Compute(i).Reasons, r => r.Token == "$vc_mood_varied_free" && r.Change == 10);
            var two = new MoodInputs { BedComfort = MoodInputs.Unknown, DistinctPastimes = 2 };
            Assert.DoesNotContain(Mood.Compute(two).Reasons, r => r.Token == "$vc_mood_varied_free");
            Assert.Contains("$vc_mood_varied_free", Mood.ReasonTokens);
        }

        [Fact]
        public void Only_the_last_two_days_count()
        {
            List<string> last = new List<string>();
            last = PastimeMemory.Add(last, Pastime.Archery, 1);
            last = PastimeMemory.Add(last, Pastime.Nap, 1);
            last = PastimeMemory.Add(last, Pastime.Map, 3);
            last = PastimeMemory.Add(last, Pastime.Spar, 4);
            Assert.Equal(2, PastimeMemory.Distinct(last, 4));   // day 3 and 4 only
            Assert.Equal(4, PastimeMemory.Distinct(last, 1) + PastimeMemory.Distinct(last, 4));
            Assert.Equal(0, PastimeMemory.Distinct(last, 9));
        }

        [Fact]
        public void Sitting_and_strolling_are_not_pastimes()
        {
            // What isn't a pastime's name doesn't count, so an old or odd entry never lifts the mood.
            var last = new List<string> { "Seat@4", "Stroll@4", "Chat@4", "Archery@4", "junk", "" };
            Assert.Equal(1, PastimeMemory.Distinct(last, 4));
            Assert.Equal(PastimeMemory.Add(new List<string>(), Pastime.None, 4), new List<string>());
        }

        [Fact]
        public void Their_favourite_today_lifts_the_mood_by_three()
        {
            var last = PastimeMemory.Add(new List<string>(), Pastime.Nap, 6);
            Assert.True(PastimeMemory.Did(last, Pastime.Nap, 6));
            Assert.False(PastimeMemory.Did(last, Pastime.Nap, 7));
            Assert.False(PastimeMemory.Did(last, Pastime.Map, 6));
            var i = new MoodInputs { BedComfort = MoodInputs.Unknown, FavouriteToday = true };
            Assert.Contains(Mood.Compute(i).Reasons, r => r.Token == "$vc_mood_favourite" && r.Change == 3);
            Assert.Contains("$vc_mood_favourite", Mood.ReasonTokens);
        }

        [Fact]
        public void A_cheerful_settler_counts_any_dance_as_their_favourite_today()
        {
            var last = PastimeMemory.Add(new List<string>(), Pastime.FireDance, 6);
            Assert.True(PastimeMemory.DidFavourite(last, Trait.Cheerful, 6));
            Assert.False(PastimeMemory.DidFavourite(last, Trait.Lazy, 6));
        }

        [Fact]
        public void The_record_keeps_their_last_pastimes()
        {
            var r = new CitizenRecord { Id = "a1", Name = "Sigrid" };
            Assert.DoesNotContain("pastimes=", r.Encode());
            r.LastPastimes = PastimeMemory.Add(PastimeMemory.Add(new List<string>(), Pastime.Archery, 3), Pastime.Nap, 4);
            CitizenRecord back = CitizenRecord.Decode(r.Encode());
            Assert.Equal(new[] { "Archery@3", "Nap@4" }, back.LastPastimes);
            Assert.Contains("|pastimes=Archery@3,Nap@4", r.Encode());
        }

        [Fact]
        public void A_keepsake_forgets_them()
        {
            var calm = new CitizenRecord { Id = "a1", Name = "Sigrid", Job = "forager" };
            var played = CitizenRecord.Decode(calm.Encode());
            played.LastPastimes = new List<string> { "Archery@3" };
            Assert.Equal(Keepsake.Of(calm), Keepsake.Of(played));
        }

        // ---- Task 4: free time weighs the pastimes with the rest ----

        private static readonly LeisureChoices StrollOnly = new LeisureChoices(seat: false, fire: false, partner: false, wet: true);

        [Fact]
        public void Free_time_weighs_pastimes_with_the_rest()
        {
            // In rain with nothing at hand: a stroll (1) or stretching (2); the leisure first, then the pastimes.
            var pc = new PastimeChoices(false, false, false, false, false, false, false, false, partner: false, wet: true, roofedProps: false, evening: false);
            Assert.Equal(Leisure.Stroll, PastimeRules.PickFreeTime(StrollOnly, pc, true, 0.0, Trait.Hardworking, out Pastime first));
            Assert.Equal(Pastime.None, first);
            Assert.Equal(Leisure.Pastime, PastimeRules.PickFreeTime(StrollOnly, pc, true, 0.99, Trait.Hardworking, out Pastime last));
            Assert.Equal(Pastime.Stretch, last);
        }

        [Fact]
        public void The_favourite_weighs_double_against_the_rest_too()
        {
            // Dry, a target only: stroll 1, sit down 1, archery 3 (6 for the hard-working), stretch 2, nap 1.
            var lc = new LeisureChoices(seat: false, fire: false, partner: false, wet: false);
            var pc = new PastimeChoices(true, false, false, false, false, false, false, false, partner: false, wet: false, roofedProps: false, evening: false);
            Assert.Equal(11, PastimeRules.FreeTimeTotal(lc, pc, true, Trait.Hardworking));
            Assert.Equal(8, PastimeRules.FreeTimeTotal(lc, pc, true, Trait.Grumpy));
        }

        [Fact]
        public void With_pastimes_off_free_time_picks_as_before()
        {
            var lc = new LeisureChoices(seat: true, fire: false, partner: true, wet: false);
            var pc = new PastimeChoices(true, true, true, true, true, true, true, true, partner: true, wet: false, roofedProps: false, evening: false);
            foreach (double roll in new[] { 0.0, 0.3, 0.6, 0.99 })
            {
                Assert.Equal(FreeTimeRules.Pick(lc, roll, Trait.Cheerful), PastimeRules.PickFreeTime(lc, pc, false, roll, Trait.Cheerful, out Pastime p));
                Assert.Equal(Pastime.None, p);
            }
        }

        [Fact]
        public void Pastimes_are_never_picked_as_plain_leisure()
        {
            var lc = new LeisureChoices(seat: true, fire: true, partner: true, wet: false, tend: true, carry: true);
            Assert.Equal(0, FreeTimeRules.Weight(Leisure.Pastime, lc));
        }

        [Fact]
        public void A_worker_uses_pieces_near_their_hut_and_the_camps_when_it_is_near()
        {
            var hut = new GroundPoint(0, 0);
            Assert.True(PastimeRules.InReach(new GroundPoint(29, 0), hut, null));
            Assert.False(PastimeRules.InReach(new GroundPoint(31, 0), hut, null));
            // The camp 50 m off: its pieces count (within 30 m of it); 70 m off: they don't.
            Assert.True(PastimeRules.InReach(new GroundPoint(75, 0), hut, new GroundPoint(50, 0)));
            Assert.False(PastimeRules.InReach(new GroundPoint(95, 0), hut, new GroundPoint(70, 0)));
        }

        [Fact]
        public void Each_pastime_has_its_hover_words_in_english()
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "VikingColonies.slnx"))) dir = dir.Parent;
            Assert.NotNull(dir);
            string source = File.ReadAllText(Path.Combine(dir!.FullName, "src", "VikingColonies", "Config", "Translations.cs"));
            var tokens = PastimeRules.All.Select(ActivityWords.Token).Concat(new[] { ActivityWords.Listening }).ToList();
            Assert.Equal(tokens.Count, tokens.Distinct().Count());
            foreach (string token in tokens) Assert.Contains("{ \"" + token.Substring(1) + "\", \"", source);
        }

        [Fact]
        public void A_piece_by_a_seat_the_seat_test_holds_is_not_at_hand()
        {
            // v16's meadhall.txt, 27 Oct: a board game sat on the seats the test held, a toast stood by them, and the seat
            // under test counted as taken for 40 s. Its places (a table's seats and the spots round it) keep the chats' 3 m.
            var places = new[] { new GroundPoint(0f, 0f), new GroundPoint(1.9f, 0f) };
            Assert.True(PastimeRules.ClearOfHeldSeats(places, null));   // no test running
            Assert.True(PastimeRules.ClearOfHeldSeats(places, new GroundPoint[0]));
            Assert.False(PastimeRules.ClearOfHeldSeats(places, new[] { new GroundPoint(0f, 0f) }));   // one of its own seats
            Assert.False(PastimeRules.ClearOfHeldSeats(places, new[] { new GroundPoint(4.8f, 0f) }));   // 2.9 m from a spot round it
            Assert.True(PastimeRules.ClearOfHeldSeats(places, new[] { new GroundPoint(5.0f, 0f), new GroundPoint(-3.1f, 0f) }));
            Assert.True(PastimeRules.ClearOfHeldSeats(null, new[] { new GroundPoint(0f, 0f) }));   // a piece with no places
            Assert.Equal(ChatPairing.HeldSeatClearance, PastimeRules.HeldSeatClearance, 3);
        }

        [Fact]
        public void A_toast_stands_on_the_floor_clear_of_the_seats()
        {
            // v16's meadhall.txt, 27 Oct: toasting settlers stood 0.8 m up on the next table and 0.5 m up on a bench, the
            // spots round one table landing on its neighbour's top and a bench's end.
            var seats = new[] { new GroundPoint(0f, 0f), new GroundPoint(0f, 0.8f) };
            Assert.True(PastimeRules.ToastSpotClear(new GroundPoint(2f, 0f), 10.05f, 10f, seats));   // the floor, a step's height
            Assert.False(PastimeRules.ToastSpotClear(new GroundPoint(2f, 0f), 10.5f, 10f, seats));   // on a bench
            Assert.False(PastimeRules.ToastSpotClear(new GroundPoint(2f, 0f), 10.8f, 10f, seats));   // on a table
            Assert.False(PastimeRules.ToastSpotClear(new GroundPoint(0.5f, 1.2f), 10f, 10f, seats));   // at a bench's end, 0.6 m from a seat
            Assert.True(PastimeRules.ToastSpotClear(new GroundPoint(1f, 0f), 10f, 10f, seats));   // a metre off
            Assert.True(PastimeRules.ToastSpotClear(new GroundPoint(1f, 0f), 9.8f, 10f, null));   // ground a little lower: still the floor
            Assert.True(PastimeRules.ToastSpotClear(new GroundPoint(1f, 0f), 9.5f, 10f, null));   // downhill outdoors
            Assert.False(PastimeRules.ToastSpotClear(new GroundPoint(1f, 0f), 9.2f, 10f, null));   // off a raised floor's edge, below it (the review)
        }

        [Fact]
        public void The_spots_round_a_table_follow_its_own_shape()
        {
            // The review of the toast's spots: a circle by the table's longest reach put the side spots of a 3.2 m oak table
            // 3 m out in the aisle. Its own half-lengths plus a step instead: ends, sides and corners, in the table's own frame.
            List<GroundPoint> spots = PastimeRules.AroundTable(3.2f, 0.6f);
            Assert.Equal(8, spots.Count);
            Assert.Contains(spots, s => Math.Abs(s.X - (3.2f + PastimeRules.ToastOut)) < 0.01f && Math.Abs(s.Z) < 0.01f);   // an end
            Assert.Contains(spots, s => Math.Abs(s.Z - (0.6f + PastimeRules.ToastOut)) < 0.01f && Math.Abs(s.X) < 0.01f);   // a side's middle
            foreach (GroundPoint s in spots)
            {
                float offX = Math.Max(0f, Math.Abs(s.X) - 3.2f), offZ = Math.Max(0f, Math.Abs(s.Z) - 0.6f);
                float fromEdge = (float)Math.Sqrt(offX * offX + offZ * offZ);
                Assert.InRange(fromEdge, 0.5f, PastimeRules.ToastOut + 0.01f);   // a step from the table's edge, never in the aisle
            }
            Assert.Equal(8, spots.Select(s => $"{s.X:0.00},{s.Z:0.00}").Distinct().Count());
        }
    }
}
