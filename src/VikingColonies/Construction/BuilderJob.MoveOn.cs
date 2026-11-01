using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VikingColonies.Core;

namespace VikingColonies.Construction
{
    /// <summary>
    /// A site that waits on the player (a tree or rock above his level, a tool, materials, something in the way) never
    /// holds the builder for good: after a minute with nothing getting done he sets it aside for five minutes and works on
    /// another site (his own hut first), or leaves it to a better builder. With nothing else to do he stays.
    /// </summary>
    public partial class BuilderJob
    {
        /// <summary>How long he waits on the player before he moves on (the self-test shortens it).</summary>
        internal static float WaitOnPlayerSeconds = (float)OrderPatience.WaitSeconds;

        private readonly TargetBlacklist _setAside = new TargetBlacklist(OrderPatience.AsideSeconds);
        /// <summary>The order's Do first mark when he set it aside: marked again since, it's his to take again.</summary>
        private readonly Dictionary<string, double> _asideFirst = new Dictionary<string, double>();
        private readonly Cooldown _moveOnCheck = new Cooldown(RetrySeconds);
        private readonly Cooldown _firstCheck = new Cooldown(RetrySeconds);
        private float _progressAt;
        private BuildState _seenState;
        private bool _waitedOnPlayer;
        private int _waitLevel;
        private bool _waitHopeless;
        private string _toldStaying = "";
        private readonly HashSet<string> _toldMoved = new HashSet<string>();

        /// <summary>
        /// He stands idle for something only the player can do; <paramref name="needsLevel"/>: the Builder's Hut level it takes
        /// (0: something else); <paramref name="hopeless"/>: nothing the player hands over helps there (no support, out of reach).
        /// </summary>
        private void WaitOnPlayer(int needsLevel = 0, bool hopeless = false)
        {
            _waitedOnPlayer = true;
            _waitLevel = needsLevel;
            _waitHopeless = hopeless;
        }

        /// <summary>Something got done on the site: the wait starts over.</summary>
        private void Progress() => _progressAt = Time.time;

        /// <summary>The order moving on (clearing, flattening, building) counts as something done.</summary>
        private void NoteState(BuildOrderRecord order)
        {
            if (order.State == _seenState) return;
            _seenState = order.State;
            Progress();
        }

        /// <summary>
        /// Before he works each tick: when he waited on the player last tick, for long enough, and someone can get on with
        /// something (OrderPatience), he sets his site aside and lets it go. Looked at every few seconds at most.
        /// </summary>
        private void MoveOnIfStuck()
        {
            bool waited = _waitedOnPlayer;
            _waitedOnPlayer = false;
            if (_site == null || !waited) return;
            float waiting = Time.time - _progressAt;
            if (waiting < WaitOnPlayerSeconds || !_moveOnCheck.Ready(Time.time)) return;
            _moveOnCheck.Start(Time.time);
            // Their own hut comes first: not left before it first stands (1.6, the player's pick A), unless the wait can't be
            // answered there or another site has all it needs (its review).
            ConstructionModule next = ChooseOrder(_site);
            bool another = next != null;
            bool ownFirstBuild = _site == OwnSite() && (_site.Order?.Kind ?? OrderKind.Build) == OrderKind.Build;
            if (ownFirstBuild && !OrderPatience.MayLeave(true, OrderKind.Build, _waitLevel, _waitHopeless, another && HasAll(next)))
            {
                if (_toldStaying != _site.Building.Id)
                {
                    _toldStaying = _site.Building.Id;
                    Log.Info($"{Citizen.CitizenName} stays at their own hut until it first stands ({Localization.instance.Localize(Citizen.Status)})");
                }
                return;
            }
            if (!OrderPatience.MoveOn(waiting, WaitOnPlayerSeconds, another, _waitLevel, ConstructionModule.HighestStaffedBuilderHutLevel(Citizen.ColonyId))) return;
            Log.Info($"{Citizen.CitizenName} leaves the {_site.Building.TypeId} {_site.Building.Id} site for now: it waited {waiting:0} s on the player " +
                     $"({Localization.instance.Localize(Citizen.Status)}); {(another ? "they work on another site" : "a better builder can take it")} " +
                     $"and comes back in {OrderPatience.AsideSeconds / 60:0} minutes");
            _setAside.Add(_site.Building.Id, Time.time);
            _asideFirst[_site.Building.Id] = _site.Order?.First ?? 0;
            // Why they moved, for every player's hover and the player nearby (1.6: the player's 1.4.2 watch saw a builder
            // leave a hut for the camp with no word why).
            Citizen.SetAwayNote(AwayNote.Encode(_site.Building.Id, AwayNote.Until(ConstructionModule.Now()), forHigherLevel: !another));
            TellMovedOn(_site, another);
            LeaveSite();
            _lookAgain.Clear();   // he picks his next site right away
        }

        /// <summary>
        /// The order he would take now, leaving out <paramref name="leaving"/>: the one the player marked first most
        /// recently, then his own hut's, then the colony's oldest he may build, passing over the ones he or the player set
        /// aside (<see cref="OrderChoice.Pick"/>); null for none.
        /// </summary>
        private ConstructionModule ChooseOrder(ConstructionModule leaving)
        {
            ConstructionModule own = OwnSite();
            List<ConstructionModule> open = Candidates(leaving);
            int pick = OrderChoice.Pick(open.Select(s => OptionOf(s, own)).ToList());
            return pick >= 0 ? open[pick] : null;
        }

        /// <summary>
        /// Every few seconds while he works on a site: when the player marked another order first after his own, and he
        /// may take it, he leaves his site for it (as when he moves on from a site that waits on the player).
        /// </summary>
        private void SwitchToFirst()
        {
            if (_site == null || !_firstCheck.Ready(Time.time)) return;
            _firstCheck.Start(Time.time);
            if (_watching.Count > 0 || !_swingPlaced) return;   // a piece just placed is watched first (review: it was never reported)
            ConstructionModule best = Candidates(_site)
                .Where(s => OrderChoice.MaySwitchTo(s.Order.First, _asideFirst.TryGetValue(s.Building.Id, out double f), f))
                .OrderByDescending(s => s.Order.First)
                .FirstOrDefault();
            if (best == null || !OrderChoice.ShouldSwitch(_site.Order?.First ?? 0, best.Order.First)) return;
            Log.Info($"{Citizen.CitizenName} leaves the {_site.Building.TypeId} {_site.Building.Id} site: the {best.Building.TypeId} " +
                     $"{best.Building.Id} is to be done first");
            LeaveSite();
            _lookAgain.Clear();   // he takes it right away
        }

        private ConstructionModule OwnSite() => _hut != null ? _hut.GetComponent<ConstructionModule>() : null;

        /// <summary>
        /// Tells a player nearby that they leave the site for now and why: once per site and need (its review: with several
        /// sites waiting, a message every few minutes); <paramref name="another"/>: for other work, else for a better builder.
        /// </summary>
        private void TellMovedOn(ConstructionModule site, bool another)
        {
            Player player = Player.m_localPlayer;
            if (player == null || site == null || site.Building == null || Vector3.Distance(player.transform.position, Citizen.transform.position) > TellPlayerRange) return;
            string needs = site.Order != null ? site.Order.Needs : "";
            if (!_toldMoved.Add(site.Building.Id + "|" + needs)) return;
            string hut = Localization.instance.Localize(site.Building.Type?.NameToken ?? "");
            string text = !another
                ? string.Format(Localization.instance.Localize("$vc_builder_left_level_msg"), Citizen.CitizenName, hut)
                : needs.Length > 0
                    ? string.Format(Localization.instance.Localize("$vc_builder_left_msg"), Citizen.CitizenName, hut, Localization.instance.Localize(needs),
                        (int)(OrderPatience.AsideSeconds / 60))
                    : string.Format(Localization.instance.Localize("$vc_builder_left_other_msg"), Citizen.CitizenName, hut);
            player.Message(MessageHud.MessageType.TopLeft, text);
        }

        /// <summary>The colony's chests hold all this order still needs (it can go on now): what another hut's chest holds for
        /// its own order isn't this one's (1.5.1's hold; the 1.6 trial merge).</summary>
        private static bool HasAll(ConstructionModule site) =>
            site != null && site.Building != null
            && NeedsTally.Shortfall(site.RemainingNeeds(), BuilderTools.StoresForBuilders(site.Building.ColonyId, site.Building.transform.position, site.Building)).Count == 0;

        /// <summary>The open orders of his colony (and his own hut's) that he may build, leaving out <paramref name="leaving"/>.</summary>
        private List<ConstructionModule> Candidates(ConstructionModule leaving)
        {
            string me = Citizen.Id;
            double now = ConstructionModule.Now();
            ConstructionModule own = OwnSite();
            return ConstructionModule.All
                .Where(s => s != null && s != leaving && s.Building != null && (s == own || s.Building.ColonyId == Citizen.ColonyId)
                    && Open(s, me, now) && MayBuild(s))
                .ToList();
        }

        /// <summary>How an open order looks to him: set aside by him counts only until the player marks it first again.</summary>
        private OrderOption OptionOf(ConstructionModule site, ConstructionModule own)
        {
            BuildOrderRecord order = site.Order;
            string id = site.Building.Id;
            bool aside = OrderChoice.StillAside(_setAside.IsBlocked(id, Time.time), order.First, _asideFirst.TryGetValue(id, out double f) ? f : 0);
            return new OrderOption(site == own, order.Created, aside, order.First, order.Paused, later: site.IsCamp);
        }
    }
}
