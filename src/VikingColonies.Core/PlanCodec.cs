using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace VikingColonies.Core
{
    /// <summary>A plan as text, one line per piece ("prefab;x;y;z;qx;qy;qz;qw"), so it can be saved with the hut.</summary>
    public static class PlanCodec
    {
        public static string Encode(IReadOnlyList<PlannedPiece> pieces) => string.Join("\n", pieces.Select(p => string.Join(";",
            p.Prefab, F(p.Position.X), F(p.Position.Y), F(p.Position.Z),
            F(p.Rotation.X), F(p.Rotation.Y), F(p.Rotation.Z), F(p.Rotation.W))));

        /// <summary>Never throws. A damaged line becomes an invalid piece so every other piece keeps its number.</summary>
        public static List<PlannedPiece> Decode(string text)
        {
            var pieces = new List<PlannedPiece>();
            if (string.IsNullOrEmpty(text)) return pieces;
            foreach (string line in text.Split('\n'))
            {
                string[] parts = line.Split(';');
                var n = new float[7];
                bool ok = parts.Length == 8 && parts[0].Length > 0;
                for (int i = 0; ok && i < 7; i++)
                    ok = float.TryParse(parts[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out n[i]);
                pieces.Add(ok
                    ? new PlannedPiece(pieces.Count, parts[0], new Vec3(n[0], n[1], n[2]), new Quat(n[3], n[4], n[5], n[6]).Normalized)
                    : new PlannedPiece(pieces.Count, "", Vec3.Zero, Quat.Identity));
            }
            return pieces;
        }

        private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
    }
}
