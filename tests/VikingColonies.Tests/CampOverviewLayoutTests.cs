using System.Collections.Generic;
using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>
    /// The Overview's boxes (1.6.0, the player's look at 1.5.1: the approved picture has a box per block, filling the window;
    /// 1.5.1's lower half was empty).
    /// </summary>
    public class CampOverviewLayoutTests
    {
        public static IEnumerable<object[]> Shown() => new[]
        {
            new object[] { false, false }, new object[] { true, false }, new object[] { false, true }, new object[] { true, true },
        };

        private static IReadOnlyList<UiBox> Boxes(OverviewBoxes o) =>
            new[] { o.Settlers, o.Food, o.Mood, o.Newcomer, o.Builders, o.Storehouse, o.Steps }.Where(b => b.Height > 0f).ToList();

        [Theory, MemberData(nameof(Shown))]
        public void Every_box_stays_inside_the_overview(bool storehouse, bool steps)
        {
            float half = CampWindowLayout.Width / 2f - CampWindowLayout.OverviewSide;
            foreach (UiBox b in Boxes(CampWindowLayout.Overview(storehouse, steps)))
            {
                Assert.True(b.Left >= -half - 0.01f && b.Right <= half + 0.01f, $"a box runs from {b.Left} to {b.Right}");
                Assert.True(b.Top <= CampWindowLayout.OverviewTop + 0.01f && b.Bottom >= CampWindowLayout.OverviewBottom - 0.01f,
                    $"a box runs from {b.Top} down to {b.Bottom}");
            }
        }

        [Theory, MemberData(nameof(Shown))]
        public void No_two_boxes_touch(bool storehouse, bool steps)
        {
            IReadOnlyList<UiBox> boxes = Boxes(CampWindowLayout.Overview(storehouse, steps));
            for (int i = 0; i < boxes.Count; i++)
                for (int j = i + 1; j < boxes.Count; j++)
                {
                    UiBox a = boxes[i], b = boxes[j];
                    float gap = CampWindowLayout.BoxGap - 0.01f;
                    bool apart = a.Right + gap <= b.Left || b.Right + gap <= a.Left || a.Bottom - gap >= b.Top || b.Bottom - gap >= a.Top;
                    Assert.True(apart, $"boxes {i} and {j} touch");
                }
        }

        [Theory, MemberData(nameof(Shown))]
        public void The_boxes_fill_the_window_to_the_bottom(bool storehouse, bool steps)
        {
            OverviewBoxes o = CampWindowLayout.Overview(storehouse, steps);
            float lowest = Boxes(o).Min(b => b.Bottom);
            Assert.Equal(CampWindowLayout.OverviewBottom, lowest, 1);
            Assert.Equal(CampWindowLayout.OverviewTop, o.Settlers.Top, 1);
            // Each row runs the overview's whole width.
            float half = CampWindowLayout.Width / 2f - CampWindowLayout.OverviewSide;
            Assert.Equal(-half, o.Settlers.Left, 1);
            Assert.Equal(half, o.Mood.Right, 1);
            Assert.Equal(-half, o.Newcomer.Left, 1);
            Assert.Equal(half, storehouse ? o.Storehouse.Right : o.Builders.Right, 1);
            if (steps) Assert.Equal(2f * half, o.Steps.Width, 1);
        }

        [Theory, MemberData(nameof(Shown))]
        public void Each_row_follows_the_one_above_with_no_gap_but_the_boxes_own(bool storehouse, bool steps)
        {
            OverviewBoxes o = CampWindowLayout.Overview(storehouse, steps);
            UiBox[] first = { o.Settlers, o.Food, o.Mood };
            UiBox[] second = storehouse ? new[] { o.Newcomer, o.Builders, o.Storehouse } : new[] { o.Newcomer, o.Builders };
            foreach (UiBox[] row in new[] { first, second })
                foreach (UiBox b in row)
                {
                    Assert.Equal(row[0].Top, b.Top, 1);
                    Assert.Equal(row[0].Height, b.Height, 1);
                }
            Assert.Equal(o.Settlers.Bottom - CampWindowLayout.BoxGap, o.Newcomer.Top, 1);
            if (steps) Assert.Equal(o.Newcomer.Bottom - CampWindowLayout.BoxGap, o.Steps.Top, 1);
        }

        [Theory, MemberData(nameof(Shown))]
        public void Hand_over_sits_inside_the_builders_box_under_its_lines(bool storehouse, bool steps)
        {
            UiBox builders = CampWindowLayout.Overview(storehouse, steps).Builders;
            UiBox button = CampWindowLayout.HandOver(builders);
            Assert.True(button.Left >= builders.Left && button.Right <= builders.Right, $"Hand over runs from {button.Left} to {button.Right}");
            Assert.True(button.Bottom >= builders.Bottom && button.Top <= builders.Top - 60f, $"Hand over runs from {button.Top} down to {button.Bottom}");
            // The builders' lines stop above it.
            Assert.True(builders.Bottom + CampWindowLayout.HandOverRoom >= button.Top);
        }

        [Fact]
        public void The_storehouse_and_hugins_step_only_when_shown()
        {
            OverviewBoxes none = CampWindowLayout.Overview(false, false);
            Assert.Equal(0f, none.Storehouse.Height);
            Assert.Equal(0f, none.Steps.Height);
            OverviewBoxes both = CampWindowLayout.Overview(true, true);
            Assert.True(both.Storehouse.Height > 0f);
            Assert.True(both.Steps.Height > 0f);
            // The second row shares its width between the boxes it has.
            Assert.Equal(none.Newcomer.Width, none.Builders.Width, 1);
            Assert.True(both.Builders.Width < none.Builders.Width);
        }

        [Theory, MemberData(nameof(Shown))]
        public void The_first_row_has_room_for_a_big_number_and_three_lines(bool storehouse, bool steps)
        {
            OverviewBoxes o = CampWindowLayout.Overview(storehouse, steps);
            foreach (UiBox b in new[] { o.Settlers, o.Food, o.Mood }) Assert.True(b.Height >= 150f, $"a first-row box is {b.Height} tall");
            Assert.True(o.Builders.Height >= 120f, "the builders' box holds its lines and Hand over");
        }

        [Fact]
        public void The_overview_starts_under_the_subtitle_and_ends_over_the_bottom_row()
        {
            Assert.True(CampWindowLayout.OverviewTop <= CampWindowLayout.SubtitleTop - 28f);
            // The bottom row's buttons are 36 high, their centres 25 from the window's foot.
            Assert.True(CampWindowLayout.OverviewBottom >= -(CampWindowLayout.Height - 25f - 18f - 8f));
        }
    }
}
