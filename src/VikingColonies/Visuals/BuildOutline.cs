using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Construction;
using VikingColonies.Core;
using static VikingColonies.Construction.CoreVectors;

namespace VikingColonies.Visuals
{
    /// <summary>
    /// See-through outlines of buildings, while the player holds the hammer: the hut being placed, coloured by its site
    /// check, and the pieces still to come on unfinished sites within 50 m. Drawn on this computer only, with the pieces'
    /// own meshes.
    /// </summary>
    internal sealed class BuildOutline : MonoBehaviour
    {
        private const float SiteRange = 50f;
        private const int MaxMeshesPerFrame = 800;

        private struct Part
        {
            public Mesh Mesh;
            public Matrix4x4 Local;
        }

        private static readonly Dictionary<string, List<Part>> s_parts = new Dictionary<string, List<Part>>();
        private static readonly AccessTools.FieldRef<Player, PieceTable> s_buildPieces = AccessTools.FieldRefAccess<Player, PieceTable>("m_buildPieces");
        private Material _green;
        private Material _yellow;
        private Material _red;
        private Material _site;
        private bool _tried;
        private int _drawn;
        private PieceTable _lastTable;
        private bool _lastWasHammer;

        private void Update()
        {
            if (!Materials()) return;
            _drawn = 0;
            if (SitePreview.Active && SitePreview.Plan != null && SitePreview.Verdict != null)
                Draw(SitePreview.Plan, MaterialFor(SitePreview.Verdict.Colour), null);
            Player player = Player.m_localPlayer;
            if (player == null || !HammerOut(player)) return;   // unfinished sites show while the hammer is out
            Vector3 me = player.transform.position;
            foreach (ConstructionModule site in ConstructionModule.All)
            {
                if (site == null || !site.IsValid || Vector3.Distance(site.transform.position, me) > SiteRange) continue;
                BuildOrderRecord order = site.Order;
                if (order == null || order.State == BuildState.Done) continue;
                Draw(site.Plan, _site, order);
            }
        }

        /// <summary>The player holds the hammer that builds the colony's huts.</summary>
        private bool HammerOut(Player player)
        {
            PieceTable table = player.InPlaceMode() ? s_buildPieces(player) : null;   // the build tool in hand (a hammer isn't a weapon)
            if (table == null) return false;
            if (table != _lastTable)
            {
                _lastTable = table;
                _lastWasHammer = table.m_pieces != null && table.m_pieces.Any(p => p != null && BuildingTypes.ByPrefab(p.name) != null);
            }
            return _lastWasHammer;
        }

        /// <param name="order">For an unfinished site: only the pieces still to come are drawn. Null: every piece.</param>
        private void Draw(IReadOnlyList<PlannedPiece> plan, Material material, BuildOrderRecord order)
        {
            foreach (PlannedPiece piece in plan)
            {
                if (_drawn >= MaxMeshesPerFrame) return;
                if (!piece.IsValid || (order != null && !order.IsOpen(piece.Index))) continue;
                List<Part> parts = PartsOf(piece.Prefab);
                if (parts == null) continue;
                Matrix4x4 at = Matrix4x4.TRS(U(piece.Position), U(piece.Rotation), Vector3.one);
                foreach (Part part in parts)
                {
                    for (int sub = 0; sub < part.Mesh.subMeshCount; sub++)
                        Graphics.DrawMesh(part.Mesh, at * part.Local, material, 0, null, sub, null, false, false);
                    _drawn++;
                }
            }
        }

        /// <summary>The meshes a piece shows up close, relative to the piece; null when it has none.</summary>
        private static List<Part> PartsOf(string prefabName)
        {
            if (s_parts.TryGetValue(prefabName, out List<Part> parts)) return parts;
            GameObject prefab = PiecePlacer.Prefab(prefabName);
            if (prefab != null)
            {
                Matrix4x4 toPiece = prefab.transform.worldToLocalMatrix;
                LODGroup lod = prefab.GetComponentInChildren<LODGroup>();
                LOD[] lods = lod != null ? lod.GetLODs() : null;
                IEnumerable<Renderer> renderers = lods != null && lods.Length > 0
                    ? lods[0].renderers
                    : (IEnumerable<Renderer>)prefab.GetComponentsInChildren<MeshRenderer>();
                foreach (Renderer renderer in renderers)
                {
                    MeshFilter filter = renderer != null ? renderer.GetComponent<MeshFilter>() : null;
                    if (filter == null || filter.sharedMesh == null) continue;
                    if (parts == null) parts = new List<Part>();
                    parts.Add(new Part { Mesh = filter.sharedMesh, Local = toPiece * renderer.transform.localToWorldMatrix });
                }
            }
            s_parts[prefabName] = parts;
            return parts;
        }

        private bool Materials()
        {
            if (_green != null) return true;
            if (_tried) return false;
            _tried = true;
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("UI/Default");
            if (shader == null)
            {
                Log.Warn("No see-through shader found; building outlines are not drawn (the site check's line still shows).");
                return false;
            }
            _green = Make(shader, new Color(0.35f, 1f, 0.45f, 0.28f));
            _yellow = Make(shader, new Color(1f, 0.85f, 0.25f, 0.28f));
            _red = Make(shader, new Color(1f, 0.3f, 0.3f, 0.32f));
            _site = Make(shader, new Color(0.7f, 0.85f, 1f, 0.12f));
            Log.Info($"Building outlines are drawn with the '{shader.name}' shader");
            return true;
        }

        private static Material Make(Shader shader, Color colour) => new Material(shader) { color = colour, renderQueue = 3000 };

        private Material MaterialFor(SiteColour colour) =>
            colour == SiteColour.Red ? _red : colour == SiteColour.Yellow ? _yellow : _green;
    }
}
