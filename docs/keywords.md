# Arcade BASIC language reference

Every statement, operator and function in Arcade BASIC, grouped by what it does, each
with a short example. New to the language? Start with [Language basics](#language-basics).

Arcade BASIC is ISO/IEC 10279 **Full BASIC** (the same language as ANSI X3.113 and
ECMA-116). Anything marked **†** in the index isn't in that standard: it comes from
Microsoft BASIC, True BASIC or Arcade BASIC itself, and its entry says which.
[Conformance](conformance.md) has the details, including where Arcade BASIC departs from
the standard.

**▶ Try it** under an example opens it in the
[browser IDE](https://3583bytes.github.io/Arcade-BASIC/), ready to run (F5).
Comments in the examples start with `!`; a comment after a statement often shows what
it prints.

## Index

**[Language basics](#language-basics):** [a program](#a-program) · [variables](#variables) ·
[numbers](#numbers) · [strings](#strings) · [true and false](#true-and-false) ·
[procedures](#procedures)

**Declarations:** [DIM](#dim) · [MAT REDIM](#redim) · [DEF](#def) ·
[FUNCTION](#function) · [SUB](#sub) · [CALL](#call) · [MODULE](#module) ·
[PUBLIC](#public) · [SHARE](#share) · [DECLARE PUBLIC](#declare-public) ·
[LIBRARY](#library)† · [HANDLER](#handler) · [OPTION BASE](#option-base) ·
[OPTION ARITHMETIC](#option-arithmetic)

**Values & assignment:** [LET](#let) · [REM / `!`](#rem)

**Output:** [PRINT](#print) · [PRINT USING](#print-using) ·
[TAB](#tab) · [`,` and `;` in PRINT lists](#comma-and-semicolon-in-print-lists)

**Input & data:** [INPUT](#input) · [LINE INPUT](#line-input) ·
[READ](#read) · [DATA](#data) · [RESTORE](#restore)

**Selection:** [IF / THEN / ELSEIF / ELSE / END IF](#if--then--elseif--else--end-if) ·
[SELECT CASE](#select-case)

**Iteration:** [FOR / NEXT](#for--to--step--next) ·
[DO / LOOP / WHILE / UNTIL](#do--loop--while--until) · [EXIT](#exit)

**Jumps:** [GOTO](#goto) · [GOSUB / RETURN](#gosub--return) · [ON … GOTO / GOSUB](#on--goto--gosub)

**Starting and stopping:** [STOP](#stop) · [END](#end) · [RUN](#run)

**[Operators](#operators):** [AND, OR, NOT](#logical-operators) · [XOR, IMP, EQV](#logical-operators)† ·
[MOD, REMAINDER](#mod--remainder) · [BAND, BOR, BXOR, BNOT](#bitwise-operators)†

**[Functions](#functions):** [numbers](#number-functions) (ABS, INT, ROUND, SQR, MAX …) ·
[trigonometry](#trigonometry) · [RND](#rnd) · [RANDOMIZE](#randomize) ·
[strings](#string-functions) (LEN, MID$, POS, UCASE$, VAL, STR$, CHR$ …) ·
[date and time](#date-and-time) · [exception information](#exception-information) ·
[constants](#constants) (PI, EPS, MAXNUM)

**Arrays & MAT:** [MAT](#mat) · [ZER](#zer) · [IDN](#idn) · [CON](#con) ·
[NUL$](#nul) · [TRN](#trn) · [INV](#inv)

**Files:** [OPEN](#open) · [CLOSE](#close) ·
[PRINT #](#print-) · [INPUT #](#input-) · [LINE INPUT #](#line-input-) ·
[WRITE #](#write-) · [READ #](#read-)

**Exceptions:** [WHEN ... USE](#when--use) · [CAUSE EXCEPTION](#cause-exception) ·
[RETRY](#retry) · [CONTINUE](#continue)

**Graphics:** [SET WINDOW / VIEWPORT / DEVICE …](#graphics) · [SET CLIP](#graphics) ·
[SET … STYLE / COLOR](#graphics) · [ASK …](#graphics) · [CLEAR](#graphics) ·
[GRAPH POINTS / LINES / AREA / TEXT](#graphics) ·
[MAT GRAPH CELLS / SET COLOR MIX](#pictures-and-colours-mat-graph-cells-set-color-mix) ·
[LOCATE POINT / GET POINT](#mouse-locate-point--get-point-get-mouse) ·
[GET MOUSE](#mouse-locate-point--get-point-get-mouse)†

**Keys, mouse and time:** [INKEY$](#inkey)† · [KEYDOWN](#keydown)† · [SLEEP](#sleep)† ·
[the mouse](#the-mouse)

**Sound:** [SOUND](#sound)† · [BEEP](#beep)† · [PLAY](#play)†

---

## Language basics

### A program

A program is a list of statements, one to a line; `:` puts several on one line. It runs
from the top until it reaches `END` (or runs out of lines). Keywords and names can be
written in any case: `PRINT`, `print` and `Print` are the same.

Line numbers are optional. A number at the start of a line is a *label*, something for
`GOTO` and `GOSUB` to jump to, so old listings with a number on every line still run,
and new programs only need one where they jump. A comment runs from `!` to the end of
the line (or a line starts with [`REM`](#rem)).

```basic
! Comments start with an exclamation mark.
PRINT "Hello"                    ! a statement, then a comment
LET A = 1 : LET B = 2            ! two statements on one line
GOSUB 100
END

100 PRINT A + B                  ! 100 labels this line, for GOSUB 100
RETURN
```
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=bc7BCoJAFAXQ_XzF1W0SY8vAhZZUEBamH_CSCYfGGXAe2OfH5EIC7-ot7jvcCAc3DMqyh2caGZPmHmShPp2hgVg7i4HG91bc60vVID4rY1yMlUSggLAKXgLulQWhm31xLRvkyJBij3AXyLD7f-fJLYCHs3BWwWirxOn2aAukUoqyOgqRSol5T44NirUtoWLoqYwH99r_mAQvN2Kx6rJp60p8AQ)

### Variables

A name starts with a letter, then letters, digits or `_`. A name ending in `$` holds
a string; any other name holds a number. A name can't be a reserved word, and some
everyday ones are — `AREA`, `NAME`, `SIZE`, `TEXT`, `POINT`, `KEY`, `TYPE`, `COLOR` —
so if a name won't work, check whether the IDE colours it as a keyword. Numbers start at 0 and strings at `""`, though
the IDE underlines a name read before anything was put in it, as that's usually a typo.
An array needs a [`DIM`](#dim) first; its subscripts start at 1 (see
[`OPTION BASE`](#option-base)).

```basic
LET SCORE = 10                   ! a number
LET PLAYER$ = "Ada"              ! a string: the name ends in $
DIM HI(5)                        ! an array of five numbers, HI(1) to HI(5)
LET HI(1) = SCORE
PRINT PLAYER$; " scored"; HI(1)  ! Ada scored 10
```
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=bY69DoIwAIT3PsXRMEjiAIOLpAPRJpKgEmRxrLRoB0rSVhPf3mjRBW-83M9X8RanzbHhYMhSzBVBwNyHi7Kk4i3qqjjzJgYDLaSg86zzVpvrGv6mYMSgoIx00AYx2ZZ77MrFKvlz8-0bCGvFE2OPXj_UdO2W72KWwI9h4cMSLBb4Sd2Uhx9fDgrXjVZJmk85RCikmFxkKXkB)

### Numbers

Numbers are exact decimals: `0.1 + 0.2` is exactly `0.3`, so sums of money never drift.
Write them as `42`, `3.25` or `1.5E3`. `PRINT` shows up to 9 significant digits, with a
space before a number that isn't negative (where a minus sign would go) and one after.
For speed, [`OPTION ARITHMETIC NATIVE`](#option-arithmetic) uses the computer's own
floating point instead.

```basic
PRINT 0.1 + 0.2                  !  0.3
PRINT 10 / 4; 2 ^ 10; 1.5E3      !  2.5  1024  1500
PRINT 1 / 3                      !  0.33333333
```
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=ZYwrDoAwEAV9TzFoku3bflQ1AkMagub-xyAhfERHz0zf1-1A5szIEgMTyHLot-ciUhqJE1fDrS7585JVcKUCXqW3IZLH7_9-CBc)

### Strings

A string is written in double quotes, and a quote inside one is doubled. `&` joins
strings (`+` doesn't). Lengths and positions count characters, whatever the alphabet:
`LEN("π")` is 1. The [string functions](#string-functions) take strings apart.

```basic
LET S$ = "Arcade" & " " & "BASIC"
PRINT S$; LEN(S$)                ! Arcade BASIC 12
PRINT "She said ""hello"""       ! She said "hello"
```
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=83ENUQhWUbBVUHIsSk5MSVVSUFNQUgCTTo7Bns5KXAFBnn4gNdYKPq5-GsEqmgpoQFEBolUBrEHB0AiqRSk4I1WhODEzRUFJKSM1JydfSUkJrgUhB5HiAgA)

### True and false

A comparison is -1 when it's true and 0 when it's false, and `IF`, `WHILE` and `UNTIL`
treat any number other than 0 as true. [`AND`, `OR` and `NOT`](#logical-operators)
combine them.

```basic
PRINT 3 > 2; 3 < 2               ! -1  0
LET READY = 5 > 1
IF READY THEN PRINT "ready"
```
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=Cwjy9AtRMFawUzCyVjBWsFEwUkAFigq6hgoKBlw-riEKQa6OLpEKtgqmCnYKhlyeblCBEA9XP4UAsEFKRamJKZVKXAA)

### Procedures

[`SUB`](#sub)s and [`FUNCTION`](#function)s are written inside the program, before its
`END`. A `SUB` is called with [`CALL`](#call); a `FUNCTION` returns a value by setting a
variable with its own name. Their parameters are their own; other names they use are
the program's variables. In a [`MODULE`](#module) or a [`LIBRARY`](#library) file,
procedures keep to their own variables.

```basic
FUNCTION PRODUCT(W, H)
  LET PRODUCT = W * H
END FUNCTION

SUB SHOW(LABEL$, VALUE)
  PRINT LABEL$; ":"; VALUE
END SUB

CALL SHOW("product", PRODUCT(3, 4))   ! product: 12
END
```
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=PY69DsIgGEX37ymuxKEYFn-mNg5AaWhCoCkgD6C7xuj7m0p1PSfn5g7Z6zQGj2kOfdapKQKWE-BM-jGcUbCDJeN7DGtAFLNCtKE0TirjtgIX6bJZ2mkefULFHVjLuuq-AzErIi2dqzF7PO-39_XFxP_CUeDEOYANVtlif1hi-gA)

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
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=XY5NDoIwFIT3nmJMXLQJmoILdQlCDAvA-HMAhEZfAi1pIehJPJAXM2JM1MksZvHNexPGCXzmCo4fjeFOQ6iuloYK5MbkNwekSiqkhTubuQJMG4gh9dRekG0PcZYi8PcRBB-FcYKACRwyrPj3XXltKiqoRaV7aXDSnSoHerOLQ7Z0sORftPe_Aszo3uJxR6Er-_6T-km0n7A5_99vW0Pq_C4O5PoYRMxz8DL_kOm0pFoqS1rllYOuQauxQEm1HT0B)

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
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=c_H0VfDwDA7RMNXk8nENgbANNRVsFcwRfCMQ34KLy9cxRCHI1QWmxdBAUwEEFBXSi_LLFUoyUhUSi4oSK61hptia68AMsLVQKChKLU4tKktN4QoI8vSDW2UNUwJlGGuCjVQwV1BQsFBQUDDgAgA)

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
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=Xc9Li8IwFAXgfX7FqatxBkFBkXF0ISTVUilaI2Tr49oJtElJqui_FyvFx1mfez4uFyHWq800FV-qjQkUvqHwSACvTZZTJ9eGRqBL6ch7bQ129nBlyzRKZHM8bOM1Afq_jC2ERIwJel3G7848CmXD_CB-lj2RR4yjswWqf4I9VeRQOpu5bQG_tyU1XL0xeNcQoDdgtTHbTFN-N_DZKE55petXcLSuYEAUQmGMLuRcJBAqkuAiZMCDahlrOoaybaXPNGr9QTGR8LpyAw)

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
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=cwv1cw7x9PdTcHN0DtHw01RAAooKRanJpUXFmWWpCmmJySX5RZmJOVwKCp5uCn4KNrYKhgohHq5-XCClIN0KtgqGXAoKrj7BrshifgpaUMMVdBUMNUEq_FwUPN24QJQb1HouroAgT78QiEIzFFeAHWJuZMAFAA)

### `SUB`

Define a procedure (no return value). Call with [`CALL`](#call).

```basic
SUB GREET(NAME$)
  PRINT "hi "; NAME$
END SUB

CALL GREET("Adam")          ! hi Adam
```
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=Cw51UnAPcnUN0fBz9HVV0eRSUAgI8vQLUVDKyFRQslYAi3K5-rkoBIc6cXE5O_r4QNUrOaYk5ippKsCBokJGpgJIkAsA)

### `CALL`

Invoke a `SUB`. Required — there's no implicit-call syntax for SUBs.

```basic
SUB DOUBLE(N)
  PRINT N; "*2 ="; N * 2
END SUB

CALL DOUBLE(21)             !  21 *2 = 42
```
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=TcsxDoAgEADB_l6xUokdFztiIUJhQk4j8v-vGAsTp5_WE_noqZbRvMB57XZjETcpi4sYEyrFMq0nkW2t9QsaPH8DaOB9zCoP)

### `MODULE`

A program-unit of its own (ISO 10279): its procedures, its variables, its own
`DATA`, and statements that run before the main program to set it up. Its
procedures are private to it unless marked `PUBLIC`; its code doesn't see the
main program's variables — only its own, its `SHARE`/`PUBLIC` ones, and the
`PUBLIC` ones it [`DECLARE`s](#declare-public). A name a module procedure reads
before setting is that procedure's own.

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
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=Xc9fC4IwFAXw932K01sZQX_ehB5KFwlzSijY49JVg9lkzejjhwZl3bcL9_C7J07CnFHEm2zPoi0BdjkPsijh2FOW0sO4mGA4IzRWPYSTPm7GoRRai5OWOFtTw7TuripJusN3HGsU8FBgigUBKA8_ACFAmm9ZFHzNNGHHgThC0560Kn1YOZPPxlgnKzgDd5WohbqhseZiRd2LXRjrwd8elv9mt8R9ZULSQ8SzN7n6Lfnhl3PyAg)

A module that sets itself up, sharing a variable:

```basic
MODULE COUNTER
  PUBLIC COUNT                   ! other units can DECLARE PUBLIC it
  SHARE INC                      ! the module's own
  LET COUNT = 10                 ! runs before the main program
  LET INC = 5
  PUBLIC SUB BUMP
    LET COUNT = COUNT + INC
  END SUB
END MODULE

DECLARE PUBLIC COUNT
CALL BUMP
PRINT COUNT                      ! 15
```
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=dZCxDsIwDER3f8UxMbDQgZGBppGolIaqNB9QIEAlSFCait9HaUAgKF5OsvzubBebTAkOtlGy5hUBpUpFzmIDvzWB9Wft0JvWd9g3BhlnYlXxF9h6Arbr0MklG3EYTPxZ42oP_UVPO9i7IUDw-pm6RDIfYVxvOuz00Tod-aY1uDl7cs31yYfEJRbvM7YqRaqKkoLFZ0LUWSAI4DILoxS0GD5C9HXXABBbCRENyyqX9d83DQsnC3oA)

### `PUBLIC`

In a `MODULE`: before a `SUB`/`FUNCTION`/`DEF`, makes it callable from the rest
of the program; followed by names, declares the module's public variables —
scalars, and arrays with bounds as in `DIM` (`PUBLIC SCORE, BOARD(10, 10), NAME$`),
dimensioned as the module starts up. Other units use them with
[`DECLARE PUBLIC`](#declare-public).

### `SHARE`

In a `MODULE`: the module's own variables (same form as `PUBLIC`), shared by its
start-up code and its procedures but not visible outside it.

### `DECLARE PUBLIC`

Use variables a module made `PUBLIC`; an array is written with parentheses.
Put it before the names are used, in whichever unit uses them (the main program,
a procedure, another module).

<!-- fragment -->
```basic
LIBRARY "level.bas"
DECLARE PUBLIC MAPW, MAP()
PRINT MAP(1, MAPW)
```

### `LIBRARY`

`LIBRARY "file.bas" [, "file.bas" …]` — other source files whose `MODULE`s and
procedures join the program, loaded before it (and the ones *they* name, before
them; each file once). A library holds modules, and `SUB`/`FUNCTION`/`DEF`s of its
own — external procedures, callable from the whole program and seeing only their
own variables; any other statement belongs in a module (where it runs first) or
the main program. The file is looked for next to the file that names it — in the
browser IDE, among the open tabs (and the bundled examples). True BASIC's
statement; see [conformance](conformance.md#modules).

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
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=Xc3BCoJAFIXh_TzFyReo1tIi8qIDwyg2kS0HvToDdgOb3j-yVp3lD4evOtrCUAtTlyW1CmhabR2y3r-mkLIc1LlbQzkynzBH4TUZbUmRLVB970pdK7Kg7kSN07WFtgow5NDhgD222KnLmX4K_rfBwiMvLD0jBYb4Ow8IXoaZF0R5JvYDHiOizFGiTKv9IdUb)

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
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=8w8I8fT3U3ByDHZVMOBy8fRVcNQw0lTABIoKmXkpmcmpxQoGenpGXD6uIQqOGgaaCrYKhgZQnhGIZ2zAFRDk6QeRtUY3TVFBwdBAQQGkCgA)

### `OPTION ARITHMETIC`

Choose how numbers are represented. `DECIMAL` (the default) is exact,
arbitrary-precision decimal — `0.1 + 0.2 = 0.3` holds, and money sums never
drift. `NATIVE` uses the machine's IEEE double: about 16 significant digits and
a range up to ~1.8E308, but several times faster — worth it for games and
anything math-heavy. It applies to the whole program wherever it appears, so put
it at the top. PRINT shows 9 significant digits either way, so most programs
print the same under both.

```basic
OPTION ARITHMETIC NATIVE
FOR A = 0 TO 90 STEP 30
  PRINT SIN(A * PI / 180);  !  0   0.5   0.8660254   1
NEXT A
PRINT
IF 0.1 + 0.2 = 0.3 THEN PRINT "equal" ELSE PRINT "not equal"   ! not equal (binary)
```
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=PY2xCsJAFAT7-4oxVVSIL4kRRSwOOfFALyF5iG0EC0EURQv_XpRos7ADu1NW6suArb2ut079kmDV75xZlTWWBYKWzIRGXUUuBqraB6XxIbYMqDwj0qn059ADASQpvjmdTCQrxkBqgtsr1ny3xq-QJGWIJNlHkeTo2oXuOTrenu05wm0a90OX64MOfzT_Snw4Xdr7q2_e)

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
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=83ENUYhQsFUwMVJAB4oKqRUFOZnJmSVckQq2ChEK2gqGGIoUFBUSc4rzFdIy81IVHjVMUfBxDVHIzC3IyUxN4QKxHVUUbBWUMjKV0LUVlxRl5qUrlCQWpaeWFCuk5qUoZOYpqHC5ePoquGsYa4J1u2sYaSrYKlhaYlhaVJRYqZuak5qbmlcCNYQLAA)

### `REM`

Single-line comment. The exclamation mark `!` is the modern shorthand and
behaves identically: the rest of the line is ignored.

```basic
REM This is a comment.
! This is also a comment.
PRINT 1 + 2                ! inline comment after a statement
```
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=C3L1VQjJyCxWyCxWSFRIzs_NTc0r0eNSRAjmFOcjywQEefqFKBgqaCsYKaABRYXMvJzMvFSYYoXEtJLUIoVEheKSxJJUkAgXAA)

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
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=XY3BCoJAEIbvPsXfnCrqUJSB4gN0CYkOXdd10IVtVlwj6elDyaSdw8D8fP83-fV8uYFqttYR_maBMY3yEYmxxgkIERz2X4AUbUDFsDTNgAoqRXBrAMtdvNXO4u2E_Wry9cgoxT142CPDceh4fhjtrJME4sYqGlWWRqqfQRyEX9YIUzob_LNpWvaePbqa0bXKWCPVhEYf)

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
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=fdDRCoIwFAbge5_itO1qrNXm5siLwChCCBHTBwg0G4gX5vsTaSEz6b89fJyfP83iJIfiGidnQBhjFIKSsJwVgJJeOhN8QD4XSujdrxguLgKMAb7QMDCwAekiM0C9nX9jY0UhfaWXCgrJfKVdRQgjhPAJ8sA4iIyKB8aF9J3_g1BK55Ps8RQUAsqjQ3GJ8tMRfUxn60e_vjW2bqsSnn1n2xrutmpK7wU)

### `TAB`

Inside a `PRINT` list, `TAB(n)` pads spaces out to column `n` (1-based). If
the cursor is already past column `n` the call is a no-op (does *not* go
back or insert a newline).

```basic
PRINT "x"; TAB(10); "y"    ! x        y     ← 'y' begins at column 10
PRINT "hello"; TAB(3); "!" ! hello!         ← TAB(3) is past current col → no-op
```
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=Cwjy9AtRUKpQslYIcXTSMDTQtFZQqlRSUFBQUFSoUICCSjD5qG2CgnqlukJSanpmXrFCYolCcn5OaW6egqEBVwDEnIzUnJx8qFnGIKMUlRQUFcCiijDDwOZAFChkFisUJBaXKCSXFhWl5oENVHjUNkkhL183v4ALAA)

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
DIM SCORES(3)
INPUT SCORES(2)            ! array element target
```
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=bY49C8JAEET7_IrxsEjgbLSLRQhqkcJTjILaLXEJwn2Eyyn470UTQSXTLfP2MYXaHvZQGMgIjXemCSlEBhEVb1IoMpxCSKjxENnVX3xev_A58mFzVwOxdciS_ukocZI4_89xlqGvliUqZwxNWm7IU-AL7qRv3EbLYo1ysdmtynj2cfX3NPl1kff0AGs2bAMC-ZpD9AQ)

### `LINE INPUT`

Read a whole line from input (or a channel — see [`LINE INPUT #`](#line-input-))
into a single string target. No comma splitting.

```basic
LINE INPUT A$              ! prompt: "? "
LINE INPUT "Quote: "; Q$   ! prompt: "Quote: "
PRINT "you said: "; Q$
```
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=8_H0c1Xw9AsIDVFwVFFAAYoKBUX5uQUlVgpK9gpKXD4IlUqBpfklqVYKStYKgSqoKmEyXAFBnn4hCkqV-aUKxYmZKVDFXAA)

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
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=VcQrDsAgEEVRP6u49SMeIFHDR9Q0DWH_a2kqOeKM2EGSk-UU2ZoxCKc53d51P5uotErncEESkP-K7AM)

### `DATA`

Declare items to populate the program's `DATA` pool. Statements may appear
anywhere — they're all concatenated in source order at compile time.
Items are numbers and quoted strings. (The standard's unquoted strings, `DATA red,
green`, aren't supported yet.)

```basic
DATA "alpha", "beta"       ! strings
DATA 1, 2, 3               ! numbers
READ NAME1$, NAME2$, X, Y, Z
PRINT NAME2$; Z            ! beta 3
```
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=c3EMcVRQSswpyEhU0lFQSkotSVRSgABFheKSosy89GIuF5AiQx0FIx0FYwVUoKiQV5qblFpUzBXk6uii4Ofo62qoogOmjVR0FCJ0FCJ1FKK4AoI8_UKgotYKUagmgCxVMOYCAA)

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
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=RcghDoAwDEBR31N8fM2GrOrWCQwjZfc_CwISvnnJD19OUaqySw4PXGmS414zx3u6EnLlcS7caEY3gr8NClD5lAc)

---

## Selection

### `IF` / `THEN` / `ELSEIF` / `ELSE` / `END IF`

Single-line and block forms. The single-line form puts the conditional
statement (often `PRINT` or `GOTO`) directly after `THEN`. A bare line-number
after `THEN` (or `ELSE`) is shorthand for an implicit `GOTO`: `IF X < 0 THEN 900`
means `IF X < 0 THEN GOTO 900`. The block form opens with `THEN` at end-of-line
and closes with `END IF`, optionally nesting `ELSEIF` and `ELSE`.

<!-- fragment -->
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
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=XdDBCoJAEAbg-z7F354T7JIKQZgOIYiF6wssstbCZotu7x9tJeqc_jl8_8AIKilrkKWCcK7TnBi-SxKiuWAXhljMBoPsb4p98rUuqgY85X8TexMnW0Q-RcnPPF7GaWsURqvaEVYNHsxbTkE29RQCB-xXl_1tZaTTz16aOe2kNpOlUtAaettK194DaRb0yBlVOYT_AnsD)

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
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=bcwxDoAgEETRnlN8-w3ZRWNsLDGhAYIU3v8kRkNh4TS_enOURmLH6AVTPpswIQiz4L0XTB3UlnInuRyvJ-4YXoc_e6yE4fX1i7AK2__BDQ)

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
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=bY7BCsIwEETv-xXTswhK6UWsJwsGlqZIxeRYJEKhJJJEpH8vwSAKndsuu-8NNz0UamzoKHE9CW6gsEeF3xR4eLOOJsQdjI3Gw9lpxnj_3BLAGaOwwpZYyo66s2jTciEFKqL0orM5E9KovwRc2l4wNA41yr8yLsTcZphewxzgnxbO3kyW6mVpSW8)

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
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=bY67DoIwAEX3fsWhYdANBkYWYtEuxZSSsDJgMEFLah38eyMkxsE7ntxX3Vg0JTmuIc8yAbpeSYE7KYPqtaNuLJDwiH4hTiOz9wtDRJeFMKp3aHG22jg0f5RQCNF2FUerlNuZdC9g88vpKrdNk1Ii5c_qJ5EQxvgMd8YhzC8uwd_WA21XfTtMKpQ5rOgN)

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
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=c_cP8VcwMjDgCgjy9AtRUCrOziwoSE1R4jIyMFCAiuUk5qWAhAA)

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
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=Cwjy9AtRUEpKTcsvSlXicvcPDnVSMDQw4AqASCSmlaQWKXEFh_gHcBkaGChAhTPzijNTUpW4glxDQoP8uAA)

### `ON ... GOTO / GOSUB`

Computed jump. The index expression is **rounded** to an integer that selects a
1-based line-number from the list (`GO TO` / `GO SUB` two-word spellings also
work). `ON ... GOSUB` pushes a return address like a plain `GOSUB`, so `RETURN`
comes back to the statement after the `ON`. An optional `ELSE <statement>` runs
when the index is out of range; without `ELSE`, an out-of-range index raises
exception `10001`, catchable with [`WHEN ... USE`](#when--use).

<!-- fragment -->
```basic
ON CHOICE GOTO 100, 200, 300        ! CHOICE=2 jumps to line 200
ON K GOSUB 1000, 2000, 3000         ! call the K-th subroutine, then continue
ON N GOTO 100, 200 ELSE PRINT "out of range"
```

---

## Starting and stopping

### `STOP`

Halt the program at the current statement (cleanly, exit code 0). Useful
for halting before a labelled subroutine block.

```basic
PRINT "main done"
STOP
100 PRINT "subroutine"
RETURN
```
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=Cwjy9AtRUMpNzMxTSMnPS1XiCg7xD-AyNDBQCIBIFZcmFeWXlmSC5IJcQ0KD_LgA)

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
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=Cwjy9AtRUMpIzcnJV-Jy9XNRwAEUFVIrCnIykzNLFDISc0oUEksUSjJSFVLzUhTy0xRyEzPzuAA)

### `RUN`

Restart the program from the top, clearing all variable state. Rarely used
in modern style.

<!-- fragment -->
```basic
PRINT "boot"
RUN                        ! starts over (clears X, etc.)
```

---

## Operators

From the first to be worked out to the last; parentheses come before them all.

| Operators | | |
|---|---|---|
| `^` | power | `2 ^ 10` is 1024 |
| `-` `+` in front | negative, positive | `-2 ^ 2` is -4: the power comes first |
| `*` `/` `MOD` `REMAINDER` | multiply, divide, [modulo, remainder](#mod--remainder) | `7 / 2` is 3.5 |
| `+` `-` | add, subtract | |
| `&` | join strings | `"Arcade" & " BASIC"` |
| `=` `<>` `<` `<=` `>` `>=` | compare two numbers or two strings: -1 if true, 0 if not | `"B" < "a"` is -1 (by character code) |
| `NOT` | [logical](#logical-operators) not | |
| `AND` `BAND` | logical and, [bitwise](#bitwise-operators) and | |
| `OR` `XOR` `BOR` `BXOR` | logical or and exclusive or, bitwise or and exclusive or | |
| `IMP` | implies | |
| `EQV` | equivalent | |

`+` adds numbers only: join strings with `&`.

```basic
PRINT 2 + 3 * 4; (2 + 3) * 4     !  14  20
PRINT -2 ^ 2; 7 / 2              ! -4  3.5
PRINT "Arcade" & " BASIC"        ! Arcade BASIC
PRINT 3 > 2 AND "b" > "a"        ! -1
```
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=Vcq7CoNAEIXh3qf4nSJowkadXUkhCJuksRFR64CJvoDvXwQvYHKqc2a-pq3qHuWC5YwriNYeL4MlIWQONA2alRrlhRbcSFD-EmIc2Gu-U_HzZxgn4YRw9131kINuv-28e0uJ4usn8hZKZPjxJgu-)

### Logical operators

`AND`, `OR`, `NOT`, `XOR`, `IMP`, `EQV` operate on truth values. BASIC's
truth model: non-zero is true, `0` is false; the operators return `-1`
(all-ones) for true and `0` for false. **Extension:** `XOR`, `IMP` and `EQV` are
Microsoft BASIC's; ISO Full BASIC has `AND`, `OR` and `NOT`.

```basic
IF X > 0 AND X < 10 THEN PRINT "in range"
IF NOT FLAG THEN PRINT "off"
IF A OR B THEN PRINT "either"
PRINT A XOR B              ! exclusive-or
PRINT A IMP B              ! A → B  (false only if A true and B false)
PRINT A EQV B              ! A ≡ B  (true iff both sides agree)
```
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=bYzBCoJAEIbvPsWfpzwEdo9gJS2hVhMJr5azuiArrBr1AkHX3qBX60miDQSpuc1833xhgAxLuGB8hQwLzF2kG58jTkKewpYKOlcl2VYYgEcpgi1bj4xGCAMZogTeCJHsKtK29V0ZMmOMZgK6nOq-lWeaNXoww138azK8bo_PeSryuiU0qr5CCjB0uifkqoAHg5wh5O8Pf0P3pwmZRykEjk1XoZUFtchLTeRYbw)

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
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=Cwjy9AtRMFfw9XdRMFZAAYoKCoZcAWB5XWwKFBUUjBQUFDTScvLzi3RTMssyizPz8zShWswVglx9HT39XFyDEPpQjcRUoKigawgysqSoNC85sSQ1RZMLAA)

### Bitwise operators

`BAND`, `BOR`, `BXOR`, `BNOT` operate on the integer representation of
their operands. **Extension:** Arcade BASIC.

```basic
PRINT 12 BAND 10           !  8   (1100 & 1010 = 1000)
PRINT 12 BOR 10            ! 14   (1100 | 1010 = 1110)
PRINT 12 BXOR 10           !  6   (1100 ^ 1010 = 0110)
PRINT BNOT 0               ! -1   (bitwise complement)
```
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=Xc2xCsJAFAXRPl8x24gphHdFxMbCkMZmE0IKKwtli4CJQgI2frwouLpOf5i62fsWLSl2vkTGNwcbYC6ZMUMmY4vMLM_qyKomVTi0iuwRmZSww79zsI7s-GH2wwpftSSrN1voxU7ddO_GwPna3y6hD8OUZ08)

---

## Functions

A function's arguments go in parentheses: `SQR(2)`, `MID$(A$, 2, 3)`. A function whose
name ends in `$` gives a string. A few take no arguments and have no parentheses: `RND`,
`PI`, `DATE$`, `INKEY$`. They aren't reserved words, but a variable of the same name
would hide one, so it's best not to.

### Number functions

| | Gives |
|---|---|
| `ABS(x)` | x without its sign |
| `SGN(x)` | -1, 0 or 1: the sign of x |
| `INT(x)` | the whole number at or below x: `INT(-2.5)` is -3 |
| `TRUNCATE(x)` | x with its fraction dropped: `TRUNCATE(-2.5)` is -2 |
| `CEIL(x)` | the whole number at or above x |
| `ROUND(x)` | the nearest whole number; a half goes to the even one, so `ROUND(2.5)` is 2 and `ROUND(3.5)` is 4 |
| `SQR(x)` | the square root (x mustn't be negative) |
| `EXP(x)` | e to the power x |
| `LOG(x)` `LOG2(x)` `LOG10(x)` | the natural, base-2 and base-10 logarithm (x must be positive) |
| `MAX(x, y, …)` `MIN(x, y, …)` | the largest or smallest of any number of values |

`MOD` and `REMAINDER` are [operators](#mod--remainder): `7 MOD 3`.

```basic
PRINT ABS(-3); SGN(-3); SQR(16)                  !  3 -1  4
PRINT INT(-2.5); TRUNCATE(-2.5); CEIL(-2.5)      ! -3 -2 -2
PRINT ROUND(2.5); ROUND(3.5); ROUND(2.567)       !  2  4  3
PRINT EXP(1); LOG10(1000); LOG2(8)               !  2.71828183  3  3
PRINT MAX(3, 9, 4); MIN(3, 9, 4)                 !  9  3
```
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=bY_JCoMwFEX3_YrbXQJGMjhEsrJWJKDRxgj-_5cUG7Wl7VudC--8YfLWBdS3mTBFDebO7fDwRBQUP3UFFJgAssv0cq0LhMk0pwbBL66pQ3vkprV95MNlCkyCyd314-LuJDZHVh8s07wo6XuvBDJA7W67TkRQg37sBCeCcx6DJPr77M1NS6GlFlpt959DhnolKkGVIKMGg3Vn-vd4tYlP)

### Trigonometry

`SIN(x)`, `COS(x)`, `TAN(x)`, and their inverses `ATN(x)` (arctangent), `ASIN(x)` and
`ACOS(x)` (for x from -1 to 1); `SEC(x)`, `CSC(x)` and `COT(x)` are 1 over `COS`, `SIN`
and `TAN`. Angles are in radians: multiply degrees by `PI / 180`.

```basic
PRINT SIN(PI / 2); COS(0); ATN(1) * 4            !  1  1  3.14159265
PRINT SIN(30 * PI / 180)                         !  0.5   (30 degrees)
```
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=dYwxDoMwEMB2XmG2HAPNQYJaZUKdsqQR8IRG3eH_EoIBdcHyaucppoU5JpMjDzoJvD-zsRIYl2RUaHD8UYOe9q069a9u8FW-Jr2l4Tzp0wp31GBbDxzBt_zWUjapdg)

### `RND`

A random number from 0 up to, but not including, 1 — a new one each time. Scale it to
the range you want: `INT(RND * 6) + 1` rolls a die. Until a program runs
[`RANDOMIZE`](#randomize), the numbers come out the same every run, which helps while
testing. An argument, if there is one, is ignored.

```basic
RANDOMIZE
FOR I = 1 TO 5
  PRINT INT(RND * 6) + 1;        ! five rolls of a die
NEXT I
```
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=C3L0c_H39Yxy5XLzD1LwVLBVMFQI8Vcw5VJQCAjy9AtR8PQL0Qjyc1HQUjDTVNBWMLRWgAJFhbTMslSFovycnGKF_DSFRIWUzFQuP9eIEAVPLgA)

### `RANDOMIZE`

Reseed the random-number generator used by the `RND` builtin. A program
that never runs `RANDOMIZE` gets the **same** `RND` sequence every time it
runs (as the standard requires) — handy while debugging. `RANDOMIZE` with no
argument starts an unpredictable sequence from the clock; with a numeric
argument (an extension) it starts a repeatable one, the same for the same
number on every platform.

```basic
RANDOMIZE                  ! time-seeded: different every run
RANDOMIZE 42               ! deterministic — useful for reproducibility
PRINT RND
```
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=C3L0c_H39YxyVcAAigolmbmpusWpqSmpKVYKKZlpaalFqXklCqllqUWVCkWleVxBcN0mRhi6U1JLUotyM_Myi0sykxUeNUxRKC1OTSvNUUjLL1IoSi0oyk8pTc5MyszJLKnkCgjy9AtRCPJz4QIA)
### String functions

Positions start at 1 and count characters.

| | Gives |
|---|---|
| `LEN(s$)` | the number of characters in s$ |
| `MID$(s$, p)` `MID$(s$, p, n)` | the characters from position p on, or n of them |
| `LEFT$(s$, n)` `RIGHT$(s$, n)` | the first or last n characters |
| `POS(s$, t$)` `POS(s$, t$, p)` | where t$ first comes in s$ (looking from position p), or 0 if it doesn't |
| `UCASE$(s$)` `LCASE$(s$)` | s$ in capitals, or in small letters (`UPRC$` is another name for `UCASE$`) |
| `LTRIM$(s$)` `RTRIM$(s$)` | s$ without the spaces at its start, or at its end |
| `REPEAT$(s$, n)` | s$ n times over |
| `STR$(x)` | the number as `PRINT` shows it, without the spaces either side |
| `VAL(s$)` | the number s$ spells out (exception 3001 if it isn't one) |
| `CHR$(n)` | the character with code n (Unicode: `CHR$(65)` is `"A"`, `CHR$(960)` is `"π"`) |
| `ORD(s$)` | the code of s$'s first character |

```basic
LET S$ = "Arcade BASIC"
PRINT LEN(S$); POS(S$, "BASIC")                  !  12  8
PRINT LEFT$(S$, 6); "|"; MID$(S$, 3, 4); "|"; RIGHT$(S$, 5)   ! Arcade|cade|BASIC
PRINT UCASE$("abc"); LCASE$("XYZ"); REPEAT$("-", 3)           ! ABCxyz---
PRINT "["; LTRIM$("  hi  "); "]["; RTRIM$("  hi  "); "]"       ! [hi  ][  hi]
PRINT VAL("3.25") * 2; "<"; STR$(42); ">"                       !  6.5 <42>
PRINT CHR$(65); ORD("A")                                        ! A 65
```
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=dZBPSwMxEMXv_RTPIYdEdgvuP4TUQrpd7ULaLkla1GUPtQp69aTSDy_ZTQvSOofAvJn3y_B05WAZ7kDqc797fcNM2bqkUWPqlYOuVtwyIdGsLbcsAg1jgbO6Am4S4PbkvHestxRCgg4ksazng5JGyI6iqR8WYTEXPWa449A__W-BuCmVrRin3cuehIQO7ePTs29N1VTKMU4xRUjFn8PUrPz6_onjOJCoJQntTL1knID3D8AjqPO6uaDTCdV6sWv9sAu0rdKc0nGSk8A1EgmakIR1hvEs8fYpnad1jKwY55hkyTSwyoVhvMiFxNrMOalLQf_HUijy0S8)

### Date and time

`DATE$` is today's date as `"YYYYMMDD"`, and `TIME$` the time now as `"HH:MM:SS"`
(24-hour), both from the computer's clock.

```basic
PRINT DATE$; " "; TIME$                          ! 20261029 14:05:09, say
PRINT "This year is "; LEFT$(DATE$, 4)
```
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=Cwjy9AtRcHEMcVWxVlBSULJWCPH0dVVRwAkUFYwMjMwMDYwsFQxNrAxMrQwsdRSKEyu5AsAmKYVkZBYrVKYmFilkFoOM83F1C1HRAFugo2CiyQUA)

### Exception information

`EXTYPE` (the exception's number), `EXLINE` (the line it happened on) and `EXTEXT$`
(its message) describe the exception being handled, inside
[`WHEN … USE`](#when--use) and a [`HANDLER`](#handler).

### Constants

| | |
|---|---|
| `PI` | 3.14159265…, to 37 digits |
| `EPS` | 1E-14, a tiny positive number |
| `MAXNUM` | 1E308, the largest number (`INF` is the same) |

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
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=ZZA7b4NAEIR7fsW4OxwowGldgJMCKUaRhdKf8Mo-6V65R4j_fQR2TGK2mtF82h3tS7NHxcoMZZqh_hW7q0jeXjtUrMhQpNiimH05-vLmy1u-mf2UPyfJvupQY4sKi1mhN_YyEbuJeEL9SJAkRTrkg_AEH9Ufer2kFQ9OfMM6c4x9ABNak8NRKA8VfRjz_pzed7BNivX_Ziv4nkvuoKIMwsq5XndoWZU-HAyOa2-NpzvWtB9LTOgvcp7A_GfkjjJoo3Mv9ClK7q593g9NO25Y_Mg6oQMGEc5wZsgtuVwKTZD8YmJIfgA)

### `ZER`

In a MAT assignment RHS, produces a zero-filled array shaped like the
target's current bounds.

```basic
DIM A(3, 3)
MAT A = ZER                ! all elements = 0
```
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=c_H0VXDUMNZRMNbk8nUMUXBUsFWIcg1SQAOKCok5OQqpOam5qXklxQq2CgZcAA)

### `IDN`

In a MAT RHS, produces an identity matrix (1s on the diagonal, 0s
elsewhere). Requires a square target.

```basic
DIM I(3, 3)
MAT I = IDN                ! diag = 1, else 0
```
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=c_H0VfDUMNZRMNbk8nUMUfBUsFXwdPFTQAOKCimZiekKtgqGOgqpOcWpCgZcAA)

### `CON`

In a MAT RHS, produces an array of all-ones shaped like the target.

```basic
DIM A(4)
MAT A = CON                ! 1, 1, 1, 1
```
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=c_H0VXDUMNHk8nUMUXBUsFVw9vdTQAOKCoY6MMQFAA)

### `NUL$`

In a string-array MAT RHS, produces an array of empty strings shaped like
the target. The only constant supported for string arrays.

```basic
DIM S$(2)
LET S$(1) = "stale"
MAT S$ = NUL$              ! S$(1) = "", S$(2) = ""
```
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=c_H0VQhW0TDS5PJxDQGxDDUVbBWUiksSc1KVuHwdQWIKtgp-oT4qCihAEaFYSQdiBJjNBQA)

### `TRN`

In a MAT RHS, returns the transpose of a 2-D matrix. Bounds flip too:
`TRN` of an `m × n` matrix has shape `n × m`.

```basic
DIM A(2, 3), T(3, 2)
! ... populate A ...
MAT T = TRN(A)
```
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=c_H0VXDUMNJRMNbUUQjRMNZRMNLkUlTQ09NTKMgvKM1JLElVcARxuXwdQxRCFGwVQoL8NBw1uQA)

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
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=Tcm9CoAgFMXx3ac4bQo3sYvo1HClBqGapEV8_9eIPsDOdH78l7xDNBPYENJ71LYWiJ4Ik8EM382342f-Onc_PahdChJm5OPUYvDfgFqdDYTR2dgIdXSWCc761tQF)

---

## Files

File I/O uses *channels* — small positive integers introduced with `#`. A
channel is opened with `OPEN` and released with `CLOSE`. Two record types:
**DISPLAY** (text) — written with `PRINT #`, read with `INPUT #` / `LINE INPUT #`;
and **INTERNAL** (exact-value, `RECTYPE INTERNAL`) — written with `WRITE #`, read
with `READ #`, storing numbers at full precision so they round-trip exactly.
`SEQUENTIAL` or `STREAM` organization is supported.

### `OPEN`

Open a file on a channel. The full clause shape is
`OPEN #ch: NAME path$, [ACCESS kind, ORGANIZATION kind, CREATE kind, RECTYPE kind]`.
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
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=dY3BCoJAFEX38xXX0WUlbosWJrMQbBzSPkDqBYLOiD7R_j4KoSi6ywP3nEyVMAH2kCG3XWgd07DhmaXIjdLwoy10fFQwwQpxkqiiQH4uzbnExzxMfc20dra578D9aC8V0wBn4TqywpxSXb5csqktwVmSP5AnJ0WS5YWCH4n_-VR_1eGhp-q61G91Q2jHgUFzPbDIUq2Wz9MWB9_kELyrDw)

`ACCESS` values: `INPUT`, `OUTPUT`, `OUTIN` (read+write).
`ORGANIZATION` values: `SEQUENTIAL`, `STREAM`.
`CREATE` values: `NEW` (must not exist), `OLD` (must exist), `NEWOLD`
(open if present, create otherwise).
`RECTYPE` values: `DISPLAY` (text — the default, use `PRINT #`/`INPUT #`) or
`INTERNAL` (exact-value records — use `WRITE #`/`READ #`).

### `CLOSE`

Release the file behind a channel and flush any pending writes.

```basic
CLOSE #1
```
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=c_bxD3ZVUDbkAgA)

### `PRINT #`

Like `PRINT`, but to a channel. Same item formatting rules — expressions,
commas (zone padding), semicolons (no padding), `TAB(n)`.

```basic
OPEN #1: NAME "scores.txt", ACCESS OUTPUT
PRINT #1: "name", "score"      ! tab-separated via zone padding
PRINT #1: "Adam", 42
CLOSE #1
```
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=8w9w9VNQNrRS8HP0dVVQKk7OL0ot1iupKFHSUXB0dnYNDlbwDw0JCA3hCgjy9AsBK1XKS8xNVdKBqlZSAANFhZLEJN3i1ILEosSS1BSFssxEhar8vFSFgsSUlMy8dGT9jimJuUo6CiZGXM4-_sGuCsqGXAA)

### `INPUT #`

Read comma-separated fields from a channel into variables. No interactive
retry loop — a malformed line raises a runtime error.

```basic
OPEN #1: NAME "data.csv", ACCESS INPUT
INPUT #1: NAME$, SCORE
CLOSE #1
PRINT NAME$; "scored"; SCORE
```
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=8w9w9VNQNrRS8HP0dVVQSkksSdRLLi5T0lFwdHZ2DQ5W8PQLCA3hApNwdSo6CsHO_kGuXM4-_sGuCsqGXAFBnn4hEDlrBaXi5Pyi1BQla6gqAA)

### `LINE INPUT #`

Read a whole line (newline-terminated) from a channel into a string
variable.

```basic
OPEN #1: NAME "log.txt", ACCESS INPUT
LINE INPUT #1: ENTRY$
CLOSE #1
PRINT ENTRY$
```
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=8w9w9VNQNrRS8HP0dVVQyslP1yupKFHSUXB0dnYNDlbw9AsIDeHy8fRzhTDBal39QoIiVbicffyDXRWUDbkCgjz9QmCiAA)

### `WRITE #`

Write **exact-value** records to an `INTERNAL` channel. Each item is a field;
numbers are stored at full precision (no display rounding), strings verbatim.
The counterpart to `READ #`. (`WRITE`/`READ` are the INTERNAL statements;
`PRINT`/`INPUT` are DISPLAY-only.)

```basic
OPEN #1: NAME "save.dat", ACCESS OUTPUT, RECTYPE INTERNAL
WRITE #1: HISCORE, PLAYER$        ! HISCORE round-trips exactly, not as rounded text
CLOSE #1
```
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=NYw9C8IwGAb3_IrH6hgFV7cQXjAQk5CkSMdgMxSklSZK_ffi163HnXVksN4fYMSJ0JT0yLs-1YZDSEkhwLbRtZHDk4ydIygTyRuh2dmrSJ_0qIK0njicFh35DX6s_gbzdB_7bZ2HW0Fe0qVenxzjVJHK1-UeNS-VSW3De8pe)

### `READ #`

Read exact-value records back from an `INTERNAL` channel, one field per target,
in order — numbers come back byte-exact, strings verbatim. (Distinct from the
`DATA`-reading `READ`: the `#channel` is what selects file input.)

```basic
OPEN #1: NAME "save.dat", ACCESS INPUT, RECTYPE INTERNAL
READ #1: HISCORE, PLAYER$
CLOSE #1
```
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=HcsxCoQwEAXQPqf46JZBsN1umP2wgTgJk2xhKegJVjy_4OtfqTSM8xsmCzH8t-uY9u0cIkSVrSFZ_fUIp_a1Esk63SQHp3ye-E1NizOiZlnpr6C5NGKcww0)

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
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=dcixDsIgFEbhnaf45QWEuXHSm5SkuRLFFEeiREm1EqCDb-_g4OTZvjP2xCC_JevMnmFYAAM5eGygsYbC31YoIdVYUZa5pWdELOVVoJXSArAHww6yTinneJXidKTfvYTldm8IDY80R9mB_GCYOsj2zl-7syVBvMPYE4sP)

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
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=TYsxCsJAFAX7PcWYE6yliIXEJ1mQ72J-SDzA1spGC28vkhRONcXM2MnQ1Cp7uhrJAlzkGAdigHReFP-F7XHo9ZfvYtyGoVeAfEvmNO-5VEqtj8rr8yzNHk1-z2JlwzLJToydLHwB)

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
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=hY8xC8IwFIT3_Iozq0vESaqD6BMDNYb2iTrW9kkFUUkj0n8vXapO3nQcdx9cSow5M20855jBqP2aHOiwIM9262CdAtLfUm-HGCnArj7JFGNwR1jMdzl9cSbGGAX4zDqGbp5lKVJJheIcJeikJyTQMVyk0WqX02cA1MWtukpAU7wQ24foBHTgo-9KGXF2xF8NEKSJRYiItcA6nO5Vq8gt0Z1Wbw)

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
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=hY6xDoJAEET7_Yrhegs_wZybcM1C5Ai2F104jCCB4_-NUaOJha-cTN5Mk7OAj5ZL7wqBEwLsrq74K9wSUB6ceJjQJp03beiv_dgZ_JDBFuKd1IzLOkwLos5KdcUfxSmsXUzmsfOu_iHDrMs6KKawJKSoz4fEskeTs9DLfL6NaugO)

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
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=bZPNctowFIX3PMWJJwuYKhN-0i5g5RKnYUqAsZ2Qdiesi9FESK4kIPQN-iB9sT5Jx8ZtSIgW8uae7xxd35tEKeajyfV0jjZD71Ob4aLD0EF9zrAzVglkxlghNfcEy3VOaCpaemZlvvJsYbw3a-ZN0WokUYqHUTSfTeO0RHZYdeMIuCJLkB6Ka-EgNbSxa67kTxIQtJUZwRU8o4p1HT2MhtFRxiPgGZpcbLnOSLTgNosLS5nnOlcEs4RfEdzGLt-Q3g33iuS5zcnjCKYr2CFbxRqORzME00mA03OGTMkCwvKd1Dm8qcRbSbvCWI9mKbsMpjc3QatRwcajSYQk_TaO0D2FdeCMkoKhC8HdigRDD8J4T6JSz6ajSVrLe6diYXwpLdTGlULuPFnpnl6Mh9PxNMbViS9XztTwS6TRY_kJ4yg8CBrDcRTG77z-qAnE7aFv0hWK7xuNL3E4u61ckz7arD1Ap80-DtBts_ZBY7SmzJOAo3xN2js0__z63UVhpPauVROqVEkfHdYZoMu6x7Zrbp_IurqyDFxbXZVXl_X-Vy6lUiRQGLXPja6Mem-MymczhGnp1Eeg-IJUUMs9PftB1aU-3lQ_sz3DfTKafIFc5-d9bBuNMPn6b4jHDDHDZ4b0VcMscYEFz56Qbawl7eHIe6nzcke8wZZbyReKXMWqpzkZfY8wZ7hluD9_YdVrtJPCry5XVG4pPmCjZTl_d1EaxUk5g-ltFAetincXPtajcPfez-QFX0gl_R4_NmQluQFM4aXRXCFAkobpfQIXIFN846jxFw)

To build a curve or any shape with a variable number of vertices, drive `GRAPH`
from a loop (coordinate expressions may reference variables) — see
[`examples/graphics.bas`](../examples/graphics.bas).

### Pictures and colours: `MAT GRAPH CELLS`, `SET COLOR MIX`

`MAT GRAPH CELLS` fills a rectangle with a grid of coloured cells, one per element
of a two-dimensional array — an image, a sprite, a texture, a whole frame of
pixels. The first subscript runs across and the second up or down; the element
with the lowest subscripts goes at the first corner, the highest at the second.
Each element's rounded value is its colour; a colour that doesn't exist, such as
`-1`, leaves that cell undrawn — see-through, for sprites.

```basic
OPTION BASE 1
DIM SPRITE(8, 8)                ! SPRITE(x, y): 8 across, 8 down
MAT READ SPRITE                 ! (filled from DATA, say)
MAT GRAPH CELLS, IN 10, 20; 18, 12: SPRITE   ! top-left (10,20) to bottom-right (18,12)

SET COLOR MIX (20) 0.6, 0.3, 0.1   ! colour 20 is now brown: red, green, blue, each 0-1
ASK COLOR MIX (20) R, G, B      ! read it back; optional " STATUS s" (11086: no such colour)
```
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=XY_NisJAEITveYrSUwKtzGRBhngaNbhh1UgmC3vNz6hhY0YmI65vvyiKYB_6UF31UZ1u8yTdYCZVDO4tkjXUNkvy2BcEEeBtBs_rH-EaRBAoKmv6niBQm0vnrWWOLJaLh-89jwH8XdO2usbOmiMWMpeEvrgG9-Qyk9tPzOPVShGSDTgjhGwKLgg8jF7QAZw5jVq9c_A5o5AFcAalcc4cR7bZH266IB4GnqfiHPN0lWZYJz_wb142nhDY-OO2-B1XmdacLUKGpkdnLiituXQRrK4Je6t1RyjbsyboojqAjbgn1dc7NyMsCbPnq1YXNRqHsqh-pzAn15iuaDGEymX-rdAP4XPOxCRCZ9Cfq8OjR-D9Aw)

There are 256 colours, 0–255 (`ASK MAX COLOR`): 0 is the background (black), 1
white, 2–15 the bright and dark primaries, and 16–255 the xterm 256-colour palette
until `SET COLOR MIX` changes them. Terminals show each as the nearest colour they
have. Both are ANSI Full BASIC (§13.3.4.4, §13.2); `CELLS` isn't a reserved word.
See [`examples/maze3d.bas`](../examples/maze3d.bas) — textured walls (one texture
column per screen column), floor and ceiling, and sprites with see-through parts.

### Mouse: `LOCATE POINT` / `GET POINT`, `GET MOUSE`

Pointer input comes back in the same problem coordinates you draw with. It works
where there's a mouse over the drawing — the browser IDE.

<!-- fragment -->
```basic
LOCATE POINT : X, Y             ! wait for a click; X, Y in window coordinates
GET POINT : X, Y                ! the same (no picture transforms here)

DO                              ! drag and drop: sample the pointer every frame
  GET MOUSE X, Y, S             ! S: 0 up, 1 held, 2 just pressed, 3 just released
  IF S = 2 THEN ...             ! (-1 if there's no mouse at all)
  SLEEP 0.03
LOOP
```

`LOCATE POINT` is ANSI Full BASIC graphic input (§13.4): it waits like `INPUT`,
ignores clicks outside the window, and with no mouse asks for typed `x, y`
instead. `GET MOUSE` is an extension in the style of True BASIC's; it never waits.
See [`examples/kanban.bas`](../examples/kanban.bas) — drag cards between lanes.
`LOCATE`, `GET` and `MOUSE` aren't reserved words.

---

## Keys, mouse and time

For games and other programs that respond as you use them: a loop that reads the keys
(and the mouse), updates, draws, and waits a moment with [`SLEEP`](#sleep) before the
next frame. In the browser IDE, keys go to the program while its screen has the focus
(click it first); the IDE keeps Ctrl, ⌘ and Alt combinations, and F5, F6, F10 and F11,
for itself.

### `INKEY$`

The next key pressed, without waiting; `""` if there isn't one. A key that types a
character gives the character (`"a"`, `"A"`, `" "`, and Enter `CHR$(13)`, Tab
`CHR$(9)`, Backspace `CHR$(8)`, Esc `CHR$(27)`). Other keys give `CHR$(0)` followed by a
code, as in GW-BASIC: the arrows 72 ↑, 80 ↓, 75 ←, 77 →; Home 71, End 79, Page Up 73,
Page Down 81, Insert 82, Delete 83; F1–F10 59–68, F11 133, F12 134. Each press comes
once, which suits commands and typing; for moving while a key is held, use
[`KEYDOWN`](#keydown). **Extension:** Microsoft BASIC.

```basic
PRINT "Press keys; Q stops"
DO
  LET K$ = INKEY$
  IF K$ = CHR$(0) & CHR$(72) THEN PRINT "up arrow"
  IF LEN(K$) = 1 THEN PRINT "you typed "; K$
  SLEEP 0.02
LOOP UNTIL K$ = "q"
```
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=VY2xCsIwGIT3PMXxE6RdJHZxKJ000pCQxhoHx4KdHBKTFunbi2RyO477vnOjsh7k0pwzXvOWW1yRlxAzsfPAACM9NEcHZbV8cAaoSylO_cgrUWNX0rGp4Xtp4YpyjZhSCh8qiJG20rxGh8PfbAsrli3OT1AL_fPfjJQOYi8aZobB4W69MuWS3sS-)

### `KEYDOWN`

`KEYDOWN(key$)` is -1 while that key is held down, and 0 when it isn't: what a game
needs to move something every frame a key is held. Name the key the way `INKEY$` gives
it (`"a"`, `" "`, `CHR$(0) & CHR$(72)`) or by its W3C name, in any case: `"ArrowLeft"`,
`"Space"`, `"Enter"`, `"KeyW"`, `"ShiftLeft"`, `"F1"`. A W3C name is a place on the
keyboard, so `"KeyW"` is where W is on a US keyboard, whatever the layout. `KEYDOWN`
doesn't use up presses: `INKEY$` still gets each one. In a terminal, which reports
presses but not releases, a key counts as held while it repeats, and Shift counts as
held along with a key pressed with it, so `KEYDOWN("ShiftLeft")` tells Shift+→ from →.
**Extension:** Arcade BASIC (like QB64's `_KEYDOWN`).

```basic
SET WINDOW 0, 100, 0, 100
LET X = 50
DO
  IF KEYDOWN("ArrowLeft") AND X > 5 THEN LET X = X - 1
  IF KEYDOWN("ArrowRight") AND X < 95 THEN LET X = X + 1
  CLEAR
  GRAPH AREA: X - 5, 10; X + 5, 10; X + 5, 14; X - 5, 14
  SLEEP 0.02
LOOP UNTIL INKEY$ = "q"
```
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=bc7LCsIwFATQfb5iCC4Uq6TSLrRWCOZqiyEpaaTtB_haiUXw98XiA9TV3MWcy5TkUeVG2QoiQChE8EymyaNGilgwZRmQr7ChRtnK9Lls2_NN7_ZXPoA0CjUWiOEzMnixGiOE_5g7HY4fN8f0Bw47uNQkHQPWThYZpCM5657Gj3lJV_s6o-RdiBhQaqICYiwmTFtbYGt8rpGbDTU9pOAXzu4)

### `SLEEP`

Pause execution for a number of seconds (fractional allowed). Paces real-time
loops — pair it with the `INKEY$` function (non-blocking keyboard: one result per
key press) and `KEYDOWN(key$)` (-1 while a key is held) for games.
**Extension:** Microsoft BASIC (QuickBASIC), not ISO/ECMA Full BASIC — see the
[Extensions table](conformance.md#extensions-beyond-isoecma-full-basic).

```basic
DO
  LET K$ = INKEY$          ! "" if no key is waiting
  IF K$ = "q" THEN EXIT DO
  IF KEYDOWN("ArrowLeft") THEN LET X = X - 1   ! moves every frame it's held
  IF KEYDOWN("ArrowRight") THEN LET X = X + 1
  ! ... update + redraw ...
  SLEEP 0.05               ! ~20 frames/second
LOOP
```
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=bY3BSsNAFEX38xWnQ0GlGFPBZRdCphgaMkUDTZfBvLRDbaKT2NCN3y4xIgi9y3vfOyeyChKTsZqyIE5XZjvlLxO0xlXUDQc541r6wnWu3imIl-OP_tBkTybF5HFGZH8ns43sJr3Wj943fSJVp2_Gs0GWsyDnlvmP49icpEVO4s9UvjgKrrtq2ctbeQn27Hb7C7QZczXQgiDg870sOmGGl9IX_dApeEmMWRMG4QP_M-HrPhzV7V0rr01dqsTatfoG)
### The mouse

`GET MOUSE x, y, s` reads where the pointer is and what its button is doing, without
waiting; `LOCATE POINT` waits for a click. Both are in [Graphics](#mouse-locate-point--get-point-get-mouse),
since they answer in the window's coordinates.

---

## Sound

The GW-BASIC and QuickBASIC sound statements. They play in the browser and in a
terminal, and `arcade-basic run file.bas --wav out.wav` records them;
[audio-extension.md](audio-extension.md) has the full details.

### `SOUND`

`SOUND freq, duration` emits a tone: `freq` in Hz (37–32767), `duration` in
clock ticks of 18.2/second (so `duration/18.2` seconds). `duration = 0` stops
the current sound. **Extension:** Microsoft BASIC (GW-BASIC/QuickBASIC) — see the
[Extensions table](conformance.md#extensions-beyond-isoecma-full-basic). Render
audio with `arcade-basic run file.bas --wav out.wav`.

```basic
SOUND 440, 9               ! 440 Hz (concert A) for about half a second
```
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=C_YP9XNRMDEx0FGwVEAFiiBhBY8qBY3k_Lzk1KISBUdNhbT8IoXEpPzSEoWMxJw0hUSF4tTk_LwULgA)

### `BEEP`

`BEEP` emits the standard alert tone (~800 Hz for ~0.25 s, like `PRINT CHR$(7)`).
**Extension:** Microsoft BASIC (GW-BASIC/QuickBASIC).

```basic
BEEP
```
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=c3J1DeACAA)

### `PLAY`

`PLAY notes$` plays a Music Macro Language (MML) string. **Extension:** Microsoft
BASIC (GW-BASIC/QuickBASIC). Commands (case-insensitive, spaces ignored):

| | |
|---|---|
| `A`–`G` | a note in the current octave; suffix `#`/`+` sharp, `-` flat; optional length digit + dots |
| `O n` / `>` / `<` | octave 0–6 (default 4) / up one / down one |
| `N n` | note by number 0–84 (0 = rest) |
| `L n` | default note length 1–64 (default 4 = quarter) |
| `P n` | rest of length n |
| `T n` | tempo: quarter notes per minute, 32–255 (default 120) |
| `MN` / `ML` / `MS` | normal / legato / staccato articulation |
| `MF` / `MB` | foreground / background — foreground waits for the music to finish; background plays asynchronously (up to 32 buffered tones) |

```basic
PLAY "T120 O4 L8 CDEFGAB>C"      ! a C major scale
PLAY "MS T140 O5 L16 C C G G"    ! staccato
```
[▶ Try it](https://3583bytes.github.io/Arcade-BASIC/#code=C_BxjFRQCjE0MlDwN1HwsVBwdnF1c3d0snNWUgADRYVEBWeF3MSs_CKF4uTEnFSuALAW32CFEEMTAwV_UwUfQzMFZwVnBXcFdyWIluKSxOTkxJJ8LgA)

---

## Notes on coverage

- Every keyword and function above is **implemented end-to-end** — parser → semantic
  analysis → compiler → bytecode VM, which runs programs everywhere: the
  browser IDE, `arcade-basic run`, and standalone binaries.
- A handful of other keywords are reserved by the lexer for parts of the standard
  not implemented yet (DISPLAY, PLOT, DRAW, POLYGON, TRANSFORM, MASK, MARGIN, ZONEWIDTH,
  PIC, PICTURE, FORMAT, TEMPLATE, TIMEOUT, BREAK, TRACE, COLLATE, native types like
  FIXED/REAL/STRING/NUMERIC, FILES/FILETYPE, RANDOM, REWRITE, RSET, …) — the parser
  rejects statements built around them with a clear error. See
  [conformance.md](conformance.md) for the full deviation list.
- Known gaps: `LBOUND` and `UBOUND` are recognised but can't be called yet (passing
  them an array name is rejected); `MOD` and `REMAINDER` work as operators
  (`7 MOD 3`) but not yet in the standard's function form (`MOD(7, 3)`); and `DATA`
  doesn't take the standard's unquoted strings.
