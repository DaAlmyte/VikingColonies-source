using System.Linq;
using UnityEngine;
using VikingColonies.Core;
using Object = UnityEngine.Object;

namespace VikingColonies.Construction
{
    /// <summary>
    /// Changes the ground like the game's hoe. The game sends only the name of a ground operation over the network (each
    /// computer looks its settings up in its own item database), so the mod registers three operations of its own with the
    /// game, on every computer, made from the hoe's level ground: <see cref="LevelName"/> levels a circle to its own
    /// height (digging or filling), <see cref="SmoothName"/> blends the ground towards its height (the edge of a
    /// flattened site) and <see cref="EarthName"/> paints it as trodden earth, as the hoe's path does, without moving it
    /// (a building's earth floor: <see cref="EarthFloor"/>). The hoe is found among the game's items, so a modded hoe works
    /// too; raising ground costs what the hoe's raise ground costs.
    /// </summary>
    internal static class GroundShaper
    {
        public const string LevelName = "vc_ground_level";
        public const string SmoothName = "vc_ground_smooth";
        public const string EarthName = "vc_ground_earth";
        /// <summary>
        /// How far the earth paint reaches round its spot, and how far apart its spots are (a 1.5 m block each, its
        /// corners within the reach): little past the roof's edge (the review of 2026-10-21: at 1.5 m round spots 2 m
        /// apart it reached half a metre past it).
        /// </summary>
        public const float EarthRadius = 1.1f;
        public const float EarthSpacing = 1.5f;
        /// <summary>How far the smoothing reaches around its spot.</summary>
        public const float SmoothRadius = 1.5f;
        private const float DefaultStonePerCubicMetre = 1f;
        private static GameObject s_holder;
        private static GameObject s_level;
        private static GameObject s_smooth;
        private static GameObject s_earth;
        private static bool s_warned;
        private static float s_stoneRate = DefaultStonePerCubicMetre;
        private static string s_stoneItem = "Stone";

        /// <summary>The operations are registered with this game: the builder can flatten sites.</summary>
        public static bool Ready
        {
            get
            {
                Register();
                return s_level != null && ObjectDB.instance != null && ObjectDB.instance.m_terrainOpsByHash.ContainsKey(LevelName.GetStableHashCode());
            }
        }

        /// <summary>Stone the game charges per cubic metre of raised ground.</summary>
        public static float StonePerCubicMetre
        {
            get
            {
                Register();
                return s_stoneRate;
            }
        }

        /// <summary>The item raising ground costs (stone in the base game).</summary>
        public static string StoneItem
        {
            get
            {
                Register();
                return s_stoneItem;
            }
        }

        /// <summary>A hoe's list of pieces: it can level (or smooth) ground and raise it.</summary>
        public static bool IsGroundTable(PieceTable table) =>
            table != null && table.m_pieces != null && table.m_pieces.Any(IsLevelPiece) && table.m_pieces.Any(IsRaisePiece);

        /// <summary>
        /// Adds the mod's operations to the game's item database. Called whenever the game loads its items (the main menu,
        /// then a world) and before each use; every computer in a game does it, so each can apply them.
        /// </summary>
        public static void Register()
        {
            ObjectDB db = ObjectDB.instance;
            if (db == null || db.m_items.Count == 0) return;
            if (s_level == null && !Create(db)) return;
            Add(db, s_level);
            Add(db, s_smooth);
            Add(db, s_earth);
        }

        /// <summary>Levels the ground within <see cref="GroundPlan.LevelRadius"/> to the height of <paramref name="at"/>.</summary>
        public static void Level(Vector3 at) => Apply(s_level, at);

        /// <summary>Blends the ground within <see cref="SmoothRadius"/> towards the height of <paramref name="at"/>.</summary>
        public static void Smooth(Vector3 at) => Apply(s_smooth, at);

        /// <summary>Paints the ground within <see cref="EarthRadius"/> of <paramref name="at"/> as trodden earth (no grass), leaving its height.</summary>
        public static void Earth(Vector3 at) => Apply(s_earth, at);

        /// <summary>
        /// Whether this computer changes the ground here itself: it owns the ground's changes there, or nobody has changed
        /// it yet (the first change makes them this computer's). The game sends a ground operation to the owner of the
        /// ground's changes by name; an owner without the mod's earth operation (an older version) would drop it.
        /// </summary>
        public static bool OwnsGround(Vector3 at)
        {
            TerrainComp ground = TerrainComp.FindTerrainCompiler(at);
            if (ground == null) return true;
            ZNetView view = ground.GetComponent<ZNetView>();
            return view != null && view.IsValid() && view.IsOwner();
        }

        /// <summary>Cultivates the ground here as the Cultivator does (the farmer; vc_ui cultivate). False without one in the game.</summary>
        public static bool Cultivate(Vector3 at) => Paint(at, TerrainModifier.PaintType.Cultivate);

        /// <summary>Test tool: grass again where the ground was cultivated, as the Cultivator's grass does (the self-test's field).</summary>
        public static bool Grass(Vector3 at) => Paint(at, TerrainModifier.PaintType.Reset);

        private static bool Paint(Vector3 at, TerrainModifier.PaintType paint)
        {
            GameObject cultivator = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab("Cultivator") : null;
            PieceTable table = cultivator != null ? cultivator.GetComponent<ItemDrop>().m_itemData.m_shared.m_buildPieces : null;
            GameObject op = table != null ? table.m_pieces.FirstOrDefault(piece => Settings(piece) is TerrainOp.Settings s && s.m_paintType == paint) : null;
            if (op == null) return false;
            var holder = new GameObject("vc_ground_op");
            holder.SetActive(false);
            try
            {
                GameObject placed = Object.Instantiate(op, at, Quaternion.identity, holder.transform);
                placed.transform.SetParent(null, true);
            }
            finally
            {
                Object.Destroy(holder);
            }
            return true;
        }

        private static void Apply(GameObject operation, Vector3 at)
        {
            if (!Ready || operation == null) return;
            var holder = new GameObject("vc_ground_op");
            holder.SetActive(false);
            try
            {
                GameObject op = Object.Instantiate(operation, at, Quaternion.identity, holder.transform);   // asleep under the holder
                op.transform.SetParent(null, true);   // wakes up: asks the terrain's owner to change the ground, then removes itself
            }
            finally
            {
                Object.Destroy(holder);
            }
        }

        private static void Add(ObjectDB db, GameObject prefab)
        {
            TerrainOp op = prefab.GetComponent<TerrainOp>();
            if (!db.m_terrainOps.Contains(op)) db.m_terrainOps.Add(op);
            db.m_terrainOpsByHash[prefab.name.GetStableHashCode()] = op;
        }

        private static bool Create(ObjectDB db)
        {
            foreach (GameObject item in db.m_items)
            {
                ItemDrop drop = item != null ? item.GetComponent<ItemDrop>() : null;
                PieceTable table = drop != null ? drop.m_itemData.m_shared.m_buildPieces : null;
                if (!IsGroundTable(table)) continue;
                GameObject level = table.m_pieces.Where(IsLevelPiece)
                    .OrderBy(p => Settings(p).m_paintType == TerrainModifier.PaintType.Paved ? 1 : 0)   // plain level ground before paving
                    .First();
                GameObject raise = table.m_pieces.First(IsRaisePiece);
                s_holder = new GameObject("vc_ground_ops");
                s_holder.SetActive(false);   // the operations live here asleep: they only act when copied out
                Object.DontDestroyOnLoad(s_holder);
                s_level = Make(level, LevelName, s =>
                {
                    s.m_level = true;
                    s.m_levelRadius = GroundPlan.LevelRadius;
                    s.m_levelOffset = 0f;
                    s.m_square = false;
                    s.m_raise = false;
                    s.m_smooth = false;
                    if (s.m_paintCleared) s.m_paintRadius = GroundPlan.LevelRadius;
                });
                s_smooth = Make(level, SmoothName, s =>
                {
                    s.m_level = false;
                    s.m_levelOffset = 0f;
                    s.m_square = false;
                    s.m_raise = false;
                    s.m_smooth = true;
                    s.m_smoothRadius = SmoothRadius;
                    s.m_smoothPower = 1f;
                    s.m_paintCleared = false;
                });
                s_earth = Make(level, EarthName, s =>
                {
                    s.m_level = false;
                    s.m_levelOffset = 0f;
                    s.m_raise = false;
                    s.m_smooth = false;
                    s.m_square = false;
                    s.m_paintCleared = true;
                    s.m_paintHeightCheck = false;
                    s.m_paintType = TerrainModifier.PaintType.Dirt;
                    s.m_paintRadius = EarthRadius;
                });
                // Quiet, and nothing left lying: the hoe's own sound and dust, and the stones its levelling turns up, 30
                // to 100 times over when a building's earth is painted (the review of 2026-10-21).
                TerrainOp earth = s_earth.GetComponent<TerrainOp>();
                earth.m_onPlacedEffect = new EffectList();
                earth.m_spawnOnPlaced = null;
                MeasureStone(raise);
                Log.Info($"Ground shaping: '{LevelName}', '{SmoothName}' and '{EarthName}' made from the {item.name}'s '{level.name}'; raising ground costs about " +
                         $"{s_stoneRate:0.00} {s_stoneItem} per cubic metre (from '{raise.name}')");
                return true;
            }
            if (!s_warned) Log.Warn("No hoe with level and raise ground found: builders build on the ground as it is.");
            s_warned = true;
            return false;
        }

        private static GameObject Make(GameObject template, string name, System.Action<TerrainOp.Settings> change)
        {
            GameObject prefab = Object.Instantiate(template, s_holder.transform);   // its own copy of the settings
            prefab.name = name;
            change(prefab.GetComponent<TerrainOp>().m_settings);
            return prefab;
        }

        /// <summary>The raise ground piece's cost over the ground it adds (a mound: about half a cylinder of its radius and height).</summary>
        private static void MeasureStone(GameObject raise)
        {
            TerrainOp.Settings settings = Settings(raise);
            Piece piece = raise.GetComponent<Piece>();
            Piece.Requirement cost = piece != null && piece.m_resources != null
                ? piece.m_resources.FirstOrDefault(r => r.m_resItem != null && r.m_amount > 0)
                : null;
            if (settings == null || cost == null) return;
            float volume = Mathf.PI * settings.m_raiseRadius * settings.m_raiseRadius * settings.m_raiseDelta / 2f;
            if (volume < 0.01f) return;
            s_stoneItem = Utils.GetPrefabName(cost.m_resItem.gameObject);
            s_stoneRate = cost.m_amount / volume;
        }

        /// <summary>The hoe's level ground: in this game version it smooths towards its own height (the level switch is off).</summary>
        private static bool IsLevelPiece(GameObject piece) =>
            Settings(piece) is TerrainOp.Settings s && !s.m_raise && (s.m_level || s.m_smooth) && s.m_paintType != TerrainModifier.PaintType.Cultivate;

        private static bool IsRaisePiece(GameObject piece) => Settings(piece) is TerrainOp.Settings s && s.m_raise && s.m_raiseDelta > 0f;

        private static TerrainOp.Settings Settings(GameObject piece)
        {
            TerrainOp op = piece != null ? piece.GetComponent<TerrainOp>() : null;
            return op != null ? op.m_settings : null;
        }
    }
}
