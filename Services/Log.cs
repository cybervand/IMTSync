using System;
using System.Collections.Generic;
using System.IO;
using CSM.IMTSync.Mod;

namespace CSM.IMTSync.Services
{
    /// <summary>
    /// Simple file logger writing to Cities_Data\Logs\CSM.IMTSync.log.
    ///
    /// IMPORTANT: All initialization is lazy and try/catch-wrapped. A logger that throws from
    /// its static constructor would permanently brick the type (TypeInitializationException
    /// on every subsequent reference) and take the whole mod down with it.
    /// </summary>
    internal static class Log
    {
        private static readonly object _gate = new object();
        private static string _logPath;       // resolved on first write
        private static bool _resolveAttempted;
        private static bool _resolveOk;
        private static bool _truncatedThisSession;  // first write per game session truncates the file

        // In-memory ring buffer of the most recent log lines, exposed to the in-game overlay.
        private const int RingCapacity = 200;
        private static readonly LinkedList<string> _ring = new LinkedList<string>();

        /// <summary>Snapshot of the most recent log lines (oldest first). Thread-safe copy.</summary>
        public static string[] RecentLines()
        {
            lock (_gate)
            {
                var arr = new string[_ring.Count];
                int i = 0;
                foreach (var s in _ring) arr[i++] = s;
                return arr;
            }
        }

        public static void Info(string message)  => Write("INFO",  message);
        public static void Warn(string message)  => Write("WARN",  message);
        public static void Error(string message) => Write("ERROR", message);
        public static void Error(Exception ex)   => Write("ERROR", ex == null ? "(null exception)" : ex.ToString());

        private static void Write(string level, string message)
        {
            string line;
            try
            {
                line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {ModMetadata.LogTag} {message}";
            }
            catch { return; } // never throw out of Log.*

            // Always mirror to Unity's output_log first so we have a fallback if file write fails
            try { UnityEngine.Debug.Log(line); } catch { }

            // Append to in-memory ring buffer for the in-game overlay
            lock (_gate)
            {
                _ring.AddLast(line);
                while (_ring.Count > RingCapacity) _ring.RemoveFirst();
            }

            lock (_gate)
            {
                if (!EnsurePath()) return;
                try
                {
                    if (!_truncatedThisSession)
                    {
                        // Fresh log per game session; previous session's contents discarded.
                        File.WriteAllText(_logPath, line + Environment.NewLine);
                        _truncatedThisSession = true;
                    }
                    else
                    {
                        File.AppendAllText(_logPath, line + Environment.NewLine);
                    }
                }
                catch { /* swallow */ }
            }
        }

        /// <summary>Returns true if _logPath is now usable. Only attempts resolution once.</summary>
        private static bool EnsurePath()
        {
            if (_resolveAttempted) return _resolveOk;
            _resolveAttempted = true;

            try
            {
                // UnityEngine.Application.dataPath returns the path to the Cities_Data folder.
                // For Cities Skylines: M:\Games\Cities Skylines\Cities_Data
                string dataPath = UnityEngine.Application.dataPath;
                if (string.IsNullOrEmpty(dataPath)) return false;

                string logsDir = Path.Combine(dataPath, "Logs");
                if (!Directory.Exists(logsDir)) Directory.CreateDirectory(logsDir);

                _logPath = Path.Combine(logsDir, "CSM.IMTSync.log");
                _resolveOk = true;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
