using UnityEngine;
using VikingColonies.Core;

namespace VikingColonies.Construction
{
    internal static class HutDoors
    {
        /// <summary>The door of a hut's building (any planned piece with a door: a door or a gate), standing; null if none.</summary>
        public static Door DoorOf(ConstructionModule site)
        {
            if (site == null) return null;
            foreach (PlannedPiece piece in site.Plan)
            {
                GameObject prefab = piece.IsValid ? PiecePlacer.Prefab(piece.Prefab) : null;
                if (prefab == null || prefab.GetComponent<Door>() == null) continue;
                Piece standing = PiecePlacer.FindStanding(piece);
                if (standing != null) return standing.GetComponent<Door>();
            }
            return null;
        }
    }
}
