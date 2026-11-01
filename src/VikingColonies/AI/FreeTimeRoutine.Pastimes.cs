using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using VikingColonies.Citizens;
using VikingColonies.Colony;
using VikingColonies.Config;
using VikingColonies.Core;

namespace VikingColonies.AI
{
    /// <summary>
    /// Pastimes in free time (1.6): a settler walks to a free spot of a piece the player built (an archery target, a
    /// training dummy, a table, a pole, a trophy, the map table), to a fire for a story or to a tame animal, or stays in
    /// the open, and plays it with the game's own gestures and swings for a minute or two. Those of two or more (a board
    /// game, a toast, a story, sparring) are a party the chooser gathers from settlers free nearby, as a chat is. Practice
    /// blows and shots are animations only: no attack is started, so nothing is hit and nothing is left behind.
    /// </summary>
    internal sealed partial class FreeTimeRoutine
    {
        private static readonly string[] BoardGestures = { "emote_point", "emote_laugh", "emote_shrug" };
        private static readonly string[] ToastGestures = { "emote_toast", "emote_laugh", "emote_cheer" };
        private static readonly string[] TellerGestures = { "emote_point", "emote_flex", "emote_roar" };
        private static readonly string[] ListenerGestures = { "emote_laugh", "emote_cheer" };
        /// <summary>Someone this near a pastime's spot stands on it.</summary>
        private const float OnSpot = 0.6f;
        /// <summary>A walk to a pastime's spot ends this near it (a stand's two spots are 1.2 m apart).</summary>
        private const float SpotStop = 0.3f;
        /// <summary>A stretch or a nap keeps this far from anyone else.</summary>
        private const float OwnRoom = 1.2f;
        /// <summary>Petting, this near the animal; further than <see cref="PetLost"/> and they walk after it.</summary>
        private const float PetFrom = 1.2f, PetLost = 3f;
        /// <summary>A story's ring round the fire, at least this wide.</summary>
        private const float StoryRing = 2.5f;
        /// <summary>The bow drawn this long before the shot.</summary>
        private const float DrawSeconds = 1.2f;
        /// <summary>After a pastime: standing up, a last gesture, then the next choice.</summary>
        private const float EndingSeconds = 3f;
        /// <summary>The longest a pastime can take, on the way and at it: the choice waits no longer.</summary>
        private const float PastimeCap = ReachSeconds + 130f;

        private static readonly MethodInfo s_setupEquipment = AccessTools.Method(typeof(Humanoid), "SetupEquipment");
        private static readonly MethodInfo s_showHandItems = AccessTools.Method(typeof(Humanoid), "ShowHandItems");
        private static readonly AccessTools.FieldRef<Humanoid, Attack> s_currentAttack = AttackRef();

        private static AccessTools.FieldRef<Humanoid, Attack> AttackRef()
        {
            try { return AccessTools.FieldRefAccess<Humanoid, Attack>("m_currentAttack"); }
            catch (System.Exception) { return null; }
        }

        /// <summary>A pastime of two or more, shared by its members; its leader (the chooser) is a story's teller.</summary>
        private sealed class Party
        {
            public string Id;
            public Pastime Pastime;
            public FreeTimeRoutine Leader;
            public readonly List<FreeTimeRoutine> Members = new List<FreeTimeRoutine>();
            /// <summary>When it ends: set once all its members have got there.</summary>
            public float Until = float.MaxValue;
            /// <summary>The table or fire it holds, so no second party starts there.</summary>
            public string HoldId = "";
            public Vector3 HoldAt;
            /// <summary>A board game's winner, by roll.</summary>
            public FreeTimeRoutine Winner;
            public bool Begun => Until != float.MaxValue;
        }

        private static int s_parties;
        private Pastime _pastime = Pastime.None;
        private Pastime _forcedPastime = Pastime.None;
        private PropPlace _prop;
        private FirePlace _storyFire;
        private SeatPlace _gameSeat;
        /// <summary>From how far they may sit down on <see cref="_gameSeat"/> (across its table: the Mead Hall's seats, 1.6).</summary>
        private float _gameSeatReach = FreeTimeRules.SitFrom;
        private string _spotId = "";
        private int _spotIndex;
        private Party _party;
        private float _pastimeCheckAt;
        private float _nextLoopAt;
        /// <summary>Where they stood when they got there (a lunge's step back returns there).</summary>
        private Vector3 _stoodAt;
        private float _backSince;
        private int _loops;
        private float _drawUntil;
        private float _petAimAt;
        /// <summary>It began (they got there; a party, all of them): it counts as done when it ends.</summary>
        private bool _pastimeBegun;
        private bool _ending;
        private float _endGestureAt;
        /// <summary>A pole dancer on the way to the next spot round the pole.</summary>
        private bool _stepping;
        private bool _bowOut, _handsHiddenForBow;
        private string _drawState = "", _shot = "";
        private readonly TargetBlacklist _skippedProps = new TargetBlacklist(SkipSeatSeconds);

        /// <summary>Self-test: a dance at the evening's fire at the next moment for one standing there.</summary>
        internal static bool EveningDanceForTest;
        /// <summary>Self-test: dances at the evening's fire on this computer.</summary>
        internal static int EveningDancesForTest { get; private set; }

        /// <summary>The pastime they are at (or on their way to), or none.</summary>
        public Pastime CurrentPastime => _doing == Leisure.Pastime ? _pastime : Pastime.None;

        /// <summary>Self-test: the piece their pastime uses.</summary>
        internal PropPlace PropForTest => _doing == Leisure.Pastime ? _prop : null;

        /// <summary>Self-test: they take this pastime next (when it's possible), at once.</summary>
        internal void ForceForTest(Pastime pastime)
        {
            _forcedPastime = pastime;
            _chooseAt = 0f;
        }

        /// <summary>Self-test: they choose again now, by chance.</summary>
        internal void ChooseAgainForTest() => _chooseAt = 0f;

        // ---- The choice ----

        /// <summary>
        /// What the pastimes have at hand now: the pieces in their reach with a free spot (roofed ones only in rain), put in
        /// <paramref name="props"/>; a fire with no story told at it; a partner. Nothing with the setting off.
        /// </summary>
        private PastimeChoices PastimesAtHand(string colony, bool wet, List<FirePlace> fires, bool partner, float now, List<PropPlace> props)
        {
            props.Clear();
            if (!ColonyConfig.Pastimes.Value)
                return new PastimeChoices(false, false, false, false, false, false, false, false, partner, wet, false, false);
            GroundPoint anchor = Ground(_anchor);
            float[] hall = _citizen.Record.ColonyPos;
            GroundPoint? camp = hall != null && hall.Length == 3 ? new GroundPoint(hall[0], hall[2]) : (GroundPoint?)null;
            // Not by the seats vc_seattest holds (v16's meadhall.txt, 27 Oct: a board game sat on them, a toast stood by them).
            List<GroundPoint> held = SeatsHeldForTest?.Where(c => c != null && c.m_attachPoint != null).Select(c => Ground(c.m_attachPoint.position)).ToList();
            foreach (PropPlace p in ColonyPlaces.Props(colony))
                if (p.Valid && (!wet || p.Roofed && (p.Kind != Prop.Target || p.Spots.Any(RoofOver))) && !_skippedProps.IsBlocked(p.Id, now) && PastimeRules.InReach(Ground(p.Position), anchor, camp)
                    && (held == null || PastimeRules.ClearOfHeldSeats(p.Spots.Concat(p.Around).Concat(p.Seats.Where(s => s.Valid).Select(s => s.Point)).Select(Ground), held))
                    && HasRoom(p))
                    props.Add(p);
            bool Has(Prop kind) => props.Any(p => p.Kind == kind);
            bool storyFire = fires.Any(f => !FreeTimeClaims.TakenByOther(StoryHold(f), f.Position, _citizen.Id));
            return new PastimeChoices(Has(Prop.Target), Has(Prop.Dummy), Has(Prop.Table), Has(Prop.Pole), storyFire, Has(Prop.Trophy),
                Has(Prop.MapTable), Has(Prop.Tame), partner, wet, roofedProps: true, evening: false);
        }

        /// <summary>A piece with room for one more: a free spot; a table with no party at it and two chairs or two places round it free.</summary>
        private bool HasRoom(PropPlace p)
        {
            switch (p.Kind)
            {
                // Room for a board game (two chairs) or a toast (two places round it): each start skips a table it can't use
                // (the review of the toast's spots: a round table's chairs can leave no place to stand, and it lost its games too).
                case Prop.Table: return !FreeTimeClaims.TakenByOther(p.Id, p.Position, _citizen.Id) && (FreeSeats(p).Count >= 2 || FreeAround(p).Count >= 2);
                case Prop.Tame: return !FreeTimeClaims.TakenByOther(p.Id, p.Position, _citizen.Id);
                default: return FreeSpot(p, _citizen.transform.position) >= 0;
            }
        }

        private static string StoryHold(FirePlace fire) => fire.Id + "#story";

        /// <summary>The nearest free spot of the piece, or -1.</summary>
        private int FreeSpot(PropPlace p, Vector3 near)
        {
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < p.Spots.Count && i < Mathf.Max(1, p.Capacity); i++)
            {
                if (!SpotFree(SpotId(p, i), p.Spots[i])) continue;
                float d = Vector3.Distance(p.Spots[i], near);
                if (d >= bestDistance) continue;
                best = i;
                bestDistance = d;
            }
            return best;
        }

        private static string SpotId(PropPlace p, int i) => p.Id + "#" + i;

        private List<SeatPlace> FreeSeats(PropPlace table) =>
            table.Seats.Where(s => s.Valid && !FreeTimeClaims.TakenByOther(s.Id, s.Point, _citizen.Id) && !ColonyPlaces.Taken(s, _citizen)).ToList();

        private List<int> FreeAround(PropPlace table)
        {
            var free = new List<int>();
            for (int i = 0; i < table.Around.Count; i++)
                if (SpotFree(table.Id + "#a" + i, table.Around[i])) free.Add(i);
            return free;
        }

        /// <summary>No other settler has claimed the spot, and nobody stands on it.</summary>
        private bool SpotFree(string id, Vector3 at) => !FreeTimeClaims.TakenByOther(id, at, _citizen.Id) && OnSpotOf(at) == null;

        /// <summary>Who stands on the spot (a player, or a settler of any computer, not this one or their party), or null.</summary>
        private string OnSpotOf(Vector3 at, bool yieldingOnly = false)
        {
            foreach (Player player in Player.GetAllPlayers())
                if (player != null && Near(player.transform.position, at, OnSpot)) return player.GetPlayerName();
            foreach (Citizen c in Citizen.All)
            {
                if (c == null || c == _citizen || !Near(c.transform.position, at, OnSpot)) continue;
                if (_party != null && _party.Members.Any(m => m._citizen == c)) continue;
                // Two on one spot (two computers' settlers, sent there at once): one there stays while the other is still
                // walking in (that one gives way); both standing there, the one whose id comes first stays (every computer
                // judges alike).
                if (yieldingOnly && _arrived && (Moving(c) || string.CompareOrdinal(_citizen.Id, c.Id) < 0)) continue;
                return c.CitizenName;
            }
            return null;
        }

        /// <summary>Still walking (not yet standing at a spot).</summary>
        private static bool Moving(Citizen c) => c.Body != null && c.Body.GetVelocity().magnitude > 0.3f;

        private static bool Near(Vector3 a, Vector3 b, float range) => Utils.DistanceXZ(a, b) < range && Mathf.Abs(a.y - b.y) < 1.5f;

        /// <summary>
        /// Starts the pastime chosen (its spot claimed, its party gathered); false, with nothing claimed or changed, when it
        /// can't be after all (the choice falls back to a stroll).
        /// </summary>
        private bool StartPastime(Pastime p, float now, List<PropPlace> props, List<FirePlace> fires, List<FreeTimeRoutine> partners)
        {
            Vector3 me = _citizen.transform.position;
            bool started;
            switch (p)
            {
                case Pastime.BoardGame: started = StartBoardGame(props, partners, me, now); break;
                case Pastime.Toast: started = StartToast(props, partners, me, now); break;
                case Pastime.Story: started = StartStory(fires, partners, me, now); break;
                case Pastime.Spar: started = StartSpar(partners, me, now); break;
                case Pastime.PetTame: started = StartPetting(props, me); break;
                case Pastime.Stretch:
                case Pastime.Nap: started = StartInTheOpen(p, me); break;
                case Pastime.FireDance: started = false; break;   // the evening's, not free time's
                default: started = StartAtSpot(p, props, me); break;
            }
            if (!started)
            {
                Log.Info($"{_citizen.CitizenName}'s free time: {p} after all can't be (no free spot or partner left); a stroll instead");
                return false;
            }
            _chooseAt = now + PastimeCap;
            return true;
        }

        private void BeginPastime(Pastime p)
        {
            _pastime = p;
            _loops = 0;
            _pastimeBegun = false;
            _ending = false;
            _stepping = false;
            _pastimeCheckAt = 0f;
        }

        /// <summary>An archery target, a training dummy, a trophy, the map table or a pole: its nearest free spot.</summary>
        private bool StartAtSpot(Pastime p, List<PropPlace> props, Vector3 me)
        {
            Prop kind = PastimeRules.PropOf(p);
            foreach (PropPlace prop in props.Where(x => x.Kind == kind).OrderBy(x => Vector3.Distance(x.Position, me)))
            {
                int i = FreeSpot(prop, me);
                if (i < 0) continue;
                BeginPastime(p);
                _prop = prop;
                TakeSpot(i, SpotId(prop, i), prop.Spots[i]);
                SaveDoing(ActivityWords.Token(p), "");
                LogPastime(PropWord(kind), "alone");
                return true;
            }
            return false;
        }

        private void TakeSpot(int index, string id, Vector3 at)
        {
            _spotIndex = index;
            _spotId = id;
            _spot = at;
            FreeTimeClaims.TryClaim(id, at, _citizen.Id);
        }

        private Party NewParty(Pastime p, string holdId, Vector3 holdAt)
        {
            var party = new Party { Id = "party" + (++s_parties) + ":" + _citizen.Id, Pastime = p, Leader = this, HoldId = holdId ?? "", HoldAt = holdAt };
            if (party.HoldId.Length > 0 && !FreeTimeClaims.TryClaim(party.HoldId, holdAt, party.Id)) return null;
            BeginPastime(p);
            _party = party;
            party.Members.Add(this);
            return party;
        }

        /// <summary>Pulled into a party by its leader: they leave what they were at and walk to their place in it.</summary>
        private void JoinPastime(Party party, float now)
        {
            StopActivity();
            Begin(Leisure.Pastime, now);
            BeginPastime(party.Pastime);
            _party = party;
            party.Members.Add(this);
            _chooseAt = now + PastimeCap;
        }

        /// <summary>A board game: the two at a table's two chairs furthest apart (facing), the winner chosen now.</summary>
        private bool StartBoardGame(List<PropPlace> props, List<FreeTimeRoutine> partners, Vector3 me, float now)
        {
            FreeTimeRoutine partner = partners.FirstOrDefault();
            if (partner == null) return false;
            foreach (PropPlace table in props.Where(x => x.Kind == Prop.Table).OrderBy(x => Vector3.Distance(x.Position, me)))
            {
                List<SeatPlace> seats = FreeSeats(table);
                if (seats.Count < 2) continue;
                SeatPlace a = null, b = null;
                float apart = -1f;
                for (int i = 0; i < seats.Count; i++)
                    for (int j = i + 1; j < seats.Count; j++)
                    {
                        float d = Vector3.Distance(seats[i].Point, seats[j].Point);
                        if (d <= apart) continue;
                        apart = d;
                        a = seats[i];
                        b = seats[j];
                    }
                if (Vector3.Distance(b.Point, me) < Vector3.Distance(a.Point, me))
                {
                    SeatPlace swap = a;
                    a = b;
                    b = swap;
                }
                Party party = NewParty(Pastime.BoardGame, table.Id, table.Position);
                if (party == null) continue;
                partner.JoinPastime(party, now);
                party.Winner = Rng.Next(2) == 0 ? this : partner;
                _prop = partner._prop = table;
                SitAtGame(a);
                partner.SitAtGame(b);
                SaveDoing(ActivityWords.Token(Pastime.BoardGame), partner._citizen.CitizenName, partner._citizen.Id);
                partner.SaveDoing(ActivityWords.Token(Pastime.BoardGame), _citizen.CitizenName, _citizen.Id);
                LogPastime("table", partner._citizen.CitizenName);
                return true;
            }
            return false;
        }

        private void SitAtGame(SeatPlace seat)
        {
            _gameSeat = seat;
            FreeTimeClaims.TryClaim(seat.Id, seat.Point, _citizen.Id);
            _spot = SeatApproach(seat, out _gameSeatReach);
        }

        /// <summary>A toast: two to four standing round a table.</summary>
        private bool StartToast(List<PropPlace> props, List<FreeTimeRoutine> partners, Vector3 me, float now)
        {
            if (partners.Count == 0) return false;
            foreach (PropPlace table in props.Where(x => x.Kind == Prop.Table).OrderBy(x => Vector3.Distance(x.Position, me)))
            {
                List<int> around = FreeAround(table);
                if (around.Count < 2) continue;
                List<FreeTimeRoutine> others = partners.Take(Mathf.Min(PastimeRules.MaxPlayers(Pastime.Toast) - 1, around.Count - 1)).ToList();
                Party party = NewParty(Pastime.Toast, table.Id, table.Position);
                if (party == null) continue;
                foreach (FreeTimeRoutine other in others) other.JoinPastime(party, now);
                foreach (FreeTimeRoutine member in party.Members)
                {
                    Vector3 from = member._citizen.transform.position;
                    int i = around.OrderBy(x => Vector3.Distance(table.Around[x], from)).First();
                    around.Remove(i);
                    member._prop = table;
                    member.TakeSpot(i, table.Id + "#a" + i, table.Around[i]);
                    FreeTimeRoutine with = member == this ? others[0] : this;
                    member.SaveDoing(ActivityWords.Token(Pastime.Toast), with._citizen.CitizenName, with._citizen.Id);
                }
                LogPastime("table", ChatPairing.Names(others.Select(o => o._citizen.CitizenName).ToList()));
                return true;
            }
            return false;
        }

        /// <summary>A story: the teller at a fire's ring, one to three listening round it.</summary>
        private bool StartStory(List<FirePlace> fires, List<FreeTimeRoutine> partners, Vector3 me, float now)
        {
            if (partners.Count == 0) return false;
            foreach (FirePlace fire in fires.Where(f => f.Valid && f.Burning && !FreeTimeClaims.TakenByOther(StoryHold(f), f.Position, _citizen.Id))
                         .OrderBy(f => Vector3.Distance(f.Position, me)))
            {
                List<KeyValuePair<int, Vector3>> ring = StorySpots(fire, me, Mathf.Min(partners.Count + 1, PastimeRules.MaxPlayers(Pastime.Story)));
                if (ring.Count < 2) continue;
                List<FreeTimeRoutine> listeners = partners.Take(Mathf.Min(3, ring.Count - 1)).ToList();
                Party party = NewParty(Pastime.Story, StoryHold(fire), fire.Position);
                if (party == null) continue;
                foreach (FreeTimeRoutine listener in listeners) listener.JoinPastime(party, now);
                for (int n = 0; n < party.Members.Count; n++)
                {
                    FreeTimeRoutine member = party.Members[n];
                    member._storyFire = fire;
                    member.TakeSpot(n, fire.Id + "#p" + ring[n].Key, ring[n].Value);
                    if (member == this) member.SaveDoing(ActivityWords.Token(Pastime.Story), "");
                    else member.SaveDoing(ActivityWords.Listening, _citizen.CitizenName, _citizen.Id);
                }
                LogPastime("fire", ChatPairing.Names(listeners.Select(o => o._citizen.CitizenName).ToList()) + " listening");
                return true;
            }
            return false;
        }

        /// <summary>Up to <paramref name="count"/> places on a ring round the fire, the first on the chooser's side, a sixth of it apart.</summary>
        private List<KeyValuePair<int, Vector3>> StorySpots(FirePlace fire, Vector3 me, int count)
        {
            var spots = new List<KeyValuePair<int, Vector3>>();
            Vector3 centre = fire.Position, away = me - centre;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
            away.Normalize();
            float radius = Mathf.Max(StoryRing, Gathering.InnerFor(fire.Radius));
            int[] order = { 0, 1, 5, 2, 4, 3 };
            foreach (int k in order)
            {
                if (spots.Count >= count) break;
                Vector3 at = centre + Quaternion.Euler(0f, 60f * k, 0f) * away * radius;
                if (ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(at, out float solid, 1000)) at.y = solid;
                string id = fire.Id + "#p" + k;
                if (Mathf.Abs(at.y - centre.y) > 1.5f || !SpotFree(id, at) || Crowded(at) || NearDoor(at, 2f) || !_ai.CanWalkTo(at)) continue;
                spots.Add(new KeyValuePair<int, Vector3>(k, at));
            }
            return spots;
        }

        /// <summary>Sparring: two facing each other where they'd meet to chat (no wall between, not in a doorway).</summary>
        private bool StartSpar(List<FreeTimeRoutine> partners, Vector3 me, float now)
        {
            foreach (FreeTimeRoutine partner in partners)
            {
                Vector3 theirs = partner._citizen.transform.position;
                if (!ChooseMeeting(me, theirs, out ChatMeeting chat)) continue;
                // A little wider than a chat's 1.6 m.
                float dx = chat.Second.X - chat.First.X, dz = chat.Second.Z - chat.First.Z, length = Mathf.Sqrt(dx * dx + dz * dz);
                if (length < 0.01f) continue;
                float push = (SparGap - length) / 2f / length;
                var meeting = new ChatMeeting(new GroundPoint(chat.First.X - dx * push, chat.First.Z - dz * push),
                    new GroundPoint(chat.Second.X + dx * push, chat.Second.Z + dz * push));
                if (WallBetween(Spot(meeting.First, me), Spot(meeting.Second, theirs))) continue;
                // Not on another pastime's spot, nor beside one at it (the review of pastimes).
                if (!SparClear(Spot(meeting.First, me), partner) || !SparClear(Spot(meeting.Second, theirs), partner)) continue;
                Party party = NewParty(Pastime.Spar, "", Vector3.zero);
                partner.JoinPastime(party, now);
                _spot = Spot(meeting.First, me);
                partner._spot = Spot(meeting.Second, theirs);
                SaveDoing(ActivityWords.Token(Pastime.Spar), partner._citizen.CitizenName, partner._citizen.Id);
                partner.SaveDoing(ActivityWords.Token(Pastime.Spar), _citizen.CitizenName, _citizen.Id);
                LogPastime("open", partner._citizen.CitizenName);
                return true;
            }
            return false;
        }

        private bool SparClear(Vector3 at, FreeTimeRoutine partner)
        {
            foreach (Citizen c in Citizen.All)
                if (c != null && c != _citizen && c != partner._citizen && Utils.DistanceXZ(c.transform.position, at) < OwnRoom) return false;
            foreach (FreeTimeRoutine r in s_active)
                if (r != this && r != partner && r._doing == Leisure.Pastime && Utils.DistanceXZ(r._spot, at) < OwnRoom) return false;
            foreach (PropPlace p in ColonyPlaces.Props(_citizen.ColonyId))
                if (p.Valid && p.Spots.Any(s => Utils.DistanceXZ(s, at) < OwnRoom)) return false;
            return !NearFire(at);
        }

        private bool StartPetting(List<PropPlace> props, Vector3 me)
        {
            foreach (PropPlace tame in props.Where(x => x.Kind == Prop.Tame && x.Creature != null).OrderBy(x => Vector3.Distance(x.Position, me)))
            {
                if (!FreeTimeClaims.TryClaim(tame.Id, tame.Position, _citizen.Id)) continue;
                BeginPastime(Pastime.PetTame);
                _prop = tame;
                _spotId = tame.Id;
                _spot = TameSpot(tame);
                // Its kind ("Wolf"), not a name a player gave it ("petting the Fluffy").
                string name = Localization.instance != null ? Localization.instance.Localize(tame.Creature.m_name) : tame.Creature.m_name;
                SaveDoing(ActivityWords.Token(Pastime.PetTame), name);
                LogPastime(name, "alone");
                return true;
            }
            return false;
        }

        private Vector3 TameSpot(PropPlace tame)
        {
            Vector3 at = tame.Position, away = _citizen.transform.position - at;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
            return at + away.normalized * PetFrom;
        }

        /// <summary>Stretching where they stand (else in the open nearby), a nap in the open: clear of doors, fires and others.</summary>
        private bool StartInTheOpen(Pastime p, Vector3 me)
        {
            if (p == Pastime.Nap && EnvMan.IsWet()) return false;
            Vector3 at = me;
            if (p == Pastime.Nap || !ClearForOwn(me))
            {
                at = StrollSpot(me);
                for (int i = 0; i < 3 && !ClearForOwn(at); i++) at = StrollSpot(me);
                if (!ClearForOwn(at)) return false;
            }
            BeginPastime(p);
            _spot = at;
            SaveDoing(ActivityWords.Token(p), "");
            LogPastime("open", "alone");
            return true;
        }

        /// <summary>A place of their own: not by a door or a fire, nobody within <see cref="OwnRoom"/>, nobody's pastime there.</summary>
        private bool ClearForOwn(Vector3 at)
        {
            if (NearDoor(at) || NearFire(at)) return false;
            foreach (Citizen c in Citizen.All)
                if (c != null && c != _citizen && Utils.DistanceXZ(c.transform.position, at) < OwnRoom) return false;
            foreach (FreeTimeRoutine r in s_active)
                if (r != this && r._doing == Leisure.Pastime && Utils.DistanceXZ(r._spot, at) < OwnRoom) return false;
            return true;
        }

        private void LogPastime(string where, string with) =>
            Log.Info($"{_citizen.CitizenName}'s free time: {_pastime} at the {where} ({with})");

        private static string PropWord(Prop kind)
        {
            switch (kind)
            {
                case Prop.Target: return "target";
                case Prop.Dummy: return "dummy";
                case Prop.Table: return "table";
                case Prop.Pole: return "pole";
                case Prop.Fire: return "fire";
                case Prop.Trophy: return "trophy";
                case Prop.MapTable: return "map table";
                case Prop.Tame: return "tame animal";
                default: return "open";
            }
        }

        // ---- At it ----

        private void TickPastime(float now)
        {
            if (now >= _pastimeCheckAt)
            {
                _pastimeCheckAt = now + 1f;
                string over = PastimeOver(now);
                if (over != null)
                {
                    Log.Info($"{_citizen.CitizenName}'s {_pastime} stops: {over}; they choose again");
                    _chooseAt = now;
                    return;
                }
                RenewPastimeClaims(now);
            }
            if (!_arrived)
            {
                if (!WalkToPastime(now)) return;
                ArrivePastime(now);
                return;
            }
            if (_ending)
            {
                TickEnding(now);
                return;
            }
            if (_party != null && !_party.Begun)
            {
                if (!_stepping) _ai.Idle();
                FacePastime();
                if (!_party.Members.All(m => m._arrived)) return;
                BeginParty(now);
            }
            if (now >= _doUntil)
            {
                StartEnding(now);
                return;
            }
            PlayPastime(now);
        }

        /// <summary>Why the pastime stops now (its piece gone, someone on its spot, too few left, rain on it), or null.</summary>
        private string PastimeOver(float now)
        {
            if (_party != null)
            {
                _party.Members.RemoveAll(m => m._citizen == null);   // one removed from the world meanwhile
                if (!_party.Members.Contains(this)) return "the party broke up";
                if (now < _party.Until && _party.Members.Count < PastimeRules.MinPlayers(_pastime)) return "too few are left";
                if (_pastime == Pastime.Story && !_party.Members.Contains(_party.Leader)) return "the teller left";
            }
            Prop kind = PastimeRules.PropOf(_pastime);
            if (kind == Prop.Fire)
            {
                if (_storyFire == null || !_storyFire.Valid || !_storyFire.Burning) return "the fire is out";
            }
            else if (kind != Prop.None && (_prop == null || !_prop.Valid)) return kind == Prop.Tame ? "the animal is gone" : "the " + PropWord(kind) + " is gone";
            if (_gameSeat != null)
            {
                if (!_gameSeat.Valid) return "the chair is gone";
                if (_arrived ? ColonyPlaces.PlayerSitsOn(_gameSeat) : ColonyPlaces.Taken(_gameSeat, _citizen)) return "someone took the chair";
            }
            else if (_pastime != Pastime.Spar && _pastime != Pastime.PetTame && _pastime != Pastime.Stretch && _pastime != Pastime.Nap)
            {
                string who = OnSpotOf(_spot, yieldingOnly: true);
                if (who != null) return who + " stands on their spot";
            }
            if (EnvMan.IsWet() && !Sheltered()) return "it rains on it";
            return null;
        }

        /// <summary>Under a roof (or stretching, which rain doesn't stop).</summary>
        private bool Sheltered()
        {
            if (_pastime == Pastime.Stretch) return true;
            Prop kind = PastimeRules.PropOf(_pastime);
            if (kind == Prop.Fire) return _storyFire != null && _storyFire.Roofed;
            if (kind == Prop.Target) return _prop != null && _prop.Roofed && RoofOver(_spot);   // the archers stand 9 m off
            return kind != Prop.None && kind != Prop.Tame && _prop != null && _prop.Roofed;
        }

        private static int s_roofOverMask;

        /// <summary>A roof over the spot (a piece above it).</summary>
        private static bool RoofOver(Vector3 at)
        {
            if (s_roofOverMask == 0) s_roofOverMask = LayerMask.GetMask("piece", "piece_nonsolid");
            return Physics.Raycast(at + Vector3.up * 0.5f, Vector3.up, 20f, s_roofOverMask);
        }

        /// <summary>Claims lapse in a minute; a pastime lasts longer: they're renewed once a second.</summary>
        private void RenewPastimeClaims(float now)
        {
            string me = _citizen.Id;
            if (_spotId.Length > 0) FreeTimeClaims.TryClaim(_spotId, _prop != null && _prop.Kind == Prop.Tame ? _prop.Position : _spot, me);
            if (_gameSeat != null && _gameSeat.Valid) FreeTimeClaims.TryClaim(_gameSeat.Id, _gameSeat.Point, me);
            if (_party != null && _party.HoldId.Length > 0) FreeTimeClaims.TryClaim(_party.HoldId, _party.HoldAt, _party.Id);
        }

        /// <summary>On the way: true once there (a board game: once seated).</summary>
        private bool WalkToPastime(float now)
        {
            if (_gameSeat != null)
            {
                Vector3 me = _citizen.transform.position, point = _gameSeat.Point;
                bool close = CanSitFrom(_ai, me, _gameSeat, _gameSeatReach);
                if (!close && !_ai.GoTo(_spot, 0.4f)) return false;
                _ai.Idle();
                if (!close && CantSitFromWalksEnd(_ai, me, _gameSeat, _gameSeatReach) || ColonyPlaces.Taken(_gameSeat, _citizen) || !_pose.SitOn(_gameSeat.Chair))
                {
                    _chooseAt = now;
                    return false;
                }
                return true;
            }
            if (_pastime == Pastime.PetTame && _prop != null && _prop.Valid)
            {
                if (now >= _petAimAt)
                {
                    _petAimAt = now + 1f;
                    _spot = TameSpot(_prop);
                }
                if (Utils.DistanceXZ(_citizen.transform.position, _prop.Position) <= PetFrom + 0.4f) return true;
                return _ai.GoTo(_spot, 0.4f);
            }
            return _ai.GoTo(_spot, SpotStop);
        }

        private void ArrivePastime(float now)
        {
            _ai.Idle();
            _stoodAt = _citizen.transform.position;
            _arrived = true;
            _arrivedAt = now;
            _nextLoopAt = now + (_pastime == Pastime.PoleDance ? ModRandom.Range("pastime", _citizen.CitizenName, 10f, 15f) : ModRandom.Range("pastime", _citizen.CitizenName, 1f, 2f));
            if (_party != null) return;   // it begins once they're all there
            if (_pastimeBegun) return;   // back at a tame animal that wandered off: the same pastime, its own end time
            _pastimeBegun = true;
            _doUntil = now + PastimeRules.DoSeconds(_pastime, Rng.NextDouble());
            _chooseAt = _doUntil + EndingSeconds + 1f;
        }

        private void BeginParty(float now)
        {
            Party party = _party;
            party.Until = now + PastimeRules.DoSeconds(party.Pastime, Rng.NextDouble());
            foreach (FreeTimeRoutine m in party.Members)
            {
                m._pastimeBegun = true;
                m._doUntil = party.Until;
                m._chooseAt = party.Until + EndingSeconds + 1f;
                m._nextLoopAt = now + (m == party.Leader ? 0.5f : ModRandom.Range("pastime", _citizen.CitizenName, 2f, 4f));
            }
            Log.Info($"{_citizen.CitizenName}'s {party.Pastime} begins: {string.Join(", ", party.Members.Select(m => m._citizen.CitizenName))}");
        }

        /// <summary>Which way they look at it: the piece, the partner, the teller, the animal.</summary>
        private Vector3? FaceTarget()
        {
            switch (_pastime)
            {
                case Pastime.BoardGame:
                case Pastime.Stretch:
                case Pastime.Nap: return null;
                case Pastime.Spar:
                    FreeTimeRoutine other = _party?.Members.FirstOrDefault(m => m != this);
                    return other != null && other._citizen != null ? other._citizen.transform.position : (Vector3?)null;
                case Pastime.Story:
                    if (_party == null || _storyFire == null) return null;
                    if (_party.Leader != this) return _party.Leader._citizen != null ? _party.Leader._citizen.transform.position : _storyFire.Position;
                    List<FreeTimeRoutine> listeners = _party.Members.Where(m => m != this && m._citizen != null).ToList();
                    if (listeners.Count == 0) return _storyFire.Position;
                    Vector3 sum = Vector3.zero;
                    foreach (FreeTimeRoutine l in listeners) sum += l._citizen.transform.position;
                    return sum / listeners.Count;
                default: return _prop != null && _prop.Valid ? _prop.Position : (Vector3?)null;
            }
        }

        private void FacePastime()
        {
            Vector3? at = FaceTarget();
            if (at.HasValue) _ai.Face(at.Value);
        }

        private bool FacingPastime(float now)
        {
            Vector3? at = FaceTarget();
            return !at.HasValue || _ai.IsFacing(at.Value) || now - _arrivedAt > 3f;
        }

        private void PlayPastime(float now)
        {
            if (_stepping)
            {
                TickPoleDance(now);
                return;
            }
            _ai.Idle();
            if (_pose.Current == SettlerPose.Kind.None) FacePastime();
            switch (_pastime)
            {
                case Pastime.Archery: TickArchery(now); break;
                case Pastime.WeaponPractice:
                case Pastime.Spar: TickBlows(now); break;
                case Pastime.BoardGame: break;   // seated: no gestures (the game plays none on a seated character)
                case Pastime.Toast: Every(now, ToastGestures, 6f, 10f); break;
                case Pastime.PoleDance: TickPoleDance(now); break;
                case Pastime.Story: TickStory(now); break;
                case Pastime.Admire:
                    if (_nextLoopAt > 0f && now >= _nextLoopAt && FacingPastime(now))
                    {
                        _pose.Gesture("emote_thumbsup");
                        _nextLoopAt = 0f;   // once; then they stand looking
                    }
                    break;
                case Pastime.Map: Every(now, new[] { "emote_point" }, 8f, 12f); break;
                case Pastime.Stretch:
                    // No stretch among the game's emotes (the plan's pre-flight): a flex, then a bend.
                    if (now >= _nextLoopAt)
                    {
                        _pose.Gesture(_loops % 2 == 0 ? "emote_flex" : "emote_bow");
                        _loops++;
                        _nextLoopAt = now + (_loops % 2 == 1 ? ModRandom.Range("pastime", _citizen.CitizenName, 3f, 4f) : ModRandom.Range("pastime", _citizen.CitizenName, 8f, 12f));
                    }
                    break;
                case Pastime.Nap:
                    if (_pose.Current != SettlerPose.Kind.Rest && Still(now)) _pose.Hold(SettlerPose.Kind.Rest);
                    break;
                case Pastime.PetTame: TickPetting(now); break;
            }
        }

        /// <summary>One of <paramref name="gestures"/> now and then, standing and facing it.</summary>
        private void Every(float now, string[] gestures, float min, float max)
        {
            if (now < _nextLoopAt || _pose.Current != SettlerPose.Kind.None || !FacingPastime(now)) return;
            _pose.Gesture(gestures[Rng.Next(gestures.Length)]);
            _nextLoopAt = now + ModRandom.Range("pastime", _citizen.CitizenName, min, max);
        }

        /// <summary>A practice bow in hand (shown only: never in their inventory or gear), drawn and loosed with no arrow.</summary>
        private void TickArchery(float now)
        {
            if (!_bowOut) ShowBow();
            if (_drawUntil > 0f)
            {
                if (now < _drawUntil) return;
                _pose.SetFlag(_drawState, false);
                if (_pose.Has(_shot)) _pose.Gesture(_shot);
                _drawUntil = 0f;
                return;
            }
            if (now < _nextLoopAt || !FacingPastime(now) || !NoAttackUnderway()) return;
            _pose.SetFlag(_drawState, true);
            _drawUntil = now + DrawSeconds;
            _nextLoopAt = now + ModRandom.Range("pastime", _citizen.CitizenName, 4f, 6f);
        }

        private void ShowBow()
        {
            _bowOut = true;   // once a pastime: without the game's Bow it is shot with empty hands
            GameObject bow = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab("Bow") : null;
            ItemDrop drop = bow != null ? bow.GetComponent<ItemDrop>() : null;
            VisEquipment vis = _citizen.GetComponent<VisEquipment>();
            if (drop == null || vis == null || _citizen.Body == null) return;
            Attack attack = drop.m_itemData.m_shared.m_attack;
            _drawState = SettlerPose.BowDrawFlag();
            _shot = attack != null ? attack.m_attackAnimation ?? "" : "";
            SettlerPose.Practising(_citizen.Body, true);   // their job leaves its tool put away meanwhile
            _handsHiddenForBow = _citizen.Body.HideHandItems(false, false);
            vis.SetLeftItem(BowHash, 0, 1);
        }

        private static readonly int BowHash = "Bow".GetStableHashCode();

        /// <summary>The practice bow put away: their own gear shown again, as it is.</summary>
        private void PutBowAway()
        {
            if (!_bowOut) return;
            _bowOut = false;
            _pose.SetFlag(_drawState, false);
            _drawUntil = 0f;
            Humanoid body = _citizen != null ? _citizen.Body : null;
            if (body == null) return;
            SettlerPose.Practising(body, false);
            // ShowHandItems equips what was hidden, and with it shows their gear as it is; with nothing hidden, the look is set again.
            if (_handsHiddenForBow && s_showHandItems != null) s_showHandItems.Invoke(body, new object[] { false, false });
            else s_setupEquipment?.Invoke(body, null);
            _handsHiddenForBow = false;
        }

        /// <summary>
        /// On a new owner (or a reload) a practice bow another computer showed is still in the saved look: their own gear
        /// shown again.
        /// </summary>
        private void PutAwayLeftoverBow()
        {
            ZNetView view = _citizen.GetComponent<ZNetView>();
            if (view == null || !view.IsValid() || view.GetZDO().GetInt(ZDOVars.s_leftItem) != BowHash || _citizen.Body == null) return;
            if (_citizen.Body.GetInventory() != null && _citizen.Body.GetInventory().GetAllItems().Any(i => i.m_dropPrefab != null && i.m_dropPrefab.name == "Bow"))
                return;   // a bow of their own
            s_setupEquipment?.Invoke(_citizen.Body, null);
        }

        /// <summary>No attack of their own is under way (one would land its blow on whatever this swing points at).</summary>
        private bool NoAttackUnderway() =>
            _citizen.Body != null && !_citizen.Body.InAttack() && (s_currentAttack == null || s_currentAttack(_citizen.Body) == null);

        /// <summary>Their weapon's own swing (their tool, or fists), as an animation only: no attack, so no blow lands.</summary>
        private void Swing()
        {
            Humanoid body = _citizen.Body;
            string swing = SwingOf(body.GetCurrentWeapon());
            if (!_pose.Has(swing) && body.m_unarmedWeapon != null) swing = SwingOf(body.m_unarmedWeapon.m_itemData);
            if (_pose.Has(swing)) _pose.Gesture(swing);
        }

        private static string SwingOf(ItemDrop.ItemData weapon)
        {
            Attack attack = weapon != null && weapon.m_shared != null ? weapon.m_shared.m_attack : null;
            if (attack == null || string.IsNullOrEmpty(attack.m_attackAnimation)) return "";
            if (attack.m_attackChainLevels > 1) return attack.m_attackAnimation + "0";
            if (attack.m_attackRandomAnimations >= 2) return attack.m_attackAnimation + ModRandom.Range("pastime", "swing", 0, attack.m_attackRandomAnimations);
            return attack.m_attackAnimation;
        }

        /// <summary>
        /// Practice blows, at the dummy or sparring. The game's swings lunge forward, so after one they step back onto
        /// their spot. A sparring pair takes turns, 3 m apart (the player's look at 1.6: swinging at once from 1.6 m they
        /// ran through each other and were pushed back, shaking).
        /// </summary>
        private void TickBlows(float now)
        {
            if (!NoAttackUnderway()) return;   // mid-swing
            // A step back after the lunge, to where they stood (a walk can end a little short of the spot itself).
            if (Utils.DistanceXZ(_citizen.transform.position, _stoodAt) > BackToSpot)
            {
                if (_backSince <= 0f) _backSince = now;
                // Not back within 3 s (something in the way): where they stand now is their place.
                if (now - _backSince > 3f) _stoodAt = _citizen.transform.position;
                else if (!_ai.GoTo(_stoodAt, 0.3f)) return;
                _ai.Idle();
            }
            _backSince = 0f;
            if (now < _nextLoopAt || !FacingPastime(now)) return;
            Swing();
            if (_pastime != Pastime.Spar)
            {
                _nextLoopAt = now + ModRandom.Range("pastime", _citizen.CitizenName, 2f, 3f);
                return;
            }
            FreeTimeRoutine other = _party?.Members.FirstOrDefault(m => m != this);
            _nextLoopAt = now + 2f * SparTurnSeconds;
            if (other != null) other._nextLoopAt = now + SparTurnSeconds;   // their turn next
        }

        /// <summary>Further than this off their spot (a swing's lunge), they step back.</summary>
        private const float BackToSpot = 0.5f;
        /// <summary>A sparring pair swings in turn, this long apart.</summary>
        private const float SparTurnSeconds = 2.2f;
        /// <summary>A sparring pair stands this far apart (their swings don't slide them: <see cref="PastimeRules.StaysPut"/>).</summary>
        private const float SparGap = 1.8f;

        /// <summary>Standing still a moment after getting there: only then do they lie down, kneel or dance (the player's look:
        /// lying down while still moving, a napper slid along the ground).</summary>
        private bool Still(float now) =>
            now - _arrivedAt > 0.5f && _citizen.Body != null && _citizen.Body.GetVelocity().magnitude < 0.15f;

        /// <summary>Dancing at a spot round the pole, every 10-15 s on to the next free one.</summary>
        private void TickPoleDance(float now)
        {
            if (_stepping)
            {
                if (!_ai.GoTo(_spot, SpotStop)) return;
                _ai.Idle();
                _stepping = false;
                _arrivedAt = now;   // still before the dance again
                _nextLoopAt = now + ModRandom.Range("pastime", _citizen.CitizenName, 10f, 15f);
                return;
            }
            if (_pose.Current != SettlerPose.Kind.Dance && Still(now)) _pose.Hold(SettlerPose.Kind.Dance);
            if (now < _nextLoopAt || _prop == null) return;
            _nextLoopAt = now + 5f;
            for (int step = 1; step < _prop.Spots.Count; step++)
            {
                int next = (_spotIndex + step) % _prop.Spots.Count;
                if (!SpotFree(SpotId(_prop, next), _prop.Spots[next])) continue;
                _pose.Stand();
                FreeTimeClaims.Release(_spotId, _citizen.Id);
                TakeSpot(next, SpotId(_prop, next), _prop.Spots[next]);
                _stepping = true;
                return;
            }
        }

        /// <summary>The teller gestures; the listeners sit (every other one; all stand in rain), those standing laugh and cheer.</summary>
        private void TickStory(float now)
        {
            if (_party == null) return;
            if (_party.Leader == this)
            {
                Every(now, TellerGestures, 5f, 8f);
                return;
            }
            bool sits = _party.Members.IndexOf(this) % 2 == 1 && !EnvMan.IsWet();
            if (sits)
            {
                if (_pose.Current == SettlerPose.Kind.None && FacingPastime(now)) _pose.SitGround();
                return;
            }
            if (_pose.Current == SettlerPose.Kind.Ground) _pose.Stand();
            Every(now, ListenerGestures, 8f, 14f);
        }

        /// <summary>Kneeling by the animal; when it wanders off, after it.</summary>
        private void TickPetting(float now)
        {
            if (_prop == null || !_prop.Valid) return;
            if (Utils.DistanceXZ(_citizen.transform.position, _prop.Position) > PetLost)
            {
                _pose.Stand();
                _arrived = false;
                _startedAt = now;   // a new walk: its own half minute to get there
                _petAimAt = 0f;
                return;
            }
            if (_pose.Current != SettlerPose.Kind.Kneel && FacingPastime(now) && Still(now)) _pose.Hold(SettlerPose.Kind.Kneel);
        }

        // ---- The end ----

        /// <summary>Time's up: they stand, and after a board game or a bout a last gesture (the winner cheers, the other shrugs).</summary>
        private void StartEnding(float now)
        {
            _ending = true;
            _stepping = false;
            _pose.Stand();
            PutBowAway();
            bool gesture = _pastime == Pastime.BoardGame || _pastime == Pastime.Spar;
            _endGestureAt = gesture ? now + 0.8f : 0f;
            _doUntil = now + (gesture ? EndingSeconds : 0f);
            _chooseAt = _doUntil;
        }

        private void TickEnding(float now)
        {
            _ai.Idle();
            if (_endGestureAt > 0f && now >= _endGestureAt)
            {
                _endGestureAt = 0f;
                string last = _pastime == Pastime.Spar ? "emote_laugh" : _party != null && _party.Winner == this ? "emote_cheer" : "emote_shrug";
                _pose.Gesture(last);
            }
            if (now >= _doUntil) _chooseAt = now;
        }

        /// <summary>
        /// The pastime is over (time's up, or work, a meal, the evening, night, a monster: <see cref="End"/>): its claims
        /// released, the bow put away, their party left, and, when it had begun, remembered for the mood.
        /// </summary>
        private void StopPastime()
        {
            if (_pastime == Pastime.None) return;
            PutBowAway();
            if (_pastimeBegun && _citizen != null) _citizen.NotePastime(_pastime, Today());
            if (_spotId.Length > 0) FreeTimeClaims.Release(_spotId, _citizen.Id);
            if (_gameSeat != null)
            {
                FreeTimeClaims.Release(_gameSeat.Id, _citizen.Id);
                _lastSeatId = _gameSeat.Id;
            }
            LeaveParty();
            ClearPastime();
        }

        private void ClearPastime()
        {
            _pastime = Pastime.None;
            _prop = null;
            _storyFire = null;
            _gameSeat = null;
            _spotId = "";
            _party = null;
            _pastimeBegun = false;
            _ending = false;
            _stepping = false;
            _drawUntil = 0f;
        }

        /// <summary>Out of their party; one left too few (before its end), or a story without its teller, ends it for the rest.</summary>
        private void LeaveParty()
        {
            Party party = _party;
            _party = null;
            if (party == null || !party.Members.Remove(this)) return;
            float now = Time.time;
            bool over = party.Members.Count < PastimeRules.MinPlayers(party.Pastime) || party.Pastime == Pastime.Story && party.Leader == this;
            if (!over) return;
            if (party.HoldId.Length > 0) FreeTimeClaims.Release(party.HoldId, party.Id);
            if (now < party.Until)
                foreach (FreeTimeRoutine m in party.Members) m._chooseAt = now;
        }

        /// <summary>Not there in time: that piece (or fire) is passed over a while.</summary>
        private void SkipPastimePlace(float now)
        {
            if (_prop != null) _skippedProps.Add(_prop.Id, now);
            if (_storyFire != null) _skippedFires.Add(_storyFire.Id, now);
        }

        private static int Today() =>
            EnvMan.instance != null && ZNet.instance != null ? EnvMan.instance.GetDay(ZNet.instance.GetTimeSeconds()) : 0;

        // ---- The evening's dance (FreeTimeRoutine.Evening) ----

        private float _danceUntil;

        /// <summary>
        /// A standing settler at the evening's fire dances now and then (1 in 3 of their gesture moments), 15-25 s, with the
        /// setting on and dry; true when they began.
        /// </summary>
        private bool EveningDance(float now)
        {
            if (!ColonyConfig.Pastimes.Value || !_eveningAtFire || _eveningSits || _seat != null || EnvMan.IsWet()) return false;
            if (!EveningDanceForTest && Rng.Next(3) != 0) return false;
            _pose.Hold(SettlerPose.Kind.Dance);
            _danceUntil = now + ModRandom.Range("pastime", _citizen.CitizenName, 15f, 25f);
            SaveDoing(ActivityWords.Token(Pastime.FireDance), "");
            EveningDancesForTest++;
            _citizen.NotePastime(Pastime.FireDance, Today());
            Log.Info($"{_citizen.CitizenName}'s free time: FireDance at the fire (the evening)");
            return true;
        }

        /// <summary>The evening's dance is over (or rain came): back to standing by the fire.</summary>
        private void EndEveningDance(float now)
        {
            _pose.Stand();
            _danceUntil = 0f;
            SaveDoing(ActivityWords.Token(Leisure.Evening), "");
            _nextGestureAt = now + ModRandom.Range("pastime", _citizen.CitizenName, 10f, 20f);
        }
    }
}
