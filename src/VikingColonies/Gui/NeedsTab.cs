using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using VikingColonies.Buildings;
using VikingColonies.Core;

namespace VikingColonies.Gui
{
    /// <summary>
    /// The storehouse's Needs tab (1.5, part 3; the spec: the colony's shopping list): one line per building short of
    /// something, "Farm: 20 Wood — Ulf is bringing it", nine a page, then "Bring: 40 Iron, 10 Bronze" for what nobody
    /// in the colony has.
    /// </summary>
    internal sealed class NeedsTab
    {
        private const int RowCount = 9;
        private const float RowsTop = -140f, RowStep = 30f;
        /// <summary>The report is worked out at most this often (it looks in every colony chest).</summary>
        private const float ReportSeconds = 2f;
        private const int BringShown = 6;

        private readonly Func<ColonyBuilding> _building;
        private readonly List<GameObject> _controls = new List<GameObject>();
        private readonly Text[] _labels = new Text[RowCount];
        private Text _header, _bring, _page;
        private Button _prev, _next;
        private int _pageIndex;
        private NeedsReport _report;
        private ColonyBuilding _reportFor;
        private float _reportAt = float.NegativeInfinity;

        public NeedsTab(Func<ColonyBuilding> building)
        {
            _building = building;
        }

        public IReadOnlyList<GameObject> Controls => _controls;

        public void Build(Transform root, float width, float height)
        {
            _controls.Clear();   // built again after a scene change
            _header = UiKit.Fitted(UiKit.Label(root, "", -105f, width - 60f, 28f, 18));
            _controls.Add(_header.gameObject);
            for (int i = 0; i < RowCount; i++)
            {
                _labels[i] = UiKit.Fitted(UiKit.Label(root, "", RowsTop - RowStep * i, width - 60f, 28f, 15));
                _controls.Add(_labels[i].gameObject);
            }
            _bring = UiKit.Fitted(UiKit.Label(root, "", RowsTop - RowStep * RowCount - 12f, width - 60f, 48f, 16));
            _prev = UiKit.Button(root, L("$vc_prev"), -140f, 70f, 110f, 32f, () => Turn(-1));
            _next = UiKit.Button(root, L("$vc_next"), 140f, 70f, 110f, 32f, () => Turn(1));
            _page = UiKit.Label(root, "", -(height - 86f), 120f, 30f, 16, align: TextAnchor.MiddleCenter);
            _controls.AddRange(new[] { _bring.gameObject, _prev.gameObject, _next.gameObject, _page.gameObject });
        }

        /// <summary>Each opening starts on the first page, with the report worked out afresh.</summary>
        public void Reset()
        {
            _pageIndex = 0;
            _report = null;
        }

        public void Refresh()
        {
            ColonyBuilding building = _building();
            CourierDispatch dispatch = building != null ? building.GetComponent<CourierDispatch>() : null;
            if (dispatch == null) return;
            if (_report == null || _reportFor != building || Time.time - _reportAt >= ReportSeconds)
            {
                _report = dispatch.Needs();
                _reportFor = building;
                _reportAt = Time.time;
            }
            WorkerModule worker = building.GetComponent<WorkerModule>();
            bool couriers = worker != null && worker.Workers(out List<Citizens.Citizen> loaded, out List<RosterEntry> elsewhere) && loaded.Count + elsewhere.Count > 0;
            _header.text = couriers ? L("$vc_needs_header") : $"<color=orange>{L("$vc_needs_no_courier")}</color>";
            List<NeedLine> lines = _report.Lines;
            int pages = ListPage.Pages(lines.Count, RowCount);
            _pageIndex = ListPage.Clamp(_pageIndex, lines.Count, RowCount);
            IReadOnlyList<NeedLine> shown = ListPage.Slice(lines, _pageIndex, RowCount);
            for (int i = 0; i < RowCount; i++)
            {
                bool on = i < shown.Count;
                _labels[i].gameObject.SetActive(on || (i == 0 && lines.Count == 0));
                if (on) _labels[i].text = Line(shown[i], dispatch);
                else if (i == 0 && lines.Count == 0) _labels[0].text = L("$vc_needs_none");
            }
            _bring.text = Bring(_report.Bring);
            bool paged = pages > 1;
            _prev.gameObject.SetActive(paged);
            _next.gameObject.SetActive(paged);
            _prev.interactable = _pageIndex > 0;
            _next.interactable = _pageIndex < pages - 1;
            _page.text = paged ? $"{_pageIndex + 1} / {pages}" : "";
        }

        /// <summary>"Farm: 20 Wood — Ulf is bringing it", or its parts: "Ulf is bringing 10; 30 nobody has".</summary>
        internal static string Line(NeedLine line, CourierDispatch dispatch)
        {
            ColonyBuilding site = ColonyBuilding.FindById(line.Site);
            string building = site != null && site.Type != null ? L(site.Type.NameToken) : "?";
            return string.Format(L("$vc_needs_line"), building, line.Amount, StockTab.EntryName(line.Item), State(line, dispatch));
        }

        private static string State(NeedLine line, CourierDispatch dispatch)
        {
            var parts = new List<string>();
            bool Whole(int n) => n >= line.Amount;
            if (line.Coming > 0)
            {
                string who = dispatch != null ? dispatch.CourierName(line.Courier) : "";
                if (who.Length == 0) who = L("$vc_needs_a_courier");
                parts.Add(Whole(line.Coming) ? string.Format(L("$vc_needs_coming_all"), who) : string.Format(L("$vc_needs_coming"), who, line.Coming));
            }
            if (line.NoRoom) parts.Add(L("$vc_needs_no_room"));
            if (line.Waiting > 0)
                parts.Add(Whole(line.Waiting) ? L("$vc_needs_waiting_all") : string.Format(L("$vc_needs_waiting"), line.Waiting));
            if (line.Elsewhere > 0)
            {
                ColonyBuilding at = ColonyBuilding.FindById(line.ElsewhereAt);
                string where = at != null && at.Type != null ? L(at.Type.NameToken) : "?";
                parts.Add(Whole(line.Elsewhere) ? string.Format(L("$vc_needs_elsewhere_all"), where) : string.Format(L("$vc_needs_elsewhere"), line.Elsewhere, where));
            }
            if (line.Missing > 0)
                parts.Add(Whole(line.Missing) ? $"<color=orange>{L("$vc_needs_missing_all")}</color>"
                    : $"<color=orange>{string.Format(L("$vc_needs_missing"), line.Missing)}</color>");
            return string.Join("; ", parts.ToArray());
        }

        /// <summary>"Bring: 40 Iron, 10 Food (any)", or that there's nothing to bring.</summary>
        internal static string Bring(IReadOnlyList<KeepEntry> bring)
        {
            if (bring == null || bring.Count == 0) return L("$vc_needs_bring_none");
            string items = string.Join(", ", bring.Take(BringShown).Select(b => $"{b.Amount} {StockTab.EntryName(b.Item)}").ToArray());
            return string.Format(L("$vc_needs_bring"), items + (bring.Count > BringShown ? "…" : ""));
        }

        private void Turn(int by)
        {
            _pageIndex += by;
            Refresh();
        }

        private static string L(string token) => Localization.instance.Localize(token);
    }
}
