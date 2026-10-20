using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VikingColonies.Core;

namespace VikingColonies.Gui
{
    /// <summary>
    /// A hut's tick list (what to grow, pick or replant): a tick per kind, in pages with Previous / Next when there are
    /// more than fit (mods add crops, plants and trees: a list that only showed its first lines left the rest out of
    /// reach; review before 1.0).
    /// </summary>
    internal abstract class TickListWindow : WindowBase
    {
        /// <summary>One line: what the tick stands for, the words beside it, whether it's ticked and may be changed.</summary>
        protected sealed class TickRow
        {
            public string Key = "";
            public string Text = "";
            public bool Ticked;
            public bool Enabled;
        }

        private const int PerPage = 12;
        private Toggle[] _ticks;
        private Text[] _labels;
        private readonly string[] _keys = new string[PerPage];
        private Button _prev, _next;
        private Text _pageText;
        private int _page;

        protected abstract string HintToken { get; }
        /// <summary>Room for the hint above the list.</summary>
        protected abstract float HintHeight { get; }
        protected abstract float LineHeight { get; }
        protected abstract List<TickRow> Rows();
        protected abstract void SetTicked(string key, bool on);

        private float ListTop => -62f - HintHeight - 6f;
        protected override float Height => -ListTop + LineHeight * PerPage + 100f;   // the page buttons and Close below

        protected override void Build(Transform root)
        {
            UiKit.Label(root, L(HintToken), -62f, Width - 60f, HintHeight, 16);
            _ticks = new Toggle[PerPage];
            _labels = new Text[PerPage];
            for (int i = 0; i < PerPage; i++)
            {
                int line = i;
                float y = ListTop - LineHeight * i;
                _ticks[i] = UiKit.Toggle(root, 30f, y, on => OnTick(line, on));
                _labels[i] = UiKit.Label(root, "", y - 2f, Width - 130f, LineHeight, 16);
            }
            _prev = UiKit.Button(root, L("$vc_prev"), -140f, 70f, 110f, 32f, () => Turn(-1));
            _next = UiKit.Button(root, L("$vc_next"), 140f, 70f, 110f, 32f, () => Turn(1));
            _pageText = UiKit.Label(root, "", -(Height - 86f), 120f, 30f, 16, align: TextAnchor.MiddleCenter);
        }

        protected override void Opened() => _page = 0;

        protected override void Refresh()
        {
            List<TickRow> rows = Target != null ? Rows() : new List<TickRow>();
            int pages = ListPage.Pages(rows.Count, PerPage);
            _page = ListPage.Clamp(_page, rows.Count, PerPage);
            IReadOnlyList<TickRow> shown = ListPage.Slice(rows, _page, PerPage);
            for (int i = 0; i < PerPage; i++)
            {
                bool on = i < shown.Count;
                _ticks[i].gameObject.SetActive(on);
                _labels[i].gameObject.SetActive(on);
                _keys[i] = on ? shown[i].Key : "";
                if (!on) continue;
                _ticks[i].interactable = shown[i].Enabled;
                _ticks[i].SetIsOnWithoutNotify(shown[i].Ticked);
                _labels[i].text = shown[i].Text;
            }
            bool paged = pages > 1;
            _prev.gameObject.SetActive(paged);
            _next.gameObject.SetActive(paged);
            _prev.interactable = _page > 0;
            _next.interactable = _page < pages - 1;
            _pageText.text = paged ? $"{_page + 1} / {pages}" : "";
        }

        private void Turn(int by)
        {
            _page += by;
            Refresh();
        }

        private void OnTick(int line, bool on)
        {
            string key = _keys[line];
            if (!string.IsNullOrEmpty(key)) SetTicked(key, on);
        }

        /// <summary>Test tool (vc_ui): turns to a page.</summary>
        internal void TurnForTest(int by) => Turn(by);
    }
}
