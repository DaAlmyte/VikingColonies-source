using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using VikingColonies.Core;

namespace VikingColonies.Citizens
{
    /// <summary>
    /// A settler sitting on a seat, sitting on the ground or lying in a bed, and his gestures (0.14), with the player's own
    /// animations. The computer running him holds his body on the seat as the game holds a seated player
    /// (<c>Player.UpdateAttach</c>: on the attach point every physics step, no gravity, colliders of the seat ignored) and
    /// saves the pose on the settler for everyone; every other computer sets its own animator from that.
    /// </summary>
    public sealed class SettlerPose : MonoBehaviour
    {
        /// <summary>Dance, Rest and Kneel (1.6): the game's looping emotes, held as sitting on the ground is.</summary>
        public enum Kind { None = 0, Seat = 1, Ground = 2, Bed = 3, Dance = 4, Rest = 5, Kneel = 6 }

        public const string SeatAnimation = "attach_chair";
        public const string BedAnimation = "attach_bed";
        public const string SitAnimation = "emote_sit";
        public const string DanceAnimation = "emote_dance";
        public const string RestAnimation = "emote_rest";
        public const string KneelAnimation = "emote_kneel";
        /// <summary>The poses held as a looping emote, with nothing under him: their animator bools.</summary>
        private static readonly string[] s_heldEmotes = { SitAnimation, DanceAnimation, RestAnimation, KneelAnimation };

        // Read guarded: a game update that renames one leaves settlers without poses (GameCheck says so), not without settlers.
        private static readonly AccessTools.FieldRef<Character, Animator> s_animator = Ref<Animator>("m_animator");
        private static readonly AccessTools.FieldRef<Character, ZSyncAnimation> s_zanim = Ref<ZSyncAnimation>("m_zanim");
        private static readonly AccessTools.FieldRef<Character, float> s_maxAirAltitude = Ref<float>("m_maxAirAltitude");
        private static readonly MethodInfo s_showHandItems = AccessTools.Method(typeof(Humanoid), "ShowHandItems");

        private static AccessTools.FieldRef<Character, T> Ref<T>(string field)
        {
            try { return AccessTools.FieldRefAccess<Character, T>(field); }
            catch (System.Exception) { return null; }
        }

        private Animator AnimatorOf() => s_animator != null ? s_animator(_body) : null;
        private ZSyncAnimation ZanimOf() => s_zanim != null ? s_zanim(_body) : null;

        private void NoFall(float y)
        {
            if (s_maxAirAltitude != null) s_maxAirAltitude(_body) = y;
        }
        /// <summary>Settlers held on a seat or in a bed on this computer: the game treats them as attached, as a seated player.</summary>
        internal static readonly HashSet<Character> s_held = new HashSet<Character>();

        private ZNetView _nview;
        private Humanoid _body;
        private Rigidbody _rb;
        private Collider _collider;
        private Transform _point;
        /// <summary>Held on the seat or in the bed, apart from whether the seat is still there (a destroyed seat reads as null).</summary>
        private readonly SeatHold _hold = new SeatHold();
        /// <summary>Where they get up to, kept each step: the seat may be gone by then.</summary>
        private Vector3 _standSpot;
        private Collider[] _ignored;
        private Vector3 _detachOffset;
        private string _holdAnimation = "";
        private bool _handsHidden;
        private Kind _shown;   // what this computer's animator shows

        /// <summary>Gestures played on this computer since the game started (the self-test counts them).</summary>
        public int GesturesPlayed { get; private set; }

        private ZDO Zdo => _nview != null && _nview.IsValid() ? _nview.GetZDO() : null;
        private bool IsOwner => Zdo != null && _nview.IsOwner();

        /// <summary>The saved pose (every computer reads the same).</summary>
        public Kind Current => Zdo != null ? (Kind)Zdo.GetInt(SaveKeys.Pose, 0) : Kind.None;

        /// <summary>The seat or bed he is on (its network id), or none.</summary>
        public ZDOID Target => Zdo != null ? Zdo.GetZDOID(SaveKeys.PoseTarget) : ZDOID.None;

        /// <summary>On the computer running him: held on a seat or in a bed right now.</summary>
        public bool Held => _hold.Holding;

        /// <summary>The point he is held on, or null.</summary>
        public Transform Point => _point;

        /// <summary>Held on a seat or in a bed on this computer (his job leaves his tool put away meanwhile).</summary>
        public static bool IsHeld(Character body) => body != null && s_held.Count > 0 && s_held.Contains(body);

        /// <summary>Settlers showing a practice bow at a pastime on this computer (1.6): their job leaves its tool put away.</summary>
        private static readonly HashSet<Character> s_practising = new HashSet<Character>();

        /// <summary>Owner (1.6): they hold a practice bow, or no longer.</summary>
        internal static void Practising(Character body, bool on)
        {
            if (body == null) return;
            if (on) s_practising.Add(body);
            else s_practising.Remove(body);
        }

        /// <summary>
        /// Their hands are taken (seated or in bed, or a practice bow at a pastime, 1.6): a job doesn't put its tool back in
        /// them meanwhile (the review of pastimes: the lumberjack's axe check took the bow away the next moment).
        /// </summary>
        public static bool HandsBusy(Character body) => IsHeld(body) || body != null && s_practising.Count > 0 && s_practising.Contains(body);

        private static string s_bowDraw;

        /// <summary>The game's bow's draw (an animator bool), or "" (read once from the game's Bow).</summary>
        internal static string BowDrawFlag()
        {
            if (s_bowDraw != null) return s_bowDraw;
            GameObject bow = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab("Bow") : null;
            ItemDrop drop = bow != null ? bow.GetComponent<ItemDrop>() : null;
            if (drop == null) return "";   // not loaded yet: asked again later
            Attack attack = drop.m_itemData.m_shared.m_attack;
            s_bowDraw = attack != null ? attack.m_drawAnimationState ?? "" : "";
            return s_bowDraw;
        }

        /// <summary>
        /// This computer has just started running him (taken over, or loaded): whatever pose was saved belongs to a body
        /// another computer held, or one before a reload. He starts standing, with the seat and sitting animations off for
        /// everyone (review of 0.14: a settler kept sitting while he walked after a hand-over).
        /// </summary>
        public void ResetForNewOwner()
        {
            if (Held) Release();
            if (!IsOwner) return;
            // The practice bow's draw (1.6) too: a hand-over mid-draw left it on.
            string draw = BowDrawFlag();
            foreach (string animation in new[] { SeatAnimation, BedAnimation, SitAnimation, DanceAnimation, RestAnimation, KneelAnimation, draw })
            {
                if (animation.Length == 0 || animation == draw && !Has(draw)) continue;
                SetBool(animation, true);   // the game skips a value its animator already shows: on, then off, writes it
                SetBool(animation, false);
            }
            if (Current != Kind.None || Target != ZDOID.None) Save(Kind.None, ZDOID.None);
        }

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            _body = GetComponent<Humanoid>();
            _rb = GetComponent<Rigidbody>();
            _collider = GetComponent<CapsuleCollider>();
        }

        private void OnDestroy()
        {
            s_held.Remove(_body);
            s_practising.Remove(_body);
        }

        /// <summary>Owner: sits on the seat (its attach point). False when it can't be used.</summary>
        public bool SitOn(Chair chair)
        {
            if (!IsOwner || chair == null || chair.m_attachPoint == null) return false;
            ZNetView seat = chair.GetComponentInParent<ZNetView>();
            Attach(chair.m_attachPoint, chair.gameObject, SeatAnimation, chair.m_detachOffset, Kind.Seat, seat);
            return true;
        }

        /// <summary>Owner: lies in the bed (where a player lies in it). False when it can't be used.</summary>
        public bool LieIn(Bed bed)
        {
            if (!IsOwner || bed == null || bed.m_spawnPoint == null) return false;
            Attach(bed.m_spawnPoint, bed.gameObject, BedAnimation, new Vector3(0f, 0.5f, 0f), Kind.Bed, bed.GetComponentInParent<ZNetView>());
            return true;
        }

        /// <summary>Owner: sits down on the ground where he stands.</summary>
        public void SitGround()
        {
            if (!IsOwner) return;
            if (Held) Release();
            if (Current != Kind.Ground) ClearEmote(Current);
            SetBool(SitAnimation, true);
            Save(Kind.Ground, ZDOID.None);
        }

        /// <summary>Owner (1.6): dances, lies down for a nap or kneels where he stands, held until <see cref="Stand"/>.</summary>
        public void Hold(Kind kind)
        {
            string animation = EmoteOf(kind);
            if (!IsOwner || animation == null || kind == Kind.Ground) return;
            if (Current == kind) return;
            if (Held) Release();
            ClearEmote(Current);
            ClearStop();
            SetBool(animation, true);
            Save(kind, ZDOID.None);
        }

        /// <summary>The animator bool a pose held without a seat plays, or null.</summary>
        private static string EmoteOf(Kind kind)
        {
            switch (kind)
            {
                case Kind.Ground: return SitAnimation;
                case Kind.Dance: return DanceAnimation;
                case Kind.Rest: return RestAnimation;
                case Kind.Kneel: return KneelAnimation;
                default: return null;
            }
        }

        private void ClearEmote(Kind kind)
        {
            string animation = EmoteOf(kind);
            if (animation == null) return;
            SetBool(animation, false);
            // The game ends a looping emote with "emote_stop" too (Player.UpdateEmote); without it a dance played on
            // after it was over (the player's look at 1.6). Sitting ends on its bool alone, as since 0.14.
            if (kind != Kind.Ground && Has(EmoteStop)) Gesture(EmoteStop);
        }

        private const string EmoteStop = "emote_stop";

        /// <summary>A stop left waiting (no emote to end) would end the next emote at once: the game clears it first too.</summary>
        private void ClearStop()
        {
            Animator animator = AnimatorOf();
            if (animator != null && Has(EmoteStop)) animator.ResetTrigger(EmoteStop);
        }

        /// <summary>Owner: gets up and stands at <paramref name="at"/> (out of bed onto his spot beside it, 0.15).</summary>
        public void StandAt(Vector3 at)
        {
            _standAt = at;
            Stand();
            _standAt = null;
        }

        private Vector3? _standAt;

        /// <summary>Owner: stands up (off a seat, out of a bed, up from the ground). Safe to call any time.</summary>
        public void Stand()
        {
            if (Held) Release();
            if (!IsOwner) return;
            ClearEmote(Current);
            if (Current == Kind.Seat || Current == Kind.Bed)   // saved as seated, but not held here (review of 0.14)
            {
                SetBool(SeatAnimation, false);
                SetBool(BedAnimation, false);
            }
            if (Current != Kind.None) Save(Kind.None, ZDOID.None);
        }

        /// <summary>Owner: a gesture everyone sees ("emote_wave", "emote_laugh", "eat"…).</summary>
        public void Gesture(string trigger)
        {
            if (!IsOwner || string.IsNullOrEmpty(trigger)) return;
            ZSyncAnimation zanim = ZanimOf();
            if (zanim == null) return;
            zanim.SetTrigger(trigger);
            GesturesPlayed++;
        }

        private void Attach(Transform point, GameObject seat, string animation, Vector3 detachOffset, Kind kind, ZNetView seatView)
        {
            if (Held) Release();
            ClearEmote(Current);
            _point = point;
            _hold.Start();
            _detachOffset = detachOffset;
            _holdAnimation = animation;
            _ignored = seat.GetComponentsInChildren<Collider>();
            if (_collider != null)
                foreach (Collider c in _ignored)
                    if (c != null) Physics.IgnoreCollision(_collider, c, true);
            _handsHidden = _body.HideHandItems(false, false);
            SetBool(animation, true);
            s_held.Add(_body);
            Hold();
            Save(kind, seatView != null && seatView.IsValid() ? seatView.GetZDO().m_uid : ZDOID.None);
        }

        private void Release()
        {
            Transform point = _point;
            _point = null;
            _hold.Stop();
            s_held.Remove(_body);
            if (_collider != null && _ignored != null)
                foreach (Collider c in _ignored)
                    if (c != null) Physics.IgnoreCollision(_collider, c, false);
            _ignored = null;
            SetBool(_holdAnimation, false);
            _holdAnimation = "";
            if (_rb != null) _rb.useGravity = true;
            // Off the seat to where the seat says (beside or in front of it), as a player gets up; out of bed onto the floor
            // beside it when he is told where; where the seat said at the last step when it's gone.
            Vector3 to = _standAt ?? (point != null ? point.position + point.TransformVector(_detachOffset) : _standSpot);
            transform.position = to;
            if (_rb != null) _rb.position = to;
            NoFall(to.y);
            if (_handsHidden && s_showHandItems != null) s_showHandItems.Invoke(_body, new object[] { false, false });
            _handsHidden = false;
        }

        /// <summary>On the seat's point, still (the game's own way of holding a seated player).</summary>
        private void Hold()
        {
            if (_point == null) return;
            _standSpot = _point.position + _point.TransformVector(_detachOffset);
            transform.position = _point.position;
            transform.rotation = _point.rotation;
            if (_rb != null)
            {
                _rb.position = _point.position;
                _rb.rotation = _point.rotation;
                _rb.useGravity = false;
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
            NoFall(_point.position.y);
        }

        private void FixedUpdate()
        {
            bool seatThere = _point != null;
            switch (_hold.Step(IsOwner, seatThere, seatThere ? Vector3.Distance(transform.position, _point.position) : float.NaN))
            {
                case SeatStep.Release:
                    Release();   // another computer runs him now: it starts him standing
                    return;
                case SeatStep.LetGo:
                    Log.Info($"SettlerPose: {name} lost their seat ({(seatThere ? "off it" : "it's gone")}); they stand up");
                    Stand();
                    return;
                case SeatStep.Keep:
                    Hold();
                    return;
            }
        }

        /// <summary>Every computer: the animator shows the saved pose (the owner's own bools are set as it changes).</summary>
        private void LateUpdate()
        {
            if (Zdo == null || IsOwner) return;
            ClearDrawLeftOver();
            Kind kind = Current;
            if (kind == _shown) return;
            Animator animator = AnimatorOf();
            if (animator == null) return;
            animator.SetBool(SeatAnimation, kind == Kind.Seat);
            animator.SetBool(BedAnimation, kind == Kind.Bed);
            if (kind == Kind.Dance || kind == Kind.Rest || kind == Kind.Kneel) ClearStop();
            foreach (string emote in s_heldEmotes) animator.SetBool(emote, emote == EmoteOf(kind));
            _shown = kind;
        }

        /// <summary>
        /// Not the owner: a practice bow's draw this computer set while it ran them (handed over mid-draw) is put down; the
        /// owner's own draw isn't synced, so nothing else sets it here.
        /// </summary>
        private void ClearDrawLeftOver()
        {
            if (!_drawSet) return;
            _drawSet = false;
            Animator animator = AnimatorOf();
            string draw = BowDrawFlag();
            if (animator != null && draw.Length > 0 && Has(draw)) animator.SetBool(draw, false);
        }

        /// <summary>This computer set the bow's draw on (as the owner), so a hand-over must put it down here.</summary>
        private bool _drawSet;

        private void SetBool(string animation, bool on)
        {
            if (string.IsNullOrEmpty(animation)) return;
            ZSyncAnimation zanim = ZanimOf();
            if (zanim != null) zanim.SetBool(animation, on);
            else AnimatorOf()?.SetBool(animation, on);
        }

        private void Save(Kind kind, ZDOID target)
        {
            ZDO zdo = Zdo;
            if (zdo == null) return;
            zdo.Set(SaveKeys.Pose, (int)kind);
            zdo.Set(SaveKeys.PoseTarget, target);
            _shown = kind;
        }

        /// <summary>Owner (1.6): an animator bool of a moment (a bow's draw), on this computer's body.</summary>
        internal void SetFlag(string animation, bool on)
        {
            if (!IsOwner || !Has(animation)) return;
            SetBool(animation, on);
            if (on) _drawSet = true;
        }

        private readonly Dictionary<string, bool> _hasParameter = new Dictionary<string, bool>();

        /// <summary>The body's animator knows this trigger or bool (a weapon's swing a settler's body may lack: no warning each time).</summary>
        public bool Has(string parameter)
        {
            if (string.IsNullOrEmpty(parameter)) return false;
            if (_hasParameter.TryGetValue(parameter, out bool known)) return known;
            Animator animator = AnimatorOf();
            if (animator == null) return false;
            bool has = false;
            foreach (AnimatorControllerParameter p in animator.parameters)
                if (p.name == parameter)
                {
                    has = true;
                    break;
                }
            _hasParameter[parameter] = has;
            return has;
        }

        /// <summary>vc_pose: an animator bool set by hand (the probe).</summary>
        internal void SetBoolForTest(string animation, bool on) => SetBool(animation, on);

        /// <summary>For the probe and the log: the animator's parameters that matter here, and their values.</summary>
        internal string DescribeAnimator(string match)
        {
            Animator animator = AnimatorOf();
            if (animator == null) return "no animator";
            var parts = new List<string>();
            foreach (AnimatorControllerParameter p in animator.parameters)
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(p.name, match)) continue;
                string value = p.type == AnimatorControllerParameterType.Bool ? animator.GetBool(p.nameHash).ToString()
                    : p.type == AnimatorControllerParameterType.Trigger ? "trigger"
                    : p.type == AnimatorControllerParameterType.Float ? animator.GetFloat(p.nameHash).ToString("0.00")
                    : animator.GetInteger(p.nameHash).ToString();
                parts.Add($"{p.name}={value}");
            }
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            var layers = new List<string>();
            for (int layer = 1; layer < animator.layerCount; layer++)
            {
                AnimatorStateInfo l = animator.GetCurrentAnimatorStateInfo(layer);
                layers.Add($"layer {layer} ({animator.GetLayerName(layer)}, weight {animator.GetLayerWeight(layer):0.0}): state {l.shortNameHash}");
            }
            ZSyncAnimation zanim = ZanimOf();
            string synced = zanim != null && zanim.m_syncBools != null ? string.Join(",", zanim.m_syncBools) : "-";
            return $"state hash {state.shortNameHash} tag emote {state.IsTag("emote")} | {string.Join("; ", layers)} | synced bools: {synced} | " + string.Join(" ", parts);
        }
    }

    [HarmonyPatch]
    internal static class SettlerPosePatches
    {
        /// <summary>A seated or lying settler counts as attached, as a seated player does: no fall, no walking pull.</summary>
        [HarmonyPatch(typeof(Character), nameof(Character.IsAttached)), HarmonyPostfix]
        private static void HeldIsAttached(Character __instance, ref bool __result)
        {
            if (!__result && SettlerPose.s_held.Count > 0 && SettlerPose.s_held.Contains(__instance)) __result = true;
        }
    }
}
