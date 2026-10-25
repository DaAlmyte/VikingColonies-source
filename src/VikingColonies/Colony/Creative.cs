using VikingColonies.Buildings;
using VikingColonies.Config;
using VikingColonies.Core;

namespace VikingColonies.Colony
{
    /// <summary>
    /// Creative mode's view of the game (1.5): the world's No build cost, a player's free building (debug mode's B), who is
    /// an admin. The rules themselves are Core's <see cref="CreativeRules"/>.
    /// </summary>
    internal static class Creative
    {
        /// <summary>Test worlds only (vc_creativetest): the owner treats every sender as not an admin.</summary>
        internal static bool TestNonAdmin;

        private static CreativeSetting Setting => ColonyConfig.CreativeMode != null ? ColonyConfig.CreativeMode.Value : CreativeSetting.Auto;

        /// <summary>The world's own No build cost setting (a world modifier, or the Hammer preset).</summary>
        public static bool WorldNoBuildCost() => ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoBuildCost);

        /// <summary>This computer's player may use creative here: the world's setting, or their own free building.</summary>
        public static bool LocalIsCreative() =>
            CreativeRules.IsCreative(Setting, WorldNoBuildCost(), Player.m_localPlayer != null && Player.m_localPlayer.NoCostCheat());

        public static bool BuildersFree() => CreativeRules.BuildersFree(Setting, WorldNoBuildCost());

        /// <summary>
        /// The sender is an admin: this computer's own player when it is an admin or the host (single player too); another
        /// computer only as the server sees it (a client knows no other client's standing, so it refuses).
        /// </summary>
        public static bool SenderIsAdmin(long sender) => SenderIsAdmin(sender, out _);

        /// <param name="knowable">This computer can know the sender's standing (its own player, the server, or one from the server).</param>
        private static bool SenderIsAdmin(long sender, out bool knowable)
        {
            knowable = true;
            if (TestNonAdmin || ZNet.instance == null) return false;
            bool isServer = ZNet.instance.IsServer();
            bool fromThis = sender == ZNet.GetUID();
            bool fromServer = !isServer && ZNet.instance.GetServerPeer() != null && ZNet.instance.GetServerPeer().m_uid == sender;
            knowable = fromThis || isServer || fromServer;
            bool serverSaysAdmin = false;
            if (isServer && !fromThis)
            {
                string host = ZNet.instance.GetPeer(sender)?.m_socket?.GetHostName();
                serverSaysAdmin = !string.IsNullOrEmpty(host)
                    && (ZNet.instance.IsAdmin(host) || (host.IndexOf('_') < 0 && ZNet.instance.IsAdmin("Steam_" + host)));
            }
            return CreativeRules.SenderIsAdmin(fromThis, isServer, fromThis && ZNet.instance.LocalPlayerIsAdminOrHost(), fromServer, serverSaysAdmin);
        }

        /// <summary>The owner's check on a creative request (Core's rule on the game's facts).</summary>
        public static bool OwnerAccepts(ColonyBuilding building, long sender) => Refusal(building, sender) == CreativeRefusal.None;

        /// <summary>The owner's check, with why it refuses.</summary>
        public static CreativeRefusal Refusal(ColonyBuilding building, long sender)
        {
            if (building == null) return CreativeRefusal.NotManager;
            bool admin = SenderIsAdmin(sender, out bool knowable);
            return CreativeRules.Refusal(Setting, WorldNoBuildCost(), admin, knowable, ColonyPermissions.SenderMayManage(building, sender));
        }

        /// <summary>What the sender reads for a refusal.</summary>
        public static string RefusalWords(CreativeRefusal refusal) =>
            refusal == CreativeRefusal.AdminUnknown ? "$vc_creative_admin_unknown" : "$vc_creative_refused";

        /// <summary>Supply what's needed: only where materials still count (not where builders build free).</summary>
        public static bool SupplyNeeded() => CreativeRules.SupplyNeeded(Setting, WorldNoBuildCost());

        /// <summary>Whether this colony's settlers eat (the server's setting, and its creative "meals off" while the world allows).</summary>
        public static bool SettlersEat(string colonyId)
        {
            // Read often (a settler's work speed, hovers): outside a No build cost world it is the server's setting alone.
            if (!ColonyConfig.SettlersEat.Value || !BuildersFree()) return ColonyConfig.SettlersEat.Value;
            bool eatOff = false;
            if (!string.IsNullOrEmpty(colonyId))
            {
                ColonyBuilding hall = ColonyBuilding.FindTownHall(colonyId);
                ZNetView nview = hall != null ? hall.GetComponent<ZNetView>() : null;
                eatOff = nview != null && nview.IsValid() && nview.GetZDO().GetInt(SaveKeys.ColonyEatOff, 0) == 1;
            }
            return CreativeRules.SettlersEat(ColonyConfig.SettlersEat.Value, Setting, WorldNoBuildCost(), eatOff);
        }
    }
}
