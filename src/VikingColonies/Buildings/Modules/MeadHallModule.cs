using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VikingColonies.Construction;
using VikingColonies.Core;
using static VikingColonies.Construction.CoreVectors;

namespace VikingColonies.Buildings
{
    /// <summary>
    /// The Mead Hall (1.6): the game's cooking stations standing inside it (within its design's walls, or near its chest
    /// while it has no design), found again every few seconds on every computer, and whether it has a cook; its chest is
    /// the larder (docs/superpowers/specs/2026-10-24-vikingcolonies-cook-and-mead-hall-design.md).
    /// </summary>
    public class MeadHallModule : MonoBehaviour
    {
        /// <summary>A station this far under the hall's floor, or this high over it, isn't in the hall.</summary>
        private const float Below = 1f, Above = 8f;

        private static readonly List<MeadHallModule> s_all = new List<MeadHallModule>();
        private static HashSet<string> s_cooked;
        private static int s_mask;
        private static Collider[] s_hits = new Collider[128];

        private ZNetView _nview;
        private ColonyBuilding _building;
        private ConstructionModule _site;
        private WorkerModule _workers;
        private readonly List<CookingStation> _stations = new List<CookingStation>();
        private float _nextScan;

        public ColonyBuilding Building => _building;
        public bool IsValid => _nview != null && _nview.IsValid() && _building != null;

        /// <summary>The cooking stations standing in the hall, nearest its chest first; never a destroyed one.</summary>
        public IReadOnlyList<CookingStation> Stations()
        {
            if (Time.time >= _nextScan) Rescan();
            _stations.RemoveAll(s => s == null);
            return _stations;
        }

        /// <summary>
        /// The hall has a cook: one hired here, loaded or not. Not known (its camp isn't loaded) counts as yes, so a station
        /// in a hall whose cook may well be away holds its food rather than burn it.
        /// </summary>
        public bool HasCook
        {
            get
            {
                if (_workers == null) return false;
                WorkerModule.WorkerState state = _workers.FindWorker(out Citizens.Citizen loaded, out RosterEntry elsewhere);
                return state != WorkerModule.WorkerState.None;
            }
        }

        /// <summary>The colony's Mead Hall (one per colony), or null.</summary>
        public static MeadHallModule For(string colonyId) =>
            string.IsNullOrEmpty(colonyId) ? null : s_all.FirstOrDefault(h => h != null && h.IsValid && h._building.ColonyId == colonyId);

        /// <summary>The Mead Hall a station stands in, or null; worked out from where they stand, not from the last look.</summary>
        public static MeadHallModule Of(CookingStation station) =>
            station == null ? null : s_all.FirstOrDefault(h => h != null && h.IsValid && h.Holds(station.transform.position));

        /// <summary>A spot inside the hall: within its design's walls, or near its chest while it has no design.</summary>
        public bool Holds(Vector3 point)
        {
            float floor = transform.position.y;
            if (point.y < floor - Below || point.y > floor + Above) return false;
            Footprint room = _site != null ? _site.RoomFootprint : null;
            if (room != null) return room.Contains(V(point));
            return Utils.DistanceXZ(point, transform.position) <= MeadHallRules.StationsNear;
        }

        /// <summary>
        /// Every item a station makes, the hot food (<see cref="HotFood"/>): what each cooking station (an oven too) turns
        /// its raw food into, and every recipe made at the cauldron. Read once from the game's lists.
        /// </summary>
        public static ICollection<string> Cooked
        {
            get
            {
                if (s_cooked != null && s_cooked.Count > 0) return s_cooked;
                var cooked = new HashSet<string>();
                if (ZNetScene.instance != null)
                    foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
                    {
                        CookingStation station = prefab != null ? prefab.GetComponent<CookingStation>() : null;
                        if (station == null || station.m_conversion == null) continue;
                        foreach (CookingStation.ItemConversion c in station.m_conversion)
                            if (c != null && c.m_to != null) cooked.Add(c.m_to.gameObject.name);
                    }
                if (ObjectDB.instance != null)
                    foreach (Recipe recipe in ObjectDB.instance.m_recipes)
                        if (recipe != null && recipe.m_item != null && recipe.m_craftingStation != null
                            && (recipe.m_craftingStation.m_name == "$piece_cauldron" || recipe.m_craftingStation.m_name == "$piece_oven"))
                            cooked.Add(recipe.m_item.gameObject.name);
                s_cooked = cooked;
                return cooked;
            }
        }

        private static readonly Dictionary<CookingStation, Dictionary<string, CookingStation.ItemConversion>> s_takes =
            new Dictionary<CookingStation, Dictionary<string, CookingStation.ItemConversion>>();

        /// <summary>The raw foods a station takes, by name, and what each becomes (worked out once a station).</summary>
        public static Dictionary<string, CookingStation.ItemConversion> Takes(CookingStation station)
        {
            if (station == null) return new Dictionary<string, CookingStation.ItemConversion>();
            if (s_takes.TryGetValue(station, out Dictionary<string, CookingStation.ItemConversion> known)) return known;
            if (s_takes.Count > 32) foreach (CookingStation gone in s_takes.Keys.Where(s => s == null).ToList()) s_takes.Remove(gone);
            var takes = new Dictionary<string, CookingStation.ItemConversion>();
            s_takes[station] = takes;
            if (station.m_conversion == null) return takes;
            foreach (CookingStation.ItemConversion c in station.m_conversion)
                if (c != null && c.m_from != null && c.m_to != null && !takes.ContainsKey(c.m_from.gameObject.name)) takes[c.m_from.gameObject.name] = c;
            return takes;
        }

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            _building = GetComponent<ColonyBuilding>();
            _site = GetComponent<Construction.ConstructionModule>();
            _workers = GetComponent<WorkerModule>();
            if (_nview != null && _nview.IsValid()) s_all.Add(this);
        }

        private void OnDestroy() => s_all.Remove(this);

        /// <summary>Looks for the stations inside now (the test, after placing one; else every few seconds).</summary>
        public void Rescan()
        {
            _nextScan = Time.time + MeadHallRules.ScanSeconds;
            if (!IsValid) return;
            if (s_mask == 0) s_mask = LayerMask.GetMask("piece", "piece_nonsolid", "Default_small");
            Footprint room = _site != null ? _site.RoomFootprint : null;
            Vector3 centre = room != null ? new Vector3(room.Centre.X, transform.position.y, room.Centre.Z) : transform.position;
            float radius = room != null ? Mathf.Sqrt(room.HalfX * room.HalfX + room.HalfZ * room.HalfZ) + 1f : MeadHallRules.StationsNear + 1f;
            int count = Physics.OverlapSphereNonAlloc(centre, radius, s_hits, s_mask, QueryTriggerInteraction.Collide);
            while (count == s_hits.Length)
            {
                s_hits = new Collider[s_hits.Length * 2];
                count = Physics.OverlapSphereNonAlloc(centre, radius, s_hits, s_mask, QueryTriggerInteraction.Collide);
            }
            var found = new List<CookingStation>();
            for (int i = 0; i < count; i++)
            {
                CookingStation station = s_hits[i] != null ? s_hits[i].GetComponentInParent<CookingStation>() : null;
                if (station == null || found.Contains(station)) continue;
                ZNetView view = station.GetComponent<ZNetView>();
                if (view == null || !view.IsValid() || !Holds(station.transform.position)) continue;
                found.Add(station);
            }
            Vector3 me = transform.position;
            found = found.OrderBy(s => (s.transform.position - me).sqrMagnitude).ToList();
            bool changed = found.Count != _stations.Count || found.Any(s => !_stations.Contains(s));
            _stations.Clear();
            _stations.AddRange(found);
            if (changed) Log.Info($"{_building.TypeId} {_building.Id}: {found.Count} cooking stations inside");
        }
    }
}
