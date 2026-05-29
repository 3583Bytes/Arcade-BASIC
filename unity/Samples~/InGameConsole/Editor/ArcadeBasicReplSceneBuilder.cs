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
    /// One-click scene builder for the in-game code editor sample.
    ///
    /// Layout (top to bottom):
    ///   Menu Bar  — File ▾  Run ▾                                   (~24 px)
    ///   Tab Bar   — Source  Output                                  (~24 px)
    ///   Content   — Source pane OR Output pane (whichever tab)       (flex)
    ///   Footer    — single status text                              (~24 px)
    ///
    /// Plus a Menu Overlay layer (sibling of Root) that contains the popup
    /// menu panels and a fullscreen click-blocker that closes any open menu
    /// when clicked.
    ///
    /// Menu: <c>Window &#x2192; Arcade BASIC &#x2192; Samples &#x2192; Create BASIC IDE Scene</c>
    /// </summary>
    internal static class ArcadeBasicReplSceneBuilder
    {
        const string MenuPath = "Window/Arcade BASIC/Samples/Create BASIC IDE Scene";

        // Modern dark theme palette.
        static readonly Color BgRoot      = new(0.06f, 0.07f, 0.09f, 1f);
        static readonly Color BgChrome    = new(0.13f, 0.14f, 0.17f, 1f);   // menu bar + footer
        static readonly Color BgTabInactive = new(0.09f, 0.10f, 0.12f, 1f);
        static readonly Color BgPane      = new(0.09f, 0.10f, 0.12f, 1f);   // both panes share this — active tab matches
        static readonly Color BgGutter    = new(0.07f, 0.08f, 0.10f, 1f);
        static readonly Color BgMenuPanel = new(0.16f, 0.17f, 0.20f, 1f);
        static readonly Color MenuHover   = new(0.30f, 0.45f, 0.75f, 1f);
        static readonly Color TextMain    = new(0.92f, 0.94f, 0.96f, 1f);
        static readonly Color TextMuted   = new(0.62f, 0.66f, 0.72f, 1f);
        static readonly Color TextDim     = new(0.50f, 0.54f, 0.60f, 1f);
        static readonly Color TextOutput  = new(0.84f, 0.94f, 0.84f, 1f);

        const int MenuBarHeight   = 20;
        const int TabBarHeight    = 20;
        const int FooterHeight    = 20;
        const int MenuItemHeight  = 22;
        const int MenuButtonWidth = 48;
        const int TabButtonWidth  = 64;
        const int MenuPanelWidth  = 150;
        const int InputLineHeight = 28;
        const int CodeFontSize    = 16;
        const int ChromeFontSize  = 12;

        [MenuItem(MenuPath)]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            EnsureEventSystem();
            var canvas = CreateCanvas();
            var refs = BuildUI(canvas);
            AttachController(refs);

            EditorSceneManager.MarkSceneDirty(scene);
            var samplesDir = "Assets/Samples";
            Directory.CreateDirectory(samplesDir);
            var path = AssetDatabase.GenerateUniqueAssetPath(samplesDir + "/ArcadeBasic_Editor.unity");
            EditorSceneManager.SaveScene(scene, path);

            Debug.Log($"[Arcade BASIC] Created code editor scene at {path}.");
            EditorUtility.DisplayDialog(
                "Arcade BASIC code editor",
                "Scene created at:\n" + path +
                "\n\nPress Play. Use the File and Run menus at the top to open/save/run programs. Ctrl/Cmd + Enter also runs.",
                "OK");
        }

        // =====================================================================

        struct UIRefs
        {
            // Tabs
            public GameObject sourcePane, outputPane;
            public Button sourceTab, outputTab;

            // Source pane internals
            public TMP_InputField input;
            public TextMeshProUGUI highlight;
            public TextMeshProUGUI gutter;

            // Output pane internals
            public TextMeshProUGUI output;
            public ScrollRect outputScroll;
            public TMP_InputField inputLine;
            public TextMeshProUGUI inputLinePrompt;

            // Menu bar + popups
            public Button fileMenuButton, runMenuButton;
            public GameObject fileMenuPanel, runMenuPanel;
            public Button menuBlocker;
            public Button fileNew, fileOpen, fileSave, fileSaveAs;
            public Button runRun, runCompile, runBuild, runStop, runClear;

            // Footer
            public TextMeshProUGUI status;
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

        // =====================================================================
        // Top-level
        // =====================================================================

        static UIRefs BuildUI(Canvas canvas)
        {
            var refs = new UIRefs();

            // Root fills the canvas. No layout group — each child pins itself
            // with explicit anchors below, so the chrome heights are exactly
            // what we ask for and Tab Content always gets the leftover middle.
            var root = NewUI("Root", canvas.transform);
            Stretch(root.GetComponent<RectTransform>(), 0, 0, 0, 0);
            root.AddComponent<Image>().color = BgRoot;

            BuildMenuBar(root.transform, ref refs);
            BuildTabBar(root.transform, ref refs);
            BuildFooter(root.transform, ref refs);
            BuildContent(root.transform, ref refs);

            // Menu overlay is a SIBLING of Root inside Canvas — drawn after Root,
            // so its panels and blocker render in front of every other UI element.
            BuildMenuOverlay(canvas.transform, ref refs);

            return refs;
        }

        // Anchor a child to the top of its parent, stretched horizontally, with
        // a fixed pixel height. yOffset shifts further down from the top edge.
        static void PinTop(RectTransform rt, float height, float yOffset = 0)
        {
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.anchoredPosition = new Vector2(0, -yOffset);
            rt.sizeDelta = new Vector2(0, height);
        }

        // Anchor a child to the bottom of its parent, stretched horizontally,
        // with a fixed pixel height.
        static void PinBottom(RectTransform rt, float height)
        {
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(0.5f, 0);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0, height);
        }

        // Fill the parent between fixed insets from the top and bottom.
        static void FillMiddle(RectTransform rt, float topInset, float bottomInset)
        {
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 1);
            rt.offsetMin = new Vector2(0, bottomInset);
            rt.offsetMax = new Vector2(0, -topInset);
        }

        // =====================================================================
        // Menu bar (top)
        // =====================================================================

        static void BuildMenuBar(Transform parent, ref UIRefs refs)
        {
            var bar = NewUI("Menu Bar", parent);
            PinTop(bar.GetComponent<RectTransform>(), MenuBarHeight, yOffset: 0);
            bar.AddComponent<Image>().color = BgChrome;
            var h = bar.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(4, 4, 0, 0);
            h.spacing = 0;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;

            refs.fileMenuButton = BuildMenuButton(bar.transform, "File");
            refs.runMenuButton = BuildMenuButton(bar.transform, "Run");

            var spacer = NewUI("Spacer", bar.transform);
            spacer.AddComponent<LayoutElement>().flexibleWidth = 1;
        }

        static Button BuildMenuButton(Transform parent, string label)
        {
            var go = NewUI(label + " Menu Button", parent);
            go.AddComponent<LayoutElement>().preferredWidth = MenuButtonWidth;
            var img = go.AddComponent<Image>();
            img.color = Color.white;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var c = btn.colors;
            c.normalColor = new Color(0, 0, 0, 0);   // transparent against menu bar bg
            c.highlightedColor = new Color(1f, 1f, 1f, 0.06f);
            c.pressedColor = new Color(1f, 1f, 1f, 0.10f);
            c.selectedColor = new Color(0, 0, 0, 0);
            btn.colors = c;

            var labelGo = NewUI("Label", go.transform);
            Stretch(labelGo.GetComponent<RectTransform>(), 0, 0, 0, 0);
            var tmp = labelGo.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = ChromeFontSize;
            tmp.color = TextMain;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
            return btn;
        }

        // =====================================================================
        // Tab bar (just below menu)
        // =====================================================================

        static void BuildTabBar(Transform parent, ref UIRefs refs)
        {
            var bar = NewUI("Tab Bar", parent);
            PinTop(bar.GetComponent<RectTransform>(), TabBarHeight, yOffset: MenuBarHeight);
            bar.AddComponent<Image>().color = BgRoot;   // sits "underneath" the tabs
            var h = bar.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(6, 0, 2, 0);
            h.spacing = 2;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;

            refs.sourceTab = BuildTabButton(bar.transform, "Source");
            refs.outputTab = BuildTabButton(bar.transform, "Output");

            var spacer = NewUI("Spacer", bar.transform);
            spacer.AddComponent<LayoutElement>().flexibleWidth = 1;
        }

        static Button BuildTabButton(Transform parent, string label)
        {
            var go = NewUI(label + " Tab", parent);
            go.AddComponent<LayoutElement>().preferredWidth = TabButtonWidth;
            var img = go.AddComponent<Image>();
            img.color = BgTabInactive;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.None;   // controller manages color state

            var labelGo = NewUI("Label", go.transform);
            Stretch(labelGo.GetComponent<RectTransform>(), 6, 0, 6, 0);
            var tmp = labelGo.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 12;
            tmp.color = TextMuted;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
            return btn;
        }

        // =====================================================================
        // Content (source pane + output pane stacked, only one active at a time)
        // =====================================================================

        static void BuildContent(Transform parent, ref UIRefs refs)
        {
            var content = NewUI("Tab Content", parent);
            FillMiddle(content.GetComponent<RectTransform>(),
                topInset: MenuBarHeight + TabBarHeight,
                bottomInset: FooterHeight);

            BuildOutputPane(content.transform, ref refs);
            BuildSourcePane(content.transform, ref refs);
        }

        static void BuildOutputPane(Transform parent, ref UIRefs refs)
        {
            var pane = NewUI("Output Pane", parent);
            Stretch(pane.GetComponent<RectTransform>(), 0, 0, 0, 0);
            pane.AddComponent<Image>().color = BgPane;
            refs.outputPane = pane;

            // Scroll region — leaves InputLineHeight at the bottom for the
            // single-line INPUT bar.
            var scrollGo = NewUI("Scroll", pane.transform);
            var scrollRT = scrollGo.GetComponent<RectTransform>();
            scrollRT.anchorMin = new Vector2(0, 0);
            scrollRT.anchorMax = new Vector2(1, 1);
            scrollRT.offsetMin = new Vector2(0, InputLineHeight);
            scrollRT.offsetMax = new Vector2(0, 0);
            refs.outputScroll = scrollGo.AddComponent<ScrollRect>();
            refs.outputScroll.horizontal = false;
            refs.outputScroll.vertical = true;
            refs.outputScroll.movementType = ScrollRect.MovementType.Clamped;

            var viewport = NewUI("Viewport", scrollGo.transform);
            Stretch(viewport.GetComponent<RectTransform>(), 8, 8, 8, 8);
            viewport.AddComponent<RectMask2D>();
            refs.outputScroll.viewport = viewport.GetComponent<RectTransform>();

            var contentGo = NewUI("Content", viewport.transform);
            var contentRT = contentGo.GetComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0, 1);
            contentRT.anchorMax = new Vector2(1, 1);
            contentRT.pivot = new Vector2(0.5f, 1);
            contentRT.anchoredPosition = Vector2.zero;
            contentRT.sizeDelta = new Vector2(0, 400);
            contentGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            refs.outputScroll.content = contentRT;

            refs.output = contentGo.AddComponent<TextMeshProUGUI>();
            ConfigureCodeText(refs.output, TextOutput);
            refs.output.enableWordWrapping = true;
            refs.output.text = string.Empty;

            // INPUT bar pinned to the bottom of the output pane. Stays
            // hidden until the program runs an INPUT / LINE INPUT.
            BuildInputLine(pane.transform, ref refs);
        }

        static void BuildInputLine(Transform parent, ref UIRefs refs)
        {
            var bar = NewUI("Input Bar", parent);
            var barRT = bar.GetComponent<RectTransform>();
            barRT.anchorMin = new Vector2(0, 0);
            barRT.anchorMax = new Vector2(1, 0);
            barRT.pivot = new Vector2(0.5f, 0);
            barRT.anchoredPosition = Vector2.zero;
            barRT.sizeDelta = new Vector2(0, InputLineHeight);
            bar.AddComponent<Image>().color = BgChrome;

            // "? " prompt label, 24px wide on the left.
            var promptGo = NewUI("Prompt", bar.transform);
            var promptRT = promptGo.GetComponent<RectTransform>();
            promptRT.anchorMin = new Vector2(0, 0);
            promptRT.anchorMax = new Vector2(0, 1);
            promptRT.pivot = new Vector2(0, 0.5f);
            promptRT.anchoredPosition = new Vector2(8, 0);
            promptRT.sizeDelta = new Vector2(24, 0);
            refs.inputLinePrompt = promptGo.AddComponent<TextMeshProUGUI>();
            refs.inputLinePrompt.text = "? ";
            refs.inputLinePrompt.color = TextMain;
            refs.inputLinePrompt.fontSize = CodeFontSize;
            refs.inputLinePrompt.alignment = TextAlignmentOptions.MidlineLeft;
            refs.inputLinePrompt.raycastTarget = false;

            // The input field itself fills the rest of the bar.
            var fieldGo = NewUI("Field", bar.transform);
            var fieldRT = fieldGo.GetComponent<RectTransform>();
            fieldRT.anchorMin = new Vector2(0, 0);
            fieldRT.anchorMax = new Vector2(1, 1);
            fieldRT.offsetMin = new Vector2(36, 4);
            fieldRT.offsetMax = new Vector2(-8, -4);
            fieldGo.AddComponent<Image>().color = BgPane;

            refs.inputLine = fieldGo.AddComponent<TMP_InputField>();
            refs.inputLine.lineType = TMP_InputField.LineType.SingleLine;

            // TMP_InputField wants a separate child for the actual text.
            var textArea = NewUI("Text Area", fieldGo.transform);
            Stretch(textArea.GetComponent<RectTransform>(), 6, 0, 6, 0);
            textArea.AddComponent<RectMask2D>();

            var textGo = NewUI("Text", textArea.transform);
            Stretch(textGo.GetComponent<RectTransform>(), 0, 0, 0, 0);
            var tmpText = textGo.AddComponent<TextMeshProUGUI>();
            ConfigureCodeText(tmpText, TextMain);
            tmpText.alignment = TextAlignmentOptions.MidlineLeft;

            refs.inputLine.textViewport = textArea.GetComponent<RectTransform>();
            refs.inputLine.textComponent = tmpText;

            // Hide the bar by default; the controller flips it on when ReadLine fires.
            bar.SetActive(false);
        }

        static void BuildSourcePane(Transform parent, ref UIRefs refs)
        {
            var pane = NewUI("Source Pane", parent);
            Stretch(pane.GetComponent<RectTransform>(), 0, 0, 0, 0);
            pane.AddComponent<Image>().color = BgPane;
            refs.sourcePane = pane;

            var h = pane.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(0, 0, 0, 0);
            h.spacing = 0;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;

            refs.gutter = BuildGutter(pane.transform);
            BuildInputArea(pane.transform, ref refs);
        }

        static TextMeshProUGUI BuildGutter(Transform parent)
        {
            var go = NewUI("Gutter", parent);
            go.AddComponent<LayoutElement>().preferredWidth = 44;
            go.AddComponent<Image>().color = BgGutter;
            var text = NewUI("Text", go.transform);
            Stretch(text.GetComponent<RectTransform>(), 6, 8, 6, 8);
            var tmp = text.AddComponent<TextMeshProUGUI>();
            tmp.text = "1\n";
            tmp.fontSize = CodeFontSize;
            tmp.color = TextDim;
            tmp.alignment = TextAlignmentOptions.TopRight;
            tmp.enableWordWrapping = false;
            tmp.richText = false;
            tmp.font = TMP_Settings.defaultFontAsset;
            return tmp;
        }

        static void BuildInputArea(Transform parent, ref UIRefs refs)
        {
            var inputGo = NewUI("Input", parent);
            inputGo.AddComponent<LayoutElement>().flexibleWidth = 1;
            inputGo.AddComponent<Image>().color = BgPane;

            var input = inputGo.AddComponent<TMP_InputField>();
            input.lineType = TMP_InputField.LineType.MultiLineNewline;
            input.richText = false;
            input.caretColor = TextMain;
            input.customCaretColor = true;
            input.selectionColor = new Color(0.30f, 0.55f, 0.85f, 0.35f);

            var textArea = NewUI("Text Area", inputGo.transform);
            Stretch(textArea.GetComponent<RectTransform>(), 8, 8, 8, 8);
            textArea.AddComponent<RectMask2D>();
            input.textViewport = textArea.GetComponent<RectTransform>();

            var overlayGo = NewUI("Highlight Overlay", textArea.transform);
            Stretch(overlayGo.GetComponent<RectTransform>(), 0, 0, 0, 0);
            refs.highlight = overlayGo.AddComponent<TextMeshProUGUI>();
            ConfigureCodeText(refs.highlight, TextMain);
            refs.highlight.richText = true;
            refs.highlight.raycastTarget = false;
            refs.highlight.text = string.Empty;

            var textGo = NewUI("Text", textArea.transform);
            Stretch(textGo.GetComponent<RectTransform>(), 0, 0, 0, 0);
            var textTMP = textGo.AddComponent<TextMeshProUGUI>();
            ConfigureCodeText(textTMP, new Color(1f, 1f, 1f, 0f));
            textTMP.richText = false;
            textTMP.raycastTarget = false;
            input.textComponent = textTMP;

            var placeholderGo = NewUI("Placeholder", textArea.transform);
            Stretch(placeholderGo.GetComponent<RectTransform>(), 0, 0, 0, 0);
            var ph = placeholderGo.AddComponent<TextMeshProUGUI>();
            ConfigureCodeText(ph, new Color(TextDim.r, TextDim.g, TextDim.b, 0.7f));
            ph.text = "! Type Arcade BASIC here, then File ▸ Save (or Run ▸ Run)";
            ph.richText = false;
            input.placeholder = ph;

            refs.input = input;
        }

        static void ConfigureCodeText(TextMeshProUGUI tmp, Color color)
        {
            tmp.fontSize = CodeFontSize;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.font = TMP_Settings.defaultFontAsset;
            tmp.lineSpacing = 0;
            tmp.characterSpacing = 0;
            tmp.wordSpacing = 0;
            tmp.paragraphSpacing = 0;
        }

        // =====================================================================
        // Footer (bottom)
        // =====================================================================

        static void BuildFooter(Transform parent, ref UIRefs refs)
        {
            var bar = NewUI("Footer", parent);
            PinBottom(bar.GetComponent<RectTransform>(), FooterHeight);
            bar.AddComponent<Image>().color = BgChrome;

            var label = NewUI("Status", bar.transform);
            Stretch(label.GetComponent<RectTransform>(), 10, 0, 10, 0);
            refs.status = label.AddComponent<TextMeshProUGUI>();
            refs.status.text = "Ready";
            refs.status.fontSize = ChromeFontSize;
            refs.status.color = TextMuted;
            refs.status.alignment = TextAlignmentOptions.MidlineLeft;
        }

        // =====================================================================
        // Menu overlay (sibling of Root inside Canvas — drawn on top of all UI)
        // =====================================================================

        static void BuildMenuOverlay(Transform canvas, ref UIRefs refs)
        {
            var overlay = NewUI("Menu Overlay", canvas);
            Stretch(overlay.GetComponent<RectTransform>(), 0, 0, 0, 0);
            // Disable the overlay's own raycasting; only children with Image+Button receive clicks.

            // Click blocker — covers everything except the menu bar at the top.
            // Anchored to fill canvas, top-inset by MenuBarHeight so the File/Run
            // buttons stay clickable while a menu is open (toggle behavior).
            var blocker = NewUI("Blocker", overlay.transform);
            var brt = blocker.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(0, 0);
            brt.anchorMax = new Vector2(1, 1);
            brt.offsetMin = new Vector2(0, 0);
            brt.offsetMax = new Vector2(0, -MenuBarHeight);
            var bimg = blocker.AddComponent<Image>();
            bimg.color = new Color(0, 0, 0, 0.001f);   // near-invisible but raycast-targetable
            refs.menuBlocker = blocker.AddComponent<Button>();
            refs.menuBlocker.targetGraphic = bimg;
            refs.menuBlocker.transition = Selectable.Transition.None;
            blocker.SetActive(false);   // controller flips this on/off

            // File menu — directly under the File button
            refs.fileMenuPanel = BuildMenuPanel(overlay.transform, "File Menu", xOffset: 4);
            refs.fileNew    = BuildMenuItem(refs.fileMenuPanel.transform, "New");
            refs.fileOpen   = BuildMenuItem(refs.fileMenuPanel.transform, "Open...");
            refs.fileSave   = BuildMenuItem(refs.fileMenuPanel.transform, "Save");
            refs.fileSaveAs = BuildMenuItem(refs.fileMenuPanel.transform, "Save As...");
            refs.fileMenuPanel.SetActive(false);

            // Run menu — directly under the Run button
            refs.runMenuPanel = BuildMenuPanel(overlay.transform, "Run Menu", xOffset: 4 + MenuButtonWidth);
            refs.runRun     = BuildMenuItem(refs.runMenuPanel.transform, "Run");
            refs.runCompile = BuildMenuItem(refs.runMenuPanel.transform, "Compile");
            refs.runBuild   = BuildMenuItem(refs.runMenuPanel.transform, "Build standalone...");
            refs.runStop    = BuildMenuItem(refs.runMenuPanel.transform, "Stop");
            refs.runClear   = BuildMenuItem(refs.runMenuPanel.transform, "Clear Output");
            refs.runMenuPanel.SetActive(false);
        }

        static GameObject BuildMenuPanel(Transform parent, string name, float xOffset)
        {
            var go = NewUI(name, parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(xOffset, -MenuBarHeight);
            rt.sizeDelta = new Vector2(MenuPanelWidth, 0);   // height controlled by VLayout

            go.AddComponent<Image>().color = BgMenuPanel;

            var v = go.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(0, 0, 4, 4);
            v.spacing = 0;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;

            var fitter = go.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return go;
        }

        static Button BuildMenuItem(Transform parent, string label)
        {
            var go = NewUI(label, parent);
            go.AddComponent<LayoutElement>().minHeight = MenuItemHeight;
            var img = go.AddComponent<Image>();
            img.color = Color.white;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var c = btn.colors;
            c.normalColor = new Color(0, 0, 0, 0);                  // blends with panel bg
            c.highlightedColor = MenuHover;
            c.pressedColor = new Color(MenuHover.r * 0.85f, MenuHover.g * 0.85f, MenuHover.b * 0.85f, 1f);
            c.selectedColor = new Color(0, 0, 0, 0);
            c.colorMultiplier = 1f;
            btn.colors = c;

            var labelGo = NewUI("Label", go.transform);
            Stretch(labelGo.GetComponent<RectTransform>(), 14, 0, 14, 0);
            var tmp = labelGo.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = ChromeFontSize;
            tmp.color = TextMain;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.raycastTarget = false;
            return btn;
        }

        // =====================================================================

        static void AttachController(UIRefs refs)
        {
            var go = new GameObject("Code Editor Controller");
            Undo.RegisterCreatedObjectUndo(go, "Create Code Editor Controller");
            var editor = go.AddComponent<ArcadeBasicCodeEditor>();

            // Editor pane
            editor.inputField = refs.input;
            editor.highlightOverlay = refs.highlight;
            editor.gutterText = refs.gutter;

            // Output pane
            editor.outputText = refs.output;
            editor.outputScroll = refs.outputScroll;

            // Tabs
            editor.sourcePane = refs.sourcePane;
            editor.outputPane = refs.outputPane;
            editor.sourceTabButton = refs.sourceTab;
            editor.outputTabButton = refs.outputTab;
            editor.tabActiveColor = BgPane;            // matches the content area
            editor.tabInactiveColor = BgTabInactive;
            editor.tabActiveText = TextMain;
            editor.tabInactiveText = TextMuted;

            // Menus
            editor.fileMenuButton = refs.fileMenuButton;
            editor.runMenuButton = refs.runMenuButton;
            editor.fileMenuPanel = refs.fileMenuPanel;
            editor.runMenuPanel = refs.runMenuPanel;
            editor.menuBlocker = refs.menuBlocker;
            editor.fileNewItem = refs.fileNew;
            editor.fileOpenItem = refs.fileOpen;
            editor.fileSaveItem = refs.fileSave;
            editor.fileSaveAsItem = refs.fileSaveAs;
            editor.runRunItem = refs.runRun;
            editor.runCompileItem = refs.runCompile;
            editor.runBuildItem = refs.runBuild;
            editor.runStopItem = refs.runStop;
            editor.runClearItem = refs.runClear;

            // INPUT bar
            editor.inputLineField = refs.inputLine;
            editor.inputLinePromptLabel = refs.inputLinePrompt;

            // Status
            editor.statusText = refs.status;
        }

        // =====================================================================
        // RectTransform helpers
        // =====================================================================

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
