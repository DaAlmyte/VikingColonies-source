using System;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace VikingColonies.Gui
{
    /// <summary>Tiny wrapper over Jötunn's GUI helpers so every window looks the same.</summary>
    internal static class UiKit
    {
        public static bool Available => GUIManager.Instance != null && GUIManager.CustomGUIFront != null;

        public static GameObject Panel(string name, float width, float height)
        {
            GameObject panel = GUIManager.Instance.CreateWoodpanel(GUIManager.CustomGUIFront.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, width, height, true);
            panel.name = name;
            panel.SetActive(false);
            return panel;
        }

        /// <summary>Text anchored to the panel's top centre; <paramref name="y"/> goes down from the top (negative).</summary>
        public static Text Label(Transform parent, string text, float y, float width, float height, int size = 18,
            bool bold = false, TextAnchor align = TextAnchor.UpperLeft)
        {
            GameObject go = GUIManager.Instance.CreateText(text, parent, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, y), bold ? GUIManager.Instance.AveriaSerifBold : GUIManager.Instance.AveriaSerif, size,
                bold ? GUIManager.Instance.ValheimOrange : Color.white, true, Color.black, width, height, false);
            // Pin the label by its top edge, so y is where the text starts and it grows downwards.
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, y);
            Text label = go.GetComponent<Text>();
            label.alignment = align;
            label.supportRichText = true;
            return label;
        }

        /// <summary>
        /// A one-line message that shrinks to fit rather than lose its end (the whole-mod review of 1.4.1: a long answer showed
        /// only its first line, "A builder will take it down; its chest's contents go").
        /// </summary>
        public static Text Fitted(Text label)
        {
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 10;
            label.resizeTextMaxSize = label.fontSize;
            return label;
        }

        /// <summary>Button anchored to the panel's bottom centre; <paramref name="x"/> offsets from the centre.</summary>
        public static Button Button(Transform parent, string label, float x, float y, float width, float height, Action onClick)
        {
            GameObject go = GUIManager.Instance.CreateButton(label, parent, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(x, y), width, height);
            Button button = go.GetComponent<Button>();
            button.onClick.AddListener(() => onClick());
            return button;
        }

        /// <summary>Button anchored to the panel's top centre (tabs, rows); <paramref name="y"/> goes down from the top (negative).</summary>
        public static Button TopButton(Transform parent, string label, float x, float y, float width, float height, Action onClick)
        {
            Button button = Button(parent, label, 0f, 0f, width, height, onClick);
            RectTransform rect = button.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
            return button;
        }

        public static void SetLabel(Button button, string text) => button.GetComponentInChildren<Text>().text = text;

        /// <summary>A tick box anchored to the panel's top-left; <paramref name="y"/> goes down from the top (negative).</summary>
        public static Toggle Toggle(Transform parent, float x, float y, Action<bool> onChange)
        {
            GameObject go = GUIManager.Instance.CreateToggle(parent, 28f, 28f);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
            Toggle toggle = go.GetComponent<Toggle>();
            toggle.onValueChanged.AddListener(v => onChange(v));
            return toggle;
        }
    }
}
