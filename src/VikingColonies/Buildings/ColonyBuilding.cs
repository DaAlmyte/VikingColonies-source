using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VikingColonies.Core;
using VikingColonies.Citizens;
using VikingColonies.Colony;
using VikingColonies.Config;

namespace VikingColonies.Buildings
{
    /// <summary>Every colony building: id, colony, type, level. Modules (storage, worker, town hall) sit beside it.</summary>
    public class ColonyBuilding : MonoBehaviour
    {
        public const int DataVersion = 2;
        private const string IdKey = SaveKeys.BuildingId;
        private const string ColonyKey = SaveKeys.ColonyId;      // the same key v1's Colony Hall used
        private const string HallLostKey = SaveKeys.HallLostAt;  // game time a hut found its Town Hall gone
        private const string TookOverKey = SaveKeys.TookOver;      // a Town Hall that took over a colony that had lost its own
        private const string ColonyOwnerKey = SaveKeys.ColonyOwner; // who built the colony's Town Hall, as a hut last saw it
        private const string ColonyPosKey = SaveKeys.ColonyPos;
        private const string ColonyLevelKey = SaveKeys.ColonyLevel;   // its Town Hall's level, as a hut last saw it (1.4)
        private const string TypeKey = SaveKeys.BuildingType;
        public const string LevelKey = SaveKeys.Level;
        private const string VersionKey = SaveKeys.DataVersion;

        private static readonly List<ColonyBuilding> s_all = new List<ColonyBuilding>();

        /// <summary>Set on the prefab when the piece is registered.</summary>
        public string TypeId = "";

        private ZNetView _nview;

        public static IReadOnlyList<ColonyBuilding> All => s_all;

        public ZNetView View => _nview;
        public bool IsValid => _nview != null && _nview.IsValid();
        public bool IsOwner => IsValid && _nview.IsOwner();
        private ZDO Zdo => IsValid ? _nview.GetZDO() : null;

        public string Id => Zdo?.GetString(IdKey, "") ?? "";
        public string ColonyId => Zdo?.GetString(ColonyKey, "") ?? "";
        public Vector3 ColonyPos => Zdo?.GetVec3(ColonyPosKey, Vector3.zero) ?? Vector3.zero;
        public bool HasColonyPos => ColonyPos != Vector3.zero;

        /// <summary>The colony's Town Hall level as this hut last saw it (1.4); 1 when it never saw one.</summary>
        public int ColonyLevel => Zdo != null ? Math.Max(1, Zdo.GetInt(ColonyLevelKey, 1)) : 1;

        /// <summary>Owner only: remembers the Town Hall's level.</summary>
        public void SetColonyLevel(int level)
        {
            if (!IsOwner || ColonyLevel == level) return;
            Zdo.Set(ColonyLevelKey, TownHallLevels.Clamp(level));
        }
        public int Level => Zdo?.GetInt(LevelKey, 1) ?? 1;
        public BuildingType Type => BuildingTypes.Get(TypeId);
        public bool IsTownHall => TypeId == BuildingTypes.TownHallId;
        public bool IsStorehouse => TypeId == BuildingTypes.StorehouseId;
        public StorageModule Storage { get; private set; }

        /// <summary>
        /// The building's chests: its own first, then for a storehouse every chest standing inside it (1.5). Never a
        /// destroyed one.
        /// </summary>
        public IReadOnlyList<StorageModule> Stores
        {
            get
            {
                var stores = new List<StorageModule>();
                if (Storage != null) stores.Add(Storage);
                if (_storehouse != null && _storehouse.Open)   // not while it's being taken down: its chests go with it
                    foreach (StorageModule chest in _storehouse.Chests)
                        if (chest != null && chest != Storage) stores.Add(chest);
                return stores;
            }
        }

        private StorehouseModule _storehouse;

        /// <summary>A storehouse's store (null for every other building).</summary>
        public StorehouseModule Storehouse => _storehouse;
        /// <summary>Its Keep in stock list (0.21).</summary>
        public StockModule Stock { get; private set; }

        /// <summary>True when this building was placed during this session (it had no saved id yet).</summary>
        public bool PlacedThisSession { get; private set; }

        public static ColonyBuilding FindById(string id) =>
            string.IsNullOrEmpty(id) ? null : s_all.FirstOrDefault(b => b != null && b.Id == id);

        public static ColonyBuilding FindTownHall(string colonyId) =>
            string.IsNullOrEmpty(colonyId) ? null : s_all.FirstOrDefault(b => b != null && b.IsTownHall && b.ColonyId == colonyId);

        /// <summary>The colony's storehouse (one per colony, 1.5), or null.</summary>
        public static ColonyBuilding FindStorehouse(string colonyId) =>
            string.IsNullOrEmpty(colonyId) ? null : s_all.FirstOrDefault(b => b != null && b.IsValid && b.IsStorehouse && b.ColonyId == colonyId);

        public static IEnumerable<ColonyBuilding> TownHalls() => s_all.Where(b => b != null && b.IsTownHall && b.IsValid);

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            Storage = GetComponent<StorageModule>();
            Stock = GetComponent<StockModule>();
            _storehouse = GetComponent<StorehouseModule>();
            if (!IsValid) return;   // the build-preview ghost has no network object
            bool isNew = false;
            if (_nview.IsOwner()) isNew = EnsureSavedData();
            PlacedThisSession = isNew;
            s_all.Add(this);
            if (isNew) ColonyEvents.RaiseBuildingPlaced(this);
        }

        private void Start()
        {
            WearNTear wear = GetComponent<WearNTear>();
            if (wear != null) wear.m_onDestroyed += () => ColonyEvents.RaiseBuildingDestroyed(this);
        }

        private void OnDestroy() => s_all.Remove(this);

        /// <summary>When this hut found its colony's Town Hall gone (game seconds); <see cref="ColonyGrace.NotLost"/> while it stands.</summary>
        public double HallLostAt => Zdo != null ? Zdo.GetFloat(HallLostKey, (float)ColonyGrace.NotLost) : ColonyGrace.NotLost;

        /// <summary>Owner only: marks (or, with NotLost, clears) when its Town Hall was found gone.</summary>
        public void SetHallLostAt(double at)
        {
            if (!IsOwner || Math.Abs(HallLostAt - at) < 0.5) return;
            Zdo.Set(HallLostKey, (float)at);
        }

        /// <summary>Days left to build a new Town Hall before this hut's colony ends; 0 while its Town Hall stands.</summary>
        public int GraceDaysLeft =>
            ColonyGrace.IsLost(HallLostAt) && ZNet.instance != null
                ? ColonyGrace.DaysLeft(HallLostAt, ZNet.instance.GetTimeSeconds(), ColonyConfig.TownHallGraceDays.Value, GraceDaySeconds) : 0;

        /// <summary>A day in this world, in game seconds (some mods change it).</summary>
        public static double GraceDaySeconds => EnvMan.instance != null && EnvMan.instance.m_dayLengthSec > 0 ? EnvMan.instance.m_dayLengthSec : ColonyGrace.DaySeconds;

        /// <summary>Who built this hut's colony's Town Hall, as the hut last saw it (kept while the Town Hall is gone).</summary>
        public long ColonyOwner => Zdo != null ? Zdo.GetLong(ColonyOwnerKey, 0L) : 0L;

        /// <summary>Owner only: remembers who built the colony's Town Hall.</summary>
        public void SetColonyOwner(long owner)
        {
            if (!IsOwner || ColonyOwner == owner) return;
            Zdo.Set(ColonyOwnerKey, owner);
        }

        /// <summary>
        /// Who built a colony's Town Hall: its creator while it stands, otherwise as one of the colony's huts remembers it
        /// (the Town Hall is gone, within the grace time); 0 when nobody knows.
        /// </summary>
        public static long OwnerOfColony(string colonyId)
        {
            if (string.IsNullOrEmpty(colonyId)) return 0L;
            ColonyBuilding hall = FindTownHall(colonyId);
            Piece piece = hall != null ? hall.GetComponent<Piece>() : null;
            if (piece != null) return piece.GetCreator();
            foreach (ColonyBuilding b in s_all)
                if (b != null && b.ColonyId == colonyId && b.ColonyOwner != 0L) return b.ColonyOwner;
            return 0L;
        }

        /// <summary>This Town Hall took over a colony that had lost its own (it adds no new settlers).</summary>
        public bool TookOver => Zdo != null && Zdo.GetBool(TookOverKey, false);

        public void SetColony(string colonyId, Vector3 hallPos)
        {
            if (!IsOwner) return;
            if ((colonyId ?? "") != ColonyId) Zdo.Set(ColonyLevelKey, 1);   // another colony: its own level, once seen (review of 1.4)
            Zdo.Set(ColonyKey, colonyId ?? "");
            Zdo.Set(ColonyPosKey, string.IsNullOrEmpty(colonyId) ? Vector3.zero : hallPos);
        }

        /// <summary>Owner only: the hut's level (1–5), set when an upgrade is finished.</summary>
        public void SetLevel(int level)
        {
            if (!IsOwner) return;
            Zdo.Set(LevelKey, HutLevels.Clamp(level));
        }

        /// <summary>The colony that lost its Town Hall within the grace time with the most huts around here, or null.</summary>
        private string OrphanColonyHere()
        {
            if (ZNet.instance == null) return null;
            double now = ZNet.instance.GetTimeSeconds();
            var huts = new List<OrphanHut>();
            foreach (ColonyBuilding b in s_all)
            {
                if (b == null || b == this || b.IsTownHall || string.IsNullOrEmpty(b.ColonyId) || FindTownHall(b.ColonyId) != null) continue;
                // A hut that hasn't noticed yet (it takes half a minute) lost its Town Hall just now, if its Town Hall's
                // spot is loaded here (with a wide colony radius, a Town Hall out of view may well stand).
                if (!ColonyGrace.IsLost(b.HallLostAt) && !(b.HasColonyPos && ZNetScene.instance.IsAreaReady(b.ColonyPos))) continue;
                double lostAt = ColonyGrace.IsLost(b.HallLostAt) ? b.HallLostAt : now;
                huts.Add(new OrphanHut(b.ColonyId, new GroundPoint(b.transform.position.x, b.transform.position.z), lostAt));
            }
            // Settlers count as well: a colony that lost its Town Hall before it had a hut is taken over too.
            foreach (Citizen c in Citizen.All)
            {
                if (c == null || !c.Record.HasColony || FindTownHall(c.Record.ColonyId) != null) continue;
                float[] hall = c.Record.ColonyPos;
                bool marked = ColonyGrace.IsLost(c.Record.HallLostAt);
                if (!marked && !(hall != null && hall.Length == 3 && ZNetScene.instance.IsAreaReady(new Vector3(hall[0], hall[1], hall[2])))) continue;
                huts.Add(new OrphanHut(c.Record.ColonyId, new GroundPoint(c.transform.position.x, c.transform.position.z), marked ? c.Record.HallLostAt : now));
            }
            // The widest of the orphans' areas (1.4: they remember their Town Hall's level; review: not a neighbour's).
            var orphanColonies = new HashSet<string>(huts.Select(h => h.ColonyId));
            int level = s_all.Where(b => b != null && b != this && !b.IsTownHall && orphanColonies.Contains(b.ColonyId)).Select(b => b.ColonyLevel).DefaultIfEmpty(1).Max();
            return TownHallTakeover.Choose(huts, new GroundPoint(transform.position.x, transform.position.z), HallRules.PerksAt(level).Radius,
                now, ColonyConfig.TownHallGraceDays.Value, GraceDaySeconds);
        }

        /// <returns>True when this building was just placed (it had no id yet).</returns>
        private bool EnsureSavedData()
        {
            ZDO zdo = Zdo;
            bool isNew = string.IsNullOrEmpty(zdo.GetString(IdKey, ""));
            if (isNew) zdo.Set(IdKey, Guid.NewGuid().ToString("N"));
            if (zdo.GetString(TypeKey, "") != TypeId) zdo.Set(TypeKey, TypeId);
            if (zdo.GetInt(LevelKey, 0) < 1) zdo.Set(LevelKey, 1);
            if (IsTownHall)
            {
                // A Town Hall's colony is its own (v1 Colony Halls already have one); a new one placed among the huts of a
                // colony that lost its Town Hall within the grace time takes that colony over.
                if (string.IsNullOrEmpty(zdo.GetString(ColonyKey, "")))
                {
                    string orphan = isNew ? OrphanColonyHere() : null;
                    zdo.Set(ColonyKey, orphan ?? Guid.NewGuid().ToString("N"));
                    if (orphan != null)
                    {
                        zdo.Set(TookOverKey, true);
                        // The colony keeps the level its lost Town Hall had reached (1.4): its huts remember it.
                        int kept = s_all.Where(b => b != null && b != this && b.ColonyId == orphan).Select(b => b.ColonyLevel).DefaultIfEmpty(1).Max();
                        if (kept > 1) zdo.Set(LevelKey, TownHallLevels.Clamp(kept));
                        Log.Info($"The new Town Hall at ({transform.position.x:0}, {transform.position.z:0}) takes over colony {orphan}, which had lost its own.");
                    }
                }
                if (zdo.GetVec3(ColonyPosKey, Vector3.zero) != transform.position) zdo.Set(ColonyPosKey, transform.position);
            }
            if (zdo.GetInt(VersionKey, 0) < DataVersion) zdo.Set(VersionKey, DataVersion);   // v1 -> v2: nothing else to convert
            return isNew;
        }
    }
}
