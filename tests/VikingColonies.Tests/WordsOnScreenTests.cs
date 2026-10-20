using System.Collections.Generic;
using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>
    /// The player, watching the release build (2026-10-19): settlers' words "stack up in weird places on the screen" when
    /// they are just out of view. Words show only for a settler whose head is on the screen, the nearest few.
    /// </summary>
    public class WordsOnScreenTests
    {
        [Fact]
        public void Words_show_for_a_head_on_the_screen_and_not_for_one_off_it_or_behind_the_camera()
        {
            Assert.True(WordsOnScreen.Shows(0.5f, 0.6f, 10f));      // in the middle, in front
            Assert.True(WordsOnScreen.Shows(0.05f, 0.9f, 3f));      // near an edge, still on it
            Assert.False(WordsOnScreen.Shows(1.02f, 0.5f, 10f));    // just off the right edge
            Assert.False(WordsOnScreen.Shows(-0.01f, 0.5f, 10f));   // just off the left edge
            Assert.False(WordsOnScreen.Shows(0.5f, 1.05f, 10f));    // above the top
            Assert.False(WordsOnScreen.Shows(0.5f, -0.2f, 10f));    // below the bottom
            Assert.False(WordsOnScreen.Shows(0.5f, 0.5f, -4f));     // behind the camera (mirrored into the middle)
            Assert.False(WordsOnScreen.Shows(0.5f, 0.5f, 0f));      // at the camera itself
        }

        [Fact]
        public void A_thin_margin_keeps_words_off_the_very_edge()
        {
            Assert.False(WordsOnScreen.Shows(WordsOnScreen.Margin / 2f, 0.5f, 10f));
            Assert.False(WordsOnScreen.Shows(1f - WordsOnScreen.Margin / 2f, 0.5f, 10f));
            Assert.True(WordsOnScreen.Shows(WordsOnScreen.Margin * 2f, 0.5f, 10f));
        }

        [Fact]
        public void At_most_a_few_speak_at_once_the_nearest_first()
        {
            var distances = new List<float> { 9f, 2f, 14f, 5f, 1f, 7f, 3f, 12f, 4f, 6f, 11f };
            List<int> chosen = WordsOnScreen.Nearest(distances, WordsOnScreen.MostAtOnce);
            Assert.Equal(WordsOnScreen.MostAtOnce, chosen.Count);
            Assert.Equal(new[] { 4, 1, 6, 8, 3, 9, 5, 0 }, chosen.Take(8).ToArray());   // 1, 2, 3, 4, 5, 6, 7, 9 m
        }

        [Fact]
        public void Fewer_than_the_limit_all_speak()
        {
            List<int> chosen = WordsOnScreen.Nearest(new List<float> { 3f, 1f }, WordsOnScreen.MostAtOnce);
            Assert.Equal(new[] { 1, 0 }, chosen.ToArray());
        }
    }
}
