using System.Collections.Generic;
using UnityEngine;
using CSM.IMTSync.Commands;

namespace CSM.IMTSync.Services
{
    /// <summary>
    /// Trailing-edge debouncer for high-frequency style edits.
    ///
    /// IMT's property panels (color picker, width slider, dash spinner, etc.) fire the owner's
    /// Changed-callback per-tick during slider drags — potentially 60 events per second per
    /// active drag. Without throttling, this floods the network. Without trailing-edge handling,
    /// throttling alone could lose the FINAL value after activity stops.
    ///
    /// Pattern:
    /// 1. Each patch fire calls <see cref="MarkDirty"/> with the latest command for that element.
    /// 2. The command supersedes any prior pending command for the same element (latest wins).
    /// 3. The per-frame <see cref="FlushReady"/> hook (called from RenderOverlay postfix) broadcasts
    ///    pending commands once <see cref="DebounceSeconds"/> has elapsed since the last change.
    ///
    /// Result: 60Hz slider drags become ~10Hz broadcasts; the user's final value (after they stop
    /// dragging) always lands on the receiver after a brief settle period.
    ///
    /// Note: <see cref="CsmBridge.SendToAll(IMTActionCommand)"/> still version-stamps via
    /// <see cref="EditClock.NextVersion"/> at flush time, so Lamport ordering is preserved.
    /// </summary>
    internal static class StyleEditDebouncer
    {
        private const float DebounceSeconds = 0.1f; // ~10 Hz max effective rate

        private struct Pending
        {
            public float LastChangeTime;
            public IMTActionCommand Cmd;
        }

        private static readonly Dictionary<string, Pending> _pending = new Dictionary<string, Pending>();
        private static readonly object _lock = new object();

        /// <summary>
        /// Record a change. Replaces any prior pending command for this element with the latest.
        /// </summary>
        public static void MarkDirty(string elementId, IMTActionCommand cmd)
        {
            if (string.IsNullOrEmpty(elementId) || cmd == null) return;
            lock (_lock)
            {
                _pending[elementId] = new Pending
                {
                    LastChangeTime = Time.realtimeSinceStartup,
                    Cmd = cmd,
                };
            }
        }

        /// <summary>
        /// Per-frame: broadcast any pending commands whose elements have settled
        /// (no further changes for &gt;= DebounceSeconds).
        /// </summary>
        public static void FlushReady()
        {
            float now = Time.realtimeSinceStartup;
            List<IMTActionCommand> toSend = null;

            lock (_lock)
            {
                if (_pending.Count == 0) return;
                List<string> ready = null;
                foreach (var kv in _pending)
                {
                    if (now - kv.Value.LastChangeTime >= DebounceSeconds)
                    {
                        if (ready == null) ready = new List<string>();
                        ready.Add(kv.Key);
                    }
                }
                if (ready == null) return;
                toSend = new List<IMTActionCommand>(ready.Count);
                foreach (var k in ready)
                {
                    toSend.Add(_pending[k].Cmd);
                    _pending.Remove(k);
                }
            }

            for (int i = 0; i < toSend.Count; i++)
                CsmBridge.SendToAll(toSend[i]);
        }
    }
}
