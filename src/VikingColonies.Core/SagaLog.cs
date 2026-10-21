using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace VikingColonies.Core
{
    /// <summary>
    /// One line of the colony's saga: the day it happened, what kind of thing ("arrived", "died", "built", "level",
    /// "recalled", "rescued", "hired", "left") and its words (names, translation tokens, numbers).
    /// </summary>
    public sealed class SagaEntry
    {
        public SagaEntry(int day, string kind, params string[] args)
        {
            Day = day;
            Kind = kind ?? "";
            Args = (args ?? new string[0]).Select(a => a ?? "").ToList();
        }

        public int Day { get; }
        public string Kind { get; }
        public IReadOnlyList<string> Args { get; }
    }

    /// <summary>
    /// The colony's saga, kept with the Town Hall: what happened while you were away, newest first, the newest
    /// <see cref="MaxEntries"/> kept. Saved as one line per entry, "day|kind|word|word", with \ escaping \, | and new
    /// lines; damaged text loads what it can and never throws.
    /// </summary>
    public sealed class SagaLog
    {
        public const int MaxEntries = 60;

        private static readonly HashSet<string> s_kinds = new HashSet<string>
            { "arrived", "died", "built", "level", "recalled", "rescued", "hired", "left", "returned", "came", "repelled", "takendown", "raised", "bossfell" };

        private readonly List<SagaEntry> _entries = new List<SagaEntry>();   // oldest first

        /// <summary>The kinds of line the game writes; anything else (a modified game's request) isn't taken.</summary>
        public static bool IsKnownKind(string kind) => kind != null && s_kinds.Contains(kind);

        public int Count => _entries.Count;

        /// <summary>
        /// Adds a line; the same line again (same day, kind and words) right after it is kept once. Seen in review: a
        /// settler stuck again and again, or Recall clicked again and again, would push everything else out.
        /// </summary>
        public void Add(SagaEntry entry)
        {
            if (entry == null) return;
            if (_entries.Count > 0 && Same(_entries[_entries.Count - 1], entry)) return;
            _entries.Add(entry);
            if (_entries.Count > MaxEntries) _entries.RemoveRange(0, _entries.Count - MaxEntries);
        }

        /// <summary>One page, newest first; empty beyond the end.</summary>
        public IReadOnlyList<SagaEntry> Page(int page, int perPage)
        {
            if (page < 0 || perPage <= 0) return new List<SagaEntry>();
            return Enumerable.Reverse(_entries).Skip(page * perPage).Take(perPage).ToList();
        }

        /// <summary>How many pages (at least one, so an empty saga still has its page).</summary>
        public int PageCount(int perPage) => perPage <= 0 ? 1 : Math.Max(1, (_entries.Count + perPage - 1) / perPage);

        public string Encode()
        {
            var sb = new StringBuilder();
            foreach (SagaEntry entry in _entries)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(entry.Day.ToString(CultureInfo.InvariantCulture)).Append('|').Append(Escape(entry.Kind));
                foreach (string arg in entry.Args) sb.Append('|').Append(Escape(arg));
            }
            return sb.ToString();
        }

        public static SagaLog Decode(string text)
        {
            var log = new SagaLog();
            if (string.IsNullOrEmpty(text)) return log;
            foreach (string line in text.Split('\n'))
            {
                List<string> fields = Split(line);
                if (fields.Count < 2 || fields[1].Length == 0) continue;
                if (!int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int day)) continue;
                log.Add(new SagaEntry(day, fields[1], fields.Skip(2).ToArray()));
            }
            return log;
        }

        private static bool Same(SagaEntry a, SagaEntry b) =>
            a.Day == b.Day && a.Kind == b.Kind && a.Args.SequenceEqual(b.Args);

        private static string Escape(string s) =>
            (s ?? "").Replace("\\", "\\\\").Replace("|", "\\|").Replace("\n", "\\n");

        /// <summary>The fields of one saved line: split at | not escaped, escapes undone (a cut-off escape is dropped).</summary>
        private static List<string> Split(string line)
        {
            var fields = new List<string>();
            var field = new StringBuilder();
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '\\')
                {
                    if (i + 1 >= line.Length) break;   // cut off: drop it
                    char next = line[++i];
                    field.Append(next == 'n' ? '\n' : next);
                }
                else if (c == '|')
                {
                    fields.Add(field.ToString());
                    field.Clear();
                }
                else if (c != '\r') field.Append(c);
            }
            fields.Add(field.ToString());
            return fields;
        }
    }
}
