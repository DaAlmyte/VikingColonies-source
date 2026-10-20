using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace VikingColonies.Core
{
    public sealed class ReportBuilding
    {
        public string Type = "";
        public string Id = "";
        public int Level = 1;
        public Vec3 Position;
        public float Turn;
        public Vec3? Door;
        /// <summary>One line about its build or upgrade order; "" when it has none.</summary>
        public string Order = "";
        /// <summary>What its chest holds; null when it has no chest.</summary>
        public IReadOnlyDictionary<string, int> Chest;
        /// <summary>Its chest's columns x rows ("6x4"); "" when unknown.</summary>
        public string ChestSize = "";
    }

    public sealed class ReportSettler
    {
        public string Name = "";
        public string Job = "";
        public string Workplace = "";
        public string Status = "";
        public Vec3 Position;
        public string Carry = "";
        /// <summary>His last states, newest first (vc_bugreport, 0.27); null when not known here.</summary>
        public List<string> Recent;
        /// <summary>What went wrong for him (errors, rescues, hops…), "" when nothing (0.27).</summary>
        public string Troubles = "";
    }

    public sealed class ReportColony
    {
        public string Id = "";
        public Vec3 Hall;
        public List<ReportBuilding> Buildings { get; } = new List<ReportBuilding>();
        public List<ReportSettler> Settlers { get; } = new List<ReportSettler>();
    }

    /// <summary>vc_report: the colonies as plain text, one line per building and settler, for reading in a file.</summary>
    public static class ColonyReport
    {
        public static string Format(string header, IReadOnlyList<ReportColony> colonies, IReadOnlyList<ReportSettler> withoutColony)
        {
            var sb = new StringBuilder();
            sb.Append(header).Append('\n');
            if (colonies.Count == 0) sb.Append("No colonies.\n");
            foreach (ReportColony colony in colonies)
            {
                sb.Append($"Colony {colony.Id}: Town Hall at {P(colony.Hall)}, {colony.Settlers.Count} settlers\n");
                foreach (ReportBuilding b in colony.Buildings)
                {
                    sb.Append($"  {b.Type} {b.Id} level {b.Level} at {P(b.Position)} turned {b.Turn.ToString("0", CultureInfo.InvariantCulture)}");
                    if (b.Door.HasValue) sb.Append($", door at {P(b.Door.Value)}");
                    sb.Append('\n');
                    if (b.Order.Length > 0) sb.Append($"    order: {b.Order}\n");
                    if (b.Chest != null) sb.Append($"    chest{(b.ChestSize.Length > 0 ? " " + b.ChestSize : "")}: {Items(b.Chest)}\n");
                }
                foreach (ReportSettler s in colony.Settlers) AppendSettler(sb, s);
            }
            if (withoutColony.Count > 0)
            {
                sb.Append("Without a colony:\n");
                foreach (ReportSettler s in withoutColony) AppendSettler(sb, s);
            }
            return sb.ToString();
        }

        private static void AppendSettler(StringBuilder sb, ReportSettler s)
        {
            sb.Append($"  {Line(s)}\n");
            if (s.Recent != null && s.Recent.Count > 0) sb.Append($"    recently: {string.Join("; ", s.Recent)}\n");
            if (!string.IsNullOrEmpty(s.Troubles)) sb.Append($"    troubles: {s.Troubles}\n");
        }

        private static string Line(ReportSettler s)
        {
            string job = s.Job.Length == 0 ? "jobless" : s.Workplace.Length == 0 ? s.Job : $"{s.Job} at {s.Workplace}";
            string status = s.Status.Length == 0 ? "-" : s.Status;
            string carry = s.Carry.Length == 0 ? "nothing" : s.Carry;
            return $"{s.Name}, {job}: {status}, at {P(s.Position)}, carrying {carry}";
        }

        private static string Items(IReadOnlyDictionary<string, int> items) =>
            items.Count == 0 ? "empty" : string.Join(", ", items.OrderByDescending(i => i.Value).ThenBy(i => i.Key, System.StringComparer.Ordinal).Select(i => $"{i.Key} {i.Value}"));

        private static string P(Vec3 v) =>
            string.Format(CultureInfo.InvariantCulture, "({0:0.0}, {1:0.0}, {2:0.0})", v.X, v.Y, v.Z);
    }
}
