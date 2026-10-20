using UnityEngine;

namespace VikingColonies.Construction
{
    /// <summary>What the builder's hand or pickaxe does to something on a site at the moment it lands.</summary>
    internal static class SiteClearer
    {
        /// <summary>Picks what can be picked (it drops beside it, for him to pick up) and removes the plant.</summary>
        public static void ClearByHand(SiteThing thing, Humanoid by)
        {
            GameObject root = thing.Root;
            if (root == null) return;
            ZNetView nview = root.GetComponent<ZNetView>();
            if (nview != null && nview.IsValid() && !nview.IsOwner()) nview.ClaimOwnership();   // so picking and removing happen here, now
            Pickable pickable = root.GetComponent<Pickable>();
            if (pickable != null && pickable.CanBePicked())
            {
                try
                {
                    pickable.Interact(by, false, false);
                }
                catch (System.Exception e)
                {
                    Log.Warn($"Picking {Utils.GetPrefabName(root)} failed ({e.Message}); it is removed instead.");
                }
            }
            // Picking removed it: a branch or a stone goes when picked; its object stays until the frame ends, but its network
            // object is gone, and destroying it again threw inside the game's effects (0.26 full run: 4 NullReferenceExceptions).
            if (root == null || (nview != null && !nview.IsValid())) return;
            Destructible destructible = root.GetComponent<Destructible>();
            if (destructible != null) destructible.Destroy(null);   // drops what it drops when destroyed, with its effect
            else ZNetScene.instance.Destroy(root);
        }

        /// <summary>
        /// One axe blow to a tree, log or stump. A tree falls the way it was last hit, so <paramref name="fallDir"/> (planned
        /// away from the site and from buildings) is the blow's direction.
        /// </summary>
        public static void Chop(SiteThing thing, ItemDrop.ItemData axe, Humanoid by, Vector3 fallDir)
        {
            IDestructible target = thing.Root != null ? thing.Root.GetComponent<IDestructible>() : null;
            if (target == null || axe == null) return;
            Vector3 at = thing.Root.transform.position;
            var hit = new HitData();
            hit.m_damage.m_chop = axe.m_shared.m_damages.m_chop;
            hit.m_toolTier = (short)axe.m_shared.m_toolTier;
            hit.m_point = at + Vector3.up;
            hit.m_dir = fallDir != Vector3.zero ? fallDir : (at - by.transform.position).normalized;
            hit.m_attacker = by.GetZDOID();
            target.Damage(hit);
        }

        /// <summary>One pickaxe blow to a rock (to the part of it on the site, for a big rock).</summary>
        public static void HitRock(SiteThing thing, ItemDrop.ItemData pickaxe, Humanoid by)
        {
            IDestructible target = thing.Root != null ? thing.Root.GetComponent<IDestructible>() : null;
            if (target == null || pickaxe == null) return;
            Vector3 at = thing.Position;
            var hit = new HitData();
            hit.m_damage.m_pickaxe = pickaxe.m_shared.m_damages.m_pickaxe;
            hit.m_toolTier = (short)pickaxe.m_shared.m_toolTier;
            hit.m_point = at;
            hit.m_dir = (at - by.transform.position).normalized;
            hit.m_hitCollider = thing.Area;   // big rocks take damage on the part that was hit
            hit.m_attacker = by.GetZDOID();
            target.Damage(hit);
        }
    }
}
