! kanban.bas — a three-lane Kanban board (To-Do / In-Progress / Done) drawn with  @category Graphics
! the ECMA-116 §13 graphics module. Each lane is an outlined GRAPH LINES box with
! a dashed header divider; each card is word-wrapped GRAPH TEXT in an outlined box
! with a GRAPH AREA stripe in the lane's colour; a thin filled GRAPH AREA bar along
! the bottom of each lane gauges how full it is. The drawing surface is queried once
! with ASK DEVICE SIZE and shown on the status line.
!
! With a mouse — the terminal IDE's Graphics tab (arcade-basic-ide) or the
! browser playground — drag a card onto another lane to move it, and type
! commands straight onto the board (GET MOUSE + INKEY$, an Arcade BASIC and a
! Microsoft BASIC extension; see docs/conformance.md). Without one — a plain
! terminal, or piped input — you type one command per line instead (LINE INPUT
! reads the whole line, so card text may contain commas). Headless, you can
! render a frame to SVG by piping commands and finishing with Q:
!   printf 'A Ship the demo 1\nM 5 2\nD 6\nQ\n' | \
!     dotnet run --project src/ArcadeBasic.Cli -- run examples/kanban.bas --svg board.svg
!
! A status line on the board echoes each command's result.

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
DIM WRAP$(64)
DIM TTASK$(64)          ! staging buffers for an atomic LOAD
DIM TLANE(64)
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
LET LX(1) = 0          ! lane 1 starts at column 0, aligned with the input field
LET LX(2) = 27
LET LX(3) = 54
LET COLW = 25           ! lane width, in canvas cells (window units)
LET CARDW = COLW - 4    ! card text width, inside the card box

! scratch / argument variables
LET RAW$ = ""
LET MSG$ = "Waiting for command"
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
LET HELP = 0            ! 1 while the help screen is showing
LET WRAPN = 0
LET WRAPW = 0
LET WRAPSRC$ = ""
LET WLINE$ = ""
LET WWORD$ = ""
LET WS$ = ""
LET LANELBL$ = ""
LET QUIT = 0

! Mouse mode: where each drawn card sits (window rows; CTOPY = -1 when it isn't
! drawn), the card being dragged and the lane under it, and the command being typed.
DIM CTOPY(64)
DIM CBOTY(64)
LET HASMOUSE = 0        ! 1 when there's a mouse
LET DRAG = 0            ! card # being dragged, or 0
LET HOVERLN = 0         ! lane under the pointer while dragging
LET GRABDX = 0          ! where on the card it was grabbed
LET GRABDY = 0
LET HIT = 0
LET HOVERCARD = 0       ! card under the pointer (highlighted), or 0
LET CARDLINE = 1        ! arguments to 9300 (draw a card)
LET CARDSTRIPE = 1
LET CARDTEXT = 1
LET CARDFILL = 0
LET MARKY = 0
LET TYPED$ = ""
LET DIRTY = 1           ! the board needs redrawing

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
GET MOUSE MX, MY, MS
IF MS < 0 THEN
  ! No pointing device: one typed command per line.
  DO
    GOSUB 9010                     ! draw the board (or the help screen)
    LINE INPUT ""; RAW$            ! whole line; "" ; suppresses the "?" prompt
    GOSUB 9150                     ! run it
    IF QUIT = 1 THEN EXIT DO
  LOOP
ELSE
  ! A mouse: drag cards between lanes; type commands onto the board.
  LET HASMOUSE = 1
  GOSUB 9010                       ! the board is up before the first key or click
  LET DIRTY = 0
  DO
    ! keys: build the command line; Enter runs it, Esc clears it
    DO
      LET K$ = INKEY$
      IF K$ = "" THEN EXIT DO
      IF K$ = CHR$(13) THEN
        LET RAW$ = TYPED$
        LET TYPED$ = ""
        GOSUB 9150
      ELSEIF K$ = CHR$(8) OR K$ = CHR$(127) THEN
        IF LEN(TYPED$) > 0 THEN LET TYPED$ = LEFT$(TYPED$, LEN(TYPED$) - 1)
      ELSEIF K$ = CHR$(27) THEN
        LET TYPED$ = ""
      ELSEIF LEN(K$) = 1 AND K$ >= " " THEN
        LET TYPED$ = TYPED$ & K$
      END IF
      LET DIRTY = 1
    LOOP
    IF QUIT = 1 THEN EXIT DO
    ! mouse: 2 = pressed, 1 = held, 3 = released (0 = up)
    GET MOUSE MX, MY, MS
    IF MS = 2 THEN
      IF HELP = 1 THEN
        LET HELP = 0                 ! a click leaves the help screen
        LET DIRTY = 1
      ELSE
        GOSUB 9700                   ! card under the pointer -> HIT
        IF HIT > 0 THEN
          LET DRAG = HIT
          LET GRABDX = MX - LX(LANE(HIT))
          LET GRABDY = MY - CTOPY(HIT)
          LET HOVERLN = LANE(HIT)
          LET MSG$ = "Moving #" & LTRIM$(STR$(HIT)) & " — drop it on a lane"
          LET DIRTY = 1
        END IF
      END IF
    ELSEIF MS = 1 AND DRAG > 0 THEN
      GOSUB 9750                     ! lane under the pointer -> HOVERLN
      LET DIRTY = 1                  ! the card follows the pointer
    ELSEIF DRAG = 0 AND HELP = 0 THEN
      GOSUB 9700                     ! the card under the pointer gets highlighted
      IF HIT <> HOVERCARD THEN
        LET HOVERCARD = HIT
        LET DIRTY = 1
      END IF
    ELSEIF MS = 3 AND DRAG > 0 THEN
      GOSUB 9750
      IF HOVERLN > 0 AND HOVERLN <> LANE(DRAG) THEN
        LET LANE(DRAG) = HOVERLN
        LET MSG$ = "Moved #" & LTRIM$(STR$(DRAG)) & " to " & LANENAME$(HOVERLN)
      ELSE
        LET MSG$ = "Left #" & LTRIM$(STR$(DRAG)) & " where it was"
      END IF
      LET DRAG = 0
      LET DIRTY = 1
    END IF
    IF DIRTY = 1 THEN
      GOSUB 9010
      LET DIRTY = 0
    END IF
    SLEEP 0.03                       ! ~30 frames/second (also presents the frame)
  LOOP
END IF
END

! ======================================================================
! 9010  DRAW — the help screen or the board.
! ======================================================================
9010 REM ---- draw ----
IF HELP = 1 THEN
  GOSUB 9050
ELSE
  GOSUB 9000
END IF
RETURN

! ======================================================================
! 9150  EXECUTE — run the command in RAW$ (sets QUIT on Q).
! ======================================================================
9150 REM ---- execute command ----
GOSUB 9100                         ! tokenize RAW$ into TOK$(), NTOK
IF NTOK > 0 THEN
  LET CMD$ = UCASE$(LEFT$(TOK$(1), 1))
  LET HELP = 0                     ! any command leaves the help screen
  IF CMD$ = "Q" THEN
    LET QUIT = 1
  ELSEIF CMD$ = "A" THEN
    GOSUB 9350                     ! parse inline add args
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
    LET HELP = 1
  ELSE
    LET MSG$ = "Unknown command '" & TOK$(1) & "' — H for help"
  END IF
ELSE
  LET HELP = 0                     ! empty Enter dismisses the help screen
END IF
RETURN

! ======================================================================
! 9000  RENDER — three outlined lanes (wrapped, bulleted cards + fill gauge)
!                above three fixed footer lines: quick help, status, prompt.
!                The window height tracks the device's cell rows (one line per
!                row) so the footer never overlaps however the canvas is scaled.
! ======================================================================
9000 REM ---- graphics render ----
CLEAR
ASK DEVICE SIZE DEVW, DEVH, DEVU$  ! re-query each frame so layout follows resizes
LET ROWS = 24                      ! fallback when the device size is unknown
IF DEVH >= 4 THEN LET ROWS = INT(DEVH / 4)
IF ROWS > 40 THEN LET ROWS = 40    ! cap so one line stays about one row
IF ROWS < 12 THEN LET ROWS = 12    ! floor (degenerate guard)
SET WINDOW 0, 80, 0, ROWS
! Vertical geometry. The bottom rows are reserved for the footer:
!   y=1 quick help   y=0 status   (y=2 is a blank gap; the live input
!   field just below the canvas is the actual prompt line)
LET BBOT = 3                       ! lane box bottom border
LET CBOT = 5                       ! lowest card line (above the gauge)
LET BTOP = ROWS - 1                ! lane box top border
LET BTITLE = ROWS - 2              ! lane title
LET BDIV = ROWS - 3                ! dashed header divider
LET CTOP = ROWS - 4                ! top card line
FOR I = 1 TO NT
  LET CTOPY(I) = -1                ! not drawn (yet)
NEXT I
FOR LN = 1 TO 3
  LET X0 = LX(LN)
  LET X1 = X0 + COLW
  ! lane box (solid outline)
  SET LINE STYLE 1
  SET LINE COLOR LCOL(LN)
  GRAPH LINES: X0, BBOT; X1, BBOT; X1, BTOP; X0, BTOP; X0, BBOT
  ! dashed divider under the header
  SET LINE STYLE 2
  GRAPH LINES: X0, BDIV; X1, BDIV
  SET LINE STYLE 1
  ! header title with the lane's card count
  LET CNT = 0
  FOR I = 1 TO NT
    IF LANE(I) = LN THEN LET CNT = CNT + 1
  NEXT I
  SET TEXT COLOR LCOL(LN)
  GRAPH TEXT, AT X0 + 2, BTITLE: LNAME$(LN) & " (" & LTRIM$(STR$(CNT)) & ")"
  ! right-aligned lane number, so the user knows the lane arg for "M n lane"
  LET LANELBL$ = "lane " & LTRIM$(STR$(LN))
  GRAPH TEXT, AT X1 - LEN(LANELBL$), BTITLE: LANELBL$
  ! cards: word-wrapped boxes, top-down, a row apart; stop when the lane fills up
  LET ROW = CTOP
  LET USED = 0
  LET HIDDEN = 0
  LET FULL = 0
  FOR I = 1 TO NT
    IF LANE(I) = LN THEN
      IF FULL = 1 THEN
        LET HIDDEN = HIDDEN + 1
      ELSE
        LET WRAPSRC$ = "#" & LTRIM$(STR$(I)) & " " & TASK$(I)
        LET WRAPW = CARDW
        GOSUB 9200                   ! wrap WRAPSRC$ into WRAP$(1..WRAPN)
        IF ROW - WRAPN + 1 < CBOT THEN
          LET FULL = 1
          LET HIDDEN = HIDDEN + 1
        ELSE
          LET CTOPY(I) = ROW         ! remember where it is, for clicks
          LET CBOTY(I) = ROW - WRAPN + 1
          LET Y0 = ROW
          LET CARDSTRIPE = LCOL(LN)
          LET CARDFILL = 0
          IF I = DRAG THEN
            LET CARDLINE = 8         ! the card being dragged, greyed
            LET CARDTEXT = 8
          ELSEIF I = HOVERCARD THEN
            LET CARDLINE = 1         ! the card under the pointer
            LET CARDTEXT = 1
          ELSE
            LET CARDLINE = LCOL(LN)
            LET CARDTEXT = 1
          END IF
          GOSUB 9300                 ! draw the card box
          LET ROW = ROW - WRAPN - 1
          LET USED = USED + WRAPN + 1
        END IF
      END IF
    END IF
  NEXT I
  ! "+K more" when cards didn't fit (just above the gauge)
  IF HIDDEN > 0 THEN
    SET TEXT COLOR 8
    GRAPH TEXT, AT X0 + 2, CBOT - 1: "+" & LTRIM$(STR$(HIDDEN)) & " more"
    SET TEXT COLOR 1
  END IF
  ! fill gauge: thin filled bar, width proportional to rows used, sitting just
  ! inside the bottom border
  IF USED > 0 THEN
    LET GW = (COLW - 1) * USED / (CTOP - CBOT + 1)
    IF GW > COLW - 1 THEN LET GW = COLW - 1
    SET AREA COLOR LCOL(LN)
    GRAPH AREA: X0 + 0.5, BBOT + 0.2; X0 + 0.5 + GW, BBOT + 0.2; X0 + 0.5 + GW, BBOT + 0.7; X0 + 0.5, BBOT + 0.7
  END IF
NEXT LN
! --- dragging: outline the lane under the pointer, mark where the card will
!     land in it (cards keep their # order), and draw the card at the pointer --
IF DRAG > 0 THEN
  SET LINE COLOR 1
  IF HOVERLN > 0 AND HOVERLN <> LANE(DRAG) THEN
    LET X0 = LX(HOVERLN) + 0.4
    LET X1 = LX(HOVERLN) + COLW - 0.4
    GRAPH LINES: X0, BBOT + 0.4; X1, BBOT + 0.4; X1, BTOP - 0.4; X0, BTOP - 0.4; X0, BBOT + 0.4
    LET MARKY = CTOP + 0.85          ! above the lane's first card ...
    FOR I = 1 TO DRAG - 1
      IF LANE(I) = HOVERLN AND CTOPY(I) >= 0 THEN LET MARKY = CBOTY(I) - 0.75   ! ... or below the last one before it
    NEXT I
    SET LINE STYLE 2
    GRAPH LINES: X0 + 0.6, MARKY; X1 - 0.6, MARKY
    SET LINE STYLE 1
  END IF
  LET WRAPSRC$ = "#" & LTRIM$(STR$(DRAG)) & " " & TASK$(DRAG)
  LET WRAPW = CARDW
  GOSUB 9200
  LET X0 = MX - GRABDX               ! the card's lane edge, as grabbed
  LET Y0 = MY - GRABDY               ! its first line
  LET CARDLINE = 1
  LET CARDSTRIPE = LCOL(LANE(DRAG))
  LET CARDTEXT = 1
  LET CARDFILL = 1                   ! opaque, over whatever it's dragged across
  GOSUB 9300
END IF
! --- footer: two fixed lines, left-aligned to the first lane. Without a mouse
!     the live input field below the canvas is the prompt line; with one, the
!     command being typed shows on the status line -----------------------------
LET FX = LX(1)
SET TEXT COLOR 6
! (Text sits on its baseline: 0.2 above the row keeps descenders off the edge of
! a fine-grained screen, and lands in the same terminal row.)
IF HASMOUSE = 1 THEN
  GRAPH TEXT, AT FX, 1.2: "Drag cards · A add  M move  D delete  N new  S save  L load  H help  Q quit"
ELSE
  GRAPH TEXT, AT FX, 1.2: "A add  M move  D delete  N new  S save  L load  H help  Q quit"
END IF
SET TEXT COLOR 7
IF TYPED$ <> "" THEN
  GRAPH TEXT, AT FX, 0.2: "> " & TYPED$ & "_"
ELSEIF HASMOUSE = 1 THEN
  GRAPH TEXT, AT FX, 0.2: "Status: " & MSG$ & "   (type a command)"
ELSE
  GRAPH TEXT, AT FX, 0.2: "Status: " & MSG$
END IF
RETURN

! ======================================================================
! 9050  HELP — a full-screen command reference. The window height is set
!              to the device's cell rows (one line per row) so the text
!              never overlaps however the canvas is scaled.
! ======================================================================
9050 REM ---- help screen ----
CLEAR
ASK DEVICE SIZE DEVW, DEVH, DEVU$
LET HROWS = 24                     ! fallback when the device size is unknown
IF DEVH >= 4 THEN LET HROWS = INT(DEVH / 4)
IF HROWS > 32 THEN LET HROWS = 32  ! cap so a huge surface keeps 1 line ~ 1 row
IF HROWS < 10 THEN LET HROWS = 10  ! (never exceed the real rows: no overlap)
SET WINDOW 0, 80, 0, HROWS
SET LINE STYLE 1
SET LINE COLOR 6
GRAPH LINES: 1, 1; 78, 1; 78, HROWS - 1; 1, HROWS - 1; 1, 1
LET Y = HROWS - 2
SET TEXT COLOR 7
GRAPH TEXT, AT 4, Y: "KANBAN BOARD  —  HELP"
LET Y = Y - 2
SET TEXT COLOR 6
GRAPH TEXT, AT 3, Y: "COMMANDS"
LET Y = Y - 1
SET TEXT COLOR 1
GRAPH TEXT, AT 4, Y: "A <text> <lane>  add a card to lane 1-3  (A Buy milk 1)"
LET Y = Y - 1
GRAPH TEXT, AT 4, Y: "M <n> <lane>     move card #n to a lane    (M 4 2)"
LET Y = Y - 1
GRAPH TEXT, AT 4, Y: "D <n>            delete card #n (others renumber)"
LET Y = Y - 1
GRAPH TEXT, AT 4, Y: "N [name]         start a new empty board"
LET Y = Y - 1
GRAPH TEXT, AT 4, Y: "S [name]         save the board to <name>.board"
LET Y = Y - 1
GRAPH TEXT, AT 4, Y: "L [name]         load a board from <name>.board"
LET Y = Y - 1
GRAPH TEXT, AT 4, Y: "H                show this help screen"
LET Y = Y - 1
GRAPH TEXT, AT 4, Y: "Q                quit"
LET Y = Y - 1
SET TEXT COLOR 6
GRAPH TEXT, AT 3, Y: "LANES"
LET Y = Y - 1
SET TEXT COLOR 1
GRAPH TEXT, AT 4, Y: "1 = To-Do      2 = In-Progress      3 = Done"
LET Y = Y - 1
SET TEXT COLOR 6
GRAPH TEXT, AT 3, Y: "NOTES"
LET Y = Y - 1
SET TEXT COLOR 1
GRAPH TEXT, AT 4, Y: "Long card text wraps; a full lane shows ""+N more""."
LET Y = Y - 1
GRAPH TEXT, AT 4, Y: "Add a trailing 1-3 to set the lane:  A Buy milk 1"
LET Y = Y - 1
GRAPH TEXT, AT 4, Y: "With a mouse, drag a card onto another lane to move it."
LET Y = Y - 2
SET TEXT COLOR 8
IF HASMOUSE = 1 THEN
  GRAPH TEXT, AT 4, Y: "Press Enter or click to return to the board."
ELSE
  GRAPH TEXT, AT 4, Y: "Press Enter to return to the board."
END IF
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
! 9200  WORD-WRAP — break WRAPSRC$ into WRAP$(1..WRAPN), each <= WRAPW cols.
!                   Splits on spaces; words longer than WRAPW are hard-broken.
! ----------------------------------------------------------------------
9200 REM ---- word wrap ----
LET WRAPN = 0
LET WLINE$ = ""
LET WWORD$ = ""
LET WS$ = WRAPSRC$ & " "           ! trailing space flushes the final word
FOR WI = 1 TO LEN(WS$)
  LET WC$ = MID$(WS$, WI, 1)
  IF WC$ = " " THEN
    IF WWORD$ <> "" THEN
      GOSUB 9250                   ! place WWORD$ onto WLINE$ / WRAP$()
      LET WWORD$ = ""
    END IF
  ELSE
    LET WWORD$ = WWORD$ & WC$
  END IF
NEXT WI
IF WLINE$ <> "" THEN
  LET WRAPN = WRAPN + 1
  LET WRAP$(WRAPN) = WLINE$
END IF
IF WRAPN = 0 THEN
  LET WRAPN = 1
  LET WRAP$(1) = ""
END IF
RETURN

! ----------------------------------------------------------------------
! 9250  WRAP-PLACE — add WWORD$ to the current wrapped line, breaking as needed.
! ----------------------------------------------------------------------
9250 REM ---- wrap place ----
DO WHILE LEN(WWORD$) > WRAPW
  ! word wider than a whole line: flush, then emit a full-width chunk
  IF WLINE$ <> "" THEN
    LET WRAPN = WRAPN + 1
    LET WRAP$(WRAPN) = WLINE$
    LET WLINE$ = ""
  END IF
  LET WRAPN = WRAPN + 1
  LET WRAP$(WRAPN) = LEFT$(WWORD$, WRAPW)
  LET WWORD$ = MID$(WWORD$, WRAPW + 1)
LOOP
IF WLINE$ = "" THEN
  LET WLINE$ = WWORD$
ELSEIF LEN(WLINE$) + 1 + LEN(WWORD$) <= WRAPW THEN
  LET WLINE$ = WLINE$ & " " & WWORD$
ELSE
  LET WRAPN = WRAPN + 1
  LET WRAP$(WRAPN) = WLINE$
  LET WLINE$ = WWORD$
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

! ----------------------------------------------------------------------
! 9300  CARD — draw one card: a box around WRAP$(1..WRAPN) with a colour stripe
!        down its left side. X0 = the lane's left edge, Y0 = the first line's
!        baseline; colours in CARDLINE / CARDSTRIPE / CARDTEXT; CARDFILL = 1
!        blacks out what's underneath first.
! ----------------------------------------------------------------------
9300 REM ---- card box ----
LET BX0 = X0 + 1
LET BX1 = X0 + COLW - 1
LET BYT = Y0 + 0.85                ! text sits on its baseline, so the box
LET BYB = Y0 - WRAPN + 0.65        ! reaches just above the first line and below the last
IF CARDFILL = 1 THEN
  SET AREA COLOR 0
  GRAPH AREA: BX0, BYB; BX1, BYB; BX1, BYT; BX0, BYT
END IF
SET LINE COLOR CARDLINE
GRAPH LINES: BX0, BYB; BX1, BYB; BX1, BYT; BX0, BYT; BX0, BYB
SET AREA COLOR CARDSTRIPE
GRAPH AREA: BX0, BYB; BX0 + 0.6, BYB; BX0 + 0.6, BYT; BX0, BYT
SET TEXT COLOR CARDTEXT
FOR WJ = 1 TO WRAPN
  GRAPH TEXT, AT X0 + 2.4, Y0 - WJ + 1: WRAP$(WJ)
NEXT WJ
RETURN

! ----------------------------------------------------------------------
! 9700  HIT — the drawn card under the pointer (MX, MY) -> HIT, or 0.
! ----------------------------------------------------------------------
9700 REM ---- card under pointer ----
LET HIT = 0
FOR I = 1 TO NT
  IF CTOPY(I) >= 0 THEN
    LET X0 = LX(LANE(I))
    IF MX >= X0 AND MX <= X0 + COLW AND MY >= CBOTY(I) - 0.2 AND MY <= CTOPY(I) + 0.9 THEN LET HIT = I
  END IF
NEXT I
RETURN

! ----------------------------------------------------------------------
! 9750  LANE — the lane under the pointer (MX, MY) -> HOVERLN, or 0.
! ----------------------------------------------------------------------
9750 REM ---- lane under pointer ----
LET HOVERLN = 0
FOR LN = 1 TO 3
  IF MX >= LX(LN) AND MX <= LX(LN) + COLW AND MY >= BBOT AND MY <= BTOP THEN LET HOVERLN = LN
NEXT LN
RETURN

! ======================================================================
! 9800  SAVE — write the board to <name>.board. The file is self-identifying
!              (magic + version line), then a card count, then one card per
!              line as "<lane> <task>".
! ======================================================================
9800 REM ---- save ----
LET FILE$ = BOARD$ & ".board"
OPEN #1: NAME FILE$, ACCESS OUTPUT
PRINT #1: "ARCADE-KANBAN 1"
PRINT #1: LTRIM$(STR$(NT))
FOR I = 1 TO NT
  PRINT #1: LTRIM$(STR$(LANE(I))) & " " & TASK$(I)
NEXT I
CLOSE #1
LET MSG$ = "Saved " & LTRIM$(STR$(NT)) & " task(s) to " & FILE$
RETURN

! ======================================================================
! 9900  LOAD — replace the board with <name>.board. Reads into staging
!              buffers and only commits if the whole file parses, so a bad
!              or foreign file leaves the current board untouched.
! ======================================================================
9900 REM ---- load ----
LET FILE$ = BOARD$ & ".board"
LET LOADOK = 0
LET TN = 0
WHEN EXCEPTION IN
  OPEN #1: NAME FILE$, ACCESS INPUT
  LINE INPUT #1: LN$
  IF LEFT$(LN$, 13) = "ARCADE-KANBAN" THEN
    LINE INPUT #1: LN$
    LET TN = INT(VAL(LN$))
    IF TN < 0 THEN LET TN = 0
    IF TN > MAXT THEN LET TN = MAXT
    FOR I = 1 TO TN
      LINE INPUT #1: LN$
      GOSUB 9950                   ! parse "<lane> <task>" -> TLANE(I), TTASK$(I)
    NEXT I
    LET LOADOK = 1
  ELSE
    LET MSG$ = FILE$ & " is not a kanban board file"
  END IF
  CLOSE #1
USE
  CLOSE #1                         ! safe no-op if it never opened
  LET MSG$ = "Could not load " & FILE$
END WHEN
IF LOADOK = 1 THEN
  LET NT = TN
  FOR I = 1 TO NT
    LET LANE(I) = TLANE(I)
    LET TASK$(I) = TTASK$(I)
  NEXT I
  LET MSG$ = "Loaded " & LTRIM$(STR$(NT)) & " task(s) from " & FILE$
END IF
RETURN

! ----------------------------------------------------------------------
! 9950  Parse one saved card line "<lane> <task>" into TLANE(I)/TTASK$(I).
!        The lane is the first token; the task is everything after the first
!        space (so it may contain spaces and commas). Bad lanes default to 1.
! ----------------------------------------------------------------------
9950 REM ---- parse saved card ----
LET SP = 0
FOR J = 1 TO LEN(LN$)
  IF SP = 0 AND MID$(LN$, J, 1) = " " THEN LET SP = J
NEXT J
IF SP = 0 THEN
  LET TLANE(I) = 1
  LET TTASK$(I) = LN$
ELSE
  LET TLANE(I) = INT(VAL(LEFT$(LN$, SP - 1)))
  LET TTASK$(I) = MID$(LN$, SP + 1)
END IF
IF TLANE(I) < 1 OR TLANE(I) > 3 THEN LET TLANE(I) = 1
RETURN
