using CSM.IMTSync.Commands;
using IMT.Manager;
using IMT.Tools;
using IMT.UI.Panel;
using ModsCommon;
using UnityEngine;

namespace CSM.IMTSync.Services
{
    internal static class ImtUiRefresh
    {
        private const float MinRefreshInterval = 0.15f;
        private static bool _pending;
        private static MarkingScope _scope;
        private static ushort _markingId;
        private static float _lastRefresh;

        public static void Request(MarkingScope scope, ushort markingId)
        {
            if (markingId == 0) return;
            _scope = scope;
            _markingId = markingId;
            _pending = true;
        }

        public static void RequestCurrentPanel()
        {
            try
            {
                var marking = SingletonTool<IntersectionMarkingTool>.Instance?.Marking;
                if (marking is NodeMarking)
                    Request(MarkingScope.Node, marking.Id);
                else if (marking is SegmentMarking)
                    Request(MarkingScope.Segment, marking.Id);
            }
            catch { }
        }

        public static void FlushReady()
        {
            if (!_pending) return;

            var now = Time.realtimeSinceStartup;
            if (now - _lastRefresh < MinRefreshInterval) return;

            try
            {
                var tool = SingletonTool<IntersectionMarkingTool>.Instance;
                var marking = tool?.Marking;
                if (!IsSameMarking(marking, _scope, _markingId)) return;

                var panel = SingletonItem<IntersectionMarkingToolPanel>.Instance;
                if (panel == null || panel.Marking != marking) return;

                panel.UpdatePanel();
                _lastRefresh = now;
            }
            catch (System.Exception ex)
            {
                Log.Warn("IMT UI refresh threw: " + ex.Message);
            }
            finally
            {
                _pending = false;
            }
        }

        private static bool IsSameMarking(Marking marking, MarkingScope scope, ushort markingId)
        {
            if (marking == null || marking.Id != markingId) return false;
            if (scope == MarkingScope.Node) return marking is NodeMarking;
            if (scope == MarkingScope.Segment) return marking is SegmentMarking;
            return false;
        }
    }
}
