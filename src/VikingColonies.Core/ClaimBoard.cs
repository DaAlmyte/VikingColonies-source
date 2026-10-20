using System.Collections.Generic;
using System.Linq;

namespace VikingColonies.Core
{
    /// <summary>
    /// Which settler is working on what, so two lumberjacks don't go for the same tree, log or piece of wood.
    /// A settler claims the thing it works on, and the area where its felled tree came down (the logs and stump
    /// appear there). A claim nobody works on for a minute lapses, e.g. when its settler was fired or died.
    /// </summary>
    public sealed class ClaimBoard
    {
        public const double ExpirySeconds = 60;

        private struct Claim
        {
            public string Owner;
            public double Touched;
            public GroundPoint Center;
            public float Radius;
        }

        private readonly Dictionary<string, Claim> _things = new Dictionary<string, Claim>();
        private readonly Dictionary<string, Claim> _areas = new Dictionary<string, Claim>();   // one per owner

        /// <summary>Claims (or renews) a thing; working inside one's own area renews that too. False if someone else has it.</summary>
        public bool TryClaim(string id, GroundPoint pos, string owner, double now)
        {
            if (IsTakenByOther(id, pos, owner, now)) return false;
            _things[id] = new Claim { Owner = owner, Touched = now };
            if (_areas.TryGetValue(owner, out Claim area) && ColonyRules.Distance(pos, area.Center) <= area.Radius)
            {
                area.Touched = now;
                _areas[owner] = area;
            }
            return true;
        }

        public void Release(string id, string owner)
        {
            if (_things.TryGetValue(id, out Claim claim) && claim.Owner == owner) _things.Remove(id);
        }

        /// <summary>Marks an area (where a felled tree came down) as this owner's.</summary>
        public void ClaimArea(string owner, GroundPoint center, float radius, double now) =>
            _areas[owner] = new Claim { Owner = owner, Touched = now, Center = center, Radius = radius };

        /// <summary>True when someone else holds a live claim on the thing, or it lies in someone else's live area.</summary>
        public bool IsTakenByOther(string id, GroundPoint pos, string owner, double now)
        {
            Prune(now);
            if (_things.TryGetValue(id, out Claim claim) && claim.Owner != owner) return true;
            foreach (Claim area in _areas.Values)
                if (area.Owner != owner && ColonyRules.Distance(pos, area.Center) <= area.Radius) return true;
            return false;
        }

        private void Prune(double now)
        {
            foreach (string id in _things.Where(p => now - p.Value.Touched >= ExpirySeconds).Select(p => p.Key).ToList()) _things.Remove(id);
            foreach (string owner in _areas.Where(p => now - p.Value.Touched >= ExpirySeconds).Select(p => p.Key).ToList()) _areas.Remove(owner);
        }
    }
}
