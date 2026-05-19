# IMT Line Wire Format

This document describes the line data that CSM.IMTSync sends today, plus the
recommended full-line state format for syncing every setting in IMT's Lines tab.

## Current Line Packets

Lines currently use the shared `IMTActionCommand` protobuf message. Fields that
do not apply to the action are left at their default values.

Common fields for every line action:

```csharp
IMTActionCommand
{
    Type,       // AddRegularLine, AddNormalLine, AddStopLine, AddLaneLine,
                // AddCrosswalk, Remove..., or UpdateLineStyle
    Scope,      // Node or Segment
    MarkingId,  // node id or segment id
    A,          // first endpoint
    B,          // second endpoint
    Version     // last-writer-wins edit version, when used
}
```

Each endpoint is a `PointRef`:

```csharp
PointRef
{
    MarkingId,   // same node or segment id
    EntranceId,  // segment id for node markings, node id for segment markings
    Index,       // point index on that entrance
    Kind         // Entrance, Normal, Crosswalk, or Lane
}
```

### Add Regular Line

```csharp
IMTActionCommand
{
    Type = IMTActionType.AddRegularLine,
    Scope,
    MarkingId,
    A,
    B,
    StyleXml,
    Alignment
}
```

`StyleXml` contains IMT's own serialized `<S ... />` block for the first
line rule. `Alignment` is the IMT `Alignment` enum value captured when the line
is created.

### Add Normal Line

```csharp
IMTActionCommand
{
    Type = IMTActionType.AddNormalLine,
    Scope,
    MarkingId,
    A,
    B,
    StyleXml,
    Alignment
}
```

### Add Stop Line

```csharp
IMTActionCommand
{
    Type = IMTActionType.AddStopLine,
    Scope,
    MarkingId,
    A,
    B,
    StyleXml
}
```

Stop lines do not currently send line-level start/end alignment changes after
creation.

### Add Lane Line

```csharp
IMTActionCommand
{
    Type = IMTActionType.AddLaneLine,
    Scope,
    MarkingId,
    A,
    B,
    StyleXml
}
```

Lane line endpoints are sent with `PointKind.Lane`.

### Add Crosswalk Line

```csharp
IMTActionCommand
{
    Type = IMTActionType.AddCrosswalk,
    Scope,
    MarkingId,
    A,
    B,
    StyleXml
}
```

Crosswalk endpoints are sent with `PointKind.Crosswalk`. Later crosswalk style
updates use `UpdateCrosswalkStyle`, not `UpdateLineStyle`.

### Remove Line

```csharp
IMTActionCommand
{
    Type = IMTActionType.RemoveRegularLine
        or IMTActionType.RemoveNormalLine
        or IMTActionType.RemoveStopLine
        or IMTActionType.RemoveLaneLine
        or IMTActionType.RemoveCrosswalk,
    Scope,
    MarkingId,
    A,
    B
}
```

The receiver resolves `A` and `B`, finds the existing IMT line from that point
pair, and removes it if present.

### Update Regular Line Style

```csharp
IMTActionCommand
{
    Type = IMTActionType.UpdateLineStyle,
    Scope,
    MarkingId,
    A,
    B,
    RuleIndex,
    StyleXml
}
```

This is sent from the `MarkingLineRawRule.RuleChanged()` hook. The receiver
resolves the line by `A` and `B`, then applies `StyleXml` to the rule at
`RuleIndex`.

This currently targets `MarkingRegularLine` rules only.

## What Current Line Style XML Covers

Because `StyleXml` is IMT's own `<S ... />` chunk, it can cover style-owned
settings such as:

- IMT's local style discriminator
- color and alpha
- width
- dash length and space length
- decal asset or object prefab references
- decal size, tiling, angle, shift, step, probability, and similar style fields
- texture density
- cracks density and scale
- voids density and scale

The exact attributes vary by IMT style class.

Example dashed-style shape:

```xml
<S
    T="..."
    C="136,136,136,224"
    W="0.15"
    DL="1.5"
    SL="1.5"
    TEX="0"
    ST="0"
    SS="100"
    VT="0"
    VS="100" />
```

## Style Identity Warning

Do not treat the XML style `T` value as a stable multiplayer identifier. It is
IMT's own style discriminator, and it is only safe to interpret inside IMT's
serializer on a machine with a compatible IMT/style setup.

For CSM.IMTSync, stable identity should come from the marked object, not the
style number:

- lines: endpoint `PointRef A` + endpoint `PointRef B` + `RuleIndex`
- crosswalks: crosswalk endpoint refs plus optional border endpoint refs
- fillers: sender filler id mapping, then vertex contour fallback

The `<S ... />` XML should be treated as an opaque IMT payload. We send it so
the receiver can ask its local IMT installation to parse and apply it. We should
not split it into our own enum table unless we also define a separate semantic
mapping layer for every style class and asset reference.

## Current Gaps

The current line packets do not send the whole IMT line. They only send creation,
deletion, and style changes. That means these line-level or rule-structure
settings are not fully represented yet:

- clip sidewalk
- regular line alignment changes after creation
- stop line left/right alignment changes after creation
- rule `From` and `To` support targets
- adding or removing extra rules
- reordering or replacing the entire rule list
- any setting stored on the line or rule wrapper instead of inside `<S ... />`

## Recommended Full Line State Packet

To sync the entire Lines tab, add a new append-only action and protobuf field:

```csharp
public enum IMTActionType : byte
{
    UpdateLineState = 81 // example; append only, do not renumber old values
}

public class IMTActionCommand
{
    [ProtoMember(31)] public string LineXml;
}
```

Recommended packet:

```csharp
IMTActionCommand
{
    Type = IMTActionType.UpdateLineState,
    Scope,
    MarkingId,
    A,
    B,
    LineXml,
    Version
}
```

`A` and `B` remain the stable cross-client identity. `LineXml` carries IMT's
complete line serialization.

## Full Line XML Shape

Regular line example:

```xml
<L Id="123456789" T="256" A="0" CS="true">
  <R>
    <!-- IMT stores the rule From/To support data here. -->
    <From ... />
    <To ... />
    <S ... />
  </R>
  <R>
    <From ... />
    <To ... />
    <S ... />
  </R>
</L>
```

Stop line example:

```xml
<L Id="987654321" T="512" AL="0" AR="0">
  <R>
    <S ... />
  </R>
</L>
```

Known line-level fields from IMT:

- `Id`: IMT line id, derived from the point pair
- `T`: line type
- `A`: regular line raw alignment
- `CS`: regular line clip sidewalk
- `AL`: stop line start alignment
- `AR`: stop line end alignment
- `<R>` children: line rules
- nested rule support data: rule `From` and `To`
- nested `<S ... />`: all style-owned settings

## Apply Rule

When receiving `UpdateLineState`, the receiver should replace the local line
state for `A` and `B` with the incoming XML. The implementation should avoid
appending duplicate rules when applying the XML. If IMT's `FromXml` appends
rules, clear or recreate the affected line first, then apply the full state.
