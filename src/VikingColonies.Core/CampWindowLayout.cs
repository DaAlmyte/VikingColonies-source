namespace VikingColonies.Core
{
    /// <summary>A button in a row: its centre and its width (the game's buttons are placed by their centre).</summary>
    public struct RowButton
    {
        public readonly float X, Width;

        public RowButton(float x, float width)
        {
            X = x;
            Width = width;
        }

        public float Left => X - Width / 2f;
        public float Right => X + Width / 2f;
    }

    /// <summary>A box on a window: its centre across, its top edge (negative, down from the window's top), its size.</summary>
    public struct UiBox
    {
        public readonly float X, Top, Width, Height;

        public UiBox(float x, float top, float width, float height)
        {
            X = x;
            Top = top;
            Width = width;
            Height = height;
        }

        public float Left => X - Width / 2f;
        public float Right => X + Width / 2f;
        public float Bottom => Top - Height;
    }

    /// <summary>The Overview's boxes; one not shown has no height.</summary>
    public struct OverviewBoxes
    {
        public UiBox Settlers, Food, Mood, Newcomer, Builders, Storehouse, Steps;
    }

    /// <summary>
    /// The camp window's size and its bottom row (1.5.1): the alarm on the left, Close in the middle, Report a problem and
    /// Feedback on the right. Kept here so a test sees them apart (the review of 1.5.1 found two touching).
    /// </summary>
    public static class CampWindowLayout
    {
        public const float Width = 760f, Height = 740f;

        /// <summary>Every window's Close button is this wide (WindowBase).</summary>
        public const float CloseWidth = 140f;

        public static readonly RowButton Alarm = new RowButton(-270f, 170f);
        public static readonly RowButton Close = new RowButton(0f, CloseWidth);
        public static readonly RowButton Report = new RowButton(205f, 130f);
        public static readonly RowButton Feedback = new RowButton(325f, 90f);

        public static RowButton[] BottomRow => new[] { Alarm, Close, Report, Feedback };

        /// <summary>The Overview's subtitle line (level, day) under the tabs.</summary>
        public const float SubtitleTop = -100f;
        /// <summary>Where the Overview's boxes start and end (the bottom row's buttons below), and the room at each side.</summary>
        public const float OverviewTop = -132f, OverviewBottom = -682f, OverviewSide = 30f;
        public const float BoxGap = 12f;

        /// <summary>
        /// The Overview as the approved picture has it (1.6.0, the player's look at 1.5.1): settlers, food and mood across
        /// the top; the next newcomer, what the builders need and the storehouse (only when it has news) under them; Hugin's
        /// next step across the foot (only while there is one). The rows share the height between them, so the boxes always
        /// reach the bottom row: 1.5.1's lower half stood empty.
        /// </summary>
        public static OverviewBoxes Overview(bool storehouse, bool steps)
        {
            float rows = OverviewTop - OverviewBottom - (steps ? 2f : 1f) * BoxGap;
            float first = steps ? rows * 0.38f : rows * 0.5f;
            float second = steps ? rows * 0.34f : rows - first;
            float third = steps ? rows - first - second : 0f;
            float top2 = OverviewTop - first - BoxGap, top3 = top2 - second - BoxGap;

            var o = new OverviewBoxes();
            UiBox[] row1 = Row(OverviewTop, first, 3);
            o.Settlers = row1[0];
            o.Food = row1[1];
            o.Mood = row1[2];
            UiBox[] row2 = Row(top2, second, storehouse ? 3 : 2);
            o.Newcomer = row2[0];
            o.Builders = row2[1];
            o.Storehouse = storehouse ? row2[2] : new UiBox(row2[1].X, top2, row2[1].Width, 0f);
            o.Steps = steps ? Row(top3, third, 1)[0] : new UiBox(0f, top2 - second, Width - 2f * OverviewSide, 0f);
            return o;
        }

        /// <summary>The Hand over button's size, and the padding inside a box.</summary>
        public const float HandOverWidth = 140f, HandOverHeight = 30f, BoxPad = 10f;

        /// <summary>The Hand over button in the builders' box, at its foot on the left: its centre across and its top edge.</summary>
        public static UiBox HandOver(UiBox builders) =>
            new UiBox(builders.Left + BoxPad + HandOverWidth / 2f, builders.Bottom + BoxPad + HandOverHeight, HandOverWidth, HandOverHeight);

        /// <summary>Room the builders' lines leave at the box's foot for Hand over.</summary>
        public const float HandOverRoom = HandOverHeight + BoxPad;

        /// <summary><paramref name="count"/> boxes side by side across the Overview's width.</summary>
        private static UiBox[] Row(float top, float height, int count)
        {
            float span = Width - 2f * OverviewSide, each = (span - (count - 1) * BoxGap) / count;
            var row = new UiBox[count];
            for (int i = 0; i < count; i++) row[i] = new UiBox(-span / 2f + each / 2f + i * (each + BoxGap), top, each, height);
            return row;
        }
    }
}
