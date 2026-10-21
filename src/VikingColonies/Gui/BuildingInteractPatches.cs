using HarmonyLib;
using VikingColonies.Buildings;

namespace VikingColonies.Gui
{
    /// <summary>
    /// E keeps opening a colony building's storage; Shift+E (the game's "alt use") opens its window. Other mods change
    /// chests too (issue #1, 1.4.1: MyLittleUI's rename took Shift+E and its hover rewrite dropped "Manage"): on a colony
    /// building our Shift+E goes first and our hover line last, whoever else is there.
    /// </summary>
    internal static class BuildingInteractPatches
    {
        /// <summary>Mods known to change chests' hover and Shift+E (their BepInEx ids): ours goes around them.</summary>
        internal const string MyLittleUI = "shudnal.MyLittleUI";

        /// <summary>
        /// Shift+E on a colony building, where the player presses it: the window opens and the chest is never "used", so
        /// another mod's Shift+E on chests (MyLittleUI's rename box, issue #1: it opened over our window though our
        /// Container.Interact prefix went first) never fires.
        /// </summary>
        [HarmonyPatch]
        private static class PlayerInteractHook
        {
            [HarmonyPatch(typeof(Player), "Interact"), HarmonyPrefix]
            [HarmonyPriority(Priority.First), HarmonyBefore(MyLittleUI)]
            private static bool Interact(Player __instance, UnityEngine.GameObject go, bool hold, bool alt)
            {
                if (!alt || hold || go == null || __instance != Player.m_localPlayer) return true;
                ColonyBuilding building = go.GetComponentInParent<ColonyBuilding>();
                if (building == null) return true;
                if (building.IsTownHall) Windows.TownHall.Open(building);
                else Windows.Hut.Open(building);
                return false;
            }
        }

        [HarmonyPatch]
        private static class InteractHook
        {
            [HarmonyPatch(typeof(Container), nameof(Container.Interact)), HarmonyPrefix]
            [HarmonyPriority(Priority.First), HarmonyBefore(MyLittleUI)]
            private static bool Interact(Container __instance, Humanoid character, bool hold, bool alt, ref bool __result)
            {
                if (!alt || hold || character != Player.m_localPlayer) return true;
                ColonyBuilding building = __instance.GetComponent<ColonyBuilding>();
                if (building == null) return true;
                if (building.IsTownHall) Windows.TownHall.Open(building);
                else Windows.Hut.Open(building);
                __result = true;
                return false;
            }
        }

        [HarmonyPatch]
        private static class HoverTextHook
        {
            [HarmonyPatch(typeof(Container), nameof(Container.GetHoverText)), HarmonyPostfix]
            [HarmonyPriority(Priority.Last), HarmonyAfter(MyLittleUI)]
            private static void HoverText(Container __instance, ref string __result)
            {
                ColonyBuilding building = __instance.GetComponent<ColonyBuilding>();
                if (building == null) return;
                if (!building.IsTownHall && string.IsNullOrEmpty(building.ColonyId))
                    __result += Localization.instance.Localize("\n<color=orange>$vc_build_near_town_hall</color>");
                else if (building.IsTownHall && Config.ColonyConfig.HallLevels.Value)
                    __result += "\n" + string.Format(Localization.instance.Localize("$vc_hall_level_hover"), building.Level, Core.TownHallLevels.MaxLevel);
                else if (!building.IsTownHall && building.GraceDaysLeft > 0)
                    __result += "\n<color=orange>" + string.Format(Localization.instance.Localize(building.GraceDaysLeft == 1 ? "$vc_hall_gone_grace_one" : "$vc_hall_gone_grace"), building.GraceDaysLeft) + "</color>";
                // Another mod's Shift+E line (MyLittleUI's "Rename", issue #1): on a colony building Shift+E manages it.
                __result = Core.HoverLines.WithoutKeyLine(__result, Localization.instance.Localize("$hud_rename"), Localization.instance.Localize("$KEY_AltPlace"));
                __result += Localization.instance.Localize("\n[<color=yellow><b>$KEY_AltPlace + $KEY_Use</b></color>] $vc_manage");
            }
        }

        // The Town Hall is also a crafting station (so huts can require it). Should the game pick the station when
        // the player looks at or uses the Town Hall, hand over to the storage, which is what players expect.

        [HarmonyPatch]
        private static class StationInteractHook
        {
            [HarmonyPatch(typeof(CraftingStation), nameof(CraftingStation.Interact)), HarmonyPrefix]
            private static bool StationInteract(CraftingStation __instance, Humanoid user, bool repeat, bool alt, ref bool __result)
            {
                Container container = ColonyContainer(__instance);
                if (container == null) return true;
                __result = container.Interact(user, repeat, alt);
                return false;
            }
        }

        [HarmonyPatch]
        private static class StationUseItemHook
        {
            [HarmonyPatch(typeof(CraftingStation), nameof(CraftingStation.UseItem)), HarmonyPrefix]
            private static bool StationUseItem(CraftingStation __instance, ref bool __result)
            {
                if (ColonyContainer(__instance) == null) return true;
                __result = false;
                return false;
            }
        }

        [HarmonyPatch]
        private static class StationHoverTextHook
        {
            [HarmonyPatch(typeof(CraftingStation), nameof(CraftingStation.GetHoverText)), HarmonyPrefix]
            private static bool StationHoverText(CraftingStation __instance, ref string __result)
            {
                Container container = ColonyContainer(__instance);
                if (container == null) return true;
                __result = container.GetHoverText();
                return false;
            }
        }

        [HarmonyPatch]
        private static class StationHoverNameHook
        {
            [HarmonyPatch(typeof(CraftingStation), nameof(CraftingStation.GetHoverName)), HarmonyPrefix]
            private static bool StationHoverName(CraftingStation __instance, ref string __result)
            {
                Container container = ColonyContainer(__instance);
                if (container == null) return true;
                __result = container.GetHoverName();
                return false;
            }
        }

        private static Container ColonyContainer(CraftingStation station) =>
            station.GetComponent<ColonyBuilding>() != null ? station.GetComponent<Container>() : null;
    }
}
