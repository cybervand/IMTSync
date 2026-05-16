using System;
using UnityEngine;

namespace CSM.IMTSync.Services
{
    /// <summary>
    /// Toggleable in-game overlay that shows the last N log lines from <see cref="Log"/>.
    /// Toggle hotkey: Ctrl+Shift+L.
    /// Survives map loads via DontDestroyOnLoad.
    /// </summary>
    public class LogOverlay : MonoBehaviour
    {
        private static LogOverlay _instance;

        private bool _visible;
        private Vector2 _scroll;
        private GUIStyle _boxStyle;
        private GUIStyle _labelStyle;
        private const int Width = 700;
        private const int Height = 360;

        public static void EnsureCreated()
        {
            if (_instance != null) return;
            try
            {
                var go = new GameObject("CSM.IMTSync.LogOverlay");
                _instance = go.AddComponent<LogOverlay>();
                DontDestroyOnLoad(go);
                Log.Info("LogOverlay created (Ctrl+Shift+L to toggle).");
            }
            catch (Exception ex) { Log.Error("LogOverlay.EnsureCreated threw: " + ex); }
        }

        public static void Destroy()
        {
            if (_instance == null) return;
            try { UnityEngine.Object.Destroy(_instance.gameObject); }
            catch { }
            _instance = null;
        }

        private void Update()
        {
            // Toggle with Ctrl+Shift+L. Use GetKeyDown so we don't toggle every frame the keys are held.
            try
            {
                if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                 && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                 && Input.GetKeyDown(KeyCode.L))
                {
                    _visible = !_visible;
                    if (_visible) _scroll = new Vector2(0, float.MaxValue); // jump to bottom
                }
            }
            catch { /* swallow Input errors during scene transitions */ }
        }

        private void OnGUI()
        {
            if (!_visible) return;
            EnsureStyles();

            var screenW = Screen.width;
            var screenH = Screen.height;
            var rect = new Rect(screenW - Width - 10, screenH - Height - 10, Width, Height);

            GUI.Box(rect, "CSM.IMTSync log  -  Ctrl+Shift+L to hide", _boxStyle);

            var inner = new Rect(rect.x + 6, rect.y + 24, rect.width - 12, rect.height - 30);
            var lines = Log.RecentLines();
            var text = string.Join("\n", lines);

            // Compute content height. Use a generous per-line size.
            float lineHeight = _labelStyle.lineHeight > 0 ? _labelStyle.lineHeight : 14f;
            float contentHeight = Math.Max(inner.height, lines.Length * (lineHeight + 1));

            _scroll = GUI.BeginScrollView(inner, _scroll, new Rect(0, 0, inner.width - 16, contentHeight));
            GUI.Label(new Rect(0, 0, inner.width - 16, contentHeight), text, _labelStyle);
            GUI.EndScrollView();
        }

        private void EnsureStyles()
        {
            if (_boxStyle == null)
            {
                _boxStyle = new GUIStyle(GUI.skin.box);
                _boxStyle.normal.textColor = Color.white;
                _boxStyle.alignment = TextAnchor.UpperLeft;
                _boxStyle.padding = new RectOffset(6, 6, 6, 6);
                _boxStyle.fontSize = 12;
            }
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label);
                _labelStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f);
                _labelStyle.fontSize = 11;
                _labelStyle.wordWrap = false;
                _labelStyle.richText = false;
            }
        }
    }
}
