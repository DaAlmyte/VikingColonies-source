using System;
using System.Collections.Generic;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VikingColonies.Gui
{
    /// <summary>
    /// Creative mode's buttons (1.5): a narrow panel docked to a colony window's right edge, shown only in creative to a
    /// player who may manage the colony. A line at its foot says what the button under the pointer does.
    /// </summary>
    internal sealed class CreativeBar
    {
        private const float BarWidth = 210f;
        private const float ButtonHeight = 34f;
        private const float Gap = 6f;
        private const float Top = 52f;

        private readonly GameObject _panel;
        private readonly Text _hover;
        private readonly List<Entry> _entries = new List<Entry>();

        private sealed class Entry
        {
            public Button Button;
            public Func<bool> ShownIf;
        }

        private CreativeBar(Transform root, float height)
        {
            _panel = GUIManager.Instance.CreateWoodpanel(root, new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.zero, BarWidth, height, false);
            _panel.name = "CreativeBar";
            RectTransform rect = _panel.GetComponent<RectTransform>();
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(8f, 0f);
            UiKit.Label(_panel.transform, Localization.instance.Localize("$vc_creative_tag"), -16f, BarWidth - 20f, 30f, 20, true, TextAnchor.MiddleCenter);
            _hover = UiKit.Fitted(UiKit.Label(_panel.transform, "", -(height - 120f), BarWidth - 24f, 100f, 15, false, TextAnchor.UpperLeft));
            _panel.SetActive(false);
        }

        /// <summary>A bar for the window whose panel is <paramref name="root"/>, as tall as it.</summary>
        public static CreativeBar Attach(Transform root, float windowHeight) => new CreativeBar(root, windowHeight);

        /// <summary>A button, shown while <paramref name="shownIf"/> holds; its hover line is <paramref name="hoverToken"/>.</summary>
        public Button Add(string labelToken, string hoverToken, Action onClick, Func<bool> shownIf)
        {
            Button button = UiKit.TopButton(_panel.transform, Localization.instance.Localize(labelToken), 0f, -Top, BarWidth - 30f, ButtonHeight, onClick);
            var trigger = button.gameObject.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => _hover.text = Localization.instance.Localize(hoverToken));
            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => _hover.text = "");
            trigger.triggers.Add(enter);
            trigger.triggers.Add(exit);
            _entries.Add(new Entry { Button = button, ShownIf = shownIf });
            return button;
        }

        /// <summary>Shown or hidden as a whole; the buttons that apply stacked from the top.</summary>
        public void Refresh(bool visible)
        {
            if (_panel == null) return;
            if (_panel.activeSelf != visible) _panel.SetActive(visible);
            if (!visible) return;
            int row = 0;
            foreach (Entry entry in _entries)
            {
                bool shown;
                try
                {
                    shown = entry.ShownIf();
                }
                catch (Exception)
                {
                    shown = false;   // a building gone between refreshes
                }
                if (entry.Button.gameObject.activeSelf != shown) entry.Button.gameObject.SetActive(shown);
                if (!shown) continue;
                entry.Button.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -(Top + row * (ButtonHeight + Gap)));
                row++;
            }
        }
    }
}
