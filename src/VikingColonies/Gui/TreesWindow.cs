using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using VikingColonies.Buildings;
using VikingColonies.Core;
using VikingColonies.Jobs;

namespace VikingColonies.Gui
{
    /// <summary>
    /// The Lumberjack's Hut's Trees list (worker polish part 3, 0.22; MineColonies' Saplings tab; it replaces the
    /// replanting list): a line per tree kind with two ticks, Fell and Replant, greyed with the reason when the hut can't
    /// have it; in pages with Previous / Next.
    /// </summary>
    internal sealed class TreesWindow : WindowBase
    {
        private const int PerPage = 12;
        private const float HintHeight = 62f,   // three lines (the 1.0 play-through: the last was cut off)
                              LineHeight = 34f, FellX = 30f, ReplantX = 78f;

        private Toggle[] _fell, _replant;
        private Text[] _labels;
        private readonly string[] _kinds = new string[PerPage];
        private readonly string[][] _saplings = new string[PerPage][];
        private Button _prev, _next;
        private Text _pageText;
        private int _page;

        protected override string Title => "$vc_trees_title";
        protected override float Width => 520f;
        private float ListTop => -62f - HintHeight - 34f;
        protected override float Height => -ListTop + LineHeight * PerPage + 100f;   // the page buttons and Close below

        protected override void Build(Transform root)
        {
            UiKit.Label(root, L("$vc_trees_hint"), -62f, Width - 60f, HintHeight, 15);
            // Column heads over the two ticks.
            Text fellHead = UiKit.Label(root, L("$vc_trees_fell"), -62f - HintHeight - 4f, 60f, 26f, 14, true, TextAnchor.MiddleCenter);
            Text replantHead = UiKit.Label(root, L("$vc_trees_replant"), -62f - HintHeight - 4f, 70f, 26f, 14, true, TextAnchor.MiddleCenter);
            Place(fellHead, FellX + 14f);
            Place(replantHead, ReplantX + 14f);
            _fell = new Toggle[PerPage];
            _replant = new Toggle[PerPage];
            _labels = new Text[PerPage];
            for (int i = 0; i < PerPage; i++)
            {
                int line = i;
                float y = ListTop - LineHeight * i;
                _fell[i] = UiKit.Toggle(root, FellX, y, on => OnFell(line, on));
                _replant[i] = UiKit.Toggle(root, ReplantX, y, on => OnReplant(line, on));
                _labels[i] = UiKit.Label(root, "", y - 2f, Width - 170f, LineHeight, 15);
                RectTransform rect = _labels[i].GetComponent<RectTransform>();
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x + 45f, rect.anchoredPosition.y);
            }
            _prev = UiKit.Button(root, L("$vc_prev"), -140f, 70f, 110f, 32f, () => Turn(-1));
            _next = UiKit.Button(root, L("$vc_next"), 140f, 70f, 110f, 32f, () => Turn(1));
            _pageText = UiKit.Label(root, "", -(Height - 86f), 120f, 30f, 16, align: TextAnchor.MiddleCenter);
        }

        /// <summary>A head over a tick column (labels are centred on the panel; the ticks sit from its left edge).</summary>
        private void Place(Text head, float fromLeft)
        {
            RectTransform rect = head.GetComponent<RectTransform>();
            rect.anchoredPosition = new Vector2(fromLeft - Width / 2f, rect.anchoredPosition.y);
        }

        protected override void Opened() => _page = 0;

        protected override void Refresh()
        {
            ColonyBuilding hut = Building;
            List<TreeLine> lines = hut != null ? TreeList.For(hut) : new List<TreeLine>();
            bool mayManage = hut != null && ColonyPermissions.LocalMayManage(hut);
            int pages = ListPage.Pages(lines.Count, PerPage);
            _page = ListPage.Clamp(_page, lines.Count, PerPage);
            IReadOnlyList<TreeLine> shown = ListPage.Slice(lines, _page, PerPage);
            for (int i = 0; i < PerPage; i++)
            {
                bool on = i < shown.Count;
                _fell[i].gameObject.SetActive(on);
                _replant[i].gameObject.SetActive(on);
                _labels[i].gameObject.SetActive(on);
                _kinds[i] = on ? shown[i].Kind : "";
                _saplings[i] = on ? shown[i].Saplings.Select(s => s.Sapling).ToArray() : new string[0];
                if (!on) continue;
                TreeLine line = shown[i];
                _fell[i].SetIsOnWithoutNotify(line.Fell);
                _fell[i].interactable = mayManage && line.CanFell;
                _replant[i].SetIsOnWithoutNotify(line.Replant);
                _replant[i].interactable = mayManage && line.ReplantOffered;
                _labels[i].text = Name(line) + Why(line);
            }
            bool paged = pages > 1;
            _prev.gameObject.SetActive(paged);
            _next.gameObject.SetActive(paged);
            _prev.interactable = _page > 0;
            _next.interactable = _page < pages - 1;
            _pageText.text = paged ? $"{_page + 1} / {pages}" : "";
        }

        /// <summary>"Beech", or a mod's tree by its name.</summary>
        internal static string Name(TreeLine line) => line.Token.Length > 0 ? L(line.Token) : line.Kind;

        private static string Why(TreeLine line)
        {
            string why = !line.CanFell ? string.Format(L("$vc_replant_needs_level"), line.NeedsLevel)
                : line.Saplings.Count == 0 ? L("$vc_trees_no_sapling")
                : line.ReplantNeedsLevel > 0 ? string.Format(L("$vc_trees_replant_needs_level"), line.ReplantNeedsLevel)   // 0.24
                : line.WrongBiome ? string.Format(L("$vc_replant_grows_only"), BiomeText.Names(line.Sapling.Biomes)) : "";
            return why.Length > 0 ? " — <color=orange>" + why + "</color>" : "";
        }

        private void Turn(int by)
        {
            _page += by;
            Refresh();
        }

        private void OnFell(int line, bool on)
        {
            WorkerModule worker = Building != null ? Building.GetComponent<WorkerModule>() : null;
            if (worker != null && _kinds[line].Length > 0) worker.RequestSetFell(_kinds[line], on);
        }

        private void OnReplant(int line, bool on)
        {
            WorkerModule worker = Building != null ? Building.GetComponent<WorkerModule>() : null;
            if (worker == null || _saplings[line] == null) return;
            foreach (string sapling in _saplings[line]) worker.RequestSetTicked(sapling, on);   // every sapling of the kind (review of 0.22)
        }

        /// <summary>Test tool (vc_ui): turns to a page.</summary>
        internal void TurnForTest(int by) => Turn(by);
    }
}
