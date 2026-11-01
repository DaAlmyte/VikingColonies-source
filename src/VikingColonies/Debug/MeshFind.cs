using System;
using System.Linq;
using Jotunn.Entities;
using UnityEngine;
using VikingColonies.Core;

namespace VikingColonies.DevTools
{
    /// <summary>
    /// vc_meshfind (design tools, 1.6.0): where the game keeps a model. 'vc_meshfind rune places' lists the loose prefabs, the
    /// locations' children and the loaded meshes whose name has "rune" in it, or which read with E as a runestone, each with
    /// its meshes, materials and size (the locations only with 'places': each one's model is loaded to look and let go
    /// again, which takes a while). Nothing is placed.
    /// </summary>
    internal sealed class MeshFindCommand : ConsoleCommand
    {
        public override string Name => "vc_meshfind";
        public override string Help => "VikingColonies (design tools): 'vc_meshfind <text> [places]' lists the game's prefabs (and with 'places' the locations' parts) and meshes named <text>, and every runestone";
        public override bool IsCheat => false;

        public override void Run(string[] args)
        {
            string text = args.Length > 0 ? args[0].ToLowerInvariant() : "rune";
            int found = 0;
            if (ZNetScene.instance != null)
                foreach (GameObject prefab in ZNetScene.instance.m_prefabs.Where(p => p != null))
                    if (prefab.name.ToLowerInvariant().Contains(text) || prefab.GetComponentInChildren<RuneStone>(true) != null)
                    {
                        Describe("prefab " + prefab.name, prefab);
                        found++;
                    }
            bool places = args.Length > 1 && args[1].ToLowerInvariant() == "places";
            if (places && ZoneSystem.instance != null)
                foreach (ZoneSystem.ZoneLocation location in ZoneSystem.instance.m_locations)
                {
                    if (location == null || !location.m_prefab.IsValid) continue;
                    try
                    {
                        location.m_prefab.Load();
                        GameObject root = location.m_prefab.Asset;
                        if (root == null) continue;
                        foreach (Transform part in root.GetComponentsInChildren<Transform>(true))
                            if (part.name.ToLowerInvariant().Contains(text) || part.GetComponent<RuneStone>() != null)
                            {
                                Describe($"location {location.m_prefabName} / {Path(part, root.transform)}", part.gameObject);
                                found++;
                            }
                    }
                    catch (Exception e) { Log.Warn($"vc_meshfind: location {location.m_prefabName}: {e.Message}"); }
                    finally { if (location.m_prefab.IsLoaded) location.m_prefab.Release(); }
                }
            foreach (Mesh mesh in Resources.FindObjectsOfTypeAll<Mesh>().Where(m => m != null && m.name.ToLowerInvariant().Contains(text)))
            {
                Log.Event($"vc_meshfind: mesh {mesh.name} ({mesh.vertexCount} vertices, size {mesh.bounds.size})");
                found++;
            }
            Log.Event($"vc_meshfind: {found} found for '{text}'");
        }

        private static void Describe(string what, GameObject obj)
        {
            RuneStone rune = obj.GetComponentInChildren<RuneStone>(true);
            Log.Event($"vc_meshfind: {what}" + (rune != null ? $" (runestone '{rune.m_name}' on {rune.gameObject.name})" : "") +
                      $", colliders {obj.GetComponentsInChildren<Collider>(true).Length}");
            foreach (MeshFilter filter in obj.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
                string materials = renderer != null ? string.Join(", ", renderer.sharedMaterials.Select(m => m != null ? m.name + " (" + m.shader.name + ")" : "none")) : "no renderer";
                Log.Event($"vc_meshfind:   {Path(filter.transform, obj.transform)}: mesh {(mesh != null ? mesh.name + " " + mesh.vertexCount + " vertices, size " + mesh.bounds.size : "none")}," +
                          $" scale {filter.transform.lossyScale}, materials {materials}");
            }
        }

        private static string Path(Transform part, Transform root)
        {
            string path = part.name;
            for (Transform t = part.parent; t != null && t != root.parent; t = t.parent) path = t.name + "/" + path;
            return path;
        }
    }
}
