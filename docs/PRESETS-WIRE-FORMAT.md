# IMT Presets Wire Format

This document describes the sync path for IMT's Presets tab. In IMT code these
saved entries are `IntersectionTemplate` objects managed by
`IntersectionTemplateManager`.

This is different from applying a preset to a live intersection. The saved
preset list lives outside any one marking, while applying a preset changes the
current marking and is handled by marking snapshot/full-state sync.

## Source Hooks

CSM.IMTSync patches the generic IMT manager methods:

```csharp
TemplateManager<IntersectionTemplate>.AddTemplate(IntersectionTemplate template)
TemplateManager<IntersectionTemplate>.TemplateChanged(IntersectionTemplate template)
TemplateManager<IntersectionTemplate>.DeleteTemplate(IntersectionTemplate template)
```

These cover:

- creating a new preset from the current marking
- renaming or editing a preset
- deleting a preset
- receiving preset changes made through IMT's preset editor

Asset presets bundled by IMT are ignored. Only user-created presets are
broadcast.

## Packet Shape

Preset upsert:

```csharp
IMTActionCommand
{
    Type = IMTActionType.UpsertIntersectionTemplate,
    TemplateId,
    TemplateXml,
    TemplatePreviewPng,
    Version
}
```

Preset delete:

```csharp
IMTActionCommand
{
    Type = IMTActionType.DeleteIntersectionTemplate,
    TemplateId,
    Version
}
```

Fields:

- `TemplateId`: the preset's stable IMT `Guid` as a string
- `TemplateXml`: IMT's own serialized `IntersectionTemplate.ToXml()` payload
- `TemplatePreviewPng`: the preset screenshot PNG saved by IMT beside the XML
- `Version`: CSM.IMTSync's normal last-writer-wins Lamport version

The `TemplateId` is duplicated outside the XML so delete packets can stay small.

## XML Shape

The preset XML is treated as opaque IMT data. IMT stores the intersection preset
as an `IntersectionTemplate` XML element containing the preset metadata and the
saved marking/template contents.

Example shape:

```xml
<T ...>
  <!-- IMT preset metadata -->
  <!-- saved marking/template data -->
</T>
```

CSM.IMTSync does not manually decode the lines, fillers, crosswalks, or style
settings inside this XML. It sends IMT's own preset XML and asks the receiver's
IMT install to parse it with `IntersectionTemplate.FromXml(...)`.

Because CSM multiplayer requires matching mods and assets, asset/style names
inside the preset should resolve the same way on every player.

## Receive Flow

On receive:

1. Parse `TemplateXml` with the Mono-safe XML loader.
2. Rebuild the preset with `IntersectionTemplate.FromXml(...)`.
3. Save `TemplatePreviewPng` directly to IMT's preset screenshot folder.
4. Replace the local manager dictionary entry by `template.Id`.
5. Call `IntersectionTemplateManager.TemplateChanged(template)` so IMT saves its
   local preset file.
6. Request a refresh of the currently open IMT panel.

The receive side deliberately does not call `Image.CreateTexture()` or
`Loader.SaveScreenshot(...)`. CSM can deliver commands from its networking /
simulation path, and creating Unity textures there can crash the game. The PNG
is written as a file and IMT can load the preview through its normal UI/load
flow. If the Presets tab is already open, CSM.IMTSync defers the preview texture
creation to the IMT overlay/UI path and refreshes the current panel afterward.

Deletes parse `TemplateId`, find the local preset by `Guid`, call
`IntersectionTemplateManager.DeleteTemplate(template)`, and refresh the panel.

The receive side runs inside `CsmBridge.StartIgnore()`, so local Harmony patches
do not re-broadcast the remote preset update.

## Applying Presets

When a user selects a preset and applies it to the current intersection, IMT
rewrites the marking state. That operation is separate from saved-list sync.

CSM.IMTSync already treats those apply/paste operations as marking state
changes. IMT may enter an order-preview mode where it repeatedly clears and
rebuilds the marking while the player rotates or remaps roads. CSM.IMTSync
suppresses those preview rebuilds and sends one final marking snapshot only when
the player applies the order mode.

In short:

- Presets tab list changes use `UpsertIntersectionTemplate` and
  `DeleteIntersectionTemplate`.
- Applying a preset to an intersection uses the existing marking snapshot path.

## Notes

Preset preview thumbnails are separate from `IntersectionTemplate.ToXml()`.
They are carried as `TemplatePreviewPng`; without that field, the preset data
can parse successfully while the Presets tab still lacks the screenshot file IMT
expects next to the saved XML.
