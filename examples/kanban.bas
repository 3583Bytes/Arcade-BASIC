! kanban.bas — a three-lane Kanban board (To-Do / In-Progress / Done).
!
! Renders the board as three side-by-side columns drawn with PRINT, then runs
! an interactive command loop. State lives in two parallel arrays indexed by a
! stable task id; a GOSUB-ed RENDER routine reads them to redraw after every
! command. Demonstrates: string-valued FUNCTION, DIM string/numeric arrays,
! READ/DATA seeding, DO/LOOP with EXIT DO, IF/ELSEIF blocks, GOSUB/RETURN,
! a hand-written line tokenizer, INPUT prompts, OPEN/PRINT #/LINE INPUT #/CLOSE
! file persistence guarded by WHEN/USE, and the REPEAT$/LEFT$/MID$/LTRIM$/STR$/
! VAL/& string toolkit.
!
! Two ways to drive it:
!   * type a letter and answer the prompts, OR
!   * put everything on one line, e.g.  M 1 3   (move task 1 to lane 3 = Done)
! Whatever you leave off the one-line form is asked for interactively.
!
! Interactive — read one value per line. Always finish with Q so the loop
! exits before the input stream runs dry. Scripted example:
!   printf 'A Ship the demo 1\nM 5 2\nD 6\nV\nQ\n' | \
!     dotnet run --project src/ArcadeBasic.Cli -- run examples/kanban.bas
! Note: input is read with INPUT, which splits on commas — so card text and
! board names should not contain a comma.

OPTION BASE 1

! Pad (or truncate) S$ to exactly W columns so every cell lines up.
FUNCTION PAD$(S$, W)
  PAD$ = LEFT$(S$ & REPEAT$(" ", W), W)
END FUNCTION

! Human-readable lane name for messages.
FUNCTION LANENAME$(LN)
  IF LN = 1 THEN
    LANENAME$ = "To-Do"
  ELSEIF LN = 2 THEN
    LANENAME$ = "In-Progress"
  ELSE
    LANENAME$ = "Done"
  END IF
END FUNCTION

! Parse S$ as a non-negative integer; return -1 if it isn't all digits.
! (VAL throws on non-numeric input, so we hand-roll a safe parser for the
! command line, where a token like "eggs" is perfectly normal.)
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

LET MAXT = 64          ! board capacity
LET CW = 30            ! column width, in characters (lane is this wide)
LET BOARD$ = "kanban"  ! current board name; its file is BOARD$ & ".board" (CWD)
DIM TASK$(64)          ! card text, indexed by task id
DIM LANE(64)           ! lane per task: 1=To-Do, 2=In-Progress, 3=Done
DIM TOK$(64)           ! command-line tokens, filled by the tokenizer
LET NT = 0             ! number of tasks on the board

! scratch variables, declared up front so the analyzer sees them assigned
LET A$ = ""            ! render scratch cells (filled by the RENDER routine)
LET B$ = ""
LET C$ = ""
LET NTOK = 0           ! token count from the last command line
LET RAW$ = ""          ! raw command line
LET NM$ = ""           ! name argument (board name)
LET N = 0              ! task-number argument
LET L = 0              ! lane argument
LET HAVEN = 0          ! 1 = task number supplied on the command line
LET HAVEL = 0          ! 1 = lane supplied on the command line
LET HAVET = 0          ! 1 = task text supplied on the command line
LET HAVENM = 0         ! 1 = name supplied on the command line

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

PRINT "=== Arcade BASIC Kanban ==="
PRINT "Type H for help."

! --- main loop ----------------------------------------------------------
DO
  GOSUB 9000                                   ! draw the board
  PRINT
  PRINT "Actions:  [A]dd  [M]ove  [D]elete  [N]ew board  [V]iew  [S]ave  [L]oad  [H]elp  [Q]uit"
  PRINT "One-liners welcome, e.g.  M 1 3 (move task 1 to Done)   A Buy milk 1   D 4"
  INPUT "What would you like to do? "; RAW$
  GOSUB 9100                                   ! tokenize RAW$ into TOK$(), NTOK
  IF NTOK > 0 THEN
    LET CMD$ = UCASE$(LEFT$(TOK$(1), 1))
    IF CMD$ = "Q" THEN
      PRINT "Bye."
      EXIT DO
    ELSEIF CMD$ = "A" THEN
      GOSUB 9350                               ! parse inline add args
      GOSUB 9400                               ! add (prompts for the rest)
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
      LET HAVENM = 0
      IF NTOK >= 2 THEN
        LET NM$ = TOK$(2)
        LET HAVENM = 1
      END IF
      GOSUB 9300
    ELSEIF CMD$ = "V" THEN
      PRINT "(refreshing)"
    ELSEIF CMD$ = "S" THEN
      LET HAVENM = 0
      IF NTOK >= 2 THEN
        LET NM$ = TOK$(2)
        LET HAVENM = 1
      END IF
      GOSUB 9800
    ELSEIF CMD$ = "L" THEN
      LET HAVENM = 0
      IF NTOK >= 2 THEN
        LET NM$ = TOK$(2)
        LET HAVENM = 1
      END IF
      GOSUB 9900
    ELSEIF CMD$ = "H" THEN
      GOSUB 9700
    ELSE
      PRINT "Unknown command — type H for help."
    END IF
  END IF
LOOP
END

! ======================================================================
! 9000  RENDER — draw the three lanes side by side.
! ======================================================================
9000 REM ---- count the cards in each lane ----
LET C1 = 0
LET C2 = 0
LET C3 = 0
FOR I = 1 TO NT
  IF LANE(I) = 1 THEN LET C1 = C1 + 1
  IF LANE(I) = 2 THEN LET C2 = C2 + 1
  IF LANE(I) = 3 THEN LET C3 = C3 + 1
NEXT I

! tallest lane decides how many body rows to print
LET ROWS = C1
IF C2 > ROWS THEN LET ROWS = C2
IF C3 > ROWS THEN LET ROWS = C3

LET BAR$ = REPEAT$("-", CW)
PRINT
PRINT "Board: "; BOARD$
PRINT "+"; BAR$; "+"; BAR$; "+"; BAR$; "+"
PRINT "|"; PAD$(" TO-DO (" & LTRIM$(STR$(C1)) & ")", CW); "|"; PAD$(" IN-PROGRESS (" & LTRIM$(STR$(C2)) & ")", CW); "|"; PAD$(" DONE (" & LTRIM$(STR$(C3)) & ")", CW); "|"
PRINT "+"; BAR$; "+"; BAR$; "+"; BAR$; "+"

FOR R = 1 TO ROWS
  GOSUB 9200                                   ! fills A$, B$, C$ for row R
  PRINT "|"; PAD$(A$, CW); "|"; PAD$(B$, CW); "|"; PAD$(C$, CW); "|"
NEXT R
PRINT "+"; BAR$; "+"; BAR$; "+"; BAR$; "+"
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
! 9200  Build the three cells (A$, B$, C$) for visual row R: the R-th card
!       in lane 1, 2 and 3 respectively. Empty string when a lane is short.
! ----------------------------------------------------------------------
9200 REM ---- cells for row R ----
LET A$ = ""
LET B$ = ""
LET C$ = ""
LET K1 = 0
LET K2 = 0
LET K3 = 0
FOR I = 1 TO NT
  IF LANE(I) = 1 THEN
    LET K1 = K1 + 1
    IF K1 = R THEN LET A$ = " #" & LTRIM$(STR$(I)) & " " & TASK$(I)
  ELSEIF LANE(I) = 2 THEN
    LET K2 = K2 + 1
    IF K2 = R THEN LET B$ = " #" & LTRIM$(STR$(I)) & " " & TASK$(I)
  ELSEIF LANE(I) = 3 THEN
    LET K3 = K3 + 1
    IF K3 = R THEN LET C$ = " #" & LTRIM$(STR$(I)) & " " & TASK$(I)
  END IF
NEXT I
RETURN

! ======================================================================
! 9300  NEW — empty the board and (optionally) rename it.
! ======================================================================
9300 REM ---- new empty board ----
LET NT = 0
PRINT "  Current board: "; BOARD$
IF HAVENM = 0 THEN
  INPUT "  Name for the new board (blank keeps the current name): "; NM$
END IF
IF NM$ <> "" THEN LET BOARD$ = NM$
PRINT "  New empty board: "; BOARD$
RETURN

! ----------------------------------------------------------------------
! 9350  Parse inline ADD arguments: "A <text...> [lane]". If the last token
!       is a lane number (1-3) it becomes the lane; everything between the
!       command and there is the text. Sets HAVET / HAVEL / T$ / L.
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
! 9400  ADD — prompts for whatever wasn't given inline.
! ======================================================================
9400 REM ---- add ----
IF NT >= MAXT THEN
  PRINT "  Board is full."
  RETURN
END IF
IF HAVET = 0 THEN
  INPUT "  Task text: "; T$
END IF
IF T$ = "" THEN
  PRINT "  (cancelled — empty text)"
  RETURN
END IF
IF HAVEL = 0 THEN
  INPUT "  Lane 1=To-Do 2=In-Progress 3=Done: "; L
END IF
LET L = INT(L)
IF L < 1 OR L > 3 THEN
  PRINT "  (cancelled — lane must be 1, 2 or 3)"
  RETURN
END IF
LET NT = NT + 1
LET TASK$(NT) = T$
LET LANE(NT) = L
PRINT "  Added #"; LTRIM$(STR$(NT)); " to "; LANENAME$(L)
RETURN

! ======================================================================
! 9500  MOVE — prompts for whatever wasn't given inline.
! ======================================================================
9500 REM ---- move ----
IF HAVEN = 0 THEN
  INPUT "  Task # to move: "; N
END IF
LET N = INT(N)
IF N < 1 OR N > NT THEN
  PRINT "  (no such task: #"; LTRIM$(STR$(N)); ")"
  RETURN
END IF
IF HAVEL = 0 THEN
  INPUT "  To lane 1=To-Do 2=In-Progress 3=Done: "; L
END IF
LET L = INT(L)
IF L < 1 OR L > 3 THEN
  PRINT "  (lane must be 1, 2 or 3)"
  RETURN
END IF
LET LANE(N) = L
PRINT "  Moved #"; LTRIM$(STR$(N)); " to "; LANENAME$(L)
RETURN

! ======================================================================
! 9600  DELETE — prompts if no task # given inline. Remaining tasks renumber.
! ======================================================================
9600 REM ---- delete ----
IF HAVEN = 0 THEN
  INPUT "  Task # to delete: "; N
END IF
LET N = INT(N)
IF N < 1 OR N > NT THEN
  PRINT "  (no such task: #"; LTRIM$(STR$(N)); ")"
  RETURN
END IF
FOR I = N TO NT - 1
  LET TASK$(I) = TASK$(I + 1)
  LET LANE(I) = LANE(I + 1)
NEXT I
LET NT = NT - 1
PRINT "  Deleted (remaining tasks renumbered)."
RETURN

! ======================================================================
! 9700  HELP — explain what each command does.
! ======================================================================
9700 REM ---- help ----
PRINT
PRINT "Commands (type the letter, or put the whole thing on one line):"
PRINT "  A  Add a task    — text, then a lane (1=To-Do 2=In-Progress 3=Done)"
PRINT "  M  Move a task   — task # (shown as #n on its card), then the new lane"
PRINT "  D  Delete a task — task #; the remaining tasks renumber"
PRINT "  N  New board     — empty the board and give it a name"
PRINT "  V  View          — redraw the board"
PRINT "  S  Save          — write the board to <name>.board (prompts for a name)"
PRINT "  L  Load          — load <name>.board into the board (prompts for a name)"
PRINT "  H  Help          — show this list"
PRINT "  Q  Quit          — leave the program"
PRINT
PRINT "One-line examples:"
PRINT "  M 1 3          move task 1 to lane 3 (Done)"
PRINT "  A Buy milk 1   add 'Buy milk' to To-Do (trailing 1-3 is the lane)"
PRINT "  D 4            delete task 4"
PRINT "  S sprint1      save as sprint1     L sprint1   load sprint1"
PRINT
PRINT "Anything you leave off is asked for interactively. Save/Load act on the"
PRINT "named board shown at the top; a blank name keeps the current one."
RETURN

! ======================================================================
! 9800  SAVE — write the board to <name>.board (count line, then lane/text
!       pairs, one value per line so card text may contain anything).
! ======================================================================
9800 REM ---- save ----
PRINT "  Current board: "; BOARD$
IF HAVENM = 0 THEN
  INPUT "  Save as (blank keeps the current name): "; NM$
END IF
IF NM$ <> "" THEN LET BOARD$ = NM$
LET FILE$ = BOARD$ & ".board"
OPEN #1: NAME FILE$, ACCESS OUTPUT
PRINT #1: STR$(NT)
FOR I = 1 TO NT
  PRINT #1: STR$(LANE(I))
  PRINT #1: TASK$(I)
NEXT I
CLOSE #1
PRINT "  Saved "; LTRIM$(STR$(NT)); " task(s) to "; FILE$
RETURN

! ======================================================================
! 9900  LOAD — replace the board with <name>.board. Wrapped in a handler so
!       a missing file reports cleanly instead of aborting the program.
! ======================================================================
9900 REM ---- load ----
PRINT "  Current board: "; BOARD$
IF HAVENM = 0 THEN
  INPUT "  Load which board (blank keeps the current name): "; NM$
END IF
IF NM$ <> "" THEN LET BOARD$ = NM$
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
  PRINT "  Loaded "; LTRIM$(STR$(NT)); " task(s) from "; FILE$
USE
  PRINT "  Could not load "; FILE$; " (no saved board by that name yet?)"
END WHEN
RETURN
