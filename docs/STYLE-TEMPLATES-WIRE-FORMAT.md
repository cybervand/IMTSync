# IMT Style Templates Wire Format

This document describes the sync path for IMT's Templates tab entries. These
are the saved style templates created from line, crosswalk, and filler editors,
such as "save as template" from a dashed line, asphalt filler, or zebra
crosswalk.

These templates are not stored inside one marking snapshot. IMT keeps them in
`StyleTemplateManager`, so CSM.IMTSync syncs them through the template manager
instead of through `Marking.ToXml()`.

## Source Hooks

CSM.IMTSync patches the generic IMT manager methods:

```csharp
TemplateManager<StyleTemplate>.AddTemplate(StyleTemplate template)
TemplateManager<StyleTemplate>.TemplateChanged(StyleTemplate template)
TemplateManager<StyleTemplate>.DeleteTemplate(StyleTemplate template)
```

These cover:

- creating a saved style template
- renaming or editing a saved style template
- deleting a saved style template
- saved templates for line styles
- saved templates for filler styles
- saved templates for crosswalk styles

Asset templates bundled by IMT are ignored. Only user-created templates are
broadcast.

## Packet Shape

Style template upsert:

```csharp
IMTActionCommand
{
    Type = IMTActionType.UpsertStyleTemplate,
    TemplateId,
    TemplateXml,
    Version
}
```

Style template delete:

```csharp
IMTActionCommand
{
    Type = IMTActionType.DeleteStyleTemplate,
    TemplateId,
    Version
}
```

Fields:

- `TemplateId`: the template's stable IMT `Guid` as a string
- `TemplateXml`: IMT's own serialized `StyleTemplate.ToXml()` payload
- `Version`: CSM.IMTSync's normal last-writer-wins Lamport version

The `TemplateId` is duplicated outside the XML so delete packets do not need to
send the whole template body.

## XML Shape

The style template XML is treated as opaque IMT data. The outer element is IMT's
template element and contains:

```xml
<T ...>
  <S ... />
</T>
```

The nested `<S ... />` is the same style XML already used by line, filler, and
crosswalk sync. That means decal asset names, colors, widths, tiling, cracks,
voids, offsets, dash settings, and other style-owned settings stay inside IMT's
native serializer.

Do not use IMT's style type value as a multiplayer identity. It is only safe as
part of the opaque XML payload that local IMT parses.

## Receive Flow

On receive:

1. Parse `TemplateXml` with the Mono-safe XML loader.
2. Rebuild the template with `StyleTemplate.FromXml(...)`.
3. Replace the local manager dictionary entry by `template.Id`.
4. Call `StyleTemplateManager.TemplateChanged(template)` so IMT saves its local
   template file.
5. Request a refresh of the currently open IMT panel.

Deletes parse `TemplateId`, find the local template by `Guid`, call
`StyleTemplateManager.DeleteTemplate(template)`, and refresh the panel.

The receive side runs inside `CsmBridge.StartIgnore()`, so local Harmony patches
do not re-broadcast the remote template update.

## Notes

This syncs the saved entries in the Templates tab. Applying one of those
templates to a line, filler, or crosswalk is still handled by the normal marking
object sync path:

- lines use full `LineXml`
- crosswalks use full `CrosswalkXml`
- fillers use the filler style packet and contour/filler-id matching

Default-template toggles are separate IMT manager state. The current packet
syncs the template itself, not the user's local default-template preference.
