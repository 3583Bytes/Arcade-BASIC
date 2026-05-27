# In-game REPL console sample

A drop-in Unity scene that gives your game an interactive Arcade BASIC console: type a program, click **Run**, see the output scroll in. Uses TextMeshPro for both the input field and the transcript.

```
┌─────────────────────────────────────────────┐
│ Arcade BASIC REPL ready. Type a program...  │
│ > PRINT 6 * 7                               │
│  42                                         │
│ > FOR I = 1 TO 3                            │
│    PRINT I, I*I                             │
│   NEXT I                                    │
│  1   1                                      │
│  2   4                                      │
│  3   9                                      │
│                                             │  ← transcript (scrollable)
├─────────────────────────────────────────────┤
│ ┌───────────────────────────┐  ┌─────────┐  │
│ │ PRINT "type here"         │  │ Run ▶   │  │
│ └───────────────────────────┘  └─────────┘  │
└─────────────────────────────────────────────┘
```

## One-click setup

1. **Import the sample**: Window → Package Manager → Arcade BASIC Interpreter → Samples → **Import** next to "In-game REPL console".
2. **Build the scene**: Window → Arcade BASIC → Samples → **Create REPL Scene**. This generates `Assets/Samples/ArcadeBasic_REPL.unity` with the Canvas, EventSystem, ScrollRect, input field, and Run button all wired to the `ArcadeBasicReplConsole` controller.
3. **Press Play**, type `PRINT 6 * 7`, click **Run**.

That's the whole thing.

> **TextMeshPro Essentials.** If the text appears as a pink placeholder, Unity prompted you to import TMP Essentials but you skipped it. Re-run **Window → TextMeshPro → Import TMP Essential Resources**, then re-open the scene.

## Manual setup (if you'd rather wire it yourself)

The `ArcadeBasicReplConsole` MonoBehaviour just needs three references in the inspector:

| Field | Type | What it is |
|---|---|---|
| `inputField` | `TMP_InputField` | Multi-line input (`LineType = MultiLineNewline`). |
| `outputText` | `TMP_Text` | Transcript label inside a `ScrollRect → Viewport → Content` hierarchy. |
| `runButton` | `Button` | (Optional.) Click handler is wired automatically on `Awake`. |
| `scrollRect` | `ScrollRect` | (Optional.) Auto-scrolls to bottom after each run. |

Drop the component on any GameObject, hit the inspector references, you're done. The auto-scene builder is convenience, not magic.

## What happens on each Run

Every click of **Run** is an independent program — each call spins up a fresh `BasicEngine`, so `LET X = 42` in one submission won't be visible in the next. If you want stateful REPL semantics (variables persist across turns), batch user input into a growing source buffer and re-run from the top each time. A simple approach:

```csharp
public TMP_InputField inputField;
StringBuilder session = new();

public void RunCurrent()
{
    session.AppendLine(inputField.text);
    BasicEngine.Run(session.ToString(), out string output);
    // ...
}
```

## Feeding INPUT statements from C#

The console as shipped does not wire stdin. If your BASIC program uses `INPUT`, pass a `TextReader` as the third argument to `BasicEngine.Run` — for example, a `StringReader` of pre-canned answers (handy for testing), or a custom reader backed by a UI prompt.

```csharp
using var stdin = new StringReader("42\nhello\n");
using var stdout = new StringWriter();
BasicEngine.Run(source, stdout, stdin);
```

## Performance

Each `Run` lex/parse/analyzes from scratch — fine for human-paced REPL usage, not what you want in a per-frame `Update`. For inner-loop scripting, compile to bytecode once (see `arcade-basic vm` in the main README) and execute the chunk many times.
