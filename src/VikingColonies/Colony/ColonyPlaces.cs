using System.Collections.Generic;
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

        public bool Valid => Fire != null;
        public bool Burning => Fire != null && Fire.IsBurning();
        public Vector3 Position => Fire.transform.position;
    }

    /// <summary>
    /// The seats and fires inside each colony's area (settler life, 0.14), looked up at most every 10 seconds for each
    /// colony and shared by all its settlers on this computer.
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
        }

        private static readonly Dictionary<string, Places> s_byColony = new Dictionary<string, Places>();
        private static readonly List<Piece> s_pieces = new List<Piece>();
        private static readonly List<Chair> s_chairs = new List<Chair>();
        private static int s_roofMask;

        public static IReadOnlyList<SeatPlace> Seats(string colonyId) => For(colonyId).Seats;

        public static IReadOnlyList<FirePlace> Fires(string colonyId) => For(colonyId).Fires;

        /// <summary>Self-test: look again now (a seat or fire was just placed).</summary>
        public static void Forget() => s_byColony.Clear();

        /// <summary>
        /// Someone else is on the seat or at it, so a settler doesn't pick it: a player or another settler within
        /// <see cref="FreeTimeRules.SeatNearPlayer"/> of its point. A seat a settler walks to is claimed by <see cref="FreeTimeClaims"/>.
        /// </summary>
        public static bool Taken(SeatPlace seat, Citizen me)
        {
            if (!seat.Valid) return true;
            Vector3 point = seat.Point;
            foreach (Player player in Player.GetAllPlayers())
                if (player != null && Vector3.Distance(player.transform.position, point) <= FreeTimeRules.SeatNearPlayer) return true;
            foreach (Citizen c in Citizen.All)
            {
                if (c == null || c == me) continue;
                if (Vector3.Distance(c.transform.position, point) <= FreeTimeRules.SeatNearPlayer) return true;
            }
            return false;
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
            Perf.Stop("settler: finding seats and fires", timer);
            return places;
        }

        private static void Refresh(string colonyId, Places places)
        {
            places.At = Time.time;
            places.Seats.Clear();
            places.Fires.Clear();
            ColonyBuilding hall = ColonyBuilding.FindTownHall(colonyId);
            if (hall == null) return;
            if (s_roofMask == 0) s_roofMask = LayerMask.GetMask("piece", "piece_nonsolid", "Default_small", "static_solid");
            s_pieces.Clear();
            Piece.GetAllPiecesInRadius(hall.transform.position, HallRules.PerksAt(hall.Level).Radius, s_pieces);
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
                Fireplace fire = piece.GetComponent<Fireplace>();
                if (fire != null && piece.m_comfortGroup == Piece.ComfortGroup.Fire)
                    places.Fires.Add(new FirePlace
                    {
                        Fire = fire, Roofed = Roofed(fire.transform.position, piece), Id = "fire" + fire.GetInstanceID(),
                        AtHall = Construction.PiecePlacer.SiteOf(piece.gameObject) == hall.Id, Radius = Reach(piece),
                        InHut = Construction.PiecePlacer.SiteOf(piece.gameObject) is string site && site.Length > 0 && site != hall.Id,
                    });
            }
        }

        private static readonly List<Collider> s_colliders = new List<Collider>();

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
