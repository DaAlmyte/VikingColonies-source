# VikingColonies: the code

NPC settlers for your own Viking village: found a Town Hall, give settlers homes and jobs, and they build, farm, chop, forage, repair and guard the colony on their own. MineColonies-style. Server and clients.

This is the code of **VikingColonies 1.4.1**, the version on [Thunderstore](https://thunderstore.io/c/valheim/p/DaAlmyte/VikingColonies/). It's refreshed with each release, without history.

- **To play it:** [Thunderstore](https://thunderstore.io/c/valheim/p/DaAlmyte/VikingColonies/), with r2modman or the Thunderstore app.
- **Bugs and ideas:** [the issues page](https://github.com/DaAlmyte/VikingColonies-issues), not here.
- **Chat:** [Discord](https://discord.gg/yCzDUPQA3D).

## Building

Needs the .NET SDK, Valheim and a mod manager profile with BepInEx and Jötunn. Then:

```
dotnet build VikingColonies.slnx -p:SkipDeploy=true -p:ValheimDir="<your Valheim folder>" -p:ProfileDir="<your mod manager profile>"
dotnet test tests/VikingColonies.Tests
```

The plugin is `src/VikingColonies/bin/Debug/VikingColonies.dll` (without `-p:SkipDeploy=true` a build also copies it
into the profile's `BepInEx/plugins`). The colony's rules, without the game, are in `src/VikingColonies.Core`, with their
tests in `tests/`.

## Making your own version

It's MIT-licensed (see LICENSE), so you may copy it and change it. Please:

- keep the LICENSE file and its copyright line in your version, and say it's based on VikingColonies;
- give it its own name and its own plugin ID (the GUID in the `[BepInPlugin]` line), so the two mods don't clash in one game;
- don't upload it as VikingColonies.

If what you're making would fit VikingColonies itself, say so on the issues page or the Discord.
