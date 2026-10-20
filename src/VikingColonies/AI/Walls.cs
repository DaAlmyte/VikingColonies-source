using UnityEngine;

namespace VikingColonies.AI
{
    /// <summary>What stands between two points of a hut: its walls, not its furniture.</summary>
    internal static class Walls
    {
        private const float KneeHeight = 0.4f;
        private static int s_mask;
        private static readonly RaycastHit[] s_hits = new RaycastHit[16];

        /// <summary>
        /// A piece of a building's structure (a wall, a door, a post) between two points at knee height. Beds, tables,
        /// chests and other furniture don't count, nor does a piece standing where <paramref name="to"/> is (its chest).
        /// Seen in review: with knee-high furniture counted, a settler inside a player's Longhouse never counted as within it.
        /// </summary>
        public static bool StructureBetween(Vector3 from, Vector3 to)
        {
            if (s_mask == 0) s_mask = LayerMask.GetMask("piece");
            Vector3 a = from + Vector3.up * KneeHeight, b = to + Vector3.up * KneeHeight;
            Vector3 way = b - a;
            float length = way.magnitude;
            if (length < 0.01f) return false;
            int count = Physics.RaycastNonAlloc(a, way / length, s_hits, length, s_mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Piece piece = s_hits[i].collider.GetComponentInParent<Piece>();
                if (piece == null) return true;
                if (!IsStructure(piece) || Utils.DistanceXZ(piece.transform.position, to) <= 0.5f) continue;
                return true;
            }
            return false;
        }

        /// <summary>Walls, floors, doors, beams and the like; not furniture, crafting stations, chests or food.</summary>
        private static bool IsStructure(Piece piece)
        {
            switch (piece.m_category)
            {
                case Piece.PieceCategory.Furniture:
                case Piece.PieceCategory.Crafting:
                case Piece.PieceCategory.Misc:
                case Piece.PieceCategory.Food:
                case Piece.PieceCategory.Feasts:
                case Piece.PieceCategory.Meads:
                    return false;
                default:
                    return true;
            }
        }
    }
}
