using System;
using System.Collections.Generic;
using System.Linq;
using Jotunn.Entities;
using Jotunn.Managers;
using SoftReferenceableAssets;
using UnityEngine;
using VikingColonies.Core;

namespace VikingColonies.Buildings
{
    /// <summary>
    /// The memorial stone's look (1.6.0): the stone of one of the game's lore runestones on the memorial stone's piece, in
    /// place of the pillar it was cloned from. The runestones stand only in the game's places (locations), whose models are
    /// loaded with a world, so the piece is dressed each time a world's places are ready, before anything in it is made:
    /// every stone already placed changes look and nothing else (its prefab, cost, rules and words stay). The place's model
    /// is kept loaded while the game runs, so its stone's mesh stays. No runestone found: the pillar stays, and the log says so.
    /// </summary>
    internal static class MemorialStoneLook
    {
        private const string LookName = "vc_runestone";

        private static CustomPiece s_piece;
        private static SoftReference<GameObject> s_held;
        private static bool s_holding;
        private static bool s_iconDone;

        public static void Watch(CustomPiece piece)
        {
            s_piece = piece;
            ZoneManager.OnVanillaLocationsAvailable += Dress;
        }

        private static void Dress()
        {
            GameObject prefab = s_piece?.PiecePrefab;
            if (prefab == null || ZoneSystem.instance == null) return;
            if (Dressed(prefab)) return;
            List<ZoneSystem.ZoneLocation> places = ZoneSystem.instance.m_locations.Where(l => l != null && l.m_prefab.IsValid).ToList();
            // Each place on its own (review of 1.6.0): one that fails is passed over for the next.
            foreach (string name in MemorialLook.Candidates(places.Select(l => l.m_prefabName)))
            {
                SoftReference<GameObject> model = places.First(l => l.m_prefabName == name).m_prefab;
                bool kept = false;
                try
                {
                    model.Load();
                    List<MeshRenderer> stone = Stone(model.Asset, out Transform root);
                    if (stone.Count == 0 || !PutOn(prefab, root, stone)) continue;
                    SoftReference<GameObject> old = s_held;
                    bool held = s_holding;
                    s_held = model;   // kept loaded: the stone's mesh and materials are the place's
                    s_holding = kept = true;
                    if (held && old.IsLoaded) old.Release();   // the game may have let it go with the world before
                    Log.Info($"The memorial stone wears the runestone of '{name}' ({root.name}, {stone.Count} part(s))");
                    Icon(prefab);
                    return;
                }
                catch (Exception e)
                {
                    Log.Warn($"The memorial stone's runestone from '{name}' failed ({e.Message}); trying the next");
                }
                finally
                {
                    if (!kept && model.IsLoaded) model.Release();
                }
            }
            Log.Info("Found none of the game's runestones; the memorial stone keeps the look it has");
        }

        /// <summary>Already wearing a stone whose mesh is still loaded (a world loaded again needs nothing new).</summary>
        private static bool Dressed(GameObject prefab)
        {
            Transform look = prefab.transform.Find(LookName);
            return look != null && look.GetComponentsInChildren<MeshFilter>(true).All(f => f.sharedMesh != null);
        }

        /// <summary>
        /// The runestone's stone in a place's model: the parts drawn round the runestone one reads (its nearest forebear that
        /// has a mesh, the place's own root excepted), its most detailed level only, and only parts shown when it stands.
        /// </summary>
        private static List<MeshRenderer> Stone(GameObject place, out Transform root)
        {
            root = null;
            RuneStone rune = place != null ? place.GetComponentInChildren<RuneStone>(true) : null;
            if (rune == null) return new List<MeshRenderer>();
            for (Transform t = rune.transform; t != null && t != place.transform; t = t.parent)
            {
                List<MeshRenderer> parts = Shown(t);
                if (parts.Count == 0) continue;
                root = t;
                return parts;
            }
            return new List<MeshRenderer>();
        }

        private static List<MeshRenderer> Shown(Transform root)
        {
            IEnumerable<MeshRenderer> parts = root.GetComponentsInChildren<MeshRenderer>(true);
            // Every level-of-detail group round it, above or below (review of 1.6.0): only the finest level of each is drawn.
            var coarser = new HashSet<Renderer>();
            foreach (LODGroup lods in root.GetComponentsInChildren<LODGroup>(true).Concat(root.GetComponentsInParent<LODGroup>(true)).Distinct())
            {
                LOD[] levels = lods.GetLODs();
                if (levels.Length < 2) continue;
                var finest = new HashSet<Renderer>(levels[0].renderers.Where(r => r != null));
                foreach (Renderer r in levels.Skip(1).SelectMany(l => l.renderers).Where(r => r != null && !finest.Contains(r))) coarser.Add(r);
            }
            parts = parts.Where(r => !coarser.Contains(r));
            return parts.Where(r => r.enabled && ActiveUpTo(r.transform, root) && r.GetComponent<MeshFilter>()?.sharedMesh != null).ToList();
        }

        private static bool ActiveUpTo(Transform t, Transform root)
        {
            for (; t != null; t = t.parent)
            {
                if (!t.gameObject.activeSelf) return false;
                if (t == root) return true;
            }
            return true;
        }

        /// <summary>
        /// The stone's parts on the piece, in the stone's own place (its size kept, its lean in its place not), lifted so its
        /// foot sits a little in the ground, with a box round it to stand, be hit and be read; then the pillar's model,
        /// colliders and snap points off, and the piece's wear showing the stone. Built whole before anything is taken off
        /// (review of 1.6.0): a failure leaves the piece as it was.
        /// </summary>
        private static bool PutOn(GameObject prefab, Transform root, List<MeshRenderer> stone)
        {
            int layer = LayerMask.NameToLayer("piece");
            var look = new GameObject(LookName) { layer = layer };
            look.transform.SetParent(prefab.transform, false);
            try { Build(look, root, stone, layer); }
            catch
            {
                UnityEngine.Object.DestroyImmediate(look);
                throw;
            }

            foreach (Transform child in prefab.transform.Cast<Transform>().Where(c => c != look.transform).ToList()) UnityEngine.Object.DestroyImmediate(child.gameObject);
            foreach (LODGroup c in prefab.GetComponents<LODGroup>()) UnityEngine.Object.DestroyImmediate(c);
            foreach (Renderer c in prefab.GetComponents<Renderer>()) UnityEngine.Object.DestroyImmediate(c);
            foreach (MeshFilter c in prefab.GetComponents<MeshFilter>()) UnityEngine.Object.DestroyImmediate(c);
            foreach (Collider c in prefab.GetComponents<Collider>()) UnityEngine.Object.DestroyImmediate(c);

            WearNTear wear = prefab.GetComponent<WearNTear>();
            if (wear != null)
            {
                wear.m_new = look;
                wear.m_worn = look;
                wear.m_broken = look;
                // Nothing left pointing at the pillar's parts (review of 1.6.0): its fragments, wet and snowy models.
                wear.m_fragmentRoots = null;
                wear.m_wet = null;
                wear.m_snow = null;
                wear.m_snowWorn = null;
                wear.m_snowBroken = null;
                wear.m_nonSolidRenderers?.Clear();
            }
            return true;
        }

        private static void Build(GameObject look, Transform root, List<MeshRenderer> stone, int layer)
        {
            Bounds? all = null;
            foreach (MeshRenderer part in stone)
            {
                var copy = new GameObject(part.name) { layer = layer };
                copy.transform.SetParent(look.transform, false);
                copy.transform.localPosition = Quaternion.Inverse(root.rotation) * (part.transform.position - root.position);
                copy.transform.localRotation = Quaternion.Inverse(root.rotation) * part.transform.rotation;
                copy.transform.localScale = part.transform.lossyScale;
                Mesh mesh = part.GetComponent<MeshFilter>().sharedMesh;
                copy.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer drawn = copy.AddComponent<MeshRenderer>();
                drawn.sharedMaterials = part.sharedMaterials;
                drawn.shadowCastingMode = part.shadowCastingMode;
                drawn.receiveShadows = part.receiveShadows;
                Bounds bounds = InLook(mesh.bounds, copy.transform);
                if (all == null) all = bounds;
                else
                {
                    Bounds grown = all.Value;
                    grown.Encapsulate(bounds);
                    all = grown;
                }
            }
            Bounds box = all.Value;
            look.transform.localPosition = new Vector3(0f, MemorialLook.Lift(box.min.y), 0f);
            look.transform.localRotation = Quaternion.Euler(0f, MemorialLook.FaceTurn, 0f);
            BoxCollider hit = look.AddComponent<BoxCollider>();
            hit.center = box.center;
            hit.size = box.size;
        }

        /// <summary>A mesh's box in the look's space (its part's place, turn and size there).</summary>
        private static Bounds InLook(Bounds bounds, Transform part)
        {
            Matrix4x4 m = Matrix4x4.TRS(part.localPosition, part.localRotation, part.localScale);
            var result = new Bounds(m.MultiplyPoint3x4(bounds.center), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                result.Encapsulate(m.MultiplyPoint3x4(corner));
            }
            return result;
        }

        /// <summary>The hammer's picture of it, once: the stone as it now looks.</summary>
        private static void Icon(GameObject prefab)
        {
            if (s_iconDone || GUIManager.IsHeadless()) return;   // a dedicated server draws nothing
            try
            {
                Sprite icon = RenderManager.Instance.Render(prefab, RenderManager.IsometricRotation);
                if (icon == null) return;
                s_piece.Piece.m_icon = icon;
                s_iconDone = true;
            }
            catch (Exception e) { Log.Warn($"The memorial stone's hammer picture failed ({e.Message}); it keeps the pillar's"); }
        }
    }
}
