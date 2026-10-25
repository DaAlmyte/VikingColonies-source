# VikingColonies: the code

NPC settlers for your own Viking village: found a Town Hall, give settlers homes and jobs, and they build, farm, chop, forage, repair and guard the colony on their own. MineColonies-style. Server and clients.

This is the code of **VikingColonies 1.5.0**, the version on [Thunderstore](https://thunderstore.io/c/valheim/p/DaAlmyte/VikingColonies/). It's refreshed with each release, without history.

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

The code has its own licence (see LICENSE). You may read it, build it and change it for your own use, share
the mod unchanged, and make add-ons and compatibility patches that work alongside VikingColonies. Publishing this
code or a changed copy of it needs DaAlmyte's OK first: ask on the issues page or the Discord. Versions up to 1.4.1
were MIT.

If what you're making would fit VikingColonies itself, say so on the issues page or the Discord.
