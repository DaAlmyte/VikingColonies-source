using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Citizens;
using VikingColonies.Construction;
using VikingColonies.Core;

namespace VikingColonies.DevTools
{
    /// <summary>Shared by the test commands and the self-test: building types by a word, judging and placing huts, doors, monsters.</summary>
    internal static class DevHelpers
    {
        /// <summary>"townhall" (or "hall"), "builder", "lumberjack", or a building type id; null for anything else.</summary>
        public static BuildingType TypeByWord(string word)
        {
            string w = (word ?? "").Trim().ToLowerInvariant();
            if (w == "townhall" || w == "hall") return BuildingTypes.TownHall;
            if (w == "builder") return BuildingTypes.BuilderHut;
            if (w == "lumberjack") return BuildingTypes.LumberjackHut;
            if (w == "forager") return BuildingTypes.ForagerHut;
            if (w == "farm" || w == "farmer") return BuildingTypes.Farm;
            return BuildingTypes.Get(w);
        }

        /// <summary>
        /// A setting changed for a test run only: the config file isn't written (the 1.4.0 review: vc_buildrun left
        /// StartingCitizens = 10 in the player's config when a run stopped half-way).
        /// </summary>
        internal static void SetUnsaved<T>(BepInEx.Configuration.ConfigEntry<T> entry, T value)
        {
            BepInEx.Configuration.ConfigFile file = entry.ConfigFile;
            bool saving = file.SaveOnConfigSet;
            file.SaveOnConfigSet = false;
            try { entry.Value = value; }
            finally { file.SaveOnConfigSet = saving; }
        }

        /// <summary>The site check for a hut of this type standing here, turned so (logged with the details).</summary>
        public static SiteVerdict Judge(BuildingType type, Vector3 at, Quaternion rotation)
        {
            BlueprintFile blueprint = BlueprintLibrary.For(type, out _);
            if (blueprint == null || !BlueprintPlan.TryPlace(blueprint, type.PrefabName, CoreVectors.V(at), CoreVectors.Q(rotation), out List<PlannedPiece> plan, out _))
                return null;
            SiteFindings findings = SiteSurvey.Look(plan, at, rotation, null, null, p => false, GroundShaper.StonePerCubicMetre,
                new List<SiteThing>(), new List<Piece>(), type: type);
            Log.Info($"Site check for a {type.Id} at {at}: clear [{string.Join(", ", findings.Clear.Select(c => c.Value + " " + c.Key))}], " +
                     $"dig {findings.Dig}, raise {findings.Raise}, {findings.Stone} stone, red [{string.Join(", ", findings.Red)}]" +
                     (findings.Unbreakable.Count > 0 ? $", nothing breaks [{string.Join("; ", findings.Unbreakable)}]" : ""));
            return SiteCheck.Judge(findings, SiteScanner.BuilderLevelFor(at), SiteScanner.ToolsAtHandFor(at));
        }

        /// <summary>
        /// Puts a building down on the ground, as the hammer would (it gets its build order); <paramref name="creator"/> is
        /// the player who "built" it, 0 for none.
        /// </summary>
        public static ColonyBuilding Place(string prefabName, Vector3 position, Quaternion rotation, long creator = 0L, float raise = 0f)
        {
            GameObject prefab = ZNetScene.instance.GetPrefab(prefabName);
            if (prefab == null) return null;
            position.y = ZoneSystem.instance.GetGroundHeight(position) + raise;   // on the ground (raised as placing starts it: the Storehouse, 1.5)
            GameObject go = Object.Instantiate(prefab, position, rotation);
            Piece piece = go.GetComponent<Piece>();
            if (piece != null && creator != 0L) piece.SetCreator(creator, default(Splatform.PlatformUserID));
            return go.GetComponent<ColonyBuilding>();
        }

        /// <summary>
        /// The test colonies of the checks from before 0.21 start with a Town Hall that keeps nothing, so settlers carrying
        /// to it in free time don't move the food those checks count (vc_workertest checks Keep in stock itself).
        /// </summary>
        public static void KeepNothing(ColonyBuilding building)
        {
            ZNetView view = building != null ? building.View : null;
            if (view != null && view.IsValid() && view.IsOwner()) view.GetZDO().Set(SaveKeys.Keep, new StockList().Encode());
        }

        /// <summary>
        /// A block of stone (2 m over the doorstep) on the spot outside a door where a settler stands to go through it, as a player
        /// might build in front of a door; empty when the game has no such piece. Destroy them when done.
        /// </summary>
        public static List<GameObject> BlockDoor(Door door, Vector3 inside)
        {
            var blocks = new List<GameObject>();
            GameObject prefab = ZNetScene.instance.GetPrefab("stone_floor_2x2");   // 2 x 2 x 1 m, its middle at its pivot
            if (prefab == null) return blocks;
            Vector3 front = door.transform.position + door.transform.forward * DoorRules.StandOff;
            Vector3 back = door.transform.position - door.transform.forward * DoorRules.StandOff;
            Vector3 outside = Utils.DistanceXZ(front, inside) > Utils.DistanceXZ(back, inside) ? front : back;
            float ground = ZoneSystem.instance.GetGroundHeight(outside);
            int layers = TestDoorBlock.Layers(ground, door.transform.position.y);   // 2 m over a raised doorstep too (1.4.0 full run)
            for (int layer = 0; layer < layers; layer++)
                blocks.Add(Object.Instantiate(prefab, new Vector3(outside.x, ground + 0.5f + layer, outside.z), door.transform.rotation));
            return blocks;
        }

        private static readonly HarmonyLib.AccessTools.FieldRef<ZNetScene, Dictionary<ZDO, ZNetView>> s_instances =
            HarmonyLib.AccessTools.FieldRefAccess<ZNetScene, Dictionary<ZDO, ZNetView>>("m_instances");

        /// <summary>
        /// The object goes as when its area unloads (its saved data kept) and the game makes it anew in the next frames, as
        /// when the area loads again (the review round of 2026-10-23: a job kept the old hut object).
        /// </summary>
        public static void UnloadAndReload(ZNetView view)
        {
            if (view == null || !view.IsValid() || ZNetScene.instance == null) return;
            s_instances(ZNetScene.instance).Remove(view.GetZDO());
            view.ResetZDO();
            Object.Destroy(view.gameObject);
        }

        /// <summary>Puts a copy of a world object (a tree, a rock) on the ground here; null when the game has no such prefab.</summary>
        public static GameObject Spawn(string prefabName, Vector3 position)
        {
            GameObject prefab = ZNetScene.instance.GetPrefab(prefabName);
            if (prefab == null) return null;
            position.y = ZoneSystem.instance.GetGroundHeight(position);
            return Object.Instantiate(prefab, position, Quaternion.identity);
        }

        /// <summary>
        /// A field for a Farm (tests): a field marker at <paramref name="at"/>, in the Farm's colony and assigned to it, turned
        /// with it, with this size and crop ("" = the Farm's list). A Farm works only its fields (0.20.1).
        /// </summary>
        public static Colony.FieldMarker PlaceField(ColonyBuilding farm, Vector3 at, int size = FieldSizes.New, string crop = "")
        {
            GameObject prefab = ZNetScene.instance.GetPrefab(SaveKeys.FieldMarkerPiece);
            if (prefab == null || farm == null) return null;
            at.y = ZoneSystem.instance.GetGroundHeight(at);
            GameObject go = Object.Instantiate(prefab, at, farm.transform.rotation);
            ZNetView view = go.GetComponent<ZNetView>();
            if (view != null && view.IsValid())
            {
                ZDO zdo = view.GetZDO();
                zdo.Set(SaveKeys.ColonyId, farm.ColonyId);
                zdo.Set(SaveKeys.FieldFarm, farm.Id);
                zdo.Set(SaveKeys.FieldSize, FieldSizes.Valid(size));
                zdo.Set(SaveKeys.FieldCrop, crop ?? "");
            }
            return go.GetComponent<Colony.FieldMarker>();
        }

        /// <summary>The middle of the site a hut of this type would have here (its chest's spot when the design can't be read).</summary>
        public static Vector3 SiteCentre(BuildingType type, Vector3 at, Quaternion rotation)
        {
            BlueprintFile blueprint = BlueprintLibrary.For(type, out _);
            if (blueprint == null || !BlueprintPlan.TryPlace(blueprint, type.PrefabName, CoreVectors.V(at), CoreVectors.Q(rotation), out List<PlannedPiece> plan, out _))
                return at;
            return CoreVectors.U(SiteSurvey.SiteFootprint(plan, at, rotation).Centre);
        }

        /// <summary>The door of a hut's building (any planned piece with a door: a door or a gate), standing; null if none.</summary>
        public static Door DoorOf(ConstructionModule site) => HutDoors.DoorOf(site);

        /// <summary>
        /// Self-test: saves a change into a chest's contents as another computer would, without this computer's copy of the
        /// chest reloading it yet (the game reloads chests once a second).
        /// </summary>
        public static void WriteRemoteChange(Container container, string item, int amount)
        {
            Inventory mine = container.GetInventory();
            var copy = new ZPackage();
            mine.Save(copy);
            var theirs = new Inventory("remote", null, mine.GetWidth(), mine.GetHeight());
            theirs.Load(new ZPackage(copy.GetArray()));
            theirs.AddItem(ObjectDB.instance.GetItemPrefab(item), amount);
            var saved = new ZPackage();
            theirs.Save(saved);
            container.GetComponent<ZNetView>().GetZDO().Set(ZDOVars.s_items, saved.GetArray());   // the game keeps chest contents as bytes
        }

        /// <summary>Removes the creatures within <paramref name="radius"/> that settlers flee from: not players, settlers, tamed or plant-eating animals.</summary>
        /// <summary>
        /// Test worlds: removes every colony building, piece a builder put up, settler and cairn within the radius (old test
        /// colonies that would spoil the next run). Returns what was removed.
        /// </summary>
        /// <summary>
        /// Test worlds only (their name starts with VCTest): says so and returns true when this isn't one. Every test command
        /// that changes the world checks it (review of 0.20.1: vc_world wipe didn't, and the scenarios run it).
        /// </summary>
        public static bool RefuseOutsideTestWorld(string command)
        {
            string world = ZNet.instance != null ? ZNet.instance.GetWorldName() : "";
            if (TestWorlds.IsTestWorld(world)) return false;
            BuildCommand.Print($"{command}: only in a test world (its name must start with {TestWorlds.NameStart}); this is {world}");
            return true;
        }

        public static string WipeColonies(Vector3 centre, float radius)
        {
            if (ZNet.instance == null || !TestWorlds.IsTestWorld(ZNet.instance.GetWorldName())) return "nothing: not a test world";
            int settlers = 0, buildings = 0, pieces = 0, cairns = 0, fields = 0;
            foreach (Citizen c in Citizen.All.Where(c => c != null && Vector3.Distance(c.transform.position, centre) <= radius).ToList())
                if (Wipe(c.gameObject)) settlers++;
            foreach (SettlerCairn cairn in SettlerCairn.All.Where(c => c != null && Vector3.Distance(c.transform.position, centre) <= radius).ToList())
                if (Wipe(cairn.gameObject)) cairns++;
            foreach (Colony.FieldMarker marker in Colony.FieldMarker.All.Where(m => m != null && Vector3.Distance(m.transform.position, centre) <= radius).ToList())
                if (Wipe(marker.gameObject)) fields++;
            var near = new List<Piece>();
            Piece.GetAllPiecesInRadius(centre, radius, near);
            foreach (Piece piece in near.Where(p => p != null && (PiecePlacer.SiteOf(p.gameObject).Length > 0 || BuildCommand.IsCostChestPiece(p))).ToList())
                if (Wipe(piece.gameObject)) pieces++;
            foreach (ColonyBuilding b in ColonyBuilding.All.Where(b => b != null && Vector3.Distance(b.transform.position, centre) <= radius).ToList())
                if (Wipe(b.gameObject)) buildings++;
            return $"{buildings} colony buildings, {pieces} building pieces, {settlers} settlers, {cairns} cairns, {fields} field markers";
        }

        /// <summary>
        /// A piece the builder put up for a hut that is gone (its colony building isn't here). Seen: earlier test runs
        /// left walls behind, and the self-test's spot finder ran out of open ground.
        /// </summary>
        public static bool IsOrphanPiece(Piece piece)
        {
            string site = piece != null ? PiecePlacer.SiteOf(piece.gameObject) : "";
            return site.Length > 0 && ColonyBuilding.FindById(site) == null;
        }

        /// <summary>Test worlds: removes the builder's pieces near a spot whose hut is gone.</summary>
        public static int RemoveOrphanPieces(Vector3 centre, float radius)
        {
            var near = new List<Piece>();
            Piece.GetAllPiecesInRadius(centre, radius, near);
            int removed = 0;
            foreach (Piece piece in near.Where(IsOrphanPiece).ToList())
                if (Wipe(piece.gameObject)) removed++;
            return removed;
        }

        private static bool Wipe(GameObject go)
        {
            ZNetView nview = go.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid()) return false;
            if (!nview.IsOwner()) nview.ClaimOwnership();
            ZNetScene.instance.Destroy(go);
            return true;
        }

        /// <summary>Test worlds: removes fallen logs near a spot (one rolled into a doorway blocks it).</summary>
        public static int ClearLogs(Vector3 centre, float radius)
        {
            int removed = 0;
            foreach (TreeLog log in Object.FindObjectsByType<TreeLog>(FindObjectsSortMode.None))
                if (log != null && Vector3.Distance(log.transform.position, centre) <= radius && Wipe(log.gameObject)) removed++;
            return removed;
        }

        /// <param name="animalsToo">Deer, boars and the like too: a settler counts one close by as a threat (BaseAI.IsEnemy).</param>
        public static int ClearMonsters(Vector3 centre, float radius, bool animalsToo = false)
        {
            int removed = 0;
            foreach (Character c in Character.GetAllCharacters().ToList())
            {
                if (c == null || c.IsPlayer() || c.IsTamed() || c.GetComponent<Citizen>() != null) continue;
                if ((c.m_faction == Character.Faction.AnimalsVeg && !animalsToo) || Vector3.Distance(c.transform.position, centre) > radius) continue;
                ZNetView nview = c.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid()) continue;
                if (!nview.IsOwner()) nview.ClaimOwnership();
                ZNetScene.instance.Destroy(c.gameObject);
                removed++;
            }
            return removed;
        }

        /// <summary>Text without the game's colour and style tags, for the console and the command file.</summary>
        public static string Plain(string text) => System.Text.RegularExpressions.Regex.Replace(text ?? "", "<[^>]+>", "");

        /// <summary>Turns the player to look at <paramref name="target"/>.</summary>
        public static void Face(Player player, Vector3 target)
        {
            Vector3 dir = target - player.GetEyePoint();
            if (dir.sqrMagnitude < 0.01f) return;
            Vector3 flat = new Vector3(dir.x, 0f, dir.z);
            if (flat.sqrMagnitude > 0.01f) player.transform.rotation = Quaternion.LookRotation(flat);
            player.SetLookDir(dir.normalized);
        }
    }
}
