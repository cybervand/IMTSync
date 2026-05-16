# IMT-MP - Multiplayer sync extension for Intersection Marking Tool

A [Cities: Skylines Multiplayer](https://github.com/CitiesSkylinesMultiplayer/CSM) sync extension that
synchronizes [Intersection Marking Tool](https://steamcommunity.com/sharedfiles/filedetails/?id=2140418403)
actions across all clients in a CSM session.

Without this extension, road markings drawn with IMT only exist on the local client and diverge across the
multiplayer session. With it, every IMT add/remove/drag action is broadcast and reapplied on remote clients.

## Status

Pre-alpha. Under active development. Not yet on Steam Workshop.

## Requirements

- Cities: Skylines 1.21+
- [Cities: Skylines Multiplayer (CSM)](https://steamcommunity.com/sharedfiles/filedetails/?id=1558438291)
- [Intersection Marking Tool (IMT)](https://steamcommunity.com/sharedfiles/filedetails/?id=2140418403)
- [Harmony 2 (CitiesHarmony)](https://steamcommunity.com/sharedfiles/filedetails/?id=2040656402)

## Install (development)

After building, copy the output DLL to:

```
M:\Games\Cities Skylines\Files\Mods\IMTSync\CSM.IMTSync.dll
```

## Build

```powershell
dotnet build CSM.IMTSync.csproj -c Release
```

Output goes to `bin\Release\net35\CSM.IMTSync.dll`. The post-build step copies it
to the Cities: Skylines mods folder above.

## Architecture

See `..\..\..\..\Users\Brewing Storm Lite\.claude\plans\mutable-chasing-falcon.md` for the full design plan.

In short: Harmony postfixes on `IMT.Manager.Marking.Add*` / `Remove*` / `Clear` capture local user
actions and broadcast them via `CSM.API.Commands.Command.SendToAll`. A handler on the receive side
applies them through `IMT.API.IDataProviderV1`, wrapped in `IgnoreHelper.StartIgnore()` to suppress
re-broadcast.

## License

GPL-3.0 (matches CSM upstream). See `LICENSE`.
