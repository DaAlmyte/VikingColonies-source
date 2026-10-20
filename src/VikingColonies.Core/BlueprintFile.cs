using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace VikingColonies.Core
{
    /// <summary>One line of a blueprint: a hammer piece and where it stands relative to the blueprint's origin.</summary>
    public sealed class BlueprintPiece
    {
        public BlueprintPiece(string name, string category, Vec3 position, Quat rotation, Vec3 scale, string info)
        {
            Name = name;
            Category = category ?? "";
            Position = position;
            Rotation = rotation;
            Scale = scale;
            Info = info ?? "";
        }

        public string Name { get; }
        public string Category { get; }
        public Vec3 Position { get; }
        public Quat Rotation { get; }
        public Vec3 Scale { get; }
        public string Info { get; }
    }

    /// <summary>
    /// A blueprint in PlanBuild's ".blueprint" text format: "#Name:", "#Creator:", "#Description:" (a JSON string),
    /// "#Category:", then sections; the "#Pieces" section has one line per piece:
    /// name;category;posX;posY;posZ;rotX;rotY;rotZ;rotW;"info";scaleX;scaleY;scaleZ (scale optional).
    /// </summary>
    public sealed class BlueprintFile
    {
        private const string HeaderName = "#Name:";
        private const string HeaderCreator = "#Creator:";
        private const string HeaderDescription = "#Description:";
        private const string HeaderCategory = "#Category:";
        private const string SectionPieces = "#Pieces";

        public string Name = "";
        public string Creator = "";
        public string Description = "";
        public string Category = "";
        public readonly List<BlueprintPiece> Pieces = new List<BlueprintPiece>();
        /// <summary>Lines that were skipped, as "line N: why".</summary>
        public readonly List<string> Problems = new List<string>();

        /// <summary>Never throws. Snap points and terrain changes are not used and are skipped.</summary>
        public static BlueprintFile Parse(IEnumerable<string> lines)
        {
            var blueprint = new BlueprintFile();
            bool inOtherSection = false;
            int number = 0;
            foreach (string raw in lines ?? Enumerable.Empty<string>())
            {
                number++;
                string line = (raw ?? "").TrimStart('﻿').Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("#"))
                {
                    if (line.StartsWith(HeaderName)) blueprint.Name = line.Substring(HeaderName.Length);
                    else if (line.StartsWith(HeaderCreator)) blueprint.Creator = line.Substring(HeaderCreator.Length);
                    else if (line.StartsWith(HeaderDescription)) blueprint.Description = Unquote(line.Substring(HeaderDescription.Length));
                    else if (line.StartsWith(HeaderCategory)) blueprint.Category = line.Substring(HeaderCategory.Length);
                    else inOtherSection = line != SectionPieces;
                    continue;
                }
                if (inOtherSection) continue;
                if (TryParsePiece(line, out BlueprintPiece piece, out string why)) blueprint.Pieces.Add(piece);
                else blueprint.Problems.Add($"line {number}: {why}");
            }
            return blueprint;
        }

        public IEnumerable<string> ToLines()
        {
            yield return HeaderName + Name;
            yield return HeaderCreator + Creator;
            yield return HeaderDescription + Quote(Description);
            yield return HeaderCategory + Category;
            yield return SectionPieces;
            foreach (BlueprintPiece p in Pieces)
            {
                yield return string.Join(";", p.Name, p.Category,
                    F(p.Position.X), F(p.Position.Y), F(p.Position.Z),
                    F(p.Rotation.X), F(p.Rotation.Y), F(p.Rotation.Z), F(p.Rotation.W),
                    Quote(p.Info), F(p.Scale.X), F(p.Scale.Y), F(p.Scale.Z));
            }
        }

        private static bool TryParsePiece(string line, out BlueprintPiece piece, out string why)
        {
            piece = null;
            // Old PlanBuild files were written with the PC's decimal comma; PlanBuild itself reads them this way.
            string text = line.IndexOf(',') >= 0 ? line.Replace(',', '.') : line;
            string[] parts = text.Split(';');
            if (parts.Length < 9)
            {
                why = "a piece line needs at least 9 fields";
                return false;
            }
            string name = parts[0].Split('(')[0].Trim();
            if (name.Length == 0)
            {
                why = "no piece name";
                return false;
            }
            var numbers = new float[7];
            for (int i = 0; i < 7; i++)
            {
                if (!float.TryParse(parts[i + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i]))
                {
                    why = $"'{parts[i + 2]}' is not a number";
                    return false;
                }
            }
            string info = parts.Length > 9 ? Unquote(parts[9]) : "";
            if (info == "null") info = "";
            Vec3 scale = new Vec3(1f, 1f, 1f);
            if (parts.Length > 12
                && float.TryParse(parts[10], NumberStyles.Float, CultureInfo.InvariantCulture, out float sx)
                && float.TryParse(parts[11], NumberStyles.Float, CultureInfo.InvariantCulture, out float sy)
                && float.TryParse(parts[12], NumberStyles.Float, CultureInfo.InvariantCulture, out float sz))
                scale = new Vec3(sx, sy, sz);
            piece = new BlueprintPiece(name, parts[1], new Vec3(numbers[0], numbers[1], numbers[2]),
                new Quat(numbers[3], numbers[4], numbers[5], numbers[6]).Normalized, scale, info);
            why = null;
            return true;
        }

        private static string F(float value) => value.ToString("0.######", CultureInfo.InvariantCulture);

        /// <summary>A JSON string: quoted, with \" \\ and \n escaped.</summary>
        private static string Quote(string text) =>
            "\"" + (text ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";

        private static string Unquote(string text)
        {
            if (text == null || text.Length < 2 || text[0] != '"' || text[text.Length - 1] != '"') return text ?? "";
            var sb = new StringBuilder();
            for (int i = 1; i < text.Length - 1; i++)
            {
                char c = text[i];
                if (c == '\\' && i + 1 < text.Length - 1)
                {
                    char next = text[++i];
                    sb.Append(next == 'n' ? '\n' : next);
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
