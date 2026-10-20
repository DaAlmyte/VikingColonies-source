using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using VikingColonies.Core;

namespace VikingColonies.Buildings
{
    /// <summary>
    /// Who may manage a colony's huts: the player who built its Town Hall, and the players they put on one of their wards
    /// covering the hut. The hut's owner computer checks every request; the windows grey out what the viewer may not do.
    /// </summary>
    internal static class ColonyPermissions
    {
        private static readonly FieldInfo s_allAreas = typeof(PrivateArea).GetField("m_allAreas", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo s_isInside = typeof(PrivateArea).GetMethod("IsInside", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo s_isPermitted = typeof(PrivateArea).GetMethod("IsPermitted", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo s_areaPiece = typeof(PrivateArea).GetField("m_piece", BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>The player who built the building's colony's Town Hall (as its huts remember while it's gone); 0 when unknown.</summary>
        public static long OwnerOf(ColonyBuilding building) => building != null ? ColonyBuilding.OwnerOfColony(building.ColonyId) : 0L;

        public static bool MayManage(ColonyBuilding building, long player)
        {
            if (building == null) return false;
            long owner = OwnerOf(building);
            bool onWard = owner != 0L && player != 0L && player != owner && OnOwnersWard(building.transform.position, owner, player);
            return ColonyManagers.MayManage(owner, player, onWard);
        }

        /// <summary>Whether the player at this computer may put a new hut for this Town Hall's colony at this spot.</summary>
        public static bool LocalMayBuildAt(ColonyBuilding hall, Vector3 at) =>
            hall == null || Player.m_localPlayer == null || MayBuildAt(hall, at, Player.m_localPlayer.GetPlayerID());

        /// <summary>Whether this player may put something of the colony's (a hut, a field marker) for this Town Hall at this spot.</summary>
        public static bool MayBuildAt(ColonyBuilding hall, Vector3 at, long player)
        {
            if (hall == null) return true;
            long owner = OwnerOf(hall);
            bool onWard = owner != 0L && player != 0L && player != owner && OnOwnersWard(at, owner, player);
            return ColonyManagers.MayManage(owner, player, onWard);
        }

        /// <summary>On the hut's owner computer: whether the player behind this request may manage the colony.</summary>
        public static bool SenderMayManage(ColonyBuilding building, long sender) => MayManage(building, PlayerOf(sender));

        /// <summary>Whether the player at this computer may manage the colony (the window greys out the rest).</summary>
        public static bool LocalMayManage(ColonyBuilding building) =>
            Player.m_localPlayer != null && MayManage(building, Player.m_localPlayer.GetPlayerID());

        /// <summary>The player whose computer sent a request: his character is owned by that computer. 0 when not found.</summary>
        private static long PlayerOf(long sender)
        {
            if (ZNet.instance != null && sender == ZNet.GetUID())
                return Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerID() : 0L;
            foreach (Player player in Player.GetAllPlayers())
            {
                ZNetView nview = player != null ? player.GetComponent<ZNetView>() : null;
                if (nview != null && nview.IsValid() && nview.GetZDO().GetOwner() == sender) return player.GetPlayerID();
            }
            return 0L;
        }

        /// <summary>The owner has a ward covering this spot and put the player on it.</summary>
        private static bool OnOwnersWard(Vector3 at, long owner, long player)
        {
            if (!(s_allAreas?.GetValue(null) is List<PrivateArea> areas)) return false;
            foreach (PrivateArea area in areas)
            {
                if (area == null || !(s_areaPiece.GetValue(area) is Piece piece) || piece.GetCreator() != owner) continue;
                if (!(bool)s_isInside.Invoke(area, new object[] { at, 0f })) continue;
                if ((bool)s_isPermitted.Invoke(area, new object[] { player })) return true;
            }
            return false;
        }
    }
}
