using System.Collections.Generic;
using ColossalFramework;
using IMT.Manager;
using UnityEngine;
using CSM.IMTSync.Commands;

namespace CSM.IMTSync.Services
{
    /// <summary>
    /// Per-sender presence state. Updated by IMTActionHandler.ApplySelectIntersection and consumed
    /// by the render postfix on IntersectionMarkingTool.RenderOverlay to draw claim rings + the
    /// IMT-style entrance-point overlay so the remote view matches what the local player sees.
    ///
    /// Single-claim-per-player: each sender has at most one active claim. Switching intersections
    /// overwrites the previous entry; explicit deselect (MarkingId=0) removes it.
    /// </summary>
    internal static class PresenceStore
    {
        public struct Claim
        {
            public MarkingScope Scope;
            public ushort MarkingId;
            public string Name;
            public Color RingColor;
            public Marking Marking; // resolved on receive so the render hot-path doesn't relookup
        }

        public struct Preview
        {
            public MarkingScope Scope;
            public ushort MarkingId;
            public string Name;
            public Color Color;
            public Marking Marking;
            public ToolPreviewKind Kind;
            public PointRef A;
            public PointRef B;
            public bool HasB;
            public Vector3 CursorPosition;
            public FillerVertexRef[] Vertices;
            public bool HasHoverVertex;
            public FillerVertexRef HoverVertex;
        }

        private static readonly Dictionary<int, Claim> _claims = new Dictionary<int, Claim>();
        private static readonly Dictionary<int, Preview> _previews = new Dictionary<int, Preview>();
        private static readonly object _lock = new object();

        // Stable, distinguishable palette indexed by senderId. Keeps each player's color
        // consistent across the session.
        private static readonly Color[] _palette = new Color[]
        {
            new Color(0.95f, 0.40f, 0.40f), // red
            new Color(0.40f, 0.85f, 0.50f), // green
            new Color(0.40f, 0.65f, 0.95f), // blue
            new Color(0.95f, 0.80f, 0.30f), // gold
            new Color(0.80f, 0.50f, 0.95f), // purple
            new Color(0.40f, 0.90f, 0.85f), // teal
            new Color(0.95f, 0.55f, 0.30f), // orange
            new Color(0.80f, 0.85f, 0.50f), // chartreuse
        };

        public static void Update(int senderId, MarkingScope scope, ushort markingId, string name, Marking marking)
        {
            lock (_lock)
            {
                if (markingId == 0)
                {
                    _claims.Remove(senderId);
                    _previews.Remove(senderId);
                    return;
                }
                _claims[senderId] = new Claim
                {
                    Scope = scope,
                    MarkingId = markingId,
                    Name = name ?? "",
                    RingColor = ColorFor(senderId),
                    Marking = marking,
                };
            }
        }

        public static void UpdatePreview(int senderId, IMTActionCommand cmd, Marking marking)
        {
            lock (_lock)
            {
                if (cmd.PreviewKind == ToolPreviewKind.None || cmd.MarkingId == 0)
                {
                    _previews.Remove(senderId);
                    return;
                }

                _previews[senderId] = new Preview
                {
                    Scope = cmd.Scope,
                    MarkingId = cmd.MarkingId,
                    Name = cmd.ClaimantName ?? "",
                    Color = ColorFor(senderId),
                    Marking = marking,
                    Kind = cmd.PreviewKind,
                    A = cmd.A,
                    B = cmd.B,
                    HasB = cmd.HasB,
                    CursorPosition = new Vector3(cmd.CursorX, cmd.CursorY, cmd.CursorZ),
                    Vertices = cmd.Vertices,
                    HasHoverVertex = cmd.HasHoverVertex,
                    HoverVertex = cmd.HoverVertex,
                };
            }
        }

        public static void Clear(int senderId)
        {
            lock (_lock)
            {
                _claims.Remove(senderId);
                _previews.Remove(senderId);
            }
        }

        public static List<Claim> Snapshot()
        {
            lock (_lock) { return new List<Claim>(_claims.Values); }
        }

        public static List<Preview> PreviewSnapshot()
        {
            lock (_lock) { return new List<Preview>(_previews.Values); }
        }

        public static int PreviewCount()
        {
            lock (_lock) { return _previews.Count; }
        }

        private static Color ColorFor(int senderId)
        {
            return _palette[((senderId % _palette.Length) + _palette.Length) % _palette.Length];
        }

        /// <summary>
        /// World-space center of an intersection (Node) or segment, looked up via CS's
        /// NetManager directly so the receiver doesn't have to GetOrCreateMarking() for every
        /// node another player happens to visit.
        /// </summary>
        public static bool TryGetWorldPosition(MarkingScope scope, ushort id, out Vector3 position)
        {
            position = Vector3.zero;
            if (id == 0) return false;
            var nm = Singleton<NetManager>.instance;
            if (nm == null) return false;

            if (scope == MarkingScope.Node)
            {
                if (id >= nm.m_nodes.m_buffer.Length) return false;
                var node = nm.m_nodes.m_buffer[id];
                if ((node.m_flags & NetNode.Flags.Created) == 0) return false;
                position = node.m_position;
                return true;
            }
            // Segment scope
            if (id >= nm.m_segments.m_buffer.Length) return false;
            var seg = nm.m_segments.m_buffer[id];
            if ((seg.m_flags & NetSegment.Flags.Created) == 0) return false;
            position = seg.m_middlePosition;
            return true;
        }
    }
}
