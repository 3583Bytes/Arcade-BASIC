! kanban.bas — a three-lane Kanban board (To-Do / In-Progress / Done) drawn with
! the ECMA-116 §13 graphics module: each lane is a filled colored GRAPH AREA
! column, and every card is a GRAPH TEXT label on top of it.
!
! In the terminal IDE (arcade-basic-ide) the board appears on the Graphics tab
! and you type commands into the field right below it. Headless, you can render
! a frame to SVG by piping commands and finishing with Q:
!   printf 'A Ship the demo 1\nM 5 2\nD 6\nQ\n' | \
!     dotnet run --project src/ArcadeBasic.Cli -- run examples/kanban.bas --svg board.svg
!
! Commands are one line each (args inline); a status line on the board echoes
! the result. INPUT splits on commas, so card text shouldn't contain a comma.

OPTION BASE 1

! Pad (or truncate) S$ to exactly W columns so card text fits its lane.
FUNCTION PAD$(S$, W)
  PAD$ = LEFT$(S$ & REPEAT$(" ", W), W)
END FUNCTION

FUNCTION LANENAME$(LN)
  IF LN = 1 THEN
    LANENAME$ = "To-Do"
  ELSEIF LN = 2 THEN
    LANENAME$ = "In-Progress"
  ELSE
    LANENAME$ = "Done"
  END IF
END FUNCTION

! Parse S$ as a non-negative integer, or -1 if it isn't all digits.
FUNCTION TONUM(S$)
  IF S$ = "" THEN
    TONUM = -1
  ELSE
    LET R = 0
    LET OK = 1
    FOR I = 1 TO LEN(S$)
      LET D$ = MID$(S$, I, 1)
      IF D$ >= "0" AND D$ <= "9" THEN
        LET R = R * 10 + (ORD(D$) - ORD("0"))
      ELSE
        LET OK = 0
      END IF
    NEXT I
    IF OK = 1 THEN
      TONUM = R
    ELSE
      TONUM = -1
    END IF
  END IF
END FUNCTION

LET MAXT = 64
LET BOARD$ = "kanban"
DIM TASK$(64)
DIM LANE(64)
DIM TOK$(64)
LET NT = 0

! Per-lane drawing metadata (set once). Colors are graphics palette indices.
DIM LNAME$(3)
DIM LCOL(3)
DIM LX(3)
LET LNAME$(1) = "TO-DO"
LET LNAME$(2) = "IN-PROGRESS"
LET LNAME$(3) = "DONE"
LET LCOL(1) = 4         ! blue
LET LCOL(2) = 7         ! yellow
LET LCOL(3) = 3         ! green
LET LX(1) = 1
LET LX(2) = 28
LET LX(3) = 55
LET COLW = 25           ! column width, in canvas cells

! scratch / argument variables
LET RAW$ = ""
LET MSG$ = "A text lane | M n lane | D n | N name | S/L name | H help | Q quit"
LET NTOK = 0
LET CMD$ = ""
LET T$ = ""
LET NM$ = ""
LET N = 0
LET L = 0
LET HAVET = 0
LET HAVEL = 0
LET HAVEN = 0
LET HAVENM = 0

! --- seed the board from DATA (terminator is a lone "*") ----------------
DO
  READ T$
  IF T$ = "*" THEN EXIT DO
  READ L
  LET NT = NT + 1
  LET TASK$(NT) = T$
  LET LANE(NT) = L
LOOP

DATA "Design the board layout", 3
DATA "Write the PAD$ helper",    3
DATA "Render three lanes",       2
DATA "Wire up the add command",  2
DATA "Wire up move + delete",    1
DATA "Persist board to a file",  1
DATA "Add this to the README",   1
DATA "*", 0

! --- main loop ----------------------------------------------------------
DO
  GOSUB 9000                       ! draw the board (graphics)
  INPUT RAW$                       ! command line; prompt is on the board
  GOSUB 9100                       ! tokenize RAW$ into TOK$(), NTOK
  IF NTOK > 0 THEN
    LET CMD$ = UCASE$(LEFT$(TOK$(1), 1))
    IF CMD$ = "Q" THEN
      EXIT DO
    ELSEIF CMD$ = "A" THEN
      GOSUB 9350                   ! parse inline add args
      GOSUB 9400
    ELSEIF CMD$ = "M" THEN
      LET HAVEN = 0
      LET HAVEL = 0
      IF NTOK >= 2 THEN
        LET N = TONUM(TOK$(2))
        LET HAVEN = 1
      END IF
      IF NTOK >= 3 THEN
        LET L = TONUM(TOK$(3))
        LET HAVEL = 1
      END IF
      GOSUB 9500
    ELSEIF CMD$ = "D" THEN
      LET HAVEN = 0
      IF NTOK >= 2 THEN
        LET N = TONUM(TOK$(2))
        LET HAVEN = 1
      END IF
      GOSUB 9600
    ELSEIF CMD$ = "N" THEN
      IF NTOK >= 2 THEN LET BOARD$ = TOK$(2)
      LET NT = 0
      LET MSG$ = "New empty board: " & BOARD$
    ELSEIF CMD$ = "S" THEN
      IF NTOK >= 2 THEN LET BOARD$ = TOK$(2)
      GOSUB 9800
    ELSEIF CMD$ = "L" THEN
      IF NTOK >= 2 THEN LET BOARD$ = TOK$(2)
      GOSUB 9900
    ELSEIF CMD$ = "H" THEN
      LET MSG$ = "A text lane | M n lane | D n | N name | S/L name | Q quit"
    ELSE
      LET MSG$ = "Unknown command '" & TOK$(1) & "' — H for help"
    END IF
  END IF
LOOP
END

! ======================================================================
! 9000  RENDER — draw three filled colored columns with text cards.
! ======================================================================
9000 REM ---- graphics render ----
CLEAR
SET WINDOW 0, 80, 0, 24
FOR LN = 1 TO 3
  LET X0 = LX(LN)
  LET X1 = X0 + COLW
  SET LINE COLOR LCOL(LN)
  GRAPH LINES: X0, 1; X1, 1; X1, 23; X0, 23; X0, 1     ! lane box (outline)
  GRAPH LINES: X0, 21; X1, 21                          ! divider under the header
  LET CNT = 0
  FOR I = 1 TO NT
    IF LANE(I) = LN THEN LET CNT = CNT + 1
  NEXT I
  SET TEXT COLOR LCOL(LN)
  GRAPH TEXT, AT X0 + 1, 22: LNAME$(LN) & " (" & LTRIM$(STR$(CNT)) & ")"
  SET TEXT COLOR 1
  LET ROW = 20
  FOR I = 1 TO NT
    IF LANE(I) = LN THEN
      GRAPH TEXT, AT X0 + 1, ROW: PAD$("#" & LTRIM$(STR$(I)) & " " & TASK$(I), COLW - 1)
      LET ROW = ROW - 1
    END IF
  NEXT I
NEXT LN
SET TEXT COLOR 1
GRAPH TEXT, AT 1, 0: "Board " & BOARD$ & ":  " & MSG$
RETURN

! ----------------------------------------------------------------------
! 9100  TOKENIZE — split RAW$ into TOK$(1..NTOK) on runs of spaces.
! ----------------------------------------------------------------------
9100 REM ---- tokenize ----
LET NTOK = 0
LET CUR$ = ""
LET TL$ = LTRIM$(RTRIM$(RAW$))
FOR I = 1 TO LEN(TL$)
  LET CH$ = MID$(TL$, I, 1)
  IF CH$ = " " THEN
    IF CUR$ <> "" THEN
      LET NTOK = NTOK + 1
      LET TOK$(NTOK) = CUR$
      LET CUR$ = ""
    END IF
  ELSE
    LET CUR$ = CUR$ & CH$
  END IF
NEXT I
IF CUR$ <> "" THEN
  LET NTOK = NTOK + 1
  LET TOK$(NTOK) = CUR$
END IF
RETURN

! ----------------------------------------------------------------------
! 9350  Parse inline ADD args: "A <text...> <lane>" (trailing 1-3 = lane).
! ----------------------------------------------------------------------
9350 REM ---- parse inline add ----
LET HAVET = 0
LET HAVEL = 0
IF NTOK >= 2 THEN
  LET HITOK = NTOK
  LET LASTV = TONUM(TOK$(NTOK))
  IF NTOK >= 3 AND LASTV >= 1 AND LASTV <= 3 THEN
    LET L = LASTV
    LET HAVEL = 1
    LET HITOK = NTOK - 1
  END IF
  LET T$ = ""
  FOR I = 2 TO HITOK
    IF T$ = "" THEN
      LET T$ = TOK$(I)
    ELSE
      LET T$ = T$ & " " & TOK$(I)
    END IF
  NEXT I
  IF T$ <> "" THEN LET HAVET = 1
END IF
RETURN

! ======================================================================
! 9400  ADD — needs text + lane inline (e.g. "A Buy milk 1").
! ======================================================================
9400 REM ---- add ----
IF NT >= MAXT THEN
  LET MSG$ = "Board is full"
  RETURN
END IF
IF HAVET = 0 THEN
  LET MSG$ = "Add needs text, e.g. A Buy milk 1"
  RETURN
END IF
IF HAVEL = 0 THEN
  LET MSG$ = "Add needs a lane 1-3, e.g. A " & T$ & " 1"
  RETURN
END IF
LET L = INT(L)
IF L < 1 OR L > 3 THEN
  LET MSG$ = "Lane must be 1, 2 or 3"
  RETURN
END IF
LET NT = NT + 1
LET TASK$(NT) = T$
LET LANE(NT) = L
LET MSG$ = "Added #" & LTRIM$(STR$(NT)) & " to " & LANENAME$(L)
RETURN

! ======================================================================
! 9500  MOVE — "M n lane".
! ======================================================================
9500 REM ---- move ----
IF HAVEN = 0 OR HAVEL = 0 THEN
  LET MSG$ = "Move needs a task # and lane, e.g. M 1 3"
  RETURN
END IF
LET N = INT(N)
IF N < 1 OR N > NT THEN
  LET MSG$ = "No such task #" & LTRIM$(STR$(N))
  RETURN
END IF
LET L = INT(L)
IF L < 1 OR L > 3 THEN
  LET MSG$ = "Lane must be 1, 2 or 3"
  RETURN
END IF
LET LANE(N) = L
LET MSG$ = "Moved #" & LTRIM$(STR$(N)) & " to " & LANENAME$(L)
RETURN

! ======================================================================
! 9600  DELETE — "D n"; remaining tasks renumber.
! ======================================================================
9600 REM ---- delete ----
IF HAVEN = 0 THEN
  LET MSG$ = "Delete needs a task #, e.g. D 4"
  RETURN
END IF
LET N = INT(N)
IF N < 1 OR N > NT THEN
  LET MSG$ = "No such task #" & LTRIM$(STR$(N))
  RETURN
END IF
FOR I = N TO NT - 1
  LET TASK$(I) = TASK$(I + 1)
  LET LANE(I) = LANE(I + 1)
NEXT I
LET NT = NT - 1
LET MSG$ = "Deleted (remaining renumbered)"
RETURN

! ======================================================================
! 9800  SAVE — write the board to <name>.board.
! ======================================================================
9800 REM ---- save ----
LET FILE$ = BOARD$ & ".board"
OPEN #1: NAME FILE$, ACCESS OUTPUT
PRINT #1: STR$(NT)
FOR I = 1 TO NT
  PRINT #1: STR$(LANE(I))
  PRINT #1: TASK$(I)
NEXT I
CLOSE #1
LET MSG$ = "Saved " & LTRIM$(STR$(NT)) & " task(s) to " & FILE$
RETURN

! ======================================================================
! 9900  LOAD — replace the board with <name>.board (handler-guarded).
! ======================================================================
9900 REM ---- load ----
LET FILE$ = BOARD$ & ".board"
WHEN EXCEPTION IN
  OPEN #1: NAME FILE$, ACCESS INPUT
  LINE INPUT #1: LN$
  LET NT = VAL(LN$)
  FOR I = 1 TO NT
    LINE INPUT #1: LN$
    LET LANE(I) = VAL(LN$)
    LINE INPUT #1: TASK$(I)
  NEXT I
  CLOSE #1
  LET MSG$ = "Loaded " & LTRIM$(STR$(NT)) & " task(s) from " & FILE$
USE
  LET MSG$ = "Could not load " & FILE$
END WHEN
RETURN
