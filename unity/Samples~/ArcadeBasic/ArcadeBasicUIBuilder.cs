using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ArcadeBasic.Samples
{
    // Builds the in-game IDE's UI tree at runtime. Replaces the old prefab-based
    // sample so the whole UI lives in one auditable place and we can fix bugs
    // (scrollbars, font fallbacks, input module mismatches) without YAML surgery.
    //
    // Contract: `Build(editor)` is called from ArcadeBasicCodeEditor.Awake before
    // any event listeners are wired. It creates a Canvas + EventSystem if the
    // scene doesn't have them, builds the entire UI under `editor.transform`,
    // and assigns every serialized field on `editor`.
    internal static class ArcadeBasicUIBuilder
    {
        // Dark theme palette.
        static readonly Color BgRoot        = new(0.06f, 0.07f, 0.09f, 1f);
        static readonly Color BgChrome      = new(0.13f, 0.14f, 0.17f, 1f);
        static readonly Color BgTabInactive = new(0.09f, 0.10f, 0.12f, 1f);
        static readonly Color BgPane        = new(0.09f, 0.10f, 0.12f, 1f);
        static readonly Color BgGutter      = new(0.07f, 0.08f, 0.10f, 1f);
        static readonly Color BgMenuPanel   = new(0.16f, 0.17f, 0.20f, 1f);
        static readonly Color BgScrollbar   = new(0.10f, 0.11f, 0.13f, 0.6f);
        static readonly Color ScrollHandle  = new(0.45f, 0.48f, 0.55f, 1f);
        static readonly Color MenuHover     = new(0.30f, 0.45f, 0.75f, 1f);
        static readonly Color TextMain      = new(0.92f, 0.94f, 0.96f, 1f);
        static readonly Color TextMuted     = new(0.62f, 0.66f, 0.72f, 1f);
        static readonly Color TextDim       = new(0.50f, 0.54f, 0.60f, 1f);
        static readonly Color TextOutput    = new(0.84f, 0.94f, 0.84f, 1f);
        static readonly Color ProblemsPanelBg = new(0.18f, 0.10f, 0.10f, 1f);

        const int MenuBarHeight   = 22;
        const int TabBarHeight    = 22;
        const int FooterHeight    = 22;
        const int MenuItemHeight  = 24;
        const int MenuButtonWidth = 52;
        const int TabButtonWidth  = 68;
        const int MenuPanelWidth  = 160;
        const int InputLineHeight = 28;
        const int ProblemsHeight  = 130;
        const int ScrollbarWidth  = 14;
        const int CodeFontSize    = 16;
        const int ChromeFontSize  = 12;

        public static void Build(ArcadeBasicCodeEditor editor)
        {
            EnsureEventSystem();
            var canvas = EnsureCanvas(editor);

            // Root fills the canvas; children pin themselves with explicit anchors.
            var root = NewUI("Root", canvas.transform);
            Stretch(root.GetComponent<RectTransform>(), 0, 0, 0, 0);
            root.AddComponent<Image>().color = BgRoot;

            BuildMenuBar(root.transform, editor);
            BuildTabBar(root.transform, editor);
            BuildFooter(root.transform, editor);
            BuildContent(root.transform, editor);
            BuildMenuOverlay(canvas.transform, editor);
        }

        static void EnsureEventSystem()
        {
            var es = Object.FindObjectOfType<EventSystem>();
            if (es == null) es = new GameObject("EventSystem").AddComponent<EventSystem>();

            // A scene may ship an EventSystem whose input-module script is missing
            // (e.g. the Input System package's module when that package isn't
            // installed). A missing script doesn't satisfy BaseInputModule, so add
            // a working module ourselves when none is present — otherwise no clicks
            // or key events reach the UI.
            if (es.GetComponent<BaseInputModule>() == null)
            {
                var inputSystemType = System.Type.GetType(
                    "UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
                if (inputSystemType != null) es.gameObject.AddComponent(inputSystemType);
                else                         es.gameObject.AddComponent<StandaloneInputModule>();
            }
        }

        static Canvas EnsureCanvas(ArcadeBasicCodeEditor editor)
        {
            // If the script's GameObject already lives under a Canvas, use it.
            var existing = editor.GetComponentInParent<Canvas>();
            if (existing != null) return existing;

            // Otherwise convert the script's own GameObject into the Canvas root.
            var go = editor.gameObject;
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        // ---- Menu bar -------------------------------------------------------

        static void BuildMenuBar(Transform parent, ArcadeBasicCodeEditor e)
        {
            var bar = NewUI("Menu Bar", parent);
            PinTop(bar.GetComponent<RectTransform>(), MenuBarHeight, 0);
            bar.AddComponent<Image>().color = BgChrome;
            var h = bar.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(4, 4, 0, 0);
            h.spacing = 0;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;

            e.fileMenuButton = BuildBarButton(bar.transform, "File");
            e.runMenuButton  = BuildBarButton(bar.transform, "Run");
            e.helpMenuButton = BuildBarButton(bar.transform, "Help");

            var spacer = NewUI("Spacer", bar.transform);
            spacer.AddComponent<LayoutElement>().flexibleWidth = 1;
        }

        static Button BuildBarButton(Transform parent, string label)
        {
            var go = NewUI(label + " Menu Button", parent);
            go.AddComponent<LayoutElement>().preferredWidth = MenuButtonWidth;
            var img = go.AddComponent<Image>();
            img.color = Color.white;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var c = btn.colors;
            c.normalColor      = new Color(0, 0, 0, 0);
            c.highlightedColor = new Color(1f, 1f, 1f, 0.06f);
            c.pressedColor     = new Color(1f, 1f, 1f, 0.10f);
            c.selectedColor    = new Color(0, 0, 0, 0);
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

        // ---- Tab bar --------------------------------------------------------

        static void BuildTabBar(Transform parent, ArcadeBasicCodeEditor e)
        {
            var bar = NewUI("Tab Bar", parent);
            PinTop(bar.GetComponent<RectTransform>(), TabBarHeight, MenuBarHeight);
            bar.AddComponent<Image>().color = BgRoot;
            var h = bar.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(6, 0, 2, 0);
            h.spacing = 2;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;

            e.sourceTabButton = BuildTabButton(bar.transform, "Source");
            e.outputTabButton = BuildTabButton(bar.transform, "Output");

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
            btn.transition = Selectable.Transition.None;

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

        // ---- Content (source + output, stacked) ----------------------------

        static void BuildContent(Transform parent, ArcadeBasicCodeEditor e)
        {
            var content = NewUI("Tab Content", parent);
            FillMiddle(content.GetComponent<RectTransform>(),
                topInset: MenuBarHeight + TabBarHeight,
                bottomInset: FooterHeight);

            BuildOutputPane(content.transform, e);
            BuildSourcePane(content.transform, e);

            // Shared INPUT bar pinned to the bottom of the content area, built last
            // so it draws on top of whichever pane (Source / Output / Graphics) is
            // active. This lets graphics programs (e.g. kanban) keep the board on
            // screen with the prompt line right below it, matching their layout.
            BuildInputBar(content.transform, e);
        }

        static void BuildOutputPane(Transform parent, ArcadeBasicCodeEditor e)
        {
            var pane = NewUI("Output Pane", parent);
            Stretch(pane.GetComponent<RectTransform>(), 0, 0, 0, 0);
            pane.AddComponent<Image>().color = BgPane;
            e.outputPane = pane;

            // Scroll region, leaves room for the INPUT bar at the bottom.
            var scrollGo = NewUI("Scroll", pane.transform);
            var scrollRT = scrollGo.GetComponent<RectTransform>();
            scrollRT.anchorMin = new Vector2(0, 0);
            scrollRT.anchorMax = new Vector2(1, 1);
            scrollRT.offsetMin = new Vector2(0, InputLineHeight);
            scrollRT.offsetMax = Vector2.zero;
            var scroll = scrollGo.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            e.outputScroll = scroll;

            var viewport = NewUI("Viewport", scrollGo.transform);
            var vpRT = viewport.GetComponent<RectTransform>();
            vpRT.anchorMin = Vector2.zero;
            vpRT.anchorMax = Vector2.one;
            vpRT.offsetMin = new Vector2(8, 8);
            vpRT.offsetMax = new Vector2(-(ScrollbarWidth + 4), -8);
            viewport.AddComponent<RectMask2D>();
            scroll.viewport = vpRT;

            var contentGo = NewUI("Content", viewport.transform);
            var contentRT = contentGo.GetComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0, 1);
            contentRT.anchorMax = new Vector2(1, 1);
            contentRT.pivot = new Vector2(0.5f, 1);
            contentRT.anchoredPosition = Vector2.zero;
            contentRT.sizeDelta = new Vector2(0, 400);
            contentGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = contentRT;

            var output = contentGo.AddComponent<TextMeshProUGUI>();
            ConfigureCodeText(output, TextOutput);
            output.enableWordWrapping = true;
            output.text = string.Empty;
            e.outputText = output;

            var scrollbar = BuildVerticalScrollbar(scrollGo.transform, "Vertical Scrollbar", anchorRight: true);
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            // The INPUT bar is built once as a shared bar in BuildContent (it spans
            // the content area, not just this pane). The scroll above still reserves
            // InputLineHeight at the bottom so output text never hides behind it.
        }

        static void BuildInputBar(Transform parent, ArcadeBasicCodeEditor e)
        {
            var bar = NewUI("Input Bar", parent);
            var barRT = bar.GetComponent<RectTransform>();
            barRT.anchorMin = new Vector2(0, 0);
            barRT.anchorMax = new Vector2(1, 0);
            barRT.pivot = new Vector2(0.5f, 0);
            barRT.anchoredPosition = Vector2.zero;
            barRT.sizeDelta = new Vector2(0, InputLineHeight);
            bar.AddComponent<Image>().color = BgChrome;

            var promptGo = NewUI("Prompt", bar.transform);
            var promptRT = promptGo.GetComponent<RectTransform>();
            promptRT.anchorMin = new Vector2(0, 0);
            promptRT.anchorMax = new Vector2(0, 1);
            promptRT.pivot = new Vector2(0, 0.5f);
            promptRT.anchoredPosition = new Vector2(8, 0);
            promptRT.sizeDelta = new Vector2(24, 0);
            var prompt = promptGo.AddComponent<TextMeshProUGUI>();
            prompt.text = "? ";
            prompt.color = TextMain;
            prompt.fontSize = CodeFontSize;
            prompt.alignment = TextAlignmentOptions.MidlineLeft;
            prompt.raycastTarget = false;
            e.inputLinePromptLabel = prompt;

            var fieldGo = NewUI("Field", bar.transform);
            var fieldRT = fieldGo.GetComponent<RectTransform>();
            fieldRT.anchorMin = new Vector2(0, 0);
            fieldRT.anchorMax = new Vector2(1, 1);
            fieldRT.offsetMin = new Vector2(36, 4);
            fieldRT.offsetMax = new Vector2(-8, -4);
            fieldGo.AddComponent<Image>().color = BgPane;

            var inputLine = fieldGo.AddComponent<TMP_InputField>();
            inputLine.lineType = TMP_InputField.LineType.SingleLine;

            var textArea = NewUI("Text Area", fieldGo.transform);
            Stretch(textArea.GetComponent<RectTransform>(), 6, 0, 6, 0);
            textArea.AddComponent<RectMask2D>();
            inputLine.textViewport = textArea.GetComponent<RectTransform>();

            var textGo = NewUI("Text", textArea.transform);
            Stretch(textGo.GetComponent<RectTransform>(), 0, 0, 0, 0);
            var tmpText = textGo.AddComponent<TextMeshProUGUI>();
            ConfigureCodeText(tmpText, TextMain);
            tmpText.alignment = TextAlignmentOptions.MidlineLeft;
            inputLine.textComponent = tmpText;

            e.inputLineField = inputLine;
            // The bar stays active and visible at all times; SetInputBarVisible
            // on the controller toggles interactability + prompt text instead.
        }

        static void BuildSourcePane(Transform parent, ArcadeBasicCodeEditor e)
        {
            var pane = NewUI("Source Pane", parent);
            Stretch(pane.GetComponent<RectTransform>(), 0, 0, 0, 0);
            pane.AddComponent<Image>().color = BgPane;
            e.sourcePane = pane;

            BuildSourceScrollView(pane.transform, e);
            BuildProblemsPane(pane.transform, e);
        }

        // The source uses a ScrollRect wrapping the TMP_InputField, with the
        // ScrollRect's scrollbar (not TMP_InputField.verticalScrollbar) driving
        // scrolling. This avoids TMP 3.0.x's UpdateScrollbar → graphic rebuild
        // collision. ArcadeBasicCodeEditor.UpdateSourceContentHeight resizes
        // Content as the user types so the ScrollRect's range always matches
        // the text's preferred height. The Gutter lives INSIDE Content so it
        // scrolls and clips with the input field instead of overflowing into
        // the footer.
        static void BuildSourceScrollView(Transform parent, ArcadeBasicCodeEditor e)
        {
            var scrollGo = NewUI("Source Scroll View", parent);
            Stretch(scrollGo.GetComponent<RectTransform>(), 0, 0, 0, 0);
            scrollGo.AddComponent<Image>().color = BgPane;
            var scroll = scrollGo.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            e.sourceScroll = scroll;

            var viewport = NewUI("Viewport", scrollGo.transform);
            var vpRT = viewport.GetComponent<RectTransform>();
            vpRT.anchorMin = Vector2.zero;
            vpRT.anchorMax = Vector2.one;
            vpRT.offsetMin = new Vector2(0, 0);
            vpRT.offsetMax = new Vector2(-(ScrollbarWidth + 4), 0);
            viewport.AddComponent<RectMask2D>();
            scroll.viewport = vpRT;

            var content = NewUI("Content", viewport.transform);
            var contentRT = content.GetComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0, 1);
            contentRT.anchorMax = new Vector2(1, 1);
            contentRT.pivot = new Vector2(0.5f, 1);
            contentRT.anchoredPosition = Vector2.zero;
            contentRT.sizeDelta = new Vector2(0, 400);
            scroll.content = contentRT;

            var h = content.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(0, 0, 0, 0);
            h.spacing = 0;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;

            e.gutterText = BuildGutter(content.transform);
            BuildInputArea(content.transform, e);

            var sb = BuildVerticalScrollbar(scrollGo.transform, "Vertical Scrollbar", anchorRight: true);
            scroll.verticalScrollbar = sb;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
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
            ApplyDefaultFont(tmp);
            return tmp;
        }

        static void BuildInputArea(Transform parent, ArcadeBasicCodeEditor e)
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
            var highlight = overlayGo.AddComponent<TextMeshProUGUI>();
            ConfigureCodeText(highlight, TextMain);
            highlight.richText = true;
            highlight.raycastTarget = false;
            highlight.text = string.Empty;
            e.highlightOverlay = highlight;

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
            ph.text = "! Type Arcade BASIC here, then File > Save (or Run > Run)";
            ph.richText = false;
            input.placeholder = ph;

            e.inputField = input;
        }

        static void BuildProblemsPane(Transform parent, ArcadeBasicCodeEditor e)
        {
            var pane = NewUI("Problems", parent);
            var rt = pane.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(0.5f, 0);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0, ProblemsHeight);
            pane.AddComponent<Image>().color = ProblemsPanelBg;
            pane.AddComponent<LayoutElement>().ignoreLayout = true;
            e.problemsPanel = pane;

            // Title strip at the top
            var title = NewUI("Title", pane.transform);
            var titleRT = title.GetComponent<RectTransform>();
            titleRT.anchorMin = new Vector2(0, 1);
            titleRT.anchorMax = new Vector2(1, 1);
            titleRT.pivot = new Vector2(0.5f, 1);
            titleRT.anchoredPosition = Vector2.zero;
            titleRT.sizeDelta = new Vector2(0, 22);
            title.AddComponent<Image>().color = new Color(0.22f, 0.13f, 0.13f, 1f);

            var titleLabelGo = NewUI("Label", title.transform);
            var labelRT = titleLabelGo.GetComponent<RectTransform>();
            labelRT.anchorMin = new Vector2(0, 0);
            labelRT.anchorMax = new Vector2(1, 1);
            labelRT.offsetMin = new Vector2(8, 0);
            labelRT.offsetMax = new Vector2(-72, 0);
            var titleLabel = titleLabelGo.AddComponent<TextMeshProUGUI>();
            titleLabel.text = "Problems";
            titleLabel.fontSize = ChromeFontSize;
            titleLabel.color = TextMain;
            titleLabel.alignment = TextAlignmentOptions.MidlineLeft;
            titleLabel.raycastTarget = false;
            e.problemsTitleLabel = titleLabel;

            e.problemsCopyButton  = BuildIconButton(title.transform, "Copy",  rightOffset: 36, width: 36, label: "Copy");
            e.problemsCloseButton = BuildIconButton(title.transform, "Close", rightOffset:  4, width: 28, label: "X");

            // Scrollable text body
            var bodyGo = NewUI("Body", pane.transform);
            var bodyRT = bodyGo.GetComponent<RectTransform>();
            bodyRT.anchorMin = new Vector2(0, 0);
            bodyRT.anchorMax = new Vector2(1, 1);
            bodyRT.offsetMin = new Vector2(8, 8);
            bodyRT.offsetMax = new Vector2(-8, -24);
            var body = bodyGo.AddComponent<TextMeshProUGUI>();
            ConfigureCodeText(body, new Color(1f, 0.85f, 0.85f, 1f));
            body.enableWordWrapping = true;
            body.fontSize = ChromeFontSize;
            body.text = string.Empty;
            e.problemsText = body;

            pane.SetActive(false);
        }

        static Button BuildIconButton(Transform parent, string name, float rightOffset, float width, string label)
        {
            var go = NewUI(name, parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1, 0);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(1, 0.5f);
            rt.anchoredPosition = new Vector2(-rightOffset, 0);
            rt.sizeDelta = new Vector2(width, -2);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.30f, 0.18f, 0.18f, 1f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;

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

        static void ConfigureCodeText(TextMeshProUGUI tmp, Color color)
        {
            tmp.fontSize = CodeFontSize;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Overflow;
            ApplyDefaultFont(tmp);
        }

        static bool _fontWarned;

        // TMP_Settings.defaultFontAsset's getter throws a NullReferenceException
        // when TMP's settings asset is missing — i.e. "TMP Essential Resources"
        // has never been imported. Guard via TMP_Settings.instance so the whole
        // UI still builds (text uses TMP's built-in fallback); warn once so the
        // user knows to run Window > TextMeshPro > Import TMP Essential Resources.
        static void ApplyDefaultFont(TMP_Text tmp)
        {
            if (TMP_Settings.instance == null)
            {
                if (!_fontWarned)
                {
                    _fontWarned = true;
                    Debug.LogWarning("[ArcadeBasic] TMP Essential Resources not found — IDE text may not " +
                                     "render. Import via Window > TextMeshPro > Import TMP Essential Resources.");
                }
                return;
            }
            var font = TMP_Settings.defaultFontAsset;
            if (font != null) tmp.font = font;
        }

        // ---- Scrollbar -----------------------------------------------------

        // Builds a vertical Scrollbar GameObject. If anchorRight=true, the
        // scrollbar pins itself to the right edge of its parent (used inside
        // ScrollRects). Otherwise it stretches to fill its parent (used inside
        // a layout-group slot).
        static Scrollbar BuildVerticalScrollbar(Transform parent, string name, bool anchorRight)
        {
            var go = NewUI(name, parent);
            var rt = go.GetComponent<RectTransform>();
            if (anchorRight)
            {
                rt.anchorMin = new Vector2(1, 0);
                rt.anchorMax = new Vector2(1, 1);
                rt.pivot = new Vector2(1, 0.5f);
                rt.sizeDelta = new Vector2(ScrollbarWidth, 0);
                rt.anchoredPosition = Vector2.zero;
            }
            else
            {
                Stretch(rt, 0, 0, 0, 0);
            }
            go.AddComponent<Image>().color = BgScrollbar;

            var sliding = NewUI("Sliding Area", go.transform);
            Stretch(sliding.GetComponent<RectTransform>(), 2, 2, 2, 2);

            var handle = NewUI("Handle", sliding.transform);
            var hRT = handle.GetComponent<RectTransform>();
            hRT.anchorMin = Vector2.zero;
            hRT.anchorMax = Vector2.one;
            hRT.offsetMin = Vector2.zero;
            hRT.offsetMax = Vector2.zero;
            var handleImg = handle.AddComponent<Image>();
            handleImg.color = ScrollHandle;

            var sb = go.AddComponent<Scrollbar>();
            sb.direction = Scrollbar.Direction.BottomToTop;
            sb.handleRect = hRT;
            sb.targetGraphic = handleImg;
            return sb;
        }

        // ---- Footer --------------------------------------------------------

        static void BuildFooter(Transform parent, ArcadeBasicCodeEditor e)
        {
            var bar = NewUI("Footer", parent);
            PinBottom(bar.GetComponent<RectTransform>(), FooterHeight);
            bar.AddComponent<Image>().color = BgChrome;

            var label = NewUI("Status", bar.transform);
            Stretch(label.GetComponent<RectTransform>(), 10, 0, 10, 0);
            var status = label.AddComponent<TextMeshProUGUI>();
            status.text = "Ready";
            status.fontSize = ChromeFontSize;
            status.color = TextMuted;
            status.alignment = TextAlignmentOptions.MidlineLeft;
            e.statusText = status;
        }

        // ---- Menu overlay (drawn on top of everything) ---------------------

        static void BuildMenuOverlay(Transform canvas, ArcadeBasicCodeEditor e)
        {
            var overlay = NewUI("Menu Overlay", canvas);
            Stretch(overlay.GetComponent<RectTransform>(), 0, 0, 0, 0);

            // Click blocker covers everything except the menu bar.
            var blocker = NewUI("Blocker", overlay.transform);
            var brt = blocker.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(0, 0);
            brt.anchorMax = new Vector2(1, 1);
            brt.offsetMin = Vector2.zero;
            brt.offsetMax = new Vector2(0, -MenuBarHeight);
            var bimg = blocker.AddComponent<Image>();
            bimg.color = new Color(0, 0, 0, 0.001f);
            e.menuBlocker = blocker.AddComponent<Button>();
            e.menuBlocker.targetGraphic = bimg;
            e.menuBlocker.transition = Selectable.Transition.None;
            blocker.SetActive(false);

            e.fileMenuPanel = BuildMenuPanel(overlay.transform, "File Menu", xOffset: 4);
            e.fileNewItem    = BuildMenuItem(e.fileMenuPanel.transform, "New");
            e.fileOpenItem   = BuildMenuItem(e.fileMenuPanel.transform, "Open...");
            e.fileSaveItem   = BuildMenuItem(e.fileMenuPanel.transform, "Save");
            e.fileSaveAsItem = BuildMenuItem(e.fileMenuPanel.transform, "Save As...");
            e.fileQuitItem   = BuildMenuItem(e.fileMenuPanel.transform, "Quit");
            e.fileMenuPanel.SetActive(false);

            e.runMenuPanel = BuildMenuPanel(overlay.transform, "Run Menu", xOffset: 4 + MenuButtonWidth);
            e.runRunItem     = BuildMenuItem(e.runMenuPanel.transform, "Run");
            e.runCompileItem = BuildMenuItem(e.runMenuPanel.transform, "Compile");
            e.runBuildItem   = BuildMenuItem(e.runMenuPanel.transform, "Build standalone...");
            e.runStopItem    = BuildMenuItem(e.runMenuPanel.transform, "Stop");
            e.runClearItem   = BuildMenuItem(e.runMenuPanel.transform, "Clear Output");
            e.runMenuPanel.SetActive(false);

            e.helpMenuPanel = BuildMenuPanel(overlay.transform, "Help Menu", xOffset: 4 + MenuButtonWidth * 2);
            e.helpAboutItem = BuildMenuItem(e.helpMenuPanel.transform, "About");
            e.helpMenuPanel.SetActive(false);

            BuildAboutModal(canvas, e);
            BuildBuildTargetModal(canvas, e);
        }

        // Centered modal-style About dialog. Lives in its own overlay layer so
        // it draws on top of every menu, including the click blocker.
        static void BuildAboutModal(Transform canvas, ArcadeBasicCodeEditor e)
        {
            var modal = NewUI("About Modal", canvas);
            Stretch(modal.GetComponent<RectTransform>(), 0, 0, 0, 0);

            // Full-screen dimmer that also catches clicks so they don't leak
            // through to the menu bar or panes behind.
            var dim = NewUI("Dim", modal.transform);
            Stretch(dim.GetComponent<RectTransform>(), 0, 0, 0, 0);
            var dimImg = dim.AddComponent<Image>();
            dimImg.color = new Color(0, 0, 0, 0.45f);
            dim.AddComponent<Button>();   // soak clicks (no listener — OK button is the only dismiss)

            // Centered panel
            var panel = NewUI("Panel", modal.transform);
            var panelRT = panel.GetComponent<RectTransform>();
            panelRT.anchorMin = new Vector2(0.5f, 0.5f);
            panelRT.anchorMax = new Vector2(0.5f, 0.5f);
            panelRT.pivot = new Vector2(0.5f, 0.5f);
            panelRT.anchoredPosition = Vector2.zero;
            panelRT.sizeDelta = new Vector2(440, 220);
            panel.AddComponent<Image>().color = BgMenuPanel;

            var bodyGo = NewUI("Body", panel.transform);
            var bodyRT = bodyGo.GetComponent<RectTransform>();
            bodyRT.anchorMin = new Vector2(0, 0);
            bodyRT.anchorMax = new Vector2(1, 1);
            bodyRT.offsetMin = new Vector2(18, 56);
            bodyRT.offsetMax = new Vector2(-18, -18);
            var body = bodyGo.AddComponent<TextMeshProUGUI>();
            body.fontSize = ChromeFontSize;
            body.color = TextMain;
            body.alignment = TextAlignmentOptions.TopLeft;
            body.enableWordWrapping = true;
            body.raycastTarget = false;
            body.text = string.Empty;
            e.aboutText = body;

            // OK button
            var okGo = NewUI("OK", panel.transform);
            var okRT = okGo.GetComponent<RectTransform>();
            okRT.anchorMin = new Vector2(1, 0);
            okRT.anchorMax = new Vector2(1, 0);
            okRT.pivot = new Vector2(1, 0);
            okRT.anchoredPosition = new Vector2(-18, 18);
            okRT.sizeDelta = new Vector2(72, 26);
            var okImg = okGo.AddComponent<Image>();
            okImg.color = MenuHover;
            var okBtn = okGo.AddComponent<Button>();
            okBtn.targetGraphic = okImg;
            var okLabelGo = NewUI("Label", okGo.transform);
            Stretch(okLabelGo.GetComponent<RectTransform>(), 0, 0, 0, 0);
            var okLabel = okLabelGo.AddComponent<TextMeshProUGUI>();
            okLabel.text = "OK";
            okLabel.fontSize = ChromeFontSize;
            okLabel.color = TextMain;
            okLabel.alignment = TextAlignmentOptions.Center;
            okLabel.raycastTarget = false;
            e.aboutOkButton = okBtn;

            e.aboutPanel = modal;
            modal.SetActive(false);
        }

        // Centered modal picker for "Build standalone" — a uGUI dialog so it renders
        // inside the game view instead of a floating Editor context menu. The editor
        // clones the (hidden) template button once per target platform at runtime.
        static void BuildBuildTargetModal(Transform canvas, ArcadeBasicCodeEditor e)
        {
            var modal = NewUI("Build Target Modal", canvas);
            Stretch(modal.GetComponent<RectTransform>(), 0, 0, 0, 0);

            var dim = NewUI("Dim", modal.transform);
            Stretch(dim.GetComponent<RectTransform>(), 0, 0, 0, 0);
            dim.AddComponent<Image>().color = new Color(0, 0, 0, 0.45f);
            dim.AddComponent<Button>();   // soak clicks; Cancel / a selection dismisses

            var panel = NewUI("Panel", modal.transform);
            var panelRT = panel.GetComponent<RectTransform>();
            panelRT.anchorMin = panelRT.anchorMax = panelRT.pivot = new Vector2(0.5f, 0.5f);
            panelRT.anchoredPosition = Vector2.zero;
            panelRT.sizeDelta = new Vector2(360, 0);   // height auto-fits the content
            panel.AddComponent<Image>().color = BgMenuPanel;
            var pv = panel.AddComponent<VerticalLayoutGroup>();
            pv.padding = new RectOffset(8, 8, 8, 8);
            pv.spacing = 2;
            pv.childControlWidth = true;
            pv.childControlHeight = true;
            pv.childForceExpandWidth = true;
            pv.childForceExpandHeight = false;
            panel.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var titleGo = NewUI("Title", panel.transform);
            titleGo.AddComponent<LayoutElement>().minHeight = MenuItemHeight;
            var title = titleGo.AddComponent<TextMeshProUGUI>();
            title.text = "Build standalone — choose platform";
            title.fontSize = ChromeFontSize;
            title.color = TextMuted;
            title.alignment = TextAlignmentOptions.Center;
            title.raycastTarget = false;

            // Container the editor fills with one button per target platform.
            var listGo = NewUI("Targets", panel.transform);
            var lv = listGo.AddComponent<VerticalLayoutGroup>();
            lv.spacing = 2;
            lv.childControlWidth = true;
            lv.childControlHeight = true;
            lv.childForceExpandWidth = true;
            lv.childForceExpandHeight = false;
            e.buildTargetContainer = listGo.transform;

            // Hidden template button cloned per target at runtime.
            var template = BuildMenuItem(listGo.transform, "Target");
            template.gameObject.SetActive(false);
            e.buildTargetTemplate = template;

            e.buildCancelButton = BuildMenuItem(panel.transform, "Cancel");

            e.buildPanel = modal;
            modal.SetActive(false);
        }

        static GameObject BuildMenuPanel(Transform parent, string name, float xOffset)
        {
            var go = NewUI(name, parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(xOffset, -MenuBarHeight);
            rt.sizeDelta = new Vector2(MenuPanelWidth, 0);

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
            c.normalColor      = new Color(0, 0, 0, 0);
            c.highlightedColor = MenuHover;
            c.pressedColor     = new Color(MenuHover.r * 0.85f, MenuHover.g * 0.85f, MenuHover.b * 0.85f, 1f);
            c.selectedColor    = new Color(0, 0, 0, 0);
            c.colorMultiplier  = 1f;
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

        // ---- RectTransform helpers -----------------------------------------

        static GameObject NewUI(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        static void Stretch(RectTransform rt, float l, float t, float r, float b)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(l, b);
            rt.offsetMax = new Vector2(-r, -t);
        }

        static void PinTop(RectTransform rt, float height, float yOffset)
        {
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.anchoredPosition = new Vector2(0, -yOffset);
            rt.sizeDelta = new Vector2(0, height);
        }

        static void PinBottom(RectTransform rt, float height)
        {
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(0.5f, 0);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0, height);
        }

        static void FillMiddle(RectTransform rt, float topInset, float bottomInset)
        {
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 1);
            rt.offsetMin = new Vector2(0, bottomInset);
            rt.offsetMax = new Vector2(0, -topInset);
        }
    }
}
