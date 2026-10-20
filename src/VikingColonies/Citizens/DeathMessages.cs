using HarmonyLib;
using UnityEngine;
using VikingColonies.Config;
using VikingColonies.Core;

namespace VikingColonies.Citizens
{
    /// <summary>
    /// "Ingrid the Lumberjack has died (hit by a falling tree)": sent once by the computer running the settler to every
    /// player; each player's computer shows it when its player is in the colony's area or near where it happened.
    /// </summary>
    [HarmonyPatch]
    internal static class DeathMessages
    {
        private const string RpcName = "vc_SettlerDied";
        private static readonly AccessTools.FieldRef<Chat, float> s_chatHideTimer = AccessTools.FieldRefAccess<Chat, float>("m_hideTimer");

        /// <summary>The last message this computer showed (the self-test reads it).</summary>
        public static string LastShown { get; private set; } = "";

        /// <summary>A world was joined: its network is new, so the message is registered again.</summary>
        [HarmonyPatch(typeof(Game), "Start"), HarmonyPostfix]
        private static void RegisterRpc() => ZRoutedRpc.instance?.Register<ZPackage>(RpcName, OnSettlerDied);

        /// <param name="cairn">He left a cairn where he fell: told players get a map pin to it.</param>
        public static void Send(string name, string jobId, DeathCause cause, Vector3 at, float[] hallPos, bool cairn, string citizenId)
        {
            if (ZRoutedRpc.instance == null) return;
            bool hallKnown = hallPos != null && hallPos.Length == 3;
            var pkg = new ZPackage();
            pkg.Write(name ?? "");
            pkg.Write(jobId ?? "");
            pkg.Write(cause.Token);
            pkg.Write(cause.Arg);
            pkg.Write(at);
            pkg.Write(hallKnown);
            pkg.Write(hallKnown ? Citizen.ToVector3(hallPos) : Vector3.zero);
            pkg.Write(cairn);
            pkg.Write(citizenId ?? "");   // every computer remembers who died (Bring back never revives the dead)
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcName, pkg);   // runs here at once, then on the others
        }

        private static void OnSettlerDied(long sender, ZPackage pkg)
        {
            string name = pkg.ReadString();
            string jobId = pkg.ReadString();
            string token = pkg.ReadString();
            string arg = pkg.ReadString();
            Vector3 at = pkg.ReadVector3();
            bool hallKnown = pkg.ReadBool();
            Vector3 hall = pkg.ReadVector3();
            bool cairn = pkg.GetPos() < pkg.Size() && pkg.ReadBool();   // a message from a version without cairns ends earlier
            string citizenId = pkg.GetPos() < pkg.Size() ? pkg.ReadString() : "";   // and before 0.10, without his id
            ColonyDeaths.Remember(citizenId);   // on every computer, a dedicated server's too
            Player player = Player.m_localPlayer;
            if (player == null) return;   // a dedicated server has nobody to tell
            if (!DeathNotice.ShouldTell(G(player.transform.position), G(at), hallKnown, G(hall), Buildings.HallRules.RadiusAt(hall))) return;
            string text = string.Format(Localization.instance.Localize("$vc_settler_died"),
                Citizen.DisplayNameFor(name, jobId), CauseText(new DeathCause(token, arg)));
            LastShown = text;
            Gui.HuginTips.Show(Gui.HuginTips.Death);
            if (cairn) SettlerCairn.PinOnMap(at, name);
            if (DeathNotice.ShowsCentre(ColonyConfig.DeathMessage.Value)) player.Message(MessageHud.MessageType.Center, text);
            Chat chat = Chat.instance;
            if (chat == null) return;
            chat.AddString(text);
            s_chatHideTimer(chat) = 0f;   // the chat window shows for a while, as when someone speaks
        }

        /// <summary>The cause in this player's words: "hit by a falling tree", "killed by Greydwarf".</summary>
        public static string CauseText(DeathCause cause) =>
            SafeText.Format(Localization.instance.Localize(cause.Token), Localization.instance.Localize(cause.Arg));   // sent by another computer

        private static GroundPoint G(Vector3 v) => new GroundPoint(v.x, v.z);
    }
}
