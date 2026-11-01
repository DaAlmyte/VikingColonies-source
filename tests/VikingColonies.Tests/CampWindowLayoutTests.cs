using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>The camp window's bottom row (review of 1.5.1: Report a problem and Feedback ran into each other).</summary>
    public class CampWindowLayoutTests
    {
        private const float Gap = 8f, Edge = 5f;

        [Fact]
        public void The_bottom_rows_buttons_keep_apart()
        {
            RowButton[] row = CampWindowLayout.BottomRow.OrderBy(b => b.X).ToArray();
            for (int i = 1; i < row.Length; i++)
                Assert.True(row[i - 1].Right + Gap <= row[i].Left,
                    $"the button at {row[i - 1].X} ends at {row[i - 1].Right}, the next at {row[i].X} starts at {row[i].Left}");
        }

        [Fact]
        public void The_bottom_rows_buttons_stay_inside_the_window()
        {
            float half = CampWindowLayout.Width / 2f - Edge;
            foreach (RowButton b in CampWindowLayout.BottomRow)
                Assert.True(b.Left >= -half && b.Right <= half, $"the button at {b.X} runs from {b.Left} to {b.Right}");
        }
    }
}
