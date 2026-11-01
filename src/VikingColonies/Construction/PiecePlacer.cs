using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VikingColonies.AI;
using VikingColonies.Buildings;
using VikingColonies.Core;
using Object = UnityEngine.Object;
using static VikingColonies.Construction.CoreVectors;

namespace VikingColonies.Construction
{
    /// <summary>Puts building pieces up for a hut, and finds pieces already standing where its plan wants them.</summary>
    internal static class PiecePlacer
    {
        public const string SiteKey = SaveKeys.Site;
        private static readonly List<Piece> s_near = new List<Piece>();
        private static int s_characterMask;
        private static int s_pieceMask;
        private static readonly Collider[] s_blockHits = new Collider[64];
        private static readonly Dictionary<string, Dictionary<string, int>> s_needs = new Dictionary<string, Dictionary<string, int>>();
        private static readonly List<Piece> s_one = new List<Piece>();

        private static BuildablePieces s_buildable;
        private static ObjectDB s_buildableFrom;
        private static float s_buildableReadAt = -1000f;
        private static readonly HashSet<string> s_refusedWarned = new HashSet<string>();
        private static readonly HashSet<string> s_rereadFor = new HashSet<string>();

        /// <summary>
        /// A piece a builder may put up, or null: only one on a player's build tool's list (<see cref="BuildablePieces"/>;
        /// the security sweep of 27 Oct 2026: a design or a saved plan naming a boss or an item had it put up, for nothing).
        /// </summary>
        public static GameObject Prefab(string name)
        {
            if (ZNetScene.instance == null || string.IsNullOrEmpty(name)) return null;
            if (Buildable(name)) return ZNetScene.instance.GetPrefab(name);
            if (ZNetScene.instance.GetPrefab(name) != null && s_refusedWarned.Add(name))
                Log.Warn($"'{name}' isn't a piece on any build tool's list: a builder won't put it up (a design or a saved plan names it)");
            return null;
        }

        /// <summary>
        /// Whether a build tool makes it. The tools' lists are read again for a new item list (a new world), and on a name
        /// they don't have: at once the first time that name is asked about, then at most every 10 seconds (another mod's
        /// pieces may join a tool late; the sweep's review: a builder gives a piece up after three refusals).
        /// </summary>
        private static bool Buildable(string name)
        {
            ObjectDB db = ObjectDB.instance;
            bool stale = s_buildable == null || s_buildableFrom != db;
            if (!stale && s_buildable.Allows(name)) return true;
            // at most 200 names read again at once: a saved plan of a thousand made-up names mustn't read them a thousand times
            if (!stale && Time.time - s_buildableReadAt < 10f && (s_rereadFor.Count >= 200 || !s_rereadFor.Add(name))) return false;
            var lists = new List<IEnumerable<string>>();
            if (db != null)
                foreach (GameObject item in db.m_items)
                {
                    PieceTable table = item != null ? item.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_buildPieces : null;
                    if (table != null && table.m_pieces != null) lists.Add(table.m_pieces.Where(p => p != null).Select(p => p.name).ToList());
                }
            s_buildable = new BuildablePieces(lists);
            s_buildableFrom = db;
            s_buildableReadAt = Time.time;
            return s_buildable.Allows(name);
        }

        /// <summary>The piece standing where the plan wants this one (same kind, within 10 cm and 5°), or null.</summary>
        public static Piece FindStanding(PlannedPiece planned)
        {
            s_near.Clear();
            Piece.GetAllPiecesInRadius(U(planned.Position), 0.5f, s_near);
            return FindStanding(planned, s_near);
        }

        /// <summary>
        /// The same, among pieces already gathered (finding pieces by radius walks every loaded piece in the world,
        /// so a builder gathers his site's pieces once and checks them all against that).
        /// </summary>
        public static Piece FindStanding(PlannedPiece planned, IReadOnlyList<Piece> candidates)
        {
            Vector3 position = U(planned.Position);
            Quaternion rotation = U(planned.Rotation);
            foreach (Piece p in candidates)
            {
                if (p == null || Vector3.Distance(p.transform.position, position) > DesignDiff.SamePlace) continue;
                if (DesignDiff.SamePiece(Vector3.Distance(p.transform.position, position), Quaternion.Angle(p.transform.rotation, rotation))
                    && Utils.GetPrefabName(p.gameObject) == planned.Prefab) return p;
            }
            return null;
        }

        /// <summary>
        /// A piece of another building standing where this piece would go (touching doesn't count, overlapping does), or
        /// null. The hut's own chest, the builder's own pieces for this hut and pieces exactly in the plan never count.
        /// </summary>
        public static Piece InTheWay(GameObject prefab, Vector3 position, Quaternion rotation, ColonyBuilding hut, Func<Piece, bool> matchesPlan)
        {
            if (s_pieceMask == 0) s_pieceMask = LayerMask.GetMask("piece", "piece_nonsolid");
            Bounds shape = PieceShape.Of(prefab).Extents;
            Vector3 half = Vector3.Max(shape.extents - Vector3.one * 0.15f, Vector3.one * 0.05f);
            int count = Physics.OverlapBoxNonAlloc(position + rotation * shape.center, half, s_blockHits, rotation, s_pieceMask);
            string thisSite = hut != null ? hut.Id : "";
            for (int i = 0; i < count; i++)
            {
                Piece piece = s_blockHits[i].GetComponentInParent<Piece>();
                if (piece == null || IsWildVegetation(piece) || IsLooseItem(piece)) continue;   // cleared with the site
                bool ownChest = hut != null && piece.gameObject == hut.gameObject;
                if (SiteRules.IsInTheWay(ownChest, SiteOf(piece.gameObject), thisSite, matchesPlan(piece))) return piece;
            }
            return null;
        }

        /// <summary>The piece would go into one of <paramref name="things"/> (a rock another building rests on: it's left whole).</summary>
        public static bool IntoAny(GameObject prefab, Vector3 position, Quaternion rotation, List<Collider> things)
        {
            if (things == null || things.Count == 0) return false;
            Bounds shape = PieceShape.Of(prefab).Extents;
            Vector3 half = Vector3.Max(shape.extents - Vector3.one * 0.15f, Vector3.one * 0.05f);
            int count = Physics.OverlapBoxNonAlloc(position + rotation * shape.center, half, s_blockHits, rotation, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++) if (things.Contains(s_blockHits[i])) return true;
            return false;
        }

        /// <summary>A player, settler or animal other than <paramref name="ignore"/> stands where the piece would go.</summary>
        public static bool Occupied(GameObject prefab, Vector3 position, Quaternion rotation, Character ignore)
        {
            return CharactersAt(prefab, position, rotation).Any(c => c != ignore);
        }

        /// <summary>A bed a player sleeps in (their spawn point): an upgrade leaves it standing (TakeDownRules.LeaveStanding).</summary>
        public static bool IsPlayersBed(Piece piece)
        {
            if (piece == null || piece.GetComponent<Bed>() == null) return false;
            ZNetView view = piece.GetComponent<ZNetView>();
            long owner = view != null && view.IsValid() ? view.GetZDO().GetLong(ZDOVars.s_owner) : 0L;
            return TakeDownRules.LeaveStanding(true, owner);
        }

        /// <summary>Who stands where the piece would go, other than <paramref name="ignore"/> (for the log).</summary>
        public static string Occupants(GameObject prefab, Vector3 position, Quaternion rotation, Character ignore) =>
            string.Join(", ", CharactersAt(prefab, position, rotation).Where(c => c != ignore).Select(c => c.GetHoverName()).Distinct());

        /// <summary>The settlers (not players or animals) other than <paramref name="ignore"/> standing where the piece would go.</summary>
        public static List<CitizenAI> SettlersAt(GameObject prefab, Vector3 position, Quaternion rotation, Character ignore) =>
            CharactersAt(prefab, position, rotation).Where(c => c != ignore).Select(c => c.GetComponent<CitizenAI>()).Where(ai => ai != null).ToList();

        /// <summary><paramref name="who"/> stands where the piece would go.</summary>
        public static bool Covers(GameObject prefab, Vector3 position, Quaternion rotation, Character who)
        {
            return CharactersAt(prefab, position, rotation).Contains(who);
        }

        /// <summary>
        /// Who stands in the piece's own colliders, a little bigger (the old huts' third rerun, 2026-10-19: the whole piece's
        /// box took in the room under a roof's slope, and a forager standing in his hut under a thatch board 2.2 m up was "in
        /// the way" of it for 30 minutes). A piece whose parts are all switched off until it is placed: its whole box.
        /// </summary>
        private static List<Character> CharactersAt(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (s_characterMask == 0) s_characterMask = LayerMask.GetMask("character", "character_net", "character_ghost", "character_noenv");
            PieceShape shape = PieceShape.Of(prefab);
            var found = new List<Character>();
            bool any = false;
            foreach (PieceShape.WorldBox box in shape.ColliderBoxes())
            {
                any = true;
                AddCharacters(found, Physics.OverlapBox(position + rotation * box.Center, box.Half + Vector3.one * OccupiedMargin,
                    rotation * box.Rotation, s_characterMask));
            }
            if (!any)
            {
                Bounds whole = shape.Extents;
                AddCharacters(found, Physics.OverlapBox(position + rotation * whole.center, whole.extents, rotation, s_characterMask));
            }
            return found;
        }

        /// <summary>This much room (metres) is kept round a piece's colliders for whoever stands by them.</summary>
        private const float OccupiedMargin = 0.05f;

        private static void AddCharacters(List<Character> found, Collider[] hits)
        {
            foreach (Collider hit in hits)
            {
                Character character = hit.GetComponentInParent<Character>();
                if (character != null && !found.Contains(character)) found.Add(character);
            }
        }

        /// <summary>How high the top of the piece would be, and the ground height under it (for the fall log).</summary>
        public static void TopAndGround(GameObject prefab, Vector3 position, Quaternion rotation, out float top, out float ground)
        {
            Bounds shape = PieceShape.Of(prefab).Extents;
            top = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? shape.min.x : shape.max.x, (i & 2) == 0 ? shape.min.y : shape.max.y, (i & 4) == 0 ? shape.min.z : shape.max.z);
                top = Mathf.Max(top, (position + rotation * corner).y);
            }
            ground = ZoneSystem.instance.GetGroundHeight(position);
        }

        /// <summary>Creates the piece as the colony owner's, tagged with its hut, with the game's placing sound and dust.</summary>
        public static GameObject Place(GameObject prefab, Vector3 position, Quaternion rotation, string siteId, long creator)
        {
            GameObject go = Object.Instantiate(prefab, position, rotation);
            Piece piece = go.GetComponent<Piece>();
            if (piece != null && creator != 0L) piece.SetCreator(creator, default(Splatform.PlatformUserID));
            ZNetView nview = go.GetComponent<ZNetView>();
            if (nview != null && nview.IsValid()) nview.GetZDO().Set(SiteKey, siteId ?? "");
            WearNTear wear = go.GetComponent<WearNTear>();
            if (wear != null) wear.OnPlaced();
            if (piece != null) piece.m_placeEffect.Create(position, rotation, go.transform);
            return go;
        }

        /// <summary>The materials a piece costs (item prefab name → amount), read once per kind.</summary>
        public static Dictionary<string, int> Needs(string prefabName)
        {
            if (s_needs.TryGetValue(prefabName, out Dictionary<string, int> needs)) return needs;
            needs = new Dictionary<string, int>();
            GameObject prefab = Prefab(prefabName);
            Piece recipe = prefab != null ? prefab.GetComponent<Piece>() : null;
            if (recipe != null)
            {
                foreach (Piece.Requirement requirement in recipe.m_resources)
                {
                    if (requirement.m_resItem == null || requirement.m_amount <= 0) continue;
                    string item = Utils.GetPrefabName(requirement.m_resItem.gameObject);
                    needs[item] = (needs.TryGetValue(item, out int n) ? n : 0) + requirement.m_amount;
                }
            }
            if (prefab != null) s_needs[prefabName] = needs;   // a piece refused for a moment isn't "free" for good (the sweep's review)
            return needs;
        }

        /// <summary>"12 Wood, 4 Iron" in the game's own item names.</summary>
        public static string DescribeItems(IReadOnlyDictionary<string, int> items) => string.Join(", ", items.Select(kv =>
        {
            GameObject prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(kv.Key) : null;
            ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            return $"{kv.Value} {(drop != null ? drop.m_itemData.m_shared.m_name : kv.Key)}";
        }));

        /// <summary>The box a standing piece takes in the world (sides along the world's axes).</summary>
        public static PieceBox BoxOf(Piece piece)
        {
            // Any piece standing near a site, a design's or not: only measured, so not through the build tools' check.
            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(Utils.GetPrefabName(piece.gameObject)) : null;
            return BoxAt(PieceShape.Of(prefab != null ? prefab : piece.gameObject).Extents, piece.transform.position, piece.transform.rotation);
        }

        /// <summary>The box a planned piece will take in the world (sides along the world's axes).</summary>
        public static PieceBox PlannedBox(PlannedPiece planned)
        {
            GameObject prefab = Prefab(planned.Prefab);
            return prefab != null ? BoxAt(PieceShape.Of(prefab).Extents, U(planned.Position), U(planned.Rotation))
                : new PieceBox(planned.Position, planned.Position);
        }

        /// <summary>
        /// The box a standing piece takes, measured in a hut's own frame (<see cref="OwnPieceRules.BoxIn"/>, 1.6): along the
        /// world's axes a turned hut's walls swell and "touch" what stands near them.
        /// </summary>
        public static PieceBox BoxIn(Piece piece, Vector3 origin, Quaternion turn)
        {
            GameObject prefab = Prefab(Utils.GetPrefabName(piece.gameObject));
            Bounds shape = PieceShape.Of(prefab != null ? prefab : piece.gameObject).Extents;
            return OwnPieceRules.BoxIn(V(shape.min), V(shape.max), V(piece.transform.position), Q(piece.transform.rotation), V(origin), Q(turn));
        }

        /// <summary>The same for a planned piece (a point where the game has no such piece).</summary>
        public static PieceBox BoxIn(PlannedPiece planned, Vector3 origin, Quaternion turn)
        {
            GameObject prefab = Prefab(planned.Prefab);
            Bounds shape = prefab != null ? PieceShape.Of(prefab).Extents : new Bounds(Vector3.zero, Vector3.zero);
            return OwnPieceRules.BoxIn(V(shape.min), V(shape.max), planned.Position, planned.Rotation, V(origin), Q(turn));
        }

        /// <summary>
        /// The piece holds something a put-back can't carry (1.6): a chest with something in it, an item stand or armour
        /// stand with something on it, a smelter, kiln or spinning wheel still at work or holding what it made, a fermenter,
        /// a cooking station with food on it, a beehive's honey, a map table's map, a ward (its list). <paramref name="why"/>:
        /// what the window says (Full, HasItem, or KeepsWhat for a ward or a map table). What a put-back does carry is
        /// <see cref="KeptValues"/> (the review's finding 1).
        /// </summary>
        public static bool HoldsThings(Piece piece, out OwnPieceWhy why)
        {
            why = OwnPieceWhy.Full;
            Container chest = piece.GetComponentInChildren<Container>();
            Inventory held = chest != null ? chest.GetInventory() : null;
            if (held != null && held.NrOfItems() > 0) return true;
            ItemStand item = piece.GetComponentInChildren<ItemStand>();
            ArmorStand armour = piece.GetComponentInChildren<ArmorStand>();
            if (chest == null && (item != null || armour != null)) why = OwnPieceWhy.HasItem;
            if ((item != null && item.HaveAttachment()) || (armour != null && armour.GetNrOfAttachedItems() > 0)) return true;
            ZNetView nview = piece.GetComponent<ZNetView>();
            ZDO zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
            if (zdo == null) return false;
            why = OwnPieceWhy.Full;
            if (piece.GetComponentInChildren<Smelter>() != null && (zdo.GetInt(ZDOVars.s_queued) > 0 || zdo.GetInt(ZDOVars.s_spawnAmount) > 0)) return true;
            if (piece.GetComponentInChildren<Fermenter>() != null && zdo.GetString(ZDOVars.s_content).Length > 0) return true;
            if (Jobs.CookJob.Filled(piece.GetComponentInChildren<CookingStation>()) > 0) return true;
            if ((piece.GetComponentInChildren<Beehive>() != null || piece.GetComponentInChildren<SapCollector>() != null) && zdo.GetInt(ZDOVars.s_level) > 0) return true;
            why = OwnPieceWhy.KeepsWhat;
            byte[] map = piece.GetComponentInChildren<MapTable>() != null ? zdo.GetByteArray(ZDOVars.s_data) : null;
            if (map != null && map.Length > 0) return true;
            if (piece.GetComponentInChildren<PrivateArea>() != null) return true;
            why = OwnPieceWhy.None;
            return false;
        }

        // What a put-back carries through its take-down, by the game's own saved names (1.6, the review's finding 1): a
        // sign's text, a portal's tag, the fuel of a fire, smelter, oven or shield, a turret's ammo. Anything else a piece
        // keeps makes the upgrade wait (HoldsThings).
        private static readonly string[] s_keptTexts = { "text", "author", "authorDisplayName", "tag", "tagauthor", "ammoType" };
        private static readonly int[] s_keptTextKeys =
            { ZDOVars.s_text, ZDOVars.s_author, ZDOVars.s_authorDisplayName, ZDOVars.s_tag, ZDOVars.s_tagauthor, ZDOVars.s_ammoType };

        /// <summary>What this piece carries back (saved with its put-back as it comes down): "" for nothing.</summary>
        public static string KeptValues(Piece piece)
        {
            ZNetView nview = piece != null ? piece.GetComponent<ZNetView>() : null;
            ZDO zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
            if (zdo == null) return "";
            var fields = new List<KeyValuePair<string, string>>();
            for (int i = 0; i < s_keptTexts.Length; i++)
            {
                string value = zdo.GetString(s_keptTextKeys[i], "");
                if (value.Length > 0) fields.Add(new KeyValuePair<string, string>("s." + s_keptTexts[i], value));
            }
            float fuel = zdo.GetFloat(ZDOVars.s_fuel, float.NaN);
            if (!float.IsNaN(fuel)) fields.Add(new KeyValuePair<string, string>("f.fuel", RecordCodec.FormatFloats(new[] { fuel })));
            int ammo = zdo.GetInt(ZDOVars.s_ammo, int.MinValue);
            if (ammo != int.MinValue) fields.Add(new KeyValuePair<string, string>("i.ammo", RecordCodec.FormatInt(ammo)));
            return fields.Count == 0 ? "" : RecordCodec.Encode(fields);
        }

        /// <summary>A put-back's saved values (<see cref="KeptValues"/>) set on the piece that went back; names it doesn't know are left.</summary>
        public static void SetKeptValues(GameObject go, string kept)
        {
            if (go == null || string.IsNullOrEmpty(kept)) return;
            ZNetView nview = go.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid()) return;
            ZDO zdo = nview.GetZDO();
            foreach (KeyValuePair<string, string> field in RecordCodec.Decode(kept))
            {
                int text = field.Key.StartsWith("s.") ? Array.IndexOf(s_keptTexts, field.Key.Substring(2)) : -1;
                if (text >= 0) zdo.Set(s_keptTextKeys[text], field.Value);
                else if (field.Key == "f.fuel" && RecordCodec.TryParseFinite(field.Value, out float fuel)) zdo.Set(ZDOVars.s_fuel, fuel);
                else if (field.Key == "i.ammo") zdo.Set(ZDOVars.s_ammo, RecordCodec.ParseInt(field.Value, 0));
            }
        }

        /// <summary>The player's piece an upgrade's put-back is about, standing where it was (same kind and place), or null.</summary>
        public static Piece FindPutBack(PutBackRecord putBack)
        {
            Piece piece = FindStanding(new PlannedPiece(0, putBack.Prefab, putBack.Position, putBack.Rotation));
            return piece != null && SiteOf(piece.gameObject).Length == 0 ? piece : null;
        }

        /// <summary>
        /// Takes one of the player's pieces off whole, to put back (1.6): nothing drops, its materials stay with it (they
        /// are in the piece that goes back). Only for a piece that holds nothing (<see cref="HoldsThings"/>); what it carries
        /// is read first (<see cref="KeptValues"/>).
        /// </summary>
        public static void TakeOffWhole(Piece piece)
        {
            if (piece == null) return;
            ZNetView nview = piece.GetComponent<ZNetView>();
            if (nview != null && nview.IsValid() && !nview.IsOwner()) nview.ClaimOwnership();
            WearNTear wear = piece.GetComponent<WearNTear>();
            if (wear != null) wear.m_destroyedEffect.Create(piece.transform.position, piece.transform.rotation);
            ZNetScene.instance.Destroy(piece.gameObject);
        }

        /// <summary>
        /// The materials of a piece that was taken off whole and can't go back (1.6): dropped where it stood, the player's
        /// to pick up, as when a piece falls in the game.
        /// </summary>
        public static void DropMaterials(string prefabName, Vector3 at)
        {
            GameObject prefab = Prefab(prefabName);
            Piece piece = prefab != null ? prefab.GetComponent<Piece>() : null;
            if (piece == null || piece.m_resources == null) return;
            foreach (Piece.Requirement need in piece.m_resources)
                if (need.m_resItem != null && need.m_recover && need.m_amount > 0)
                    ItemDrop.DropItem(need.m_resItem.m_itemData.Clone(), need.m_amount, at + Vector3.up * 0.3f, Quaternion.identity);
        }

        /// <summary>A piece a player placed that no colony site claims (1.6, the own-pieces design's point 1).</summary>
        public static bool IsPlayersOwn(Piece piece) => piece != null && piece.IsPlacedByPlayer() && SiteOf(piece.gameObject).Length == 0;

        /// <summary>"DaAlmyte's" for the log: the player who placed it, when they are about; else "a player's".</summary>
        public static string Whose(long creator)
        {
            Player player = creator != 0L ? Player.GetPlayer(creator) : null;
            return player != null ? player.GetPlayerName() + "'s" : "a player's";
        }

        private static PieceBox BoxAt(Bounds shape, Vector3 position, Quaternion rotation)
        {
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? shape.min.x : shape.max.x, (i & 2) == 0 ? shape.min.y : shape.max.y, (i & 4) == 0 ? shape.min.z : shape.max.z);
                Vector3 p = position + rotation * corner;
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            return new PieceBox(V(min), V(max));
        }

        /// <summary>
        /// An old piece of this hut (one its upgrade takes away) standing where this piece would go, with its index in the
        /// take-away list; null when there is none.
        /// </summary>
        public static Piece OldPieceInTheWay(GameObject prefab, Vector3 position, Quaternion rotation, ColonyBuilding hut,
            IReadOnlyList<PlannedPiece> takeAway, out int index)
        {
            index = -1;
            if (hut == null || takeAway.Count == 0) return null;
            if (s_pieceMask == 0) s_pieceMask = LayerMask.GetMask("piece", "piece_nonsolid");
            Bounds shape = PieceShape.Of(prefab).Extents;
            Vector3 half = Vector3.Max(shape.extents - Vector3.one * 0.15f, Vector3.one * 0.05f);
            int count = Physics.OverlapBoxNonAlloc(position + rotation * shape.center, half, s_blockHits, rotation, s_pieceMask);
            for (int i = 0; i < count; i++)
            {
                Piece piece = s_blockHits[i].GetComponentInParent<Piece>();
                if (piece == null || SiteOf(piece.gameObject) != hut.Id) continue;
                index = TakeAwayIndexOf(piece, takeAway);
                if (index >= 0) return piece;
            }
            index = -1;
            return null;
        }

        /// <summary>Which of an upgrade's old pieces this standing piece is (same kind, place and turn), or -1.</summary>
        public static int TakeAwayIndexOf(Piece piece, IReadOnlyList<PlannedPiece> takeAway)
        {
            s_one.Clear();
            s_one.Add(piece);
            for (int i = 0; i < takeAway.Count; i++)
                if (FindStanding(takeAway[i], s_one) != null) return i;
            return -1;
        }

        /// <summary>
        /// A wild tree, bush or plant that a mod made a building piece (PlantEverything does, so they can be planted): no
        /// one placed it and it can't wear (every real building piece can). It's vegetation to clear, not a building in
        /// the way (the test next to other mods, 2026-10-15: small beeches turned hut sites red and blocked doors).
        /// </summary>
        public static bool IsWildVegetation(Piece piece)
        {
            if (piece == null || piece.IsPlacedByPlayer() || piece.GetComponent<WearNTear>() != null) return false;
            return piece.GetComponent<TreeBase>() != null || piece.GetComponent<Destructible>() != null
                || piece.GetComponent<Pickable>() != null || piece.GetComponent<Plant>() != null;
        }

        /// <summary>An item lying on the ground that nobody placed, though the game gives it a Piece (SiteRules.IsLooseItem).</summary>
        public static bool IsLooseItem(Piece piece)
        {
            ItemDrop drop = piece != null ? piece.GetComponent<ItemDrop>() : null;
            if (drop == null) return false;
            ZNetView nview = piece.GetComponent<ZNetView>();
            bool placedAsPiece = nview != null && nview.IsValid() && drop.IsPiece();   // IsPiece reads its ZDO: gone once picked up
            return SiteRules.IsLooseItem(true, placedAsPiece, piece.IsPlacedByPlayer());
        }

        /// <summary>The id of the hut this piece was built for; "" for any other piece.</summary>
        public static string SiteOf(GameObject go)
        {
            ZNetView nview = go != null ? go.GetComponent<ZNetView>() : null;
            return nview != null && nview.IsValid() ? nview.GetZDO().GetString(SiteKey, "") : "";
        }
    }
}
