using System.Collections.Generic;
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
            RegisterFieldMarker(hallStation);
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
