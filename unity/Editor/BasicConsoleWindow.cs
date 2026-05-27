using UnityEditor;
using UnityEngine;

namespace FullBasic.Editor
{
    /// <summary>
    /// Interactive Full BASIC console as a Unity EditorWindow. Edit source on
    /// the top, click Run (or press Ctrl/Cmd + Enter), see output below.
    /// Source persists across editor sessions via <see cref="EditorPrefs"/>.
    /// </summary>
    public sealed class BasicConsoleWindow : EditorWindow
    {
        private const string PrefKeySource = "FullBasic.Console.Source";
        private const string DefaultSource =
            "REM Full BASIC — Unity console\n" +
            "PRINT \"hello from BASIC\"\n" +
            "FOR I = 1 TO 5\n" +
            "  PRINT I, I * I, I ^ 3\n" +
            "NEXT I\n";

        private string _source = string.Empty;
        private string _output = string.Empty;
        private string _diagnostics = string.Empty;
        private Vector2 _sourceScroll;
        private Vector2 _outputScroll;

        // --- Menu entry / opening helpers ---------------------------------

        [MenuItem("Window/Full BASIC/Console", priority = 1)]
        public static void Open()
        {
            var window = GetWindow<BasicConsoleWindow>("Full BASIC");
            window.minSize = new Vector2(420, 360);
            window.Show();
        }

        /// <summary>
        /// Open the console (creating if needed) and replace the source with
        /// <paramref name="source"/>. Used by the asset inspector's "Open in
        /// Console" button.
        /// </summary>
        public static void OpenWithSource(string source)
        {
            var window = GetWindow<BasicConsoleWindow>("Full BASIC");
            window.minSize = new Vector2(420, 360);
            window._source = source ?? string.Empty;
            window._output = string.Empty;
            window._diagnostics = string.Empty;
            window.Show();
            window.Focus();
        }

        // --- Lifecycle ----------------------------------------------------

        private void OnEnable()
        {
            _source = EditorPrefs.GetString(PrefKeySource, DefaultSource);
        }

        private void OnDisable()
        {
            EditorPrefs.SetString(PrefKeySource, _source);
        }

        // --- Layout -------------------------------------------------------

        private void OnGUI()
        {
            HandleHotkeys();
            DrawToolbar();
            DrawSourcePane();
            DrawOutputPane();
        }

        private void HandleHotkeys()
        {
            var e = Event.current;
            if (e.type == EventType.KeyDown
                && e.keyCode == KeyCode.Return
                && (e.control || e.command))
            {
                RunCode();
                e.Use();
            }
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            GUI.backgroundColor = new Color(0.45f, 0.8f, 0.45f);
            if (GUILayout.Button("▶  Run   (Ctrl+Enter)", EditorStyles.toolbarButton, GUILayout.Width(170)))
            {
                RunCode();
            }
            GUI.backgroundColor = Color.white;

            if (GUILayout.Button("Clear output", EditorStyles.toolbarButton))
            {
                _output = string.Empty;
                _diagnostics = string.Empty;
            }
            if (GUILayout.Button("Reset source", EditorStyles.toolbarButton))
            {
                if (EditorUtility.DisplayDialog(
                    "Reset source",
                    "Replace the current console source with the default template?",
                    "Reset", "Cancel"))
                {
                    _source = DefaultSource;
                }
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Docs", EditorStyles.toolbarButton, GUILayout.Width(50)))
            {
                Application.OpenURL("https://github.com/OWNER/REPO");
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawSourcePane()
        {
            EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
            var sourceHeight = Mathf.Max(120f, position.height * 0.45f);
            _sourceScroll = EditorGUILayout.BeginScrollView(_sourceScroll, GUILayout.Height(sourceHeight));
            _source = EditorGUILayout.TextArea(_source, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private void DrawOutputPane()
        {
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            _outputScroll = EditorGUILayout.BeginScrollView(_outputScroll, GUILayout.ExpandHeight(true));
            EditorGUILayout.SelectableLabel(
                string.IsNullOrEmpty(_output) ? "(run a program to see output)" : _output,
                EditorStyles.textArea,
                GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();

            if (!string.IsNullOrEmpty(_diagnostics))
            {
                var hasError = _diagnostics.IndexOf("error", System.StringComparison.OrdinalIgnoreCase) >= 0;
                EditorGUILayout.HelpBox(_diagnostics, hasError ? MessageType.Error : MessageType.Warning);
            }
        }

        // --- Execution ----------------------------------------------------

        private void RunCode()
        {
            try
            {
                var result = BasicEngine.Run(_source, out string output);
                _output = output;
                _diagnostics = string.Join("\n", result.Diagnostics);
                if (result.ExitCode != 0 && string.IsNullOrEmpty(_diagnostics))
                {
                    _diagnostics = $"Program exited with code {result.ExitCode}.";
                }
            }
            catch (System.Exception ex)
            {
                _output = string.Empty;
                _diagnostics = $"Runtime exception: {ex.Message}";
            }
            Repaint();
        }
    }
}
