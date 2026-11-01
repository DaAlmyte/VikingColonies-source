using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Jotunn.Entities;
using UnityEngine;
using UnityEngine.Rendering;
using VikingColonies.Construction;
using VikingColonies.Core;

namespace VikingColonies.DevTools
{
    /// <summary>
    /// vc_look (test worlds; the Valheim look pass, 27 Oct 2026): measures how the game's own pieces are drawn, so the
    /// mod's own models (tools/assets) can be painted to match: for each prefab's look (its new look, its first level of
    /// detail, no snow) every material's shader, its textures' sizes, filtering and tiling, its number settings, the
    /// texture's look in numbers (LookStats over the texels its mesh uses), the normal map's strength, texture pixels per
    /// metre and triangles. Writes BepInEx/VikingColonies/look.json. Numbers only: no picture of the game's is saved.
    /// 'vc_look' measures the usual set; 'vc_look name ...' those prefabs.
    /// </summary>
    internal sealed class LookCommand : ConsoleCommand
    {
        public override string Name => "vc_look";
        public override string Help => "VikingColonies (test worlds): 'vc_look [prefab ...]' measures how the game draws its pieces (shader, " +
                                       "texture size, brightness, colour, grain, normal maps, pixels per metre, triangles) into BepInEx/VikingColonies/look.json";
        public override bool IsCheat => true;

        public static string FilePath => Path.Combine(Path.Combine(BepInEx.Paths.BepInExRootPath, "VikingColonies"), "look.json");

        // The usual set: wood, thatch, cloth and stone pieces, the farm's look, the crow, logs and stumps (bark and end
        // grain), axes and iron-bound pieces, and the mod's own models.
        internal static readonly string[] Usual =
        {
            "wood_pole2", "wood_pole", "wood_beam", "woodwall", "wood_wall_half", "wood_floor", "wood_door", "wood_stair",
            "piece_chest_wood", "piece_workbench", "Cart", "piece_chair", "piece_bench01", "piece_table", "itemstand", "sign",
            "piece_TrainingDummy", "piece_beehive", "piece_maypole", "wood_roof", "wood_roof_45", "bed", "piece_bed02",
            "piece_banner01", "piece_banner02", "piece_cloth_hanging_door", "rug_deer", "rug_wolf", "ArmorRagsChest",
            "CapeDeerHide", "fire_pit", "stone_wall_1x1", "Crow", "Wood", "RoundLog", "FineWood", "ElderBark", "AxeStone",
            "AxeFlint", "AxeBronze", "AxeIron", "Hammer", "piece_chest", "Beech_Stub", "FirTree_Stub", "Pinetree_01_Stub",
            "vc_scarecrow", "vc_choppingblock",
        };

        private static readonly Dictionary<Renderer, double> s_lopsided = new Dictionary<Renderer, double>();

        public override void Run(string[] args)
        {
            if (DevHelpers.RefuseOutsideTestWorld(Name)) return;
            s_lopsided.Clear();
            string[] names = args.Length > 0 ? args : Usual;
            var entries = new List<string>();
            var shaders = new Dictionary<string, string>();
            int measured = 0;
            foreach (string name in names)
            {
                GameObject prefab = (ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(name) : null)
                                    ?? (ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(name) : null);
                if (prefab == null)
                {
                    BuildCommand.Print($"vc_look: {name} missing");
                    continue;
                }
                try
                {
                    foreach (string entry in Measure(prefab, shaders)) entries.Add(entry);
                    measured++;
                }
                catch (Exception e)
                {
                    BuildCommand.Print($"vc_look: {name} failed: {e.Message}");
                }
            }
            string json = "{\n\"shaders\": {\n" + string.Join(",\n", shaders.Select(s => $"  {Quote(s.Key)}: {s.Value}")) +
                          "\n},\n\"materials\": [\n" + string.Join(",\n", entries) + "\n]\n}\n";
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, json, new UTF8Encoding(false));
            BuildCommand.Print($"vc_look: measured {measured} of {names.Length} prefabs, {entries.Count} materials, {shaders.Count} shaders -> BepInEx/VikingColonies/look.json");
        }

        /// <summary>The renderers that draw the prefab's own look: its new look (a piece), the first level of detail, no snow.</summary>
        internal static List<Renderer> LookRenderers(GameObject prefab)
        {
            WearNTear wear = prefab.GetComponent<WearNTear>();
            GameObject look = wear != null && wear.m_new != null ? wear.m_new : prefab;
            var skip = new HashSet<Renderer>();
            foreach (LODGroup group in look.GetComponentsInChildren<LODGroup>(true))
            {
                LOD[] lods = group.GetLODs();
                for (int i = 1; i < lods.Length; i++)
                    foreach (Renderer r in lods[i].renderers)
                        if (r != null) skip.Add(r);
                if (lods.Length > 0) foreach (Renderer r in lods[0].renderers) if (r != null) skip.Remove(r);
            }
            if (wear != null)
                foreach (MeshRenderer r in new[] { wear.m_snow, wear.m_snowWorn, wear.m_snowBroken }.Where(r => r != null)) skip.Add(r);
            return look.GetComponentsInChildren<Renderer>(true)
                .Where(r => (r is MeshRenderer || r is SkinnedMeshRenderer) && !skip.Contains(r))
                .Where(r => r.sharedMaterials.Any(m => m != null && m.shader != null &&
                                                        m.shader.name.IndexOf("snow", StringComparison.OrdinalIgnoreCase) < 0))
                .ToList();
        }

        private static IEnumerable<string> Measure(GameObject prefab, Dictionary<string, string> shaders)
        {
            foreach (Renderer renderer in LookRenderers(prefab))
            {
                Mesh mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                Material[] materials = renderer.sharedMaterials;
                for (int sub = 0; sub < materials.Length && sub < mesh.subMeshCount; sub++)
                {
                    Material material = materials[sub];
                    if (material == null || material.shader == null) continue;
                    if (material.shader.name.IndexOf("snow", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (!shaders.ContainsKey(material.shader.name)) shaders[material.shader.name] = ShaderProperties(material.shader);
                    yield return Entry(prefab, renderer, mesh, sub, material);
                }
            }
        }

        private static string Entry(GameObject prefab, Renderer renderer, Mesh mesh, int sub, Material material)
        {
            var c = CultureInfo.InvariantCulture;
            var parts = new List<string>
            {
                "\"prefab\": " + Quote(prefab.name),
                "\"renderer\": " + Quote(renderer.name),
                "\"material\": " + Quote(material.name),
                "\"shader\": " + Quote(material.shader.name),
                "\"triangles\": " + (mesh.GetIndexCount(sub) / 3).ToString(c),
                "\"mesh_triangles\": " + Enumerable.Range(0, mesh.subMeshCount).Sum(i => (long)mesh.GetIndexCount(i) / 3).ToString(c),
                "\"vertex_colours\": " + (mesh.HasVertexAttribute(VertexAttribute.Color) ? "true" : "false"),
                "\"readable\": " + (mesh.isReadable ? "true" : "false"),
            };
            Vector2 scale = material.HasProperty("_MainTex") ? material.GetTextureScale("_MainTex") : Vector2.one;
            var textures = new List<string>();
            foreach (string prop in material.GetTexturePropertyNames())
            {
                Texture t = material.GetTexture(prop);
                if (t == null) continue;
                textures.Add($"{Quote(prop)}: {{\"size\": [{t.width}, {t.height}], \"filter\": {Quote(t.filterMode.ToString())}, " +
                             $"\"scale\": [{material.GetTextureScale(prop).x.ToString("0.###", c)}, {material.GetTextureScale(prop).y.ToString("0.###", c)}], " +
                             $"\"srgb\": {(t.isDataSRGB ? "true" : "false")}, \"name\": {Quote(t.name)}}}");
            }
            parts.Add("\"textures\": {" + string.Join(", ", textures) + "}");
            parts.Add("\"numbers\": {" + string.Join(", ", NumberProperties(material)) + "}");
            if (material.HasProperty("_Color")) parts.Add("\"colour\": " + Vec(material.GetColor("_Color")));

            Texture main = material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;
            bool[] mask = null;
            if (main != null && mesh.isReadable)
            {
                int[] tris = mesh.GetTriangles(sub);
                float[] uv = mesh.uv.SelectMany(v => new[] { v.x, v.y }).ToArray();
                Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                float[] positions = mesh.vertices.Select(v => toRoot.MultiplyPoint3x4(v)).SelectMany(v => new[] { v.x, v.y, v.z }).ToArray();
                if (!s_lopsided.TryGetValue(renderer, out double lopsided))
                    s_lopsided[renderer] = lopsided = LookStats.Lopsided(positions);    // once a renderer, not each of its materials: slow on big meshes
                parts.Add("\"lopsided\": " + lopsided.ToString("0.####", c));
                if (uv.Length > 0)
                {
                    double density = LookStats.TexelsPerMetre(positions, uv, tris, main.width, main.height, scale.x, scale.y);
                    parts.Add("\"texels_per_metre\": " + density.ToString("0.#", c));
                    // the texels its UVs cover, unless the texture tiles (then all of it is used)
                    bool tiles = Mathf.Abs(scale.x - 1f) > 0.01f || Mathf.Abs(scale.y - 1f) > 0.01f ||
                                 uv.Any(v => v < -0.01f || v > 1.01f);
                    if (!tiles) mask = LookStats.UvMask(uv, tris, main.width, main.height);
                    parts.Add("\"uv_mask\": " + (mask != null ? "true" : "false"));
                }
            }
            if (main != null)
            {
                byte[] pixels = Pixels(main);
                LookStats stats = LookStats.Measure(pixels, main.width, main.height, mask);
                if (stats != null) parts.Add("\"look\": " + stats.ToJson());
            }
            Texture bump = material.HasProperty("_BumpMap") ? material.GetTexture("_BumpMap") : null;
            if (bump != null)
                parts.Add("\"normal_strength\": " + LookStats.NormalStrength(Pixels(bump), bump.width, bump.height).ToString("0.####", c));
            return "  {" + string.Join(", ", parts) + "}";
        }

        /// <summary>A texture's pixels as authored (sRGB bytes for a colour texture), read back through the graphics card: the game's textures can't be read directly. Kept in memory only.</summary>
        private static byte[] Pixels(Texture texture)
        {
            int w = texture.width, h = texture.height;
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32,
                texture.isDataSRGB ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);
            RenderTexture previous = RenderTexture.active;
            var copy = new Texture2D(w, h, TextureFormat.RGBA32, false, !texture.isDataSRGB);
            try
            {
                Graphics.Blit(texture, rt);
                RenderTexture.active = rt;
                copy.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                copy.Apply();
                Color32[] px = copy.GetPixels32();
                var bytes = new byte[px.Length * 4];
                for (int i = 0; i < px.Length; i++)
                {
                    bytes[i * 4] = px[i].r; bytes[i * 4 + 1] = px[i].g; bytes[i * 4 + 2] = px[i].b; bytes[i * 4 + 3] = px[i].a;
                }
                return bytes;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.Destroy(copy);
            }
        }

        private static IEnumerable<string> NumberProperties(Material material)
        {
            var c = CultureInfo.InvariantCulture;
            Shader shader = material.shader;
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                string name = shader.GetPropertyName(i);
                ShaderPropertyType type = shader.GetPropertyType(i);
                if (type == ShaderPropertyType.Float || type == ShaderPropertyType.Range)
                    yield return $"{Quote(name)}: {material.GetFloat(name).ToString("0.####", c)}";
                else if (type == ShaderPropertyType.Color || type == ShaderPropertyType.Vector)
                    yield return $"{Quote(name)}: {Vec(material.GetVector(name))}";
            }
        }

        private static string ShaderProperties(Shader shader)
        {
            var names = new List<string>();
            for (int i = 0; i < shader.GetPropertyCount(); i++)
                names.Add(Quote(shader.GetPropertyName(i) + ":" + shader.GetPropertyType(i)));
            return "[" + string.Join(", ", names) + "]";
        }

        private static string Vec(Vector4 v)
        {
            var c = CultureInfo.InvariantCulture;
            return $"[{v.x.ToString("0.###", c)}, {v.y.ToString("0.###", c)}, {v.z.ToString("0.###", c)}, {v.w.ToString("0.###", c)}]";
        }

        private static string Quote(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
