namespace VikingColonies.Gui
{
    internal static class Windows
    {
        public static readonly HutWindow Hut = new HutWindow();
        public static readonly TownHallWindow TownHall = new TownHallWindow();
        public static readonly TreesWindow Trees = new TreesWindow();
        public static readonly PickWindow Pick = new PickWindow();
        public static readonly GrowWindow Grow = new GrowWindow();
        public static readonly FieldWindow Field = new FieldWindow();
        public static readonly FieldCropWindow FieldCrop = new FieldCropWindow();
        public static readonly StockAddWindow StockAdd = new StockAddWindow();
        public static readonly FeedbackWindow Feedback = new FeedbackWindow();

        /// <summary>A reply to the player: in the open window's message line, else in the game's message corner (Core.Replies).</summary>
        public static void Reply(string text)
        {
            switch (Core.Replies.Place(Hut.IsOpen, TownHall.IsOpen))
            {
                case Core.ReplyPlace.HutWindow:
                    Hut.ShowMessage(text);
                    break;
                case Core.ReplyPlace.HallWindow:
                    TownHall.ShowMessage(text);
                    break;
                default:
                    if (Player.m_localPlayer != null) Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, text);
                    break;
            }
        }

        /// <summary>Any colony window is open (the feedback ask waits until none is, 1.4.2).</summary>
        public static bool AnyOpen() =>
            Hut.IsOpen || TownHall.IsOpen || Trees.IsOpen || Pick.IsOpen || Grow.IsOpen || Field.IsOpen || FieldCrop.IsOpen || StockAdd.IsOpen || Feedback.IsOpen;
    }
}
