# IMT-MP — CSM sync extension for Intersection Marking Tool

## What this is

A **Cities: Skylines Multiplayer (CSM)** sync extension that broadcasts user actions performed
in **Intersection Marking Tool (IMT)** so they replay on every other client in the multiplayer
session. Without this extension, IMT actions only exist on the local client and diverge across
the session.

Same architectural pattern as the existing `CSM.MoveItSync`, `CSM.TmpeSync`, and
`CSM.ExtraLandscapingTools` extensions.

## Repo layout

```text
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
│   ├── Log.cs                     file logger (Cities_Data\Logs\CSM.IMTSync.log) + ring buffer
│   ├── LogOverlay.cs              in-game OnGUI overlay, toggle Ctrl+Shift+L
│   ├── CsmBridge.cs               Command.SendToAll wrapper + IDisposable IgnoreScope
│   ├── PointResolver.cs           PointRef → IEntrancePointData (API) or MarkingEnterPoint (internal)
│   ├── StyleSerializer.cs         IMT Style ↔ XML round-trip (Mono-2.x-safe parser)
│   ├── FillerVertexConverter.cs   IFillerVertex ↔ FillerVertexRef[] (Enter/LineEnd/Intersect)
│   ├── EditClock.cs               Tier-2 LWW versioning + tombstones + Lamport clock
│   └── PresenceStore.cs           Tier-1 per-sender claim dict, world-pos lookup via NetManager
└── docs\
    ├── TODO.md                    unsynced-action backlog with source-confirmed patch targets
    └── IMT-INTERNALS.md           consolidated IMT architecture reference (6-agent survey)
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
| --- | --- |
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

## IMT source code — local mirror

A copy of [MacSergey/NodeMarkup](https://github.com/MacSergey/NodeMarkup) lives at:

```text
M:\develop\IMT-MP\NodeMarkup-master\NodeMarkup-master\
├── IMT\              the main mod source (Manager/, MarkingItems/, Tools/, UI/, Utilities/)
├── IMT.API\          public API source (Interfaces/, Helper.cs, Enums.cs, Exceptions.cs)
├── ModsCommon\       MacSergey's shared framework (PropertyValue, BaseTool, OverlayData, etc.)
└── IntersectionMarkingTool.sln
```

**Gitignored** — don't commit. **Prefer Read/Grep on this over WebFetch** when investigating
IMT internals. The repo is current as of MacSergey's latest master.

See [docs/IMT-INTERNALS.md](../docs/IMT-INTERNALS.md) for the consolidated catalog from the
6-agent survey of this codebase.

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
| --- | --- | --- |
| `63e61e4` | Phase 0+1 | Repo scaffold, Marking.Clear sync — networking proven on two PCs (84ms latency) |
| `0ffbca7` | Phase 2.2 | AddRegularLine sync — proved style XML round-trip + point resolution end-to-end |
| `5696976` | Phase 2.4+2.5 | Full Add/Remove surface (NormalLine, StopLine, LaneLine, CrosswalkLine, Filler), ResetOffsets, in-game log overlay |
| `cf6def5` | .claude scaffold | `.claude/CLAUDE.md` + `.claude/memory/` for next session |
| `daff3f2` | Phase 2.6 + Tier 1/2 | Style edit sync (Line/Filler/Crosswalk via StyleChanged taps), SetPointOffset, Filler v2 (Enter/LineEnd/Intersect), Tier 2 LWW versioning with tombstones, Tier 1 presence (SelectIntersection chat + claim rings + entrance-point dots), CursorPresence via CSM PCM, docs/IMT-INTERNALS.md + revised docs/TODO.md |

## What's pending

The full unsynced-action backlog lives in [`docs/TODO.md`](../docs/TODO.md) — now source-confirmed
exact patch targets from the 6-agent IMT survey. The consolidated IMT architecture reference is
[`docs/IMT-INTERNALS.md`](../docs/IMT-INTERNALS.md).

Top-of-list:

1. **Line style change (multi-rule lines)** — current `MarkingLineRawRule.StyleChanged` patch only
   applies to `Rules[0]`. Wire format needs a rule index/edge identity.
2. **Point split toggle** — `PointsEditor.SplitChanged(bool)` (private). Drop-in.
3. **Crosswalk borders + CutLines** — `CrosswalksEditor.RightBorgerChanged/LeftBorgerChanged/CutLines()`
   (typos intentional upstream).
4. **Full Crosswalk add/remove** — `Marking.AddCrosswalk(MarkingCrosswalk)` (the wide painted shape,
   not just `AddCrosswalkLine`).
5. **Bulk style applies** — `*.PasteStyle()/ResetStyle()/ApplyStyleSameStyle()/ApplyStyleSameType()`
   across Filler/Crosswalks/RulePanel editors.
6. **Phase 4 — Paste / Preset / Template** — patch `IntersectionTemplateEditor.Apply/ApplyAll/Link`
   or the Tool-layer equivalents; wire format = copy `MoveItIntegration` shape exactly
   (`Marking.ToXml() ↔ FromXml(Version, XElement, ObjectsMap)`).
7. **Phase 3 — Live drag previews** — patch `MakeLineToolMode.OnToolUpdate`,
   `MakeFillerToolMode.OnPrimaryMouseClicked`, `DragPointToolMode.OnMouseDrag` (throttled 10 Hz).
8. **Mid-session join snapshot** — design is now precise: iterate
   `SingletonManager<NodeMarkingManager>.Instance` (it's `IEnumerable`), `ToXml()` each, broadcast.
   Receiver applies via `FromXml(Version, xml, new ObjectsMap(), needUpdate:true)` —
   `VersionMigration` auto-handles cross-version.

## Known bugs / mitigations

- **Bug 2 (open):** Fillers anchored to **pre-existing** lines from prior sessions fail on the
  receiver — the host's saved-state lines aren't in the client's marking. **Workaround:** type
  `/sync` on the client to pull host's full savegame. **Proper fix:** mid-session state push
  (item 8 in pending list above).

## Operational gotchas worth remembering

- **Background agents can do WebFetch / Read / Grep / Glob but NOT shell.** The earlier rule
  "BG agents can't do anything" was too broad. We successfully ran 6 parallel agents that
  WebFetched GitHub raw files and returned structured surveys (see [docs/IMT-INTERNALS.md](../docs/IMT-INTERNALS.md)).
  Agents CAN do source-level investigation via GitHub raw URLs, code analysis, file reviews.
  Agents CAN'T run PowerShell/Bash/dotnet — Cecil inspection and builds must stay in foreground.
- **Folder ≠ namespace in IMT source.** The `IMT.Manager.*` namespace types Cecil shows are
  actually under `IMT/MarkingItems/` in the GitHub repo. `IMT/Manager/` folder is just
  orchestration (managers, save extensions). See [docs/IMT-INTERNALS.md](../docs/IMT-INTERNALS.md).
- **IMT has NO public events.** Both DLL and source confirmed: zero `event`, zero `Action`/`Func`
  fields in the public API, no `Subscribe`/`Register` methods. Harmony patches are the only
  send-side mechanism, forever.
- **`Style.OnStyleChanged` is the universal mutation tap.** Patching the owner's private
  `StyleChanged()` method (MarkingFiller / MarkingCrosswalk / MarkingLineRawRule) catches every
  property edit across all 42 style classes via the Action delegate the owner wired in its ctor.
- **Upstream typos preserved on disk** — match exactly when patching by string name:
  `PointLocation.Rigth`, `EntranceDataEnterfaces.cs`, `CrosswalksEditor.*BorgerChanged`,
  `IMT/Tools/MakeItem/MakeLIne.cs`.
- **Each new mod release of IMT may rename internal types.** When IMT updates, re-Cecil-check
  signatures before assuming patches still apply. Or switch to `IMT.API.*` interfaces for the
  receive side (more stable — see [docs/IMT-INTERNALS.md](../docs/IMT-INTERNALS.md) "high-value API methods" table).
- **CSM has cursor sync we can leverage.** `CSM.BaseGame.Injections.Tools.ToolSimulatorCursorManager`
  is a public singleton with `GetCursorView(senderId)` → `PlayerCursorManager`. Call
  `pcm.SetCursor(null)` (falls back to DefaultTool cursor) + `pcm.SetLabelContent(name, pos)` and
  CSM's PCM.Update renders the cursor sprite + name label per frame — exactly like for vanilla tools.
- **CSM updates** can change CSM.API. The `Connection` contract is stable but `CSM.BaseGame`
  (where the cursor and tool simulators live) is internal and may shift between releases.
- **CSM `Connection` only exposes `RegisterHandlers` / `UnregisterHandlers` as virtual** — no
  `OnClientConnect(Player)` hook on the public API (despite older notes suggesting otherwise).
  Mid-session state push must piggyback on the first `RegisterHandlers` after a connection event.

## Two-CS-install layout (this user's specific setup)

| | Host | Client |
| --- | --- | --- |
| CS install path | `M:\Games\Cities Skylines\` | `M:\Games\C_S2\Cities Skylines\` |
| Mods folder | `Files\Mods\` | `Files\Mods\` |
| Log folder | `Cities_Data\Logs\` | `Cities_Data\Logs\` |
| CSM connection config | `client-config.json` (HostAddress, port, username) | same |
| User multiplayer name | `dune` | (set per-instance in client-config) |
