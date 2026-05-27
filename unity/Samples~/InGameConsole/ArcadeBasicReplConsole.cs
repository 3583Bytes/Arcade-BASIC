using System;
using System.IO;
using System.Text;
using ArcadeBasic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArcadeBasic.Samples
{
    /// <summary>
    /// In-game REPL console for Arcade BASIC: type a program into the input
    /// field, press Run (or Ctrl/Cmd+Enter), and the captured PRINT output
    /// appears in a scrollable text panel above.
    ///
    /// Wire the UI fields in the inspector, or use
    /// <c>Window &#x2192; Arcade BASIC &#x2192; Samples &#x2192; Create REPL Scene</c>
    /// to generate a ready-to-play scene for you.
    ///
    /// Each submission runs as an independent program; there is no persistent
    /// variable state between turns. (BasicEngine.Run builds a fresh
    /// interpreter every call.) If you want stateful REPL behaviour, keep a
    /// growing source buffer and re-run from the start on each turn.
    /// </summary>
    [AddComponentMenu("Arcade BASIC/REPL Console")]
    public sealed class ArcadeBasicReplConsole : MonoBehaviour
    {
        [Header("UI references")]
        [Tooltip("Multi-line input where the player types BASIC source.")]
        public TMP_InputField inputField;

        [Tooltip("Scrollable transcript of past submissions and their output.")]
        public TMP_Text outputText;

        [Tooltip("Optional. If wired, clicking it runs whatever is in the input field.")]
        public Button runButton;

        [Tooltip("Optional. If wired, the transcript auto-scrolls to the bottom after each run.")]
        public ScrollRect scrollRect;

        [Header("Behaviour")]
        [Tooltip("Clear the input field after a successful run so the next program can be typed.")]
        public bool clearInputAfterRun = true;

        [Tooltip("Cap on transcript length (characters) so it can't grow unbounded. 0 disables the cap.")]
        public int outputCharCap = 8000;

        [TextArea(2, 6)]
        [Tooltip("Greeting shown in the transcript on Start. Edit or empty as you like.")]
        public string greeting =
            "Arcade BASIC REPL ready. Type a program and press Run.\n" +
            "  Example:   PRINT 6 * 7\n" +
            "  Example:   FOR I = 1 TO 3\n              PRINT I, I*I\n            NEXT I\n";

        readonly StringBuilder _transcript = new();

        void Awake()
        {
            if (outputText != null)
            {
                _transcript.Append(greeting);
                outputText.text = _transcript.ToString();
            }
            if (runButton != null)
            {
                runButton.onClick.AddListener(RunCurrent);
            }
        }

        /// <summary>
        /// Run whatever is in the input field. Wire this to a Button.onClick
        /// in the inspector, or call from another script.
        /// </summary>
        public void RunCurrent()
        {
            if (inputField == null || outputText == null)
            {
                Debug.LogWarning("[ArcadeBasic.REPL] inputField / outputText is not wired in the inspector.");
                return;
            }

            string source = inputField.text;
            if (string.IsNullOrWhiteSpace(source)) return;

            AppendTranscript("> " + source.TrimEnd().Replace("\n", "\n  ") + "\n");

            try
            {
                using var stdout = new StringWriter();
                var result = BasicEngine.Run(source, stdout);
                if (stdout.GetStringBuilder().Length > 0)
                {
                    AppendTranscript(stdout.ToString());
                }
                foreach (var diag in result.Diagnostics)
                {
                    AppendTranscript(diag + "\n");
                }
                if (result.ExitCode != 0)
                {
                    AppendTranscript("[exit " + result.ExitCode + "]\n");
                }
            }
            catch (Exception ex)
            {
                AppendTranscript("[host error] " + ex.Message + "\n");
            }

            if (clearInputAfterRun) inputField.text = string.Empty;
            ScrollToBottom();
        }

        /// <summary>Wipe the transcript text. Useful as a button handler.</summary>
        public void ClearTranscript()
        {
            _transcript.Clear();
            if (outputText != null) outputText.text = string.Empty;
        }

        void AppendTranscript(string text)
        {
            _transcript.Append(text);
            if (outputCharCap > 0 && _transcript.Length > outputCharCap)
            {
                _transcript.Remove(0, _transcript.Length - outputCharCap);
            }
            outputText.text = _transcript.ToString();
        }

        void ScrollToBottom()
        {
            if (scrollRect == null) return;
            Canvas.ForceUpdateCanvases();
            scrollRect.verticalNormalizedPosition = 0f;
        }
    }
}
