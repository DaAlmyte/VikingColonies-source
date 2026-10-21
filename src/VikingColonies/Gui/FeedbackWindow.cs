using UnityEngine;
using UnityEngine.UI;
using VikingColonies.Core;

namespace VikingColonies.Gui
{
    /// <summary>
    /// "What do you think?" (1.4.2, the feedback design of 2026-10-20): the ask that comes once per computer, and the Town
    /// Hall's Feedback button. Its two buttons open a page in the browser, the Discord server or the issues page's feedback
    /// form with the versions filled in. Nothing is sent from the game. The ask's Close button says "Not now".
    /// </summary>
    internal sealed class FeedbackWindow : WindowBase
    {
        private static readonly ReportCooldown Cooldown = new ReportCooldown();
        private Button _discord;
        private Button _github;
        private Text _hint;
        private bool _asking;

        protected override string Title => "$vc_feedback_title";
        protected override float Width => 560f;
        protected override float Height => 300f;

        /// <summary>A page was opened in the last 10 seconds: both buttons are greyed (the same spam guard as Report a problem).</summary>
        internal static bool Busy => Cooldown.Busy(Time.realtimeSinceStartup);

        /// <summary>Open now as the ask (with "Not now"), rather than from the Town Hall's button.</summary>
        internal bool Asking => IsOpen && _asking;

        /// <summary>Opens on the local player: it stays open wherever they stand, until closed.</summary>
        public void Show(bool asking)
        {
            Player player = Player.m_localPlayer;
            if (player == null) return;
            _asking = asking;
            OpenOn(player);
        }

        protected override void Build(Transform root)
        {
            UiKit.Label(root, L("$vc_feedback_text"), -64f, Width - 80f, 84f, 18, false, TextAnchor.UpperCenter);
            _discord = UiKit.Button(root, L("$vc_feedback_discord"), -128f, 125f, 230f, 40f, () => Press(true, false));
            _github = UiKit.Button(root, L("$vc_feedback_github"), 128f, 125f, 230f, 40f, () => Press(false, false));
            _hint = UiKit.Fitted(UiKit.Label(root, "", -(Height - 85f), Width - 60f, 30f, 15, false, TextAnchor.MiddleCenter));
        }

        protected override void Opened()
        {
            CloseLabel(_asking ? "$vc_feedback_not_now" : "$vc_close");
            _hint.text = L("$vc_feedback_hint");
        }

        protected override void Refresh()
        {
            bool busy = Busy;
            _discord.interactable = !busy;
            _github.interactable = !busy;
        }

        /// <summary>
        /// A button: opens the page, closes the window and says where to write. <paramref name="test"/> (vc_ui, test worlds
        /// only): the address goes to the log and the browser is left alone. Within 10 seconds of the last page it opens
        /// nothing and says when the next may.
        /// </summary>
        internal void Press(bool discord, bool test)
        {
            double now = Time.realtimeSinceStartup;
            string told;
            if (!Cooldown.TryStart(now))
            {
                told = string.Format(L("$vc_feedback_wait"), Cooldown.SecondsLeft(now));
                Log.Event($"Feedback: {(discord ? "Discord" : "GitHub")} refused, a page was just opened ({Cooldown.SecondsLeft(now)} s to wait)");
            }
            else
            {
                string address = discord ? FeedbackLinks.Discord : FeedbackLinks.GitHub(Plugin.PluginVersion, global::Version.GetVersionString());
                told = L(discord ? "$vc_feedback_discord_opened" : "$vc_feedback_github_opened");
                if (test && ZNet.instance != null && TestWorlds.IsTestWorld(ZNet.instance.GetWorldName()))
                    Log.Event($"Feedback (test): {(discord ? "Discord" : "GitHub")} would open {address}");
                else
                {
                    Application.OpenURL(address);
                    Log.Event($"Feedback: {(discord ? "Discord" : "GitHub")} opened{(_asking ? " from the ask" : "")}");
                }
                FeedbackAsker.Answered();   // whether from the ask or the button: the ask won't come after this
                Close();
            }
            if (Player.m_localPlayer != null) Player.m_localPlayer.Message(MessageHud.MessageType.Center, told);
        }
    }
}
