# IMT Crosswalk Wire Format

This document describes the crosswalk data that CSM.IMTSync sends today, plus
the complete IMT crosswalk state shape from the bundled IMT source.

## Current Crosswalk Packets

Crosswalk creation currently uses the same `ApplyAddLine<BaseCrosswalkStyle>`
path as other line creation actions:

```csharp
IMTActionCommand
{
    Type = IMTActionType.AddCrosswalk,
    Scope,
    MarkingId,
    A,        // first crosswalk endpoint, PointKind.Crosswalk
    B,        // second crosswalk endpoint, PointKind.Crosswalk
    StyleXml
}
```

Crosswalk style updates currently use:

```csharp
IMTActionCommand
{
    Type = IMTActionType.UpdateCrosswalkStyle,
    Scope,
    MarkingId,
    A,               // crosswalk line start endpoint
    B,               // crosswalk line end endpoint
    StyleXml,

    HasRightBorder,
    RightBorderA,
    RightBorderB,

    HasLeftBorder,
    LeftBorderA,
    LeftBorderB
}
```

The receiver resolves `A` and `B` to the `MarkingCrosswalkLine`, finds the
`MarkingCrosswalk` that references that line, applies the optional border line
refs, and then applies `StyleXml`.

## Full IMT Crosswalk XML Shape

IMT serializes a crosswalk as a `<C>` element:

```xml
<C L="123456789" RB="222222222" LB="333333333">
  <S ... />
</C>
```

Fields:

- `L`: crosswalk line point-pair hash
- `RB`: optional right border regular-line point-pair hash
- `LB`: optional left border regular-line point-pair hash
- nested `<S ... />`: crosswalk style XML

The current packet already sends cross-client-safe endpoint refs for the
crosswalk line and optional border lines instead of relying on raw hashes.

## Common Style Fields

Most visible crosswalk styles inherit these fields:

```xml
<S
    T="..."
    C="..."
    W="..."
    TEX="..."
    ST="..."
    SS="..."
    VT="..."
    VS="..."
    OB="..."
    OA="..." />
```

Common attributes:

- `T`: IMT's local style discriminator
- `C`: main color
- `W`: width
- `TEX`: texture density
- `ST`: cracks density
- `SS`: cracks scale
- `VT`: voids density
- `VS`: voids scale
- `OB`: offset before
- `OA`: offset after

`TEX`, `ST`, `SS`, `VT`, and `VS` only exist on styles that implement IMT's
effect-style interface.

## Style Identity Warning

Do not treat the XML style `T` value as a stable multiplayer identifier. It is
IMT's own style discriminator, and it is only safe to interpret inside IMT's
serializer on a machine with a compatible IMT/style setup.

In CSM multiplayer, players must have the exact same mod setup, so the style
`T` value is acceptable inside the opaque IMT `<S ... />` payload for one
session. It should still not be used as the identity of the crosswalk object.

For CSM.IMTSync, stable crosswalk identity should come from the crosswalk line
endpoint refs and optional border endpoint refs. The nested `<S ... />` should
be treated as opaque IMT XML, not as a protocol model we manually decode.

## Asset Identity

For decal crosswalks, asset identity should come from the prefab/raw asset name
stored by IMT, not from a runtime object id. IMT's decal crosswalk style stores
the decal asset in the style XML:

```xml
<S
    T="..."
    DCL="..."
    DC="..."
    TLX="..."
    TLY="..."
    A="..." />
```

Relevant decal fields:

- `DCL`: decal prefab/raw asset name
- `DC`: decal color
- `TLX`: tiling X
- `TLY`: tiling Y
- `A`: angle

Because CSM enforces matching mods/assets, the receiver should be able to
resolve the same decal name locally.

## Style-Specific Settings

### Existent

```xml
<S T="..." W="..." />
```

Settings:

- width

### Solid

```xml
<S T="..." C="..." W="..." OB="..." OA="..." TEX="..." ST="..." SS="..." VT="..." VS="..." />
```

Settings:

- color
- width
- offset before
- offset after
- texture density
- cracks density/scale
- voids density/scale

### Zebra

```xml
<S
    T="..."
    C="..."
    W="..."
    OB="..."
    OA="..."
    DL="..."
    SL="..."
    P="..."
    USC="..."
    SC="..."
    UG="..."
    GL="..."
    GP="..."
    TEX="..."
    ST="..."
    SS="..."
    VT="..."
    VS="..." />
```

Settings:

- main color
- width
- offset before
- offset after
- dash length
- space length
- dash end type
- use second color
- second color
- use gap
- gap length
- gap period
- texture density
- cracks density/scale
- voids density/scale

### Double Zebra

Double Zebra has every Zebra setting plus:

```xml
O="..."
```

Additional setting:

- offset between the two zebra rows

### Parallel Solid Lines

```xml
<S
    T="..."
    C="..."
    W="..."
    LW="..."
    OB="..."
    OA="..."
    TEX="..."
    ST="..."
    SS="..."
    VT="..."
    VS="..." />
```

Settings:

- color
- total width
- line width
- offset before
- offset after
- texture density
- cracks density/scale
- voids density/scale

### Parallel Dashed Lines

Parallel Dashed Lines has every Parallel Solid Lines setting plus:

```xml
DL="..."
SL="..."
```

Additional settings:

- dash length
- space length

### Ladder

Ladder has the Parallel Solid Lines settings plus:

```xml
DL="..."
SL="..."
```

Additional settings:

- dash length
- space length

### Chess Board

```xml
<S
    T="..."
    C="..."
    W="..."
    OB="..."
    OA="..."
    SS="..."
    LC="..."
    I="..."
    TEX="..."
    ST="..."
    VT="..."
    VS="..." />
```

Settings:

- color
- square side
- line count
- offset before
- offset after
- invert
- texture density
- cracks density/scale
- voids density/scale

Note: `SS` is reused here as square side. For effect styles, `SS` is also the
cracks scale attribute. Because this style serializes both through IMT's own
property system, we should treat the full `<S>` XML as opaque instead of trying
to remodel it manually.

### Decal

```xml
<S
    T="..."
    W="..."
    OB="..."
    OA="..."
    DCL="..."
    DC="..."
    TLX="..."
    TLY="..."
    A="..." />
```

Settings:

- decal asset
- decal color
- width
- tiling X
- tiling Y
- angle
- offset before
- offset after

## Recommended Full Crosswalk State Packet

The current `UpdateCrosswalkStyle` packet is close, because IMT stores almost
everything in the nested style XML and the packet already has border refs.

If we want one full-state action mirroring IMT's own save shape, add:

```csharp
public enum IMTActionType : byte
{
    UpdateCrosswalkState = 82 // example; append only
}

public class IMTActionCommand
{
    [ProtoMember(32)] public string CrosswalkXml;
}
```

Recommended packet:

```csharp
IMTActionCommand
{
    Type = IMTActionType.UpdateCrosswalkState,
    Scope,
    MarkingId,
    A,
    B,
    HasRightBorder,
    RightBorderA,
    RightBorderB,
    HasLeftBorder,
    LeftBorderA,
    LeftBorderB,
    CrosswalkXml,
    Version
}
```

For multiplayer safety, keep using endpoint refs for identity and borders. Treat
`CrosswalkXml` or `StyleXml` as opaque IMT XML.
