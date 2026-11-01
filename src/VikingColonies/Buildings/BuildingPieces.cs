using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;
using VikingColonies.Config;
using VikingColonies.Core;

namespace VikingColonies.Buildings
{
    /// <summary>Registers the colony buildings in the hammer menu. v2 reuses vanilla chest models.</summary>
    internal static class BuildingPieces
    {
        public static void Register()
        {
            CustomPiece townHall = Register(BuildingTypes.TownHall, "piece_chest", "$vc_town_hall_desc", 6, 4,
                ColonyConfig.HallWoodCost, ColonyConfig.HallStoneCost);
            CustomPiece hut = Register(BuildingTypes.LumberjackHut, "piece_chest_wood", "$vc_hut_lumberjack_desc", 6, 3,
                ColonyConfig.LumberjackHutWoodCost, null);
            CustomPiece builderHut = Register(BuildingTypes.BuilderHut, "piece_chest_wood", "$vc_hut_builder_desc", 6, 3,
                ColonyConfig.BuilderHutWoodCost, null);
            CustomPiece longhouse = Register(BuildingTypes.Longhouse, "piece_chest_wood", "$vc_hut_longhouse_desc", 4, 2,
                ColonyConfig.LonghouseWoodCost, null);
            CustomPiece foragerHut = Register(BuildingTypes.ForagerHut, "piece_chest_wood", "$vc_hut_forager_desc", 6, 3,
                ColonyConfig.ForagerHutWoodCost, null);
            CustomPiece farm = Register(BuildingTypes.Farm, "piece_chest_wood", "$vc_hut_farm_desc", 6, 3,
                ColonyConfig.FarmWoodCost, null);
            CustomPiece watchtower = Register(BuildingTypes.Watchtower, "piece_chest_wood", "$vc_hut_watchtower_desc", 6, 3,
                ColonyConfig.WatchtowerWoodCost, null);
            CustomPiece storehouse = Register(BuildingTypes.Storehouse, "piece_chest_wood", "$vc_hut_storehouse_desc", 6, 3,
                ColonyConfig.StorehouseWoodCost, null);
            CustomPiece meadHall = Register(BuildingTypes.MeadHall, "piece_chest_wood", "$vc_hut_meadhall_desc", 6, 3,
                ColonyConfig.MeadHallWoodCost, null);
            // A cost changed in the settings, or the server's arriving when a player joins it, is what the hammer asks for
            // at once (v1 review: it took the costs read at start-up until the game was restarted).
            SynchronizationManager.OnConfigurationSynchronized += (sender, e) => ApplyCosts();
            // Again each time a world's pieces are made ready: leaving a server puts our settings back while the game shuts
            // its world down, perhaps after the game's item list is gone (review of 0.19).
            PieceManager.OnPiecesRegistered += ApplyCosts;
            // Job huts need a Town Hall nearby instead of a workbench (the Town Hall is their crafting station).
            CraftingStation hallStation = townHall?.PiecePrefab.GetComponent<CraftingStation>();
            if (hut != null) hut.Piece.m_craftingStation = hallStation;
            if (builderHut != null) builderHut.Piece.m_craftingStation = hallStation;
            if (longhouse != null) longhouse.Piece.m_craftingStation = hallStation;
            if (foragerHut != null) foragerHut.Piece.m_craftingStation = hallStation;
            if (farm != null) farm.Piece.m_craftingStation = hallStation;
            if (watchtower != null) watchtower.Piece.m_craftingStation = hallStation;
            if (storehouse != null) storehouse.Piece.m_craftingStation = hallStation;
            if (meadHall != null) meadHall.Piece.m_craftingStation = hallStation;
            RegisterFieldMarker(hallStation);
            RegisterScarecrow(hallStation);
            RegisterMemorialStone(hallStation);
        }

        private static readonly List<CustomPiece> s_ownModels = new List<CustomPiece>();

        /// <summary>
        /// The scarecrow (custom assets' first test, 27 Oct 2026) and the chopping block (the Valheim look pass's second
        /// model, the same day): the mod's own models (tools/assets, art/models, in the embedded bundle), decoration only,
        /// inside a colony (the camp is their station, as the field marker's); in the hammer while Scarecrow.Enabled is on
        /// (off until the player decides they ship). Ones already standing stay when it's turned off.
        /// </summary>
        private static void RegisterScarecrow(CraftingStation hallStation)
        {
            // guarded (review of the look pass): a game update breaking these test pieces must not stop the pieces and
            // the settlers registered after them, whatever the setting
            try
            {
                RegisterOwnModel("scarecrow", SaveKeys.ScarecrowPiece, "$vc_scarecrow", "$vc_scarecrow_desc", hallStation,
                    new RequirementConfig("Wood", 4, 0, true), new RequirementConfig("LeatherScraps", 1, 0, true));
                RegisterOwnModel("choppingblock", SaveKeys.ChoppingBlockPiece, "$vc_choppingblock", "$vc_choppingblock_desc", hallStation,
                    new RequirementConfig("Wood", 4, 0, true), new RequirementConfig("Flint", 1, 0, true));
            }
            catch (System.Exception e)
            {
                Log.Error($"The mod's own models couldn't be registered: {e}");
            }
            finally
            {
                Visuals.ModAssets.Release();
            }
        }

        /// <summary>
        /// One of the mod's own models as a piece, on a copy of the game's 2 m wooden pole, which gives it a game piece's
        /// parts (its network id, cost and station, wood's health, sounds and breaking). The pole's look is taken off and
        /// the model put in its place, drawn with a copy of the pole's own wood material (the game's piece shader: its
        /// light, wet look and snow) with the model's painted texture and normal map.
        /// </summary>
        private static void RegisterOwnModel(string modelName, string key, string name, string description,
            CraftingStation hallStation, params RequirementConfig[] cost)
        {
            GameObject model = Visuals.ModAssets.Prefab(modelName);
            if (model == null)
            {
                Log.Error($"The model '{modelName}' is missing; {key} is disabled.");
                return;
            }
            var config = new PieceConfig
            {
                Name = name,
                Description = description,
                PieceTable = PieceTables.Hammer,
                Category = PieceCategories.Misc,
                CraftingStation = CraftingStations.Workbench,
            };
            foreach (RequirementConfig requirement in cost) config.AddRequirement(requirement);
            var piece = new CustomPiece(key, "wood_pole2", config);
            if (piece.PiecePrefab == null)
            {
                Log.Error($"Could not clone 'wood_pole2'; {key} is disabled.");
                return;
            }
            GameObject prefab = piece.PiecePrefab;
            WearNTear wear = prefab.GetComponent<WearNTear>();
            Material wood = WoodMaterial(prefab, wear);
            if (wood == null) Log.Warn($"'wood_pole2' has no wood material; {key} keeps its stand-in look.");
            string pole = string.Join(", ", prefab.transform.Cast<Transform>().Select(t => t.name));
            foreach (Transform child in prefab.transform.Cast<Transform>().ToList()) Object.DestroyImmediate(child.gameObject);
            foreach (LODGroup lod in prefab.GetComponents<LODGroup>()) Object.DestroyImmediate(lod);
            foreach (Collider collider in prefab.GetComponents<Collider>()) Object.DestroyImmediate(collider);

            GameObject look = Object.Instantiate(model, prefab.transform, false);
            look.name = modelName;
            int layer = LayerMask.NameToLayer("piece");
            foreach (Transform t in look.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            MeshRenderer renderer = look.GetComponentInChildren<MeshRenderer>(true);
            if (renderer != null && wood != null)
                renderer.sharedMaterial = Visuals.ModAssets.Painted(wood, renderer.sharedMaterial, key);

            if (wear != null)
            {
                wear.m_new = wear.m_worn = wear.m_broken = look;   // one look, however worn
                wear.m_wet = null;
                wear.m_snow = wear.m_snowWorn = wear.m_snowBroken = null;
                wear.m_fragmentRoots = null;                         // broken up from the model itself
                wear.m_nonSolidRenderers?.Clear();
                wear.m_noSupportWear = false;                        // stands on its own, as the field marker
            }
            Sprite icon = Visuals.ModAssets.Icon(modelName);
            if (icon != null) piece.Piece.m_icon = icon;
            piece.Piece.m_craftingStation = hallStation;
            InMiscTab(piece);
            PieceManager.Instance.AddPiece(piece);
            s_ownModels.Add(piece);
            if (s_ownModels.Count == 1)
            {
                if (ColonyConfig.ScarecrowEnabled != null) ColonyConfig.ScarecrowEnabled.SettingChanged += (sender, e) => ShowScarecrow();
                SynchronizationManager.OnConfigurationSynchronized += (sender, e) => ShowScarecrow();
            }
            ShowScarecrow();
            Log.Info($"{modelName} registered: its own model in place of the pole's ({pole}), drawn with '{(wood != null ? wood.name : "none")}' " +
                     $"on '{(wood != null && wood.shader != null ? wood.shader.name : "none")}' (its tiling {(wood != null && wood.HasProperty("_MainTex") ? wood.GetTextureScale("_MainTex").ToString() : "none")})" +
                     $"{(icon != null ? ", its own icon" : "")}");
        }

        /// <summary>
        /// The cloned piece's own look's material (its "new" look, the game's piece shader), never its snow cap's: the
        /// pole's first renderer is its snow (the first game run drew the scarecrow with the snow shader).
        /// </summary>
        private static Material WoodMaterial(GameObject prefab, WearNTear wear)
        {
            var snow = new HashSet<Renderer> { wear?.m_snow, wear?.m_snowWorn, wear?.m_snowBroken };
            GameObject look = wear != null && wear.m_new != null ? wear.m_new : prefab;
            return look.GetComponentsInChildren<MeshRenderer>(true)
                .Where(r => !snow.Contains(r) && r.sharedMaterial != null && r.sharedMaterial.shader != null &&
                            r.sharedMaterial.shader.name.IndexOf("snow", System.StringComparison.OrdinalIgnoreCase) < 0)
                .Select(r => r.sharedMaterial)
                .OrderByDescending(m => m.shader.name == "Custom/Piece")
                .FirstOrDefault();
        }

        /// <summary>A test's own choice for this game (vc_scarecrow on|off), over the setting, never written to its file.</summary>
        internal static bool? ScarecrowTestChoice;

        /// <summary>Whether the mod's own models (the scarecrow, the chopping block) are offered in the hammer: the test's choice, else the setting (the server's, on a server).</summary>
        internal static bool ScarecrowShown =>
            (InTestWorld ? ScarecrowTestChoice : null) ?? ColonyConfig.ScarecrowEnabled?.Value ?? false;

        /// <summary>vc_scarecrow's choice holds in a test world only (review: it outlived a logout into a real world).</summary>
        private static bool InTestWorld => ZNet.instance != null && TestWorlds.IsTestWorld(ZNet.instance.GetWorldName());

        /// <summary>The scarecrow in the hammer or not, at once: the local player's build menu is made again (review: a
        /// server's setting arriving left an open menu as it was).</summary>
        internal static void ShowScarecrow()
        {
            if (s_ownModels.Count == 0) return;
            foreach (CustomPiece piece in s_ownModels)
                if (piece.Piece != null) piece.Piece.m_enabled = ScarecrowShown;
            if (Player.m_localPlayer == null) return;
            // what the player knows how to build first (the game adds a piece once its materials are known), then the menu
            s_updateKnown?.Invoke(Player.m_localPlayer, null);
            s_updatePieces?.Invoke(Player.m_localPlayer, null);
        }

        private static readonly System.Reflection.MethodInfo s_updatePieces = HarmonyLib.AccessTools.Method(typeof(Player), "UpdateAvailablePiecesList");
        private static readonly System.Reflection.MethodInfo s_updateKnown = HarmonyLib.AccessTools.Method(typeof(Player), "UpdateKnownRecipesList");

        /// <summary>
        /// The scarecrow kept out of the build menu while it isn't shown, in no-cost mode too: the game's list then offers
        /// every piece, its off switch (m_enabled) or not (review of the scarecrow: devcommands' nocost, and the mod's
        /// Creative mode, would have shown it).
        /// </summary>
        [HarmonyLib.HarmonyPatch(typeof(PieceTable), nameof(PieceTable.UpdateAvailable))]
        private static class HideScarecrowPatch
        {
            private static readonly HarmonyLib.AccessTools.FieldRef<PieceTable, List<List<Piece>>> s_byCategory =
                HarmonyLib.AccessTools.FieldRefAccess<PieceTable, List<List<Piece>>>("m_availablePiecesByCategory");

            private static void Postfix(PieceTable __instance)
            {
                if (s_ownModels.Count == 0 || ScarecrowShown) return;
                var names = new HashSet<string>(s_ownModels.Where(p => p.Piece != null).Select(p => p.Piece.name));
                __instance.m_availablePieces.RemoveWhere(p => p != null && names.Contains(p.name));
                List<List<Piece>> byCategory = s_byCategory(__instance);
                if (byCategory == null) return;
                foreach (List<Piece> category in byCategory)
                    category?.RemoveAll(p => p != null && names.Contains(p.name));
            }
        }

        /// <summary>
        /// The memorial stone (1.5.1): the game's Grausten tapered pillar standing as a stone where the colony remembers its
        /// fallen, dressed as one of the game's runestones in each world (<see cref="MemorialStoneLook"/>, 1.6.0), inside a colony's area (the camp is its station), one per colony (<see cref="Construction.PlacementPatches"/>).
        /// </summary>
        private static void RegisterMemorialStone(CraftingStation hallStation)
        {
            ConfigEntry<int> stoneCost = ColonyConfig.MemorialStoneCost;
            int stone = stoneCost != null ? stoneCost.Value : 0;
            var config = new PieceConfig
            {
                Name = "$vc_memorial",
                Description = "$vc_memorial_desc",
                PieceTable = PieceTables.Hammer,
                Category = PieceCategories.Misc,
                CraftingStation = CraftingStations.Workbench,
            };
            if (stone > 0) config.AddRequirement("Stone", stone);
            var piece = new CustomPiece(SaveKeys.MemorialPiece, "Piece_grausten_pillarbase_tapered", config);
            if (piece.PiecePrefab == null)
            {
                Log.Info("Could not clone 'Piece_grausten_pillarbase_tapered'; the memorial stone is the stone pillar.");
                piece = new CustomPiece(SaveKeys.MemorialPiece, "stone_pillar", config);
            }
            if (piece.PiecePrefab == null)
            {
                Log.Error("Could not clone a stone for the memorial stone; it is disabled.");
                return;
            }
            if (stone <= 0) piece.Piece.m_resources = new Piece.Requirement[0];
            piece.PiecePrefab.AddComponent<Colony.MemorialStone>();
            // It stands on its own (as the field marker): no support needed, so it never crumbles on soft ground.
            WearNTear wear = piece.PiecePrefab.GetComponent<WearNTear>();
            if (wear != null) wear.m_noSupportWear = false;
            piece.Piece.m_craftingStation = hallStation;
            InMiscTab(piece);
            PieceManager.Instance.AddPiece(piece);
            s_costed.Add(new Costed { Piece = piece, Wood = null, Stone = stoneCost });
            if (stoneCost != null) stoneCost.SettingChanged += (sender, e) => ApplyCosts();
            MemorialStoneLook.Watch(piece);   // 1.6.0: one of the game's runestones once a world's places are loaded
            Log.Info("memorial stone registered");
        }

        /// <summary>
        /// The field marker (worker polish, 0.20): the game's wooden sign, which shows its field's crop, inside a colony's area
        /// (the Town Hall is its station, as for the huts). The sign's own component is taken off, so E opens the field's
        /// window instead of the sign's text box.
        /// </summary>
        private static void RegisterFieldMarker(CraftingStation hallStation)
        {
            ConfigEntry<int> woodCost = ColonyConfig.FieldWoodCost;
            int wood = woodCost != null ? woodCost.Value : 0;
            var config = new PieceConfig
            {
                Name = "$vc_field_marker",
                Description = "$vc_field_marker_desc",
                PieceTable = PieceTables.Hammer,
                Category = PieceCategories.Misc,
                CraftingStation = CraftingStations.Workbench,
            };
            if (wood > 0) config.AddRequirement("Wood", wood);
            var piece = new CustomPiece(SaveKeys.FieldMarkerPiece, "sign", config);
            if (piece.PiecePrefab == null)
            {
                Log.Error("Could not clone 'sign'; the field marker is disabled.");
                return;
            }
            if (wood <= 0) piece.Piece.m_resources = new Piece.Requirement[0];
            Sign sign = piece.PiecePrefab.GetComponent<Sign>();
            Colony.FieldMarker marker = piece.PiecePrefab.AddComponent<Colony.FieldMarker>();
            if (sign != null)
            {
                marker.TextWidget = sign.m_textWidget;
                Object.DestroyImmediate(sign);
            }
            // Needs no support, as the huts' chests: the farmer cultivates and levels the ground round it, and a sign left
            // hanging broke (worker test in the full run: the first field's marker was gone a minute after its field was sown).
            WearNTear wear = piece.PiecePrefab.GetComponent<WearNTear>();
            if (wear != null) wear.m_noSupportWear = false;
            piece.Piece.m_craftingStation = hallStation;
            InMiscTab(piece);
            PieceManager.Instance.AddPiece(piece);
            s_costed.Add(new Costed { Piece = piece, Wood = woodCost, Stone = null });
            if (woodCost != null) woodCost.SettingChanged += (sender, e) => ApplyCosts();
            Log.Info("field marker registered");
        }

        /// <summary>A registered building and the settings its cost comes from.</summary>
        private sealed class Costed
        {
            public CustomPiece Piece;
            public ConfigEntry<int> Wood, Stone;
        }

        private static readonly List<Costed> s_costed = new List<Costed>();

        /// <summary>Each building's cost set again from its settings (as the server has them, on a server).</summary>
        internal static void ApplyCosts()
        {
            foreach (Costed costed in s_costed)
            {
                if (costed.Piece == null || costed.Piece.Piece == null) continue;
                var needs = new List<Piece.Requirement>();
                // An item that can't be found now keeps the building as it was, rather than making it free.
                if (!Need(needs, "Wood", costed.Wood != null ? costed.Wood.Value : 0) || !Need(needs, "Stone", costed.Stone != null ? costed.Stone.Value : 0))
                    continue;
                costed.Piece.Piece.m_resources = needs.ToArray();
            }
        }

        /// <summary>Adds the requirement; false when the item can't be found (Jötunn's cache of the game's prefabs).</summary>
        private static bool Need(List<Piece.Requirement> needs, string item, int amount)
        {
            if (amount <= 0) return true;
            ItemDrop drop = PrefabManager.Cache.GetPrefab<ItemDrop>(item);
            if (drop == null) return false;
            needs.Add(new Piece.Requirement { m_resItem = drop, m_amount = amount, m_recover = true });
            return true;
        }

        /// <summary>
        /// The build menu's Misc tab (1.5.1, the player's 1.5.0 try: the huts were in Furniture): Valheim 1.0's menu files a
        /// piece by its usage tags, which a cloned chest, sign or pillar keeps; the old category alone no longer decides.
        /// </summary>
        private static void InMiscTab(CustomPiece piece)
        {
            if (piece?.Piece != null) piece.Piece.m_usage = Piece.UsageTagFlags.Misc;
        }

        private static CustomPiece Register(BuildingType type, string basePiece, string descriptionToken, int width, int height,
            ConfigEntry<int> woodCost, ConfigEntry<int> stoneCost)
        {
            int wood = woodCost != null ? woodCost.Value : 0, stone = stoneCost != null ? stoneCost.Value : 0;
            var config = new PieceConfig
            {
                Name = type.NameToken,
                Description = descriptionToken,
                PieceTable = PieceTables.Hammer,
                Category = PieceCategories.Misc,
                CraftingStation = CraftingStations.Workbench,
            };
            if (wood > 0) config.AddRequirement("Wood", wood);
            if (stone > 0) config.AddRequirement("Stone", stone);

            var piece = new CustomPiece(type.PrefabName, basePiece, config);
            if (piece.PiecePrefab == null)
            {
                Log.Error($"Could not clone '{basePiece}'; {type.Id} is disabled.");
                return null;
            }
            // With no requirements added, Jötunn keeps the base piece's recipe; a free building must clear it.
            if (wood <= 0 && stone <= 0) piece.Piece.m_resources = new Piece.Requirement[0];

            Container container = piece.PiecePrefab.GetComponent<Container>();
            container.m_name = type.NameToken;
            container.m_width = width;
            container.m_height = height;
            AddModules(piece.PiecePrefab, type);
            if (type.Id != BuildingTypes.TownHallId)
            {
                // A hut's chest needs no support: a raised hut floats on its spot, and digging its site doesn't break it.
                WearNTear wear = piece.PiecePrefab.GetComponent<WearNTear>();
                if (wear != null) wear.m_noSupportWear = false;
            }

            InMiscTab(piece);
            PieceManager.Instance.AddPiece(piece);
            s_costed.Add(new Costed { Piece = piece, Wood = woodCost, Stone = stoneCost });
            if (woodCost != null) woodCost.SettingChanged += (sender, e) => ApplyCosts();
            if (stoneCost != null) stoneCost.SettingChanged += (sender, e) => ApplyCosts();
            Log.Info($"{type.Id} registered");
            return piece;
        }

        private static void AddModules(GameObject prefab, BuildingType type)
        {
            prefab.AddComponent<StorageModule>();
            prefab.AddComponent<StockModule>();
            ColonyBuilding building = prefab.AddComponent<ColonyBuilding>();
            building.TypeId = type.Id;
            if (type.Id == BuildingTypes.TownHallId)
            {
                prefab.AddComponent<TownHallModule>();
                prefab.AddComponent<Construction.ConstructionModule>();   // its camp (huts round 2), when there is a design for it
                AddStation(prefab, type.NameToken, ColonyRules.HutBuildRange(ColonyConfig.ColonyRadius.Value), ColonyConfig.ColonyRadius.Value);
            }
            if (type.Id != BuildingTypes.TownHallId)
            {
                prefab.AddComponent<Construction.ConstructionModule>();
                prefab.AddComponent<HutMembership>();
            }
            if (!string.IsNullOrEmpty(type.JobId)) prefab.AddComponent<WorkerModule>();
            if (type.Id == BuildingTypes.LonghouseId) prefab.AddComponent<ResidenceModule>();
            if (type.Id == BuildingTypes.FarmId) prefab.AddComponent<FieldsModule>();
            if (type.Id == BuildingTypes.StorehouseId)
            {
                prefab.AddComponent<StorehouseModule>();   // its store (1.5)
                prefab.AddComponent<CourierDispatch>();    // its couriers' plan (1.5)
            }
            if (type.Id == BuildingTypes.MeadHallId) prefab.AddComponent<MeadHallModule>();   // its stations and its cook (1.6)
            if (type.Id == BuildingTypes.WatchtowerId)
            {
                // The colony's own station, so the game lets huts be placed by the tower; its range is set per tower (1.1).
                AddStation(prefab, BuildingTypes.TownHall.NameToken, 0f, 0f);
                prefab.AddComponent<WatchtowerModule>();
            }
        }

        /// <summary>
        /// Makes the Town Hall (and the Watchtower, 1.1) a building station like the workbench: pieces that require it can
        /// only be built within the colony area. Added after the Container, so E still opens the storage (see
        /// BuildingInteractPatches). The range is refreshed per building (TownHallModule, WatchtowerModule).
        /// </summary>
        private static void AddStation(GameObject prefab, string name, float range, float discoverRange)
        {
            CraftingStation station = prefab.AddComponent<CraftingStation>();
            station.m_name = name;
            station.m_icon = prefab.GetComponent<Piece>().m_icon;
            station.m_craftRequireRoof = false;
            station.m_craftRequireFire = false;
            station.m_showBasicRecipies = false;
            station.m_rangeBuild = range;
            station.m_discoverRange = discoverRange;
        }
    }
}
