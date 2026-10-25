using System.Collections;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using VikingColonies.Buildings;

namespace VikingColonies.Gui
{
    /// <summary>
    /// Base for colony windows: built once, refreshed twice a second while open, closed with the Close button,
    /// Esc (Valheim's menu opening), walking away, or the building disappearing.
    /// </summary>
    internal abstract class WindowBase
    {
        private const float RefreshSeconds = 0.5f;
        private const float MaxDistance = 8f;

        private GameObject _panel;
        private Text _title;
        private CreativeBar _creative;
        private Button _close;
        private float _refreshTimer;

        /// <summary>What the window is about: a colony building, or another thing in the world (a field marker, 0.20).</summary>
        protected MonoBehaviour Target { get; private set; }

        protected ColonyBuilding Building => Target as ColonyBuilding;
        protected abstract string Title { get; }
        protected abstract float Width { get; }
        protected abstract float Height { get; }
        /// <summary>Where the Close button sits, from the bottom row's centre (the Town Hall's row of four, 1.4.2).</summary>
        protected virtual float CloseX => 0f;

        public bool IsOpen => _panel != null && _panel.activeSelf;

        protected abstract void Build(Transform root);
        protected abstract void Refresh();

        /// <summary>Each time the window opens (for this or another building), before its first refresh.</summary>
        protected virtual void Opened() { }

        /// <summary>Creative mode (1.5): the window's creative buttons, added once when it is built.</summary>
        protected virtual void BuildCreative(CreativeBar bar) { }

        /// <summary>The creative buttons show: in creative, to a player who may manage the colony of a colony building.</summary>
        private bool CreativeShown => Building != null && Colony.Creative.LocalIsCreative() && ColonyPermissions.LocalMayManage(Building);

        private void RefreshCreative()
        {
            bool shown = CreativeShown;
            _creative?.Refresh(shown);
            if (_title != null) _title.text = L(Title) + (shown ? "  ·  " + L("$vc_creative_tag") : "");
        }

        protected static string L(string token) => Localization.instance.Localize(token);

        /// <summary>The Close button's words for this opening (the feedback ask's "Not now", 1.4.2); call from <see cref="Opened"/>.</summary>
        protected void CloseLabel(string token)
        {
            if (_close != null) UiKit.SetLabel(_close, L(token));
        }

        public void Open(ColonyBuilding building) => OpenOn(building);

        /// <summary>Opens the window on any thing in the world; it closes when that goes or the player walks away.</summary>
        public void OpenOn(MonoBehaviour target)
        {
            if (!UiKit.Available || target == null) return;
            if (_panel == null)   // also true after a scene change destroyed the old panel
            {
                _panel = UiKit.Panel(GetType().Name, Width, Height);
                _title = UiKit.Label(_panel.transform, "", -20f, Width - 40f, 34f, 24, true, TextAnchor.MiddleCenter);
                Build(_panel.transform);
                _creative = CreativeBar.Attach(_panel.transform, Height);
                BuildCreative(_creative);
                _close = UiKit.Button(_panel.transform, L("$vc_close"), CloseX, 25f, 140f, 36f, Close);
                _panel.AddComponent<WindowDriver>().Window = this;
            }
            Target = target;
            _title.text = L(Title);   // per building: one window serves every hut
            Opened();
            Refresh();
            RefreshCreative();
            if (!_panel.activeSelf)
            {
                _panel.SetActive(true);
                GUIManager.BlockInput(true);
            }
        }

        public void Close()
        {
            if (!IsOpen) return;
            _panel.SetActive(false);
            Target = null;
            // Give input back next frame, so the Esc that closed the window doesn't also open the game menu.
            Plugin.Instance.StartCoroutine(UnblockInputNextFrame());
        }

        private static IEnumerator UnblockInputNextFrame()
        {
            yield return null;
            GUIManager.BlockInput(false);
        }

        /// <summary>Esc or the gamepad's B button. Read directly: the game ignores them while our window blocks input.</summary>
        private static bool BackPressed()
        {
            Keyboard keyboard = Keyboard.current;
            Gamepad gamepad = Gamepad.current;
            return (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) ||
                   (gamepad != null && gamepad.buttonEast.wasPressedThisFrame);
        }

        internal void Tick(float dt)
        {
            if (!IsOpen) return;
            Player player = Player.m_localPlayer;
            if (Target == null || player == null || BackPressed() || Menu.IsVisible() ||
                Vector3.Distance(player.transform.position, Target.transform.position) > MaxDistance)
            {
                Close();
                return;
            }
            _refreshTimer -= dt;
            if (_refreshTimer > 0f) return;
            _refreshTimer = RefreshSeconds;
            Refresh();
            RefreshCreative();
        }

        /// <summary>Test tool (vc_ui): every line the window shows, its buttons and ticks with whether they can be used.</summary>
        internal string TextForTest()
        {
            if (!IsOpen) return "";
            var sb = new System.Text.StringBuilder();
            foreach (Text text in _panel.GetComponentsInChildren<Text>(false))
            {
                if (string.IsNullOrEmpty(text.text)) continue;
                Selectable control = text.GetComponentInParent<Selectable>();
                string kind = control is Toggle toggle ? $"[tick {(toggle.isOn ? "on" : "off")}{(toggle.interactable ? "" : ", greyed")}] "
                    : control is Button button ? $"[button{(button.interactable ? "" : ", greyed")}] " : "";
                sb.Append(kind).Append(text.text.Replace("\n", " / ")).Append(" | ");
            }
            return sb.ToString();
        }

        /// <summary>vc_selftest: the panel destroyed, as leaving a world and entering one again in the same game does.</summary>
        internal void DestroyPanelForTest()
        {
            if (_panel != null) Object.DestroyImmediate(_panel);
        }

        internal void OnPanelDestroyed()
        {
            if (Target != null) GUIManager.BlockInput(false);
            Target = null;
        }
    }

    internal sealed class WindowDriver : MonoBehaviour
    {
        public WindowBase Window;

        private void Update() => Window?.Tick(Time.deltaTime);

        private void OnDestroy() => Window?.OnPanelDestroyed();
    }
}
