using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace VikingColonies.Core
{
    /// <summary>The kinds of deed the camp's window counts (1.5.1, review 8), in the order its rows show them.</summary>
    public static class DeedKinds
    {
        public const string Felled = "felled", Gathered = "gathered", Carried = "carried", Mended = "mended", Built = "built",
            Raids = "raids", Lost = "lost";

        public static readonly IReadOnlyList<string> All = new[] { Felled, Gathered, Carried, Mended, Built, Raids, Lost };

        public static bool IsKnown(string kind) => kind != null && All.Contains(kind);
    }

    /// <summary>
    /// The colony's deeds (1.5.1, the Chieftain's Camp's window, from MineColonies' statistics): each kind's count today and
    /// all time. Saved on the camp as "day|kind=today,all;…"; an old save has none, and damaged text loads what it can.
    /// </summary>
    public sealed class ColonyDeeds
    {
        /// <summary>The most one request may add (a load of items): more is a modified game's request, dropped.</summary>
        public const int MaxAmount = 1000;

        private int _day = -1;
        private readonly Dictionary<string, int> _today = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _all = new Dictionary<string, int>();

        /// <summary>Adds a deed on <paramref name="today"/>; a new day starts today's counts at zero. False: dropped.</summary>
        public bool Add(string kind, int amount, int today)
        {
            if (!DeedKinds.IsKnown(kind) || amount < 1 || amount > MaxAmount) return false;
            if (today != _day)
            {
                _today.Clear();
                _day = today;
            }
            _today[kind] = Count(_today, kind) + amount;
            _all[kind] = Count(_all, kind) + amount;
            return true;
        }

        /// <summary>Today's count: 0 when the last deed was on another day, or the saved day didn't read.</summary>
        public int Today(string kind, int today) => _day >= 0 && today == _day ? Count(_today, kind) : 0;

        public int AllTime(string kind) => Count(_all, kind);

        private static int Count(Dictionary<string, int> counts, string kind) =>
            kind != null && counts.TryGetValue(kind, out int n) ? n : 0;

        public string Encode()
        {
            var text = new StringBuilder(_day.ToString(CultureInfo.InvariantCulture)).Append('|');
            bool first = true;
            foreach (string kind in DeedKinds.All)
            {
                if (!_all.ContainsKey(kind)) continue;
                if (!first) text.Append(';');
                first = false;
                text.Append(kind).Append('=').Append(Count(_today, kind).ToString(CultureInfo.InvariantCulture))
                    .Append(',').Append(Count(_all, kind).ToString(CultureInfo.InvariantCulture));
            }
            return text.ToString();
        }

        public static ColonyDeeds Decode(string text)
        {
            var deeds = new ColonyDeeds();
            if (string.IsNullOrEmpty(text)) return deeds;
            int bar = text.IndexOf('|');
            string head = bar < 0 ? text : text.Substring(0, bar);
            deeds._day = int.TryParse(head, NumberStyles.Integer, CultureInfo.InvariantCulture, out int day) ? day : -1;
            if (bar < 0) return deeds;
            foreach (string pair in text.Substring(bar + 1).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                string kind = pair.Substring(0, eq);
                string[] counts = pair.Substring(eq + 1).Split(',');
                if (!DeedKinds.IsKnown(kind) || counts.Length != 2) continue;
                if (!int.TryParse(counts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int today) || today < 0) continue;
                if (!int.TryParse(counts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int all) || all < 0) continue;
                deeds._today[kind] = today;
                deeds._all[kind] = all;
            }
            return deeds;
        }
    }
}
