using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using Jotunn.Managers;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Core;

namespace VikingColonies.Construction
{
    /// <summary>
    /// Each hut's design for each level: the player's file BepInEx/config/VikingColonies/blueprints/&lt;hut&gt;.blueprint
    /// (level 1) or &lt;hut&gt;_&lt;level&gt;.blueprint (levels 2–5) when it reads and marks where the chest goes, otherwise the
    /// placeholder built into the mod.
    /// </summary>
    internal static class BlueprintLibrary
    {
        private static readonly Dictionary<string, BlueprintFile> s_cache = new Dictionary<string, BlueprintFile>();
        private static readonly Dictionary<string, string> s_sources = new Dictionary<string, string>();
        private static readonly HashSet<string> s_unknownWarned = new HashSet<string>();

        public static string UserFolder => Path.Combine(Paths.ConfigPath, Path.Combine("VikingColonies", "blueprints"));

        /// <returns>The level-1 design (see the level overload).</returns>
        public static BlueprintFile For(BuildingType type, out string source) => For(type, 1, out source);

        /// <returns>The hut's design for <paramref name="level"/>, or null when neither the player's file nor the placeholder can be used.</returns>
        public static BlueprintFile For(BuildingType type, int level, out string source)
        {
            string file = BlueprintNames.FileName(type.Id, level);
            if (!s_cache.TryGetValue(file, out BlueprintFile blueprint))
            {
                blueprint = LoadUser(type, file, out string from) ?? LoadBuiltIn(type, file, out from);
                s_cache[file] = blueprint;
                s_sources[file] = from;
            }
            s_sources.TryGetValue(file, out source);
            return blueprint;
        }

        /// <summary>What each piece of a design costs (its crafting station and materials), for the era warning.</summary>
        public static List<PieceCost> CostsOf(BlueprintFile blueprint)
        {
            var costs = new List<PieceCost>();
            foreach (BlueprintPiece line in blueprint.Pieces)
            {
                if (line.Name.StartsWith("vc_")) continue;
                GameObject prefab = PrefabManager.Instance.GetPrefab(line.Name);
                Piece piece = prefab != null ? prefab.GetComponent<Piece>() : null;
                if (piece == null) continue;
                string station = piece.m_craftingStation != null ? Utils.GetPrefabName(piece.m_craftingStation.gameObject) : "";
                IEnumerable<string> materials = (piece.m_resources ?? new Piece.Requirement[0])
                    .Where(r => r.m_resItem != null)
                    .Select(r => Utils.GetPrefabName(r.m_resItem.gameObject));
                costs.Add(new PieceCost(station, materials));
            }
            return costs;
        }

        /// <summary>Reads the file again next time (after vc_blueprint save).</summary>
        public static void Forget(string file)
        {
            s_cache.Remove(file);
            s_sources.Remove(file);
        }

        public static string Save(string name, BlueprintFile blueprint)
        {
            Directory.CreateDirectory(UserFolder);
            string path = Path.Combine(UserFolder, name + ".blueprint");
            File.WriteAllLines(path, blueprint.ToLines().ToArray());
            return path;
        }

        private static BlueprintFile LoadUser(BuildingType type, string file, out string source)
        {
            source = null;
            string path = Path.Combine(UserFolder, file + ".blueprint");
            if (!File.Exists(path)) return null;
            BlueprintFile blueprint;
            try
            {
                blueprint = BlueprintFile.Parse(File.ReadAllLines(path));
            }
            catch (IOException e)
            {
                Log.Warn($"Couldn't read {path} ({e.Message}); using the built-in {file} blueprint.");
                return null;
            }
            if (!Usable(type, blueprint, path)) return null;
            source = path;
            return blueprint;
        }

        private static BlueprintFile LoadBuiltIn(BuildingType type, string file, out string source)
        {
            source = "built-in";
            string resource = "VikingColonies.Blueprints." + file + ".blueprint";
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            {
                if (stream == null) return null;
                var lines = new List<string>();
                using (var reader = new StreamReader(stream))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null) lines.Add(line);
                }
                BlueprintFile blueprint = BlueprintFile.Parse(lines);
                return Usable(type, blueprint, resource) ? blueprint : null;
            }
        }

        private static bool Usable(BuildingType type, BlueprintFile blueprint, string where)
        {
            foreach (string problem in blueprint.Problems) Log.Warn($"{where}: {problem} (skipped)");
            if (!blueprint.Pieces.Any(p => p.Name == type.PrefabName))
            {
                Log.Warn($"{where} has no {type.PrefabName} line marking where the chest goes; it can't be used for a {type.Id}.");
                return false;
            }
            foreach (string name in blueprint.Pieces.Select(p => p.Name).Where(n => !n.StartsWith("vc_")).Distinct())
                if (PrefabManager.Instance.GetPrefab(name) == null && s_unknownWarned.Add(name))
                    Log.Warn($"{where}: the game has no piece called '{name}' (from a mod that isn't installed?); it will be skipped.");
            return true;
        }

        /// <summary>Logs the size of every piece the placeholders use, measured from the game's colliders.</summary>
        public static void LogPlaceholderShapes()
        {
            foreach (BuildingType type in BuildingTypes.All.Where(t => t.Id != BuildingTypes.TownHallId))
            for (int level = 1; level <= HutLevels.MaxLevel; level++)
            {
                BlueprintFile blueprint = LoadBuiltIn(type, BlueprintNames.FileName(type.Id, level), out _);
                if (blueprint == null)
                {
                    Log.Warn($"No usable built-in blueprint for {type.Id} level {level}");
                    continue;
                }
                foreach (string name in blueprint.Pieces.Select(p => p.Name).Where(n => !n.StartsWith("vc_")).Distinct())
                {
                    GameObject prefab = PrefabManager.Instance.GetPrefab(name);
                    if (prefab == null) continue;
                    Bounds b = PieceShape.Of(prefab).Extents;
                    Log.Info($"Piece shape {name}: from {b.min:F2} to {b.max:F2}");
                }
            }
        }
    }
}
