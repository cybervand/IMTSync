# IMT-MP - Multiplayer sync extension for Intersection Marking Tool

A [Cities: Skylines Multiplayer](https://github.com/CitiesSkylinesMultiplayer/CSM) sync extension that
synchronizes [Intersection Marking Tool](https://steamcommunity.com/sharedfiles/filedetails/?id=2140418403)(https://github.com/MacSergey/NodeMarkup)
actions across all clients in a CSM session.

I still need bug testing, the mod works for almost all functions but there most likely a few I haven't tried. but templates and presets seamlessly sync along with lines and fillers. Modded decals also work.

## Status

 Under active development. Not yet on Steam Workshop.

## Requirements

- Cities: Skylines 1.21+
- [Cities: Skylines Multiplayer (CSM)](https://steamcommunity.com/sharedfiles/filedetails/?id=1558438291)
- [Intersection Marking Tool (IMT)](https://steamcommunity.com/sharedfiles/filedetails/?id=2140418403)
- [Harmony 2 (CitiesHarmony)](https://steamcommunity.com/sharedfiles/filedetails/?id=2040656402)

## Install (development)

After building, copy the output DLL to:

```
Mods\IMTSync\CSM.IMTSync.dll
```

## Build

```powershell
dotnet build CSM.IMTSync.csproj -c Release
```

Output goes to `bin\Release\net35\CSM.IMTSync.dll`. The post-build step copies it
to the Cities: Skylines mods folder above.

## Architecture

In short: Harmony postfixes on `IMT.Manager.Marking.Add*` / `Remove*` / `Clear` capture local user
actions and broadcast them via `CSM.API.Commands.Command.SendToAll`. A handler on the receive side
applies them through `IMT.API.IDataProviderV1`, wrapped in `IgnoreHelper.StartIgnore()` to suppress
re-broadcast.

## License

GPL-3.0 (matches CSM upstream). See `LICENSE`.
