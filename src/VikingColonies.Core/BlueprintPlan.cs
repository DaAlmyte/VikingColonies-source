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

        /// <summary>A hut's chest or the Town Hall's: the piece a hut's design is placed by, never put up with it.</summary>
        public static bool IsChest(string name) =>
            name != null && (name.StartsWith("vc_hut_", System.StringComparison.Ordinal) || name == SaveKeys.TownHallPiece);

        /// <summary>
        /// vc_preview (the build tools, 2026-10-25): a design laid out round its own origin, which goes to
        /// <paramref name="origin"/> turned by <paramref name="turn"/>; a showcase design has no chest line (a hut's chest
        /// and the Town Hall's are left out; the mod's other pieces, the field marker or the scarecrow, are put up: until
        /// the scarecrow's preview of 27 Oct 2026 every "vc_" line was left out). A missing design is an empty plan.
        /// </summary>
        public static List<PlannedPiece> PlaceAround(BlueprintFile blueprint, Vec3 origin, Quat turn)
        {
            var pieces = new List<PlannedPiece>();
            if (blueprint == null) return pieces;
            Quat t = turn.Normalized;
            foreach (BlueprintPiece piece in blueprint.Pieces)
            {
                if (IsChest(piece.Name)) continue;
                pieces.Add(new PlannedPiece(pieces.Count, piece.Name, origin + t.Rotate(piece.Position), (t * piece.Rotation.Normalized).Normalized));
            }
            return pieces;
        }
    }
}
