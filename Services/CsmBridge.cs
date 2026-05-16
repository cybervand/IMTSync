using System;
using CSM.API.Commands;
using CSM.API.Helpers;

namespace CSM.IMTSync.Services
{
    /// <summary>
    /// Thin facade over CSM.API for sending commands and managing the IgnoreHelper scope.
    ///
    /// SendToAll is the static Action&lt;CommandBase&gt; field on CSM.API.Commands.Command, plumbed in
    /// by CSM at runtime via Command.ConnectToCSM(...). It will be null until CSM has connected.
    ///
    /// IgnoreHelper.Instance is ThreadLocal&lt;&gt;; StartIgnore is ref-counted via _ignoreAll int,
    /// so nested using-blocks compose correctly.
    /// </summary>
    internal static class CsmBridge
    {
        public static void SendToAll(CommandBase cmd)
        {
            if (cmd == null) return;
            var sender = Command.SendToAll;
            if (sender == null)
            {
                Log.Warn("SendToAll called but Command.SendToAll is null - CSM not connected yet?");
                return;
            }
            try { sender(cmd); }
            catch (Exception ex) { Log.Error("SendToAll threw: " + ex); }
        }

        public static bool IsIgnoring()
        {
            var inst = IgnoreHelper.Instance;
            return inst != null && inst.IsIgnored();
        }

        /// <summary>
        /// Wraps IgnoreHelper.StartIgnore / EndIgnore for use in a using-statement.
        /// While the scope is active, our Harmony postfixes will short-circuit on IsIgnoring()
        /// and not re-broadcast the action we just received from a remote client.
        /// </summary>
        public static IDisposable StartIgnore() => new IgnoreScope();

        private sealed class IgnoreScope : IDisposable
        {
            private bool _disposed;
            public IgnoreScope() { IgnoreHelper.Instance.StartIgnore(); }
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                try { IgnoreHelper.Instance.EndIgnore(); }
                catch (Exception ex) { Log.Error("EndIgnore threw: " + ex); }
            }
        }
    }
}
