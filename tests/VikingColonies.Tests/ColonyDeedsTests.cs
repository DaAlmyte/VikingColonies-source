using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>The colony's deeds (1.5.1, the camp's window: review 8): today and all time, saved on the camp.</summary>
    public class ColonyDeedsTests
    {
        [Fact]
        public void Adding_counts_today_and_all_time()
        {
            var d = new ColonyDeeds();
            Assert.True(d.Add(DeedKinds.Felled, 2, 10));
            d.Add(DeedKinds.Felled, 3, 10);
            Assert.Equal(5, d.Today(DeedKinds.Felled, 10));
            Assert.Equal(5, d.AllTime(DeedKinds.Felled));
        }

        [Fact]
        public void A_new_day_starts_today_at_zero_and_keeps_all_time()
        {
            var d = new ColonyDeeds();
            d.Add(DeedKinds.Felled, 5, 10);
            d.Add(DeedKinds.Felled, 1, 11);
            Assert.Equal(1, d.Today(DeedKinds.Felled, 11));
            Assert.Equal(6, d.AllTime(DeedKinds.Felled));
        }

        [Fact]
        public void A_colony_left_for_days_shows_zero_today()
        {
            var d = new ColonyDeeds();
            d.Add(DeedKinds.Mended, 4, 10);
            Assert.Equal(0, d.Today(DeedKinds.Mended, 14));
            Assert.Equal(4, d.AllTime(DeedKinds.Mended));
        }

        [Fact]
        public void Unknown_kinds_and_amounts_out_of_range_are_dropped()
        {
            var d = new ColonyDeeds();
            Assert.False(d.Add("gold", 1, 1));
            Assert.False(d.Add(DeedKinds.Carried, 0, 1));
            Assert.False(d.Add(DeedKinds.Carried, -5, 1));
            Assert.False(d.Add(DeedKinds.Carried, ColonyDeeds.MaxAmount + 1, 1));
            Assert.True(d.Add(DeedKinds.Carried, ColonyDeeds.MaxAmount, 1));
            Assert.Equal(ColonyDeeds.MaxAmount, d.AllTime(DeedKinds.Carried));
        }

        [Fact]
        public void Encode_and_decode_keep_every_count_and_the_day()
        {
            var d = new ColonyDeeds();
            foreach (string k in DeedKinds.All) d.Add(k, 3, 7);
            d.Add(DeedKinds.Lost, 1, 8);
            ColonyDeeds back = ColonyDeeds.Decode(d.Encode());
            Assert.Equal(1, back.Today(DeedKinds.Lost, 8));
            Assert.Equal(4, back.AllTime(DeedKinds.Lost));
            Assert.Equal(0, back.Today(DeedKinds.Felled, 8));
            Assert.Equal(3, back.AllTime(DeedKinds.Felled));
        }

        [Fact]
        public void An_old_save_or_damaged_text_loads_what_it_can()
        {
            Assert.Equal(0, ColonyDeeds.Decode("").AllTime(DeedKinds.Felled));
            Assert.Equal(0, ColonyDeeds.Decode(null).AllTime(DeedKinds.Built));
            ColonyDeeds odd = ColonyDeeds.Decode("x|felled=2,9;gold=1,1;built=a,b;mended=1,4");
            Assert.Equal(9, odd.AllTime(DeedKinds.Felled));
            Assert.Equal(0, odd.AllTime(DeedKinds.Built));
            Assert.Equal(4, odd.AllTime(DeedKinds.Mended));
            Assert.Equal(0, odd.Today(DeedKinds.Mended, -1));   // a day that doesn't read: nothing is today
        }

        [Fact]
        public void The_rows_come_in_the_windows_order()
        {
            Assert.Equal(new[] { "felled", "gathered", "carried", "mended", "built", "raids", "lost" }, DeedKinds.All);
        }
    }
}
