---
name: imt-internals
description: Verified IMT type signatures, mutating-method surface on Marking, point identity model, style XML round-trip, ambiguous overloads requiring patch disambiguation
metadata:
  type: reference
---

# IMT internals — tactical notes

> **The authoritative reference is now `docs/IMT-INTERNALS.md`** — a 6-agent source-level survey
> of [MacSergey/NodeMarkup](https://github.com/MacSergey/NodeMarkup) covering folder/namespace
> mapping, every public type, the universal mutation tap (`Style.OnStyleChanged`), exact patch
> targets, persistence flow, and 42 style classes. This memory file keeps the tactical
> Cecil-verified callouts; the doc has the full architecture.
>
> Key things to know that LIVE ONLY in the doc:
>
> - `IMT.Manager.*` namespace types live in the `IMT/MarkingItems/` folder, not `IMT/Manager/`
> - `Style.OnStyleChanged` Action wired by `MarkingFiller`/`MarkingCrosswalk`/`MarkingLineRawRule` ctors
> - `MoveItIntegration` provides the exact Phase 4 paste pattern (ToXml ↔ FromXml + ObjectsMap)
> - Upstream typos preserved on disk: `PointLocation.Rigth`, `EntranceDataEnterfaces.cs`,
>   `CrosswalksEditor.*BorgerChanged`, `MakeLIne.cs`

All findings here verified via Mono.Cecil against `IntersectionMarkingTool.dll` v1.15.0.0 — see [[cecil_pattern]] for the harness.

## `IMT.Manager.Marking` mutating surface (the patch targets)

| Method | Notes |
| --- | --- |
| `Clear()` | wipe all markings |
| `ResetOffsets()` | reset entrance point offsets |
| `AddRegularLine(MarkingPointPair, RegularLineStyle, Alignment)` | draws a line — most common user action |
| `AddNormalLine(MarkingPointPair, RegularLineStyle, Alignment)` | rare |
| `AddStopLine(MarkingPointPair, StopLineStyle)` | no alignment |
| `AddLaneLine(MarkingPointPair, RegularLineStyle)` | no alignment |
| `AddCrosswalkLine(MarkingPointPair, BaseCrosswalkStyle)` | for crosswalks |
| `AddFiller(FillerContour, BaseFillerStyle, out List<MarkingRegularLine>)` | the user-facing filler add. The 3rd arg is `out` not `ref` despite Cecil's `&` annotation |
| `RemoveLine(MarkingLine line)` | handles ALL line subtypes; dispatch on `line.Type` (LineType.Regular/Stop/Lane/Crosswalk) |
| `RemoveCrosswalk(MarkingCrosswalk)` | called internally by RemoveLine for crosswalk lines; user-facing path uses RemoveLine |
| `RemoveFiller(MarkingFiller)` | single overload, no ambiguity |

## Ambiguous overloads (REQUIRE patch disambiguation)

- `Marking.RemoveLine` has `(MarkingLine)` AND `(MarkingLine, bool recalculate)` — use `[HarmonyPatch(typeof(Marking), nameof(Marking.RemoveLine), new System.Type[] { typeof(MarkingLine) })]`
- `Marking.AddFiller` has `(MarkingFiller)` AND `(FillerContour, BaseFillerStyle, ref/out List<MarkingRegularLine>)` — use `static MethodBase TargetMethod()` returning `AccessTools.Method(typeof(Marking), nameof(Marking.AddFiller), new[] { typeof(FillerContour), typeof(BaseFillerStyle), typeof(List<MarkingRegularLine>).MakeByRefType() })` — the `MakeByRefType()` call can't appear inside an attribute

## Point identity model

- API exposes `IPointData.MarkingId (ushort)`, `EntranceId (ushort)`, `Index (byte)` — our 6-byte `PointRef` struct mirrors this
- Internal `MarkingPoint` has `Id (int — composite hash)`, `Index (byte)`, `Enter (Entrance)` — extract `point.Enter.Id` for the entrance ID and `point.Index` for the index
- Internal `MarkingPoint` does NOT implement `IPointData` — they are separate type hierarchies; the API wraps internal types
- `Marking.Enters` returns `IEnumerable` of `Entrance` — for iterating
- `Entrance.EnterPoints` returns `IEnumerable` of `MarkingEnterPoint` — for finding by index (`marking.Enters[id].EnterPoints[idx]`)

## Style serialization

- `IMT.Manager.Style` has generic static factory `bool FromXml<T>(XElement, ObjectsMap, bool invert, bool typeChanged, out T style)` — reads concrete type discriminator from XML
- Every style (RegularLineStyle, StopLineStyle, BaseCrosswalkStyle, BaseFillerStyle, etc.) has `XElement ToXml()` instance method
- `ObjectsMap` ctor is `(bool invert, bool isSimple)` — no parameterless. Use `new ObjectsMap(false, false)` for sync (we don't remap IDs)
- Wire format: ship the `Style` XML chunk verbatim — much simpler than parallel protobuf for ~40 style types, and tracks IMT updates for free
- See `Services\StyleSerializer.cs` for the Mono-2.x-safe parser

## Marking find by ID (receive side)

- Use `ModsCommon.SingletonManager<NodeMarkingManager>.Instance.GetOrCreateMarking(ushort id)` for nodes
- Use `ModsCommon.SingletonManager<SegmentMarkingManager>.Instance.GetOrCreateMarking(ushort id)` for segments
- `MarkingManager<T>.GetOrCreateMarking(ushort)` — NOT `GetOrCreate` (different name)
- `Marking.TryGetLine(MarkingPointPair, out MarkingLine)` — find a line by endpoints
- `Marking.TryGetFiller(int id, out MarkingFiller)` — but filler IDs differ across clients; use contour-set comparison instead

## FillerContour

- Constructor: `FillerContour(Marking marking, IEnumerable<IFillerVertex> vertices)`
- Vertex types (concrete `IFillerVertex` implementors): `EnterFillerVertex` (user-clicked entrance), `LineEndFillerVertex`, `IntersectFillerVertex`
- `EnterFillerVertex(MarkingPoint point, Alignment alignment)` — the v1-only-supported case
- `EnterFillerVertex.Alignment` is the `Alignment` enum directly (NOT a `BasePropertyValue<Alignment>` wrapper) — extract via `(byte)v.Alignment`, NOT `v.Alignment.Value`
- `contour.RawVertices` returns `IEnumerable<IFillerVertex>` — what we serialize
- `marking.Fillers` returns `IEnumerable<MarkingFiller>` — for receive-side contour matching

## LineType enum values

Regular=256, Stop=512, Crosswalk=2048, Lane=4096, All=6912

## Tool-mode → Marking call IL trace (verified)

- `MakeLineToolMode.OnPrimaryMouseClicked` → `AddRegularLine` / `AddStopLine` / `AddLaneLine`
- `MakeLineToolMode.OnDelete` → `RemoveLine`
- `MakeCrosswalkToolMode.OnPrimaryMouseClicked` → `AddCrosswalkLine`
- `MakeCrosswalkToolMode.OnDelete` → `RemoveLine` (same — dispatched by line.Type)
- `MakeFillerToolMode.OnPrimaryMouseClicked` → `AddFiller(FillerContour, ...)`
- `DragPointToolMode.OnMouseDrag` → `MarkingPoint.Offset.set_Value(...)` (the `BasePropertyValue` float setter — patching this directly is too broad; patch `OnMouseDrag` postfix instead)
