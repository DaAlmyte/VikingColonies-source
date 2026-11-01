using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace VikingColonies.Visuals
{
    /// <summary>
    /// The mod's own models (custom assets' first test, 27 Oct 2026): one asset bundle, vc_assets, embedded in the DLL
    /// (the way Jötunn's docs give for a mod that ships one file), made by tools/assets from art/models through the Unity project in
    /// unity/. Loaded once, on first use, and let go once the pieces are made (Release); each prop is a prefab of its own name (a "model" child, a "colliders" child) and
    /// its build menu icon a sprite "&lt;name&gt;-icon".
    /// </summary>
    internal static class ModAssets
    {
        private const string BundleName = "vc_assets";
        private static AssetBundle s_bundle;
        private static bool s_tried;

        public static AssetBundle Bundle
        {
            get
            {
                if (s_tried) return s_bundle;
                s_tried = true;
                try
                {
                    // Read whole into memory: Jötunn's AssetUtils.LoadAssetBundleFromResources opens the bundle from the
                    // resource's stream and closes the stream, but Unity reads a bundle's contents from its stream later,
                    // at the first LoadAsset ("ManagedStream object must be readable", then a crash: the first game run,
                    // 27 Oct 2026).
                    Assembly assembly = typeof(ModAssets).Assembly;
                    string resource = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("." + BundleName, StringComparison.Ordinal));
                    if (resource != null)
                        using (Stream stream = assembly.GetManifestResourceStream(resource))
                        {
                            var bytes = new byte[stream.Length];
                            int read = 0;
                            while (read < bytes.Length)
                            {
                                int n = stream.Read(bytes, read, bytes.Length - read);
                                if (n <= 0) break;
                                read += n;
                            }
                            s_bundle = AssetBundle.LoadFromMemory(bytes);
                        }
                }
                catch (Exception e)
                {
                    Log.Error($"The mod's own models ({BundleName}) couldn't be loaded: {e.Message}");
                }
                if (s_bundle == null) Log.Error($"The mod's own models ({BundleName}) are missing from the DLL.");
                return s_bundle;
            }
        }

        /// <summary>The prop's prefab from the bundle, or null (logged).</summary>
        public static GameObject Prefab(string name)
        {
            GameObject prefab = Bundle != null ? Bundle.LoadAsset<GameObject>(name) : null;
            if (prefab == null && Bundle != null) Log.Error($"The mod's models have no '{name}'.");
            return prefab;
        }

        /// <summary>
        /// A copy of a game piece's material (its shader and settings: light, the wet look in rain, snow, the value noise
        /// every piece of the game has) drawn with a prop's own painted texture and normal map (`ours`, the bundle's
        /// material), untiled: none of the game's normal or metal maps (they belong to its own texture's layout).
        /// </summary>
        public static Material Painted(Material template, Material ours, string name)
        {
            var material = new Material(template) { name = name };
            Texture texture = ours != null && ours.HasProperty("_MainTex") ? ours.GetTexture("_MainTex") : null;
            Texture normal = ours != null && ours.HasProperty("_BumpMap") ? ours.GetTexture("_BumpMap") : null;
            if (texture != null) material.SetTexture("_MainTex", texture);
            // the prop's UVs as they are: the game's wood tiles its texture (the pole's scale drew the whole scarecrow in
            // the atlas's one corner, the second game run of 27 Oct 2026)
            if (material.HasProperty("_MainTex"))
            {
                material.SetTextureScale("_MainTex", Vector2.one);
                material.SetTextureOffset("_MainTex", Vector2.zero);
            }
            foreach (string map in new[] { "_BumpMap", "_MetallicGlossMap", "_MetallicTex", "_EmissionMap" })
                if (material.HasProperty(map)) material.SetTexture(map, null);
            // a normal map of the prop's own (its bake), at the game's usual strength (vc_look: 1 on almost every piece)
            if (normal != null && normal.name != "bump" && material.HasProperty("_BumpMap"))
            {
                material.SetTexture("_BumpMap", normal);
                material.EnableKeyword("_NORMALMAP");
                if (material.HasProperty("_BumpScale")) material.SetFloat("_BumpScale", 1f);
            }
            if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
            // the game's light and dark blotches over the texture, as on its own props (vc_look, 27 Oct 2026: 0.5 on
            // the chest, the workbench, the bench, the beds; none on the vertices)
            if (material.HasProperty("_ValueNoise")) material.SetFloat("_ValueNoise", 0.5f);
            if (material.HasProperty("_ValueNoiseVertex")) material.SetFloat("_ValueNoiseVertex", 0f);
            return material;
        }

        /// <summary>The prop's build menu icon, or null.</summary>
        public static Sprite Icon(string name) => Bundle != null ? Bundle.LoadAsset<Sprite>(name + "-icon") : null;

        /// <summary>
        /// Lets the bundle go once its pieces are made (review of the look pass: Unity kept its ~760 KB for the whole
        /// game, read by nothing after start-up). What was loaded from it, the models, materials and icons, stays.
        /// </summary>
        public static void Release()
        {
            if (s_bundle != null) s_bundle.Unload(false);
            s_bundle = null;
        }
    }
}
