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
| Graphics + Picture (SVG backend) | ❌ Not yet |
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
- **DEVIATION:** **Numeric literals in scientific notation (`1E-03`) are not lexed.** Use the decimal form (`0.001`) instead. The lexer treats the letter after a digit as the start of a new token. Adding `E[+-]?digits` support is a one-method change in `Lexer.cs`; not done yet because none of our example programs need it after manual conversion.

## Statements

### Implemented

```
LET / assignment without LET
PRINT / PRINT USING / PRINT #channel /
INPUT / INPUT #channel / LINE INPUT / LINE INPUT #channel
READ / DATA / RESTORE
GOTO / GO TO / GOSUB / RETURN
ON ... GOTO          — see below (DEVIATION)
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

### DEVIATION: `ON i GOTO` / `ON i GOSUB`

The lexer/parser **do not yet support the `ON i GOTO L1,L2,...` form.** The standard requires it. Workaround: expand into a chain of `IF i = k THEN GOTO Lk` statements. The Star Trek example (`examples/startrek.bas`) shows the pattern; comments mark each expanded block.

This is on the list to fix; the parser change is small but the AST node + sema resolution needs to thread through label-list semantics.

### DEVIATION: Bare `IF cond THEN <line>`

`IF cond THEN 1990` (where `1990` is intended as a GOTO target) is rejected by the parser. Write `IF cond THEN GOTO 1990`. ISO permits the bare-line-number form as a shorthand for implicit GOTO; we don't.

### DEVIATION: Labels on block terminators are discarded

`100 NEXT I` — the `100` label is parsed but not retained on the AST node. Same for `END IF`, `LOOP`, `END SELECT`, etc. GOTO/GOSUB targets must land on a non-terminator statement. Anchor them with a `REM` if you need the label to survive:

```basic
100 REM end of loop
    NEXT I
```

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

Picture-string formatting per ISO §15. Supported format characters:

- `#` decimal digit (space if no digit)
- `0` decimal digit (zero if no digit)
- `.` decimal point
- `,` thousands separator
- `+` `-` sign
- `*` fill character
- `$` currency
- `\...\` string field (length = chars inside)
- `&` variable-length string

Multiple values are emitted by re-applying the picture; `Apply` cycles the parsed parts list.

**DEVIATION:** The `^^^^` exponent field is not implemented.

## CLI / tooling

**IMPLEMENTATION-DEFINED:** Diagnostics use stable codes (`FB0001`...) with Rust-style snippets and a caret. Colour goes through ANSI when stderr is a TTY.

**IMPLEMENTATION-DEFINED:** The `build` subcommand produces a self-extracting native binary by appending a serialised bytecode payload to the running CLI binary. The compiled program may use any documented Arcade BASIC feature — the VM covers the full surface: arrays/DIM, `INPUT`, `LINE INPUT`, MAT operations, `READ`/`DATA`/`RESTORE`, `PRINT USING`, DISPLAY-mode file I/O, `WHEN`/`USE`/`CAUSE`/`RETRY`/`CONTINUE` with inline or named `HANDLER` bodies, and modules with PUBLIC re-export.

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
