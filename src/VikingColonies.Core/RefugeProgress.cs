namespace VikingColonies.Core
{
    /// <summary>One step of getting into a refuge: whether he is in, whether he just settled where he stands, and whether he gives the hut up.</summary>
    public struct RefugeStep
    {
        public RefugeStep(bool @in, bool justSettled, bool givesUp)
        {
            In = @in;
            JustSettled = justSettled;
            GivesUp = givesUp;
        }

        public bool In { get; }
        public bool JustSettled { get; }
        public bool GivesUp { get; }
    }

    /// <summary>
    /// How a settler gets on with getting into his refuge (for the night, a raid or an attack). On his spot he is in.
    /// Within its walls but kept off his spot (someone stands on it, furniture is in the way) he stays where he is once he
    /// has got no closer for <see cref="IndoorRules.SettleAnywhereSeconds"/>, though never by the door. He never gives up a
    /// hut he is inside; from outside he gives it up after trying for <see cref="IndoorRules.GiveUpSeconds"/>, a door he
    /// comes through counting as getting on. Seen: two settlers each on the other's sleeping spot left their own Longhouse
    /// for the Builder's Hut, and a forager who took long to reach the door gave the Longhouse up as he stepped in.
    /// </summary>
    public sealed class RefugeProgress
    {
        private double _tryingSince;
        private double _keptOffSince = -1;
        private float _closest;
        private int _passesSeen = -1;

        /// <summary>He stays where he is, kept off his spot.</summary>
        public bool Settled { get; private set; }

        /// <summary>A new refuge, or a new reason to be indoors: everything starts again.</summary>
        public void Restart(double now)
        {
            Settled = false;
            _keptOffSince = -1;
            _tryingSince = now;
        }

        /// <summary>He can't go in yet (he finishes his work first): the clock for giving up waits.</summary>
        public void Wait(double now) => _tryingSince = now;

        /// <param name="onSpot">He stands on his spot.</param>
        /// <param name="withinWalls">He is within the hut's walls (<see cref="IndoorRules.WithinWalls"/>).</param>
        /// <param name="fromDoor">How far he is from its door.</param>
        /// <param name="toSpot">How far he is from his spot.</param>
        /// <param name="doorPasses">How many doors he has gone through so far (a count that only grows).</param>
        /// <param name="doorBusy">He is going through a door now.</param>
        public RefugeStep Step(bool onSpot, bool withinWalls, float fromDoor, float toSpot, int doorPasses, bool doorBusy, double now)
        {
            if (doorPasses != _passesSeen)
            {
                if (_passesSeen >= 0) _tryingSince = now;   // through a door: he is getting on
                _passesSeen = doorPasses;
            }
            bool room = withinWalls && IndoorRules.RoomToSettle(fromDoor);
            if (Settled && (onSpot || !room)) Settled = false;   // on his spot after all, or pushed out or to the door
            bool keptOff = !onSpot && room;
            if (!keptOff) _keptOffSince = -1;
            else if (_keptOffSince < 0 || toSpot < _closest - IndoorRules.SpotProgress)
            {
                _keptOffSince = now;
                _closest = toSpot;
            }
            bool justSettled = false;
            if (!Settled && keptOff && now - _keptOffSince >= IndoorRules.SettleAnywhereSeconds)
            {
                Settled = true;
                justSettled = true;
            }
            bool isIn = onSpot || Settled;
            if (isIn || withinWalls) _tryingSince = now;   // inside: nothing to give up on
            bool givesUp = !doorBusy && now - _tryingSince > IndoorRules.GiveUpSeconds;
            return new RefugeStep(isIn, justSettled, givesUp);
        }
    }
}
