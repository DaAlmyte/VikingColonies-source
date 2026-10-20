using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Config;

namespace VikingColonies.Visuals
{
    /// <summary>
    /// Ground rings (Valheim's workbench/ward marker): while placing a hut, the circles of each nearby colony's area (its Town
    /// Hall's and its Watchtowers', 1.1) and the hut's work area; while hovering a built hut, its work area.
    /// </summary>
    internal sealed class AreaRings : MonoBehaviour
    {
        private const float HoverLingerSeconds = 2f;
        private const float AreaSearchRange = 200f;

        private static readonly FieldInfo PlacementGhost = AccessTools.Field(typeof(Player), "m_placementGhost");

        // Rings are pooled per radius: the marker lays out its segments for the radius it starts with.
        private readonly Dictionary<int, List<CircleProjector>> _pools = new Dictionary<int, List<CircleProjector>>();
        private readonly Dictionary<int, int> _used = new Dictionary<int, int>();
        private GameObject _template;
        private bool _warned;
        private ColonyBuilding _hovered;
        private float _hoverLinger;

        private void Update()
        {
            _used.Clear();
            Player player = Player.m_localPlayer;
            if (player != null)
            {
                ShowPlacementRings(player);
                ShowHoverRing(player);
            }
            foreach (KeyValuePair<int, List<CircleProjector>> pool in _pools)
            {
                _used.TryGetValue(pool.Key, out int used);
                for (int i = used; i < pool.Value.Count; i++)
                    if (pool.Value[i] != null) pool.Value[i].gameObject.SetActive(false);
            }
        }

        private void ShowPlacementRings(Player player)
        {
            if (!player.InPlaceMode()) return;
            Piece selected = player.GetSelectedPiece();
            BuildingType type = selected != null ? BuildingTypes.ByPrefab(Utils.GetPrefabName(selected.gameObject)) : null;
            if (type == null || type.Id == BuildingTypes.TownHallId) return;
            Vector3 me = player.transform.position;
            foreach (Core.ColonyAreaInfo colony in ColonyAreas.Current())
            {
                RingNear(colony.Hall, me);
                foreach (Core.AreaCircle tower in colony.Towers) RingNear(tower, me);
            }
            float? area = Jobs.WorkAreas.Base(type.Id);   // the lumberjack's, the forager's, the guard's
            if (area == null) return;
            GameObject ghost = PlacementGhost?.GetValue(player) as GameObject;
            if (ghost != null && ghost.activeInHierarchy) Ring(ghost.transform.position, area.Value);
        }

        private void ShowHoverRing(Player player)
        {
            GameObject hover = player.GetHoverObject();
            ColonyBuilding building = hover != null ? hover.GetComponentInParent<ColonyBuilding>() : null;
            if (building != null && Jobs.WorkAreas.Of(building) != null)
            {
                _hovered = building;
                _hoverLinger = HoverLingerSeconds;
            }
            else
            {
                _hoverLinger -= Time.deltaTime;
            }
            if (_hovered != null && _hoverLinger > 0f) Ring(_hovered.transform.position, Jobs.WorkAreas.Of(_hovered) ?? 0f);
        }

        private void RingNear(Core.AreaCircle circle, Vector3 me)
        {
            var centre = new Vector3(circle.Centre.X, me.y, circle.Centre.Z);
            if (Utils.DistanceXZ(centre, me) >= AreaSearchRange) return;
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(centre, out float ground)) centre.y = ground;
            Ring(centre, circle.Radius);
        }

        private void Ring(Vector3 center, float radius)
        {
            int key = Mathf.RoundToInt(radius);
            if (!_pools.TryGetValue(key, out List<CircleProjector> pool)) _pools[key] = pool = new List<CircleProjector>();
            _used.TryGetValue(key, out int used);
            if (used >= pool.Count || pool[used] == null)
            {
                CircleProjector created = CreateRing(radius);
                if (created == null) return;
                if (used < pool.Count) pool[used] = created;
                else pool.Add(created);
            }
            CircleProjector ring = pool[used];
            _used[key] = used + 1;
            ring.transform.position = center;
            ring.gameObject.SetActive(true);
        }

        private CircleProjector CreateRing(float radius)
        {
            if (_template == null)
            {
                GameObject bench = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("piece_workbench") : null;
                CraftingStation station = bench != null ? bench.GetComponent<CraftingStation>() : null;
                _template = station != null ? station.m_areaMarker : null;
                if (_template == null)
                {
                    if (!_warned) Log.Warn("Workbench area marker not found; colony rings are disabled.");
                    _warned = true;
                    return null;
                }
            }
            GameObject go = Instantiate(_template);
            go.name = "vc_area_ring";
            CircleProjector ring = go.GetComponentInChildren<CircleProjector>(true);
            if (ring == null)
            {
                Destroy(go);
                return null;
            }
            ring.m_radius = radius;   // set before the marker's Start lays out its segments
            return ring;
        }
    }
}
