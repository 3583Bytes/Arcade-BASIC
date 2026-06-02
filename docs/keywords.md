# Arcade BASIC keyword reference

Every reserved word in Arcade BASIC, grouped by what it does. Each entry has a
one-paragraph description and a runnable example. Comments inside examples
start with `!` (Arcade BASIC's line-comment marker — `REM` works too).

Keywords are case-insensitive: `PRINT`, `print`, and `Print` are the same
token. Examples use uppercase by convention.

> **Companion docs:** [`architecture.md`](architecture.md) for the pipeline,
> [`conformance.md`](conformance.md) for ISO 10279 deviations and the
> [**Extensions** table](conformance.md#extensions-beyond-isoecma-full-basic)
> (which keyword/function belongs to which spec — e.g. `INKEY$` is a Microsoft
> BASIC extension, not ISO Full BASIC),
> [`../examples/README.md`](../examples/README.md) for full programs that
> exercise these features together.

## Index

**Declarations:** [DIM](#dim) · [REDIM](#redim) · [DEF](#def) ·
[FUNCTION](#function) · [SUB](#sub) · [CALL](#call) · [MODULE](#module) ·
[PUBLIC](#public) · [HANDLER](#handler) · [OPTION BASE](#option-base)

**Values & assignment:** [LET](#let) · [REM / `!`](#rem)

**Output:** [PRINT](#print) · [PRINT USING](#print-using) ·
[TAB](#tab) · [`,` and `;` in PRINT lists](#comma-and-semicolon-in-print-lists)

**Input & data:** [INPUT](#input) · [LINE INPUT](#line-input) ·
[READ](#read) · [DATA](#data) · [RESTORE](#restore)

**Selection:** [IF / THEN / ELSEIF / ELSE / END IF](#if--then--elseif--else--end-if) ·
[SELECT CASE / IS / TO / CASE ELSE](#select-case)

**Iteration:** [FOR / TO / STEP / NEXT](#for--to--step--next) ·
[DO / LOOP / WHILE / UNTIL](#do--loop--while--until) ·
[EXIT](#exit)

**Jumps:** [GOTO](#goto) · [GOSUB / RETURN](#gosub--return) ·
[ON ... GOTO / GOSUB](#on--goto--gosub) ·
[STOP](#stop) · [END](#end) · [RANDOMIZE](#randomize) · [RUN](#run) · [SLEEP](#sleep)

**Word-form operators:** [AND, OR, NOT, XOR, IMP, EQV](#logical-operators) ·
[MOD, REMAINDER](#mod--remainder) ·
[BAND, BOR, BXOR, BNOT](#bitwise-operators)

**Arrays & MAT:** [MAT](#mat) · [ZER](#zer) · [IDN](#idn) · [CON](#con) ·
[NUL$](#nul) · [TRN](#trn) · [INV](#inv)

**Files:** [OPEN](#open) · [CLOSE](#close) ·
[PRINT #](#print-) · [INPUT #](#input-) · [LINE INPUT #](#line-input-)

**Exceptions:** [WHEN ... USE](#when--use) · [CAUSE EXCEPTION](#cause-exception) ·
[RETRY](#retry) · [CONTINUE](#continue)

**Graphics (§13):** [SET WINDOW / VIEWPORT / DEVICE …](#graphics) · [SET CLIP](#graphics) ·
[SET … STYLE / COLOR](#graphics) · [ASK …](#graphics) · [CLEAR](#graphics) ·
[GRAPH POINTS / LINES / AREA / TEXT](#graphics)

---

## Declarations

### `DIM`

Allocate an array with explicit bounds. Bounds may be literal numbers or any
numeric expression. The default lower bound is 1 (or 0 if `OPTION BASE 0` is
in effect). Multi-dimensional arrays declare bounds for each axis separated
by commas. Both numeric arrays and string arrays (`name$`) are supported.

```basic
DIM A(10)              ! 1-D numeric array, indices 1..10 (or 0..10 with OPTION BASE 0)
DIM B(0 TO 9)          ! explicit lower bound
DIM GRID(8, 8)         ! 2-D numeric array (rows × cols)
DIM NAMES$(3)          ! 1-D string array
DIM CUBE(2, 2, 2)      ! N-dimensional, up to 7 dims
```

### `REDIM`

(Spelled `MAT REDIM` — see also [`MAT`](#mat).) Re-dimension an existing
array. Elements that fit the overlap of the old and new bounds are
preserved; new cells are zero-initialised for numeric arrays and `""` for
string arrays.

```basic
DIM HIST(5)
LET HIST(1) = 7
LET HIST(2) = 8

MAT REDIM HIST(10)     ! grow the array; HIST(1)=7, HIST(2)=8 preserved
PRINT HIST(1); HIST(2); HIST(3)   !  7   8   0
```

### `DEF`

Define a short user-defined function. The single-line form is one expression;
the multi-line form is a statement list. Single-line DEFs can read enclosing
program-scope variables.

```basic
DEF SQUARE(X) = X * X       ! single-line: expression body
PRINT SQUARE(7)             ! 49

LET K = 10
DEF SHIFT(X) = X + K        ! sees K from the outer program scope
PRINT SHIFT(5)              ! 15

DEF GUARD(X)                ! multi-line form
  IF X < 0 THEN EXIT DEF
  PRINT "non-negative:"; X
END DEF
```

### `FUNCTION`

Define a multi-line, named function with parameters and a return value. The
function returns by assigning to a variable with its own name. Functions can
be recursive.

```basic
FUNCTION FACT(N)            ! recursive factorial
  IF N <= 1 THEN
    FACT = 1
  ELSE
    FACT = N * FACT(N - 1)
  END IF
END FUNCTION

PRINT FACT(6)               ! 720
```

### `SUB`

Define a procedure (no return value). Call with [`CALL`](#call).

```basic
SUB GREET(NAME$)
  PRINT "hi "; NAME$
END SUB

CALL GREET("Adam")          ! hi Adam
```

### `CALL`

Invoke a `SUB`. Required — there's no implicit-call syntax for SUBs.

```basic
SUB DOUBLE(N)
  PRINT N; "*2 ="; N * 2
END SUB

CALL DOUBLE(21)             !  21 *2 = 42
```

### `MODULE`

Group `SUB`/`FUNCTION`/`DEF` declarations behind a namespace boundary.
Declarations inside a module are private to it by default; prefix them with
`PUBLIC` to make them visible from the main program.

```basic
MODULE MATHLIB
  FUNCTION HELPER(X)             ! private: not callable from outside
    HELPER = X * X + 1
  END FUNCTION

  PUBLIC FUNCTION POLY(X)        ! public: re-exported to the main program
    POLY = HELPER(X) * 2
  END FUNCTION
END MODULE

PRINT POLY(3)                    ! 20
```

### `PUBLIC`

Marker that re-exports a `SUB`/`FUNCTION`/`DEF` from a `MODULE` into the
enclosing program scope. See [`MODULE`](#module).

### `HANDLER`

Define a named exception-handler block that one or more `WHEN ... USE`
sites can reference. Body runs with `EXTYPE`/`EXLINE`/`EXTEXT$` populated
from the raised exception.

```basic
HANDLER LOGGER
  PRINT "caught"; EXTYPE; "at line"; EXLINE
END HANDLER

WHEN EXCEPTION IN
  LET X = 1 / 0
USE LOGGER                 ! reference the named handler instead of inlining
END WHEN
```

### `OPTION BASE`

Set the default lower bound for `DIM`med arrays whose `DIM` doesn't specify
one explicitly. Valid values are `0` and `1`. Must appear before any `DIM`
in source order; per spec it's a module-level directive.

```basic
OPTION BASE 0
DIM A(2)                   ! indices 0..2
LET A(0) = 10
LET A(2) = 30
PRINT A(0); A(2)           !  10   30
```

---

## Values & assignment

### `LET`

Assign a value to a variable, parameter, or array element. The `LET`
keyword is optional in Arcade BASIC, but writing it makes the intent
explicit and matches ISO 10279.

```basic
LET X = 42                 ! explicit
Y = X + 1                  ! also fine — LET implied
LET A$ = "hi"              ! string targets end in $
DIM G(3)
LET G(2) = 99              ! array-element target
```

### `REM`

Single-line comment. The exclamation mark `!` is the modern shorthand and
behaves identically: the rest of the line is ignored.

```basic
REM This is a comment.
! This is also a comment.
PRINT 1 + 2                ! inline comment after a statement
```

---

## Output

### `PRINT`

Write expressions to standard output. Items are separated by `,` (advance to
the next zone — every 16 columns by default) or `;` (no space added).
Numeric values are formatted with a leading space for non-negatives and
trailing space; strings are written verbatim. A trailing `;` suppresses the
final newline.

```basic
PRINT "hello"              ! hello
PRINT 6 * 7                !  42
PRINT "a", "b", "c"        ! a               b               c   (16-col zones)
PRINT "x ="; X             ! x = 5   (semicolon: no zone padding)
PRINT "no newline";        ! suppresses the trailing newline
```

### `PRINT USING`

Formatted output via a picture string. Numeric fields: `#` (digit, space if
absent), `0` (zero-fill), `*` (asterisk-fill), `$` (floating currency), `,`
(thousands grouping), `.` (decimal point), `+`/`-` (sign). String fields:
`<####` / `>####` / `=####` (left / right / centre, width = count of `#`). Any
other characters are literal. A value too big for its field overflows to `*`.
The `^^^^` exponent field is not implemented.

```basic
PRINT USING "###": 42                       !  42
PRINT USING "###.##": 3.14159               !   3.14
PRINT USING " ##  ##.##": 7, 7 / 2          !   7   3.50
PRINT USING "##,###": 12345                  ! 12,345
PRINT USING "$$,$$$.##": 12345.67           ! $12,345.67
PRINT USING "*****": 42                       ! ***42
PRINT USING ">###########": "TABULATED"     ! right-aligned string field
```

### `TAB`

Inside a `PRINT` list, `TAB(n)` pads spaces out to column `n` (1-based). If
the cursor is already past column `n` the call is a no-op (does *not* go
back or insert a newline).

```basic
PRINT "x"; TAB(10); "y"    ! x        y     ← 'y' begins at column 10
PRINT "hello"; TAB(3); "!" ! hello!         ← TAB(3) is past current col → no-op
```

### Comma and semicolon in PRINT lists

`,` separates items and advances to the next 16-column print zone. `;`
separates items without inserting whitespace. A trailing `,` or `;` on the
end of a `PRINT` list suppresses the otherwise-automatic newline.

---

## Input & data

### `INPUT`

Prompt and read one or more values from standard input. A semicolon after a
literal prompt suppresses the auto-`? `; a comma adds it. On bad input
(too few fields, or a non-numeric value supplied for a numeric target),
the runtime prints `Not enough data — redo from start.` or
`'…' is not numeric — redo from start.` and re-prompts.

```basic
INPUT N                    ! prompt: "? "
INPUT "Name: ", N$         ! prompt: "Name: ? "
INPUT "Age: "; A           ! prompt: "Age: "  (no ?)
INPUT X, Y, Z              ! one line, comma-separated values
DIM A(3)
INPUT A(2)                 ! array element target
```

### `LINE INPUT`

Read a whole line from input (or a channel — see [`LINE INPUT #`](#line-input-))
into a single string target. No comma splitting.

```basic
LINE INPUT A$              ! prompt: "? "
LINE INPUT "Quote: "; Q$   ! prompt: "Quote: "
PRINT "you said: "; Q$
```

### `READ`

Read the next item(s) from the program's `DATA` pool into variables. The
DATA pool is a single ordered sequence collected from every `DATA`
statement, no matter where they appear. A runtime error fires if the pool
is exhausted.

```basic
DATA 10, 20, 30
READ A, B, C
PRINT A; B; C              !  10   20   30
```

### `DATA`

Declare items to populate the program's `DATA` pool. Statements may appear
anywhere — they're all concatenated in source order at compile time.
Strings can be quoted or bare (a bare item is parsed as numeric if `READ`
expects a numeric target, otherwise as a string).

```basic
DATA "alpha", "beta"       ! quoted strings
DATA 1, 2, 3               ! numerics
DATA bare-string-ish       ! bare → string when a string target reads
READ NAME1$, NAME2$, X, Y, Z, S$
```

### `RESTORE`

Rewind the `DATA` cursor to the start of the pool, so the next `READ`
re-reads from the beginning.

```basic
DATA 1, 2, 3
READ A, B
RESTORE
READ C, D
PRINT A; B; C; D           !  1   2   1   2
```

---

## Selection

### `IF` / `THEN` / `ELSEIF` / `ELSE` / `END IF`

Single-line and block forms. The single-line form puts the conditional
statement (often `PRINT` or `GOTO`) directly after `THEN`. A bare line-number
after `THEN` (or `ELSE`) is shorthand for an implicit `GOTO`: `IF X < 0 THEN 900`
means `IF X < 0 THEN GOTO 900`. The block form opens with `THEN` at end-of-line
and closes with `END IF`, optionally nesting `ELSEIF` and `ELSE`.

```basic
! Single-line form
IF X > 0 THEN PRINT "positive"
IF Y < 0 THEN PRINT "neg" ELSE PRINT "non-neg"

! Bare line-number after THEN/ELSE is an implicit GOTO
IF X < 0 THEN 900
IF X = 0 THEN 100 ELSE 200

! Block form
IF X = 1 THEN
  PRINT "one"
ELSEIF X = 2 THEN
  PRINT "two"
ELSE
  PRINT "other"
END IF
```

### `SELECT CASE`

Multi-branch dispatch on a single subject expression. `CASE` clauses can be
literal values, ranges (`value TO value`), or relational (`IS op value`).
`CASE ELSE` catches anything not matched.

```basic
SELECT CASE GRADE
  CASE 90 TO 100              ! range
    PRINT "A"
  CASE 80 TO 89, 70 TO 79     ! multiple specs per CASE
    PRINT "B-C"
  CASE IS < 60                ! relational
    PRINT "fail"
  CASE ELSE                   ! catch-all
    PRINT "?"
END SELECT
```

---

## Iteration

### `FOR` / `TO` / `STEP` / `NEXT`

Counted loop. `STEP` defaults to `1`; non-default step values may be
negative. The loop body re-evaluates the test before each iteration.

```basic
FOR I = 1 TO 10            ! 1, 2, 3, ..., 10
  PRINT I
NEXT I

FOR I = 0 TO 10 STEP 2     ! 0, 2, 4, 6, 8, 10
  PRINT I
NEXT I
```

### `DO` / `LOOP` / `WHILE` / `UNTIL`

Pre- or post-test indefinite loop. Both `WHILE` (loop while condition is
true) and `UNTIL` (loop until condition is true) work on either end.

```basic
LET X = 0
DO WHILE X < 5             ! pre-test: enter only if X < 5
  LET X = X + 1
LOOP
PRINT X                    ! 5

LET Y = 0
DO
  LET Y = Y + 1
LOOP UNTIL Y >= 3          ! post-test: always run once
PRINT Y                    ! 3
```

### `EXIT`

Leave a block early. The keyword takes the kind of block as a clause:
`EXIT FOR`, `EXIT DO`, `EXIT SELECT`, `EXIT SUB`, `EXIT FUNCTION`,
`EXIT DEF`, `EXIT WHEN`, `EXIT HANDLER`.

```basic
FOR I = 1 TO 100
  IF I = 5 THEN EXIT FOR   ! stop the loop at I=5
NEXT I
PRINT I                    ! 5

SUB GREET(N$)
  PRINT "hi"
  IF N$ = "" THEN EXIT SUB ! return early from the SUB
  PRINT N$
END SUB
```

---

## Jumps

### `GOTO`

Branch unconditionally to a line label. Labels are leading integers on a
line. Forward and backward jumps both work. A label on a block terminator
(`120 NEXT I`, `999 END IF`, …) is a valid target: jumping to a `NEXT`/`LOOP`
runs the loop's increment/test next, and jumping to an `END IF`/`END SELECT`
falls past the block.

```basic
GOTO 200
PRINT "skipped"
200 PRINT "landed"
```

### `GOSUB` / `RETURN`

Call a labelled subroutine and return to the statement after the `GOSUB`.
The return-PC stack is local to the enclosing scope.

```basic
PRINT "before"
GOSUB 100
PRINT "after"
STOP
100 PRINT "inside"
RETURN
```

### `ON ... GOTO / GOSUB`

Computed jump. The index expression is **rounded** to an integer that selects a
1-based line-number from the list (`GO TO` / `GO SUB` two-word spellings also
work). `ON ... GOSUB` pushes a return address like a plain `GOSUB`, so `RETURN`
comes back to the statement after the `ON`. An optional `ELSE <statement>` runs
when the index is out of range; without `ELSE`, an out-of-range index raises
exception `10001`, catchable with [`WHEN ... USE`](#when--use).

```basic
ON CHOICE GOTO 100, 200, 300        ! CHOICE=2 jumps to line 200
ON K GOSUB 1000, 2000, 3000         ! call the K-th subroutine, then continue
ON N GOTO 100, 200 ELSE PRINT "out of range"
```

### `STOP`

Halt the program at the current statement (cleanly, exit code 0). Useful
for halting before a labelled subroutine block.

```basic
PRINT "main done"
STOP
100 PRINT "subroutine"
RETURN
```

### `END`

Halt the program normally. May appear anywhere; commonly the final
statement of the program. `END IF`, `END SUB`, `END FUNCTION`, `END SELECT`,
`END WHEN`, `END HANDLER`, `END MODULE`, `END DEF` close their respective
block statements (these are two-token forms — `END` followed by the block
keyword).

```basic
PRINT "hello"
END                        ! explicit halt at the end of main
```

### `RANDOMIZE`

Reseed the random-number generator used by the `RND` builtin. With no
argument, uses a time-based seed; with a numeric argument, seeds
deterministically.

```basic
RANDOMIZE                  ! time-seeded
RANDOMIZE 42               ! deterministic — useful for reproducibility
PRINT RND
```

### `RUN`

Restart the program from the top, clearing all variable state. Rarely used
in modern style.

```basic
PRINT "boot"
RUN                        ! starts over (clears X, etc.)
```

### `SLEEP`

Pause execution for a number of seconds (fractional allowed). Paces real-time
loops — pair it with the `INKEY$` function (non-blocking keyboard) for games.
**Extension:** Microsoft BASIC (QuickBASIC), not ISO/ECMA Full BASIC — see the
[Extensions table](conformance.md#extensions-beyond-isoecma-full-basic).

```basic
DO
  LET K$ = INKEY$          ! "" if no key is waiting
  IF K$ = "q" THEN EXIT DO
  ! ... update + redraw ...
  SLEEP 0.05               ! ~20 frames/second
LOOP
```

---

## Word-form operators

### Logical operators

`AND`, `OR`, `NOT`, `XOR`, `IMP`, `EQV` operate on truth values. BASIC's
truth model: non-zero is true, `0` is false; the operators return `-1`
(all-ones) for true and `0` for false.

```basic
IF X > 0 AND X < 10 THEN PRINT "in range"
IF NOT FLAG THEN PRINT "off"
IF A OR B THEN PRINT "either"
PRINT A XOR B              ! exclusive-or
PRINT A IMP B              ! A → B  (false only if A true and B false)
PRINT A EQV B              ! A ≡ B  (true iff both sides agree)
```

### `MOD` / `REMAINDER`

Numeric modulo and remainder. `MOD` follows mathematical floor-division
semantics (result has the sign of the divisor). `REMAINDER` truncates
toward zero (result has the sign of the dividend).

```basic
PRINT 7 MOD 3              !  1
PRINT -7 MOD 3             !  2   (floor-division)
PRINT 7 REMAINDER 3        !  1
PRINT -7 REMAINDER 3       ! -1   (truncated)
```

### Bitwise operators

`BAND`, `BOR`, `BXOR`, `BNOT` operate on the integer representation of
their operands.

```basic
PRINT 12 BAND 10           !  8   (1100 & 1010 = 1000)
PRINT 12 BOR 10            ! 14   (1100 | 1010 = 1110)
PRINT 12 BXOR 10           !  6   (1100 ^ 1010 = 0110)
PRINT BNOT 0               ! -1   (bitwise complement)
```

---

## Arrays & MAT

### `MAT`

Apply an operation to a whole array. `MAT` is always followed by one of:
`name = rhs` (assign), `REDIM` (resize), `PRINT name` (output),
`INPUT name` (read), `READ name` (DATA pool). The `rhs` for an assignment
can be another array name, a binary operation (`+`, `-`, `*`), a scalar
multiply `(k) * name`, a transpose `TRN(name)`, an inverse `INV(name)`,
or a [constant array](#zer): `ZER`, `IDN`, `CON`, `NUL$`.

```basic
DIM A(2, 2), B(2, 2), C(2, 2)
LET A(1, 1) = 1
LET A(1, 2) = 2
LET A(2, 1) = 3
LET A(2, 2) = 4

MAT B = A                  ! copy
MAT C = A + B              ! element-wise sum
MAT C = A * B              ! matrix product (inner dims must match)
MAT C = (3) * A            ! scalar multiply
MAT C = TRN(A)             ! transpose
MAT C = INV(A)             ! inverse (square, non-singular)
MAT PRINT C                ! print with row-per-line layout
```

### `ZER`

In a MAT assignment RHS, produces a zero-filled array shaped like the
target's current bounds.

```basic
DIM A(3, 3)
MAT A = ZER                ! all elements = 0
```

### `IDN`

In a MAT RHS, produces an identity matrix (1s on the diagonal, 0s
elsewhere). Requires a square target.

```basic
DIM I(3, 3)
MAT I = IDN                ! diag = 1, else 0
```

### `CON`

In a MAT RHS, produces an array of all-ones shaped like the target.

```basic
DIM A(4)
MAT A = CON                ! 1, 1, 1, 1
```

### `NUL$`

In a string-array MAT RHS, produces an array of empty strings shaped like
the target. The only constant supported for string arrays.

```basic
DIM S$(2)
LET S$(1) = "stale"
MAT S$ = NUL$              ! S$(1) = "", S$(2) = ""
```

### `TRN`

In a MAT RHS, returns the transpose of a 2-D matrix. Bounds flip too:
`TRN` of an `m × n` matrix has shape `n × m`.

```basic
DIM A(2, 3), T(3, 2)
! ... populate A ...
MAT T = TRN(A)
```

### `INV`

In a MAT RHS, returns the inverse of a square 2-D matrix via LU
decomposition with partial pivoting. A singular matrix raises runtime
error 6016.

```basic
DIM A(2, 2), B(2, 2)
LET A(1, 1) = 4
LET A(1, 2) = 7
LET A(2, 1) = 2
LET A(2, 2) = 6
MAT B = INV(A)             ! [[0.6, -0.7], [-0.2, 0.4]]
```

---

## Files

File I/O uses *channels* — small positive integers introduced with `#`. A
channel is opened with `OPEN`, written via `PRINT #`, read via `INPUT #` or
`LINE INPUT #`, and released with `CLOSE`. DISPLAY mode (text) with
`SEQUENTIAL` or `STREAM` organization is supported.

### `OPEN`

Open a file on a channel. The full clause shape is
`OPEN #ch: NAME path$, [ACCESS kind, ORGANIZATION kind, CREATE kind]`.
Each clause has defaults: `ACCESS OUTIN`, `ORGANIZATION SEQUENTIAL`,
`CREATE NEWOLD`. `ACCESS OUTPUT` with no explicit `CREATE` truncates on
open per spec.

```basic
LET P$ = "/tmp/notes.txt"
OPEN #1: NAME P$, ACCESS OUTPUT            ! write-only; truncates on open
PRINT #1: "line one"
PRINT #1: "line two"
CLOSE #1

OPEN #1: NAME P$, ACCESS INPUT             ! read-only; file must exist
LINE INPUT #1: A$
LINE INPUT #1: B$
CLOSE #1
```

`ACCESS` values: `INPUT`, `OUTPUT`, `OUTIN` (read+write).
`ORGANIZATION` values: `SEQUENTIAL`, `STREAM`.
`CREATE` values: `NEW` (must not exist), `OLD` (must exist), `NEWOLD`
(open if present, create otherwise).

### `CLOSE`

Release the file behind a channel and flush any pending writes.

```basic
CLOSE #1
```

### `PRINT #`

Like `PRINT`, but to a channel. Same item formatting rules — expressions,
commas (zone padding), semicolons (no padding), `TAB(n)`.

```basic
OPEN #1: NAME "scores.txt", ACCESS OUTPUT
PRINT #1: "name", "score"      ! tab-separated via zone padding
PRINT #1: "Adam", 42
CLOSE #1
```

### `INPUT #`

Read comma-separated fields from a channel into variables. No interactive
retry loop — a malformed line raises a runtime error.

```basic
OPEN #1: NAME "data.csv", ACCESS INPUT
INPUT #1: NAME$, SCORE
CLOSE #1
PRINT NAME$; "scored"; SCORE
```

### `LINE INPUT #`

Read a whole line (newline-terminated) from a channel into a string
variable.

```basic
OPEN #1: NAME "log.txt", ACCESS INPUT
LINE INPUT #1: ENTRY$
CLOSE #1
PRINT ENTRY$
```

---

## Exceptions

Exceptions in Arcade BASIC are integer-typed events that propagate up the
statement stack. A `WHEN ... USE` block catches both implicit runtime
errors (division by zero, file errors, array-bounds violations, …) and
user-raised `CAUSE EXCEPTION` events. Inside the `USE` body, three
builtins read the active exception: `EXTYPE` (integer code), `EXLINE`
(source line of the failing statement), `EXTEXT$` (message text).

### `WHEN` / `USE`

`WHEN EXCEPTION IN <body> USE <handler-body> END WHEN`. The `IN` body
runs normally; if it raises, control transfers to the `USE` body.
`USE` can be inline (a statement list) or a reference to a named
[`HANDLER`](#handler).

```basic
WHEN EXCEPTION IN
  LET X = 1 / 0                          ! raises runtime error 1001
  PRINT "skipped"
USE
  PRINT "caught at line"; EXLINE; "type"; EXTYPE
END WHEN
```

### `CAUSE EXCEPTION`

Raise a user-defined exception with an explicit integer type. Numeric
expression after `EXCEPTION` is the type; runtime-error codes occupy the
low integer range, so user codes are conventionally ≥ 9000.

```basic
WHEN EXCEPTION IN
  LET N = 0
  IF N = 0 THEN CAUSE EXCEPTION 9001
USE
  PRINT "user error type"; EXTYPE        !  9001
END WHEN
```

### `RETRY`

Inside the `USE` body, restart the `IN` body from the top with a fresh
handler. Useful for retry loops that recover from a transient failure.

```basic
LET ATTEMPTS = 0
WHEN EXCEPTION IN
  LET ATTEMPTS = ATTEMPTS + 1
  IF ATTEMPTS < 3 THEN CAUSE EXCEPTION 9000
  PRINT "succeeded after"; ATTEMPTS; "tries"
USE
  PRINT "  handler saw type"; EXTYPE
  RETRY                                  ! restart the IN body
END WHEN
```

### `CONTINUE`

Inside the `USE` body, resume the `IN` body at the statement *immediately
after* the one that raised. Useful when the offending operation can be
treated as a no-op.

```basic
WHEN EXCEPTION IN
  CAUSE EXCEPTION 1
  PRINT "after-failing"                  ! CONTINUE jumps here
USE
  PRINT "caught"
  CONTINUE                                ! resume past the CAUSE
END WHEN
PRINT "done"
```

---

## Graphics

ECMA-116 §13 graphics. Output goes to a pluggable device; the CLI can render to
SVG with `--svg`:

```sh
arcade-basic run examples/graphics.bas --svg out.svg
```

Drawing happens in **problem (world) coordinates** that you choose with `SET
WINDOW`; they're mapped into the `[0,1]` viewport, clipped, and handed to the
backend. The `run` and `vm` engines produce identical output.

```basic
SET WINDOW 0, 360, -1, 1        ! world coordinate range (left,right,bottom,top)
SET VIEWPORT 0, 1, 0, 1         ! where it lands in normalized device space
SET DEVICE WINDOW 0, 1, 0, 1    ! (advanced) sub-rectangle of the surface
SET DEVICE VIEWPORT 0, 1, 0, 1  ! (advanced) target rectangle on the device
SET CLIP "ON"                   ! clip drawing to the viewport ("ON"/"OFF")

SET LINE STYLE 2                ! 1 solid, 2 dashed, 3 dotted
SET POINT STYLE 3              ! 1 dot, 2 plus, 3 asterisk
SET LINE COLOR 4               ! also POINT / TEXT / AREA COLOR
CLEAR                          ! clear the display

GRAPH LINES: 0,0; 10,5; 20,0   ! connected segments (≥2 points)
GRAPH POINTS: 1,1; 2,2          ! markers
GRAPH AREA: 0,0; 4,0; 2,3       ! filled polygon (≥3 points)
GRAPH TEXT, AT 1,1: "label"     ! text; also: GRAPH TEXT, AT x,y, USING img$: v

ASK WINDOW L, R, B, T           ! read back current settings into variables
ASK DEVICE SIZE W, H, U$        ! device width/height + unit ("METERS"/"OTHER")
ASK MAX COLOR M                 ! capability queries; optional " STATUS s" clause
```

To build a curve or any shape with a variable number of vertices, drive `GRAPH`
from a loop (coordinate expressions may reference variables) — see
[`examples/graphics.bas`](../examples/graphics.bas).

## Notes on coverage

- Every keyword above is **implemented end-to-end** — parser → semantic
  analysis → tree-walker → bytecode VM. The bytecode `vm` and `build`
  paths produce byte-identical output to the tree-walking `run` path on
  every example program.
- The §13 graphics statements (`SET`/`ASK`/`GRAPH`/`CLEAR` — see
  [Graphics](#graphics) below) are implemented. A handful of other keywords are
  still reserved by the lexer for future spec features (DISPLAY, PLOT, DRAW,
  POLYGON, TRANSFORM, MASK, MARGIN, ZONEWIDTH, PIC, PICTURE, FORMAT, TEMPLATE,
  TIMEOUT, BREAK, TRACE, COLLATE, native types like FIXED/REAL/STRING/NUMERIC,
  FILES/FILETYPE, RANDOM, REWRITE, RSET, …) — the parser rejects statements
  built around them with a clear error. See [conformance.md](conformance.md)
  for the full deviation list.
- Function names like `SIN`, `COS`, `LEN`, `MID$`, `CHR$`, `RND`,
  `EXTYPE`, etc. are **builtins**, not keywords — they're resolved by
  name at semantic analysis time and can in principle be shadowed (avoid).
  See [`src/ArcadeBasic.Runtime/BuiltinImpls.cs`](../src/ArcadeBasic.Runtime/BuiltinImpls.cs)
  for the complete catalogue.
