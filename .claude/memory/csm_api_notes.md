---
name: csm-api-notes
description: CSM (Cities Skylines Multiplayer) extension contract — Connection class, send/receive APIs, IgnoreHelper semantics
metadata:
  type: reference
---

# CSM API notes

CSM = Cities Skylines Multiplayer, workshop ID `1558438291`. We extend it via the
`CSM.API.dll` contract. All findings verified via Mono.Cecil — see [[cecil_pattern]].

## Connection class (subclass `CSM.API.Connection`)

- 4 properties: `Name (string)`, `Enabled (bool)`, `ModClass (Type)`, `CommandAssemblies` (a `List` of `Assembly`)
- 2 abstract-ish methods: `RegisterHandlers()`, `UnregisterHandlers()`
- Discovered automatically by CSM via `PluginManager` scan when CSM loads. CSM shows the mod as "Supported" on the host/join screen.
- Lifecycle: `RegisterHandlers()` is called when the **map loads with the mod enabled**. `UnregisterHandlers()` on map unload.
- Our impl: `Mod\IMTSyncSupport.cs` — calls `Patcher.PatchAll()` / `Patcher.UnpatchAll()`

## Sending commands (`CSM.API.Commands.Command`)

- Static `Action<CommandBase>` fields: `SendToAll`, `SendToServer`, `SendToClients` — these are `null` until CSM has connected to the network
- Static `Func<Type, CommandHandler>` field: `GetCommandHandler`
- CSM populates these via `Command.ConnectToCSM(sendToAll, sendToServer, sendToClients, getHandler)` at runtime
- Our wrapper: `Services\CsmBridge.SendToAll(CommandBase)` — null-safe, logs warning if not connected yet

## Receiving (`CSM.API.Commands.CommandHandler<T>`)

- Subclass with concrete `T : CommandBase`
- Override `protected override void Handle(T command)`
- CSM finds your handler by scanning the assemblies listed in `Connection.CommandAssemblies`
- Properties on base: `RelayOnServer (bool)`, `TransactionCmd (bool)` — for advanced use
- **NO `OnClientConnect(Player)` hook on public `Connection`.** Verified via Cecil 2026-05-16:
  only `RegisterHandlers` and `UnregisterHandlers` are virtual. Mid-session snapshot sync must
  piggyback on the first `RegisterHandlers` call after a connection event (or use `/sync` —
  CSM's chat command that retransmits the host's full savegame).

## Commands (`CSM.API.Commands.CommandBase`)

- `int SenderId` — populated by CSM on receive; identifies the sending player. `-1` means server.
- Subclass with `[ProtoContract]` and add fields with `[ProtoMember(N)]` (protobuf-net is bundled with CSM at workshop `1558438291\protobuf-net.dll`)

## `IgnoreHelper` (`CSM.API.Helpers.IgnoreHelper`)

- `Instance` is a per-thread `ThreadLocal` of `IgnoreHelper`
- `StartIgnore() / EndIgnore()` are ref-counted via internal `_ignoreAll` int
- `IsIgnored()` — true while inside any StartIgnore scope on this thread
- Used to suppress re-broadcast: when applying a remote command, wrap in `using (CsmBridge.StartIgnore())` so our Harmony postfixes see `IsIgnoring()` and short-circuit
- Our wrapper: `Services\CsmBridge.StartIgnore()` returns `IDisposable` for clean using-statement use

## Player attribution

- `CSM.API.Networking.Player` exposes `Username (string)`, `Latency (long)`, `Status (ClientStatus)` — read-only.
- Local player's name: `CSM.API.Chat.Instance.GetCurrentUsername()` (public `IChat.GetCurrentUsername()`).
- Sender ID on incoming command: `cmd.SenderId` (int). `-1` means server/host.
- Chat output: `CSM.API.Chat.Instance.PrintGameMessage(msg)` (game-style banner) or `PrintChatMessage(username, msg)` (chat with sender attribution).

## CSM cursor rendering (in `CSM.BaseGame.dll`, NOT `CSM.API.dll`)

- `CSM.BaseGame.Injections.Tools.ToolSimulatorCursorManager` — public `ColossalFramework.Singleton`. Public methods:
  - `PlayerCursorManager GetCursorView(int senderId)` — returns/creates PCM, cached in dict
  - `void RemoveCursorView(int senderId)`
  - `Vector3 DoRaycast(Ray mouseRay, float mouseRayLenght)` — yes, "Lenght" sic (parameter name)
- `CSM.BaseGame.Injections.Tools.PlayerCursorManager` — `MonoBehaviour` per sender:
  - `void SetCursor(CursorInfo)` — pass `null` to fall back to `DefaultTool.m_cursor`. Flips `_cursorImage.isVisible=true`.
  - `void SetLabelContent(string name, Vector3 worldPos)` — sets label + position; flips `_playerNameLabel.isVisible` based on name nullity.
  - `Update()` runs per frame; does `WorldToScreenPoint` from `_cursorWorldPosition` to position the sprite and label.
- **Pattern for our cursor presence:** broadcast `(senderId, worldPos, name)`; on receive call `GetCursorView(senderId)` + `SetCursor(null)` + `SetLabelContent(name, pos)`. CSM's PCM.Update renders the cursor sprite + name label exactly like for vanilla CS tools.
- **PCM.Start initializes the sprite invisible** — `SetCursor` is required to make it visible.

## CSM tool commands (`CSM.BaseGame.Injections.Tools`)

- `ToolCommandBase : CommandBase` — base for tool-state commands. Carries `PlayerName (string)`, `CursorWorldPosition (Vector3)`.
- Concrete subclasses per CS tool: `PlayerDefaultToolCommand`, `PlayerNetToolCommand`, `PlayerBuildingToolCommand`, `PlayerPropToolCommand`, `PlayerTreeToolCommand`, `PlayerTerrainToolCommand`, `PlayerTransportToolCommand`, `PlayerZoneToolCommand`, `PlayerDistrictToolCommand`.
- **Each has a dedicated handler** (a generic `BaseToolCommandHandler` parameterized on `TCmd, TTool`). Generic subclassing-and-let-it-route doesn't work — CSM dispatches per concrete type, not via base class. **We don't piggyback on this; we send our own `CursorPresence` command and call `ToolSimulatorCursorManager` directly.**

## Wire format protocol

- Don't ship our own protobuf-net (CSM ships it; reference theirs with `Private=False`)
- ProtoMember tags are part of the wire format — ONLY APPEND new tags, never renumber
- Same for enum values (e.g. `IMTActionType`)

## Bundling contract

`CSM.API.dll` must be bundled with the extension OR referenced via NuGet package `CitiesSkylinesMultiplayer.API`. We reference the workshop copy locally and don't ship it.

## Reference extensions (templates)

- `CSM.MoveItSync.dll` (workshop ID `3693412367`) — closest analogue, has live preview pattern (`MoveItPreviewCommand`) we'll mirror in Phase 3
- `CSM.TmpeSync.*.dll` (workshop ID `3600743038`, 12 sub-DLLs) — pattern for splitting a large mod into per-feature sync DLLs
- `CSM.ExtraLandscapingTools.dll` (workshop ID `3700356519`) — single-file mid-size example
