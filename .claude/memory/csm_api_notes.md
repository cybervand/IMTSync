---
name: csm-api-notes
description: CSM (Cities Skylines Multiplayer) extension contract — Connection class, send/receive APIs, IgnoreHelper semantics
metadata:
  type: reference
---

CSM = Cities Skylines Multiplayer, workshop ID `1558438291`. We extend it via the
`CSM.API.dll` contract. All findings verified via Mono.Cecil — see [[cecil_pattern]].

**Connection class (subclass `CSM.API.Connection`):**
- 4 properties: `Name (string)`, `Enabled (bool)`, `ModClass (Type)`, `CommandAssemblies (List<Assembly>)`
- 2 abstract-ish methods: `RegisterHandlers()`, `UnregisterHandlers()`
- Discovered automatically by CSM via `PluginManager` scan when CSM loads. CSM shows the mod as "Supported" on the host/join screen.
- Lifecycle: `RegisterHandlers()` is called when the **map loads with the mod enabled**. `UnregisterHandlers()` on map unload.
- Our impl: `Mod\IMTSyncSupport.cs` — calls `Patcher.PatchAll()` / `Patcher.UnpatchAll()`

**Sending commands (`CSM.API.Commands.Command`):**
- `static Action<CommandBase> SendToAll`, `SendToServer`, `SendToClients` — these are `null` until CSM has connected to the network
- `static Func<Type, CommandHandler> GetCommandHandler`
- CSM populates these via `Command.ConnectToCSM(sendToAll, sendToServer, sendToClients, getHandler)` at runtime
- Our wrapper: `Services\CsmBridge.SendToAll(CommandBase)` — null-safe, logs warning if not connected yet

**Receiving (`CSM.API.Commands.CommandHandler<T>`):**
- Subclass with concrete `T : CommandBase`
- Override `protected override void Handle(T command)`
- CSM finds your handler by scanning the assemblies listed in `Connection.CommandAssemblies`
- Properties on base: `RelayOnServer (bool)`, `TransactionCmd (bool)` — for advanced use
- Lifecycle hooks: `OnClientConnect(Player)`, `OnClientDisconnect(Player)` — useful for snapshot sync (Phase 4+)

**Commands (`CSM.API.Commands.CommandBase`):**
- `int SenderId` — populated by CSM on receive; identifies the sending player. `-1` means server.
- Subclass with `[ProtoContract]` and add fields with `[ProtoMember(N)]` (protobuf-net is bundled with CSM at workshop `1558438291\protobuf-net.dll`)

**`IgnoreHelper` (`CSM.API.Helpers.IgnoreHelper`):**
- `Instance` is `ThreadLocal<IgnoreHelper>` — per-thread state
- `StartIgnore() / EndIgnore()` are ref-counted via internal `_ignoreAll` int
- `IsIgnored()` — true while inside any StartIgnore scope on this thread
- Used to suppress re-broadcast: when applying a remote command, wrap in `using (CsmBridge.StartIgnore())` so our Harmony postfixes see `IsIgnoring()` and short-circuit
- Our wrapper: `Services\CsmBridge.StartIgnore()` returns `IDisposable` for clean using-statement use

**Player attribution:** for Phase 3 presence work — the local player ID and senderID-to-name mapping live somewhere in `CSM.API.Networking.Player` and `CSM.API.MultiplayerManager`. Cecil-check before designing.

**Wire format protocol:**
- Don't ship our own protobuf-net (CSM ships it; reference theirs with `<Private>False</Private>`)
- ProtoMember tags are part of the wire format — ONLY APPEND new tags, never renumber
- Same for enum values (e.g. `IMTActionType`)

**Bundling contract:** CSM.API.dll must be bundled with the extension OR referenced via NuGet package `CitiesSkylinesMultiplayer.API`. We reference the workshop copy locally and don't ship it.

**Reference extensions (templates):**
- `CSM.MoveItSync.dll` (workshop ID `3693412367`) — closest analogue, has live preview pattern (`MoveItPreviewCommand`) we'll mirror in Phase 3
- `CSM.TmpeSync.*.dll` (workshop ID `3600743038`, 12 sub-DLLs) — pattern for splitting a large mod into per-feature sync DLLs
- `CSM.ExtraLandscapingTools.dll` (workshop ID `3700356519`) — single-file mid-size example
