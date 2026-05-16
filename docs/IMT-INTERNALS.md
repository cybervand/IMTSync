# IMT internals — consolidated reference

Synthesized 2026-05-16 from a 6-agent parallel survey of [MacSergey/NodeMarkup](https://github.com/MacSergey/NodeMarkup) plus prior Cecil inspection of `IntersectionMarkingTool.dll` and `IntersectionMarkingTool.API.dll`.

This document captures the source-level view that Cecil alone can't give: comments, intent, public-vs-internal contract notes, and naming conventions. Use as a reference when designing new sync features or debugging.

---

## Critical: folder ≠ namespace

A surprise from the source survey: **the `IMT.Manager.*` namespace types live in `IMT/MarkingItems/`, not `IMT/Manager/`**. The folder structure and namespace declarations are deliberately mismatched.

| Cecil namespace | Source folder |
|---|---|
| `IMT.Manager.Marking`, `IMT.Manager.MarkingLine`, `IMT.Manager.Style`, `IMT.Manager.MarkingFiller`, `IMT.Manager.MarkingCrosswalk`, `IMT.Manager.MarkingPoint`, `IMT.Manager.FillerContour`, `IMT.Manager.FillerVertex`, `IMT.Manager.Enter`, all line/filler/crosswalk style classes | `IMT/MarkingItems/` |
| `IMT.Manager.NodeMarkingManager`, `IMT.Manager.SegmentMarkingManager`, `IMT.Manager.TemplateManager`, `IMT.Manager.StyleTemplateManager`, `IMT.Manager.IntersectionTemplateManager`, asset/save/load extensions | `IMT/Manager/` |
| `IMT.Tools.*` (all tool modes) | `IMT/Tools/` |
| `IMT.UI.*` and `IMT.UI.Editors.*` | `IMT/UI/` |
| `IMT.Utilities.API.*` (implementation of `IMT.API.*` interfaces) | `IMT/Utilities/API/` |
| Render helpers, ObjectsMap, MoveIt integration | `IMT/Utilities/` |
| `IMT.API.*` (public API surface) | `IMT.API/` (separate project) |

When grepping source, search by namespace **and** by folder.

---

## The universal mutation tap: `Style.OnStyleChanged`

Every PropertyValue assignment on any style routes through:
```
PropertyValue<T>.set_Value
  → invokes the Action passed to PropertyValue's ctor
  → calls Style.StyleChanged()
  → invokes Style.OnStyleChanged Action
```

`Style.OnStyleChanged` is declared `public Action OnStyleChanged { private get; set; }` — the setter is accessible so external code (us, in principle) can subscribe by replacing it. But the owners (`MarkingFiller`, `MarkingCrosswalk`, `MarkingLineRawRule`) already wire their own handlers in their constructors:

| Owner | Wires `OnStyleChanged` to | Which calls |
|---|---|---|
| `MarkingFiller` | `FillerChanged()` | `Marking.Update(this, true)` |
| `MarkingCrosswalk` | `CrosswalkChanged()` | `Marking.Update(this, true, true)` |
| `MarkingLineRawRule` | `RuleChanged()` | `Marking.Update(line, true)` (via the line) |

**Implication for sync:** patching the owner's private `StyleChanged()` method (which we already do for `MarkingFiller`, `MarkingCrosswalk`, `MarkingLineRawRule`) catches every property edit on the style without needing to patch each `BasePropertyValue<T>.set_Value` — that warning in CLAUDE.md is now fully justified.

---

## Counts & enumerations

### Concrete style classes (42 total)
- **Regular line:** 14 — Solid, Dashed, DoubleSolid, DoubleDashed, DoubleDashedAsym, SolidAndDashed, SharkTeeth, ZigZag, Pavement (3D), Prop, Tree, Text, Decal, Network. Plus `Empty` and `Buffer` sentinels.
- **Stop line:** 7 — Solid, Dashed, DoubleSolid, DoubleDashed, SolidAndDashed, SharkTeeth, Pavement.
- **Filler:** 12 — 2D: Stripe, Grid, Solid, Chevron, Decal, Asphalt. 3D: Pavement, Grass, Gravel, Ruined, Cliff, CustomTexture.
- **Crosswalk:** 9 — Existent, Zebra, DoubleZebra, ParallelSolidLines, ParallelDashedLines, Ladder, Solid, ChessBoard, Decal.

Every style has 1–20 PropertyValues. Our XML-payload sync abstracts over all 42 — we never need per-style code paths.

### Concrete line subclasses (7)
| Class | User-creatable? | Notes |
|---|---|---|
| `MarkingRegularLine` | ✅ | The default line type |
| `MarkingNormalLine` | ✅ | Goes to a normal point (perpendicular) |
| `MarkingStopLine` | ✅ | Single-style, no rules |
| `MarkingCrosswalkLine` | ✅ | Inherits from MarkingRegularLine |
| `MarkingLaneLine` | ✅ | Inherits from MarkingRegularLine |
| `MarkingEnterLine` | ❌ | Auto-managed |
| `MarkingFillerTempLine` | ❌ | Transient during filler creation |

User-creatable count (5) matches our `IMTActionType.Add*` surface exactly.

### Point types (4 subclasses of `MarkingPoint`)
- `MarkingEnterPoint` — entrance edge point. Has `Offset`, `Split` (PropertyBoolValue), `SplitOffset` (PropertyValue<float>).
- `MarkingNormalPoint` — for perpendicular lines.
- `MarkingCrosswalkPoint` — for crosswalk lines.
- `MarkingLanePoint` — for lane lines.

Only `IEntrancePointData.Offset` has a public setter in the IMT.API surface.

### Filler vertex types (3 subclasses of `IFillerVertex`)
- `EnterFillerVertex(MarkingPoint, Alignment)` — entrance-anchored.
- `LineEndFillerVertex(MarkingPoint, MarkingRegularLine)` — anchored to one end of a line.
- `IntersectFillerVertex(MarkingLine, MarkingLine)` — at the intersection of two lines.

Our filler v2 (`FillerVertexRef`) supports all three.

---

## ToXml / FromXml convention

**Every** style and marking element follows a strictly additive XML round-trip:

```csharp
public override XElement ToXml() {
    var config = base.ToXml();             // get parent's element
    Foo.ToXml(config);                     // append our own PropertyValues
    Bar.ToXml(config);
    return config;
}

public override void FromXml(XElement config, ObjectsMap map, bool invert, bool typeChanged) {
    base.FromXml(config, map, invert, typeChanged);
    Foo.FromXml(config, DefaultFoo);
    Bar.FromXml(config, DefaultBar);
    // `invert` flips alignment-style PropertyValues
    // `typeChanged` lets a class skip restoring properties that don't apply after a type swap
}
```

Robust to IMT internal field additions: new properties show up in XML automatically, old payloads decode with their fields defaulted.

`VersionMigration.Befor1_2 / Befor1_9 / CorrectColor01` auto-migrates old XML on receive — useful if hosts run mismatched IMT versions.

---

## Whole-marking serialization (Phase 4 reference)

**The canonical entry point already exists**: `IMT/Utilities/MoveItIntegration.cs` implements paste-with-remap via:

```csharp
// Send side
var xml = marking.ToXml();        // returns XElement
var encoded = Encode64(xml);      // base64 wrap

// Receive side
var xml = Decode64(encoded);
var map = new ObjectsMap(invert: false, isSimple: false);
// add id remapping if cross-marking paste
map.AddPoint(enterId, sourceIdx, targetIdx);
marking.FromXml(Version, xml, map);
```

**Module ID** for IMT's MoveIt integration: `"CS.macsergey.IntersectionMarkingTool"`. Our extension uses `"CSM.IMTSync"`.

`ObjectsMap` constructor signature: `(bool invert=false, bool isSimple=false)`. Methods:
- `AddInvertEnter(Entrance)` — flip an entrance's point ordering
- `AddPoint(ushort enter, byte source, byte target)` — remap point index
- `AddPoint(int source, int target)` — generic id remap
- `TryGetNode`, `TryGetSegment`

Receivers in CSM context can use `new ObjectsMap()` (no remap) when host and client share node ids (CSM already syncs net state).

---

## The IMT public API (`IMT.API`)

### Composition
- `Helper.GetProvider(string id) → IDataProviderV1` — entry point. Walks `AppDomain` looking for `IDataProviderFactory`, returns versioned provider.
- `IDataProviderV1` exposes ~50 methods/properties for marking lookup, line/filler/crosswalk add/remove, style construction.

### High-value API methods for our receive-side handler
| Today (internal) | API equivalent |
|---|---|
| `SingletonManager<NodeMarkingManager>.Instance.GetOrCreateMarking(id)` | `provider.GetOrCreateNodeMarking(id)` |
| `marking.AddRegularLine(pair, style, alignment)` | `nodeMarking.AddRegularLine(start, end, styleData)` |
| `pt.Offset.Value = cmd.Offset` | `((IEntrancePointData)pt).Offset = cmd.Offset` |
| `marking.RemoveLine(line)` | `nodeMarking.RemoveRegularLine(start, end)` (or typed variant) |
| `marking.Clear()` | `marking.ClearMarkings()` |
| `marking.ResetOffsets()` | `marking.ResetPointOffsets()` |

### The reflective style property bag
`IStyleData.SetValue(string propertyName, object value)` + iterable `IStyleData.Properties[]` (each `IStylePropertyData` has `Type`, `Name`, `Value { get; set; }`) — this is a **generic style-edit channel**. We could broadcast `(propertyName, value)` tuples without ever knowing the concrete style class. Big potential for Tier 2 style sync v2.

### Confirmed: no events anywhere in IMT.API
All 90+ public types. Zero `event`, zero `Action`/`Func` fields, zero `Subscribe`/`Register`/`Listener` methods. The only registration mechanism is `IDataProviderFactory` (mod identity for `Helper.GetProvider`).

**This means Harmony patches are the only way to detect IMT mutations.** Confirmed across both DLL Cecil and source-level inspection.

### Upstream typos to mirror or translate
- `PointLocation.Rigth` (sic — enum value)
- `IMT.API/Interfaces/EntranceDataEnterfaces.cs` (sic — filename)
- `CrosswalksEditor.LeftBorgerChanged` / `RightBorgerChanged` (sic — "Borger" not "Border", in UI editor methods)
- `IMT/Tools/MakeItem/MakeLIne.cs` (sic — capital `I`, would break on case-sensitive filesystems)

---

## Patch targets: precise method signatures

### Already patched (✅)

| Patch | What | Source |
|---|---|---|
| `Marking.Clear` postfix | Wipe marking | MarkingItems/Marking/Marking.cs |
| `Marking.ResetOffsets` postfix | Reset all point offsets | same |
| `Marking.AddRegularLine` postfix | Add regular line | same |
| `Marking.AddNormalLine` postfix | Add normal line | same |
| `Marking.AddStopLine` postfix | Add stop line | same |
| `Marking.AddLaneLine` postfix | Add lane line | same |
| `Marking.AddCrosswalkLine` postfix | Add crosswalk line | same |
| `Marking.AddFiller(FillerContour, BaseFillerStyle, out ...)` postfix | Add filler | same |
| `Marking.RemoveLine(MarkingLine)` postfix | Remove any line | same |
| `Marking.RemoveFiller(MarkingFiller)` postfix | Remove filler | same |
| `DragPointToolMode.OnMouseUp` postfix | Drag commit | Tools/DragPointMode.cs |
| `PointsEditor.OffsetChanged(float)` postfix | Panel-typed point offset | UI/Editors/PointEditor.cs |
| `MarkingFiller.StyleChanged` postfix | Any property on filler style | MarkingItems/Filler/Filler.cs |
| `MarkingCrosswalk.StyleChanged` postfix | Any property on crosswalk style | MarkingItems/Crosswalk/Crosswalk.cs |
| `MarkingLineRawRule.StyleChanged` postfix | Any property on line-rule style | MarkingItems/Line/LineRule.cs |
| `IntersectionMarkingTool.SetMarking(Marking)` postfix | Intersection selection (presence) | Tools/Tool.cs |
| `IntersectionMarkingTool.RenderOverlay(CameraInfo)` postfix | Per-frame render + cursor broadcast | inherited from BaseTool |

### Newly-precise targets (for upcoming TODO items)

| User action | Patch target (exact name) | Where |
|---|---|---|
| **Toggle point split** | `PointsEditor.SplitChanged(bool)` (private) | UI/Editors/PointEditor.cs |
| **Edit line rule style type** | `RulePanel.StyleChanged(Style.StyleType)` + `RulePanel.AfterStyleChanged()` | UI/Editors/RulePanel.cs |
| **Add/delete line rule** | `LinesEditor.AddRule()` / `LinesEditor.DeleteRule(RulePanel)` | UI/Editors/LineEditor.cs |
| **Change line rule edge anchors** | `RulePanel.FromChanged(ILinePartEdge)` / `RulePanel.ToChanged(ILinePartEdge)` | UI/Editors/RulePanel.cs |
| **Set crosswalk right border line** | `CrosswalksEditor.RightBorgerChanged(MarkingRegularLine)` (typo intentional, private) | UI/Editors/CrosswalkEditor.cs |
| **Set crosswalk left border line** | `CrosswalksEditor.LeftBorgerChanged(MarkingRegularLine)` (typo intentional, private) | UI/Editors/CrosswalkEditor.cs |
| **Cut lines under crosswalk** | `CrosswalksEditor.CutLines()` | UI/Editors/CrosswalkEditor.cs |
| **Paste / reset / apply-same on filler** | `FillerEditor.PasteStyle()` / `ResetStyle()` / `ApplyStyleSameStyle()` / `ApplyStyleSameType()` / `ApplyStyle(BaseFillerStyle)` | UI/Editors/FillerEditor.cs |
| **Paste / reset / apply-same on crosswalk** | `CrosswalksEditor.PasteStyle()` / etc. | UI/Editors/CrosswalkEditor.cs |
| **Paste / reset / apply-same on rule** | `RulePanel.PasteStyle()` / `ResetStyle()` / `ApplyStyleToAllRules()` / `ApplyStyleSameStyle()` / `ApplyStyleSameType()` | UI/Editors/RulePanel.cs |
| **Apply intersection preset** | `IntersectionTemplateEditor.Apply()` / `ApplyAll()` / `Link()` | UI/Editors/IntersectionTemplateEditor.cs |
| **Or at the Tool layer** | `IntersectionMarkingTool.PasteMarking()` / `ApplyIntersectionTemplate(template)` / `ApplyAllIntersectionTemplate(template)` / `ApplyPresetToAsset(...)` / `EditMarking()` / `CreateEdgeLines()` / `CutByCrosswalks()` / `SaveAsIntersectionTemplate()` / `LinkPreset(template, roadName)` | Tools/Tool.cs |
| **Apply style template to many** | `StyleTemplateEditor.ApplyStyleSameStyle()` / `ApplyStyleSameType()` | UI/Editors/StyleTemplateEditor.cs |
| **Reorder intersection entrances** | `EditEntersOrderToolMode` Exit / Apply path | Tools/Order/EntersOrderMode.cs |
| **Reorder points within an entrance** | `PointsOrderToolMode` Exit path | Tools/Order/PointsOrderMode.cs |

### Filler-specific commit at the tool level (for Phase 3 live preview)
- `MakeFillerToolMode.OnPrimaryMouseClicked` — vertex add (in-progress contour)
- `MakeFillerToolMode.OnSecondaryMouseClicked` — vertex pop / exit
- `Contour` field — read-only inspection of in-progress contour

### Line/crosswalk commit at the tool level (for Phase 3 rubber-band preview)
- `MakeLineToolMode.OnToolUpdate` — hover state per frame
- `MakeLineToolMode.SelectPoint` field — first-clicked point during drawing
- `MakeCrosswalkToolMode` — same pattern

---

## Persistence & lifecycle

### Mod entry point
`Mod : BasePatcherMod<Mod>` runs Harmony `PatchAll` at IUserMod `OnEnabled` — well before any map load. Our extension's patches are installed before CSM hands us any commands. There is **no "IMT not loaded" race condition** to defend against once we pass the existing `Patcher.PatchAll` IMT-presence guard.

### Save/load flow
- Save game: `SerializableDataExtension.GetSaveData()` → `MarkingManager.ToXml()` → blob in savegame.
- Load game: `SerializableDataExtension.SetLoadData(blob)` → `MarkingManager.FromXml(config, new ObjectsMap(), needUpdate: false)`.
- Asset save/load: `BuildingAssetDataExtension` / `NetworkAssetDataExtension` round-trip per-asset markings, using `ObjectsMap.AddSegment(oldId, newId) / AddNode(oldId, newId)` for id remapping. **This is the proven pattern for snapshot-replay.**
- **For our sync replays:** use `needUpdate: true` (we want visuals to render immediately) when calling `FromXml`.

### CSM `/sync` interaction
When a client runs `/sync`, CSM retransmits the host's savegame. IMT's `SerializableDataExtension.SetLoadData` runs on the receiver. **This is the current workaround for the "filler references missing lines" problem** because both clients end up with identical marking state from the host's save.

### `MarkingManager.Errors` counter
Set to -1 by `SetFailed()` when partial load fails. After applying a snapshot, our handler could check `Errors == 0` and log warnings.

---

## Property panels & commit semantics

The IMT UI has a clear convention:

| Method-name pattern | Fires for | Sync-relevant? |
|---|---|---|
| `*Changed(value)` (e.g. `OffsetChanged`, `StyleChanged`, `RightBorgerChanged`, `SplitChanged`, `FromChanged`, `ToChanged`) | **User commit** — single click/edit/dropdown selection | ✅ Patch this |
| `AfterStyleChanged()` | After `StyleChanged` finishes (debounced) | ✅ Sometimes; `StyleChanged` alone is usually enough |
| `Apply*`, `Paste*`, `Reset*`, `Save*`, `Discard*`, `Cancel`, `OnApplyChanges`, `OnNotApplyChanges` | Explicit user action button | ✅ Patch these |
| `Refresh*`, `OnObjectUpdate`, `RefreshProperties`, `RefreshAdditionalProperties` | Preview / rebuild only | ❌ Skip |
| `Hover*`, `Leave*`, `Select*Edge`, `Select*Button`, `Enter*`, `Leave*` | Preview / presence only | Phase 3 only |
| Property panel `OnValueChanged` / `OnTextChanged` | **Every keystroke / slider tick** | ❌ Don't patch directly — fires too often. Use editor-level commit instead. |

**Implication:** if we want per-property style sync (vs whole-style XML), the right approach is to patch the editor's `AfterStyleChanged()` and broadcast the full style snapshot at that point — not patch each property panel.

---

## ModsCommon utilities worth knowing

Statically linked into IMT.dll (the `ModsCommon/` git submodule):

- `SingletonManager<T>.Instance` / `SingletonMod<T>` / `SingletonTool<T>` / `SingletonItem<T>` — global access pattern.
- `BasePropertyValue<T>(Action onChanged, ...)` — the property wrapper with change-notification.
- `PropertyValue<T>`, `PropertyStructValue<T>`, `PropertyBoolValue`, `PropertyEnumValue<T>`, `PropertyColorValue`, `PropertyVector2Value`, `PropertyPrefabValue`, `PropertyNullableStructValue<T>`, `PropertyThemeValue` — concrete typed wrappers.
- `ModsCommon.Utilities.XmlExtension.Parse(xml)` — **Mono-safe XML parser**. Investigate replacing our explicit `XmlReader.Create(StringReader(xml))` dance in `Services/StyleSerializer.cs`.
- `ModsCommon.Utilities.OverlayData` — drawing struct (we use this).
- `ModsCommon.Utilities.RenderExtension` — drawing methods (we use `RenderCircle`).
- `ModsCommon.BaseTool<,>` / `BaseToolMode<>` — tool lifecycle base classes.

---

## What we still don't have an answer for

- **Multi-rule line sync** (Tier 2 style v2). The pieces are clear: `LinesEditor.AddRule()`, `DeleteRule(RulePanel)`, `RulePanel.StyleChanged`, `RulePanel.FromChanged`/`ToChanged`. Wire format needs rule index/edge identity. Not yet designed.
- **Mid-session state push.** Pattern is clear (copy MoveItIntegration). Trigger TBD — likely on first `RegisterHandlers` after a new client connects. Need to figure out which side initiates and when.
- **Phase 3 live drag previews.** `MakeLineToolMode` / `MakeFillerToolMode` / `MakeCrosswalkToolMode` `OnToolUpdate` for hover broadcasts. Wire format and ghost rendering TBD.
- **Bulk burst rate-limiting.** `Tool.CreateEdgeLines()` and `Tool.CutByCrosswalks()` produce dozens of individual `AddRegularLine` events. Current per-line patches work but could flood a slow link. Could batch with a higher-level `Tool.*` postfix.
