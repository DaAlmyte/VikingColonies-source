using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Core;

namespace VikingColonies.Jobs
{
    /// <summary>
    /// The Farm's "What to grow" list: the crops from the game's own Cultivator plants (what a seed grows into, where it
    /// grows), read the first time it's needed, and a Farm's unticked crops.
    /// </summary>
    internal static class CropList
    {
        private static List<CropInfo> s_crops;
        private static HashSet<string> s_grown;

        public static IReadOnlyList<CropInfo> Crops => Build() ? s_crops : new List<CropInfo>();

        /// <summary>The crops' grown prefabs ("Pickable_Carrot"), to tell a crop from a wild bush.</summary>
        public static ICollection<string> GrownPrefabs => Build() ? s_grown : new HashSet<string>();

        public static List<CropLine> For(ColonyBuilding farm) =>
            CropChoice.Lines(Crops, ReplantList.HutBiome(farm), Unticked(farm));

        public static HashSet<string> Unticked(ColonyBuilding farm)
        {
            WorkerModule worker = farm != null ? farm.GetComponent<WorkerModule>() : null;
            return worker != null ? worker.GrowUnticked : new HashSet<string>();
        }

        /// <summary>Materials a planting mod also makes plantable: not for a Farm.</summary>
        private static readonly HashSet<string> s_notCrops = new HashSet<string> { "Wood", "Stone", "Flint" };

        /// <summary>
        /// A bush, mushroom or flower a planting mod (PlantEverything) lets the Cultivator place as it is, not as a sapling:
        /// the piece is itself a pickable that regrows, and costs some of what it gives (5 raspberries). The farmer plants it
        /// once and picks it each time it regrows (0.19: they never came onto the list). Null for anything else.
        /// </summary>
        private static PlantInfo Bush(GameObject pieceObject, Piece piece)
        {
            Pickable pickable = pieceObject != null ? pieceObject.GetComponent<Pickable>() : null;
            if (pickable == null || piece == null || pieceObject.GetComponent<Plant>() != null || pickable.m_itemPrefab == null) return null;
            if (pickable.m_respawnTimeMinutes <= 0f || piece.m_resources == null || piece.m_resources.Length == 0 || piece.m_resources[0].m_resItem == null) return null;
            string yield = pickable.m_itemPrefab.name;
            if (s_notCrops.Contains(yield)) return null;
            ItemDrop yieldDrop = pickable.m_itemPrefab.GetComponent<ItemDrop>();
            string name = yieldDrop != null ? yieldDrop.m_itemData.m_shared.m_name : piece.m_name;
            bool food = yieldDrop != null && yieldDrop.m_itemData.m_shared.m_food > 0f;
            return new PlantInfo(pieceObject.name, piece.m_resources[0].m_resItem.name, name, pieceObject.name, yield, true, 0, 0.5f,
                isFood: food, seedCount: piece.m_resources[0].m_amount);
        }

        private static bool Build()
        {
            if (s_crops != null) return true;
            if (ObjectDB.instance == null || ZNetScene.instance == null) return false;
            GameObject cultivator = ObjectDB.instance.GetItemPrefab("Cultivator");
            PieceTable table = cultivator != null ? cultivator.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces : null;
            if (table == null) return false;
            var plants = new List<PlantInfo>();
            foreach (GameObject pieceObject in table.m_pieces)
            {
                Plant plant = pieceObject != null ? pieceObject.GetComponent<Plant>() : null;
                Piece piece = pieceObject != null ? pieceObject.GetComponent<Piece>() : null;
                PlantInfo bush = Bush(pieceObject, piece);
                if (bush != null) plants.Add(bush);
                if (plant == null || piece == null || plant.m_grownPrefabs == null || piece.m_resources.Length == 0 || piece.m_resources[0].m_resItem == null) continue;
                GameObject grown = plant.m_grownPrefabs.FirstOrDefault(g => g != null);
                Pickable crop = grown != null ? grown.GetComponent<Pickable>() : null;
                string yield = crop != null && crop.m_itemPrefab != null ? crop.m_itemPrefab.name : "";
                ItemDrop yieldDrop = crop != null && crop.m_itemPrefab != null ? crop.m_itemPrefab.GetComponent<ItemDrop>() : null;
                string name = yieldDrop != null ? yieldDrop.m_itemData.m_shared.m_name : piece.m_name;
                bool food = yieldDrop != null && yieldDrop.m_itemData.m_shared.m_food > 0f;
                plants.Add(new PlantInfo(pieceObject.name, piece.m_resources[0].m_resItem.name, name, grown != null ? grown.name : "", yield,
                    crop != null, (int)plant.m_biome, plant.m_growRadius, isVine: plant.m_attachDistance > 0f || plant.m_growRadiusVines > 0f, isFood: food));
            }
            s_crops = CropCatalog.Crops(plants).ToList();
            // Every form a crop can grow into (the game picks one at random), not only the first (review of 0.12).
            s_grown = new HashSet<string>(s_crops.Where(c => c.SeedCount > 1 || c.Grown == c.Piece).Select(c => c.Grown));   // a bush is its own crop
            foreach (GameObject pieceObject in table.m_pieces)
            {
                Plant plant = pieceObject != null ? pieceObject.GetComponent<Plant>() : null;
                if (plant == null || plant.m_grownPrefabs == null || s_crops.All(c => c.Piece != pieceObject.name)) continue;
                foreach (GameObject form in plant.m_grownPrefabs)
                    if (form != null && form.GetComponent<Pickable>() != null) s_grown.Add(form.name);
            }
            Log.Info($"Crop list: {s_crops.Count} crops ({string.Join(", ", s_crops.Select(c => c.Piece))})");
            return true;
        }
    }
}
