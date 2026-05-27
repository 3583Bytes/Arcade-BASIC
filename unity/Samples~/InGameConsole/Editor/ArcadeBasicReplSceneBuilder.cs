#if UNITY_EDITOR
using System.IO;
using ArcadeBasic.Samples;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ArcadeBasic.Samples.EditorTools
{
    /// <summary>
    /// One-click scene builder for the In-game REPL sample. Saves you from
    /// hand-wiring Canvas + ScrollRect + InputField + Button every time.
    ///
    /// Menu: <c>Window &#x2192; Arcade BASIC &#x2192; Samples &#x2192; Create REPL Scene</c>
    /// </summary>
    internal static class ArcadeBasicReplSceneBuilder
    {
        const string MenuPath = "Window/Arcade BASIC/Samples/Create REPL Scene";

        [MenuItem(MenuPath)]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            EnsureEventSystem();
            var canvas = CreateCanvas();
            BuildReplUI(canvas);

            EditorSceneManager.MarkSceneDirty(scene);

            var samplesDir = "Assets/Samples";
            Directory.CreateDirectory(samplesDir);
            var path = AssetDatabase.GenerateUniqueAssetPath(samplesDir + "/ArcadeBasic_REPL.unity");
            EditorSceneManager.SaveScene(scene, path);

            Debug.Log($"[Arcade BASIC] Created REPL sample scene at {path}. Press Play to try it.");
            EditorUtility.DisplayDialog(
                "Arcade BASIC REPL sample",
                "Scene created at:\n" + path + "\n\nPress Play, type a BASIC program in the input field, and click Run.",
                "OK");
        }

        static void EnsureEventSystem()
        {
            if (Object.FindObjectOfType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            Undo.RegisterCreatedObjectUndo(go, "Create EventSystem");
        }

        static Canvas CreateCanvas()
        {
            var go = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        static void BuildReplUI(Canvas canvas)
        {
            // Root panel with vertical layout: transcript on top, input + run below.
            var root = NewUI("REPL Root", canvas.transform);
            Stretch(root.GetComponent<RectTransform>(), 20, 20, 20, 20);
            var rootBg = root.AddComponent<Image>();
            rootBg.color = new Color(0.10f, 0.10f, 0.12f, 0.92f);

            var vlayout = root.AddComponent<VerticalLayoutGroup>();
            vlayout.padding = new RectOffset(12, 12, 12, 12);
            vlayout.spacing = 8;
            vlayout.childControlWidth = true;
            vlayout.childControlHeight = true;
            vlayout.childForceExpandWidth = true;
            vlayout.childForceExpandHeight = false;

            // --- Transcript (ScrollRect + Viewport + Content + TMP_Text) ---
            var scrollGo = NewUI("Transcript", root.transform);
            var scrollLE = scrollGo.AddComponent<LayoutElement>();
            scrollLE.flexibleHeight = 1;
            scrollLE.minHeight = 200;
            var scrollImg = scrollGo.AddComponent<Image>();
            scrollImg.color = new Color(0.06f, 0.06f, 0.08f, 1f);
            var scroll = scrollGo.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            var viewport = NewUI("Viewport", scrollGo.transform);
            Stretch(viewport.GetComponent<RectTransform>(), 0, 0, 0, 0);
            // RectMask2D is a hard rect clip — unlike UI.Mask it doesn't
            // require a Graphic and doesn't alpha-multiply children, so
            // transcripts render at full opacity inside the scroll area.
            viewport.AddComponent<RectMask2D>();

            var content = NewUI("Content", viewport.transform);
            var contentRT = content.GetComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0, 1);
            contentRT.anchorMax = new Vector2(1, 1);
            contentRT.pivot = new Vector2(0.5f, 1);
            contentRT.anchoredPosition = Vector2.zero;
            contentRT.sizeDelta = new Vector2(0, 400);
            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var transcriptText = content.AddComponent<TextMeshProUGUI>();
            transcriptText.fontSize = 18;
            transcriptText.color = new Color(0.85f, 0.95f, 0.85f);
            transcriptText.enableWordWrapping = true;
            transcriptText.alignment = TextAlignmentOptions.TopLeft;
            transcriptText.text = string.Empty;

            scroll.viewport = viewport.GetComponent<RectTransform>();
            scroll.content = contentRT;

            // --- Input row (TMP_InputField + Run button side by side) ---
            var inputRow = NewUI("Input Row", root.transform);
            var inputRowLE = inputRow.AddComponent<LayoutElement>();
            inputRowLE.minHeight = 120;
            var hlayout = inputRow.AddComponent<HorizontalLayoutGroup>();
            hlayout.spacing = 8;
            hlayout.childControlWidth = true;
            hlayout.childControlHeight = true;
            hlayout.childForceExpandWidth = false;
            hlayout.childForceExpandHeight = true;

            var inputGo = NewUI("Input", inputRow.transform);
            var inputLE = inputGo.AddComponent<LayoutElement>();
            inputLE.flexibleWidth = 1;
            var inputBg = inputGo.AddComponent<Image>();
            inputBg.color = new Color(0.18f, 0.18f, 0.20f, 1f);
            var input = inputGo.AddComponent<TMP_InputField>();
            input.lineType = TMP_InputField.LineType.MultiLineNewline;

            // Text Area child clips text to the input field bounds.
            var textArea = NewUI("Text Area", inputGo.transform);
            Stretch(textArea.GetComponent<RectTransform>(), 8, 8, 8, 8);
            textArea.AddComponent<RectMask2D>();
            input.textViewport = textArea.GetComponent<RectTransform>();

            var inputTextGo = NewUI("Text", textArea.transform);
            Stretch(inputTextGo.GetComponent<RectTransform>(), 0, 0, 0, 0);
            var inputText = inputTextGo.AddComponent<TextMeshProUGUI>();
            inputText.fontSize = 18;
            inputText.color = Color.white;
            inputText.enableWordWrapping = true;
            inputText.alignment = TextAlignmentOptions.TopLeft;
            input.textComponent = inputText;

            var placeholderGo = NewUI("Placeholder", textArea.transform);
            Stretch(placeholderGo.GetComponent<RectTransform>(), 0, 0, 0, 0);
            var placeholder = placeholderGo.AddComponent<TextMeshProUGUI>();
            placeholder.fontSize = 18;
            placeholder.color = new Color(1, 1, 1, 0.4f);
            placeholder.text = "PRINT \"hello, world\"";
            placeholder.alignment = TextAlignmentOptions.TopLeft;
            input.placeholder = placeholder;

            var runGo = NewUI("Run", inputRow.transform);
            var runLE = runGo.AddComponent<LayoutElement>();
            runLE.preferredWidth = 120;
            var runImg = runGo.AddComponent<Image>();
            runImg.color = new Color(0.20f, 0.55f, 0.30f, 1f);
            var runBtn = runGo.AddComponent<Button>();
            runBtn.targetGraphic = runImg;

            var runLabelGo = NewUI("Label", runGo.transform);
            Stretch(runLabelGo.GetComponent<RectTransform>(), 0, 0, 0, 0);
            var runLabel = runLabelGo.AddComponent<TextMeshProUGUI>();
            runLabel.text = "Run ▶";
            runLabel.fontSize = 20;
            runLabel.alignment = TextAlignmentOptions.Center;
            runLabel.color = Color.white;

            // --- Wire up the controller ---
            var controllerGo = new GameObject("REPL Controller");
            Undo.RegisterCreatedObjectUndo(controllerGo, "Create REPL Controller");
            var repl = controllerGo.AddComponent<ArcadeBasicReplConsole>();
            repl.inputField = input;
            repl.outputText = transcriptText;
            repl.runButton = runBtn;
            repl.scrollRect = scroll;
        }

        static GameObject NewUI(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            return go;
        }

        static void Stretch(RectTransform rt, float l, float t, float r, float b)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(l, b);
            rt.offsetMax = new Vector2(-r, -t);
        }
    }
}
#endif
