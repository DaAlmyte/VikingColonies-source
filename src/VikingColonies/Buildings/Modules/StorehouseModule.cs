using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VikingColonies.Construction;
using VikingColonies.Core;
using static VikingColonies.Construction.CoreVectors;

namespace VikingColonies.Buildings
{
    /// <summary>
    /// The storehouse's store (1.5): every plain chest standing inside its walls (<see cref="StorehouseRules.Joins"/>), found
    /// again every few seconds and on a level change, on every computer. Each one gets a <see cref="StorageModule"/>, so a
    /// settler on any computer takes from it and puts into it through the safe hand-over, as with a hut's own chest.
    /// </summary>
    public class StorehouseModule : MonoBehaviour
    {
        private static int s_mask;
        private static Collider[] s_hits = new Collider[256];

        /// <summary>vc_storehousetest: a tiny search buffer, to check a full one still finds every chest.</summary>
        internal static void ShrinkBufferForTest(int size) => s_hits = new Collider[size];

        private ZNetView _nview;
        private ColonyBuilding _building;
        private ConstructionModule _site;
        private readonly List<StorageModule> _chests = new List<StorageModule>();
        private readonly List<Piece> _standing = new List<Piece>();
        private float _nextScan;
        private int _level = -1;

        /// <summary>The chests standing inside (its own left out), nearest first; never a destroyed one.</summary>
        public IReadOnlyList<StorageModule> Chests
        {
            get
            {
                _chests.RemoveAll(c => c == null);   // a chest broken or removed since the last look
                return _chests;
            }
        }

        /// <summary>
        /// Every chest a player stood inside its walls, a personal one too (the store's and the rest): its upgrade keeps them
        /// standing (its review). Never a destroyed one.
        /// </summary>
        public IReadOnlyList<Piece> StandingInside
        {
            get
            {
                _standing.RemoveAll(p => p == null);
                return _standing;
            }
        }

        /// <summary>
        /// Its store takes and gives, except while the storehouse is being taken down (its chests go with it) or emptied by
        /// its couriers before that (part 3).
        /// </summary>
        public bool Open => _site == null || (!_site.IsTakingDown && !_site.IsClosingStore);

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            _building = GetComponent<ColonyBuilding>();
            _site = GetComponent<ConstructionModule>();
        }

        private void Update()
        {
            if (_nview == null || !_nview.IsValid() || _building == null) return;
            int level = _building.Level;
            if (Time.time < _nextScan && level == _level) return;
            _level = level;
            Rescan();
            if (_site != null && _nview.IsOwner()) _site.TakeDownOnceEmptied();   // emptied by its couriers: taken down (part 3)
        }

        /// <summary>Looks for the chests inside now (the test, after placing or breaking one; else every few seconds).</summary>
        public void Rescan()
        {
            _nextScan = Time.time + StorehouseRules.ScanSeconds;
            Footprint room = _site != null ? _site.RoomFootprint : null;
            if (room == null) return;
            Footprint walls = room.Grow(-SiteSurvey.SiteMargin);   // inside its walls: a chest against the outside doesn't join
            if (s_mask == 0) s_mask = LayerMask.GetMask("piece", "piece_nonsolid", "vehicle");
            float floor = transform.position.y;
            const float below = 0.5f, above = 8f;
            var centre = new Vector3(room.Centre.X, floor + (above - below) / 2f, room.Centre.Z);
            var half = new Vector3(room.HalfX + 0.5f, (above + below) / 2f, room.HalfZ + 0.5f);
            int count = Physics.OverlapBoxNonAlloc(centre, half, s_hits, U(room.Rotation), s_mask, QueryTriggerInteraction.Collide);
            while (count == s_hits.Length)   // full: some may be left out, so a bigger one and again (its review: a big storehouse)
            {
                s_hits = new Collider[s_hits.Length * 2];
                count = Physics.OverlapBoxNonAlloc(centre, half, s_hits, U(room.Rotation), s_mask, QueryTriggerInteraction.Collide);
            }

            var found = new List<StorageModule>();
            _standing.Clear();
            for (int i = 0; i < count; i++)
            {
                Container chest = s_hits[i] != null ? s_hits[i].GetComponentInParent<Container>() : null;
                if (chest == null || chest.gameObject == gameObject) continue;
                ZNetView view = chest.GetComponent<ZNetView>();
                if (view == null || !view.IsValid()) continue;
                bool inside = walls.Contains(V(chest.transform.position));
                Piece piece = chest.GetComponent<Piece>();
                bool colonyBuilding = chest.GetComponent<ColonyBuilding>() != null;
                bool onVehicle = chest.GetComponent<Vagon>() != null || chest.GetComponentInParent<Ship>() != null;
                // Any chest standing inside, a personal one too, stays through an upgrade (its review).
                float aboveFloor = chest.transform.position.y - floor;   // not on the ground under a raised floor (the follow-ups' review)
                if (StorehouseRules.Joins(inside, piece != null, colonyBuilding, false, onVehicle, aboveFloor) && !_standing.Contains(piece)) _standing.Add(piece);
                bool joins = StorehouseRules.Joins(inside, piece != null, colonyBuilding, chest.m_privacy == Container.PrivacySetting.Private, onVehicle, aboveFloor);
                if (!joins) continue;
                StorageModule storage = chest.GetComponent<StorageModule>() ?? chest.gameObject.AddComponent<StorageModule>();
                if (!found.Contains(storage)) found.Add(storage);
            }
            Vector3 me = transform.position;
            found = found.OrderBy(s => (s.transform.position - me).sqrMagnitude).ToList();

            List<StorageModule> before = Chests.ToList();
            int joined = found.Count(s => !before.Contains(s));
            int left = before.Count(s => !found.Contains(s));
            _chests.Clear();
            _chests.AddRange(found);
            if (joined > 0 || left > 0)
                Log.Info($"{_building.TypeId} {_building.Id} store: {found.Count} chests inside ({joined} joined, {left} left)");
        }
    }
}
