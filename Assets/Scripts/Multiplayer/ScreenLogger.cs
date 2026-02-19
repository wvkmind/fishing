using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MultiplayerFishing
{
    /// <summary>
    /// On-screen debug log overlay + file logging for build clients.
    /// Shows recent log entries on screen (toggle with F1).
    /// Also writes all logs to a timestamped file next to the executable.
    ///
    /// Add to a GameObject in the Lobby scene (or any persistent object).
    /// Uses DontDestroyOnLoad so it survives scene transitions.
    /// </summary>
    public class ScreenLogger : MonoBehaviour
    {
        [Header("Display")]
        [SerializeField] private int _maxLines = 30;
        [SerializeField] private int _fontSize = 14;
        [SerializeField] private KeyCode _toggleKey = KeyCode.F1;

        // Filter: only show our multiplayer logs (lines containing these prefixes)
        private static readonly string[] LogPrefixes = {
            "[NFC]", "[StateMachine]", "[Presenter]", "[NetRod]", "[NetFloat]"
        };

        private readonly List<string> _lines = new List<string>();
        private Vector2 _scrollPos;
        private bool _visible = true;
        private GUIStyle _style;
        private StreamWriter _fileWriter;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            Application.runInBackground = true;

            Application.logMessageReceived += OnLogMessage;
            InitFileLog();
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= OnLogMessage;
            _fileWriter?.Flush();
            _fileWriter?.Close();
        }

        private void InitFileLog()
        {
            try
            {
                string dir = Application.persistentDataPath;
                string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string path = Path.Combine(dir, $"fishing_log_{timestamp}.txt");
                _fileWriter = new StreamWriter(path, false) { AutoFlush = true };
                Debug.Log($"[ScreenLogger] File log: {path}");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[ScreenLogger] Failed to create log file: {e.Message}");
            }
        }

        private void OnLogMessage(string message, string stackTrace, LogType type)
        {
            // Write everything to file
            _fileWriter?.WriteLine($"[{System.DateTime.Now:HH:mm:ss.fff}][{type}] {message}");

            // Only show our prefixed logs on screen
            bool match = false;
            for (int i = 0; i < LogPrefixes.Length; i++)
            {
                if (message.Contains(LogPrefixes[i]))
                {
                    match = true;
                    break;
                }
            }
            if (!match && type != LogType.Error && type != LogType.Exception) return;

            string prefix = type == LogType.Error || type == LogType.Exception ? "<color=red>" : "<color=white>";
            _lines.Add($"{prefix}[{System.DateTime.Now:HH:mm:ss}] {message}</color>");

            if (_lines.Count > _maxLines * 2)
                _lines.RemoveRange(0, _lines.Count - _maxLines);
        }

        private void Update()
        {
            if (Input.GetKeyDown(_toggleKey))
                _visible = !_visible;
        }

        private void OnGUI()
        {
            if (!_visible) return;

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = _fontSize,
                    richText = true,
                    wordWrap = false
                };
            }

            float w = Screen.width * 0.5f;
            float h = Screen.height * 0.4f;
            Rect area = new Rect(4, Screen.height - h - 4, w, h);

            GUI.Box(area, "");

            // Semi-transparent background
            GUI.color = new Color(0, 0, 0, 0.7f);
            GUI.DrawTexture(area, Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUILayout.BeginArea(area);

            int start = Mathf.Max(0, _lines.Count - _maxLines);
            _scrollPos = GUILayout.BeginScrollView(
                new Vector2(0, float.MaxValue), // auto-scroll to bottom
                GUILayout.Width(w), GUILayout.Height(h));

            for (int i = start; i < _lines.Count; i++)
                GUILayout.Label(_lines[i], _style);

            GUILayout.EndScrollView();
            GUILayout.EndArea();

            // Toggle hint
            GUI.Label(new Rect(4, Screen.height - h - 22, 200, 20),
                "<color=yellow>F1 toggle log</color>", _style);
        }
    }
}
