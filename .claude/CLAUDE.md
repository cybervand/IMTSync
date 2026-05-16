# IMT-MP — CSM sync extension for Intersection Marking Tool

## What this is

A **Cities: Skylines Multiplayer (CSM)** sync extension that broadcasts user actions performed
in **Intersection Marking Tool (IMT)** so they replay on every other client in the multiplayer
session. Without this extension, IMT actions only exist on the local client and diverge across
the session.

Same architectural pattern as the existing `CSM.MoveItSync`, `CSM.TmpeSync`, and
`CSM.ExtraLandscapingTools` extensions.

## Repo layout

```
M:\develop\IMT-MP\          working tree (git --separate-git-dir setup)
M:\git\IMT-MP\              the .git directory
├── CSM.IMTSync.csproj      net35 SDK-style project, dual-deploy post-build
├── README.md
├── LICENSE                 GPL-3 placeholder (matches CSM upstream)
├── .gitignore
├── Properties\AssemblyInfo.cs
├── Mod\
│   ├── ModMetadata.cs      version constants, Harmony ID
│   ├── MyUserMod.cs        IUserMod entry; creates LogOverlay on enable
│   └── IMTSyncSupport.cs   CSM.API.Connection subclass; auto-discovered by CSM
├── Patcher.cs              Harmony PatchAll/UnpatchAll; guards against IMT-not-loaded
├── Injections\             Harmony patch classes (CSM convention dir name)
│   └── MarkingMutationPatches.cs  postfixes on IMT.Manager.Marking.{Add,Remove,Clear,Reset}*
├── Commands\               protobuf message types
│   ├── PointRef.cs         6-byte wire identifier for any MarkingPoint
│   ├── IMTActionType.cs    enum discriminator (proto values FROZEN — only append)
│   └── IMTActionCommand.cs polymorphic CSM command, fields keyed by ProtoMember tags
├── Handlers\
│   └── IMTActionHandler.cs CommandHandler<IMTActionCommand>; routes by Type
├── Services\
│   ├── Log.cs              file logger (Cities_Data\Logs\CSM.IMTSync.log) + ring buffer
│   ├── LogOverlay.cs       in-game OnGUI overlay, toggle Ctrl+Shift+L
│   ├── CsmBridge.cs        Command.SendToAll wrapper + IDisposable IgnoreScope
│   ├── PointResolver.cs    PointRef → IEntrancePointData (API) or MarkingEnterPoint (internal)
│   └── StyleSerializer.cs  IMT Style ↔ XML round-trip (Mono-2.x-safe parser)
└── docs\                   (Phase 3+ design docs land here)
```

## Build & deploy

```powershell
cd M:\develop\IMT-MP
dotnet build CSM.IMTSync.csproj -c Release
```

Post-build automatically copies `bin\Release\CSM.IMTSync.dll` to **both** local CS installs:
- `M:\Games\Cities Skylines\Files\Mods\IMTSync\` (host install)
- `M:\Games\C_S2\Cities Skylines\Files\Mods\IMTSync\` (client install for two-PC tests)

Restart CS after each build to pick up the new DLL.

## Critical runtime constraint — CS uses Mono 2.x with .NET 3.5 profile

The compile-time reference assemblies define many .NET 4.0+ APIs that the actual Mono 2.x
runtime does NOT have. Code compiles but throws `MissingMethodException` at runtime.

**Already-discovered traps (do NOT use):**

| Forbidden API | Workaround |
|---|---|
| `XElement.Parse(string)` | `XElement.Load(XmlReader.Create(new StringReader(xml), new XmlReaderSettings()))` — see `Services\StyleSerializer.cs` |
| `Path.Combine(a, b, c)` 3-arg | nest 2-arg calls: `Path.Combine(Path.Combine(a, b), c)` |
| `String.IsNullOrWhiteSpace` | `String.IsNullOrEmpty` (manually trim if needed) |
| `Tuple<>` (4.0+) | custom struct or `KeyValuePair<>` |
| `IReadOnlyList<>`, `IReadOnlyCollection<>` (4.5) | `IList<>`, `ICollection<>` |
| `HttpClient` (4.5) | `WebClient` / `HttpWebRequest` |
| `async`/`await`/`Task` | IEnumerator coroutines via Unity StartCoroutine |

**Anything new from `System.Xml.Linq`, `System.Net.Http`, or APIs added in 4.0+ should be
runtime-tested before committing.** The C# compiler will not warn you.

## How to add a new patched action (extension recipe)

1. **Find the IMT method to patch.** User-facing actions usually live in `IMT.Manager.Marking`
   (line/filler/crosswalk add/remove) or `IMT.Tools.*` (mode-specific UI behavior). Cecil-trace
   from a tool mode's `OnPrimaryMouseClicked` / `OnDelete` to find the exact `Marking::...` call.
2. **Watch for ambiguous overloads.** `RemoveLine` and `AddFiller` both have multiple. Disambiguate
   via `[HarmonyPatch(typeof(T), nameof(...), new System.Type[]{ ... })]` OR via `static
   MethodBase TargetMethod()` returning `AccessTools.Method(...)` (required for `out`/`ref`
   params via `MakeByRefType()`).
3. **Add a new value to `Commands/IMTActionType.cs`.** APPEND ONLY — never renumber existing
   entries; the proto wire format depends on the numeric value.
4. **Extend `Commands/IMTActionCommand.cs` if you need new fields.** APPEND new `[ProtoMember(N)]`
   tags only. Existing tag numbers are frozen.
5. **Write the patch postfix in `Injections/MarkingMutationPatches.cs`** following the established
   template (see `Marking_AddRegularLine_Patch`):
   - Short-circuit if `CsmBridge.IsIgnoring()` (suppresses re-broadcast on remote-applied actions)
   - Build `IMTActionCommand` with all required fields
   - Call `CsmBridge.SendToAll(cmd)`
6. **Write the receive case in `Handlers/IMTActionHandler.cs`** in the central switch:
   - The handler already wraps everything in `CsmBridge.StartIgnore()` — your patch's
     short-circuit will fire correctly
   - Use `PointResolver.TryResolveInternalEnterPoint(marking, ref, out point)` for points
   - Use `StyleSerializer.TryFromXml<TStyle>(xml, out style)` for styles
   - Use `SingletonManager<NodeMarkingManager>.Instance.GetOrCreateMarking(id)` (or Segment) to
     find the marking
7. **Build clean (zero warnings).** Restart both CS instances. Test in two-PC CSM session.

## Cecil inspection (always use this before guessing IMT internals)

```powershell
$tmp = "$env:TEMP\imt-sync-analysis"
if (-not (Test-Path "$tmp\Mono.Cecil.dll")) {
  Copy-Item "M:\Games\Cities Skylines\Files\Mods\2881031511\Mono.Cecil.dll" "$tmp\Mono.Cecil.dll"
  Unblock-File "$tmp\Mono.Cecil.dll"
}
Add-Type -Path "$tmp\Mono.Cecil.dll"
$module = [Mono.Cecil.ModuleDefinition]::ReadModule("M:\Games\Cities Skylines\Files\Mods\2140418403\IntersectionMarkingTool.dll")
# enumerate types/methods/fields as needed
```

The IMT mod folder is `Files\Mods\2140418403\` (offline copy) and
`D:\SteamLibrary\steamapps\workshop\content\255710\2140418403\` (Steam workshop). Same DLL.

## Testing protocol

1. Build (`dotnet build` — auto-deploys to both installs)
2. Restart **both** CS instances (host: `M:\Games\Cities Skylines`, client: `M:\Games\C_S2\Cities Skylines`)
3. Both: enable CSM, IMT, and IMTSync in Content Manager → Mods (if not already enabled)
4. Host launches CSM session; client joins via LAN
5. Both should see "IMTSync: Supported" on connection screen
6. Perform action on one side; check other side's overlay (Ctrl+Shift+L) for `RECV` + `Applied` lines
7. Check visual change appears on remote

Logs at:
- `M:\Games\Cities Skylines\Cities_Data\Logs\CSM.IMTSync.log` (host)
- `M:\Games\C_S2\Cities Skylines\Cities_Data\Logs\CSM.IMTSync.log` (client)

Logs **truncate per session** (file is wiped on first write each launch).

## Plan file

The full implementation plan lives at
`C:\Users\Brewing Storm Lite\.claude\plans\mutable-chasing-falcon.md` (approved 2026-05-16).
Read it for the phase ordering, design rationale, and known risks.

## Phase status (commits)

| Commit | Phase | What landed |
|---|---|---|
| `63e61e4` | Phase 0+1 | Repo scaffold, Marking.Clear sync — networking proven on two PCs (84ms latency) |
| `0ffbca7` | Phase 2.2 | AddRegularLine sync — proved style XML round-trip + point resolution end-to-end |
| `5696976` | Phase 2.4+2.5 | Full Add/Remove surface (NormalLine, StopLine, LaneLine, CrosswalkLine, Filler), ResetOffsets, in-game log overlay |

## What's pending (in priority order)

1. **In-game test of Phase 2.4+2.5** (Filler, Stop, Crosswalk, Delete, Reset) — built clean but not all exercised yet
2. **Style-edit sync** — change a line's color/width/dash propagates. Likely patch target:
   `Marking.Update(MarkingLine, bool, bool)` postfix (need to filter the per-frame render-driven
   calls vs user-edit calls). Could also patch `IRegularLineData.AddRule` if rules need syncing.
3. **SetPointOffset** — patch `MarkingEnterPoint.Offset` setter (or `DragPointToolMode.OnMouseUp`
   for the commit case). Avoid patching `BasePropertyValue<float>.set_Value` directly — too broad.
4. **Phase 3: Presence + Live preview** — show other players' tool mode + selection ghost +
   rubber-band line + live drag preview. Substantial new infrastructure (throttle, ghost renderer).
5. **Phase 4: Paste/Preset whole-marking sync** — `ApplyMarkingXmlCommand` for `PasteMarkingToolMode`
   and `ApplyPresetToolMode`. Receiver calls `Marking.FromXml(version, xml, new ObjectsMap(), true)`.
6. **Mid-session join snapshot** — `Connection.OnClientConnect(Player)` hook to ship the host's
   `MarkingManager.ToXml()` to the joining client.

## Operational gotchas worth remembering

- **Background agents in this user's environment can't run shell.** Don't delegate Cecil
  research or `dotnet build` to BG agents — they'll stall asking for permission. Do shell-
  requiring work in foreground or send the agent the pre-extracted info.
- **Each new mod release of IMT may rename internal types.** When IMT updates, re-Cecil-check
  signatures before assuming patches still apply.
- **CSM updates** can change CSM.API. The `Connection` contract has been stable but watch
  release notes.

## Two-CS-install layout (this user's specific setup)

| | Host | Client |
|---|---|---|
| CS install path | `M:\Games\Cities Skylines\` | `M:\Games\C_S2\Cities Skylines\` |
| Mods folder | `Files\Mods\` | `Files\Mods\` |
| Log folder | `Cities_Data\Logs\` | `Cities_Data\Logs\` |
| CSM connection config | `client-config.json` (HostAddress, port, username) | same |
| User multiplayer name | `dune` | (set per-instance in client-config) |
