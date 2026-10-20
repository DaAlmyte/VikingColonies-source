namespace VikingColonies.Core
{
    /// <summary>
    /// A settler who dies carrying something leaves a cairn where he fell, holding it; one who carried nothing leaves
    /// none (the death message is all). Players told of the death get a map pin to it.
    /// </summary>
    public static class Graves
    {
        /// <summary>The cairn's chest: room for more kinds of things than a settler carries.</summary>
        public const int Columns = 5;
        public const int Rows = 4;

        public static bool HasGrave(CarryLoad carry) => carry != null && carry.Total > 0;

        public static string PinName(string settlerName) => $"{settlerName}'s cairn";
    }
}
