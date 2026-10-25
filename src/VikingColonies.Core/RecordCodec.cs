using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace VikingColonies.Core
{
    /// <summary>
    /// Turns key/value pairs into one saved string ("k=v|k=v") and back. Any character can appear in values.
    /// Numbers are always written with a dot, whatever the PC's language settings.
    /// </summary>
    public static class RecordCodec
    {
        public static string Encode(IEnumerable<KeyValuePair<string, string>> fields)
        {
            var sb = new StringBuilder();
            foreach (KeyValuePair<string, string> field in fields)
            {
                if (sb.Length > 0) sb.Append('|');
                sb.Append(Escape(field.Key)).Append('=').Append(Escape(field.Value ?? ""));
            }
            return sb.ToString();
        }

        /// <summary>Never throws; malformed parts are skipped.</summary>
        public static Dictionary<string, string> Decode(string text)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(text)) return result;
            foreach (string part in text.Split('|'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                result[Unescape(part.Substring(0, eq))] = Unescape(part.Substring(eq + 1));
            }
            return result;
        }

        public static string Escape(string s) =>
            s.Replace("\\", "\\\\").Replace("|", "\\p").Replace("=", "\\e").Replace("\n", "\\n");

        public static string Unescape(string s)
        {
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '\\' || i + 1 >= s.Length)
                {
                    sb.Append(c);
                    continue;
                }
                char next = s[++i];
                sb.Append(next == 'p' ? '|' : next == 'e' ? '=' : next == 'n' ? '\n' : next);
            }
            return sb.ToString();
        }

        public static string FormatFloats(float[] values) =>
            values == null ? "" : string.Join(",", values.Select(v => v.ToString("R", CultureInfo.InvariantCulture)));

        /// <summary>
        /// A saved number that is a real, finite one: "NaN" and "Infinity" are refused (the review round of 2026-10-23: a piece
        /// placed at NaN, the ground levelled towards infinity, a skill levelling up on every hit).
        /// </summary>
        public static bool TryParseFinite(string text, out float value) =>
            float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !float.IsNaN(value) && !float.IsInfinity(value);

        /// <summary>As the float one, for a double.</summary>
        public static bool TryParseFinite(string text, out double value) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !double.IsNaN(value) && !double.IsInfinity(value);

        /// <returns>The numbers, or null when the text is empty, malformed or has the wrong count.</returns>
        public static float[] ParseFloats(string text, int count)
        {
            if (string.IsNullOrEmpty(text)) return null;
            string[] parts = text.Split(',');
            if (parts.Length != count) return null;
            var values = new float[count];
            for (int i = 0; i < count; i++)
                if (!TryParseFinite(parts[i], out values[i])) return null;
            return values;
        }

        public static int ParseInt(string text, int fallback) =>
            int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : fallback;

        public static string FormatInt(int value) => value.ToString(CultureInfo.InvariantCulture);

        public static string Get(Dictionary<string, string> fields, string key) =>
            fields.TryGetValue(key, out string value) ? value : "";
    }
}
