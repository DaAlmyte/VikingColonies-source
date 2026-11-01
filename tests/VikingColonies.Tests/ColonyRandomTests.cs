using System.Collections.Generic;
using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>Seeded choices for test runs (the flake hunt and replays, 26 Oct 2026).</summary>
    [Collection("ColonyRandom")]   // one static seed: these tests never run side by side
    public class ColonyRandomTests
    {
        private static List<double> Draws(string purpose, string who, int n) =>
            Enumerable.Range(0, n).Select(_ => ColonyRandom.For(purpose, who).NextDouble()).ToList();

        [Fact]
        public void Without_a_seed_the_choices_differ_from_run_to_run()
        {
            ColonyRandom.SetSeed(null);
            Assert.Null(ColonyRandom.Seed);
            List<double> a = Draws("pastime", "Ingrid", 8);
            List<double> b = Draws("pastime", "Ingrid", 8);
            Assert.NotEqual(a, b);
        }

        [Fact]
        public void The_same_seed_gives_the_same_draws_for_each_purpose_and_settler()
        {
            ColonyRandom.SetSeed(1234);
            List<double> first = Draws("pastime", "Ingrid", 10);
            ColonyRandom.SetSeed(1234);
            List<double> again = Draws("pastime", "Ingrid", 10);
            Assert.Equal(first, again);
            ColonyRandom.SetSeed(null);
        }

        [Fact]
        public void Another_seed_gives_other_draws()
        {
            ColonyRandom.SetSeed(1);
            List<double> one = Draws("founding", "", 5);
            ColonyRandom.SetSeed(2);
            List<double> two = Draws("founding", "", 5);
            Assert.NotEqual(one, two);
            ColonyRandom.SetSeed(null);
        }

        [Fact]
        public void One_settlers_extra_draws_never_shift_anothers()
        {
            ColonyRandom.SetSeed(77);
            List<double> alone = Draws("pastime", "Leif", 5);
            ColonyRandom.SetSeed(77);
            Draws("pastime", "Ingrid", 13);   // another settler draws in between, as frame timing would have it
            Draws("jitter", "Leif", 3);       // and Leif's own other purpose
            List<double> mixed = Draws("pastime", "Leif", 5);
            Assert.Equal(alone, mixed);
            ColonyRandom.SetSeed(null);
        }

        [Fact]
        public void Purposes_and_settlers_get_streams_of_their_own()
        {
            ColonyRandom.SetSeed(5);
            Assert.NotEqual(Draws("pastime", "Leif", 4), Draws("pastime", "Ingrid", 4));
            ColonyRandom.SetSeed(5);
            Assert.NotEqual(Draws("pastime", "Leif", 4), Draws("jitter", "Leif", 4));
            ColonyRandom.SetSeed(null);
        }

        [Fact]
        public void The_keys_hash_the_same_on_every_computer()
        {
            // FNV-1a, never string.GetHashCode (which .NET may vary between processes).
            Assert.Equal(2166136261u, ColonyRandom.Fnv1a(""));
            Assert.Equal(0xe40c292cu, ColonyRandom.Fnv1a("a"));
            Assert.Equal(0xbf9cf968u, ColonyRandom.Fnv1a("foobar"));
        }

        [Fact]
        public void The_helpers_stay_in_their_ranges()
        {
            ColonyRandom.SetSeed(9);
            for (int i = 0; i < 500; i++)
            {
                float f = ColonyRandom.Range("spot", "Ulf", 2f, 5f);
                Assert.InRange(f, 2f, 5f);
                int n = ColonyRandom.Range("spot", "Ulf", 0, 3);
                Assert.InRange(n, 0, 2);
                float v = ColonyRandom.Value("spot", "Ulf");
                Assert.InRange(v, 0f, 1f);
            }
            Assert.Equal(4, ColonyRandom.Range("spot", "Ulf", 4, 4));   // an empty range gives its start, as Unity's
            ColonyRandom.SetSeed(null);
        }

        [Fact]
        public void A_seed_gives_the_same_new_settler_ids_and_so_the_same_traits()
        {
            // The weak test's founders (the review of 26 Oct): traits come from the id, so the ids must come from the seed.
            ColonyRandom.SetSeed(2026);
            List<string> ids = Enumerable.Range(0, 6).Select(_ => ColonyRandom.NewId()).ToList();
            ColonyRandom.SetSeed(2026);
            List<string> again = Enumerable.Range(0, 6).Select(_ => ColonyRandom.NewId()).ToList();
            Assert.Equal(ids, again);
            Assert.Equal(ids.Select(Traits.FromId), again.Select(Traits.FromId));
            Assert.All(ids, id => Assert.Matches("^[0-9a-f]{32}$", id));
            Assert.Equal(6, ids.Distinct().Count());
            ColonyRandom.SetSeed(2027);
            Assert.NotEqual(ids, Enumerable.Range(0, 6).Select(_ => ColonyRandom.NewId()).ToList());
            ColonyRandom.SetSeed(null);
            Assert.Null(ColonyRandom.NewId());   // unseeded: the game's own random id
        }

        [Fact]
        public void New_ids_dont_shift_the_founding_stream()
        {
            ColonyRandom.SetSeed(8);
            List<double> plain = Draws("founding", "", 6);
            ColonyRandom.SetSeed(8);
            ColonyRandom.NewId();
            ColonyRandom.NewId();
            Assert.Equal(plain, Draws("founding", "", 6));
            ColonyRandom.SetSeed(null);
        }

        [Fact]
        public void A_missing_purpose_or_name_still_draws()
        {
            ColonyRandom.SetSeed(3);
            Assert.InRange(ColonyRandom.For(null, null).NextDouble(), 0.0, 1.0);
            ColonyRandom.SetSeed(null);
            Assert.InRange(ColonyRandom.For(null, null).NextDouble(), 0.0, 1.0);
        }
    }
}
