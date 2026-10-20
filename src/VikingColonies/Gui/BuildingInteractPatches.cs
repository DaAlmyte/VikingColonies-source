using HarmonyLib;
using VikingColonies.Buildings;

namespace VikingColonies.Gui
{
    /// <summary>E keeps opening a colony building's storage; Shift+E (the game's "alt use") opens its window.</summary>
    internal static class BuildingInteractPatches
    {
        [HarmonyPatch]
        private static class InteractHook
        {
            [HarmonyPatch(typeof(Container), nameof(Container.Interact)), HarmonyPrefix]
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
