using System.Collections.Generic;
using CSM.API.Commands;
using CSM.IMTSync.Commands;
using IMT.Manager;
using ModsCommon;
using UnityEngine;

namespace CSM.IMTSync.Services
{
    internal static class SnapshotDeferral
    {
        private static readonly Dictionary<string, PendingSnapshot> _pending = new Dictionary<string, PendingSnapshot>();
        private static readonly object _lock = new object();
        private static float _lastFlushAttempt;

        private struct PendingSnapshot
        {
            public MarkingScope Scope;
            public ushort MarkingId;
        }

        public static void Request(MarkingScope scope, ushort markingId, string reason)
        {
            if (markingId == 0) return;
            if (!IsServer()) return;

            lock (_lock)
            {
                _pending[Key(scope, markingId)] = new PendingSnapshot
                {
                    Scope = scope,
                    MarkingId = markingId,
                };
            }

            Log.Info($"Deferred MarkingSnapshot for {scope} {markingId}" + (string.IsNullOrEmpty(reason) ? "." : $" ({reason})."));
        }

        public static void FlushReady()
        {
            if (!IsServer()) return;

            var now = Time.realtimeSinceStartup;
            if (now - _lastFlushAttempt < 0.5f) return;
            _lastFlushAttempt = now;

            List<PendingSnapshot> ready = null;
            lock (_lock)
            {
                if (_pending.Count == 0) return;

                var keys = new List<string>();
                foreach (var pair in _pending)
                {
                    var pending = pair.Value;
                    if (PresenceStore.HasActiveConstruction(pending.Scope, pending.MarkingId))
                        continue;

                    if (ready == null) ready = new List<PendingSnapshot>();
                    ready.Add(pending);
                    keys.Add(pair.Key);
                }

                for (var i = 0; i < keys.Count; i++)
                    _pending.Remove(keys[i]);
            }

            if (ready == null) return;
            for (var i = 0; i < ready.Count; i++)
                SendFreshSnapshot(ready[i]);
        }

        private static void SendFreshSnapshot(PendingSnapshot pending)
        {
            try
            {
                var marking = pending.Scope == MarkingScope.Node
                    ? (Marking)SingletonManager<NodeMarkingManager>.Instance.GetOrCreateMarking(pending.MarkingId)
                    : (Marking)SingletonManager<SegmentMarkingManager>.Instance.GetOrCreateMarking(pending.MarkingId);

                var snap = MarkingSnapshotter.BuildSnapshot(marking);
                if (snap == null) return;

                Log.Info($"Deferred MarkingSnapshot for {pending.Scope} {pending.MarkingId} is quiet - broadcasting fresh snapshot.");
                CsmBridge.SendToAll(snap);
            }
            catch (System.Exception ex)
            {
                Log.Warn($"Deferred MarkingSnapshot for {pending.Scope} {pending.MarkingId} threw: " + ex.Message);
            }
        }

        private static bool IsServer()
        {
            try
            {
                var mm = global::CSM.Networking.MultiplayerManager.Instance;
                return mm != null && mm.CurrentRole == MultiplayerRole.Server;
            }
            catch { return false; }
        }

        private static string Key(MarkingScope scope, ushort markingId) => scope + ":" + markingId;
    }
}
