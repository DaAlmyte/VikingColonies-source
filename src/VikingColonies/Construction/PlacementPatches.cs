using System.Reflection;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Managers;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Config;
using VikingColonies.Core;
using static VikingColonies.Construction.CoreVectors;

namespace VikingColonies.Construction
{
    /// <summary>
    /// Placing a hut: the raise and lower keys move the placement ghost up or down, the site check runs where it stands,
    /// and a red site can't be placed. Only while one of our huts (not the Town Hall) is selected; every other piece
    /// is left to the game.
    /// </summary>
    internal static class PlacementPatches
    {
        private static readonly AccessTools.FieldRef<Player, GameObject> s_ghost =
            AccessTools.FieldRefAccess<Player, GameObject>("m_placementGhost");
        private static readonly AccessTools.FieldRef<Player, Player.PlacementStatus> s_status =
            AccessTools.FieldRefAccess<Player, Player.PlacementStatus>("m_placementStatus");
        private static readonly MethodInfo s_setValid = AccessTools.Method(typeof(Player), "SetPlacementGhostValid");
        private static readonly PlacementOffset s_offset = new PlacementOffset();
        private static readonly GhostLift s_lift = new GhostLift();
        private static ButtonConfig s_raise;
        private static ButtonConfig s_lower;
        private static int s_keysFrame = -1;

        public static void RegisterKeys()
        {
            s_raise = new ButtonConfig { Name = "vc_raise_hut", Config = ColonyConfig.RaiseHutKey, HintToken = "$vc_key_raise" };
            s_lower = new ButtonConfig { Name = "vc_lower_hut", Config = ColonyConfig.LowerHutKey, HintToken = "$vc_key_lower" };
            InputManager.Instance.AddButton(Plugin.PluginGuid, s_raise);
            InputManager.Instance.AddButton(Plugin.PluginGuid, s_lower);
            CreativePlacing.RegisterKeys();   // creative mode (1.5): the level a colony building goes up at once
        }

        /// <summary>
        /// Placing a hut: the site is checked again right now (not the last look, up to a quarter of a second old), and a
        /// red site is refused with its reason. Every attempt is logged with the numbers behind the check.
        /// </summary>
        [HarmonyPatch]
        private static class BeforeTryPlacePieceHook
        {
            [HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece)), HarmonyPrefix]
            private static bool BeforeTryPlacePiece(Player __instance, Piece piece, ref bool __result)
            {
                if (__instance != Player.m_localPlayer || piece == null) return true;
                BuildingType type = BuildingTypes.ByPrefab(Utils.GetPrefabName(piece.gameObject));
                GameObject ghost = s_ghost(__instance);
                if (type != null && ghost != null && ghost.activeSelf) CreativePlacing.NotePlacing(type, ghost.transform.position);
                // A field marker in a colony he may not manage is refused, as a hut is (final review of 0.20); its owner checks too.
                if (Utils.GetPrefabName(piece.gameObject) == SaveKeys.FieldMarkerPiece && ghost != null && ghost.activeSelf)
                {
                    Vector3 at = ghost.transform.position;
                    if (ColonyPermissions.LocalMayBuildAt(SiteScanner.ColonyFor(at), at)) return true;
                    Log.Info($"Placing a field marker at {at}: refused, someone else's colony");
                    __instance.Message(MessageHud.MessageType.Center, Localization.instance.Localize("$vc_site_red_not_yours"));
                    __result = false;
                    return false;
                }
                if (type == null || type.Id == BuildingTypes.TownHallId || ghost == null || !ghost.activeSelf) return true;
                SitePreview.Update(type, ghost, s_offset.Value, force: true);
                SiteVerdict verdict = SitePreview.Verdict;
                SiteFindings f = SitePreview.Findings;
                Log.Info($"Placing a {type.Id} at {ghost.transform.position} (raised {s_offset.Value:+0.00;-0.00} m): {verdict?.Colour} {verdict?.Red}" +
                         (f == null ? "" : $"; floor {f.Floor:0.00}, design reaches {f.ReachBelow:0.00} below it, ground {f.Natural:0.00}, flattened to {f.Target:0.00}"));
                if (verdict == null || verdict.Colour != SiteColour.Red) return true;
                __instance.Message(MessageHud.MessageType.Center, SiteText.Describe(verdict, 0f));
                __result = false;
                return false;
            }
        }

        [HarmonyPatch]
        private static class AfterUpdatePlacementGhostHook
        {
            [HarmonyPatch(typeof(Player), "UpdatePlacementGhost"), HarmonyPostfix]
            private static void AfterUpdatePlacementGhost(Player __instance)
            {
                if (__instance != Player.m_localPlayer) return;
                Piece selected = __instance.GetSelectedPiece();
                s_offset.Select(selected != null ? selected.name : "");
                BuildingType type = selected != null ? BuildingTypes.ByPrefab(Utils.GetPrefabName(selected.gameObject)) : null;
                GameObject ghost = s_ghost(__instance);
                bool placing = type != null && ghost != null && ghost.activeSelf && __instance.InPlaceMode();
                CreativePlacing.Update(placing ? type : null, placing ? ghost.transform.position : Vector3.zero);
                if (type == null || type.Id == BuildingTypes.TownHallId || ghost == null || !ghost.activeSelf || !__instance.InPlaceMode())
                {
                    SitePreview.Clear();
                    s_lift.Forget();
                    return;
                }
                ReadKeys();
                ghost.transform.position = U(s_lift.Lift(V(ghost.transform.position), s_offset.Value));
                SitePreview.Update(type, ghost, s_offset.Value);
                if (SitePreview.Verdict != null && SitePreview.Verdict.Colour == SiteColour.Red && s_status(__instance) == Player.PlacementStatus.Valid)
                {
                    s_status(__instance) = Player.PlacementStatus.Invalid;   // placing it says "invalid placement"; the line says why
                    s_setValid.Invoke(__instance, new object[] { false });
                }
            }
        }

        /// <summary>The site check's line under the crosshair while a hut is being placed.</summary>
        [HarmonyPatch]
        private static class AfterUpdateCrosshairHook
        {
            [HarmonyPatch(typeof(Hud), "UpdateCrosshair"), HarmonyPostfix]
            private static void AfterUpdateCrosshair(Hud __instance)
            {
                if (__instance.m_hoverName == null) return;
                string site = SitePreview.Active ? SitePreview.Text ?? "" : "";
                string creative = CreativePlacing.Line();   // creative mode (1.5): the level it goes up at once
                if (site.Length == 0 && creative.Length == 0) return;
                __instance.m_hoverName.text = site.Length == 0 ? creative : creative.Length == 0 ? site : site + "\n" + creative;
            }
        }

        private static void ReadKeys()
        {
            if (Time.frameCount == s_keysFrame || s_raise == null) return;   // the game may update the ghost twice in a frame
            s_keysFrame = Time.frameCount;
            if (ZInput.GetButtonDown(s_raise.Name)) s_offset.Raise();
            if (ZInput.GetButtonDown(s_lower.Name)) s_offset.Lower();
        }
    }
}
