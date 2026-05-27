using System.IO;
using UnityEditor;
using UnityEditor.ProjectWindowCallback;
using UnityEngine;

namespace FullBasic.Editor
{
    /// <summary>
    /// Menu entries that surface Full BASIC in the editor:
    /// <list type="bullet">
    /// <item><c>Assets/Create/Full BASIC/...</c> — templates for new <c>.bas</c> files.</item>
    /// <item><c>Assets/Run Full BASIC Program</c> — context action on selected <c>.bas</c>.</item>
    /// <item><c>Window/Full BASIC/...</c> — console window, docs, conformance.</item>
    /// </list>
    /// </summary>
    public static class BasicMenuItems
    {
        // --- Asset templates ----------------------------------------------

        [MenuItem("Assets/Create/Full BASIC/Empty Program", priority = 81)]
        public static void CreateEmptyProgram()
        {
            CreateAsset("NewProgram.bas",
                "REM Full BASIC program\nPRINT \"hello\"\n");
        }

        [MenuItem("Assets/Create/Full BASIC/Hello World", priority = 82)]
        public static void CreateHelloWorld()
        {
            CreateAsset("HelloWorld.bas",
                "REM Classic hello-world\nPRINT \"hello, BASIC world\"\n");
        }

        [MenuItem("Assets/Create/Full BASIC/FOR Loop Demo", priority = 83)]
        public static void CreateForLoopDemo()
        {
            CreateAsset("ForLoopDemo.bas",
                "REM FOR loop demo — index, square, cube\n" +
                "FOR I = 1 TO 10\n" +
                "  PRINT I, I * I, I ^ 3\n" +
                "NEXT I\n");
        }

        [MenuItem("Assets/Create/Full BASIC/Function Demo", priority = 84)]
        public static void CreateFunctionDemo()
        {
            CreateAsset("FunctionDemo.bas",
                "REM FUNCTION + recursion (factorial)\n" +
                "FUNCTION fact(n)\n" +
                "  IF n <= 1 THEN\n" +
                "    LET fact = 1\n" +
                "  ELSE\n" +
                "    LET fact = n * fact(n - 1)\n" +
                "  END IF\n" +
                "END FUNCTION\n" +
                "\n" +
                "FOR I = 1 TO 10\n" +
                "  PRINT I, fact(I)\n" +
                "NEXT I\n");
        }

        // --- Context menu on a selected .bas asset ------------------------

        [MenuItem("Assets/Run Full BASIC Program", priority = 30)]
        public static void RunSelectedProgram()
        {
            var ta = Selection.activeObject as TextAsset;
            if (ta == null) return;

            var path = AssetDatabase.GetAssetPath(ta);
            string source;
            try
            {
                source = File.ReadAllText(path);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[FullBasic] cannot read {path}: {ex.Message}");
                return;
            }

            BasicConsoleWindow.OpenWithSource(source);
        }

        [MenuItem("Assets/Run Full BASIC Program", isValidateFunction: true)]
        public static bool ValidateRunSelectedProgram()
        {
            return Selection.activeObject is TextAsset
                && AssetDatabase.GetAssetPath(Selection.activeObject)
                    .EndsWith(".bas", System.StringComparison.OrdinalIgnoreCase);
        }

        // --- Window menu --------------------------------------------------

        [MenuItem("Window/Full BASIC/Documentation", priority = 100)]
        public static void OpenDocumentation()
        {
            Application.OpenURL("https://github.com/OWNER/REPO");
        }

        [MenuItem("Window/Full BASIC/Conformance notes", priority = 101)]
        public static void OpenConformance()
        {
            Application.OpenURL("https://github.com/OWNER/REPO/blob/main/docs/conformance.md");
        }

        [MenuItem("Window/Full BASIC/About", priority = 200)]
        public static void OpenAbout()
        {
            EditorUtility.DisplayDialog(
                "Full BASIC",
                "Full BASIC (ISO/IEC 10279:1991) interpreter for Unity.\n\n" +
                "• Window → Full BASIC → Console: interactive editor + runner.\n" +
                "• Assets → Create → Full BASIC: new program templates.\n" +
                "• Select a .bas asset and click Run in the Inspector.\n\n" +
                "Embed from code:\n" +
                "    var result = FullBasic.BasicEngine.Run(source, out var output);\n" +
                "    Debug.Log(output);",
                "OK");
        }

        // --- Helpers ------------------------------------------------------

        private static void CreateAsset(string defaultName, string content)
        {
            var endAction = ScriptableObject.CreateInstance<CreateBasicAssetEndAction>();
            endAction.Content = content;

            ProjectWindowUtil.StartNameEditingIfProjectWindowExists(
                instanceID: 0,
                endAction: endAction,
                pathName: defaultName,
                icon: EditorGUIUtility.IconContent("TextAsset Icon").image as Texture2D,
                resourceFile: null);
        }

        private sealed class CreateBasicAssetEndAction : EndNameEditAction
        {
            public string Content = string.Empty;

            public override void Action(int instanceId, string pathName, string resourceFile)
            {
                File.WriteAllText(pathName, Content);
                AssetDatabase.ImportAsset(pathName);
                var imported = AssetDatabase.LoadAssetAtPath<Object>(pathName);
                ProjectWindowUtil.ShowCreatedAsset(imported);
            }
        }
    }
}
