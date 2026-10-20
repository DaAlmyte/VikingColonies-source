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

        public static GameObject Prefab(string name) => ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(name) : null;

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
            s_needs[prefabName] = needs;
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
            GameObject prefab = Prefab(Utils.GetPrefabName(piece.gameObject));
            return BoxAt(PieceShape.Of(prefab != null ? prefab : piece.gameObject).Extents, piece.transform.position, piece.transform.rotation);
        }

        /// <summary>The box a planned piece will take in the world (sides along the world's axes).</summary>
        public static PieceBox PlannedBox(PlannedPiece planned)
        {
            GameObject prefab = Prefab(planned.Prefab);
            return prefab != null ? BoxAt(PieceShape.Of(prefab).Extents, U(planned.Position), U(planned.Rotation))
                : new PieceBox(planned.Position, planned.Position);
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
        public static bool IsLooseItem(Piece piece) =>
            piece != null && SiteRules.IsLooseItem(piece.GetComponent<ItemDrop>() != null, piece.IsPlacedByPlayer());

        /// <summary>The id of the hut this piece was built for; "" for any other piece.</summary>
        public static string SiteOf(GameObject go)
        {
            ZNetView nview = go != null ? go.GetComponent<ZNetView>() : null;
            return nview != null && nview.IsValid() ? nview.GetZDO().GetString(SiteKey, "") : "";
        }
    }
}
