using System.Collections.Generic;
using System.Linq;

namespace VikingColonies.Core
{
    /// <summary>A piece of a hut's building, where it goes in the world. <see cref="Index"/> is its number in the plan.</summary>
    public sealed class PlannedPiece
    {
        public PlannedPiece(int index, string prefab, Vec3 position, Quat rotation)
        {
            Index = index;
            Prefab = prefab ?? "";
            Position = position;
            Rotation = rotation;
        }

        public int Index { get; }
        public string Prefab { get; }
        public Vec3 Position { get; }
        public Quat Rotation { get; }
        /// <summary>False for a damaged saved line: it keeps its number but is never built.</summary>
        public bool IsValid => Prefab.Length > 0;
    }

    public static class BlueprintPlan
    {
        /// <summary>
        /// Puts a hut's blueprint around its chest. The line for the hut's own piece marks where the chest stands; every
        /// other piece keeps its place and turn relative to it. Colony buildings ("vc_" pieces) are never built.
        /// </summary>
        public static bool TryPlace(BlueprintFile blueprint, string anchorPrefab, Vec3 chestPosition, Quat chestRotation,
            out List<PlannedPiece> pieces, out string problem)
        {
            pieces = new List<PlannedPiece>();
            BlueprintPiece anchor = blueprint?.Pieces.FirstOrDefault(p => p.Name == anchorPrefab);
            if (anchor == null)
            {
                problem = $"no {anchorPrefab} line marks where the chest goes";
                return false;
            }
            Quat undo = anchor.Rotation.Normalized.Inverse;
            Quat turn = chestRotation.Normalized;
            foreach (BlueprintPiece piece in blueprint.Pieces)
            {
                if (piece.Name == anchorPrefab || piece.Name.StartsWith("vc_")) continue;
                Vec3 local = undo.Rotate(piece.Position - anchor.Position);
                Quat localTurn = undo * piece.Rotation.Normalized;
                pieces.Add(new PlannedPiece(pieces.Count, piece.Name, chestPosition + turn.Rotate(local), (turn * localTurn).Normalized));
            }
            problem = pieces.Count == 0 ? "the blueprint has no pieces besides the chest" : null;
            return pieces.Count > 0;
        }
    }
}
