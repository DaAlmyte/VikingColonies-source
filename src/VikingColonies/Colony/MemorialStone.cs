using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Core;
using VikingColonies.Jobs;

namespace VikingColonies.Colony
{
    /// <summary>
    /// The memorial stone (1.5.1): one per colony, raised by the player inside its area, where the colony remembers its
    /// fallen. Its words are the camp's fallen list without the records, copied onto the stone (each side takes in the
    /// other's lines, so the words outlive a lost camp); E reads them like one of the game's runestones, a page at a time.
    /// </summary>
    public class MemorialStone : MonoBehaviour, Hoverable, Interactable
    {
        private const float TickSeconds = 5f;

        private static readonly List<MemorialStone> s_all = new List<MemorialStone>();
        public static IReadOnlyList<MemorialStone> All => s_all;

        private ZNetView _nview;
        private float _timer = 1f;
        private int _page;
        private bool _strangerLogged;
        private FallenList _words;
        private uint _wordsRevision;

        public bool IsValid => _nview != null && _nview.IsValid();
        private ZDO Zdo => IsValid ? _nview.GetZDO() : null;

        public string ColonyId => Zdo?.GetString(SaveKeys.ColonyId, "") ?? "";

        /// <summary>Its copy of the fallen's words (no records), read again only when its saved data has changed.</summary>
        public FallenList Words
        {
            get
            {
                ZDO zdo = Zdo;
                if (zdo == null) return new FallenList();
                if (_words == null || zdo.DataRevision != _wordsRevision)
                {
                    _words = FallenList.Decode(zdo.GetString(SaveKeys.MemorialWords, ""));
                    _wordsRevision = zdo.DataRevision;
                }
                return _words;
            }
        }

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            if (!IsValid) return;   // the hammer's placement ghost
            s_all.Add(this);
        }

        private void OnDestroy() => s_all.Remove(this);

        private void Update()
        {
            if (!IsValid || !_nview.IsOwner()) return;
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = TickSeconds;
            JoinColony();
            TakeWords();
        }

        /// <summary>The colony it stands in, once: only one its raiser may manage (one placed by no player, a command, joins).</summary>
        private void JoinColony()
        {
            string colony = ColonyId;
            if (colony.Length > 0) return;
            ColonyBuilding hall = ColonyAreas.BuildingAt(transform.position);
            if (hall == null || hall.ColonyId.Length == 0) return;
            long raiser = Zdo.GetLong(ZDOVars.s_creator, 0L);
            if (raiser != 0L && !ColonyPermissions.MayBuildAt(hall, transform.position, raiser))
            {
                if (!_strangerLogged) Log.Info($"Memorial stone at {transform.position}: its raiser may not manage colony {hall.ColonyId}; it stays out of it");
                _strangerLogged = true;
                return;
            }
            Zdo.Set(SaveKeys.ColonyId, hall.ColonyId);
            Log.Info($"Memorial stone at {transform.position} joined colony {hall.ColonyId}");
        }

        /// <summary>The camp's words taken in (when the camp is loaded): its new deaths and returns.</summary>
        private void TakeWords()
        {
            string colony = ColonyId;
            if (colony.Length == 0) return;
            ColonyBuilding hall = ColonyBuilding.FindTownHall(colony);
            TownHallModule camp = hall != null && hall.IsValid ? hall.GetComponent<TownHallModule>() : null;
            if (camp == null) return;
            FallenList words = Words;
            if (!words.MergeFrom(camp.Fallen.WordsOnly()) && FallenList.IsSaved(Zdo.GetString(SaveKeys.MemorialWords, ""))) return;
            Zdo.Set(SaveKeys.MemorialWords, words.Encode());
            _words = null;
        }

        public string GetHoverText()
        {
            if (!IsValid) return "";
            string count = string.Format(Localization.instance.Localize("$vc_memorial_count"), Words.Lines.Count);
            return Localization.instance.Localize("$vc_memorial") + ": " + count + Localization.instance.Localize("\n[<color=yellow><b>$KEY_Use</b></color>] $vc_memorial_read");
        }

        public string GetHoverName() => Localization.instance.Localize("$vc_memorial");

        public float GetHoverOffset() => 0f;

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || !IsValid || TextViewer.instance == null) return false;
            MemorialPage page = MemorialText.Page(Words, _page);
            _page = page.Page + 1;   // the next E, the next page (round again after the last)
            TextViewer.instance.ShowText(TextViewer.Style.Rune, Localization.instance.Localize("$vc_memorial"), Text(page), true);
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        /// <summary>"Here the colony remembers:", the page it is, then a line per settler.</summary>
        internal static string Text(MemorialPage page)
        {
            Localization loc = Localization.instance;
            var sb = new StringBuilder(loc.Localize("$vc_memorial_first"));
            if (page.Pages > 1) sb.Append(' ').Append(string.Format(loc.Localize("$vc_memorial_page"), page.Page + 1, page.Pages));
            foreach (FallenLine line in page.Lines) sb.Append('\n').Append(Line(line));
            return sb.ToString();
        }

        /// <summary>"Ulf, Forager, day 39, killed by a Greydwarf"; greyed with "(returned on day 42)" once they came back.</summary>
        internal static string Line(FallenLine line)
        {
            Localization loc = Localization.instance;
            string cause = SafeText.Format(loc.Localize(line.Cause), loc.Localize(line.CauseArg));
            string trade = JobDefinitions.TitleOf(line.Job);
            string text = trade.Length > 0
                ? string.Format(loc.Localize("$vc_memorial_line"), line.Name, trade, line.Day, cause)
                : string.Format(loc.Localize("$vc_memorial_line_short"), line.Name, line.Day, cause);
            if (!line.Returned) return text;
            return "<color=#9a9a9a>" + text + " " + string.Format(loc.Localize("$vc_memorial_returned"), line.BackDay) + "</color>";
        }
    }
}
