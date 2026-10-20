using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jotunn.Entities;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Core;
using static VikingColonies.Construction.CoreVectors;

namespace VikingColonies.Construction
{
    /// <summary>vc_blueprint save: saves what's built around a hut's chest as a blueprint, anchored on that chest.</summary>
    internal sealed class BlueprintCommand : ConsoleCommand
    {
        private const float DefaultRadius = 10f;

        public override string Name => "vc_blueprint";
        public override string Help => "VikingColonies (devcommands): 'vc_blueprint save <hut> [level] [radius]' saves what's built around the hut chest you look at as that hut's design for a level (1-5, default 1), e.g. vc_blueprint save lumberjack_hut 3; another name saves a free blueprint";

        // Saving counts the hut as built, so it is a design tool for test worlds, not something any player may run.
        public override bool IsCheat => true;

        public override void Run(string[] args)
        {
            if (args == null || args.Length < 2 || !string.Equals(args[0], "save", System.StringComparison.OrdinalIgnoreCase))
            {
                BuildCommand.Print("Usage: vc_blueprint save <name> [level] [radius]");
                return;
            }
            string name = new string(args[1].Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray());
            if (name.Length == 0)
            {
                BuildCommand.Print("vc_blueprint: use letters, digits, _ or - in the name.");
                return;
            }
            int level = 1;
            if (args.Length > 2 && (!int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out level) || level < 1 || level > HutLevels.MaxLevel))
            {
                BuildCommand.Print($"vc_blueprint: the level must be 1 to {HutLevels.MaxLevel}.");
                return;
            }
            float radius = args.Length > 3 && float.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float r)
                ? Mathf.Clamp(r, 2f, 30f) : DefaultRadius;
            ConstructionModule site = BuildCommand.Target(Player.m_localPlayer, 5f);
            if (site == null)
            {
                BuildCommand.Print("vc_blueprint: look at a hut's chest, or stand within 5 m of one.");
                return;
            }
            string problem = BlueprintNames.SaveProblem(name, site.Building.TypeId, BuildingTypes.All.Select(t => t.Id));
            if (problem != null)
            {
                BuildCommand.Print("vc_blueprint: " + problem);
                return;
            }
            BlueprintFile blueprint = Capture(site.Building, radius, out List<string> fragile);
            string empty = BlueprintNames.EmptyProblem(blueprint.Pieces.Count - 1, radius);   // the first piece is the chest
            if (empty != null)
            {
                BuildCommand.Print("vc_blueprint: " + empty);
                return;
            }
            string file = BlueprintNames.FileName(name, level);
            blueprint.Name = file;
            string path = BlueprintLibrary.Save(file, blueprint);
            BlueprintLibrary.Forget(file);
            BuildCommand.Print($"vc_blueprint: saved {blueprint.Pieces.Count - 1} pieces around the {site.Building.TypeId} to {path} (level {level})");
            if (BuildingTypes.Get(name) != null)
            {
                string warning = EraTable.Warning(level, BlueprintLibrary.CostsOf(blueprint));
                if (warning != null) BuildCommand.Print("vc_blueprint: note: " + warning);
            }
            BuildOrderRecord order = site.Order;
            if (order != null && order.State != BuildState.Done)
            {
                site.RequestMarkBuilt(false);   // what was saved is this hut's building: the builder mustn't build another one into it
                BuildCommand.Print($"vc_blueprint: this {site.Building.TypeId} now counts as built.");
            }
            if (fragile.Count > 0)
                BuildCommand.Print($"vc_blueprint: these barely hold (support under 1.5 x their minimum): {string.Join(", ", fragile)}");
        }

        /// <summary>The pieces players (or the builder, for this hut) put up within the radius, relative to the chest.</summary>
        private static BlueprintFile Capture(ColonyBuilding hut, float radius, out List<string> fragile)
        {
            fragile = new List<string>();
            Vector3 origin = hut.transform.position;
            Quaternion undo = Quaternion.Inverse(hut.transform.rotation);
            var blueprint = new BlueprintFile
            {
                Creator = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : "",
                Description = $"Saved with vc_blueprint save around a {hut.TypeId}",
                Category = "VikingColonies",
            };
            string chest = hut.Type != null ? hut.Type.PrefabName : Utils.GetPrefabName(hut.gameObject);
            blueprint.Pieces.Add(new BlueprintPiece(chest, "Misc", Vec3.Zero, Quat.Identity, new Vec3(1, 1, 1), ""));
            var pieces = new List<Piece>();
            Piece.GetAllPiecesInRadius(origin, radius, pieces);
            foreach (Piece piece in pieces.Where(p => p != null).OrderBy(p => p.transform.position.y))
            {
                if (piece.GetComponent<ColonyBuilding>() != null) continue;   // huts' chests are buildings of their own
                string siteId = PiecePlacer.SiteOf(piece.gameObject);
                if (siteId.Length > 0 ? siteId != hut.Id : !piece.IsPlacedByPlayer()) continue;   // another hut's building, or nobody's
                string prefab = Utils.GetPrefabName(piece.gameObject);
                Vec3 local = V(undo * (piece.transform.position - origin));
                blueprint.Pieces.Add(new BlueprintPiece(prefab, "Building", local, Q(undo * piece.transform.rotation), new Vec3(1, 1, 1), ""));
                WearNTear wear = piece.GetComponent<WearNTear>();
                if (wear != null && SupportProbe.SupportOf(wear) < SupportProbe.MaterialOf(wear).MinSupport * 1.5f) fragile.Add($"{prefab} at {local}");
            }
            return blueprint;
        }
    }
}
