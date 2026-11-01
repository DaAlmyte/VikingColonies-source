using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Citizens;
using VikingColonies.Config;
using VikingColonies.Core;

namespace VikingColonies.Colony
{
    /// <summary>A seat in the colony: any piece with the game's seat part (a bench, a chair, a stool, a throne, a mod's seat).</summary>
    internal sealed class SeatPlace
    {
        public Chair Chair;
        public bool Roofed;
        public string Id;

        public bool Valid => Chair != null && Chair.m_attachPoint != null;
        public Vector3 Point => Chair.m_attachPoint.position;
    }

    /// <summary>A fire in the colony: a campfire, hearth or bonfire (the game's Fire comfort group), not a torch.</summary>
    internal sealed class FirePlace
    {
        public Fireplace Fire;
        public bool Roofed;
        public string Id;
        /// <summary>The Town Hall camp's own fire (huts round 2): the colony gathers there first.</summary>
        public bool AtHall;
        /// <summary>Part of one of the colony's huts (the Longhouse's fire pit), not the camp's: not the colony's evening fire (1.4.1).</summary>
        public bool InHut;
        /// <summary>How far it reaches from its middle on the ground (a bonfire about 2.2 m): the evening's ring stands clear.</summary>
        public float Radius;
        /// <summary>A cook's fire (1.6): a cooking station or cauldron stands over it; no one stands by it in free time.</summary>
        public bool Cooks;

        public bool Valid => Fire != null;
        public bool Burning => Fire != null && Fire.IsBurning();
        public Vector3 Position => Fire.transform.position;
    }

    /// <summary>
    /// A piece a pastime is played at (1.6): an archery target, a training dummy, a table with chairs, a maypole or banner
    /// pole, a trophy or armour stand, a cartography table; or one of the colony's tame animals (petting). Its spots are
    /// where they stand to play, on the open side of it (not behind the wall a stand hangs on).
    /// </summary>
    internal sealed class PropPlace
    {
        public Prop Kind;
        public Piece Piece;
        /// <summary>A tame animal of the colony (petting): no piece; it walks about, so its spot is worked out on the way.</summary>
        public Character Creature;
        public string Id;
        public bool Roofed;
        public int Capacity;
        /// <summary>Where they stand to play it (a table's: its chairs' points).</summary>
        public List<Vector3> Spots = new List<Vector3>();
        /// <summary>A table's chairs (a board game sits on them).</summary>
        public List<SeatPlace> Seats = new List<SeatPlace>();
        /// <summary>A table's places to stand round it (a toast).</summary>
        public List<Vector3> Around = new List<Vector3>();

        public bool Valid => Piece != null || Creature != null && !Creature.IsDead();
        public Vector3 Position => Piece != null ? Piece.transform.position : Creature != null ? Creature.transform.position : Vector3.zero;
    }

    /// <summary>
    /// The seats, fires and pastimes' pieces inside each colony's area (settler life, 0.14; pieces 1.6), looked up at most
    /// every 10 seconds for each colony and shared by all its settlers on this computer.
    /// </summary>
    internal static class ColonyPlaces
    {
        private const float RefreshSeconds = 10f;
        /// <summary>A ray straight up from this high over a seat or fire finds its roof within <see cref="RoofReach"/> (review of
        /// 0.14: from 2 m it started above a low roof by a wall).</summary>
        private const float RoofFrom = 1.2f;
        /// <summary>A player sitting this near a seat's point sits on it: a settler there gets up.</summary>
        private const float SittingOn = 0.2f;
        private const float RoofReach = 10f;

        private sealed class Places
        {
            public float At = float.NegativeInfinity;
            public readonly List<SeatPlace> Seats = new List<SeatPlace>();
            public readonly List<FirePlace> Fires = new List<FirePlace>();
            public readonly List<PropPlace> Props = new List<PropPlace>();
        }

        private static readonly Dictionary<string, Places> s_byColony = new Dictionary<string, Places>();
        private static readonly List<Piece> s_pieces = new List<Piece>();
        private static readonly List<Chair> s_chairs = new List<Chair>();
        private static readonly List<GroundPoint> s_overFires = new List<GroundPoint>();
        private static int s_roofMask;

        public static IReadOnlyList<SeatPlace> Seats(string colonyId) => For(colonyId).Seats;

        public static IReadOnlyList<FirePlace> Fires(string colonyId) => For(colonyId).Fires;

        /// <summary>The pieces (and tame animals) the colony's pastimes are played at (1.6).</summary>
        public static IReadOnlyList<PropPlace> Props(string colonyId) => For(colonyId).Props;

        /// <summary>Self-test: look again now (a seat or fire was just placed).</summary>
        public static void Forget() => s_byColony.Clear();

        /// <summary>
        /// Someone else is on the seat or at it, so a settler doesn't pick it: a player or another settler within
        /// <see cref="FreeTimeRules.SeatNearPlayer"/> of its point. A seat a settler walks to is claimed by <see cref="FreeTimeClaims"/>.
        /// </summary>
        public static bool Taken(SeatPlace seat, Citizen me) => TakenBy(seat, me) != null;

        /// <summary>Who is at the seat (<see cref="Taken"/>): a player's or settler's name, "nobody's" for a gone seat, or null.</summary>
        public static string TakenBy(SeatPlace seat, Citizen me)
        {
            if (!seat.Valid) return "nobody's";
            Vector3 point = seat.Point;
            foreach (Player player in Player.GetAllPlayers())
                if (player != null && Vector3.Distance(player.transform.position, point) <= FreeTimeRules.SeatNearPlayer) return player.GetPlayerName();
            foreach (Citizen c in Citizen.All)
            {
                if (c == null || c == me) continue;
                if (Vector3.Distance(c.transform.position, point) <= FreeTimeRules.SeatNearPlayer) return string.IsNullOrEmpty(c.CitizenName) ? "a settler" : c.CitizenName;
            }
            return null;
        }

        /// <summary>A player sits right on this seat (a seated settler then gets up; one beside him keeps his seat).</summary>
        public static bool PlayerSitsOn(SeatPlace seat)
        {
            if (!seat.Valid) return false;
            Vector3 point = seat.Point;
            foreach (Player player in Player.GetAllPlayers())
                if (player != null && player.IsAttached() && Vector3.Distance(player.transform.position, point) <= SittingOn) return true;
            return false;
        }

        private static Places For(string colonyId)
        {
            if (string.IsNullOrEmpty(colonyId)) return new Places();
            if (!s_byColony.TryGetValue(colonyId, out Places places)) s_byColony[colonyId] = places = new Places();
            if (Time.time - places.At < RefreshSeconds) return places;
            long timer = Perf.Start();
            Refresh(colonyId, places);
            Perf.Stop("settler: finding seats, fires and pieces", timer);
            return places;
        }

        private static void Refresh(string colonyId, Places places)
        {
            places.At = Time.time;
            places.Seats.Clear();
            places.Fires.Clear();
            places.Props.Clear();
            ColonyBuilding hall = ColonyBuilding.FindTownHall(colonyId);
            if (hall == null) return;
            s_tables.Clear();
            s_poles.Clear();
            s_banners.Clear();
            if (s_roofMask == 0) s_roofMask = LayerMask.GetMask("piece", "piece_nonsolid", "Default_small", "static_solid");
            s_pieces.Clear();
            Piece.GetAllPiecesInRadius(hall.transform.position, HallRules.PerksAt(hall.Level).Radius, s_pieces);
            s_overFires.Clear();
            MeadHallModule meadHall = MeadHallModule.For(colonyId);   // a cook's fire is only ever in the colony's Mead Hall
            foreach (Piece piece in s_pieces)
            {
                if (piece == null) continue;
                s_chairs.Clear();
                piece.GetComponentsInChildren(s_chairs);
                foreach (Chair chair in s_chairs)
                {
                    if (chair == null || chair.m_inShip || chair.m_attachPoint == null) continue;
                    places.Seats.Add(new SeatPlace { Chair = chair, Roofed = Roofed(chair.m_attachPoint.position, piece), Id = "seat" + chair.GetInstanceID() });
                }
                if (meadHall != null)
                {
                    CookingStation cooking = piece.GetComponent<CookingStation>();
                    CraftingStation crafting = piece.GetComponent<CraftingStation>();
                    bool needsFire = (cooking != null && cooking.m_requireFire) || (crafting != null && crafting.m_craftRequireFire);
                    if (FreeTimeRules.IsCooksStation(needsFire, needsFire && meadHall.Holds(piece.transform.position)))
                        s_overFires.Add(new GroundPoint(piece.transform.position.x, piece.transform.position.z));
                }
                Fireplace fire = piece.GetComponent<Fireplace>();
                if (fire != null && piece.m_comfortGroup == Piece.ComfortGroup.Fire)
                    places.Fires.Add(new FirePlace
                    {
                        Fire = fire, Roofed = Roofed(fire.transform.position, piece), Id = "fire" + fire.GetInstanceID(),
                        AtHall = Construction.PiecePlacer.SiteOf(piece.gameObject) == hall.Id, Radius = Reach(piece),
                        InHut = Construction.PiecePlacer.SiteOf(piece.gameObject) is string site && site.Length > 0 && site != hall.Id,
                    });
                AddProp(piece, places);
            }
            // The stations and fires come in any order: which fires are a cook's is known once every piece is seen.
            if (s_overFires.Count > 0)
                foreach (FirePlace f in places.Fires)
                    f.Cooks = FreeTimeRules.IsCooksFire(new GroundPoint(f.Position.x, f.Position.z), s_overFires);
            foreach (Piece table in s_tables) AddTable(table, places);
            foreach (Piece pole in s_poles) AddPole(pole, places);
            AddTames(hall.transform.position, HallRules.PerksAt(hall.Level).Radius, places);
        }

        // ---- The pastimes' pieces (1.6) ----

        /// <summary>From the target this far they shoot (the spec: "stands 10 m off").</summary>
        private const float ArcheryFrom = 9f;
        /// <summary>Close to the dummy (the player's look at 1.6: they stood too far off; their blows don't slide them now).</summary>
        private const float DummyFrom = 1.2f;
        private const float TrophyFrom = 2f;
        private const float PoleRing = 2.5f;
        /// <summary>A chair this far beyond a table's edge belongs to it.</summary>
        private const float ChairAtTable = 0.8f;
        /// <summary>A banner this near a log pole across, and up to this high above its foot, makes it a banner pole (the camp's).</summary>
        private const float BannerAcross = 2f, BannerUp = 7f;

        private static readonly string[] s_tableNames =
            { "piece_table", "piece_table_oak", "piece_table_round", "piece_table_runed", "piece_table_runed_small", "piece_blackmarble_table" };
        private static readonly List<Piece> s_tables = new List<Piece>(), s_poles = new List<Piece>(), s_banners = new List<Piece>();

        private static void AddProp(Piece piece, Places places)
        {
            string name = Utils.GetPrefabName(piece.gameObject);
            if (System.Array.IndexOf(s_tableNames, name) >= 0)
            {
                s_tables.Add(piece);
                return;
            }
            if (name == "wood_pole_log_4")
            {
                s_poles.Add(piece);
                return;
            }
            if (name.StartsWith("piece_banner", System.StringComparison.Ordinal))
            {
                s_banners.Add(piece);
                return;
            }
            switch (name)
            {
                case "piece_ArcheryTarget":
                    Add(places, piece, Prop.Target, FrontSpots(piece, ArcheryFrom, 2, 1.5f));
                    return;
                case "piece_TrainingDummy":
                    Add(places, piece, Prop.Dummy, FrontSpots(piece, DummyFrom, 2, 1.4f));
                    return;
                case "piece_maypole":
                    Add(places, piece, Prop.Pole, Ring(piece, piece.transform.position, PoleRing, 6));
                    return;
                case "piece_cartographytable":
                    Add(places, piece, Prop.MapTable, FrontSpots(piece, Reach(piece) + 0.6f, 1, 0f));
                    return;
                case "ArmorStand":
                    Add(places, piece, Prop.Trophy, FrontSpots(piece, TrophyFrom, 2, 1.2f));
                    return;
                case "itemstand":
                case "itemstandh":
                    if (HoldsTrophy(piece)) Add(places, piece, Prop.Trophy, FrontSpots(piece, TrophyFrom, 2, 1.2f));
                    return;
            }
        }

        private static void Add(Places places, Piece piece, Prop kind, List<Vector3> spots)
        {
            if (spots.Count == 0) return;   // nowhere to stand: walled in
            places.Props.Add(new PropPlace
            {
                Kind = kind, Piece = piece, Id = "prop" + piece.GetInstanceID(), Roofed = Roofed(piece.transform.position, piece),
                Capacity = Mathf.Min(PastimeRules.Capacity(kind), spots.Count), Spots = spots,
            });
        }

        /// <summary>A table with two or more chairs at it (their points the spots; a toast stands round it).</summary>
        private static void AddTable(Piece table, Places places)
        {
            Vector3 at = table.transform.position;
            float edge = Reach(table) + ChairAtTable;
            List<SeatPlace> chairs = places.Seats.Where(s => s.Valid && Utils.DistanceXZ(s.Point, at) <= edge && Mathf.Abs(s.Point.y - at.y) <= 1f).ToList();
            if (chairs.Count < 2) return;
            // Eight round it in its own shape (its ends, sides and corners), those up on a bench or the next table, off a raised
            // floor's edge or at any seat dropped (v16's meadhall.txt: the four corners of a circle landed on the Mead Hall's
            // next table and a bench's end; its review: a circle put a long table's sides 3 m out).
            List<GroundPoint> seats = places.Seats.Where(s => s.Valid && Utils.DistanceXZ(s.Point, at) <= edge + PastimeRules.ToastOut + 1f)
                .Select(s => new GroundPoint(s.Point.x, s.Point.z)).ToList();
            HalfSize(table, out float halfX, out float halfZ);
            var around = new List<Vector3>();
            foreach (GroundPoint p in PastimeRules.AroundTable(halfX, halfZ))
            {
                Vector3 spot = table.transform.TransformPoint(new Vector3(p.X, 0f, p.Z));
                spot = Floor(new Vector3(spot.x, at.y, spot.z), at.y);
                if (Open(spot, table) && PastimeRules.ToastSpotClear(new GroundPoint(spot.x, spot.z), spot.y, at.y, seats)) around.Add(spot);
            }
            string tableId = "prop" + table.GetInstanceID();
            if (!s_aroundLogged.TryGetValue(tableId, out int was) || was != around.Count)
            {
                s_aroundLogged[tableId] = around.Count;
                Log.Info($"{Utils.GetPrefabName(table.gameObject)} at ({at.x:0.0}, {at.z:0.0}): {chairs.Count} seats, {around.Count} of 8 places to stand round it for a toast");
            }
            places.Props.Add(new PropPlace
            {
                Kind = Prop.Table, Piece = table, Id = "prop" + table.GetInstanceID(), Roofed = Roofed(at, table), Capacity = chairs.Count,
                Seats = chairs, Spots = chairs.Select(c => c.Point).ToList(), Around = around,
            });
        }

        /// <summary>A log pole with a banner hung near its top (the camp's banner pole, 1.4.3, or a player's own): dancers circle it.</summary>
        private static void AddPole(Piece pole, Places places)
        {
            Vector3 foot = pole.transform.position;
            if (!s_banners.Any(b => b != null && Utils.DistanceXZ(b.transform.position, foot) <= BannerAcross
                                    && b.transform.position.y >= foot.y && b.transform.position.y - foot.y <= BannerUp)) return;
            if (places.Props.Any(p => p.Kind == Prop.Pole && Utils.DistanceXZ(p.Position, foot) < 1f)) return;
            Add(places, pole, Prop.Pole, Ring(pole, foot, PoleRing, 6));
        }

        /// <summary>The colony's tame animals in its area (a wolf, a boar, a lox…): one settler pets each.</summary>
        private static void AddTames(Vector3 hall, float radius, Places places)
        {
            foreach (Character c in Character.GetAllCharacters())
            {
                if (c == null || c.IsDead() || c.IsPlayer() || !c.IsTamed() || c.GetComponent<Citizen>() != null) continue;
                if (Utils.DistanceXZ(c.transform.position, hall) > radius) continue;
                places.Props.Add(new PropPlace { Kind = Prop.Tame, Creature = c, Id = "tame" + c.GetInstanceID(), Capacity = PastimeRules.Capacity(Prop.Tame) });
            }
        }

        private static bool HoldsTrophy(Piece piece)
        {
            ItemStand stand = piece.GetComponentInChildren<ItemStand>();
            if (stand == null || !stand.HaveAttachment() || ObjectDB.instance == null) return false;
            GameObject item = ObjectDB.instance.GetItemPrefab(stand.GetAttachedItem());
            ItemDrop drop = item != null ? item.GetComponent<ItemDrop>() : null;
            return drop != null && drop.m_itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Trophy;
        }

        private static int s_spotMask, s_openMask;

        /// <summary>
        /// <paramref name="count"/> spots <paramref name="from"/> before the piece, <paramref name="apart"/> apart, on its open
        /// side: its front first, else its back (a stand hung on a wall: the wall's side is shut).
        /// </summary>
        private static List<Vector3> FrontSpots(Piece piece, float from, int count, float apart)
        {
            Vector3 front = piece.transform.forward;
            front.y = 0f;
            front = front.sqrMagnitude > 0.01f ? front.normalized : Vector3.forward;
            foreach (float side in new[] { 1f, -1f })
            {
                Vector3 outwards = front * side, across = Vector3.Cross(Vector3.up, outwards);
                var spots = new List<Vector3>();
                for (int i = 0; i < count; i++)
                {
                    float offset = (i - (count - 1) / 2f) * apart;
                    Vector3 at = Floor(piece.transform.position + outwards * from + across * offset, piece.transform.position.y);
                    if (Open(at, piece)) spots.Add(at);
                }
                if (spots.Count > 0) return spots;
            }
            return new List<Vector3>();
        }

        /// <summary><paramref name="count"/> spots on a circle round <paramref name="centre"/>, those walled off dropped.</summary>
        private static List<Vector3> Ring(Piece piece, Vector3 centre, float radius, int count, float turn = 0f)
        {
            var spots = new List<Vector3>();
            Vector3 start = Quaternion.Euler(0f, piece.transform.eulerAngles.y + turn, 0f) * Vector3.forward;
            for (int i = 0; i < count; i++)
            {
                Vector3 at = Floor(centre + Quaternion.Euler(0f, 360f * i / count, 0f) * start * radius, centre.y);
                if (Open(at, piece)) spots.Add(at);
            }
            return spots;
        }

        /// <summary>The floor or ground under a spot, from a little above the piece's height (under a roof, not on it).</summary>
        private static Vector3 Floor(Vector3 at, float pieceY)
        {
            if (s_spotMask == 0) s_spotMask = LayerMask.GetMask("Default", "static_solid", "terrain", "piece", "vehicle");
            if (Physics.Raycast(new Vector3(at.x, pieceY + 1.5f, at.z), Vector3.down, out RaycastHit hit, 8f, s_spotMask, QueryTriggerInteraction.Ignore))
                at.y = hit.point.y;
            else if (ZoneSystem.instance != null) at.y = ZoneSystem.instance.GetGroundHeight(at);
            return at;
        }

        /// <summary>
        /// One can stand at the spot and see the piece: the floor there within a step of the piece's foot (or of the floor
        /// under a stand hung on a wall), and nothing solid on the line from the spot to the piece but the piece itself
        /// (from behind a stand's wall, the wall is in the way).
        /// </summary>
        private static bool Open(Vector3 spot, Piece piece)
        {
            if (s_openMask == 0) s_openMask = LayerMask.GetMask("piece", "static_solid");
            Vector3 target = piece.transform.position;
            if (Mathf.Abs(spot.y - target.y) > 1.5f && Mathf.Abs(spot.y - (target.y - 1.5f)) > 1.5f) return false;
            Vector3 eye = spot + Vector3.up * 1.2f, at = new Vector3(target.x, Mathf.Max(target.y, spot.y + 0.5f), target.z);
            Vector3 way = at - eye;
            float length = way.magnitude - 0.3f;
            if (length <= 0.01f) return true;
            int count = Physics.RaycastNonAlloc(eye, way.normalized, s_hits, length, s_openMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (s_hits[i].collider != null && s_hits[i].collider.GetComponentInParent<Piece>() != piece) return false;
            return true;
        }

        private static readonly List<Collider> s_colliders = new List<Collider>();
        private static readonly Dictionary<string, int> s_aroundLogged = new Dictionary<string, int>();

        /// <summary>A piece's solid parts' half-lengths along its own two flat axes (a long table's length and width).</summary>
        private static void HalfSize(Piece piece, out float halfX, out float halfZ)
        {
            halfX = halfZ = 0f;
            s_colliders.Clear();
            piece.GetComponentsInChildren(s_colliders);
            Transform t = piece.transform;
            foreach (Collider c in s_colliders)
            {
                if (c == null || c.isTrigger || !c.enabled) continue;
                Bounds b = c.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                    Vector3 local = t.InverseTransformPoint(corner);
                    halfX = Mathf.Max(halfX, Mathf.Abs(local.x));
                    halfZ = Mathf.Max(halfZ, Mathf.Abs(local.z));
                }
            }
            halfX = Mathf.Min(halfX, 4f);
            halfZ = Mathf.Min(halfZ, 4f);
        }

        /// <summary>How far a piece's solid parts reach from its middle on the ground.</summary>
        private static float Reach(Piece piece)
        {
            s_colliders.Clear();
            piece.GetComponentsInChildren(s_colliders);
            float reach = 0f;
            Vector3 at = piece.transform.position;
            foreach (Collider c in s_colliders)
            {
                if (c == null || c.isTrigger || !c.enabled) continue;
                Bounds b = c.bounds;
                reach = Mathf.Max(reach, Mathf.Abs(b.min.x - at.x), Mathf.Abs(b.max.x - at.x), Mathf.Abs(b.min.z - at.z), Mathf.Abs(b.max.z - at.z));
            }
            return Mathf.Min(reach, 4f);
        }

        private static readonly RaycastHit[] s_hits = new RaycastHit[8];

        /// <summary>Another piece over it (its own seat back or chimney aside): under a roof.</summary>
        private static bool Roofed(Vector3 at, Piece own)
        {
            int count = Physics.RaycastNonAlloc(at + Vector3.up * RoofFrom, Vector3.up, s_hits, RoofReach, s_roofMask);
            for (int i = 0; i < count; i++)
                if (s_hits[i].collider != null && s_hits[i].collider.GetComponentInParent<Piece>() != own) return true;
            return false;
        }
    }

    /// <summary>Seats claimed by settlers on their way to them (on this computer), so two never pick the same one.</summary>
    internal static class FreeTimeClaims
    {
        private static readonly ClaimBoard s_board = new ClaimBoard();

        public static bool TryClaim(string seatId, Vector3 at, string settlerId) =>
            s_board.TryClaim(seatId, new GroundPoint(at.x, at.z), settlerId, Time.time);

        public static bool TakenByOther(string seatId, Vector3 at, string settlerId) =>
            s_board.IsTakenByOther(seatId, new GroundPoint(at.x, at.z), settlerId, Time.time);

        public static void Release(string seatId, string settlerId) => s_board.Release(seatId, settlerId);
    }
}
