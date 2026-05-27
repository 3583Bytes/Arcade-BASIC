using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace ArcadeBasic.Editor
{
    /// <summary>
    /// Custom inspector for <c>.bas</c> files. Shows a source preview, a
    /// <b>Run</b> button that executes the program through
    /// <see cref="BasicEngine"/>, and the captured stdout / diagnostics.
    /// </summary>
    [CustomEditor(typeof(BasicScriptedImporter))]
    public sealed class BasicAssetInspector : ScriptedImporterEditor
    {
        private string _cachedSource = string.Empty;
        private string _output = string.Empty;
        private string _diagnostics = string.Empty;
        private Vector2 _previewScroll;
        private Vector2 _outputScroll;
        private bool _showPreview = true;

        protected override bool needsApplyRevert => false;

        public override void OnInspectorGUI()
        {
            var importer = (BasicScriptedImporter)target;
            EnsureSource(importer.assetPath);

            // Header bar.
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Arcade BASIC", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Refresh", EditorStyles.miniButton, GUILayout.Width(60)))
            {
                LoadSource(importer.assetPath);
            }
            if (GUILayout.Button("Open in Console", EditorStyles.miniButton, GUILayout.Width(120)))
            {
                BasicConsoleWindow.OpenWithSource(_cachedSource);
            }
            EditorGUILayout.EndHorizontal();

            // Source preview.
            _showPreview = EditorGUILayout.Foldout(_showPreview, "Source preview", true);
            if (_showPreview)
            {
                _previewScroll = EditorGUILayout.BeginScrollView(_previewScroll, GUILayout.MaxHeight(220));
                EditorGUILayout.SelectableLabel(_cachedSource, EditorStyles.textArea, GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();
            }

            EditorGUILayout.Space();

            // Run button.
            GUI.backgroundColor = new Color(0.45f, 0.8f, 0.45f);
            if (GUILayout.Button("▶  Run", GUILayout.Height(28)))
            {
                RunProgram(_cachedSource);
            }
            GUI.backgroundColor = Color.white;

            // Output.
            if (!string.IsNullOrEmpty(_output) || !string.IsNullOrEmpty(_diagnostics))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
                _outputScroll = EditorGUILayout.BeginScrollView(_outputScroll, GUILayout.MaxHeight(220));
                EditorGUILayout.SelectableLabel(
                    string.IsNullOrEmpty(_output) ? "(no output)" : _output,
                    EditorStyles.textArea,
                    GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();

                if (!string.IsNullOrEmpty(_diagnostics))
                {
                    var hasError = _diagnostics.IndexOf("error", System.StringComparison.OrdinalIgnoreCase) >= 0;
                    EditorGUILayout.HelpBox(_diagnostics, hasError ? MessageType.Error : MessageType.Warning);
                }
            }

            ApplyRevertGUI();
        }

        private void EnsureSource(string assetPath)
        {
            if (string.IsNullOrEmpty(_cachedSource)) LoadSource(assetPath);
        }

        private void LoadSource(string assetPath)
        {
            _cachedSource = File.Exists(assetPath)
                ? File.ReadAllText(assetPath)
                : "(file not found on disk)";
        }

        private void RunProgram(string source)
        {
            try
            {
                var result = BasicEngine.Run(source, out string output);
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
        }
    }
}
