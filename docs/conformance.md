# Conformance to ISO/IEC 10279:1991

The implementation targets **ISO/IEC 10279:1991** (Full BASIC — the standard's official language name). ANSI X3.113-1987 is the historical predecessor and is used as a fallback reference when 10279 is ambiguous. The standard permits a number of implementation-defined choices; this document records the choices we made and the deviations we know about.

The spec uses "shall" for normative requirements and "should" for recommendations. Where we deviate from a "shall", it's called out as a **DEVIATION**. Where we make a permitted implementation-defined choice, it's called out as an **IMPLEMENTATION-DEFINED**.

## Scope

| Module | Status |
|---|---|
| Core language (programs, blocks, expressions, control flow) | ✅ Implemented |
| Arrays + MAT | ✅ Implemented |
| Exception handling (`WHEN` / `USE` / `HANDLER` / `CAUSE` / `RETRY` / `CONTINUE`) | ✅ Implemented |
| Modules | ✅ Implemented |
| File I/O (DISPLAY mode, SEQUENTIAL/STREAM) | ✅ Implemented |
| File I/O (INTERNAL mode, BYTE mode, RANDOM organisation) | ❌ Not yet |
| Editing module — `PRINT USING` | ✅ Implemented |
| Editing module — formatted `INPUT` | ❌ Not yet |
| Graphics (§13: coordinate systems, attributes, output) | ✅ Implemented (SVG + terminal/braille backends; Unity backend pending) |
| Fixed-decimal | ❌ Not yet (skipped per project plan unless asked) |
| Real-time | ❌ Not in scope |

Items marked ❌ Not yet are scoped for later phases; the parser and analyzer recognise their syntax but emit "not implemented" diagnostics at execution time, or reject the syntax outright.

## Numeric representation

**IMPLEMENTATION-DEFINED:** Numeric values are arbitrary-precision decimal via `Singulink.Numerics.BigDecimal`. There is no integer fast path: `42` and `42.0` are the same value. Range is effectively unbounded; precision is preserved exactly across `+`, `-`, `*`, but `/` is rounded to 30 significant digits with banker's rounding (`MidpointToEven`).

**IMPLEMENTATION-DEFINED:** PRINT output rounds to **9 significant digits**. ISO 10279 requires at least 6. Internal computation keeps full precision; only the display is rounded. `PRINT USING` uses its picture string verbatim and is unaffected.

**DEVIATION:** `OPTION ARITHMETIC FIXED` is parsed but currently treated as a no-op (still decimal arithmetic). The fixed-decimal subset was deferred per the project plan.

## Strings

**IMPLEMENTATION-DEFINED:** Strings are sequences of Unicode codepoints. `LEN`, `MID$`, `LEFT$`, `RIGHT$`, `ORD`, `CHR$` all operate over codepoints, not bytes or UTF-16 code units. `LEN("π")` is 1; `LEN("😀")` is 1 even though it's a surrogate pair in the underlying UTF-16 storage.

**DEVIATION:** String concatenation uses `&` only. MS-BASIC-style `+` for string concat is rejected (`error FB0302: left operand of Add must be numeric`). ISO 10279 specifies `&` for concatenation and permits `+` only for numeric; we enforce that strictly.

## Lexical

- **Keywords are case-insensitive.** `PRINT`, `print`, `Print`, `PrInT` are all the same token. Identifiers are case-preserving but compared case-insensitively in sema, so `foo` and `FOO` refer to the same variable.
- **Comments:** both `REM ...` and `! ...` to end-of-line. The `!` form is an ISO 10279 addition over Minimal BASIC.
- **Line labels.** Leading integers on a logical line become labels (used as GOTO/GOSUB/RESTORE targets). Optional per ISO. Embedded numeric literals later in the line are not labels.
- **Scientific notation.** Numeric literals with an exponent (`1E-03`, `2.5E3`, `6.022E2`) are lexed and evaluated. Literals are parsed with `NumberStyles.Float` in both the interpreter and the compiler, so `run`, `vm`, and `build` agree.

## Statements

### Implemented

```
LET / assignment without LET
PRINT / PRINT USING / PRINT #channel /
INPUT / INPUT #channel / LINE INPUT / LINE INPUT #channel
READ / DATA / RESTORE
GOTO / GO TO / GOSUB / RETURN
ON i GOTO / ON i GOSUB (with optional ELSE)
STOP / END / RUN
RANDOMIZE [seed]
REM / !
DIM
OPTION BASE 0|1 / OPTION ARITHMETIC ...   (ARITHMETIC is no-op)
IF / THEN / ELSE / ELSEIF / END IF    (block and single-line)
FOR / NEXT / EXIT FOR
DO / LOOP (WHILE/UNTIL pre or post) / EXIT DO
SELECT CASE / CASE / CASE ELSE / END SELECT / EXIT SELECT
DEF FN... (single-line or multi-line)
SUB / END SUB / CALL / EXIT SUB
FUNCTION / END FUNCTION / EXIT FUNCTION
MAT (assignment, read, input, print, redim)
OPEN / CLOSE
WHEN EXCEPTION ... USE ... END WHEN / EXIT WHEN
HANDLER / END HANDLER
CAUSE / RETRY / CONTINUE
MODULE / END MODULE
PUBLIC / PRIVATE
```

### `ON i GOTO` / `ON i GOSUB`

Supported, including the `GO TO`/`GO SUB` two-word spellings and the optional
`ELSE <statement>` clause: `ON i GOTO L1, L2, ... [ELSE stmt]`. The index is
**rounded** (banker's rounding, matching the `ROUND` builtin) to select the
1-based target. An out-of-range index runs the `ELSE` statement if present;
otherwise it raises exception **10001** (§8.2), which `WHEN`/`USE` can catch.

`ON i GOSUB` pushes the return address like a plain `GOSUB`, so `RETURN` comes
back to the statement after the `ON`. The bytecode VM lowers the statement to a
compare-and-jump chain (the index rounded via the `ROUND` builtin), so `run`,
`vm`, and `build` produce identical output.

### Bare `IF cond THEN <line>`

`IF cond THEN 1990` is supported as the ISO shorthand for an implicit `GOTO`: a bare line-number in the THEN (or ELSE) arm of a single-line `IF` parses to a `GotoStmt`. `IF cond THEN 100 ELSE 200` works too. No statement otherwise begins with a numeric literal, so the form is unambiguous.

### Labels on block terminators

A line label on a block terminator (`120 NEXT I`, `999 END IF`, `LOOP`, `END SELECT`, …) is retained and is a valid `GOTO`/`GOSUB` target. The parser preserves it as a labeled no-op at the end of the block body — exactly the old `<label> REM` workaround, applied automatically. Jump semantics follow from where that no-op sits:

- on `NEXT` / `LOOP`: the jump lands at the end of the loop body, so the loop's increment/test runs next (i.e. "continue this iteration"), matching flat-BASIC behaviour.
- on `END IF` / `END SELECT` / `END WHEN`: the jump falls past the block.

Cross-section jumps (e.g. into an `END IF` label from inside an `ELSE` arm) are the one rough edge — the label is anchored to the last sub-block — but these are vanishingly rare and were previously rejected outright.

## Expressions

### Operators

| Op | Form | Notes |
|---|---|---|
| `^` | numeric ^ numeric | Integer exponents use `BigDecimal.Pow`; non-integer exponents go through `double` |
| `*` `/` | numeric only | Division rounds to 30 digits |
| `MOD` `REMAINDER` | numeric, both as operators and as 2-arg builtins | `MOD` follows mathematical modulo (result has sign of divisor); `REMAINDER` has sign of dividend, per spec |
| `+` `-` | numeric only | **`+` is rejected for strings** (see DEVIATION above) |
| `&` | string only | The only string concat operator |
| `=` `<>` `<` `<=` `>` `>=` | numeric or string | String comparison is `string.CompareOrdinal` |
| `AND` `OR` `NOT` `XOR` `IMP` `EQV` | numeric (treated as boolean: nonzero = true) | Result is -1 (true) or 0 (false) per BASIC convention |
| `BAND` `BOR` `BXOR` `BNOT` | numeric, integer-converted | Operate on `(long)x` |

### IMPLEMENTATION-DEFINED: short-circuit evaluation

`AND` and `OR` are **not** short-circuiting. Both operands are evaluated. The spec doesn't require short-circuiting; ours doesn't do it.

## Built-in functions

Implemented per `src/ArcadeBasic.Sema/Builtins.cs` (registration) and `src/ArcadeBasic.Runtime/BuiltinImpls.cs` (semantics):

```
Numeric → numeric:   ABS SGN INT TRUNCATE CEIL ROUND
                     SQR EXP LOG LOG2 LOG10
                     SIN COS TAN ATN ASIN ACOS SEC CSC COT
                     RND  (0 or 1 arg; argument ignored)
Variadic numeric:    MAX MIN
Binary numeric:      MOD REMAINDER  (also operator forms)
String → numeric:    LEN VAL ORD POS
Numeric → string:    STR$ CHR$ REPEAT$
String → string:     LCASE$ UCASE$ UPRC$ LTRIM$ RTRIM$
                     MID$ LEFT$ RIGHT$
System:              DATE$ TIME$
Array introspection: LBOUND UBOUND
Exception accessors: EXTYPE EXLINE EXTEXT$
Constants:           PI EPS INF MAXNUM

Extensions (non-ISO): INKEY$   ← Microsoft BASIC (see "Extensions" below)
```

**IMPLEMENTATION-DEFINED:** `RND` takes 0 or 1 arguments. The argument is **ignored**; every call advances the underlying PRNG. ISO permits dialect-specific behaviour; MS-BASIC's `RND(0) = last value` is not implemented.

**IMPLEMENTATION-DEFINED:** `PI`, `EPS`, `INF`, `MAXNUM` are modelled as constant symbols (no parens). Their values:

| Constant | Value |
|---|---|
| `PI` | 3.141592653589793238462643383279502884 (37 digits) |
| `EPS` | 1e-14 |
| `INF` | 1e308 |
| `MAXNUM` | 1e308 |

`EPS`/`INF`/`MAXNUM` are conservative placeholders, not derived from the BigDecimal type's actual limits.

**IMPLEMENTATION-DEFINED:** Trigonometric and exponential functions evaluate through `double` and parse back to `BigDecimal`. Result accuracy is ~15 significant decimal digits (double precision), well above ISO's recommended 6. Pure-decimal big-precision implementations would be slower and aren't necessary for any conformance test we have.

## Extensions beyond ISO/ECMA Full BASIC

Everything above is ISO/IEC 10279 (ECMA-116) Full BASIC. A few features come from
other dialects and are **not** part of that standard; they're tracked here so it
stays clear which keyword belongs to which specification. Each is tagged with its
source.

| Feature | Source | Notes |
|---|---|---|
| `INKEY$` | **Microsoft BASIC** (GW-BASIC / QuickBASIC) | Niladic string function; **non-blocking** keyboard poll. Returns `""` when no key is waiting, a 1-char string for a normal key, or `CHR$(0)` + a key code for special keys (arrows: 72/80/75/77 — Up/Down/Left/Right). ISO Full BASIC explicitly excludes real-time input (§14), so this is a deliberate arcade extension. Reads from the active keyboard backend (console for `run`/`vm`/standalone; the IDE captures keys globally while a program runs). With no interactive keyboard (piped input, headless) it always returns `""`. |
| `SLEEP <seconds>` | **Microsoft BASIC** (QuickBASIC), extended | Pause execution for the given number of seconds; **fractional seconds are allowed** (QuickBASIC only took whole seconds). Pairs with `INKEY$` to pace a real-time loop. Also acts as the frame boundary — the console graphics backend presents the current frame at each `SLEEP`. A cancellation (the IDE's Stop) interrupts a long `SLEEP` promptly. |

> **Reserved-but-unimplemented:** `WAIT` is reserved in the lexer but has no
> behaviour (in Microsoft BASIC `WAIT port, mask` is a hardware port-wait, *not*
> a delay — `SLEEP` above is the delay statement). The standard
> `INPUT … TIMEOUT/ELAPSED` (§10.2) is also not yet implemented.

## Arrays + MAT

- **OPTION BASE 0 | 1** controlled. Defaults to 0 per ISO; most example programs use `OPTION BASE 1` for 1-based indexing.
- **MAT operations:** `=`, `+`, `-`, `*` (matrix multiply), `TRN`, `INV`, `IDN`, `ZER`, `CON`, `PRINT`, `READ`, `INPUT`, `REDIM`.
- **MAT INV** uses LU decomposition with partial pivoting.
- **MAT REDIM preserves elements** within the new bounds; element-wise per the spec.
- **DEVIATION:** Element-wise string `MAT` is **not allowed.** ISO 10279 §13 explicitly forbids string MAT element-wise concat; we reject it at sema time.

## File I/O

Implemented surface: `OPEN #channel: NAME ...`, `CLOSE #channel`, `PRINT #channel:`, `INPUT #channel:`, `LINE INPUT #channel:`. Channel 0 is implicit stdin/stdout.

**IMPLEMENTATION-DEFINED:** DISPLAY mode files use the host's default text encoding. SEQUENTIAL and STREAM organizations are supported. RANDOM is not yet.

**IMPLEMENTATION-DEFINED:** BYTE mode files (raw `byte[]` I/O) and INTERNAL mode (spec-defined binary format) are not yet implemented.

## Exception handling

`WHEN EXCEPTION IN ... USE ... END WHEN`, `HANDLER name ... END HANDLER`, `CAUSE EXCEPTION n`, `RETRY`, `CONTINUE` all work. Handler stack is explicit; the runtime maintains it without using .NET `Exception`.

Implicit exceptions (division by zero, INPUT type mismatch when the stream is exhausted, MAT dimension mismatch, etc.) raise via a central `RaiseImplicit(type, line, text)` helper.

**IMPLEMENTATION-DEFINED:** Exception type codes follow the spec for the ones we implement. Our extensions (e.g. `4003` for INPUT-stream-closed) are documented inline in `BasicRuntimeException` callsites.

## Modules

`MODULE name ... END MODULE` is supported with static linkage. The CLI's `run` subcommand accepts the main file followed by any number of module files; sema runs across all of them, and module-level declarations become visible globally via `PUBLIC` / scoped via `PRIVATE`.

**DEVIATION:** No on-disk compiled module format. Modules are recompiled from source on each invocation.

## PRINT USING (Editing module)

Picture-string formatting (ISO §10.4). The picture language is a pragmatic
subset; it is **not** the literal ISO grammar (e.g. zero-fill is `0` rather than
the spec's `%`, and string fields use `<`/`>`/`=` rather than the spec's
justifier + floating-character form). Supported format characters:

**Numeric fields**

- `#` digit position (space if no digit)
- `0` digit position (zero-fill)
- `*` digit position (asterisk-fill — cheque protection)
- `$` digit position with a floating currency sign (one `$` prints immediately left of the most-significant digit)
- `,` group the integer part with thousands separators
- `.` decimal point
- `+` / `-` sign placeholder (leading or trailing). A negative value with no sign placeholder floats a `-` next to the leading digit.

A value whose integer part exceeds the field's digit positions overflows and the
field is filled with `*`. Fill precedence when a field mixes characters is
`*` over `0` over space.

**String fields**

- `<####` left-justified, `>####` right-justified, `=####` centred; width = number of `#`.

Multiple values are emitted by re-applying the picture; `Apply` cycles the parsed parts list.

```basic
PRINT USING "##,###":     12345      ! 12,345
PRINT USING "$$,$$$.##":  12345.67   ! $12,345.67
PRINT USING "*****":      42         ! ***42
```

**DEVIATION:** The `^^^^` exponent (scaled-notation) field is not implemented; a
picture containing `^` treats it as literal text.

## Graphics (§13)

The coordinate-systems, attributes, and graphic-output sections are implemented:
`SET WINDOW/VIEWPORT/DEVICE WINDOW/DEVICE VIEWPORT/CLIP`, `ASK …` (with optional
`STATUS`), `CLEAR`, `SET {POINT|LINE} STYLE`, `SET {POINT|LINE|TEXT|AREA} COLOR`,
and `GRAPH POINTS|LINES|AREA` / `GRAPH TEXT`.

**Architecture:** a device-independent core (`GraphicsState` in
`ArcadeBasic.Runtime`) performs all coordinate mapping and clipping
(Cohen–Sutherland for lines, Sutherland–Hodgman for areas) and hands an
`IGraphicsDevice` clipped vector primitives in the normalized device unit square.
The interpreter and VM share that core, so graphics output is byte-identical
between engines (asserted in `ArcadeBasic.Conformance.Tests`). Shipped backends:
**SVG** (`arcade-basic run|vm file.bas --svg out.svg`); the **terminal IDE**
(a Braille-cell canvas on a Graphics tab in `arcade-basic-ide`); and a **console
Braille + ANSI** backend (`AnsiGraphicsDevice` in Runtime) used by `arcade-basic
run`/`vm` and standalone binaries on an interactive terminal — so graphics
programs run anywhere, not just the IDE. All three share a `Rasterizer` in
Runtime. A Unity (`Texture2D`) backend is planned.

**IMPLEMENTATION-DEFINED:** the device transform (DEVICE WINDOW → DEVICE
VIEWPORT) is a plain linear remap within the unit square; physical aspect-ratio
preservation is delegated to the backend, which knows its real pixel geometry.
`ASK DEVICE SIZE` / `MAX COLOR` / `MAX … STYLE` report the active backend's
capabilities (the null backend used by a plain `run` reports a 16-colour, 3-style
device).

**DEVIATION:** invalid `SET` bounds (zero/negative size, or out of `[0,1]` where
required) are treated as the spec's nonfatal "continue with current values" by
leaving the current value unchanged; no catchable exception is raised for them in
this phase. The `^`-style picture exponent is unrelated; see PRINT USING.

## CLI / tooling

**IMPLEMENTATION-DEFINED:** Diagnostics use stable codes (`FB0001`...) with Rust-style snippets and a caret. Colour goes through ANSI when stderr is a TTY.

**IMPLEMENTATION-DEFINED:** The `build` subcommand produces a self-extracting native binary by appending a serialised bytecode payload to the running CLI binary. The VM is feature-complete against the tree-walker — it handles every Arcade BASIC feature the tree-walker accepts (arrays/DIM, `INPUT`, `LINE INPUT`, MAT operations, `READ`/`DATA`/`RESTORE`, `PRINT USING`, `PRINT TAB(n)`, DISPLAY-mode file I/O, `WHEN`/`USE`/`CAUSE`/`RETRY`/`CONTINUE` with inline or named `HANDLER` bodies, forward `GOTO`/`GOSUB`, single-line and multi-line `DEF`, nested MAT constants, modules with PUBLIC re-export) and matches its byte-for-byte output on every example program.

## Known gaps vs `examples/`

| Example | Spec-aligned? | Notes |
|---|---|---|
| `hello.bas`–`pi.bas` | Yes | Pure spec-derived |
| `startrek.bas` | Mostly | Translated from MS BASIC; deviations annotated in the file header (ON GOTO expanded, `+` → `&`, etc.) |
| `lunar.bas` | Mostly | Same translation conventions as startrek; deviations called out at the top of the file |

## Reporting a conformance bug

Open an issue with:

- The minimal `.bas` snippet.
- The ISO 10279 section (or ANSI X3.113 section) the program relies on.
- Observed output from `dotnet run --project src/ArcadeBasic.Cli -- run <file>`.
- Expected output per the spec, ideally with the relevant sentence quoted.

Conformance fixes that change observable behaviour land with both a regression test under `test/ArcadeBasic.Interpreter.Tests/` and an update to this document if a new deviation is introduced or an old one resolved.
