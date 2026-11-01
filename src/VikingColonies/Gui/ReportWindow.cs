using UnityEngine;
using UnityEngine.UI;

namespace VikingColonies.Gui
{
    /// <summary>
    /// Report a problem's window (1.6, from the Town Hall's button): opening it writes the bug report file; its buttons
    /// open the GitHub bug form or the Discord server with the report copied, or the file's folder; it says where the
    /// file is. Nothing is sent from the game. It stays open after a button, so the file's place stays readable.
    /// See docs/superpowers/specs/2026-10-25-report-problem-discord-design.md.
    /// </summary>
    internal sealed class ReportWindow : WindowBase
    {
        private const string OpenedFrom = "the Chieftain's Camp window";
        private Button _github;
        private Button _discord;
        private Button _folder;
        private Text _file;

        protected override string Title => "$vc_report_problem";
        protected override float Width => 600f;
        protected override float Height => 400f;

        /// <summary>Opens on the local player: it stays open wherever they stand, until closed.</summary>
        public void Show()
        {
            Player player = Player.m_localPlayer;
            if (player == null) return;
            OpenOn(player);
        }

        protected override void Build(Transform root)
        {
            UiKit.Label(root, L("$vc_report_text"), -64f, Width - 80f, 56f, 18, false, TextAnchor.UpperCenter);
            _github = UiKit.Button(root, L("$vc_report_github"), -128f, 255f, 230f, 40f, () => Press(ReportProblem.Way.Form, false));
            _discord = UiKit.Button(root, L("$vc_report_discord"), 128f, 255f, 230f, 40f, () => Press(ReportProblem.Way.Discord, false));
            _file = UiKit.Label(root, "", -170f, Width - 60f, 90f, 15, false, TextAnchor.UpperCenter);
            _folder = UiKit.Button(root, L("$vc_report_folder"), 0f, 110f, 230f, 36f, () => Press(ReportProblem.Way.Folder, false));
            UiKit.Fitted(UiKit.Label(root, L("$vc_report_hint"), -(Height - 90f), Width - 60f, 26f, 15, false, TextAnchor.MiddleCenter));
        }

        protected override void Opened()
        {
            string error = ReportProblem.Prepare(OpenedFrom).FileError;
            _file.text = error == null
                ? string.Format(L("$vc_report_file"), ReportProblem.ShownPath)
                : string.Format(L("$vc_report_file_failed"), ReportProblem.Shown(error));
        }

        protected override void Refresh()
        {
            bool busy = ReportProblem.Busy;
            _github.interactable = !busy;
            _discord.interactable = !busy;
            _folder.interactable = !ReportProblem.FolderBusy;
        }

        /// <summary>A button; <paramref name="test"/> from vc_ui in a test world (the address to the log; no browser, folder or clipboard).</summary>
        internal void Press(ReportProblem.Way way, bool test)
        {
            string told = ReportProblem.Press(way, OpenedFrom, test);
            Refresh();
            if (Player.m_localPlayer != null) Player.m_localPlayer.Message(MessageHud.MessageType.Center, told);
        }
    }
}
