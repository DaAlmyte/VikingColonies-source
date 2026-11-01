namespace VikingColonies.Core
{
    public enum SeatStep { Nothing, Keep, LetGo, Release }

    /// <summary>
    /// A settler held on a seat or in a bed, each physics step (SettlerPose). Whether they're held is its own state, never
    /// read off the seat: the builders' test of 27 Oct took the camp's log bench down under three settlers, the game's
    /// destroyed seat read as "not held", nothing let them go, and they stayed held as seated (no gravity, "attached") and
    /// were found 12-17 m under the ground.
    /// </summary>
    public sealed class SeatHold
    {
        /// <summary>Held further than this from their point (pushed, the seat moved), they stand up.</summary>
        public const float SlipDistance = 1f;

        public bool Holding { get; private set; }

        public void Start() => Holding = true;

        public void Stop() => Holding = false;

        /// <summary>
        /// Not held: nothing. Another computer runs them now: released (it starts them standing). The seat gone, or they're
        /// off it: they stand up. Otherwise kept on it.
        /// </summary>
        public SeatStep Step(bool owner, bool seatThere, float distance)
        {
            if (!Holding) return SeatStep.Nothing;
            if (!owner) return SeatStep.Release;
            if (!seatThere || !(distance <= SlipDistance)) return SeatStep.LetGo;
            return SeatStep.Keep;
        }
    }
}
