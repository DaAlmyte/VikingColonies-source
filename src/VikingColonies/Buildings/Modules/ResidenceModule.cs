using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VikingColonies.Citizens;
using VikingColonies.Construction;
using VikingColonies.Core;

namespace VikingColonies.Buildings
{
    /// <summary>
    /// A Longhouse: the beds of its building (the plan's beds that stand) are homes. On its owner it looks every few
    /// seconds at the colony's beds and settlers (<see cref="Homes"/>) and asks a settler's computer to give him one of
    /// its beds, or to take back a bed of its that's no longer his.
    /// </summary>
    internal sealed class ResidenceModule : MonoBehaviour
    {
        private const float TickSeconds = 5f;
        /// <summary>Finding the beds that stand walks every loaded piece: the answer is kept this long.</summary>
        private const float BedsCacheSeconds = 2f;
        /// <summary>A Longhouse's beds load in after its chest: no sharing out until it has been here this long.</summary>
        private const float SettleSeconds = 10f;
        private static readonly List<Piece> s_near = new List<Piece>();
        private static readonly List<ResidenceModule> s_all = new List<ResidenceModule>();

        private ZNetView _nview;
        private ColonyBuilding _building;
        private ConstructionModule _site;
        private float _timer = 2f;
        private float _awakeAt;
        private List<HomeBed> _beds;
        private float _bedsAt = -100f;

        /// <summary>The Longhouses loaded on this computer.</summary>
        public static IReadOnlyList<ResidenceModule> All => s_all;

        public ColonyBuilding Building => _building;

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            _building = GetComponent<ColonyBuilding>();
            _site = GetComponent<ConstructionModule>();
            _awakeAt = Time.time;
            s_all.Add(this);
        }

        private void OnDestroy() => s_all.Remove(this);

        /// <summary>This Longhouse's beds that stand: "longhouse id#plan index" and where each is.</summary>
        public List<HomeBed> Beds()
        {
            if (_beds != null && Time.time - _bedsAt < BedsCacheSeconds) return _beds;
            var beds = new List<HomeBed>();
            List<PlannedPiece> planned = _site == null || _building == null ? new List<PlannedPiece>()
                : _site.Plan.Where(p => p.IsValid && PiecePlacer.Prefab(p.Prefab)?.GetComponent<Bed>() != null).ToList();
            if (planned.Count > 0)
            {
                // One look round all the beds at once: finding pieces by radius walks every loaded piece in the world.
                Vector3 centre = Vector3.zero;
                foreach (PlannedPiece piece in planned) centre += At(piece);
                centre /= planned.Count;
                float radius = planned.Max(p => Vector3.Distance(At(p), centre)) + 1f;
                s_near.Clear();
                Piece.GetAllPiecesInRadius(centre, radius, s_near);
                foreach (PlannedPiece piece in planned)
                    if (PiecePlacer.FindStanding(piece, s_near) != null)
                        beds.Add(new HomeBed(BedId(piece.Index), new GroundPoint(piece.Position.X, piece.Position.Z)));
            }
            _beds = beds;
            _bedsAt = Time.time;
            return beds;
        }

        private static Vector3 At(PlannedPiece piece) => new Vector3(piece.Position.X, piece.Position.Y, piece.Position.Z);

        /// <summary>Where a bed of this Longhouse stands, in the world (null when it doesn't).</summary>
        public Vector3? BedPosition(string bedId)
        {
            if (_site == null) return null;
            foreach (PlannedPiece piece in _site.Plan)
                if (BedId(piece.Index) == bedId) return new Vector3(piece.Position.X, piece.Position.Y, piece.Position.Z);
            return null;
        }

        public string BedId(int planIndex) => $"{_building.Id}#{planIndex}";

        private static int s_solidMask;

        /// <summary>
        /// Where a settler spends the night by this bed of ours: of the four spots around it (<see cref="BedSpot"/>), the
        /// first that nothing solid stands on and that <paramref name="walker"/>'s way from inside the door reaches.
        /// </summary>
        public Vector3? SleepSpot(string bedId, AI.CitizenAI walker)
        {
            if (_site == null || _building == null) return null;
            PlannedPiece bed = _site.Plan.FirstOrDefault(p => BedId(p.Index) == bedId);
            if (bed == null) return null;
            if (s_solidMask == 0) s_solidMask = LayerMask.GetMask("piece", "piece_nonsolid", "Default", "static_solid", "Default_small");
            Vector3 chest = transform.position, inside = InsideDoor();
            List<Vector3> spots = BedSpot.Candidates(bed.Position, bed.Rotation, CoreVectors.V(chest)).Select(CoreVectors.U).ToList();
            Vector3? free = null;
            foreach (Vector3 spot in spots)
            {
                bool clear = !Physics.CheckCapsule(spot + Vector3.up * 0.5f, spot + Vector3.up * 1.4f, 0.3f, s_solidMask, QueryTriggerInteraction.Ignore);
                // Seen in review: past a bed's head, beyond a thin wall, a spot outside the hut could be chosen.
                if (!clear || AI.Walls.StructureBetween(spot, chest)) continue;
                if (walker == null || walker.WayEndGap(inside, spot) < 0.6f) return spot;
                if (free == null) free = spot;
            }
            return free;   // none inside: he takes a spot by the chest, as in any hut
        }

        /// <summary>A step inside the Longhouse's door (its chest when it has no door).</summary>
        public Vector3 InsideDoor()
        {
            Vector3 chest = transform.position;
            Door door = DoorClearance.DoorsOf(_site).FirstOrDefault();
            if (door == null) return chest;
            float side = Mathf.Sign(Vector3.Dot(chest - door.transform.position, door.transform.forward));
            Vector3 inside = door.transform.position + door.transform.forward * side * 1.2f;
            inside.y = chest.y;
            return inside;
        }

        /// <summary>The bed itself, standing, for a bed id of this Longhouse (null when it doesn't stand).</summary>
        public Bed BedPiece(string bedId)
        {
            if (_site == null) return null;
            // Kept while it stands (0.18: each settler looked it up through the whole plan every few seconds at night); a
            // bed taken down reads as null at once (Unity's destroyed object), and is looked up again.
            if (_bedPieces.TryGetValue(bedId ?? "", out Bed known) && known != null) return known;
            PlannedPiece planned = _site.Plan.FirstOrDefault(p => p.IsValid && BedId(p.Index) == bedId);
            Piece piece = planned != null ? PiecePlacer.FindStanding(planned) : null;
            Bed bed = piece != null ? piece.GetComponentInChildren<Bed>() : null;
            if (bed != null) _bedPieces[bedId] = bed;
            return bed;
        }

        private readonly Dictionary<string, Bed> _bedPieces = new Dictionary<string, Bed>();

        /// <summary>A settler's bed (0.15: he lies in it at night), when its Longhouse is loaded here.</summary>
        public static Bed BedOf(string bedId)
        {
            string house = LonghouseOf(bedId);
            ResidenceModule module = s_all.FirstOrDefault(r => r != null && r._building != null && r._building.Id == house);
            return module != null ? module.BedPiece(bedId) : null;
        }

        /// <summary>His bed's number in its Longhouse ("Longhouse bed 2"), when it is loaded here; 0 otherwise.</summary>
        public static int BedNumberOf(string bedId)
        {
            string house = LonghouseOf(bedId);
            ResidenceModule module = s_all.FirstOrDefault(r => r != null && r._building != null && r._building.Id == house);
            // Over the plan's beds, standing or not: a broken bed 1 doesn't make bed 2 read "bed 1" (review).
            return module != null && module._site != null
                ? Homes.BedNumber(bedId, module._site.Plan.Where(p => p.IsValid && PiecePlacer.Prefab(p.Prefab)?.GetComponent<Bed>() != null).Select(p => module.BedId(p.Index)))
                : 0;
        }

        /// <summary>The sleeping spot for a bed id, when its Longhouse is loaded here.</summary>
        public static Vector3? SleepSpotOf(string bedId, AI.CitizenAI walker)
        {
            string house = LonghouseOf(bedId);
            ResidenceModule module = s_all.FirstOrDefault(r => r != null && r._building != null && r._building.Id == house);
            return module != null ? module.SleepSpot(bedId, walker) : null;
        }

        /// <summary>His bed of ours stands elsewhere than he thinks (the Longhouse went up a level with another design).</summary>
        private bool BedMoved(Citizen settler, string bed)
        {
            if (bed.Length == 0 || !bed.StartsWith(_building.Id + "#", StringComparison.Ordinal)) return false;
            Vector3? at = BedPosition(bed);
            float[] known = settler.Record.HomePos;
            return at.HasValue && (known == null || known.Length != 3 || Utils.DistanceXZ(at.Value, Citizen.ToVector3(known)) > 0.3f);
        }

        /// <summary>The Longhouse a bed id belongs to ("" for none).</summary>
        public static string LonghouseOf(string bedId)
        {
            int cut = bedId?.IndexOf('#') ?? -1;
            return cut > 0 ? bedId.Substring(0, cut) : "";
        }

        /// <summary>"Ingrid, free" — who sleeps in each bed, for the window.</summary>
        public string Describe()
        {
            List<HomeBed> beds = Beds();
            if (beds.Count == 0) return Localization.instance.Localize("$vc_no_beds_yet");
            var names = new List<string>();
            foreach (HomeBed bed in beds)
            {
                Citizen sleeper = Citizen.All.FirstOrDefault(c => c != null && c.Record.Home == bed.Id);
                names.Add(sleeper != null ? sleeper.CitizenName : Localization.instance.Localize("$vc_bed_free"));
            }
            return $"{beds.Count}: " + string.Join(", ", names);
        }

        private void Update()
        {
            if (_nview == null || !_nview.IsValid() || !_nview.IsOwner()) return;
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = TickSeconds;
            ShareOutBeds();
        }

        /// <summary>Every Longhouse works out the same sharing from the same beds and settlers, and asks for its own beds.</summary>
        private void ShareOutBeds()
        {
            string colony = _building.ColonyId;
            if (string.IsNullOrEmpty(colony)) return;
            // Just loaded, its beds may not be in yet: they'd look gone and every resident be sent away.
            if (Time.time - _awakeAt < SettleSeconds || ZNetScene.instance == null || !ZNetScene.instance.IsAreaReady(transform.position)) return;
            var beds = new List<HomeBed>();
            foreach (ResidenceModule house in s_all)
                if (house != null && house._building != null && house._building.ColonyId == colony) beds.AddRange(house.Beds());
            List<Citizen> settlers = Citizen.All.Where(c => c != null && c.Record.ColonyId == colony).ToList();
            var seekers = settlers.Select(c => new HomeSeeker(c.Id, new GroundPoint(c.transform.position.x, c.transform.position.z), c.Record.Home)).ToList();
            // Members beyond this computer's loaded area keep their beds, as the Town Hall's roster last saw them.
            TownHallModule hall = ColonyBuilding.FindTownHall(colony)?.GetComponent<TownHallModule>();
            if (hall != null) seekers.AddRange(Homes.OutOfSight(hall.Roster.Entries, new HashSet<string>(settlers.Select(c => c.Id))));
            Dictionary<string, string> homes = Homes.Assign(beds, seekers);
            string mine = _building.Id + "#";
            foreach (Citizen settler in settlers)
            {
                string bed = homes.TryGetValue(settler.Id, out string b) ? b : "";
                if (bed == settler.Record.Home && !BedMoved(settler, bed)) continue;
                if (bed.StartsWith(mine))
                {
                    Vector3? at = BedPosition(bed);
                    if (at.HasValue) settler.RequestHome(colony, bed, at.Value);
                }
                else if (bed.Length == 0 && settler.Record.Home.StartsWith(mine))
                    settler.RequestHome(colony, "", Vector3.zero);   // his bed of ours is gone, or another settler has it
            }
        }
    }
}
