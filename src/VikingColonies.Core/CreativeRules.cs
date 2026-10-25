using System;
using System.Collections.Generic;
using System.Linq;

namespace VikingColonies.Core
{
    /// <summary>Colony.CreativeMode: Auto follows the game's own creative signals; Off never.</summary>
    public enum CreativeSetting { Auto = 0, Off = 1 }

    /// <summary>A colony building's creative requests (saved numbers: new ones get new numbers).</summary>
    public enum CreativeAction { Build = 0, Upgrade = 1, Repair = 2, Supply = 3 }

    /// <summary>The Town Hall's creative requests (saved numbers: new ones get new numbers).</summary>
    public enum HallCreativeAction { SetLevel = 0, AddSettlers = 1, EatOff = 2, FinishAll = 3 }

    /// <summary>Why the owner refused a creative request (None: accepted).</summary>
    public enum CreativeRefusal { None, Off, NotManager, NotCreative, AdminUnknown }

    /// <summary>A colony's order as Finish all now sees it.</summary>
    public struct FinishCandidate
    {
        public double Created;
        public bool IsCamp;
        public OrderKind Kind;
        public bool Done;
        public bool Paused;
    }

    /// <summary>
    /// Creative mode (1.5, docs/superpowers/specs/2026-10-22-vikingcolonies-creative-mode-design.md): on when the world has
    /// No build cost or a player has debug mode's free building on, unless the setting is Off. Every request is checked
    /// again by the building's owner; the colony's own life (free builders, meals) changes only in a No build cost world.
    /// </summary>
    public static class CreativeRules
    {
        /// <summary>Finish all now finishes one order this often (two a second: no stall).</summary>
        public const float FinishAllGapSeconds = 0.5f;

        public static bool IsCreative(CreativeSetting setting, bool worldNoBuildCost, bool playerNoCost) =>
            setting == CreativeSetting.Auto && (worldNoBuildCost || playerNoCost);

        /// <summary>The owner's check: a manager of the colony, in a No build cost world or an admin (debug mode needs admin).</summary>
        public static bool OwnerAccepts(CreativeSetting setting, bool worldNoBuildCost, bool senderIsAdmin, bool senderMayManage) =>
            setting == CreativeSetting.Auto && senderMayManage && (worldNoBuildCost || senderIsAdmin);

        /// <summary>
        /// The owner's check, with why it refuses: <see cref="CreativeRefusal.AdminUnknown"/> when the building runs on another
        /// player's computer, which can't know whether the sender is an admin (only the server and the sender's own computer
        /// can; the review round of 2026-10-23: such an admin read "Creative mode isn't on for you here").
        /// </summary>
        /// <param name="adminKnowable">This computer can know the sender's standing: its own player, the server, or a request from the server.</param>
        public static CreativeRefusal Refusal(CreativeSetting setting, bool worldNoBuildCost, bool senderIsAdmin, bool adminKnowable, bool senderMayManage)
        {
            if (setting != CreativeSetting.Auto) return CreativeRefusal.Off;
            if (!senderMayManage) return CreativeRefusal.NotManager;
            if (worldNoBuildCost || senderIsAdmin) return CreativeRefusal.None;
            return adminKnowable ? CreativeRefusal.NotCreative : CreativeRefusal.AdminUnknown;
        }

        /// <summary>
        /// Supply what's needed is for a world where materials still count: where builders build free nothing is needed (the
        /// review round: in a No build cost world any manager pressed it again and again for free iron and bronze).
        /// </summary>
        public static bool SupplyNeeded(CreativeSetting setting, bool worldNoBuildCost) => !BuildersFree(setting, worldNoBuildCost);

        /// <summary>
        /// Whether a request's sender is an admin, from what this computer can know: its own player (admin or host; single
        /// player too); the server, seen from a client (a hosted game's host: the review of 1.5); another computer only on the
        /// server, by its admin list (a client knows no other client's standing).
        /// </summary>
        public static bool SenderIsAdmin(bool fromThisComputer, bool thisIsServer, bool localAdminOrHost, bool fromServer, bool serverSaysAdmin)
        {
            if (fromThisComputer) return thisIsServer || localAdminOrHost;
            if (fromServer && !thisIsServer) return true;
            return thisIsServer && serverSaysAdmin;
        }

        /// <summary>Builders need no materials or tools: only the world's setting, never one player's cheat.</summary>
        public static bool BuildersFree(CreativeSetting setting, bool worldNoBuildCost) =>
            setting == CreativeSetting.Auto && worldNoBuildCost;

        /// <summary>A colony spared its meals only while the world has No build cost; the server's setting off: nobody eats.</summary>
        public static bool SettlersEat(bool serverSettlersEat, CreativeSetting setting, bool worldNoBuildCost, bool colonyEatOff) =>
            serverSettlersEat && !(BuildersFree(setting, worldNoBuildCost) && colonyEatOff);

        /// <summary>The highest level a building placed in creative may get: a Town Hall any, a hut its Town Hall's.</summary>
        public static int MaxPlacedLevel(bool isTownHall, int hallLevel) =>
            isTownHall ? TownHallLevels.MaxLevel : HutLevels.Clamp(hallLevel);

        /// <summary>
        /// The placing level after a key press: 0 is "the builder builds it", then 1 up to <paramref name="max"/>; a level
        /// above a limit that went down steps from the limit.
        /// </summary>
        public static int StepPlacedLevel(int current, int step, int max) => Math.Max(0, Math.Min(max, Math.Min(current, max) + step));

        public static int SettlersToAdd(int asked, int settlers, int cap) => Math.Max(0, Math.Min(asked, cap - settlers));

        /// <summary>Which orders Finish all now finishes, in turn: oldest first, the camp last; no take-down, none done or set aside.</summary>
        public static IList<int> FinishAllSequence(IReadOnlyList<FinishCandidate> orders) =>
            Enumerable.Range(0, orders.Count)
                .Where(i => !orders[i].Done && !orders[i].Paused && orders[i].Kind != OrderKind.Remove)
                .OrderBy(i => orders[i].IsCamp ? 1 : 0)
                .ThenBy(i => orders[i].Created)
                .ToList();
    }
}
