# Changelog

## 1.5.0 (2026-10-25) — The storehouse, creative mode and settlers standing up to weak animals

**For server owners:** the server and every player need 1.5.0. New settings: `Creative.CreativeMode` (Auto; the
server's value counts for everyone), `Storehouse.WoodCost` (20), `Courier.Carry` (50) and `Courier.CarryPerLevel` (25),
`Colony.SettlersFightBack` (on), and each player's two keys for creative mode. A Town Hall saves one new value, `vc_colony_eat_off`; the Storehouse is a
new building and saves who carries what (`vc_courier_tasks`) and whether its couriers are emptying it for a take-down
(`vc_store_closing`) and whether its chests are full (`vc_store_full`); a hut saves `vc_couriers_leave` when its "Couriers take the rest" is off; a settler's record
gains the Hauling skill. Worlds and colonies load as they were.

### New
- **A Storehouse: the colony's store**, a Norse storehouse on posts with a stair to its door (its outline starts 0.75 m
  up while you place it). One per colony, built by a Builder's Hut. Every chest standing inside it holds
  the colony's goods: the builder looks there right after its own hut, meals come from it first, and a worker whose hut
  chest is full takes the load there before the Town Hall. Its **Stock** tab shows what the colony's chests hold, most
  first, and where most of each lies. Taking it down, its couriers first carry the goods to the colony's other chests.
  Hugin explains it the first time you place one.
- **Couriers.** The storehouse hires couriers (one, then up to three as it grows) who carry for the whole colony: they
  fill what buildings keep in stock and the builder's order, take producers' surplus to the storehouse before their
  chests fill, put away what you drop in the storehouse's own chest, and keep the store like with like (each kind
  into the chest already holding it; sorting from level 3 tidies the rest). They never take what a
  building keeps, a Farm's or a lumberjack's seeds, or anything from the Town Hall. Once a colony has a courier, idle
  settlers stop carrying and rest instead. **Couriers take the rest**, on each hut's Keep in stock tab, turned off
  keeps that hut's chest as you arranged it.
- **The storehouse's Needs tab: your shopping list.** Every building short of something, with what and how much, who is
  bringing it or why nobody can ("only in the Town Hall", "no room in its chest"), and under them what nobody in the
  colony has: "Bring: 40 Iron, 10 Food (any)".
- **Creative mode.** In a world with the No build cost setting, or for an admin in debug mode with free building (B):
  place a colony building already built at the level you pick (**=** and **-** while placing); **Build now**, **Upgrade
  now** and **Repair now** beside each building's window; **Finish all now**, the Town Hall's level without the
  trophies, **Add settlers** and **Settlers eat** on the Town Hall; **Supply what's needed** on a Builder's Hut. In a No
  build cost world builders build for free. Survival play is as it was. See the guide's "Creative mode".
- **Settlers stand up to weak animals.** A Neck, a Greyling or a Boar that goes for a settler is fought off with what's
  in their hands, or fists, instead of the whole colony running and hiding again and again; Greydwarfs count from the
  Town Hall's level 2 and Skeletons from level 3. They still run when hurt to half, from anything stronger, from what's
  thrown from afar, in a raid or with the alarm, and a timid settler still runs. Every settler now heals when left alone,
  and is whole after a night in bed. Guards drive off a weak monster prowling in the colony before it bites. The setting
  `Colony.SettlersFightBack` (on).
- **The Town Hall's overview says when the storehouse is full**, with what to do (more chests inside it, or a higher
  level), and when couriers hold loads with nowhere to put them.
- **Repairs:** the builder mends the worst-damaged building first and finishes it before the next; a big repair goes in
  the Saga ("Ulf mended 14 pieces of the Longhouse").
- Hugin's storehouse tip also comes for a Storehouse placed already built in creative mode.

### Fixed
- Two settlers no longer stop to chat in a doorway, where they held up anyone going through (a courier could give up
  at the storehouse's door).
- The Builder's Hut's chopping block, adze and tool shelf stood too far from its workbench to upgrade it. From level 2
  the workbench stands in the covered yard with the chopping block and adze beside it, and from level 3 the tool shelf
  hangs inside on the same wall, so it reaches level 4. The Lumberjack's Hut's chopping block had no workbench: the hut
  now has one inside, along its right wall, and the chopping block stands by the door. The Builder's Hut's artisan
  table, too exposed to use at the yard's far end, stands inside from level 4. Huts built before 1.5 get them at their
  next upgrade; one already at level 5 keeps its old layout.
- A world whose name merely contained "test" ("Greatest", "Contest") counted as a test world: every step of every
  settler went to the log, and the test commands worked there. Test worlds are now only those whose name starts with
  `VCTest`; use such a name for a world where you make your own designs.
- A guard whose Watchtower raiders had broken (or whose tower wasn't loaded) still counted as on duty, without a weapon:
  they walked among the raiders and never ran, or stood out all night. Now they shelter like everyone until their tower
  is back.
- A worker who couldn't reach their hut with a load at dusk stood outside all night, and their hover text, switching
  between "Delivering" and "Can't reach the hut" every frame, saved their whole record each time (sent to every player on
  a server). Now they give that last errand a minute, then go in with the load still in hand, and a settler's changing
  hover text is saved at most twice a second.
- An old Longhouse (built before 1.4.3) whose upgrade puts a new piece where a player's spawn bed stands never finished
  its upgrade: the builder "took down" the bed again and again, leaving it standing. The bed now shows as in the way, for
  you to move, and builders never take down a bed someone sleeps in, a leftover's either.
- A repair of a Farm (a fence piece a troll knocked off) had the builder pull up every crop in the Farm's yard while the
  farmer replanted. A repair now leaves plants and crops where they grow.
- A builder putting leftovers back in their hut's chest, whose way round to it needed a door they couldn't get to,
  circled the hut for good. They now put them in from where they stand, as they already did while working on a site.

## 1.4.3 (2026-10-23) — The huts polished, and fixes

**For server owners:** the server and every player need 1.4.3. Each colony building saves one new value,
`vc_earth_level` (the level whose earth was painted), and there is a new ground operation, `vc_ground_earth` (it paints
trodden earth without moving the ground). Only the computer that owns a patch of ground sends it, so a player still on
1.4.2 who owns the ground there is skipped until a 1.4.3 player does it. Worlds and colonies load as they were.

### Changed
- **The huts, polished.** Every hut and the Town Hall's camp got a careful going-over:
  - every wall shows its plain side outside, on all four sides (the back and left walls showed their framing outside);
  - the Watchtower's ladders climb in one line onto a landing beside the platform, its roof stands higher, and the way
    from the landing onto the platform is clear;
  - the Town Hall's camp is drawn again as a camp site: shelters round one fire, the trophy stands in the side
    shelters, the chieftain's seat across the fire, a banner pole at the way in (the maypole, which can only be built in
    its season, is gone);
  - timber frames round the doors, posts at the wall joints and beams along the gables' edges from level 2, and crossed
    beams over both ends of every gabled hut;
  - the Longhouse has plank sleeping platforms along its walls with the beds on them, and trodden earth down the middle
    round the fire;
  - the Forager's porch keeps its front wall closed up to the roof round the door;
  - smaller fixes: the Builder's tool shelf hangs on the wall, the dvergr wall lanterns hang higher, no bed stands in
    the Longhouse's stone wall, the Lumberjack's torch stands by its door.
- **Trodden earth under every roof.** When a building or an upgrade is finished, the ground under its roof becomes
  trodden earth, as the hoe's path makes it: no more grass growing up through an earth floor or a low floor. Buildings
  you already have get it once, the first time they load with this version. Tilled ground is left as it is.
- **Your huts at their next upgrade:** the builder takes the back and left walls down and puts them up again turned
  round (a little wood, once), and a Longhouse's beds move up onto the new sleeping platforms.

### Fixed
- The Town Hall camp's left sitting log and the Longhouse's sitting log faced away from their fire: whoever sat there had
  their back to it. Both face the fire now (a camp or Longhouse already built gets it at its next upgrade).
- **Huts can be upgraded to level 4 again:** a level that needs a Dvergr lantern stopped the builder for good. It is built from a
  Lantern, which doesn't stack, and since 1.4.1 settlers took only items that stack from chests. The builder now takes
  anything a piece is built from.
- A Farm's seeds stay in its chest: settlers no longer eat the carrots a Farm plants (in the game a carrot is planted
  to grow carrot seeds), nor burn, build with or carry off the seeds its fields need.
- A field in the Farm's own fenced yard is worked: a finished Farm's yard is farmland. A Farm whose fields have no
  free ground says so, instead of "Needs a field".
- A tree stump standing partly on a hut's site is cleared before building, not built round.
- Messages such as "No jobless settlers…" show in the open window, or in the corner of the screen, instead of
  running across the middle of it.
- Guards gather at the alarm where they can stand: when it's sounded inside the Town Hall or beside a hut, each guard
  takes the nearest open spot round it that they can walk to, never one behind a wall, in a fenced yard or by the
  colony's fire.
- A settler whose nearest friend stands behind a wall asks the next one for a chat, instead of strolling off alone.
- Shift+E on a colony building no longer opens its window while you're attacking or dodging, and another mod's object
  on a hut keeps its own Shift+E.
- A shorter log for bug reports: a routine "no door leads there" line, written while a settler was already where they
  were going, is gone.
- A fallen settler's cairn says "Here lies …" first again when another mod changes what chests show on hover.

## 1.4.2 (2026-10-21) — Polish

**For server owners:** the server and every player need 1.4.2. One new setting, `Messages.AskForFeedback` (on by
default; the server's value counts for everyone). Worlds and colonies load as they were.

### Added
- **Tell us what you think.** Once, at a good moment (the first time a hut near you reaches level 2, or after two hours
  with a colony), a small window asks what you think of the mod, with a link to our Discord and one to the feedback
  form on GitHub. "Not now" closes it for good, and it never comes during a fight, a raid or over another window.
  There's also a **Feedback** button in the Town Hall's window, beside Report a problem. Nothing is sent from the game:
  the links open a page in your browser. Don't want the ask? Turn off `Messages.AskForFeedback`.

### Changed
- Settlers have much more to say: they talk about their own trade (the lumberjack about trees, the farmer about crops,
  the guard about the watch), about a raid the colony held off, and for a few days after a boss falls in your world
  (the Town Hall's saga tells it), and every topic has more lines (127 in all, from 68).
- A lumberjack says what they're doing ("Felling trees", "Gathering wood", "Keeping clear of falling logs"), and a
  settler on the way to a hut you're far from says "Heading to work", instead of a plain "Working".
- Archers in a Watchtower shoot monsters that swim or fly, and ones standing up to 15 m high; guards with swords still
  leave those alone.
- The colony says more of its rules where they happen:
  - the Town Hall's summary shows the food it keeps for newcomers ("Food in the Town Hall: 14 (it keeps 10 for
    newcomers)");
  - a fire the settlers keep burning says so when you look at it, with what it takes from the colony's chests;
  - a builder who needs something another building keeps says where ("Needs 12 Wood (more Wood is kept in the
    Lumberjack's Hut)");
  - settlers gathering at the Town Hall in the evening say no fire is lit outdoors.
- The licence, from this version: free to play, to share unchanged (in a modpack, with friends) and to make add-ons
  and compatibility patches for; publishing the code or a changed copy needs DaAlmyte's OK first. Versions up to 1.4.1
  stay MIT.

### Fixed
- A settler's daily greeting is no longer used up when too many words are on screen to show it.
- A builder breaking a rock from where they stand walks back to it if nudged away, instead of swinging at the air.
- Debug mode's K, and the console's killall, killenemies and killtame, no longer kill your settlers.
- A hungry settler eats food you put in later that day, instead of staying hungry until the next day (they look again
  every half minute).
- With MyLittleUI installed, Shift+E on the Town Hall and the huts opens their window again, and their hover says
  "Manage" instead of "Rename" (#1).
- Two settlers no longer chat face to face through a wall: they meet where nothing stands between them, or do
  something else.
- Settlers no longer open a door from beside it or from a roof above it. When a hut's door was blocked, a settler could
  step onto the low edge of its roof, "go through" the door onto the roof and keep trying all night; now the hut is
  given up within 45 seconds for another.
- `vc_blueprint save` takes only a plain name (letters, digits, `_` and `-`), so a design can't be written outside
  the designs' folder.

## 1.4.1 (2026-10-20) — Fixes

**For server owners:** the server and every player need 1.4.1. No settings changed; worlds and colonies load as they
were.

### Changed
- **Keep in stock** offers only things that stack (wood, food, bolts, seeds…). A settler carried a sword as just its
  name and put a new one down, so its quality, wear and maker were lost; weapons, armour and tools now stay where they
  are, for you to move by hand.
- A guard's call that raiders are coming reaches the players in or near the colony (its area and 30 m more), not
  everyone on the server.

### Fixed
- Necks, Greylings and Boars no longer send the whole colony hiding and out again over and over (54 times in 15
  minutes, and nothing got built): only the settler one is after runs from it, everyone keeps clear of a fight, and
  the rest work on. A fleeing settler no longer stands still at the Town Hall's camp or an unbuilt hut while a monster
  that followed bites them: they run on.
- Settlers no longer shove each other or you when they bump into each other: they pass through.
- A settler inside one of your buildings past the colony's edge at night now leaves it by its door and goes to bed,
  instead of trying far doors all night.
- A long walk to a door is no longer given up after 12 seconds (settlers gave up on doors 40–60 m away).
- In the evening the colony gathers at the camp's fire or one outdoors, not at a fire pit inside a hut, where the seats
  fell among walls and beds.
- Settlers' words no longer pile up at odd places on the screen when they are just out of view; at most 8 show,
  nearest first.
- A newcomer arriving out of view no longer appears in plain sight over a rise.
- The Town Hall's and huts' windows open again after you leave a world and enter one in the same game (they stayed
  shut until the game was restarted). Long answers in them shrink to fit instead of losing their end, and a hut's window
  no longer shows the last hut's upgrade cost for a moment.
- A guard hired at night or in a raid arms at once, instead of going to bed unarmed.
- Ballista bolts left in a guard's load when the alarm sounds go back to a chest on the guard's next round, instead of
  staying there for good.
- A lumberjack who sits down in free time no longer finds no tree to fell (the axe was put away).
- A farmer no longer makes the game stutter while you have the Farm's chest open.
- A settler called back by the Town Hall gets out of bed and leaves breakfast first.
- Safety: a broken or hostile building plan from another computer can no longer crash every computer that loads it,
  and a broken text from one no longer throws errors every frame.
- A big rock reaching into a hut's or the Town Hall camp's site from beside it is broken up like one standing on it
  (a camp was built into a big boulder whose middle stood just off its lot). The builder works each of its parts from
  the site's side, on the ground. A part still out of reach after three tries is left if it only reaches the edge of
  the site, so the building goes up anyway; where the building itself goes, the site waits and the hut's window says
  what's in the way. The builder never breaks a rock one of your buildings rests on: where the building itself would
  go into it, the site waits and the hut's window says a rock is in the way.
- Inside a hut is what its walls enclose, not its whole lot: a settler standing behind a hut's back wall (by the
  Builder's Hut's woodpile) is no longer walked straight through the wall towards the door, which could put them on
  the roof.
- Builders no longer take from or put into a chest through a wall: standing within reach of it with a wall between,
  they go round to its open side first.
- A player's grave, or a crate from a ship, on a hut's or the camp's site is no longer called "a rock nothing can break":
  the site says "A grave or a crate stands where it goes: it goes once it's emptied".
- A hut taken down on a slope no longer leaves a stack of its materials lying: what rolled off its ground goes into the
  Town Hall's chest with the rest (a pile of yours that lay beside the hut before stays).
- In an upgrade, a piece that stays and rests on an old one alone (an ornament on a ridge being replaced) is taken down
  with the hammer and put up again, instead of falling when the old piece makes way.
- The camp's waiting reason in the Hall tab is shown in each player's own language (it was stored in its owner's), and
  a camp site that can't be built is looked at again once a minute instead of every 15 seconds.
- A builder waiting for someone to move off a piece that went up before and fell waits the usual few seconds again,
  instead of setting it aside at once.
- Mushrooms lying on the ground no longer count as another building. A hut on a slope with some lying by it could
  never be finished: the builder left the ground round them uneven, a piece that stands on the ground there (the
  Lumberjack's Hut's torch by its door) had nothing to stand on, the builder waited at it ("Can't place: no support
  for …"), and a Lumberjack's Hut never got its lumberjack. A site with mushrooms lying on it is no longer red
  ("another building stands there"), and the builder carries them off.

## 1.4.0 (2026-10-19) — New huts and the Hall of Trophies
The first update since 1.0.0. It also brings 1.1.0 to 1.3.0 below (guards, archers and defending the colony), which
never came out on their own.

**For server owners:** the server and every player need 1.4.0 (with Jötunn 2.30.2). Your 1.0 settings keep their
values; the new ones are listed under each version below, with sensible defaults. Worlds from 1.0 load as they were.

### New
- **New huts**: every hut has a new building at each of its five levels, made for its trade: the Builder's Hut's
  workshop with a covered yard for its tools, the Lumberjack's log house and woodshed, the Forager's porch, the Farm's
  barn and yard, the Longhouse's hall round its hearth, the Watchtower with its ballista at the platform's edge (where
  it can aim: behind the old railing it never could). Each hut has its own shape, its chest against a wall and crossed
  beams over its gables, and looks its age: thatch and logs, a stone plinth, darkwood shingles, grausten. Each level is
  built from what its age gives (nothing a trader sells) and costs a few minutes' gathering; the Longhouse is smaller
  and far cheaper at level 5 (49 Grausten, not 381).
- **The Town Hall camp**: the builder puts up a camp round the Town Hall's chest, and it grows with the hall's level:
  a fire pit, a lean-to over the chest and torches (1); sitting logs, a maypole and a trophy rack (2); the fire becomes
  a bonfire, with a chieftain's stone seat and a second rack (3); shingles and dvergr lanterns (4); grausten pillars and
  lava lanterns (5). Trophies on its racks count for raising the hall, and the colony gathers at its fire in the
  evening. A new Town Hall orders it at once (a builder puts it up after the huts); an existing colony gets it with
  the Hall tab's **Build the camp**. It waits while your own pieces stand where it goes, and the Hall tab says why.
- **Town Hall levels**: the Town Hall has levels 1–5, one for each age of your journey. A level is raised by **showing
  the hall the trophies of the Forsaken you've beaten since** — level 2: Eikthyr; 3: The Elder and Bonemass; 4: Moder
  and Yagluth; 5: The Queen — hung on item stands within 10 m or put in its chest, and paying materials from its chest
  (level 2: 40 Wood, 20 Stone, 10 Deer Hide; up to level 5: 30 Yggdrasil Wood, 40 Black Marble, 10 Refined Eitr). The
  trophies aren't used up: they can still go to the sacrificial stones. **A Forsaken your world has already beaten
  counts too**, so a trophy already on the stones never has to be won again (setting `Town Hall.BeatenBossesCount`).
- **What a level gives**: a colony area 10 m wider (50 m at level 1, 90 m at level 5), room for 5 more settlers (10 at
  level 1, 30 at level 5) and the next age of huts: huts go up to the Town Hall's level (level-2 huts once Eikthyr's
  trophy has raised the hall, level-5 huts after the Queen's).
- **The Hall tab**: a fifth tab in the Town Hall's window shows the level, what it gives, what the next level needs
  (ticked off as you bring it) and gives, the trophies shown in the hall, and **Raise to level N**. The Saga records it,
  and everyone in the colony's area hears of it.
- Hugin tells you about it the first time you carry a trophy that would raise your hall.
- **Report a problem**: a button in the Town Hall's window (beside Close, on every tab) writes the bug report, copies
  it, and opens the bug form on GitHub with the versions and the report's first lines filled in: paste the report into
  the form's last box and say what happened. Nothing is sent until you press Submit on GitHub. `vc_bugreport form`
  does the same from the console.
- New settings (section `Town Hall`): `Levels`, `BeatenBossesCount`, `RadiusPerLevel`, `SettlersAtLevel1`,
  `SettlersPerLevel`, `Level2Cost` … `Level5Cost`.

### Changed
- **Old colonies** start at level 1 and keep everything: huts above level 1 keep their level, settlers above 10 stay.
  **Changed from 1.0** (where only the Builder's Hut limited huts): huts go as high as the Town Hall, so a 1.0 colony
  raises its hall before upgrading further. The bosses its world has beaten already count, so it can raise the hall
  at once with the materials. A new colony's huts, its Longhouses' beds included, stay at level 1 until Eikthyr.
  `Town Hall.Levels = false` keeps 1.0's rules. The Citizens tab says when newcomers wait for a bigger hall, and a
  hut's window when an upgrade waits for one.
- A Town Hall that takes over a colony which lost its own keeps the level the colony had reached.
- The Town Hall's chest gets a row more per level, as huts' chests do.
- **Old colonies' huts are rebuilt at their next upgrade**: the builder takes the old building down piece by piece
  (its materials go back to their hut) and puts up the new design, the doorways first, so nobody is walled in or sent
  to an old door. The hut works on at its old level until the new one stands.
- A Watchtower's guards stand 2.8 m in front of its door, out of the way of anyone coming out.
- **An upgrade never takes down a bed a player sleeps in**: when a hut's next design has no bed where a player's spawn
  bed stands (an old Longhouse's beds, for one), that bed stays where it is.

### Fixed
- **The builder judges what holds as the game does**: they had counted on support from pieces a little too far off, so
  a piece could fall just after they set it and go up again.
- A design with an artisan table (or another station with a wide work area) no longer counts as standing in the way
  of buildings 20 m off, and its table no longer waits for ever.
- The Watchtower's level 3 has a stone platform under its stone railing (on wood the railing would have fallen).
- Several builders no longer fetch the same materials twice: a builder who moves to another site puts back what it
  doesn't need there, so no hut waits for an item that is in someone's pockets.
- A builder waiting for someone standing where a piece goes works on other pieces meanwhile (the camp, where the
  colony gathers, could wait for ever). A settler standing there is asked to make way, and moved out by the door if
  they stay (a forager stayed in their own hut while it was upgraded); someone under a roof's slope no longer counts
  as standing where the roof goes.
- A settler who goes through the same door again and again without getting any closer stops trying it for a while,
  and the usual ways out of being stuck take over.
- A tree the builder must clear may fall onto the hut's own old pieces that are coming down anyway.
- A rock nothing can break (a cliff, or a boulder no pickaxe breaks) where a hut or the Town Hall's camp goes now turns
  its site red, "A rock nothing can break stands where it goes", instead of the building going up inside it: the hut
  can't be placed there, an upgrade is refused with that reason, and the camp waits with it in the Hall tab.
- In an upgrade, a piece the new design moves a little (an item stand 10 cm further out on a thicker wall) is no longer
  taken for the old one: the builder took it down again and again and in the end left it out.
- Builders leave materials that fell onto a roof or a wall top where they lie, instead of climbing up after them and
  getting stuck there (seen with a design that changes its roof on an upgrade).
- A settler who ends up on a roof (stepping out of a door under a low eave, say) is put back down by the Town Hall
  after a few seconds.
- A rescued or recalled settler is never set down in or beside a fire (the camp's fire burns by the Town Hall).
- A settler who keeps burning where they stand (squeezed between a wall and a fire, say) is moved away by the Town
  Hall after a few seconds and the fire on them put out.
- **A last resort for stuck settlers that always works**: one who has wanted to be somewhere else for four minutes
  without getting more than 10 m from where it started is moved to a free spot by the Town Hall, whatever held them up
  (a builder in a hut being rebuilt tried a door for 25 minutes, and the old rescue never counted that). As before: out
  of sight if it can be, never at night or in a raid, at most twice in ten minutes.

## 1.3.0 (in 1.4.0) — Defending the colony
- **Orders and posts**: a Watchtower's window sets its guards' **Orders** — walk rounds, or **hold the post** (a
  gatehouse) — and **Post here** puts their post where you stand.
- **Calls for help**: a monster attacking a settler or a player anywhere in the colony's area brings the guards of the
  towers within 80 m, past their own patrol.
- **The alarm**: the Town Hall's window can **sound the alarm** — everyone else shelters indoors, the guards gather
  where you stood and fight there (a call for help still brings them) — until **All clear** (5 minutes at most).
- **Ballistas**: guards on watch keep the colony's ballistas loaded from what its chests can spare; a ballista never
  aims at a settler, nor in a colony's area at a player, and its bolts never hurt a settler.
- **Raiders called out**: in a raid, the first guard to see a raider calls out where they come from.
- **Safer near a tower**: no monsters spring up within a finished Watchtower's circle, as by a workbench.
- **A tally**: each guard counts the monsters they killed.
- New settings: `Guards.HelpRadius`, `Guards.LoadBallistas`, `Watchtower.StopsSpawns`.
- From the 1.0 play-through: a Cultivator in any of the colony's chests does for the farmer and the lumberjack's
  replanting (not only their hut's); an order the builder set aside for materials says so once the chests hold them
  ("the builder comes back to it"); help texts no longer call every settler "he"; a new build's button says "Cancel",
  not "Cancel upgrade"; "25 m" and a mood's score no longer break across lines.
- Smaller things: a warrior never stands behind their shield for long without striking (a monster striking on and on
  at someone beside them kept it up); a guard whose way to their post stops short counts as at their post; an archer who
  can't see a monster in range walks on until they can; a guard running round the edge of
  their watch is no longer called back early; a tower of two or three places never hires past them while the Town Hall
  is far away.

## 1.2.0 (in 1.4.0) — Archers, armour and more guards
- **Archers**: a Watchtower's window sets its guards' **Role** — warrior or archer. An archer shoots fully drawn shots
  at monsters they can see within 25 m with a copy of the best bow or crossbow in the chests and its ammunition (never
  used up), and steps back from one that comes too near.
- **Armour**: guards wear copies of the best helmet, chest, legs and cape in the chests, and it protects them as a
  player's does.
- **Shields block**: a warrior holds their shield up between blows and it blocks as a player's does; after three quick
  blocks they lower it a moment.
- **Tougher with Fighting**: half again their health at Fighting 100.
- **More guards**: a Watchtower holds two guards at level 3 and three at level 5, each at their own place; its window
  lists them, Fire lets one go at a time.
- Smaller things: a guard isn't saddened by being hit; only a guard on duty counts for "a guard on watch"; their rounds
  stop outside each building instead of walking in; thrown weapons are never a guard's weapon.

## 1.1.0 (in 1.4.0) — Guards and the Watchtower
- **The Watchtower** (20 Wood): a new colony building, put up by the builder like every hut, five levels by era. It
  hires a **guard** who keeps watch by its door, walks a round of the colony every few minutes and fights the monsters
  that come near it — one attacking a settler or a player first — with a copy of the best weapon and shield in its
  chest. They never hurt a player, a settler, a tame or an animal minding its own business (nor a wall or a crop beside
  the monster), never chase further than their patrol + 10 m, and below 30% health they are off duty and go back to heal. Armed, they stay out at night and in raids while everyone
  else shelters. Their hits grow their new **Fighting** skill, which makes them stronger.
- **The colony grows tower by tower**: a finished Watchtower adds the ground 25 m round it (45 m at level 5) to the
  colony's area, as MineColonies' guard towers do, so huts can be built by it — up to `Colony.MaxReach` (150 m) from
  the Town Hall (a tower near that edge adds what fits). The rings shown while placing a hut show every circle.
- Everyone in a colony with a guard is glad of it (+3 mood); a guard who falls "fell defending the colony" in the Saga,
  and a raid held off names the guards.
- New settings: `Watchtower.WoodCost`, `Colony.MaxReach`, `Guards.Enabled`, `Guards.ChaseBeyondPatrol`,
  `Guards.RetreatHealth`.

## 1.0.0 — The first release
Everything below (0.1 to 0.27), checked once more in a play-through of a whole colony, with these last touches:
- The **Trees list**'s explanation has room for all its lines, and the snow fir has a name of its own (it showed as
  "SnowFirTree", twice).
- A chosen settler's details on the Town Hall's **Citizens** tab are readable (they had shrunk to a tiny font), and a
  long status no longer runs off the end of its row.
- The builder names the tree that needs a better Builder's Hut ("a tree (Birch)"), so you know which to clear.
- The farmer says where their Cultivator goes ("Needs a Cultivator in the Farm's chest"), and so does the lumberjack's
  replanting line.
- A lumberjack in a wood of trees their hut's level (or your Trees list) keeps them off says "No trees to fell (see
  Trees…)", not "No trees nearby".
- Plugin id `daalmyte.vikingcolonies` (the settings file is `BepInEx/config/daalmyte.vikingcolonies.cfg`); MIT licence.

## Before 1.0

### 0.27.0 — A bug report in one file
- **`vc_bugreport`** (console, F5; no cheats needed) writes `BepInEx/VikingColonies-bugreport.txt` to attach to a bug
  report: the mod's and the game's versions, your other mods, whether everything started, the colonies around you
  (their buildings and orders, each settler's state, what they did lately and anything that went wrong for them), and the
  mod's last warnings and errors. Type it near the settler or building with the trouble.

### 0.26.0 — The last small things
- **Stop at**: with enough wood the lumberjack finishes the tree they felled (its wood, logs and stump) before they rest
  (it lay there until they worked again); other wood lying near stays where it is, and no new tree is felled.
- **Skills grow only for work that happened**: a plot cultivated, planted or harvested, a pick that picked, a piece
  placed, taken down or mended (a failed try counted too).
- **Hand over** counts what the builder already carries for their order (and so does "Still to bring"), and tops up the
  Town Hall's Keep in stock first, so the builder can still take what's short (before, a Town Hall keeping Wood
  swallowed the handed-over wood).
- **Trees by your buildings** are measured from any part of a piece, not its middle: a tree by the end of a long wall
  stays standing.
- **Take down** sweeps up only the materials on the hut's own ground (a pile of yours beside it stays), and a hut is
  taken down only when all of it is loaded ("Come closer…"), so no piece stays standing out of reach.

### 0.25.0 — Hardening for 1.0
- **A Valheim update can't switch the whole mod off**: the start-up runs part by part, each hook on its own, so a hook
  whose game method or field was renamed fails alone, is named in the log, and everything else starts (and the game's
  own code is never left with a broken hook in it).
- **A settler's error can't stall the game's other creatures**: an error anywhere in a settler's update (meals, doors,
  rescues, not only their work) makes them Dazed for a moment, as one in their work did.
- **The farmer looks over big fields cheaply**: a level-5 Farm's fields (about 600 plots) are looked up in a grid, and
  its chest is read once per look.
- "No jobless settlers" says how newcomers come (it spoke of a Tavern that isn't coming). A refusal on the Town Hall or
  a Longhouse ("not your colony") now reaches the player who asked, in the open window (or on screen), as on every hut. The test shortcut "upgrade at once" checks who
  asks. A settler changing computers right as their bed is assigned no longer reads their old record.
- The build has no warnings (an outdated Unity call, the tests' null warnings).

### 0.24.1 — Test tools (for development; nothing changes in play)
- `vc_watch <settler|nearest> [seconds]`: a settler's place, status, job, path, goal and the door near them in the log
  every half second while it changes; `vc_shot [label]`: a screenshot from inside the game; `vc_perf` also gives the
  size of the game's walking map (tiles and links).
- The test runner keeps every run's transcript and game log, sums up the log's errors and warnings, stops at once when
  the game freezes, closes or waits on a popup, and says when Valheim doesn't start (a Steam dialog waiting).

### 0.24.0 — Polish: the small things
- **Take down leaves nothing behind**: the builder no longer fells trees or breaks rocks round a hut they take down,
  an old level's leftover pieces come down with it, and the last pieces' materials lying where it stood go to the
  Town Hall's chest. Hand over never picks a take-down as "their order".
- **Trees list**: the game's trees are told apart by whole names (a mod's "FireTree" or "Pineapple" keeps its own
  line), and a greyed Replant tick says which hut level replants it.
- The lumberjack's search for a tree is quicker in a big base; a settler without a job says "Delivering (the Town
  Hall's chest is full)"; Keep in stock refusals show on the Town Hall and the Longhouse too; settlers don't ask
  another player's computer for a chest that has nothing to spare.
- A newcomer no longer now and then appears in plain view: the spot out of view is judged where they will stand.
- Tests: the self-test's door check watches where a settler crosses the doorway (it failed when they walked out
  through a door left open); the shelter check keeps animals away and the words check keeps the settler near you
  (an animal wandering by, a chat with a friend made them fail now and then).

### 0.23.0 — The builder
- **Hand over** (MineColonies' arrow): on the Builder's Hut window, what their current order still needs beyond the
  colony's chests goes from your bag into their chest; on the Town Hall's Work orders tab, for the chosen order, into the
  Town Hall's chest. "Handed over 12 Wood, 4 Stone".
- **Take down**: a finished hut's window asks twice, then a builder takes its building down from the top and brings
  the materials to their hut; the hut's chest goes last, its contents to the Town Hall's chest, its worker is let go, and
  the saga tells it. Work orders shows "take down". Cancel take down stops it (what came down is put back as a
  repair).
- (Crafting makes them faster since 0.22.)

### 0.22.0 — The lumberjack and skills
- **Trees list** on the Lumberjack's Hut (in place of the replanting list; MineColonies' Saplings tab): a line per tree
  kind with **Fell** and **Replant** ticks. Untick Fell and they leave that kind standing. Your replanting choices
  carry over.
- **Trees by your buildings stay standing**: they don't fell a tree within 3 m of something you built
  (`Lumberjack.LeaveTreesNearBuildings`, 0 = off). The builder still clears trees from a building site.
- **Stop at** on the Lumberjack's Hut: no limit, 200, 500 or 1000 Wood. With that much in the colony's chests they
  rest ("Enough wood").
- **Skills for every worker**: the farmer and the forager learn Farming, the builder Crafting (as the lumberjack learns
  Woodcutting); a skill of 100 works twice as fast as 0. Shown in the hut's window and the Town Hall's Citizens tab;
  kept when a settler is brought back. The learning rate is now `Colony.LearningRate` (your old
  `Lumberjack.LearningRate` moves over).
- Untick a kind while the lumberjack walks to such a tree and they leave it standing. A field marker doesn't count as
  a building (trees by it are felled).
- The farmer: a plot where a crop can't be planted waits 10 minutes, then 30, then an hour (it cost a seed fetch
  every 10 minutes).

### 0.21.0 — Keep in stock
- **Keep in stock** (MineColonies' minimum stock): every hut, the Longhouse and the Town Hall have a **Keep in stock**
  tab, a list of items with amounts its chest keeps (5 per level on a job hut, 10 per level on the Longhouse, 10 on the
  Town Hall). − / + by 5, Shift by a whole stack, × to remove, **Add…** for Food (any) and everything the colony's chests
  hold. Hugin explains it the first time.
- **Other settlers leave kept items alone**: breakfast, meals, fires and builders take only what's beyond the kept
  amount; a building's own worker uses all of it. A settler whose meal is due eats kept food only when there's no other
  food in the colony.
- **Carried in free time**: a settler with nothing to do brings what a building keeps and lacks from a chest that can
  spare it, up to 20 at a time ("carrying 10 Raspberries to the Town Hall"). Setting `Colony.CarryToStock` (on).
- **The Town Hall keeps 10 food** for newcomers until you change its list.
- **A full chest no longer stops a worker**: a lumberjack, forager or farmer whose hut chest is full takes their load to
  the Town Hall's chest or the nearest one with room ("Delivering (the hut's chest is full)"); "Storage full" only when every
  chest is full.

### 0.20.1 — Fields only, and door fixes
- **A Farm works only its fields**, as in MineColonies: no more ground round the Farm. Place a field marker near it,
  choose its crop and size (now up to **11×11**), and the Farm's level gives more fields (1 to 5). A Farm without a
  field says so ("Needs a field", and Hugin explains the first time); crops already round a Farm are yours to harvest.
  The "work the ground round the Farm" tick and the settings `Farmer.FieldRadius` and `Farmer.RadiusPerLevel` are gone.
- **Doors**, from what other colony mods do: a settler finding a door open towards them (opened from the other side)
  closes it and opens it from their side; they see a closed door in their way by where they are going, not where they face
  (after a hop they could stand facing sideways at a closed gate); a player standing in a doorway holds them up two
  seconds, then they go on through instead of giving up that door.
- Tests start on a cleared spot (old test colonies, cairns and field markers wiped first).

### 0.20.0 — Worker polish, part 1: fields
- **Field markers:** a wooden sign in the hammer's Misc tab marks a square field round it (3×3 to 9×9 plots) with its
  own crop, or the Farm's list; the sign shows the crop. E opens its window (crop, size, Farm, how its plots stand).
  A new field joins the nearest Farm with room by itself; one you assign or take away by hand stays as you set it.
  Only players who may manage the colony place one in it.
- **A Farm works one field per level** (1 to 5) as well as the ground round it; its window's new **Fields** tab lists the
  colony's fields with Assign / Leave, and turns the ground or new fields off.
- **Seeds by the handful:** the farmer takes seeds for every plot waiting for that crop at once (up to 20) instead of
  walking to the chest for each.
- **Turned huts:** a worker in a hut turned off the compass no longer gets trapped inside (the door's side comes from the
  hut's shape, not the game's walking map, and where the walking map gives them no way to the door they walk there
  straight, round the chest); one wedged inside is moved to just inside its door after half a minute.
- **Hut windows in tabs**, with **Recall**: the worker back to just outside their hut's door.
- A plot the farmer can't get to rests for ten minutes (it came back every minute).
- **Fixed:** a settler in free time could end a stroll in a campfire, sit down there and burn to death; strolls and
  sitting down now keep clear of fires.
- Test tools: `vc_workertest` (10 checks), `vc_observe` (watch a whole stocked colony work).

### 0.19.0 — Polish, part 2: the colony
- **Costs follow the settings:** a colony building's cost changed in the settings is what the hammer asks for at once,
  and on a server the server's cost from the moment you join (it took the costs read at start-up).
- **No settler body:** if the settler body didn't load (after a game update), founding a colony, a newcomer's day and
  Bring back tell players near "Settlers can't come…" instead of an error; nothing half made, no food eaten.
- **Last seen:** a settler out of sight shows "(far away)" or "(seen day 12)" in the Citizens tab.
- **Newcomers out of view:** they appear where no player is looking (behind you, or past a wall or a hill), on the
  game's walkable ground (never in a boulder or under a floor).
- **The colony's mood on every computer:** kept on the colony's list of settlers, so every player sees the same mood
  and the same newcomer rule; a cosy bed counts while its area isn't loaded.
- **PlantEverything's bushes on the Farm:** berry bushes, mushrooms, thistle and dandelions it makes plantable are on
  the What to grow list, unticked until you tick them, with their price; the farmer plants each for its price, picks it
  when ripe and leaves it to regrow.
- Test tools: `vc_polishtest` (the colony polish in a few minutes), `vc_crops` (the Cultivator's pieces and the crops).
- The save-format tests now pin the fields 0.16–0.19 added (who a settler is, their mood and meals, the Saga's raids
  held off, the roster's mood), so 1.0 reads every world saved since.

### 0.18.0 — Polish, part 1: settlers
- **Doors:** a settler going to something inside a hut goes in by that hut's own door, and leaves the hut they are in by
  its own; a farmer no longer wanders through a neighbour's hut to reach the Farm's chest (the self-test's occasional
  farm failure).
- **Words:** "Here 1 day."; "with a trader's cart"; the Town Hall says which Longhouse bed is theirs ("Longhouse bed 2");
  the hut's window says when its worker is good at its trade; Hugin's mood tip comes with the words above heads off
  too, and says where to see why.
- **Settlers:** the cheerful are cheerful only when content; the lazy tend fires half as often; each goes to breakfast
  at their own moment in the first minute (waiting where they are, their work not begun), so twenty don't reach one chest on
  the same tick.
- **Two players:** the evening ring comes from the colony's list of settlers, the same on every computer, so settlers
  run by different players keep to their own spots (a computer that hasn't loaded the Town Hall still counts only its
  own); two settlers of one name take turns when they chat.
- **"Hungry" now and then:** a hungry settler says so for a few seconds every half minute, with their chat and small talk
  in between, instead of all day (seen in play: it stuck over a settler's head through their chats). The hover always
  says it.
- **Under the hood:** the colony's Saga is read again only when it changes; a bed's piece is kept while it stands;
  bed numbers count the Longhouse's planned beds;
  Bring back fills in who a settler is from a keepsake made before 0.16; a damaged saved mood reads as unknown; no
  empty-name lines in the log for a new settler.

### 0.17.0 — Settler life, part 4: mood
- **Mood.** Each settler has a mood from 0 to 100 with plain reasons: a home and a cosy bed, a meal a day and varied
  food, an evening by the fire with company, work in their own trade, and the colony's news (a death, a raid held off);
  hunger, no home, no work or a monster's hit take it down; cheerful and grumpy settlers start higher or lower.
- **What it does.** Joyful settlers work 15% faster, unhappy ones 15% slower, miserable ones 30% slower. Nobody joins
  an unhappy colony; a joyful one gets one more newcomer a day. Nobody leaves.
- **Seeing it.** The hover ("· Content"), the Town Hall's details line ("Content (62): +5 home, +5 cosy bed…"), the
  colony's mood in the Citizens tab, an unhappy settler's worst reason above their head now and then, and a Hugin tip.
- **Varied meals.** A settler eats the plainest food that isn't what they had last time, when there's a choice.
- The cheerful talk of good things and the grumpy grumble. The Saga tells of a raid held off with nobody lost.
- Setting `Colony.Mood`.

### 0.16.0 — Settler life, part 3: newcomers and who they are
- **Walking in.** Newcomers appear at the colony's edge (dry ground with a way in, as far from every player as can be) and
  walk to the Town Hall: "Astrid is coming to live in the colony: a forager from the coast." The first settlers of a
  new Town Hall walk in together. Setting `Colony.NewcomersWalkIn`.
- **A trade they're good at.** Each settler is good at one trade (or none): 20% faster in it (`Colony.CallingBonus`). A hut
  hires the one good at its trade first, and newcomers often bring the trade an empty hut needs; the first settlers
  bring a builder.
- **A trait**: hardworking, lazy, cheerful, grumpy, chatty, quiet, brave or timid, each with one plain effect (work
  pace, free time, small talk, when they run from monsters).
- **Where they came from** and the day they came: in the Saga, the hover and the Town Hall's details line.
- **Clothes by trade**: rags, leather, a builder's cap, troll-hide trousers; for looks only.
- New settlers get names nobody in the colony has.
- Review fixes: newcomers with no way in appear by the Town Hall (an island, a closed palisade); one hired on
  their way in no longer says where they're from while working; only walking counts toward their three minutes, and they are
  then placed with no "called back" line; clothes that didn't go on (fired mid-swing) are put on again.

### 0.15.0 — Settler life, part 2: the day
- **Breakfast at the table.** The daily meal is eaten in sight. After dawn each settler walks to the colony chest
  that holds their food, takes it, and eats it on a seat by the chest (or standing by it) before work. If they can't get to
  it within a minute, they eat where they are, as before.
- **The evening by the fire.** From 0.70 of the day work stops, and settlers gather round the colony's fire (the burning
  campfire or hearth nearest the Town Hall, a roofed one first; the Town Hall with none). They take the seats near it or
  a ring round it, facing it, some sitting, some raising a toast, with evening small talk. Then they go to bed. Setting
  `Colony.EveningGathering`.
- **Sleeping in beds.** At night a settler with a home lies in their Longhouse bed and gets up beside it at dawn; never in
  a bed a player lies in or has claimed. Setting `Colony.SleepInBeds`.
- **Fires kept fed.** Settlers with nothing to do feed the colony's fires below half from its chests (never the last
  10 of the fuel), one settler per fire. Setting `Colony.TendFires`.
- Hugin tells you, once, to light a fire for the evening.

### 0.14.0 — Settler life, part 1: free time
- **Free time.** A settler with nothing to do sits on a bench, chair or stool, sits by a burning fire facing it,
  chats with another settler, strolls or sits down, near their hut (the Town Hall for the jobless). That covers the
  jobless and a worker whose job waits with nothing to do: no trees, crops growing, nothing to pick, no work. They
  pick again every 30–90 seconds and get up the moment there's work, a monster, night or a recall. Any seat in the
  game (a mod's too) and any campfire, hearth or bonfire count. Setting `Colony.FreeTime`.
- **Greetings and small talk.** A settler greets you by name the first time each day you come near. In free time
  settlers make small talk above their heads: about a death in the last days, the weather, a newcomer or a new
  building today, their job. A chatting pair takes turns. Needs always come first. Setting `Messages.SmallTalk`
  (your own).
- The hover says what they're doing: "Crops growing (taking a seat)", "Jobless (talking with Bjorn)".
- Hugin tells you, once, to give settlers a bench or a fire.
- Settlers sit, lie and gesture with the player's own animations, and every player sees them.
- Test tools: `vc_pose` (sit, lie, gestures on a settler) and `vc_lifetest` (the free-time checks in the game).

- **Long lists in pages.** A hut's What to grow, What to pick and replanting lists show 12 lines a page, with
  < Prev / Next >: the game's own 14 crops already took more than a page, and mods add more.
- **Tested next to other mods**: PlantEverything, PlantEasily, Seasonality, PlanBuild, EpicLoot and Monstrum (with
  Warfare): the whole self-test passes, no errors, no clashing patches. Found and fixed on the way: trees and bushes
  PlantEverything makes plantable count as plants the builder clears (they turned hut sites red and stood in doors);
  the builder mends a piece from where they stand when the way to it sticks within their reach.
- **Flint and branches** are on the forager's What to pick list, unticked to start: tick them on a hut to have its
  forager bring them home (your decision of 2026-10-15).
- **Two Farms side by side** split the ground between them: each plot belongs to the nearer Farm (a strip a plot wide
  between them stays empty), so two farmers never work the same plots; what one planted on ground a new Farm took is
  still theirs to pick.
- The save format is pinned in tests (every saved name, record, escape and the packed plan), so a later version can't
  change how a world reads without a test failing.
- The Thunderstore package (manifest, icon, store page) and the script that builds it.
- **Fixes:** the builder mends a damaged wall from the ground just outside the hut (they could fail to reach a wall
  piece itself and give up); a ripe crop the farmer can't get to is tried again after a minute instead of five; the
  ground in front of a door is cleared from half a metre inside the site, so no sliver is left (a wild raspberry bush
  there blocked a door).
- Test tools: a fresh test world made on the land of the first one, `vc_findflat` keeping clear of ruins and camps, and
  the scenario runner taking `--profile` (the test next to other mods); the self-test has 109 checks.
- A 20-minute soak test at 4x speed with every job at work: no warnings, no errors, 89 fps.

## 0.13.0 — Repair (unreleased)

Step 5 of the Road to 1.0.

- **Builders mend.** With nothing to build, a builder mends the colony's damaged buildings — any piece a builder put
  up that's below three quarters of its health, the nearest first — with a swing of their hammer, free of materials.
  Status "Mending the <hut>".
- **Broken pieces come back.** Every minute a finished colony building is checked against its design; pieces that
  are broken (a troll, a fire) reopen its order as a **repair**, and a builder fetches the materials and puts them
  back. The hut keeps working meanwhile. Not during a raid there or with monsters by the hut. A piece you take away
  with your hammer is left out, as are pieces already missing from before, and one that keeps falling after it's put
  back. The Work orders tab shows a repair as "repair", with **Leave as it is**. Setting
  `Builder.RebuildMissingPieces` (on). No upgrade while a repair runs.
- The huts' own chests are mended too.
- The self-test checks both.
- **Fix:** a settler waiting their turn at a door with walls on both sides (a gate in a fence) steps back from it,
  so the one coming the other way can step out; the two used to wait for each other until both gave up.

## 0.12.0 — The farmer (unreleased)

Step 4 of the Road to 1.0.

- **The Farm and its farmer.** A new hut (20 Wood) whose farmer works the plots round it as a player farms: they
  cultivate the ground with a copy of the Cultivator in its chest, plant the seeds in it, pick what's ripe and
  carry the harvest home. 12 plots at level 1, 8 more each level, out to 8 m (2 m more per level).
- **What to grow…** in the Farm's window: one tick per crop the game's Cultivator can plant, with its seed, greyed
  where it doesn't grow. They plant the ticked crops in turn, each while its seed is in the chest; with carrots and
  the carrot-seed crop both ticked the Farm keeps itself going. Mod-added crops show up too.
- They only harvest crops on their own plots: your garden elsewhere stays yours. Each plot gets a crop that grows on
  that spot; a seed crop is sown only while fewer than 6 of its seed are stored; a plant that can't grow is cleared and its plot rested.
- Settings `Farm.WoodCost`, `Farmer.FieldRadius`, `Farmer.RadiusPerLevel`, `Farmer.CarryCapacity`. Test tools
  `vc_ui grow`, `vc_ui crops`, `vc_build grow`. The self-test checks planting and harvesting.

## 0.11.0 — First steps (unreleased)

Step 3 of the Road to 1.0; from the project review of 2026-10 ("for a player who doesn't read the docs").

- **Hugin explains the colony as you play.** The game's raven lands by you the first time something comes up: your
  first Town Hall, a hut placed before any Builder's Hut, the builder's first needs, the first worker, the first
  night, the first hungry settler and the first death. Each tip once per character, with the game's tutorials on;
  `Messages.HuginTips` turns them off.
- **Words above heads.** A settler near you who needs you says so above their head — "Needs 12 Wood", "Storage full",
  "Can't reach the hut", "Hungry" — with the game's own NPC speech. Plain work shows nothing.
  `Messages.WordsAboveHeads` turns them off.
- **Getting Started** covers the whole first evening: Longhouses and newcomers, food, the Forager's Hut, the Town Hall's
  tabs.

## 0.10.0 — Steady and fast (unreleased)

Step 2 of the Road to 1.0; from the project review of 2026-10.

- **Bring back a lost settler.** The Town Hall keeps what it needs of every member (name, looks, job, home, skills).
  A settler marked *Missing* — their body lost — gets **Bring back** on the Citizens tab: they return to the Town Hall
  as themselves, empty-handed, unless their body is still somewhere in the world. The saga tells of it.
- **A quiet log.** Every settler's step is written only with the new setting `Developer.VerboseLog` (or in a test
  world); start-up, deaths, the saga, warnings and errors always are.
- **Start-up check.** The log names any game part a Valheim update renamed and what stops working without it.
- **Doorways.** A settler in a doorway gets their turn: the one waiting outside steps aside instead of standing on the
  spot the other needs, then giving up.
- **No more stutter from settlers.** Measuring showed Unity's search for every tree, log, plant, item and door in the
  loaded world took 20–60 ms each time, every couple of seconds. Settlers now read the game's own list of loaded
  objects instead (about 1 ms): in the test colony their share of each frame fell from 0.62 to 0.07 ms by day, and
  the game ran at 98 fps instead of 90. The forager and lumberjack also look closely only at plants and trees that
  can matter.
- Test tools: `vc_perf` by kind of work with the slowest update and garbage collections; scenarios `soak.txt` and
  `perf-quick.txt`. The self-test checks Bring back and the start-up check.

## 0.9.0 — Work orders (unreleased)

Step 1 of the Road to 1.0; from the project review of 2026-10 (Town Hall tabs, part 2).

- **Work orders tab** in the Town Hall window: every open build and upgrade order in the order builders take them,
  with the hut, what it becomes, pieces up and how it stands (who builds it, what it needs, waiting, set aside); the
  chosen order's full description; and at the top what all orders still need beyond the colony's chests.
- **Do first**: builders take that order before all others (their own hut too); one busy elsewhere comes over within
  seconds. **Set aside** / **Resume**: no builder takes the order until it's resumed; one on it leaves at once.
  **Cancel upgrade** as in the hut window. Only players who may manage the colony can use them.
- The hut window says when its order is set aside or marked to be done first.
- Test tools: `vc_ui tab orders`, `vc_ui order <hut> first|aside|resume|cancel`. The self-test checks Do first, Set
  aside, Resume and the tab.

## 0.8.1 — The forager's "What to pick" list (unreleased)

The player's idea (2026-10-15).

- **The forager picks herbs too**: everything that grows wild and grows back, not only food: dandelions and thistles
  join berries and mushrooms. Food feeds the colony; the rest waits in their chest for you. What never grows back
  (stones, wild seeds, the Mistlands mushrooms) is left, and so are flint and branches.
- **What to pick…** in the Forager's Hut's window: a tick per kind, with where it grows and whether it's food; each
  hut its own list, everything ticked to start. The hut's window says how many kinds they pick.
- Test tools: `vc_ui pick`, `vc_build pick [kind on|off]`, `vc_ui pickables`. The self-test checks they pick a
  dandelion and brings it home, and leaves the next one once it's unticked.

## 0.8.0 — Town Hall tabs, part 1 (unreleased)

From the project review of 2026-10.

- **The Town Hall window has tabs.** **Citizens**: the colony summary and every citizen, ten a page; click one to
  choose them and **Recall** brings them at once to a free spot by the Town Hall (stuck somewhere, or just far off).
  Only players who may manage the colony can recall, and the window tells how it went (back, back but gone
  indoors for the night, no free spot, not your colony).
- **Saga**: the colony's story, newest first: arrivals, deaths and their cause, huts built and levels gained, who
  started or left work where, recalls and settlers moved out when boxed in. The last 60 lines are kept with the Town
  Hall, and each player reads them in their own language.
- Test tools: `vc_ui tab`, `vc_ui choose`, `vc_ui recall`. The self-test checks the saga lines and Recall.
- Part 2 (next): a Work orders tab.

## 0.7.2 — No bronze gap (unreleased)

From the project review of 2026-10.

- **Level 2 no longer needs iron.** Its placeholders' four corner poles are now core-wood poles, so a colony reaches
  level 2 in the Black Forest age and its lumberjack and builder use bronze axes on the birches and oaks of the
  Meadows. Before, level 2 waited for iron and those trees stayed standing through the whole bronze age.
- A level now opens with the first era of its band (level 2: the Black Forest; level 3: the Mountains); saving your own
  design that uses nothing from it gives the gentle warning. Huts already standing keep their pieces.
- Test tool: `vc_ui recipe <piece> ...` shows what a building piece costs, where it's made and its size.

## 0.7.1 — Fixes from testing the checklist (unreleased)

The hands-on checklist was played through in a test world; these are the fixes it led to.

- **New Longhouse designs** (all five levels): bunk beds along both long walls, the chest against the back wall and a
  clear aisle down the middle. Every bed can now be walked to; in the old level-2 design none could. Longhouses already
  standing keep their design until they go up a level.
- A settler sleeps at the first of the four places around their bed that is free and that they can walk to from the door.
- **A settler already inside their Longhouse (or hut) at night never gives it up.** If someone stands on their sleeping
  place, they sleep where they are after a moment (never by the door, where they'd keep it open and stand in the next one's
  way). Seen: two settlers each standing on the other's place left their Longhouse for the Builder's Hut, and got boxed
  into a corner on the way. One who has just come in through the door doesn't give up on the hut either.
- **A settler boxed in for good is moved out.** One who keeps trying to walk somewhere for two minutes without getting
  out of a few metres of ground (seen: a builder between their hut's old and new walls) is moved to a free spot by the
  Town Hall, out of sight, when no player is within 30 m (after five minutes, whoever is near). Waiting on purpose (a
  lumberjack by their hut with a full chest) doesn't count, nobody is moved at night or in a raid, and nobody more than
  twice in ten minutes. Other colony mods do the same.
- **A settler whose way is cut off by a wall looks for a door near them at once**, instead of first walking to where
  that way ends. Seen: every morning a whole household, off to work behind the Longhouse's back wall, crowded into its
  back corner before thinking of the door.
- At a door, a settler or player walking through counts as a turn: settlers waiting behind a household going in no
  longer give up. A monster in the doorway is never waited for, and a whole passing has a time limit.
- **An upgrade is no longer refused as "can't stand in the air"** when the old design (one with posts, like a
  player's own) left the ground a little lower than the new design's floors: the builder raises it, as the upgrade
  planned all along (up to 2 m; more than that is refused as too high).
- A bed's sleeping place is always inside the Longhouse (a player's design could put one past the bed's head, beyond
  the wall); beds, tables and chests no longer count as walls when the game asks whether a settler is inside.
- The site line no longer asks for tools the colony's chests already hold (it said "needs an axe and a hoe" with both
  there).
- The self-test also checks that nothing stands in any hut's doorway and that every Longhouse bed can be reached (81 checks).
- Test tools: `vc_ui` (open and read windows, give and take items, spawn things at exact spots, list trees, pieces,
  sleepers and more), `vc_set`, `vc_remove`, `vc_kill`, `vc_settlers remove` and `forget`, `vc_build leftovers` (removes an old level's pieces a test
  shortcut left standing), `vc_build status` (shows the next level's ground check) and `scripts/vc.py` (drives a running
  game from the command line). `vc_build finish` on an upgrade now takes the old pieces away first: it had left the
  level-1 hut standing inside the level-2 one, and its builder was trapped between the walls.

## 0.7.0 — The forager (unreleased)

- **The Forager's Hut**, a new job hut (20 Wood, `Forager Hut.WoodCost`; needs the Town Hall nearby; five level
  designs). Its forager walks to wild berry bushes and mushrooms within 40 m of the hut (10 m more per level), picks
  them as a player does, gathers what drops and carries it home to the hut's chest (up to 20 at a time). The colony's
  settlers eat from it like any colony chest: the colony's first own food.
- They pick only wild food that grows back (raspberries, blueberries, mushrooms, cloudberries …): never your planted
  crops, anything a player placed or that grows on cultivated ground, nor food someone dropped by a bush (they gather
  only what fell from the bush they picked). Thistle, dandelions, flint and branches are left. Status: "Picking",
  "Nothing to pick"; hungry, they pick at half speed. At dusk they deliver what they carry, then go indoors like
  everyone.
- The README lists everything the mod does now. The self-test has 75 checks.
- Settings `Forager.WorkRadius` (40), `Forager.RadiusPerLevel` (10), `Forager.CarryCapacity` (20). The forager's work area
  shows as a ring while you place the hut and when you look at it, as the lumberjack's does.

## 0.6.0 — People and growth (unreleased)

Part 1: homes.

- **The Longhouse**, a new hut (20 Wood, `Longhouse.WoodCost`; needs the Town Hall nearby). The builder puts up its
  building like any hut's: a long hall with two beds at level 1 and two more each level (ten at level 5). Its window
  lists the beds and who sleeps in each. While it goes up a level its settlers wait for the new building's beds.
- **Every bed is a settler's home.** The colony shares its beds out by itself, nearest first; a settler keeps their bed
  while it stands. At night a settler with a home sleeps by their bed instead of in their workplace.
- Every hut, not only the ones with a job, joins the nearest colony and keeps it through the Town Hall's grace days.

Part 2: food.

- **Settlers eat once a day**, after dawn, wherever they are: one item of food from any of the colony's chests (the
  Town Hall's and the huts'), the plainest there is (raspberries before cooked meat), so your best food is left for
  you. Any food a player can eat counts; meads and potions don't. A new settler, and settlers from before this version, come fed: the first meal is due the
  next morning.
- **A settler who finds nothing to eat is Hungry** till their next meal: hovering over them says so, and they work at half
  speed (chop weaker, build slower). Nobody starves. Settings `Colony.SettlersEat` (on) and
  `Colony.HungryWorkSpeed` (0.5).

Part 3: newcomers.

- **The colony grows by itself.** Once a day, in daylight, while the colony has a free bed (more Longhouse beds than
  settlers) and at least 5 food in the Town Hall's chest, a newcomer arrives at the Town Hall and eats that food.
  Players in the colony see "Astrid has come to live in the colony". Settings `Colony.NewcomersPerDay` (1, 0 = none),
  `Colony.NewcomerFood` (5) and `Colony.MaxSettlers` (30).
- Test tools: `vc_beds` lists a Longhouse's beds and who sleeps in each; `vc_place` takes `~dx ~dz` (metres from you).

Part 4: the Town Hall window and fixes.

- **The Town Hall window shows the colony at a glance:** beds taken, food in the Town Hall's chest, how many are
  hungry, and what the next newcomer waits for ("needs 5 food in the Town Hall's chest", "needs a free bed", "tomorrow").
- Fix: a lumberjack fired with wood in their arms kept it for good; a settler let go with a load now takes it to the Town
  Hall's chest.
- Fix: a lumberjack's delivery at dusk used up their time to get indoors, and they gave up their own Longhouse for the night.
- The builder also clears what grows or lies in front of a hut's doors (bushes, small trees, stumps, rocks, logs; never
  items or anyone's building): a small beech in a doorway had kept settlers out. The site check counts them too.
- A settler out of sight (beyond the loaded area) keeps their bed; a Longhouse that has just loaded waits for its beds
  before sharing them out; a clock put back (a time skip, a day-length mod) no longer stops meals or newcomers.
- Version 0.6.0. The self-test has 71 checks. `vc_build finish` and `vc_build upgrade` first clear the site as the
  builder would (a small beech they left standing in a doorway had blocked the door).

## 0.5.0 — Survival (unreleased)

Part 1: indoors.

- **Settlers go indoors at night.** At dusk they stop work (the lumberjack delivers what they carry first), go into
  their hut through its door and shut it; their status says "Sleeping". At dawn they come out and carry on. Settlers
  without a job, or whose hut isn't finished or has no door, use the nearest finished colony hut. With no hut to go
  into, nights are as before. Setting `Colony.SettlersGoIndoorsAtNight` (on).
- **They shelter when attacked.** A settler a monster hunts runs into the nearest safe colony hut ("Sheltering") instead
  of standing outside its chest, and comes out 15 seconds after the last monster is gone. A monster that gets in after
  them makes them leave for another hut, or run. No hut: they flee as before.
- **They shelter during raids.** While one of Valheim's raids is on at the colony, every settler stays indoors until it
  ends. Setting `Colony.SettlersShelterInRaids` (on).
- Inside, settlers stand by the chest on its door side. A hut a settler can't get into within 45 seconds (its door
  blocked, say) is passed over for five minutes. The self-test has 54 checks; the command file also takes `event`,
  `randomevent` and `stopevent` (test worlds), no longer takes `killenemies` (it kills settlers too), and
  `vc_world clear` removes the monsters within 60 m and spares settlers.

Part 2: graves.

- **A settler who dies carrying anything leaves a cairn** where they fell, with what they carried. Hover over it: "Here lies
  Ingrid the Lumberjack: killed by Greydwarf, day 37". Open it like a chest; it can't be damaged and crumbles away once
  emptied. Players who got the death message get a map pin "Ingrid's cairn", which goes when they empty it. The
  self-test has 57 checks.

Part 3: the Town Hall.

- **Only players can damage the Town Hall chest.** Monsters, raids and stray rocks can't break it.
- **A colony outlives its Town Hall for a while.** If the Town Hall is lost anyway, settlers keep their colony and
  jobs and huts keep working for 3 in-game days (setting `Colony.TownHallGraceDays`, 0–10); the huts count the days
  down, the huts remember who owns the colony, and settlers still open its doors. A new Town Hall built among the huts
  or settlers in that time takes the colony over, with no newcomers. A grace day is the world's day. The self-test has
  61 checks.

Fixes and tools after part 1:

- **Settlers take turns at a door.** Two settlers going through the same door at once used to wedge each other in
  the doorway until one gave up; now one goes through while the others wait beside it, each on their own spot (settlers
  run by the same computer). Floor spots inside a hut keep clear of the way in and are shared out evenly, and a
  settler steps right onto their spot. A door with a stair or a bump in front is used from further out.
- **A settler waits for whoever stands in a doorway** (a player, or a settler run by another computer) up to 12
  seconds, and two settlers meeting from opposite sides let one through first.
- **The builder levels the ground in front of a hut's doors** (3 m out, 3 m wide) with the site, so a door facing
  uphill doesn't open onto a bank; the site check counts the stone for it.
- **Settlers get unstuck.** A settler whose walk doesn't move them (a log, a lip, someone in the way) hops after three
  seconds and steps aside after five (not off a ledge or into water), twice at most, as a player would.
- For testing: `vc_doors` (how settlers see the doors near you), `vc_walk` (send a settler in or out of a hut, or to a
  point) and `scripts/ingame.py`, which plays a scenario file in the game hands-free and checks the log.

## 0.4.0 — Hut levels (unreleased)

Builder round 2, parts 1a and 1b.

- **Hut levels 1–5**, following Valheim's eras: 1 Meadows; 2 Black Forest and Swamp; 3 Mountains and Plains;
  4 Mistlands; 5 Ashlands. Every hut built so far is level 1.
- **A design per level.** `vc_blueprint save <hut> <level>` saves your design for a level (`lumberjack_hut_3.blueprint`);
  plain longhouse placeholders ship for every level, built from their era's materials, so a level can only be built
  once you can supply them. Saving a design that uses nothing from its band's last era gives a gentle note.
- **Upgrades.** The hut's window shows its level and an "Upgrade to level N" button with what the upgrade needs, or why
  it is locked. The builder rebuilds the hut to the next level's design: pieces that are the same stay, old pieces
  are taken down from the top (their materials drop and they bring them back to their hut), new ones go up. The ground
  stays at the level the hut already has, so nothing is dug away under it. They change nothing before they have a new
  piece's materials, so an upgrade ordered without them waits with the hut untouched. The hut keeps working meanwhile
  and gets its new level when the upgrade is finished. An upgrade can be cancelled until they have changed the first
  piece.
- **The builder's level:** they build and upgrade huts up to their own Builder's Hut's level; their own hut can always go
  one level up, so upgrade the Builder's Hut first.
- **Existing worlds:** every hut built so far is level 1, so its builder now uses only a flint axe and an antler pickaxe
  (put one in a chest), and leaves birch, oak and harder rocks on their sites until their Builder's Hut is level 2 or 3 — they
  say which level (they work on other sites meanwhile). Bronze and iron tools work for them again from level 2.
- **A site that waits on you no longer holds the builder.** When a site waits on something only you can do — a tree or
  rock above their level, a tool, materials, something in the way — they work on another site after a minute (their own
  hut first) and come back after five minutes, once they are free or their other site waits on you too; with nothing else
  to do they stay and carry on as soon as you have helped. A better builder can take over a site they can't clear. Placing or upgrading a hut names what is above the
  colony's best Builder's Hut ("1 tree needs a level 2 Builder's Hut — or clear it yourself"), and their status says
  "Needs Builder's Hut level 2 for a tree — or clear it yourself".
- **What a level gives:** the lumberjack works 10 m further out per level and may use better axes (flint, iron, black
  metal, Jotun Bane, Berserkir); the builder may use better axes and pickaxes; every hut chest grows a row per level.
  Workers borrow a copy of the best allowed tool from the colony's chests. The hut's window shows the perks.
- **Replanting list:** tick which sapling kinds the lumberjack plants; they plant the ticked kinds in turn, each where it
  grows, so the forest becomes mixed. Kinds their level's axe can't fell, or that don't grow there, are greyed with why.
- Settings `Lumberjack.RadiusPerLevel` (10) and `Storage.RowsPerLevel` (1).
- More fixes: the builder names every kind of thing their site waits on (a missing axe no longer hides a birch that
  needs a level-2 Builder's Hut), and only a Builder's Hut with a builder counts as "a better builder"; a player who
  may not manage a colony can't place a hut in it (the outline is red: "Someone else's colony"); a chest left marked
  "in use" by a player who crashed no longer blocks settlers, and the builder waits at a chest a player has open
  instead of filling the log; a lumberjack's unfinished swing can't land after their job stopped.
- **Only the colony's owner and their ward friends manage it.** On a shared server anyone could fire your workers or
  order an upgrade that used up your iron. Now hiring, firing, auto-hire, the replanting list, building orders and
  upgrades are for the player who built the Town Hall and the players they put on their ward over the hut; others see
  the buttons greyed out with the reason, and a refused request (including a refused upgrade) tells the player why.
  **When you update a shared world:** Valheim remembers who built each Town Hall, so friends who share a colony need
  to be added to a ward (Guard stone) over its huts by the player who built the Town Hall.
- **Chests are safe in multiplayer.** A settler run by one computer used to take a hut chest over directly and could
  save an old copy over what a friend had just put in or taken out. Now they ask the computer that has the chest, which
  sends its latest contents and hands it over, as the game does when a player opens a chest, and the settler's
  computer reloads the chest before changing it.
- **A lumberjack's swing only hurts what they chop.** Chopping a stump, their axe used to land on the tree beside it,
  which could then fall any way, even onto a house, and their misses switched stand-in hits on for every lumberjack. Now a
  swing that lands on something else (a tree, a log, a rock, a wall) does it no harm and the stump gets the hit; only
  swings that hit nothing switch stand-in hits on, and only for that lumberjack.
- **Settlers survive errors in their work.** An error in a job used to repeat about 20 times a second, flooding the log
  and freezing the settler. Now they show "Dazed", pause (5 s, then longer if it happens again, up to a minute) and
  start their job over; the error is logged at most once a minute.
- **Settler deaths are reported.** When a settler dies, players in the colony's area or near the spot see
  "Ingrid the Lumberjack has died (hit by a falling tree)" in the middle of the screen and in the chat (setting
  `Messages.DeathMessage`: `CentreAndChat` or `ChatOnly`), and the log says who, where, which hut and why.
- **Safe from falling trees.** Settlers aren't hurt by falling trees and rolling logs (setting
  `Colony.SettlersSafeFromTrees`, on by default; players still are). The lumberjack and the builder also step back to
  the side after the felling blow and wait for the logs to settle before going on; the builder now fells a tree from
  behind it, opposite its fall.
- `vc_build upgrade` (test worlds) upgrades the hut you look at at once; the self-test checks levels, an upgrade
  and the perks, that a birch on one hut's site doesn't hold up another hut, and tree hits and a settler's death
  (49 checks). Also for test worlds: `vc_build window` opens the nearby hut's window, `vc_build stock` fills its chest
  for the next level, `vc_build give <item> [amount]` puts an item in it.
- Fixes: every hut window was titled "Lumberjack's Hut"; a settler without a job couldn't go through doors;
  `vc_blueprint save` worked without devcommands, so any player could make a hut count as built (it now needs
  devcommands and refuses a design with nothing built).
- Getting Started, the wiki home page, Troubleshooting and the README now cover the Builder's Hut and hut levels.
- In a world with passive enemies, settlers no longer flee from monsters that leave them alone; only from stirred-up ones.
- A test toolkit for test worlds: `vc_report`, `vc_goto`, `vc_place`, `vc_findflat`, `vc_speed`, `vc_quit`,
  `vc_world`, `vc_build order|cancel`, and a command file that runs these without the console (setting `Developer › CommandFile`,
  off by default). Developer settings can skip the intro film and start straight into a test world. The self-test turns
  on god mode and removes nearby monsters. See the Developer Guide.

## 0.3.0 — The Builder (unreleased)
- New **Builder's Hut** (20 Wood, setting `Builder Hut.WoodCost`): employs a builder who puts up the colony huts'
  buildings piece by piece from real hammer pieces — their own hut first, then the others in the order you placed them.
- Buildings come from **blueprints** in PlanBuild's format. Plain placeholder huts ship with the mod; build your own
  around a hut's chest and save it with `vc_blueprint save lumberjack_hut` (or `builder_hut`) to use it for new huts.
- They check every piece with the game's own support rule before placing it, place lowest first, watch each new
  piece for a few seconds, and skip a piece that falls three times (the hut window says so).
- They fetch materials from their hut, then the Town Hall, then the other huts' storage, and say what's missing.
- New huts only hire once their building stands (the Builder's Hut hires straight away). Huts from before this update
  keep working; their window has a **Build** button.
- The builder's pieces count as yours (comfort, raids, wards, hammer) and are safe from falling trees and rolling logs.
- Pieces of a design that would be more than 15 cm underground at the new spot (a hut designed on a slope, put up on
  flatter ground) are left out — the game would break them at once. The hut window counts them as "left out".
- Settlers' tool swings never damage buildings (the builder's hammer can touch the walls they stand next to).
- They stand just outside the building's real outline, within 8 m of the piece they work on (never beyond their reach),
  and stay put between pieces while they're close enough; their hammer swing no longer slides them forward.
- A building only counts as finished when every piece stands (pieces that can't exist at the spot don't count). When
  something stands where a piece goes, the builder waits and the hut window says what to remove; leftovers of an old
  colony building whose hut is gone they take down themselves.
  They only pick up wood where one of their own pieces fell.
- They never build into another building: a piece that would go into one of your own pieces (or another hut's) is left
  out and their status says "Something's in the way: …".
- Saving a design with `vc_blueprint save` around a hut makes that hut count as built (what you saved is its building).
- Test commands: `vc_build done`, `vc_build finish`, `vc_build clear` (removes the builder's pieces for that hut),
  `vc_build status`. `vc_selftest` now also tests building.
- Settings: `Builder.Reach` (15 m), `Builder.SecondsPerPiece` (2), `Builder.CarryCapacity` (50).
- **Placing a hut** shows its building as a see-through outline with a line under the crosshair: **green** (the builder
  can start right away), **yellow** (they clear or flatten the site first; the line says what, and which tools they need
  in the colony's chests — or clear it yourself), **red** (it can't go there: another building or hut site, outside the
  colony, someone else's ward, a no-build place, below the water line, too steep, or higher than its posts reach).
  Red huts can't be placed: clicking says why.
- **Page Up / Page Down** raise or lower a hut 25 cm at a time, up to 2 m (settings `Placing.RaiseKey`,
  `Placing.LowerKey`, each player's own). A raised hut stands on its design's posts; a design without posts can be
  lowered but not raised.
- **The builder prepares the site** before building: they pick up items, remove plants (picking what can be picked),
  fell trees away from the site, chop logs and stumps, break rocks and take down leftovers, then flatten the
  ground — 30 cm under the floor for designs with posts (the natural ground for a raised hut), or under the floors for
  designs without posts. What they gather goes to their hut.
- **Tools stay in your chests:** they work with a copy of the best axe, pickaxe and hoe in the colony's chests and
  fetch stone for raising low ground; when something is missing their status, the hut window and a message say what.
  The hoe levels the ground; a pickaxe is only needed for rocks and for ground more than half a metre too high.
- While building, they clear whatever grows or falls into the site; what they can't clear is named in the hut window but
  doesn't stop the building. Ground work stays clear of other buildings, and fires or workbenches near a site don't
  make it red.
- While you hold the hammer, unfinished buildings within 50 m show the pieces still to come as a light outline.
- Hut chests no longer need support: a raised hut floats on its spot, and digging its site doesn't break it.
- The builder's swings no longer hit anything themselves (no stray holes or knocked walls) and never slide them.
- `vc_build ready` skips clearing and flattening a site; `vc_build status` shows the stage and the ground level.
- **Settlers use doors:** a settler opens a closed door on their way when it is inside their colony's area and the colony's
  owner may open it (no key, no one else's ward), walks through, and closes it behind them once they are 2 m past (never on
  someone standing in the doorway). Doors they didn't open are left as they are.
- The placeholder Builder's and Lumberjack's Huts have a wooden door.
- The lumberjack (and the builder) fell trees away from unfinished building sites.
- The lumberjack finishes the logs and stump of a tree they felled even when they landed or rolled outside their work area.
- A chest behind a wall or a window isn't within reach from outside: settlers walk round, through the door if there is
  one, instead of reaching through the wall.

## 0.2.1 — Polish (unreleased)
- Settlers walk smoothly: they turn while walking, jog like a player normally does (they used to sprint) and no
  longer stop-start at the end of a walk. They sprint only when fleeing.
- Settlers flee to their Town Hall or hut from monsters that are close, alerted or hunting (not deer or a penned
  animal), never towards the monster, and carry on with their work when it's calm.
- The lumberjack stands at the trunk (measured where the axe lands) or beside a log on the uphill side, and only
  swings when their axe reaches; hits land in time with the swing.
- Lumberjacks have a **Woodcutting skill**: they start as strong as a new character and get better with practice,
  at a quarter of a player's pace per hit (setting `Lumberjack.LearningRate`; shown in the hut window).
- Two lumberjacks no longer go for the same tree: each claims their tree, the logs and stump where it fell, and the
  wood they fetch.
- A lumberjack who can't reach their hut (behind a door, on a platform) delivers from close by or says
  "Can't reach the hut".
- Names match bodies: women's names go with women's bodies, women never have beards, and settlers' looks are saved
  so everyone sees them.
- The red "!" over a settler shows only while it flees (it used to stay).
- Colony buildings aren't destroyed by falling trees or rolling logs, and the lumberjack fells trees away from them.
- Replanting needs a Cultivator in the Lumberjack's Hut (setting `Lumberjack.ReplantNeedsCultivator`); the hut window
  shows whether replanting is on.
- Settlers share one world scan, so more settlers cost less.
- The log shows the lumberjack's first ten axe swings in detail and a summary every fifty.
- `vc_selftest` removes its Town Hall and the settlers it spawned when it's done; `vc_selftest keep` leaves them.

## 0.2.0 — Town Hall and job huts (unreleased)
- The Colony Hall is now the **Town Hall**: it founds the colony and brings 4 starting citizens (setting
  `StartingCitizens`), and Shift+E lists every citizen with job, status and workplace.
- New **Lumberjack's Hut** (20 Wood, 18 slots): employs one lumberjack who works within 40 m of the hut and delivers
  to its storage. Shift+E: Hire, Fire, Auto-hire (on by default). Needs a Town Hall nearby instead of a workbench.
- Rings show the colony area (50 m, setting `ColonyRadius`) and the hut's work area while placing or hovering.
- Settlers and buildings far away or still loading are no longer mistaken for destroyed ones (a building must be
  missing for about 30 seconds).
- Citizens without a colony join any Town Hall within 50 m, not only a newly built one.
- Existing worlds convert automatically: the Colony Hall becomes the Town Hall; old lumberjacks become jobless
  citizens until a hut hires them.
- Removed setting `MaxLumberjacksPerHall` (each hut has one worker).
- New `vc_selftest` console command (with `devcommands`) for testing.

## 0.1.0 — First lumberjack
- Colony Hall, viking settlers with random looks and names, lumberjack that chops, collects, delivers and replants.
