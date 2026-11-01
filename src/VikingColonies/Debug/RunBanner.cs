using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VikingColonies.Core;

namespace VikingColonies.DevTools
{
    /// <summary>
    /// The test-run banner (the player, 2026-10-25): while scripts/ingame.py drives the game, a small box at the top of the
    /// screen says a test is running, which one, its step, its checks so far, for how long and who holds the game. DevRunner
    /// adds and enables it only while the runner's status is fresh (Developer › CommandFile on), so a player's game never
    /// calls this OnGUI at all. vc_shot hides it for its picture.
    /// </summary>
    internal sealed class RunBanner : MonoBehaviour
    {
        private static int s_hideFrame = -1;
        private static float s_hideUntil = -1f;

        private List<BannerLine> _lines = new List<BannerLine>();
        private GUIContent[] _contents = new GUIContent[0];
        private Vector2[] _sizes = new Vector2[0];
        private bool _measured;
        private GUIStyle _heading, _line, _warn;
        private Texture2D _back;
        private int _fontFor;

        /// <summary>
        /// Leaves the banner out of a picture: ScreenCapture takes in what OnGUI draws at the end of the frame, so it hides
        /// from this frame for a few more, and for a second (a picture saved a frame late is still clean).
        /// </summary>
        internal static void HideForPicture()
        {
            s_hideFrame = Time.frameCount + 5;
            s_hideUntil = Time.unscaledTime + 1f;
        }

        /// <summary>'vc_shot banner': in this picture, even straight after one that hid it.</summary>
        internal static void ShowForPicture()
        {
            s_hideFrame = -1;
            s_hideUntil = -1f;
        }

        private void Awake() => useGUILayout = false;   // no layout pass: it draws with fixed rectangles only

        /// <summary>New words, once a second; measured again only when they changed.</summary>
        internal void Show(List<BannerLine> lines)
        {
            lines = lines ?? new List<BannerLine>();
            if (lines.Count == _lines.Count && lines.Select(l => l.Text + l.Warn).SequenceEqual(_lines.Select(l => l.Text + l.Warn))) return;
            _lines = lines;
            _contents = lines.Select(l => new GUIContent(l.Text)).ToArray();
            _measured = false;
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || _lines.Count == 0) return;
            if (Time.frameCount <= s_hideFrame || Time.unscaledTime < s_hideUntil) return;
            int font = Mathf.Max(12, Mathf.RoundToInt(16f * Screen.height / 1080f));
            if (_heading == null || _fontFor != font)
            {
                MakeStyles(font);
                _measured = false;
            }
            if (!_measured)
            {
                _sizes = new Vector2[_lines.Count];
                for (int i = 0; i < _lines.Count; i++) _sizes[i] = StyleOf(i).CalcSize(_contents[i]);
                _measured = true;
            }
            float pad = font * 0.6f, width = 0f, height = 0f;
            for (int i = 0; i < _sizes.Length; i++)
            {
                width = Mathf.Max(width, _sizes[i].x);
                height += _sizes[i].y;
            }
            width = Mathf.Min(width, Screen.width - 4f * pad);
            // Below the hotbar (the first check's box hid its eighth slot), above the game's own messages mid-screen.
            var box = new Rect((Screen.width - width) / 2f - pad, Screen.height * 0.12f, width + 2f * pad, height + 2f * pad);
            GUI.DrawTexture(box, _back);
            float y = box.y + pad;
            for (int i = 0; i < _lines.Count; i++)
            {
                GUI.Label(new Rect(box.x + pad, y, width, _sizes[i].y), _contents[i], StyleOf(i));
                y += _sizes[i].y;
            }
        }

        private GUIStyle StyleOf(int i) => i == 0 ? _heading : _lines[i].Warn ? _warn : _line;

        private void MakeStyles(int font)
        {
            _fontFor = font;
            if (_back == null)
            {
                _back = new Texture2D(1, 1);
                _back.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.7f));
                _back.Apply();
            }
            _line = new GUIStyle(GUI.skin.label) { fontSize = font, richText = false, wordWrap = false, clipping = TextClipping.Clip };
            _line.normal.textColor = Color.white;
            _heading = new GUIStyle(_line) { fontStyle = FontStyle.Bold };
            _heading.normal.textColor = new Color(1f, 0.85f, 0.3f);
            _warn = new GUIStyle(_line);
            _warn.normal.textColor = new Color(1f, 0.6f, 0.25f);
        }

        private void OnDestroy()
        {
            if (_back != null) Destroy(_back);
        }
    }
}
