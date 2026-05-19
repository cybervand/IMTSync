namespace CSM.IMTSync.Commands
{
    /// <summary>
    /// Discriminator for IMTActionCommand. New action types are added as Phase 2/3/4 land.
    /// Numeric values are the wire format - DO NOT renumber existing entries; only append.
    /// </summary>
    public enum IMTActionType : byte
    {
        // Phase 1 - vertical slice
        ClearMarking = 0,

        // Phase 2 - Add/Remove low-frequency mutations (placeholders, will be filled in)
        AddRegularLine = 10,
        AddNormalLine = 11,
        AddStopLine = 12,
        AddLaneLine = 13,
        AddCrosswalk = 14,
        AddFiller = 15,

        RemoveRegularLine = 20,
        RemoveNormalLine = 21,
        RemoveStopLine = 22,
        RemoveLaneLine = 23,
        RemoveCrosswalk = 24,
        RemoveFiller = 25,

        ResetOffsets = 30,
        SetPointOffset = 31,

        // Phase 2.6 - style edits (color / width / dash / pattern / etc.)
        UpdateLineStyle = 40,      // identifies the line via A+B; first rule only (v1)
        UpdateFillerStyle = 41,    // identifies the filler via Vertices fingerprint
        UpdateCrosswalkStyle = 42, // identifies the crosswalk via A+B (its CrosswalkLine endpoints)

        // Tier 1 (presence) - transient signals, not versioned by EditClock
        SelectIntersection = 50,   // user picked an intersection; MarkingId=0 means "deselected"
        CursorPresence = 60,       // local cursor world position; throttled at 10 Hz from sender

        // Mid-session state push (Bug 2 fix) - new clients request state, host responds with snapshots
        MarkingSnapshot = 70,      // full Marking.ToXml() chunk for one marking; receiver applies via FromXml
        SnapshotRequest = 71,      // "send me the current state" - server responds with MarkingSnapshot per marking

        // Transient IMT tool-mode overlay: selected point, hover point, cursor, partial filler contour.
        ToolPreview = 80,

        // Full per-object state pushes. These carry IMT's own XML for the edited object and are
        // preferred over the older style-only packets when available.
        UpdateLineState = 81,
        UpdateCrosswalkState = 82,

        // Saved IMT template lists (Templates and Presets tabs). These are not part of a marking
        // snapshot; they live in IMT's local template managers.
        UpsertStyleTemplate = 83,
        DeleteStyleTemplate = 84,
        UpsertIntersectionTemplate = 85,
        DeleteIntersectionTemplate = 86,
    }

    public enum ToolPreviewKind : byte
    {
        None = 0,
        Line = 1,
        Crosswalk = 2,
        Filler = 3,
        DragPoint = 4,
    }

    /// <summary>
    /// Whether the marking ID refers to a node or a segment.
    /// Mirrors IMT.API.MarkingType but kept as a separate byte to keep the wire format
    /// independent of any future IMT API renumbering.
    /// </summary>
    public enum MarkingScope : byte
    {
        Node = 0,
        Segment = 1,
    }
}
