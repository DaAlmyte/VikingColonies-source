using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jotunn.Entities;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Construction;
using VikingColonies.Core;
using static VikingColonies.Construction.CoreVectors;

namespace VikingColonies.DevTools
{
    /// <summary>
    /// vc_stamp (the hut gallery, 2026-10-21; the player: "can you add all the town hall camp stages to the gallery just like
    /// all the other builds", "you dont have to place the town hall chest"): a level of a design put up at once at a spot,
    /// without its chest, so no colony, settlers or build order come with it. The gallery's one Town Hall shows its camp at
    /// level 5; a row of stamps shows levels 1 to 5 side by side. Its pieces carry the gallery's mark (vc_build costchest's),
    /// so vc_world tidy and wipe and 'vc_build costchest clear' take them, and no hut's mark, so no colony counts them as
    /// anyone's building. Test worlds only.
    /// </summary>
    internal sealed class StampCommand : ConsoleCommand
    {
        public override string Name => "vc_stamp";
        public override string Help => "VikingColonies (test worlds): 'vc_stamp <hut> <level> <x> <z> [turn]' puts that level of the hut's design up at " +
                                       "once round x z (its chest's spot, on the ground; turn 0 = its front to the north) without the chest: no colony, " +
                                       "settlers or build order; its fires lit, and a black metal chest of what that level cost the builder with a sign " +
                                       "beyond its front right corner. 'vc_stamp stands' lists, for each design stamped since the game started, its " +
                                       "pieces that don't stand; 'vc_stamp mend' puts them back up";
        public override bool IsCheat => true;

        private sealed class Stamp
        {
            public string Label;
            public Vector3 Origin;
            public Quaternion Rotation;
            public List<PlannedPiece> Plan;
        }

        private static readonly List<Stamp> s_stamps = new List<Stamp>();

        public override void Run(string[] args)
        {
            if (DevHelpers.RefuseOutsideTestWorld(Name)) return;   // it changes the world
            args = args ?? new string[0];
            string first = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            if (first == "stands")
            {
                Stands();
                return;
            }
            if (first == "mend")
            {
                BuildCommand.Print($"vc_stamp: put {Mend()} missing pieces back up");
                return;
            }
            BuildingType type = args.Length > 0 ? DevHelpers.TypeByWord(args[0]) : null;
            if (type == null || args.Length < 4 || !int.TryParse(args[1], out int level) || !Number(args[2], out float x) || !Number(args[3], out float z))
            {
                BuildCommand.Print("vc_stamp: 'vc_stamp <hut> <level> <x> <z> [turn]', 'vc_stamp stands' or 'vc_stamp mend'");
                return;
            }
            float turn = args.Length > 4 && Number(args[4], out float t) ? t : 0f;
            BuildCommand.Print(Put(type, level, x, z, turn));
        }

        private static bool Number(string word, out float value) =>
            float.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        private static string Put(BuildingType type, int level, float x, float z, float turn)
        {
            var origin = new Vector3(x, 0f, z);
            if (ZNetScene.instance == null || !ZNetScene.instance.IsAreaReady(origin) || Heightmap.FindHeightmap(origin) == null)
                return $"vc_stamp: the ground at ({x:0.#}, {z:0.#}) isn't loaded: go nearer first (a stamp there stood at height 0)";
            origin.y = ZoneSystem.instance.GetGroundHeight(origin);
            Quaternion rotation = Quaternion.Euler(0f, turn, 0f);
            BlueprintFile blueprint = BlueprintLibrary.For(type, level, out _);
            if (blueprint == null || !BlueprintPlan.TryPlace(blueprint, type.PrefabName, V(origin), Q(rotation), out List<PlannedPiece> plan, out string problem))
                return $"vc_stamp: {type.Id} has no usable design for level {level}";
            int placed = 0;
            foreach (PlannedPiece piece in plan.Where(p => p.IsValid))
            {
                GameObject prefab = PiecePlacer.Prefab(piece.Prefab);
                if (prefab == null)
                {
                    BuildCommand.Print($"vc_stamp: the game has no piece called {piece.Prefab}");
                    continue;
                }
                Lay(prefab, piece);
                placed++;
            }
            int earth = GroundShaper.Ready ? ConstructionModule.PaintEarth(plan, origin, rotation) : 0;   // as a finished hut's
            string name = Localization.instance.Localize(type.NameToken);
            string label = type.Id == BuildingTypes.TownHallId ? $"{name} camp, level {level}" : $"{name}, level {level}";
            s_stamps.Add(new Stamp { Label = label, Origin = origin, Rotation = rotation, Plan = plan });
            ConstructionModule.PlanReach(plan, origin, rotation, out float maxX, out float maxZ);
            string chest = BuildCommand.CostChestAt(origin, rotation, maxX, maxZ, ConstructionModule.LevelCost(type, level, plan, origin, rotation),
                label, "vc_stamp costchest");
            return string.Format(CultureInfo.InvariantCulture, "vc_stamp: {0}: {1} of {2} pieces put up round ({3:0.#}, {4:0.#}), its earth painted at {5} spots (-1: its ground not loaded)\n{6}",
                label, placed, plan.Count, x, z, earth, chest);
        }

        /// <summary>A piece of a stamp: no creator and no hut, the gallery's mark; a fire or torch lit (full of fuel).</summary>
        private static void Lay(GameObject prefab, PlannedPiece piece)
        {
            GameObject go = PiecePlacer.Place(prefab, U(piece.Position), U(piece.Rotation), "", 0L);
            BuildCommand.MarkCostChest(go);
            Fireplace fire = go.GetComponent<Fireplace>();
            ZNetView view = go.GetComponent<ZNetView>();
            if (fire != null && !fire.m_infiniteFuel && view != null && view.IsValid()) view.GetZDO().Set(ZDOVars.s_fuel, fire.m_maxFuel);
        }

        private static List<Piece> Near(Stamp s)
        {
            float radius = 1f;
            foreach (PlannedPiece p in s.Plan)
                if (p.IsValid) radius = Mathf.Max(radius, Vector3.Distance(U(p.Position), s.Origin) + 1f);
            var near = new List<Piece>();
            Piece.GetAllPiecesInRadius(s.Origin, radius, near);
            return near;
        }

        /// <summary>vc_stamp stands: as 'vc_build stands', for every design stamped since the game started.</summary>
        private static void Stands()
        {
            if (s_stamps.Count == 0)
            {
                BuildCommand.Print("vc_stamp stands: nothing stamped since the game started");
                return;
            }
            foreach (Stamp s in s_stamps)
            {
                List<Piece> near = Near(s);
                Quaternion undo = Quaternion.Inverse(s.Rotation);
                int total = 0, standing = 0;
                var missing = new List<string>();
                foreach (PlannedPiece p in s.Plan.Where(p => p.IsValid))
                {
                    total++;
                    if (PiecePlacer.FindStanding(p, near) != null)
                    {
                        standing++;
                        continue;
                    }
                    Vector3 at = undo * (U(p.Position) - s.Origin);
                    missing.Add(string.Format(CultureInfo.InvariantCulture, "{0} at ({1:0.##}, {2:0.##}, {3:0.##})", p.Prefab, at.x, at.y, at.z));
                }
                BuildCommand.Print($"vc_stamp stands: {s.Label}: {standing} of {total} pieces stand");
                foreach (string m in missing) BuildCommand.Print($"vc_stamp stands: missing {m} ({s.Label})");
            }
        }

        /// <summary>vc_stamp mend: every stamped piece that doesn't stand goes back up (as 'vc_build mend').</summary>
        private static int Mend()
        {
            int put = 0;
            foreach (Stamp s in s_stamps)
            {
                List<Piece> near = Near(s);
                foreach (PlannedPiece p in s.Plan.Where(p => p.IsValid && PiecePlacer.FindStanding(p, near) == null))
                {
                    GameObject prefab = PiecePlacer.Prefab(p.Prefab);
                    if (prefab == null) continue;
                    Lay(prefab, p);
                    put++;
                }
            }
            return put;
        }
    }
}
