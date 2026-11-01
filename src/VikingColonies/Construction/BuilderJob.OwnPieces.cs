using System.Collections.Generic;
using UnityEngine;
using VikingColonies.AI;
using VikingColonies.Config;
using VikingColonies.Core;
using static VikingColonies.Construction.CoreVectors;

namespace VikingColonies.Construction
{
    /// <summary>
    /// The player's own pieces through an upgrade (1.6, docs/superpowers/specs/2026-10-25-vikingcolonies-own-pieces-through-
    /// upgrades-design.md): one of the order's put-backs resting on or hanging from a piece he is about to take down comes
    /// down first, whole (its materials stay in it, none from the colony's stores), and goes back where it was, as the
    /// player's, once no old piece it touched stands and the new pieces it touches do.
    /// </summary>
    public partial class BuilderJob
    {
        private Piece _ownFirstFor;   // the piece he is about to take down that _ownFirst was looked for
        private Piece _ownFirst;      // the player's piece on it he takes down first, or null
        private int _ownFirstIndex = -1;
        private int _ownBack = -1;    // the put-back he puts back now, or -1
        private readonly Cooldown _ownBackLook = new Cooldown(2f);
        // Put-backs he took down: still standing in the order until the owner hears, never taken for gone meanwhile.
        private readonly HashSet<int> _ownTakenDown = new HashSet<int>();
        // Put-backs filled or slept in since the order, which the piece under them waits for (said once in the log).
        private readonly HashSet<int> _ownWaitSaid = new HashSet<int>();
        private int _ownBackAsked = -1;   // the put-back whose spot he asked settlers to leave

        private void ForgetOwnPieces()
        {
            _ownFirstFor = null;
            _ownFirst = null;
            _ownFirstIndex = -1;
            _ownBack = -1;
            _ownTakenDown.Clear();
            _ownWaitSaid.Clear();
            _ownBackAsked = -1;
        }

        /// <summary>True while he takes down one of the player's pieces resting on the piece he is about to take down.</summary>
        private bool TakeOwnPieceFirst(float dt)
        {
            if (!ReferenceEquals(_ownFirstFor, _clear) || (!ReferenceEquals(_ownFirst, null) && _ownFirst == null))   // or it went meanwhile
            {
                _ownFirstFor = _clear;
                _ownFirst = OwnPieceOn(_clear, out _ownFirstIndex);
            }
            if (_ownFirst == null) return false;
            Piece own = _ownFirst;
            int index = _ownFirstIndex;
            if (NowHolds(own))
            {
                WaitForOwnPiece(own, index);
                return true;
            }
            Citizen.Status = "$vc_status_taking_down";
            Vector3 at = own.transform.position;
            if (!ReachFromSpot(at, out bool unreachable))
            {
                if (!unreachable) return true;
                // It would fall with what holds it: that waits too, for now (as a piece he can't get near).
                Log.Info($"{Citizen.CitizenName} can't get near {Utils.GetPrefabName(own.gameObject)} at {at}, the player's; " +
                         $"{Utils.GetPrefabName(_clear.gameObject)} at {_clear.transform.position} under it stays for now");
                SetHolderAside();
                return true;
            }
            SwingWhenReady(at, ColonyConfig.SecondsPerPiece.Value / Citizen.SkillSpeed(WorkSkill.Crafting), dt, () =>
            {
                if (TakeOwnPieceDown(own, index)) Citizen.Practise(WorkSkill.Crafting);
            });
            return true;
        }

        /// <summary>The piece he was about to take down waits a while (as one he can't get near); he works on others meanwhile.</summary>
        private void SetHolderAside()
        {
            if (_clear != null) _badThings.Add(SiteThing.KeyOf(_clear.gameObject, null), Time.time);
            _clear = null;
            _clearTakeAway = -1;
            _clearKeptFor = -1;
            _ownFirstFor = null;
            _ownFirst = null;
        }

        /// <summary>A put-back filled or slept in since the order: never taken down with its contents or a spawn point.</summary>
        private static bool NowHolds(Piece own) => PiecePlacer.HoldsThings(own, out OwnPieceWhy _) || PiecePlacer.IsPlayersBed(own);

        /// <summary>
        /// A put-back filled or slept in since the order (couriers fill a store chest; someone sleeps in a bed): the piece
        /// under it waits, and the window names it, until the player empties or moves it (the review's finding 7: it was left
        /// standing, then fell with what held it and spilled, or a spawn bed was lost).
        /// </summary>
        private void WaitForOwnPiece(Piece own, int index)
        {
            if (_ownWaitSaid.Add(index))
                Log.Info($"{Citizen.CitizenName}: {Utils.GetPrefabName(own.gameObject)} at {own.transform.position}, the player's, holds something now " +
                         $"(or is slept in): {(_clear != null ? Utils.GetPrefabName(_clear.gameObject) : "the piece")} under it waits till it's emptied or moved");
            ReportBlocked(own.m_name);
            Citizen.Status = "$vc_status_in_the_way " + own.m_name;
            SetHolderAside();
        }

        /// <summary>
        /// The order's put-back to take down before <paramref name="under"/> (<see cref="OwnPieceRules.FirstToTakeDown"/>),
        /// with its index, or null. One gone meanwhile is left out (unless another computer took it down meanwhile).
        /// </summary>
        private Piece OwnPieceOn(Piece under, out int index)
        {
            index = -1;
            BuildOrderRecord order = _site != null && _site.IsValid ? _site.Order : null;
            if (under == null || order == null || order.Kind != OrderKind.Upgrade || order.PutBacksLeft == 0) return null;
            Vector3 origin = _site.transform.position;
            Quaternion turn = _site.transform.rotation;
            var pieces = new List<Piece>();
            var indices = new List<int>();
            var boxes = new List<PieceBox>();
            for (int i = 0; i < order.PutBacks.Count; i++)
            {
                PutBackRecord putBack = order.PutBacks[i];
                if (putBack.State != PutBackState.Standing || _ownTakenDown.Contains(i)) continue;
                Piece own = PiecePlacer.FindPutBack(putBack);
                if (own == null)
                {
                    Log.Info($"{Citizen.CitizenName}: {PiecePlacer.Whose(putBack.Creator)} {putBack.Prefab} at {U(putBack.Position)} is gone (taken, or fell): nothing to put back");
                    _site.ReportPutBackNotTaken(i);
                    continue;
                }
                pieces.Add(own);
                indices.Add(i);
                boxes.Add(PiecePlacer.BoxIn(own, origin, turn));
            }
            int first = OwnPieceRules.FirstToTakeDown(PiecePlacer.BoxIn(under, origin, turn), boxes);
            if (first < 0) return null;
            index = indices[first];
            return pieces[first];
        }

        /// <param name="index">The order's put-back it is.</param>
        /// <returns>Whether it came down (the builder practises for that).</returns>
        private bool TakeOwnPieceDown(Piece own, int index)
        {
            Piece under = _ownFirstFor;
            _ownFirstFor = null;   // looked for afresh: another may rest on the same piece
            _ownFirst = null;
            BuildOrderRecord order = _site != null && _site.IsValid ? _site.Order : null;
            if (own == null || order == null || index < 0 || index >= order.PutBacks.Count || order.PutBacks[index].State != PutBackState.Standing) return false;
            if (NowHolds(own)) return false;   // filled while he walked over: looked at again, and waited for
            PutBackRecord putBack = order.PutBacks[index];
            string on = under != null ? $": it touches {Utils.GetPrefabName(under.gameObject)} at {under.transform.position}, which comes down" : "";
            Log.Info($"{Citizen.CitizenName} takes down {PiecePlacer.Whose(putBack.Creator)} {putBack.Prefab} at {own.transform.position} " +
                     $"to put it back after{on}");
            string kept = PiecePlacer.KeptValues(own);   // a sign's text, a portal's tag, a torch's fuel … go back with it
            PiecePlacer.TakeOffWhole(own);
            _ownTakenDown.Add(index);
            _ownWaitSaid.Remove(index);
            _site.ReportPutBack(index, PutBackState.Down, kept);
            ReportBlocked("");
            Progress();
            _cursor?.Restart();
            return true;
        }

        /// <summary>
        /// True while he puts one of the player's pieces back. Once every piece is built and every old one gone, a put-back
        /// still standing where it was is done, and one gone is left out, so the order can finish.
        /// </summary>
        private bool PutOwnPiecesBack(BuildOrderRecord order, float dt)
        {
            if (order.Kind != OrderKind.Upgrade || order.PutBacksLeft == 0) return false;
            if (_ownBack >= 0 && (_ownBack >= order.PutBacks.Count || !order.PutBacks[_ownBack].Open)) _ownBack = -1;
            if (_ownBack < 0)
            {
                if (!_ownBackLook.Ready(Time.time)) return false;
                _ownBackLook.Start(Time.time);
                _ownBack = ChooseOwnPieceBack(order);
                if (_ownBack < 0) return false;
                Log.Info($"{Citizen.CitizenName} goes to put back {PiecePlacer.Whose(order.PutBacks[_ownBack].Creator)} " +
                         $"{order.PutBacks[_ownBack].Prefab} at {U(order.PutBacks[_ownBack].Position)}");
            }
            PutBackRecord putBack = order.PutBacks[_ownBack];
            Vector3 at = U(putBack.Position);
            Citizen.Status = BuildingStatus(order);
            if (!ReachFromSpot(at, out bool unreachable))
            {
                if (!unreachable) return true;
                Log.Info($"{Citizen.CitizenName} can't get near where {putBack.Prefab} goes back at {at}; tries again later");
                _site.ReportPutBackFailed(_ownBack);
                _ownBack = -1;
                return false;
            }
            int index = _ownBack;
            SwingWhenReady(at, ColonyConfig.SecondsPerPiece.Value / Citizen.SkillSpeed(WorkSkill.Crafting), dt, () =>
            {
                if (PutOwnPieceBackNow(index)) Citizen.Practise(WorkSkill.Crafting);
            });
            return true;
        }

        /// <summary>
        /// The next put-back to go back (<see cref="OwnPieceRules.NextToPutBack"/>), or -1; tidies the others' states. None
        /// goes back before every old piece is gone (the review's finding 4: an old piece he couldn't reach for now, a kept
        /// piece still to come down for a while, would have dropped it again), nor before the new pieces it touches stand.
        /// </summary>
        private int ChooseOwnPieceBack(BuildOrderRecord order)
        {
            bool allDone = order.BuildHandled && order.TakeAwayLeft == 0;
            Vector3 origin = _site.transform.position;
            Quaternion turn = _site.transform.rotation;
            var down = new List<PieceBox>();
            var indices = new List<int>();
            for (int i = 0; i < order.PutBacks.Count; i++)
            {
                PutBackRecord putBack = order.PutBacks[i];
                if (!putBack.Open)
                {
                    _ownTakenDown.Remove(i);
                    continue;
                }
                bool standing = PiecePlacer.FindPutBack(putBack) != null;
                if (putBack.State == PutBackState.Standing && !_ownTakenDown.Contains(i))
                {
                    // Nothing he took down held it after all: it stands where it was, or it went (taken, or fell).
                    if (allDone)
                    {
                        if (standing) _site.ReportPutBack(i, PutBackState.Back);
                        else _site.ReportPutBackNotTaken(i);
                    }
                    continue;
                }
                if (standing)
                {
                    _site.ReportPutBack(i, PutBackState.Back);   // there already (back before a reload, or put there by hand)
                    continue;
                }
                if (PiecePlacer.Prefab(putBack.Prefab) == null)
                {
                    _site.ReportPutBack(i, PutBackState.Skipped);   // a piece the game doesn't know any more
                    continue;
                }
                down.Add(PiecePlacer.BoxIn(new PlannedPiece(0, putBack.Prefab, putBack.Position, putBack.Rotation), origin, turn));
                indices.Add(i);
            }
            if (down.Count == 0 || order.TakeAwayLeft > 0) return -1;
            var newLevel = new List<PieceBox>();
            var up = new List<bool>();
            NewLevelAround(order, down, origin, turn, newLevel, up);
            int next = OwnPieceRules.NextToPutBack(down, new PieceBox[0], newLevel, up);
            return next >= 0 ? indices[next] : -1;
        }

        /// <summary>
        /// The new level's pieces touching any of these put-backs, and whether each is up: done and standing, or skipped (it
        /// never comes; the support check before one goes back says whether it holds without). A kept piece taken down for a
        /// while stays marked done until it goes back on the list: it counts only once it stands (the review's finding 2).
        /// </summary>
        private void NewLevelAround(BuildOrderRecord order, IReadOnlyList<PieceBox> near, Vector3 origin, Quaternion turn,
            List<PieceBox> newLevel, List<bool> up)
        {
            IReadOnlyList<PlannedPiece> plan = _site.Plan;
            List<Piece> pieces = null;
            for (int i = 0; i < plan.Count; i++)
            {
                if (!plan[i].IsValid) continue;
                PieceBox box = PiecePlacer.BoxIn(plan[i], origin, turn);
                bool touches = false;
                for (int j = 0; j < near.Count && !touches; j++) touches = OwnPieceRules.Touches(near[j], box);
                if (!touches) continue;
                if (pieces == null) pieces = SitePieces();
                newLevel.Add(box);
                up.Add(order.IsSkipped(i) || (!order.IsOpen(i) && PiecePlacer.FindStanding(plan[i], pieces) != null));
            }
        }

        /// <summary>The moment the hammer lands: the player's piece goes back where it was, as theirs. True when it did.</summary>
        private bool PutOwnPieceBackNow(int index)
        {
            _ownBack = -1;
            BuildOrderRecord order = _site != null && _site.IsValid ? _site.Order : null;
            if (order == null || index < 0 || index >= order.PutBacks.Count || !order.PutBacks[index].Open) return false;
            PutBackRecord putBack = order.PutBacks[index];
            GameObject prefab = PiecePlacer.Prefab(putBack.Prefab);
            if (prefab == null) return false;
            Vector3 position = U(putBack.Position);
            Quaternion rotation = U(putBack.Rotation);
            string what = $"{PiecePlacer.Whose(putBack.Creator)} {putBack.Prefab} at {position}";
            if (PiecePlacer.FindPutBack(putBack) != null)
            {
                _site.ReportPutBack(index, PutBackState.Back);
                return false;
            }
            if (PiecePlacer.Occupied(prefab, position, rotation, Citizen.Body))
            {
                // Someone stands there: a settler is asked to make way, then moved out (the review's finding 7), as for
                // the plan's pieces; tried again at the next look.
                Citizen.Status = "$vc_status_waiting_space";
                foreach (CitizenAI settler in PiecePlacer.SettlersAt(prefab, position, rotation, Citizen.Body))
                    settler.MakeWay(_site, position, _ownBackAsked == index, Citizen.CitizenName);
                _ownBackAsked = index;
                return false;
            }
            Piece blocker = PiecePlacer.InTheWay(prefab, position, rotation, _site.Building, _site.MatchesPlan);
            bool held = SupportProbe.WouldHold(prefab, position, rotation, out _);
            if (blocker == null && !held && !HoldersUp(order, putBack))
            {
                Citizen.Status = BuildingStatus(order);   // what holds it isn't up (again) yet: no try counted, looked at later
                return false;
            }
            if (blocker != null || !held)
            {
                Log.Info($"{Citizen.CitizenName} can't put back {what} " +
                         $"({(blocker != null ? Utils.GetPrefabName(blocker.gameObject) + " is there now" : "nothing would hold it")}); tries again later");
                _site.ReportPutBackFailed(index);
                return false;
            }
            PiecePlacer.SetKeptValues(PiecePlacer.Place(prefab, position, rotation, "", putBack.Creator), putBack.Kept);
            _ownTakenDown.Remove(index);
            _site.ReportPutBack(index, PutBackState.Back);
            Log.Info($"{Citizen.CitizenName} puts back {what}");
            Progress();
            return true;
        }

        /// <summary>Every old piece is gone and every new piece the put-back touches is up: a refusal now counts as a try.</summary>
        private bool HoldersUp(BuildOrderRecord order, PutBackRecord putBack)
        {
            if (order.TakeAwayLeft > 0) return false;
            Vector3 origin = _site.transform.position;
            Quaternion turn = _site.transform.rotation;
            PieceBox box = PiecePlacer.BoxIn(new PlannedPiece(0, putBack.Prefab, putBack.Position, putBack.Rotation), origin, turn);
            var newLevel = new List<PieceBox>();
            var up = new List<bool>();
            NewLevelAround(order, new[] { box }, origin, turn, newLevel, up);
            return OwnPieceRules.ReadyToGoBack(box, new PieceBox[0], newLevel, up);
        }
    }
}
