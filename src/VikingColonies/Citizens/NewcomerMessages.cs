using HarmonyLib;
using UnityEngine;
using VikingColonies.Config;
using VikingColonies.Core;

namespace VikingColonies.Citizens
{
    /// <summary>
    /// "Astrid has come to live in the colony": sent by the Town Hall's computer to every player; each player's computer
    /// shows it when its player is in the colony's area.
    /// </summary>
    [HarmonyPatch]
    internal static class NewcomerMessages
    {
        private const string RpcName = "vc_Newcomer";

        /// <summary>The last message this computer showed (the self-test reads it).</summary>
        public static string LastShown { get; private set; } = "";

        [HarmonyPatch(typeof(Game), "Start"), HarmonyPostfix]
        private static void RegisterRpc() => ZRoutedRpc.instance?.Register<string, Vector3>(RpcName, OnNewcomer);

        private const string NoBody = "#nobody";

        /// <summary>"Settlers can't come: the settler body didn't load."</summary>
        public static void SendNoBody(Vector3 hall) => Send(NoBody, hall);

        public static void Send(string name, Vector3 hall) =>
            ZRoutedRpc.instance?.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcName, name ?? "", hall);   // here at once, then the others

        private static void OnNewcomer(long sender, string name, Vector3 hall)
        {
            Player player = Player.m_localPlayer;
            if (player == null) return;   // a dedicated server has nobody to tell
            var at = new GroundPoint(hall.x, hall.z);
            if (!DeathNotice.ShouldTell(new GroundPoint(player.transform.position.x, player.transform.position.z), at, true, at,
                    Buildings.HallRules.RadiusAt(hall))) return;
            // Since 0.16: "name|calling token|origin token" — "Astrid is coming to live in the colony: a forager from the coast."
            string[] parts = (name ?? "").Split('|');
            string text = name == NoBody ? Localization.instance.Localize("$vc_no_settler_body")
                : parts.Length >= 3
                ? string.Format(Localization.instance.Localize("$vc_newcomer_coming"), parts[0],
                    Localization.instance.Localize(parts[1]), Localization.instance.Localize(parts[2]))
                : string.Format(Localization.instance.Localize("$vc_newcomer_arrived"), parts[0]);
            LastShown = text;
            player.Message(MessageHud.MessageType.Center, text);
            Chat.instance?.AddString(text);
        }
    }
}
