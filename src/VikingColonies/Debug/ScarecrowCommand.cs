using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Jotunn.Entities;
using UnityEngine;
using VikingColonies.Construction;
using VikingColonies.Core;

namespace VikingColonies.DevTools
{
    /// <summary>
    /// vc_scarecrow (test worlds; custom assets' first test, 27 Oct 2026): what the game made of the mod's own models, the
    /// scarecrow and the chopping block (mesh, material, shader, icon and colliders), and 'on'/'off' to put them in the hammer or take it out for
    /// this game (over its Scarecrow.Enabled setting, never written to the settings file), 'setting' to go back to the
    /// setting, with whether the build menu then offers it.
    /// </summary>
    internal sealed class ScarecrowCommand : ConsoleCommand
    {
        public override string Name => "vc_scarecrow";
        public override string Help => "VikingColonies (test worlds): 'vc_scarecrow' says what the game made of the mod's own models (the scarecrow, the chopping block); " +
                                       "'vc_scarecrow on|off' puts them in the hammer's Misc tab or takes them out for this game (not written to the settings); " +
                                       "'vc_scarecrow setting' goes back to their setting";
        public override bool IsCheat => true;

        private static readonly System.Reflection.MethodInfo s_updatePieces = AccessTools.Method(typeof(Player), "UpdateAvailablePiecesList");

        public override void Run(string[] args)
        {
            if (DevHelpers.RefuseOutsideTestWorld(Name)) return;
            string word = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            if (word == "on" || word == "off" || word == "setting")
            {
                Buildings.BuildingPieces.ScarecrowTestChoice = word == "setting" ? (bool?)null : word == "on";
                Buildings.BuildingPieces.ShowScarecrow();
            }
            BuildCommand.Print("vc_scarecrow: " + Describe(SaveKeys.ScarecrowPiece) + "; the chopping block: " +
                               Describe(SaveKeys.ChoppingBlockPiece));
        }

        private static string Describe(string key)
        {
            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(key) : null;
            if (prefab == null) return "not registered";
            Piece piece = prefab.GetComponent<Piece>();
            MeshFilter mesh = prefab.GetComponentInChildren<MeshFilter>(true);
            Renderer renderer = prefab.GetComponentInChildren<MeshRenderer>(true);
            Material material = renderer != null ? renderer.sharedMaterial : null;
            int triangles = mesh != null && mesh.sharedMesh != null ? mesh.sharedMesh.triangles.Length / 3 : 0;
            int boxes = prefab.GetComponentsInChildren<BoxCollider>(true).Length;
            WearNTear wear = prefab.GetComponent<WearNTear>();
            string cost = piece != null ? string.Join(" ", piece.m_resources.Select(r => r.m_amount + " " + (r.m_resItem != null ? r.m_resItem.name : "?"))) : "";
            return $"registered, model '{(mesh != null ? mesh.sharedMesh?.name : "none")}' {triangles} triangles, " +
                   $"material '{(material != null ? material.name : "none")}' on '{(material != null && material.shader != null ? material.shader.name : "none")}', " +
                   $"texture {(material != null && material.mainTexture != null ? material.mainTexture.width + " px" : "none")} " +
                   $"at {(material != null && material.HasProperty("_MainTex") ? material.GetTextureScale("_MainTex").ToString() : "none")}, " +
                   $"normal map {(material != null && material.HasProperty("_BumpMap") && material.GetTexture("_BumpMap") != null ? material.GetTexture("_BumpMap").width + " px" : "none")}, " +
                   $"value noise {(material != null && material.HasProperty("_ValueNoise") ? material.GetFloat("_ValueNoise").ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture) : "none")}, " +
                   $"icon {(piece != null && piece.m_icon != null ? piece.m_icon.name : "none")}, {boxes} colliders, " +
                   $"wear {(wear != null ? wear.m_materialType + " " + wear.m_health : "none")}, cost {cost}, " +
                   $"recipe known {(Player.m_localPlayer != null && piece != null && Player.m_localPlayer.IsRecipeKnown(piece.m_name))}, " +
                   $"enabled {(piece != null && piece.m_enabled)}, no-cost mode {(Player.m_localPlayer != null && Player.m_localPlayer.NoCostCheat())}, " +
                   $"in the hammer: {(InHammer(piece) ? "yes" : "no")}";
        }

        /// <summary>
        /// Whether the hammer's build menu offers it now: the hammer's list made again as the game makes it when the hammer
        /// is in hand (the player's own refresh does nothing while it isn't: the third game run read a stale list), with
        /// every piece the player knows, then the player's own refresh to put the menu back as it was.
        /// </summary>
        private static bool InHammer(Piece piece)
        {
            Player player = Player.m_localPlayer;
            if (player == null || piece == null) return false;
            GameObject hammer = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab("Hammer") : null;
            PieceTable table = hammer != null ? hammer.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_buildPieces : null;
            HashSet<string> known = s_known != null ? s_known(player) : null;
            if (table == null || known == null) return false;
            table.UpdateAvailable(known, player, false, player.NoCostCheat());
            bool offered = table.m_availablePieces.Any(p => p != null && p.name == piece.name);
            s_updatePieces?.Invoke(player, null);
            return offered;
        }

        private static readonly AccessTools.FieldRef<Player, HashSet<string>> s_known = AccessTools.FieldRefAccess<Player, HashSet<string>>("m_knownRecipes");
    }
}
