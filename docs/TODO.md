# IMTSync — Unsynced actions TODO

Tracking what's still missing from the IMT → CSM bridge.
Updated 2026-05-16 after the 6-agent IMT-source survey (see [docs/IMT-INTERNALS.md](IMT-INTERNALS.md)).

Conventions:

- "Patch target" is the Harmony patch site (method to postfix), now with source-confirmed exact names.
- "Wire" is the proposed `IMTActionType` discriminator value (append-only — never renumber existing entries).
- Wherever a new `IMTActionCommand` field is needed, append a new `[ProtoMember(N)]` only.
- See [.claude/CLAUDE.md](../.claude/CLAUDE.md) for the patch recipe and Mono 2.x runtime traps.
- See [docs/IMT-INTERNALS.md](IMT-INTERNALS.md) for the consolidated IMT architecture reference.

---

## Priority 1 — Style editing (biggest gap, every drawn item gets edited)

- [x] **Filler style change** — color, hatch pattern, width, step, angle.
      Patch: `MarkingFiller.StyleChanged` (private). Already shipped.
- [x] **Crosswalk style change** — color, pattern, etc.
      Patch: `MarkingCrosswalk.StyleChanged` (private). Already shipped.
- [x] **Line rule style change (first rule only — single-rule lines)** — color, width, dash, etc.
      Patch: `MarkingLineRawRule.StyleChanged` (private). Already shipped.
- [ ] **Line rule style change (multi-rule, additional rules)** — currently only rule[0] syncs.
      Patch target: same `MarkingLineRawRule.StyleChanged` postfix, but wire format needs a rule-index
      or rule-edge identifier so the receiver can find the right rule.
      Wire idea: new `UpdateLineRuleStyle` action carrying `(LineRef A+B, RuleIdentity, StyleXml)`.
      RuleIdentity options: index in `MarkingLine.Rules`, or `(ILinePartEdge from, ILinePartEdge to)` pair.

---

## Priority 2 — Crosswalks (partial coverage today)

- [ ] **Add full Crosswalk** — the wide painted shape (zebra, ladder, parallel, etc.), not just
      `AddCrosswalkLine`. Patch target: `Marking.AddCrosswalk(MarkingCrosswalk)`.
- [ ] **Remove full Crosswalk** — `Marking.RemoveCrosswalk(MarkingCrosswalk)`.
- [ ] **Crosswalk right border line** — `CrosswalksEditor.RightBorgerChanged(MarkingRegularLine)`.
      (Typo "Borger" intentional — preserved upstream, patch via string name.)
- [ ] **Crosswalk left border line** — `CrosswalksEditor.LeftBorgerChanged(MarkingRegularLine)`.
- [ ] **Cut lines under crosswalk** — `CrosswalksEditor.CutLines()`. Side-effect mutator that
      shortens regular lines that pass under a new crosswalk.
- [ ] **Filler/Crosswalk paste/reset/apply-same buttons** —
      `FillerEditor.PasteStyle() / ResetStyle() / ApplyStyleSameStyle() / ApplyStyleSameType() / ApplyStyle(BaseFillerStyle)`
      and the parallel set on `CrosswalksEditor`. Bulk style mutators.

---

## Priority 3 — Point editing

- [ ] **Toggle point split** — when `IsSplit` flips on/off, sub-points appear/disappear.
      Patch target: `PointsEditor.SplitChanged(bool)` (private). Drop-in next to OffsetChanged.
      Wire: new `SetPointSplit` action `(PointRef, bool IsSplit)`.
- [ ] **Split offset value** — `PointsEditor` likely has a similar `SplitOffsetChanged(float)`;
      confirm by source. Mirror SetPointOffset pattern, new `SetSplitOffset` action.
- [ ] **Point shift** — `PointsEditor.AddShift(...)` wires a property panel; the actual commit may
      be an inline lambda on `OnValueChanged`. Source-confirm to get the patchable method name.

---

## Priority 4 — Workflows (CLAUDE.md "Phase 4")

Wire pattern (re-used across all of these): copy MoveItIntegration's shape — broadcast
`Marking.ToXml()` as a string, receiver decodes with `Marking.FromXml(Version, XElement, new ObjectsMap(), needUpdate: true)`.
VersionMigration auto-handles cross-version migration on receive.

- [ ] **Paste whole marking** — patch `IntersectionMarkingTool.PasteMarking()` postfix (Tool layer).
      Captures the user's paste action; the XML is available via `Tool.MarkingBuffer.ToXml()`
      or by reading `Tool.Marking.ToXml()` after the call.
- [ ] **Apply intersection preset** — patch `IntersectionTemplateEditor.Apply()` / `ApplyAll()` /
      `Link()` (private). Or the Tool layer: `IntersectionMarkingTool.ApplyIntersectionTemplate(template)`
      / `ApplyAllIntersectionTemplate(template)` / `LinkPreset(template, roadName)`.
- [ ] **Apply preset to asset** — `IntersectionMarkingTool.ApplyPresetToAsset(NetInfo, preset, bool flip, bool invert)`.
- [ ] **Edit existing marking via paste-flow** — `IntersectionMarkingTool.EditMarking()`.
- [ ] **Copy marking** — `IntersectionMarkingTool.CopyMarking()` (mostly local, but the buffer
      affects subsequent paste; could optionally sync the buffer state).
- [ ] **Apply style template to many items** —
      `StyleTemplateEditor.ApplyStyleSameStyle()` / `ApplyStyleSameType()` / `ApplyStyle(BaseFillerStyle/...)`.
- [ ] **Style template save/duplicate/set-as-default** — `StyleTemplateEditor.OnApplyChanges() / OnNotApplyChanges() / ToggleAsDefault() / Duplicate()`.
      Local-data oriented; may need to sync if host's templates are the source of truth for paste flow.

---

## Priority 5 — Line rules & advanced

- [ ] **Add line rule** — `LinesEditor.AddRule()` (private). User clicks `+` button.
- [ ] **Delete line rule** — `LinesEditor.DeleteRule(RulePanel)`. Public.
- [ ] **Change rule edge anchors** — `RulePanel.FromChanged(ILinePartEdge)` / `RulePanel.ToChanged(ILinePartEdge)`.
- [ ] **Rule paste / reset / apply-same** — `RulePanel.PasteStyle() / ResetStyle() / ApplyStyleToAllRules() / ApplyStyleSameStyle() / ApplyStyleSameType()`.
- [ ] **Rule-edge tool mode** — `PartEdgeToolMode` is the visual picker for rule edges. Probably
      no patch needed if `FromChanged`/`ToChanged` are the commit hooks.

---

## Priority 6 — Tool-level burst operations

These produce dozens of individual `Marking.AddRegularLine` events that are already covered by our
per-line patches, but on a slow link the flood is wasteful. Could batch into single commands.

- [ ] **`IntersectionMarkingTool.CreateEdgeLines()`** — adds an edge line per entrance.
- [ ] **`IntersectionMarkingTool.CutByCrosswalks()`** — re-cuts every line that crosses a crosswalk.
- [ ] **`IntersectionMarkingTool.DeleteAllMarking()`** — already covered by `Marking.Clear` patch.
- [ ] **`IntersectionMarkingTool.ResetAllOffsets()`** — already covered by `Marking.ResetOffsets`.

---

## Priority 7 — Filler v2 scope limits

The filler vertex sync supports Enter / LineEnd / Intersect for entrance-anchored regular lines.
Remaining edge cases:

- [ ] **LineEnd / Intersect vertices on non-regular lines** — fillers anchored to stop/lane/crosswalk
      lines fail today. `PointResolver.TryResolveInternalEnterPoint` only walks `entrance.EnterPoints`.
      Fix: extend `PointResolver` to be `PointKind`-aware (check `Lane`/`Crosswalk`/`Normal` and
      walk the matching collection). API equivalent exists: `APIHelper.GetEntrancePoint / GetNormalPoint / GetCrosswalkPoint / GetLanePoint`.

---

## Priority 8 — Order operations

- [ ] **Reorder intersection entrances** — `EditEntersOrderToolMode`. The mode is the post-import
      confirm UI; the actual commit happens on `Exit(false)` after order edits. Could also patch
      `IntersectionMarkingTool.SetEntersOrder(...)` if such a method exists.
- [ ] **Reorder points within entrance** — `PointsOrderToolMode`. Commit on `Exit`.

---

## Phase 3 — Live presence (separate feature class)

Build on top of `IMTActionType.CursorPresence` (already shipped — broadcasts at 10 Hz via
`IntersectionMarkingTool.RenderOverlay` patch, routes through CSM's `ToolSimulatorCursorManager` +
`PlayerCursorManager`).

- [x] **Cursor position** — shipped via `CursorPresence` action + CSM PCM rendering.
- [x] **Intersection claim ring** — shipped via `SelectIntersection` + custom render postfix.
- [x] **Entrance-point dots at remote-claimed intersections** (Tier α) — shipped.
- [ ] **Rubber-band line preview** — patch `MakeLineToolMode.OnToolUpdate` to broadcast
      `(SelectPoint, HoverPoint)` while drawing. Ghost-render via our existing render postfix.
- [ ] **In-progress filler contour preview** — patch `MakeFillerToolMode.OnPrimaryMouseClicked`
      vertex-add and `OnSecondaryMouseClicked` vertex-pop. Render in-progress polygon.
- [ ] **Crosswalk drawing preview** — same pattern via `MakeCrosswalkToolMode`.
- [ ] **Drag-in-progress ghost** — patch `DragPointToolMode.OnMouseDrag` (throttle 10 Hz) to
      broadcast the live offset value. Render a ghost point at the predicted position.
- [ ] **Tool mode badge** — broadcast the player's current tool mode (extend `CursorPresence` or new
      action) so receivers can show "Bob is drawing a filler" near the cursor.
- [ ] **Selection ghost** — when a player selects a line/filler/crosswalk in the editor, broadcast
      it; receivers highlight the same item in their view. Editor hooks: `OnObjectSelect(T)`.

---

## Mid-session join

- [ ] **Automatic full-state sync on connect.** Currently relies on user typing `/sync` (which
      retransmits the host's full savegame). CSM doesn't expose an `OnClientConnect` hook on
      `CSM.API.Connection` — only `RegisterHandlers` / `UnregisterHandlers` are virtual.
      Design (now precise):
      1. On the **host's** `RegisterHandlers` (post-map-load), enumerate all markings:
         `foreach (var m in SingletonManager<NodeMarkingManager>.Instance) { ... }` (the manager
         is `IEnumerable<TypeMarking>`).
      2. For each, capture `m.ToXml()` and pack into a `MarkingsSnapshot` action.
      3. Broadcast on a timer or on a new-client signal (the latter requires CSM internals).
      4. **Receiver**: `marking.FromXml(Version, xml, new ObjectsMap(), needUpdate: true)`.
      5. Wrap apply in `CsmBridge.StartIgnore()` to suppress re-broadcast cascades.
      6. Use `VersionMigration` auto-migration semantics — receiver tolerates cross-version XML.

---

## Operational / housekeeping

- [ ] Audit whether `Marking.Clear` patch flooding can happen during a CSM `/sync` reload window
      (would show as a flurry of `Local Marking.Clear` lines during map unload). If so, add a
      `IsLoading` flag set during `UnregisterHandlers`/`RegisterHandlers` window and check in patches.
- [ ] **Refactor opportunity:** receive-side handlers could be rewritten using `IMT.API` interfaces
      instead of internal `IMT.Manager.*` types. More resilient to IMT internal renames.
      Specific replacements: see [docs/IMT-INTERNALS.md](IMT-INTERNALS.md) "high-value API methods" table.
- [ ] **Reflective style property sync (Tier 2 v2):** consider switching from `StyleXml` payload
      to per-property `(propertyName, boxedValue)` tuples via `IStyleData.SetValue(...)`. Smaller
      wire payload, granular diffs. Trade-off: type-tag complexity for the value.
- [ ] **`XmlExtension.Parse(xml)`** (ModsCommon utility) may be Mono-2.x-safe. Investigate
      replacing the explicit `XmlReader.Create(new StringReader(xml), new XmlReaderSettings())`
      dance in [Services/StyleSerializer.cs](../Services/StyleSerializer.cs).
- [ ] Confirm SetPointOffset works end-to-end on two PCs (built clean, not yet exercised).
- [ ] Confirm Tier 2 LWW versioning + tombstones work in-game (built clean, not yet stress-tested
      with concurrent edits).
- [ ] Confirm Tier 1 (intersection ring + entrance dots + cursor sprite) renders on both sides
      after the merged `RenderOverlay_Patch` fix.
- [ ] Update [.claude/CLAUDE.md](../.claude/CLAUDE.md) "Phase status" table once SetPointOffset,
      filler v2, Tier 2 LWW, and Tier 1 presence are all confirmed working on two PCs.

---

## Upstream typos & quirks to remember

- `IMT.API.PointLocation.Rigth` (sic — enum value)
- `IMT.API/Interfaces/EntranceDataEnterfaces.cs` (sic — filename)
- `IMT.UI.Editors.CrosswalksEditor.LeftBorgerChanged` / `RightBorgerChanged` (sic — "Borger" not "Border")
- `IMT/Tools/MakeItem/MakeLIne.cs` (sic — capital `I` in middle of "Line")

Mirror them exactly when patching by string name; the typos appear stable upstream.
