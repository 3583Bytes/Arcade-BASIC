! kanban.bas — a Kanban board for your own tasks (To Do, Doing, Done), made with the     @category Graphics
! components in ui.bas (LIBRARY "ui.bas"): rounded lanes and cards with shadows, text in
! sizes and weights, wrapped to fit, buttons, and a dialog over the dimmed board. It keeps
! itself in a file, kanban.board, saving after every change and opening it again next
! time — so it can track real work. (In the browser IDE the file stays in the browser:
! File → Program files.)
!
! With a mouse click a card to select it (it opens up to show all its text), double-click
! it to edit it where it is, and drag it to another lane or up and down; "+ Add a card"
! adds one. The keyboard works too: the arrow keys select, Enter edits, Space picks the
! card up (then the arrows move it), Delete deletes, and typing a letter starts a command.
! GET MOUSE and INKEY$ are an Arcade BASIC and a Microsoft BASIC extension (see
! docs/conformance.md).
!
! Without a mouse — piped input, or a terminal that doesn't report one — type one command
! per line (LINE INPUT reads the whole line, so a card may have commas in it); H lists
! them. Headless, you can render a frame to SVG by piping commands and finishing with Q (it
! saves kanban.board in the current folder as it goes):
!   printf 'A Ship the demo\nM 5 doing\nQ\n' | \
!     dotnet run --project src/ArcadeBasic.Cli -- run examples/kanban.bas --svg board.svg
!
! Cards keep their number (#7) for good — the commands use it — and a lane keeps its
! cards in the order you put them. The board is laid out in points (ui.bas's: 960 across,
! y down) from the screen's size, so it follows a resized window; on a terminal it's laid
! out in rows of text instead.

OPTION BASE 1
LIBRARY "ui.bas"
DECLARE PUBLIC UIW, UIH, UIMX, UIMY, UIMS, UIDOUBLE, UICHANGED, UICELLS, UIROW, UICOL
DECLARE PUBLIC UCBG, UCSURFACE, UCRAISED, UCHOVER, UCLINE, UCINK, UCMUTED, UCFAINT
DECLARE PUBLIC UCACCENT, UCGOOD, UCDANGER, UCLIGHTEN, UISMALL, UIBODY, UITITLE, UIRADIUS
DECLARE PUBLIC UITYPED$

! S$ cut to W columns, ending in "…" when it doesn't fit.
FUNCTION FIT$(S$, W)
  IF W < 1 THEN
    FIT$ = ""
  ELSEIF LEN(S$) <= W THEN
    FIT$ = S$
  ELSE
    FIT$ = LEFT$(S$, W - 1) & "…"
  END IF
END FUNCTION

FUNCTION LANENAME$(N)
  IF N = 1 THEN
    LANENAME$ = "To Do"
  ELSEIF N = 2 THEN
    LANENAME$ = "Doing"
  ELSE
    LANENAME$ = "Done"
  END IF
END FUNCTION

! S$ as a whole number of digits, or -1 if it isn't one.
FUNCTION TONUM(S$)
  IF S$ = "" OR LEN(S$) > 6 THEN
    TONUM = -1
  ELSE
    LET TNR = 0
    LET TNOK = 1
    FOR TNI = 1 TO LEN(S$)
      LET TND$ = MID$(S$, TNI, 1)
      IF TND$ >= "0" AND TND$ <= "9" THEN
        LET TNR = TNR * 10 + (ORD(TND$) - ORD("0"))
      ELSE
        LET TNOK = 0
      END IF
    NEXT TNI
    IF TNOK = 1 THEN
      TONUM = TNR
    ELSE
      TONUM = -1
    END IF
  END IF
END FUNCTION

! ---- the cards: in lane order (a lane shows its cards in the order they come here) ----
LET MAXT = 200
DIM TASK$(200)
DIM LANE(200)
DIM ID(200)             ! each card's number, for good
DIM TTASK$(200)         ! staging for an all-or-nothing load
DIM TLANE(200)
DIM TID(200)
DIM CTOPY(200)          ! where each card was drawn: its top and bottom, in points (-1: not drawn)
DIM CBOTY(200)
DIM TOK$(64)
DIM WRAP$(64)           ! a card's text in lines (UIWRAP), and where each starts
DIM WRP(64)
LET NT = 0
LET NEXTID = 1
LET BOARD$ = "kanban"   ! the board's file is BOARD$ & ".board"

! ---- the lanes ----
DIM LACC(3)             ! each lane's colour
DIM LX(3)               ! its left edge
DIM TOP(3)              ! how many of its cards are scrolled out of sight above
DIM UPAT(3)             ! where its "▲ n more" and "▼ n more" are (-1: not shown)
DIM DNAT(3)
DIM LCNT(3)             ! how many cards it has
DIM VISN(3)             ! and how many showed
DIM LMAXL(3)            ! the most lines of a selected card it has room for

! ---- colours: ui.bas's theme (240-254), and a colour for each lane ----
LET LACC(1) = 16
LET LACC(2) = 17
LET LACC(3) = 18
SET COLOR MIX (LACC(1)) 0.42, 0.62, 1.00      ! blue
SET COLOR MIX (LACC(2)) 0.98, 0.74, 0.32      ! amber
SET COLOR MIX (LACC(3)) 0.38, 0.80, 0.54      ! green

! ---- keys, as INKEY$ spells them ----
LET KENTER$ = CHR$(13)
LET KESC$ = CHR$(27)
LET KBS$ = CHR$(8)
LET KBS2$ = CHR$(127)
LET KUP$ = CHR$(0) & CHR$(72)
LET KDN$ = CHR$(0) & CHR$(80)
LET KLT$ = CHR$(0) & CHR$(75)
LET KRT$ = CHR$(0) & CHR$(77)
LET KHOME$ = CHR$(0) & CHR$(71)
LET KEND$ = CHR$(0) & CHR$(79)
LET KDEL$ = CHR$(0) & CHR$(83)

! ---- what's going on ----
LET MSG$ = ""
LET HASMOUSE = 0        ! 1 when there's a mouse (and keys arrive one at a time)
LET HELP = 0            ! 1 while the help shows
LET QUIT = 0
LET DIRTY = 1           ! the screen needs drawing again
LET SELID = 0           ! the selected card's number, or 0
LET KEEPSEL = 0         ! scroll so the selected card shows, next time it's drawn
LET CARRY = 0           ! 1 while the selected card is picked up (Space)
LET CONFIRMID = 0       ! the card Delete was pressed on once
LET PRESSID = 0         ! the card pressed on, and where
LET PRESSX = 0
LET PRESSY = 0
LET DRAGGING = 0        ! 1 once it's being dragged
LET GRABDX = 0
LET GRABDY = 0
LET DROPLN = 0          ! where it would land: the lane, the card it would go before (0: the end)
LET DROPBEFORE = 0
LET MARKY = 0
LET LASTID = 0          ! the last card clicked (for a double-click)
LET MODE = 0            ! typing (ui.bas's UITYPED$): 1 a command, 2 a card's new text, 3 a new card
LET EDITID = 0
LET ADDLN = 1
LET NOSAVE = 0          ! 1 when the file there couldn't be read: don't save over it
LET SAVEOK = 0
LET LOADOK = 0
LET LOADBAD = 0
LET RAW$ = ""
LET NTOK = 0
LET HITKIND = 0         ! 9700: 1 a card, else 0
LET HITID = 0
LET HITLN = 0
LET FILE$ = BOARD$ & ".board"
LET DAY$ = ""
LET FI = 0              ! 8950's answer: a card's index
LET LV = 0              ! 9140's: a lane
LET NAMEOK = 0          ! 9130's and 9120's: a board's name is fine, and there's one by that name
LET EXISTS = 0
LET MX = 0              ! the pointer, as ui.bas found it this frame
LET MY = 0
LET MS = -1
! the layout, in points (9000)
LET LW = 296            ! a lane's width, and its top and bottom
LET LT = 60
LET LB = 540
LET CW = 276            ! a card's width
LET HEADH = 46          ! a lane's name, above its cards
LET CINSET = 10         ! a card, inside its lane
LET GUT = 16            ! between lanes, and between cards
LET CGAP = 8
LET H = 0               ! a card's height (9020)
LET EBX = 0             ! the box being typed in (a card being edited or added), to click in
LET EBY = 0
LET EBW = 0
LET EBH = 0

GOSUB 9990              ! today's date, for the title bar
GOSUB 9900              ! open kanban.board, if it's there
IF LOADOK = 1 THEN
  LET MSG$ = "Opened " & FILE$ & " · " & LTRIM$(STR$(NT)) & " cards"
ELSEIF LOADBAD = 1 THEN
  LET NOSAVE = 1
  LET MSG$ = FILE$ & " isn't a board this can read, so it won't save over it (S name saves elsewhere)"
ELSE
  ! A first run: a few cards that show how it works.
  DO
    READ T$
    IF T$ = "*" THEN EXIT DO
    READ L
    LET NT = NT + 1
    LET TASK$(NT) = T$
    LET LANE(NT) = L
    LET ID(NT) = NEXTID
    LET NEXTID = NEXTID + 1
  LOOP
  LET MSG$ = "A new board: it saves itself to " & FILE$ & " as you go"
END IF

DATA "Click a card to select it, double-click to edit it", 1
DATA "Drag cards to another lane, or up and down", 1
DATA "Add one with + Add a card, or type A Buy milk", 1
DATA "Select a card and press Delete (twice) to remove it", 1
DATA "Arrow keys select; Space picks a card up, arrows move it", 2
DATA "The board saves itself as you go", 3
DATA "Press H for help", 3
DATA "*", 0

! --- main loop ----------------------------------------------------------
CALL UIBEGIN
IF UIMS < 0 THEN
  ! No pointing device: one typed command per line.
  DO
    GOSUB 9010                     ! draw the board (or the help)
    LINE INPUT ""; RAW$            ! whole line; "" ; suppresses the "?" prompt
    GOSUB 9150                     ! run it
    IF QUIT = 1 THEN EXIT DO
    CALL UIBEGIN                   ! (the screen's size, for the next drawing)
  LOOP
ELSE
  ! A mouse, and keys one at a time.
  LET HASMOUSE = 1
  GOSUB 9010                       ! the board is up before the first key or click
  LET DIRTY = 0
  DO
    DO
      LET K$ = INKEY$
      IF K$ = "" THEN EXIT DO
      GOSUB 8000                   ! a key
      LET DIRTY = 1
      IF QUIT = 1 THEN EXIT DO
    LOOP
    IF QUIT = 1 THEN EXIT DO
    ! the screen and the mouse: 2 = pressed, 1 = held, 3 = released, 0 = up
    CALL UIBEGIN
    LET MX = UIMX
    LET MY = UIMY
    LET MS = UIMS
    GOSUB 8500
    ! drawing it all again, when anything's changed (the pointer moving lights up what's
    ! under it; the buttons are clicked as they're drawn)
    IF DIRTY = 1 OR UICHANGED = 1 THEN
      GOSUB 9010
      LET DIRTY = 0
    END IF
    SLEEP 0.03                     ! ~30 frames/second (also presents the frame)
  LOOP
END IF
END

! ======================================================================
! 8000  A KEY (K$), with a mouse: typing, or the selected card.
! ======================================================================
8000 REM ---- a key ----
IF HELP = 1 THEN
  LET HELP = 0                     ! any key leaves the help
  RETURN
END IF
IF CONFIRMID > 0 THEN              ! Delete was pressed once: again deletes
  IF K$ = KDEL$ OR K$ = KBS$ OR K$ = KBS2$ THEN
    LET DELID = CONFIRMID
    LET CONFIRMID = 0
    GOSUB 8900
    RETURN
  END IF
  LET CONFIRMID = 0
  LET MSG$ = "Kept it"
  IF K$ = KESC$ THEN RETURN
END IF
IF MODE > 0 THEN
  GOSUB 8100                       ! the text being typed
  RETURN
END IF
IF K$ = KENTER$ OR K$ = CHR$(10) THEN
  IF CARRY = 1 THEN
    LET CARRY = 0
    LET MSG$ = "Put it down"
  ELSE
    GOSUB 8050                     ! edit the selected card
  END IF
ELSEIF K$ = " " THEN
  IF SELID > 0 THEN
    LET CARRY = 1 - CARRY
    IF CARRY = 1 THEN
      LET MSG$ = "Picked up #" & LTRIM$(STR$(SELID)) & ": the arrow keys move it"
    ELSE
      LET MSG$ = "Put it down"
    END IF
  END IF
ELSEIF K$ = KESC$ THEN
  IF CARRY = 1 THEN
    LET CARRY = 0
  ELSE
    LET SELID = 0
  END IF
ELSEIF K$ = KDEL$ OR K$ = KBS$ OR K$ = KBS2$ THEN
  IF SELID > 0 THEN
    LET FID = SELID
    GOSUB 8950
    LET CONFIRMID = SELID
    LET CARRY = 0
    LET MSG$ = "Delete #" & LTRIM$(STR$(SELID)) & " """ & FIT$(TASK$(FI), 28) & """? Press Delete again"
  END IF
ELSEIF K$ = KUP$ OR K$ = KDN$ OR K$ = KLT$ OR K$ = KRT$ THEN
  GOSUB 8200                       ! select, or move the card picked up
ELSEIF LEN(K$) = 1 AND K$ >= " " AND K$ <> KBS2$ THEN
  LET MODE = 1                     ! the start of a command
  CALL UIEDITSTART(K$)
  LET CARRY = 0
END IF
RETURN

! ----------------------------------------------------------------------
! 8050  EDIT the selected card, where it is on the board.
! ----------------------------------------------------------------------
8050 REM ---- edit the selected card ----
IF SELID = 0 THEN
  LET MSG$ = "Select a card first: click it, or press an arrow key"
  RETURN
END IF
LET FID = SELID
GOSUB 8950
IF FI = 0 THEN
  LET MSG$ = "There's no card #" & LTRIM$(STR$(SELID))
  LET SELID = 0
  RETURN
END IF
LET MODE = 2
LET EDITID = SELID
CALL UIEDITSTART(TASK$(FI))
LET CARRY = 0
LET KEEPSEL = 1
RETURN

! ----------------------------------------------------------------------
! 8100  TYPING — K$ edits what's typed (ui.bas: UIEDITKEY); Enter finishes,
!        Esc cancels.
! ----------------------------------------------------------------------
8100 REM ---- typing ----
LET TYPEDKEY = UIEDITKEY(K$)
IF TYPEDKEY = 1 THEN
  GOSUB 8150
ELSEIF TYPEDKEY = -1 THEN
  IF MODE = 2 THEN
    LET MSG$ = "Left #" & LTRIM$(STR$(EDITID)) & " as it was"
  ELSE
    LET MSG$ = "Cancelled"
  END IF
  LET MODE = 0
ELSEIF MODE = 1 AND UITYPED$ = "" THEN
  LET MODE = 0                     ! a command rubbed out
END IF
RETURN

! ----------------------------------------------------------------------
! 8150  FINISH TYPING — run the command, change the card, or add the new one.
! ----------------------------------------------------------------------
8150 REM ---- finish typing ----
LET TXT$ = LTRIM$(RTRIM$(UITYPED$))
LET WASMODE = MODE
LET MODE = 0
IF WASMODE = 1 THEN
  LET RAW$ = TXT$
  GOSUB 9150
ELSEIF WASMODE = 2 THEN
  LET EI = EDITID
  LET ET$ = TXT$
  GOSUB 9450
ELSEIF TXT$ = "" THEN
  LET MSG$ = "Nothing to add"
ELSE
  LET AT$ = TXT$
  LET AL = ADDLN
  GOSUB 9400
END IF
RETURN

! ----------------------------------------------------------------------
! 8200  ARROWS — select the card above, below or beside; or, picked up, move it.
! ----------------------------------------------------------------------
8200 REM ---- arrow keys ----
IF SELID = 0 THEN                  ! nothing selected: the first card there is
  FOR ALN = 1 TO 3
    FOR AI = 1 TO NT
      IF SELID = 0 AND LANE(AI) = ALN THEN LET SELID = ID(AI)
    NEXT AI
  NEXT ALN
  LET KEEPSEL = 1
  RETURN
END IF
LET FID = SELID
GOSUB 8950
IF FI = 0 THEN RETURN
LET SL = LANE(FI)
! its place in the lane (SK, from 1), and the cards before and after it
LET SK = 1
LET PREVI = 0
LET NEXTI = 0
LET NEXT2I = 0
FOR AI = 1 TO NT
  IF LANE(AI) = SL THEN
    IF AI < FI THEN
      LET SK = SK + 1
      LET PREVI = AI
    ELSEIF AI > FI THEN
      IF NEXTI = 0 THEN
        LET NEXTI = AI
      ELSEIF NEXT2I = 0 THEN
        LET NEXT2I = AI
      END IF
    END IF
  END IF
NEXT AI
IF K$ = KUP$ OR K$ = KDN$ THEN
  IF CARRY = 1 THEN
    IF K$ = KUP$ AND PREVI > 0 THEN
      LET SRC = FI
      LET NL = SL
      LET BEFORE = PREVI
      GOSUB 8800
      LET MSG$ = "Moved #" & LTRIM$(STR$(SELID)) & " up"
      GOSUB 9790
    ELSEIF K$ = KDN$ AND NEXTI > 0 THEN
      LET SRC = FI
      LET NL = SL
      LET BEFORE = NEXT2I          ! (0: to the end of the lane)
      GOSUB 8800
      LET MSG$ = "Moved #" & LTRIM$(STR$(SELID)) & " down"
      GOSUB 9790
    END IF
  ELSE
    IF K$ = KUP$ AND PREVI > 0 THEN LET SELID = ID(PREVI)
    IF K$ = KDN$ AND NEXTI > 0 THEN LET SELID = ID(NEXTI)
  END IF
ELSE
  ! left or right: the lane next door (just selecting, the next one with cards)
  LET STEPD = 1
  IF K$ = KLT$ THEN LET STEPD = -1
  LET TL2 = SL + STEPD
  IF CARRY = 0 THEN
    DO WHILE TL2 >= 1 AND TL2 <= 3
      LET TC = 0
      FOR AI = 1 TO NT
        IF LANE(AI) = TL2 THEN LET TC = TC + 1
      NEXT AI
      IF TC > 0 THEN EXIT DO
      LET TL2 = TL2 + STEPD
    LOOP
  END IF
  IF TL2 >= 1 AND TL2 <= 3 THEN
    ! the card in the same place there (TGT), or its last (TLAST)
    LET TK = 0
    LET TGT = 0
    LET TLAST = 0
    FOR AI = 1 TO NT
      IF LANE(AI) = TL2 AND AI <> FI THEN
        LET TK = TK + 1
        IF TK = SK THEN LET TGT = AI
        LET TLAST = AI
      END IF
    NEXT AI
    IF CARRY = 1 THEN
      LET SRC = FI
      LET NL = TL2
      LET BEFORE = TGT
      GOSUB 8800
      LET MSG$ = "Moved #" & LTRIM$(STR$(SELID)) & " to " & LANENAME$(TL2)
      GOSUB 9790
    ELSE
      IF TGT = 0 THEN LET TGT = TLAST
      IF TGT > 0 THEN LET SELID = ID(TGT)
    END IF
  END IF
END IF
LET KEEPSEL = 1
RETURN

! ======================================================================
! 8500  THE MOUSE (MX, MY, MS): select, double-click, drag and drop. (The
!        buttons — "+ Add a card", "▲ more", Help — click themselves as the board's
!        drawn, 9030.)
! ======================================================================
8500 REM ---- the mouse ----
IF HELP = 1 THEN
  IF MS = 2 THEN
    LET HELP = 0                   ! a click leaves the help ...
    CALL UIFORGETPRESS             ! ... and clicks nothing beneath it
    LET DIRTY = 1
  END IF
  RETURN
END IF
IF MS = 2 THEN
  IF UIONCONTROL() = 1 THEN RETURN ! a button's
  IF MODE >= 2 AND UIIN(EBX, EBY, EBW, EBH) = 1 THEN RETURN   ! in what's being typed
  GOSUB 9700                       ! what's under the pointer
  IF CONFIRMID > 0 THEN
    LET CONFIRMID = 0
    LET MSG$ = "Kept it"
  END IF
  IF MODE >= 2 THEN GOSUB 8150     ! clicking away keeps what was typed
  IF HITKIND = 1 THEN
    IF HITID = LASTID AND UIDOUBLE = 1 THEN
      LET SELID = HITID            ! a double-click: edit it
      GOSUB 8050
      LET LASTID = 0
    ELSE
      LET SELID = HITID
      LET CARRY = 0
      LET PRESSID = HITID          ! it may be the start of a drag
      LET PRESSX = MX
      LET PRESSY = MY
      LET DRAGGING = 0
      LET LASTID = HITID
    END IF
  ELSE
    LET SELID = 0                  ! a click on nothing
    LET CARRY = 0
  END IF
  LET DIRTY = 1
ELSEIF MS = 1 THEN
  IF PRESSID > 0 THEN
    IF DRAGGING = 0 AND (ABS(MX - PRESSX) >= 5 OR ABS(MY - PRESSY) >= 5) THEN
      LET FID = PRESSID
      GOSUB 8950
      IF FI > 0 AND CTOPY(FI) >= 0 THEN
        LET DRAGGING = 1
        LET GRABDX = PRESSX - LX(LANE(FI)) - CINSET
        LET GRABDY = PRESSY - CTOPY(FI)
        LET MSG$ = "Moving #" & LTRIM$(STR$(PRESSID)) & ": drop it in a lane"
      END IF
    END IF
    IF DRAGGING = 1 THEN
      GOSUB 8700                   ! where it would land
      LET DIRTY = 1
    END IF
  END IF
ELSEIF MS = 3 THEN
  IF PRESSID > 0 AND DRAGGING = 1 THEN
    GOSUB 8700
    LET FID = PRESSID
    GOSUB 8950
    IF DROPLN > 0 AND FI > 0 THEN
      LET OLDLN = LANE(FI)
      LET SRC = FI
      LET NL = DROPLN
      LET BEFORE = DROPBEFORE
      GOSUB 8800
      IF NL <> OLDLN THEN
        LET MSG$ = "Moved #" & LTRIM$(STR$(PRESSID)) & " to " & LANENAME$(NL)
      ELSE
        LET MSG$ = "Moved #" & LTRIM$(STR$(PRESSID))
      END IF
      GOSUB 9790
    ELSE
      LET MSG$ = "Left #" & LTRIM$(STR$(PRESSID)) & " where it was"
    END IF
    LET SELID = PRESSID
    LET KEEPSEL = 1
  END IF
  LET PRESSID = 0
  LET DRAGGING = 0
  LET DIRTY = 1
END IF
RETURN

! ----------------------------------------------------------------------
! 8700  DROP — where the card being dragged would land, from the pointer:
!        the lane (DROPLN, 0 for none), the card it would go before (DROPBEFORE,
!        0 for the end of the lane), and where to mark it (MARKY).
! ----------------------------------------------------------------------
8700 REM ---- where it would land ----
LET DROPLN = 0
LET DROPBEFORE = 0
FOR DI = 1 TO 3
  IF MX >= LX(DI) - GUT / 2 AND MX < LX(DI) + LW + GUT / 2 AND MY >= LT AND MY <= LB THEN LET DROPLN = DI
NEXT DI
IF DROPLN = 0 THEN RETURN
LET MARKY = -1
LET LASTVIS = 0
FOR DI = 1 TO NT
  IF LANE(DI) = DROPLN AND ID(DI) <> PRESSID AND DROPBEFORE = 0 THEN
    IF CTOPY(DI) >= 0 THEN
      IF MY < (CTOPY(DI) + CBOTY(DI)) / 2 THEN
        LET DROPBEFORE = DI        ! above this card's middle: before it
        LET MARKY = CTOPY(DI) - CGAP / 2
      ELSE
        LET LASTVIS = DI
      END IF
    ELSEIF LASTVIS > 0 THEN
      LET DROPBEFORE = DI          ! below what shows: after the last card shown
    END IF
  END IF
NEXT DI
IF MARKY < 0 THEN
  IF LASTVIS > 0 THEN
    LET MARKY = CBOTY(LASTVIS) + CGAP / 2
  ELSE
    LET MARKY = LT + HEADH
  END IF
END IF
RETURN

! ----------------------------------------------------------------------
! 8800  MOVE card SRC (an index) to lane NL, before card BEFORE (0: at the
!        end of the lane). The cards after it shift to close the gap.
! ----------------------------------------------------------------------
8800 REM ---- move a card ----
IF BEFORE = SRC THEN
  LET LANE(SRC) = NL
  RETURN
END IF
LET MVT$ = TASK$(SRC)
LET MVID = ID(SRC)
FOR MI = SRC TO NT - 1
  LET TASK$(MI) = TASK$(MI + 1)
  LET LANE(MI) = LANE(MI + 1)
  LET ID(MI) = ID(MI + 1)
NEXT MI
LET NT = NT - 1
IF BEFORE > SRC THEN LET BEFORE = BEFORE - 1
LET POSN = NT + 1                  ! the end: last in its lane
IF BEFORE > 0 THEN LET POSN = BEFORE
FOR MI = NT TO POSN STEP -1
  LET TASK$(MI + 1) = TASK$(MI)
  LET LANE(MI + 1) = LANE(MI)
  LET ID(MI + 1) = ID(MI)
NEXT MI
LET TASK$(POSN) = MVT$
LET LANE(POSN) = NL
LET ID(POSN) = MVID
LET NT = NT + 1
RETURN

! ----------------------------------------------------------------------
! 8900  DELETE card number DELID (the selection moves to its neighbour).
! ----------------------------------------------------------------------
8900 REM ---- delete a card ----
LET FID = DELID
GOSUB 8950
IF FI = 0 THEN
  LET MSG$ = "There's no card #" & LTRIM$(STR$(DELID))
  RETURN
END IF
LET DT$ = TASK$(FI)
LET DL = LANE(FI)
LET NEXTSEL = 0
FOR DI = NT TO FI + 1 STEP -1      ! the next card in its lane ...
  IF LANE(DI) = DL THEN LET NEXTSEL = ID(DI)
NEXT DI
IF NEXTSEL = 0 THEN                ! ... or the one before it
  FOR DI = 1 TO FI - 1
    IF LANE(DI) = DL THEN LET NEXTSEL = ID(DI)
  NEXT DI
END IF
FOR DI = FI TO NT - 1
  LET TASK$(DI) = TASK$(DI + 1)
  LET LANE(DI) = LANE(DI + 1)
  LET ID(DI) = ID(DI + 1)
NEXT DI
LET NT = NT - 1
IF SELID = DELID THEN LET SELID = NEXTSEL
IF HASMOUSE = 0 THEN LET SELID = 0
LET KEEPSEL = 1
LET MSG$ = "Deleted #" & LTRIM$(STR$(DELID)) & " " & FIT$(DT$, 40)
GOSUB 9790
RETURN

! ----------------------------------------------------------------------
! 8950  FIND card number FID: its index FI (0 if there's no such card).
! ----------------------------------------------------------------------
8950 REM ---- find a card ----
LET FI = 0
FOR FJ = 1 TO NT
  IF ID(FJ) = FID THEN LET FI = FJ
NEXT FJ
RETURN

! ======================================================================
! 9010  DRAW — the board, scrolled (KEEPSEL) so the selected card shows; and
!        the help over it, if it's up.
! ======================================================================
9010 REM ---- draw ----
IF KEEPSEL = 1 AND SELID > 0 THEN
  LET FID = SELID
  GOSUB 8950
  IF FI > 0 THEN
    LET KL = LANE(FI)
    LET KK = 0                     ! its place in its lane, from 0
    FOR KI = 1 TO FI - 1
      IF LANE(KI) = KL THEN LET KK = KK + 1
    NEXT KI
    IF KK < TOP(KL) THEN LET TOP(KL) = KK
    FOR KTRY = 1 TO MAXT
      GOSUB 9000
      IF CTOPY(FI) >= 0 THEN EXIT FOR
      LET TOP(KL) = TOP(KL) + 1    ! below what shows: scroll down a card
    NEXT KTRY
  END IF
END IF
LET KEEPSEL = 0
GOSUB 9000                         ! where everything goes
GOSUB 9030                         ! and drawing it there
IF HELP = 1 THEN GOSUB 9050
RETURN

! ======================================================================
! 9000  LAYOUT — where the lanes, and the cards that show in them, go: in points,
!        y down (ui.bas's), from the screen's size. Each lane: its name, then its
!        cards from TOP(LN) on while they fit (a "▲ n more" above them if it's
!        scrolled, "▼ n more" below if they don't all fit), then "+ Add a card".
!        A card is its text, wrapped (three lines, or all of it when it's
!        selected), and a row for its number. On a terminal everything's a row
!        of text high, with no gaps.
! ======================================================================
9000 REM ---- layout ----
IF UICELLS = 1 THEN
  LET MX0 = UICOL                  ! the margin, and the gap between lanes
  LET GUT = 2 * UICOL
  LET TOPH = UIROW                 ! the title bar, the status bar, a lane's name
  LET STATH = UIROW
  LET HEADH = UIROW
  LET CINSET = UICOL               ! a card, inside its lane; its text, inside it
  LET CPADX = UICOL
  LET CPADY = 0
  LET LH = UIROW                   ! a line of its text, and its number's row
  LET METAH = 0
  LET CGAP = 0                     ! between cards
  LET MOREH = UIROW                ! "▲ n more", "▼ n more", "+ Add a card"
  LET ADDH = UIROW
ELSE
  LET MX0 = 20
  LET GUT = 16
  LET TOPH = 60
  LET STATH = 36
  LET HEADH = 46
  LET CINSET = 10
  LET CPADX = 14
  LET CPADY = 11
  LET LH = 21
  LET METAH = 20
  LET CGAP = 8
  LET MOREH = 28
  LET ADDH = 38
END IF
IF HASMOUSE = 0 THEN LET STATH = 2 * STATH   ! (the commands, then the status)
LET LW = (UIW - 2 * MX0 - 2 * GUT) / 3
LET LT = TOPH
LET LB = UIH - STATH
LET CW = LW - 2 * CINSET
FOR LN = 1 TO 3
  LET LX(LN) = MX0 + (LN - 1) * (LW + GUT)
NEXT LN
FOR I = 1 TO NT
  LET CTOPY(I) = -1
NEXT I
LET EBW = 0
FOR LN = 1 TO 3
  LET LCNT(LN) = 0
  FOR I = 1 TO NT
    IF LANE(I) = LN THEN LET LCNT(LN) = LCNT(LN) + 1
  NEXT I
  ! the cards go from CY0 down to CY1: above "+ Add a card", or the new card being typed
  LET CY0 = LT + HEADH
  LET CY1 = LB - CINSET
  IF HASMOUSE = 1 THEN
    LET ADDBOX = ADDH
    IF MODE = 3 AND ADDLN = LN THEN
      LET ADDBOX = 2 * CPADY + UIEDITLINES(CW - 2 * CPADX, UIBODY, "SANS") * LH + METAH
      LET EBX = LX(LN) + CINSET
      LET EBY = CY1 - ADDBOX
      LET EBW = CW
      LET EBH = ADDBOX
    END IF
    LET CY1 = CY1 - ADDBOX - CGAP
  END IF
  LET SKIPN = TOP(LN)
  IF SKIPN > LCNT(LN) - 1 THEN LET SKIPN = MAX(0, LCNT(LN) - 1)
  LET TOP(LN) = SKIPN
  LET UPAT(LN) = -1
  LET DNAT(LN) = -1
  LET VISN(LN) = 0
  LET ROW = CY0
  IF SKIPN > 0 THEN
    LET UPAT(LN) = ROW
    LET ROW = ROW + MOREH + CGAP
  END IF
  ! a selected card shows as much of its text as the lane has room for
  LET LMAXL(LN) = MAX(1, INT((CY1 - CY0 - 2 * CPADY - METAH - 2 * (MOREH + CGAP)) / LH))
  LET K = 0
  LET FULL = 0
  LET HID = 0
  FOR I = 1 TO NT
    IF LANE(I) = LN THEN
      LET K = K + 1
      IF K > SKIPN AND FULL = 0 THEN
        GOSUB 9020                 ! its height: H
        LET NEED = H
        IF K < LCNT(LN) THEN LET NEED = H + CGAP + MOREH   ! (room for "▼ n more" after it)
        IF ROW + NEED > CY1 AND VISN(LN) > 0 THEN
          LET FULL = 1
          LET HID = LCNT(LN) - K + 1
        ELSE
          LET CTOPY(I) = ROW
          LET CBOTY(I) = ROW + H
          LET VISN(LN) = VISN(LN) + 1
          LET ROW = ROW + H + CGAP
          IF MODE = 2 AND ID(I) = EDITID THEN
            LET EBX = LX(LN) + CINSET
            LET EBY = CTOPY(I)
            LET EBW = CW
            LET EBH = H
          END IF
        END IF
      END IF
    END IF
  NEXT I
  IF HID > 0 THEN LET DNAT(LN) = ROW
NEXT LN
RETURN

! ----------------------------------------------------------------------
! 9020  A CARD's lines and height: card I's text wrapped (or, being edited,
!        what's typed) — NL lines of it, of NWRAP, in WRAP$() — and its height H.
! ----------------------------------------------------------------------
9020 REM ---- a card's lines ----
LET CTW = CW - 2 * CPADX           ! its text's width (on a terminal its number's on its first line)
IF UICELLS = 1 THEN LET CTW = CTW - (LEN(LTRIM$(STR$(ID(I)))) + 2) * UICOL
IF MODE = 2 AND ID(I) = EDITID THEN
  LET NWRAP = UIEDITLINES(CTW, UIBODY, "SANS")
  LET NL = NWRAP
ELSE
  LET NWRAP = UIWRAP(TASK$(I), CTW, UIBODY, "SANS", WRAP$, WRP)
  LET NL = MIN(NWRAP, 3)
  IF ID(I) = SELID AND DRAGGING = 0 THEN LET NL = MIN(NWRAP, LMAXL(LANE(I)))
END IF
LET H = 2 * CPADY + NL * LH + METAH
RETURN

! ======================================================================
! 9030  PAINT — the title bar, the lanes and their cards, a card being
!        dragged, and the status bar. The buttons are clicked here, as they're
!        drawn (ui.bas's UIBUTTON).
! ======================================================================
9030 REM ---- paint ----
CALL UICLEAR(UCBG)
! --- the title bar: the board's name, how it's going, and today ------------
LET NDONE = 0
FOR I = 1 TO NT
  IF LANE(I) = 3 THEN LET NDONE = NDONE + 1
NEXT I
LET TY = TOPH / 2
CALL UITEXT(MX0, TY, "Kanban", UITITLE, "SANS BOLD", UCINK)
LET X = MX0 + UIWIDTH("Kanban", UITITLE, "SANS BOLD") + 12
IF UICELLS = 1 THEN LET X = MX0 + 8 * UICOL
LET PW = UIPILL(X, TY, FILE$, UISMALL, UCSURFACE, UCMUTED)
LET RX = UIW - MX0                 ! the right of the bar, going left
IF HASMOUSE = 1 THEN
  LET BW = 120
  IF UICELLS = 1 THEN LET BW = 12 * UICOL
  LET BH = MIN(34, TOPH)
  IF UIBUTTON(RX - BW, TY - BH / 2, BW, BH, "+ New card", "PRIMARY") THEN
    LET ADDLN = 1                  ! (to the selected card's lane)
    LET FID = SELID
    GOSUB 8950
    IF FI > 0 THEN LET ADDLN = LANE(FI)
    LET MODE = 3
    CALL UIEDITSTART("")
    LET CARRY = 0
    LET DIRTY = 1
  END IF
  LET RX = RX - BW - 8
  LET BW = 70
  IF UICELLS = 1 THEN LET BW = 6 * UICOL
  IF UIBUTTON(RX - BW, TY - BH / 2, BW, BH, "Help", "GHOST") THEN
    LET HELP = 1
    LET DIRTY = 1
  END IF
  LET RX = RX - BW - 20
END IF
LET R$ = LTRIM$(STR$(NDONE)) & " of " & LTRIM$(STR$(NT)) & " done"
IF UICELLS = 1 THEN
  LET R$ = DAY$ & "  ·  " & R$
  CALL UILABEL(X + PW + UICOL, 0, RX - X - PW - UICOL, UIROW, R$, UISMALL, "SANS", UCMUTED, "RIGHT")
ELSE
  LET FRAC = 0
  IF NT > 0 THEN LET FRAC = NDONE / NT
  CALL UIPROGRESS(RX - 90, TY - 3, 90, 6, FRAC, LACC(3))
  LET RX = RX - 102
  CALL UILABEL(X + PW + 12, 0, RX - X - PW - 12, TOPH, R$, UISMALL, "SANS", UCMUTED, "RIGHT")
  LET RX = RX - UIWIDTH(R$, UISMALL, "SANS") - 10
  CALL UILABEL(X + PW + 12, 0, RX - X - PW - 12, TOPH, DAY$ & "  ·", UISMALL, "SANS", UCFAINT, "RIGHT")
END IF

! --- the lanes ------------------------------------------------------------
FOR LN = 1 TO 3
  LET X0 = LX(LN)
  LET FILL = UCSURFACE
  IF DRAGGING = 1 AND DROPLN = LN THEN LET FILL = UCHOVER
  CALL UIBOX(X0, LT, LW, LB - LT, 12, FILL)
  ! its name and how many cards it has
  LET HY = LT + HEADH / 2
  IF UICELLS = 1 THEN
    CALL UITEXT(X0 + CPADX, HY, UCASE$(LANENAME$(LN)), UIBODY, "SANS BOLD", LACC(LN))
    LET X = X0 + CPADX + (LEN(LANENAME$(LN)) + 1) * UICOL
  ELSE
    CALL UIBOX(X0 + 16, HY - 4, 8, 8, 4, LACC(LN))
    CALL UITEXT(X0 + 32, HY, LANENAME$(LN), UIBODY, "SANS BOLD", UCINK)
    LET X = X0 + 32 + UIWIDTH(LANENAME$(LN), UIBODY, "SANS BOLD") + 8
  END IF
  LET PW = UIPILL(X, HY, LTRIM$(STR$(LCNT(LN))), UISMALL - 1, UCRAISED, UCMUTED)
  ! scrolled: the cards above
  IF UPAT(LN) >= 0 THEN
    IF UIBUTTON(X0 + CINSET, UPAT(LN), CW, MOREH, "▲ " & LTRIM$(STR$(TOP(LN))) & " more", "GHOST") THEN
      LET TOP(LN) = MAX(0, TOP(LN) - MAX(1, VISN(LN) - 1))
      LET DIRTY = 1
    END IF
  END IF
  ! its cards
  FOR I = 1 TO NT
    IF LANE(I) = LN AND CTOPY(I) >= 0 THEN
      GOSUB 9020
      LET CXL = X0 + CINSET
      LET CYT = CTOPY(I)
      LET CFILL = UCRAISED
      LET CTCOL = UCINK
      IF LN = 3 THEN LET CTCOL = UCMUTED   ! done: quieter
      LET CRING = -1
      LET CLIFT = 3
      LET CGHOST = 0
      IF ID(I) = SELID THEN
        LET CRING = UCACCENT
        LET CTCOL = UCINK
        IF CARRY = 1 THEN
          LET CRING = LACC(LN)
          LET CLIFT = 12
        END IF
      END IF
      IF MODE = 2 AND ID(I) = EDITID THEN LET CRING = UCACCENT
      IF UICELLS = 1 AND CRING >= 0 THEN        ! (a terminal: no ring, but the card in a colour)
        LET CFILL = CRING
        LET CTCOL = UCBG
        LET CRING = -1
      END IF
      IF ID(I) = PRESSID AND DRAGGING = 1 THEN   ! the card being dragged, left behind
        LET CGHOST = 1
        LET CRING = -1
        LET CLIFT = 0
      END IF
      GOSUB 9300
    END IF
  NEXT I
  ! the cards below
  IF DNAT(LN) >= 0 THEN
    LET HID = LCNT(LN) - TOP(LN) - VISN(LN)
    IF UIBUTTON(X0 + CINSET, DNAT(LN), CW, MOREH, "▼ " & LTRIM$(STR$(HID)) & " more", "GHOST") THEN
      LET TOP(LN) = TOP(LN) + MAX(1, VISN(LN) - 1)
      LET DIRTY = 1
    END IF
  END IF
  ! "+ Add a card", or the new card being typed
  IF HASMOUSE = 1 THEN
    IF MODE = 3 AND ADDLN = LN THEN
      CALL UIBOX(EBX - 2, EBY - 2, EBW + 4, EBH + 4, UIRADIUS + 2, UCACCENT)
      CALL UIBOX(EBX, EBY, EBW, EBH, UIRADIUS, UCRAISED)
      CALL UIEDITDRAW(EBX + CPADX, EBY + CPADY + LH / 2, EBW - 2 * CPADX, UIBODY, "SANS", UCINK, LH, "Type the card")
      IF UICELLS = 0 THEN CALL UITEXT(EBX + CPADX, EBY + EBH - CPADY - METAH / 2 + 2, "Enter adds it · Esc cancels", UISMALL, "SANS", UCFAINT)
    ELSE
      IF UIBUTTON(X0 + CINSET, LB - CINSET - ADDH, CW, ADDH, "+ Add a card", "GHOST") THEN
        LET ADDLN = LN
        LET MODE = 3
        CALL UIEDITSTART("")
        LET CARRY = 0
        LET DIRTY = 1
      END IF
    END IF
  END IF
NEXT LN

! --- dragging: mark where it would land, and draw it at the pointer --------
IF DRAGGING = 1 THEN
  IF DROPLN > 0 THEN CALL UIBOX(LX(DROPLN) + CINSET, MARKY - 1.5, CW, 3, 1.5, LACC(DROPLN))
  LET FID = PRESSID
  GOSUB 8950
  IF FI > 0 THEN
    LET I = FI
    GOSUB 9020
    LET CXL = MX - GRABDX
    LET CYT = MY - GRABDY
    LET CFILL = UCHOVER
    LET CTCOL = UCINK
    LET CRING = -1
    LET CLIFT = 16
    LET CGHOST = 0
    GOSUB 9300
  END IF
END IF

! --- the status bar: what happened, and what the keys do now ---------------
IF MODE = 1 THEN
  LET HINT$ = "Enter runs the command  ·  Esc cancels  ·  H Enter: help"
ELSEIF MODE = 2 THEN
  LET HINT$ = "Editing #" & LTRIM$(STR$(EDITID)) & "  ·  Enter saves  ·  Esc cancels"
ELSEIF MODE = 3 THEN
  LET HINT$ = "Adding to " & LANENAME$(ADDLN) & "  ·  Enter adds it  ·  Esc cancels"
ELSEIF DRAGGING = 1 THEN
  LET HINT$ = "Let go over a lane to move it there"
ELSEIF CONFIRMID > 0 THEN
  LET HINT$ = "Delete again deletes it  ·  any other key keeps it"
ELSEIF CARRY = 1 THEN
  LET HINT$ = "↑ ↓ ← → move the card  ·  Space or Enter puts it down"
ELSEIF SELID > 0 THEN
  LET HINT$ = "Enter edit  ·  Space pick up  ·  Del delete  ·  ↑ ↓ ← → select"
ELSE
  LET HINT$ = "Click a card, or ↑ ↓ ← →  ·  or type a command (H: help)"
END IF
LET SY = UIH - STATH / 2
IF HASMOUSE = 0 THEN
  LET SY = UIH - STATH / 4
  CALL UILABEL(MX0, UIH - STATH, UIW - 2 * MX0, STATH / 2, "A add  M move  E edit  D delete  N new  S save  L open  H help  Q quit", UISMALL, "SANS", UCMUTED, "LEFT")
END IF
IF MODE = 1 THEN
  CALL UITEXT(MX0, SY, "> ", UIBODY, "MONO BOLD", UCACCENT)
  CALL UIEDITDRAW(MX0 + UIWIDTH("> ", UIBODY, "MONO BOLD"), SY, UIW, UIBODY, "MONO", UCINK, LH, "")
ELSE
  LET MCOL = UCMUTED
  IF CONFIRMID > 0 THEN LET MCOL = UCDANGER
  LET MW = MIN(UIWIDTH(MSG$, UISMALL, "SANS"), (UIW - 2 * MX0) * 0.6)
  CALL UILABEL(MX0, SY - STATH / 4, MW + 1, STATH / 2, MSG$, UISMALL, "SANS", MCOL, "LEFT")
END IF
IF HASMOUSE = 1 THEN              ! (the hint has what the message leaves)
  LET HX = MX0 + 24
  IF MODE <> 1 THEN LET HX = HX + MW
  IF MODE = 1 THEN LET HX = HX + UIWIDTH("> " & UITYPED$, UIBODY, "MONO") + UICOL
  CALL UILABEL(HX, UIH - STATH, UIW - MX0 - HX, STATH, HINT$, UISMALL, "SANS", UCFAINT, "RIGHT")
END IF
RETURN

! ----------------------------------------------------------------------
! 9300  CARD I — at CXL, CYT (its top left), its lines from 9020: a box in
!        CFILL with a shadow CLIFT high, a ring of CRING (-1: none) around it,
!        a stripe in its lane's colour down its left, its text in CTCOL and its
!        number under it; or, being edited, what's typed and the caret. CGHOST:
!        the place of the card being dragged, faint.
! ----------------------------------------------------------------------
9300 REM ---- one card ----
CALL UISHADOW(CXL, CYT, CW, H, UIRADIUS, CLIFT)
IF CRING >= 0 THEN CALL UIBOX(CXL - 2, CYT - 2, CW + 4, H + 4, UIRADIUS + 2, CRING)
IF CGHOST = 1 THEN
  CALL UIBOX(CXL, CYT, CW, H, UIRADIUS, UCSURFACE)
  CALL UIRING(CXL, CYT, CW, H, UIRADIUS, UCLINE)
  LET CTCOL = UCFAINT
ELSE
  CALL UIBOX(CXL, CYT, CW, H, UIRADIUS, CFILL)
  IF UIIN(CXL, CYT, CW, H) = 1 AND DRAGGING = 0 AND CRING < 0 THEN CALL UIBOX(CXL, CYT, CW, H, UIRADIUS, UCLIGHTEN)
  IF UICELLS = 0 THEN CALL UIBOX(CXL, CYT + UIRADIUS, 3, H - 2 * UIRADIUS, 1.5, LACC(LANE(I)))
END IF
LET TX = CXL + CPADX
LET TY = CYT + CPADY + LH / 2
IF MODE = 2 AND ID(I) = EDITID AND CGHOST = 0 AND CLIFT < 16 THEN
  CALL UIEDITDRAW(TX, TY, CTW, UIBODY, "SANS", UCINK, LH, "Card text")
ELSE
  FOR WJ = 1 TO NL
    LET WL$ = WRAP$(WJ)
    IF WJ = NL AND NL < NWRAP THEN LET WL$ = UIFIT$(MID$(TASK$(I), WRP(WJ)), CTW, UIBODY, "SANS")
    CALL UITEXT(TX, TY + (WJ - 1) * LH, WL$, UIBODY, "SANS", CTCOL)
  NEXT WJ
END IF
LET CIDS$ = "#" & LTRIM$(STR$(ID(I)))
IF UICELLS = 1 THEN
  CALL UILABEL(CXL, CYT, CW - CPADX, LH, CIDS$, UISMALL, "SANS", UCFAINT, "RIGHT")
ELSE
  LET MY0 = CYT + H - CPADY - METAH / 2 + 2
  IF LANE(I) = 3 THEN
    CALL UITEXT(TX, MY0, "✓ " & CIDS$, UISMALL, "SANS", UCGOOD)
  ELSE
    CALL UITEXT(TX, MY0, CIDS$, UISMALL, "SANS", UCFAINT)
  END IF
  IF ID(I) = SELID AND CARRY = 1 THEN CALL UILABEL(CXL, MY0 - 10, CW - CPADX, 20, "Picked up", UISMALL, "SANS BOLD", LACC(LANE(I)), "RIGHT")
END IF
RETURN

! ======================================================================
! 9050  HELP — what the mouse, the keys and the commands do: a dialog over
!        the dimmed board.
! ======================================================================
9050 REM ---- help ----
CALL UISCRIM
IF UICELLS = 1 THEN
  LET DW = UIW - 2 * MX0
  LET DH = UIH - 2 * UIROW
  LET RH = UIROW
  LET COL2 = 40 * UICOL
ELSE
  LET DW = MIN(UIW - 40, 720)
  LET DH = MIN(UIH - 40, 500)
  LET RH = 22
  LET COL2 = DW / 2
END IF
LET DX = (UIW - DW) / 2
LET DY = (UIH - DH) / 2
CALL UIPANEL(DX, DY, DW, DH)
LET PX = DX + 28
IF UICELLS = 1 THEN LET PX = DX + 2 * UICOL
LET Y = DY + 34
IF UICELLS = 1 THEN LET Y = DY + UIROW
CALL UITEXT(PX, Y, "Kanban help", UITITLE, "SANS BOLD", UCINK)
LET Y = Y + 1.8 * RH
IF HASMOUSE = 1 AND (UICELLS = 0 OR UIH >= 23 * UIROW) THEN
  CALL UITEXT(PX, Y, "MOUSE", UISMALL, "SANS BOLD", LACC(1))
  CALL UITEXT(PX + COL2, Y, "KEYBOARD", UISMALL, "SANS BOLD", LACC(1))
  RESTORE 9060
  FOR HI = 1 TO 5
    READ HK$, HD$, HK2$, HD2$
    LET Y = Y + RH
    GOSUB 9070
  NEXT HI
  LET Y = Y + 1.6 * RH
END IF
IF HASMOUSE = 1 THEN
  CALL UITEXT(PX, Y, "COMMANDS — type one on the board; Enter runs it", UISMALL, "SANS BOLD", LACC(2))
ELSE
  CALL UITEXT(PX, Y, "COMMANDS", UISMALL, "SANS BOLD", LACC(2))
END IF
RESTORE 9065
FOR HI = 1 TO 7
  READ HK$, HD$
  LET HK2$ = ""
  LET Y = Y + RH
  GOSUB 9070
NEXT HI
LET Y = Y + 1.6 * RH
CALL UITEXT(PX, Y, UIFIT$("The board saves itself to " & BOARD$ & ".board after every change.", DW - 56, UISMALL, "SANS"), UISMALL, "SANS", UCMUTED)
LET Y = Y + RH
IF HASMOUSE = 1 THEN
  CALL UITEXT(PX, Y, "Press any key, or click, to go back.", UISMALL, "SANS", UCFAINT)
ELSE
  CALL UITEXT(PX, Y, "Press Enter to go back.", UISMALL, "SANS", UCFAINT)
END IF
RETURN

9060 DATA "Click a card", "select it", "↑ ↓ ← →", "select a card"
DATA "Double-click", "edit it", "Enter", "edit it"
DATA "Drag a card", "move or reorder it", "Space", "pick it up: ↑ ↓ ← → move it"
DATA "+ Add a card", "add one to a lane", "Delete", "delete it (press it twice)"
DATA "▲ / ▼ more", "scroll a lane", "Esc", "cancel, or let go"
9065 DATA "A text", "add a card to To Do"
DATA "M n lane [place]", "move card n to a lane: 1-3, todo, doing or done (place 1: the top)"
DATA "E n text", "change card n's text"
DATA "D n", "delete card n"
DATA "N name  /  L name", "start a new board  /  open another (name.board)"
DATA "S [name]", "save now  /  save as name.board, and carry on there"
DATA "H  /  Q", "this help  /  quit"

! 9070  A row of the help: HK$ (a key or a click, bold) and what it does, HD$ —
!       and, if HK2$ isn't "", a second pair in the right-hand column.
9070 REM ---- a row of the help ----
LET KW = 130
IF UICELLS = 1 THEN LET KW = 18 * UICOL
IF HK2$ = "" THEN
  LET KW = 160
  IF UICELLS = 1 THEN LET KW = 19 * UICOL
END IF
CALL UITEXT(PX, Y, HK$, UISMALL, "SANS BOLD", UCINK)
LET DWID = COL2 - KW - 16
IF HK2$ = "" THEN LET DWID = DW - (PX - DX) - KW - 28
CALL UITEXT(PX + KW, Y, UIFIT$(HD$, DWID, UISMALL, "SANS"), UISMALL, "SANS", UCMUTED)
IF HK2$ <> "" THEN
  LET KW2 = 90
  IF UICELLS = 1 THEN LET KW2 = 10 * UICOL
  CALL UITEXT(PX + COL2, Y, HK2$, UISMALL, "SANS BOLD", UCINK)
  CALL UITEXT(PX + COL2 + KW2, Y, UIFIT$(HD2$, DW - (PX - DX) - COL2 - KW2 - 28, UISMALL, "SANS"), UISMALL, "SANS", UCMUTED)
END IF
RETURN

! ======================================================================
! 9150  COMMAND — run the one in RAW$ (typed on the board, or one per line).
! ======================================================================
9150 REM ---- a command ----
GOSUB 9100                         ! RAW$ → TOK$(1 .. NTOK)
LET HELP = 0
IF NTOK = 0 THEN RETURN            ! an empty line leaves the help screen
LET CMD$ = UCASE$(TOK$(1))
IF CMD$ = "Q" OR CMD$ = "QUIT" THEN
  LET QUIT = 1
ELSEIF CMD$ = "A" OR CMD$ = "ADD" THEN
  ! A text: to To Do ("Call mum at 3" is all text: M moves it on)
  LET AL = 1
  LET AT$ = ""
  FOR CI = 2 TO NTOK
    IF AT$ = "" THEN
      LET AT$ = TOK$(CI)
    ELSE
      LET AT$ = AT$ & " " & TOK$(CI)
    END IF
  NEXT CI
  IF AT$ = "" THEN
    LET MSG$ = "A needs the card's text, e.g. A Buy milk"
  ELSE
    GOSUB 9400
  END IF
ELSEIF CMD$ = "M" OR CMD$ = "MOVE" THEN
  ! M n lane [place]
  IF NTOK < 3 THEN
    LET MSG$ = "M needs a card and a lane, e.g. M 4 done (or M 4 3; M 4 2 1 puts it at the top)"
  ELSE
    LET FID = TONUM(TOK$(2))
    GOSUB 8950
    LET LT$ = TOK$(3)
    GOSUB 9140
    LET MP = 0
    IF NTOK >= 4 THEN LET MP = TONUM(TOK$(4))
    IF FI = 0 THEN
      LET MSG$ = "There's no card #" & TOK$(2)
    ELSEIF LV = 0 THEN
      LET MSG$ = "Which lane? 1 or todo, 2 or doing, 3 or done"
    ELSE
      LET BEFORE = 0               ! the card now in that place there, if any
      LET TK = 0
      FOR CI = 1 TO NT
        IF LANE(CI) = LV AND CI <> FI THEN
          LET TK = TK + 1
          IF TK = MP THEN LET BEFORE = CI
        END IF
      NEXT CI
      LET MVNUM = ID(FI)
      LET SRC = FI
      LET NL = LV
      GOSUB 8800
      LET MSG$ = "Moved #" & LTRIM$(STR$(MVNUM)) & " to " & LANENAME$(LV)
      IF HASMOUSE = 1 THEN
        LET SELID = MVNUM
        LET KEEPSEL = 1
      END IF
      GOSUB 9790
    END IF
  END IF
ELSEIF CMD$ = "E" OR CMD$ = "EDIT" THEN
  ! E n text (E n alone, with a mouse: edit it on the typing line)
  LET EI = -1
  IF NTOK >= 2 THEN LET EI = TONUM(TOK$(2))
  LET ET$ = ""
  FOR CI = 3 TO NTOK
    IF ET$ = "" THEN
      LET ET$ = TOK$(CI)
    ELSE
      LET ET$ = ET$ & " " & TOK$(CI)
    END IF
  NEXT CI
  IF NTOK = 2 AND HASMOUSE = 1 AND EI > 0 THEN
    LET SELID = EI
    LET KEEPSEL = 1
    GOSUB 8050
  ELSEIF NTOK < 3 THEN
    LET MSG$ = "E needs a card and its new text, e.g. E 4 Buy oat milk"
  ELSE
    GOSUB 9450
  END IF
ELSEIF CMD$ = "D" OR CMD$ = "DEL" OR CMD$ = "DELETE" THEN
  IF NTOK < 2 THEN
    LET MSG$ = "D needs a card's number, e.g. D 4"
  ELSE
    LET DELID = TONUM(TOK$(2))
    GOSUB 8900
  END IF
ELSEIF CMD$ = "N" OR CMD$ = "NEW" THEN
  IF NTOK < 2 THEN
    LET MSG$ = "N needs a name for the new board, e.g. N work"
  ELSE
    LET BNAME$ = TOK$(2)
    GOSUB 9130
    IF NAMEOK = 1 THEN
      GOSUB 9120
      IF EXISTS = 1 THEN
        LET MSG$ = "There's already a " & BNAME$ & ".board: L " & BNAME$ & " opens it"
      ELSE
        LET BOARD$ = BNAME$
        LET NT = 0
        LET NEXTID = 1
        LET SELID = 0
        LET NOSAVE = 0
        FOR CI = 1 TO 3
          LET TOP(CI) = 0
        NEXT CI
        LET MSG$ = "A new board, " & BOARD$ & ".board"
        GOSUB 9790
      END IF
    END IF
  END IF
ELSEIF CMD$ = "S" OR CMD$ = "SAVE" THEN
  LET NAMEOK = 1
  IF NTOK >= 2 THEN
    LET BNAME$ = TOK$(2)
    GOSUB 9130
    IF NAMEOK = 1 THEN LET BOARD$ = BNAME$
  END IF
  IF NAMEOK = 1 THEN
    GOSUB 9800
    IF SAVEOK = 1 THEN
      LET NOSAVE = 0
      LET MSG$ = "Saved " & FILE$ & " · " & LTRIM$(STR$(NT)) & " cards"
    ELSE
      LET MSG$ = "Couldn't save " & FILE$
    END IF
  END IF
ELSEIF CMD$ = "L" OR CMD$ = "LOAD" OR CMD$ = "OPEN" THEN
  IF NTOK < 2 THEN
    LET MSG$ = "L needs the board's name, e.g. L work (for work.board)"
  ELSE
    LET BNAME$ = TOK$(2)
    GOSUB 9130
    IF NAMEOK = 1 THEN
      LET OLDBOARD$ = BOARD$
      LET BOARD$ = BNAME$
      GOSUB 9900
      IF LOADOK = 1 THEN
        LET NOSAVE = 0
        LET MSG$ = "Opened " & FILE$ & " · " & LTRIM$(STR$(NT)) & " cards"
      ELSE
        IF LOADBAD = 1 THEN
          LET MSG$ = FILE$ & " isn't a board this can read"
        ELSE
          LET MSG$ = "There's no " & FILE$ & " (N " & BNAME$ & " starts one)"
        END IF
        LET BOARD$ = OLDBOARD$
        LET FILE$ = BOARD$ & ".board"
      END IF
    END IF
  END IF
ELSEIF CMD$ = "H" OR CMD$ = "HELP" OR CMD$ = "?" THEN
  LET HELP = 1
ELSE
  LET MSG$ = "There's no command " & TOK$(1) & ": H lists them"
END IF
RETURN

! ----------------------------------------------------------------------
! 9100  TOKENIZE — split RAW$ into TOK$(1..NTOK) on runs of spaces.
! ----------------------------------------------------------------------
9100 REM ---- tokenize ----
LET NTOK = 0
LET CUR$ = ""
LET TL$ = LTRIM$(RTRIM$(RAW$))
FOR TI = 1 TO LEN(TL$)
  LET CH$ = MID$(TL$, TI, 1)
  IF CH$ = " " THEN
    IF CUR$ <> "" AND NTOK < 64 THEN
      LET NTOK = NTOK + 1
      LET TOK$(NTOK) = CUR$
    END IF
    LET CUR$ = ""
  ELSE
    LET CUR$ = CUR$ & CH$
  END IF
NEXT TI
IF CUR$ <> "" AND NTOK < 64 THEN
  LET NTOK = NTOK + 1
  LET TOK$(NTOK) = CUR$
END IF
RETURN

! ----------------------------------------------------------------------
! 9120  EXISTS — is there a NAME$.board already?
! 9130  NAME — is NAME$ a board's name: letters, digits, - and _ (NAMEOK)?
! 9140  LANE — the lane LT$ names, by number or name (LV; 0 if none).
! ----------------------------------------------------------------------
9120 REM ---- is there a board called BNAME$? ----
LET EXISTS = 0
WHEN EXCEPTION IN
  OPEN #2: NAME BNAME$ & ".board", ACCESS INPUT
  LET EXISTS = 1
  CLOSE #2
USE
  CLOSE #2
END WHEN
RETURN

9130 REM ---- a board's name ----
IF LEN(BNAME$) > 6 THEN
  IF UCASE$(RIGHT$(BNAME$, 6)) = ".BOARD" THEN LET BNAME$ = LEFT$(BNAME$, LEN(BNAME$) - 6)
END IF
LET NAMEOK = 1
IF BNAME$ = "" OR LEN(BNAME$) > 40 THEN LET NAMEOK = 0
FOR NI = 1 TO LEN(BNAME$)
  LET NC$ = UCASE$(MID$(BNAME$, NI, 1))
  IF (NC$ < "A" OR NC$ > "Z") AND (NC$ < "0" OR NC$ > "9") AND NC$ <> "-" AND NC$ <> "_" THEN LET NAMEOK = 0
NEXT NI
IF NAMEOK = 0 THEN LET MSG$ = "A board's name is letters, digits, - and _ (like work or home-jobs)"
RETURN

9140 REM ---- which lane ----
LET LV = 0
LET LU$ = UCASE$(LT$)
IF LU$ = "1" OR LU$ = "TODO" OR LU$ = "TO-DO" OR LU$ = "TO" THEN LET LV = 1
IF LU$ = "2" OR LU$ = "DOING" OR LU$ = "IN-PROGRESS" OR LU$ = "PROGRESS" THEN LET LV = 2
IF LU$ = "3" OR LU$ = "DONE" THEN LET LV = 3
RETURN

! ======================================================================
! 9400  ADD a card AT$ to the end of lane AL.
! 9450  EDIT card number EI: its text becomes ET$.
! ======================================================================
9400 REM ---- add a card ----
IF NT >= MAXT THEN
  LET MSG$ = "The board is full: " & LTRIM$(STR$(MAXT)) & " cards"
  RETURN
END IF
LET NT = NT + 1
LET TASK$(NT) = AT$
LET LANE(NT) = AL
LET ID(NT) = NEXTID
LET NEXTID = NEXTID + 1
IF HASMOUSE = 1 THEN
  LET SELID = ID(NT)
  LET KEEPSEL = 1
END IF
LET MSG$ = "Added #" & LTRIM$(STR$(ID(NT))) & " to " & LANENAME$(AL)
GOSUB 9790
RETURN

9450 REM ---- edit a card ----
LET FID = EI
GOSUB 8950
IF FI = 0 THEN
  LET MSG$ = "There's no card #" & LTRIM$(STR$(EI))
ELSEIF ET$ = "" THEN
  LET MSG$ = "A card needs some text (D " & LTRIM$(STR$(EI)) & " deletes it)"
ELSEIF ET$ = TASK$(FI) THEN
  LET MSG$ = "Left #" & LTRIM$(STR$(EI)) & " as it was"
ELSE
  LET TASK$(FI) = ET$
  LET MSG$ = "Changed #" & LTRIM$(STR$(EI))
  GOSUB 9790
END IF
RETURN

! ----------------------------------------------------------------------
! 9700  HIT — what's under the pointer (MX, MY): HITKIND 1 for a card (its
!        number HITID), else 0; and the lane it's in (HITLN, 0 for none).
! ----------------------------------------------------------------------
9700 REM ---- what's under the pointer ----
LET HITKIND = 0
LET HITID = 0
LET HITLN = 0
FOR HI = 1 TO 3
  IF MX >= LX(HI) AND MX < LX(HI) + LW THEN LET HITLN = HI
NEXT HI
IF HITLN = 0 THEN RETURN
IF MX >= LX(HITLN) + CINSET AND MX < LX(HITLN) + CINSET + CW THEN
  FOR HI = 1 TO NT
    IF CTOPY(HI) >= 0 AND LANE(HI) = HITLN THEN
      IF MY >= CTOPY(HI) AND MY < CBOTY(HI) THEN
        LET HITKIND = 1
        LET HITID = ID(HI)
      END IF
    END IF
  NEXT HI
END IF
RETURN

! ======================================================================
! 9790  CHANGED — the board changed: save it (unless the file there
!        couldn't be read, so as not to lose it).
! 9800  SAVE — write the board to BOARD$.board (SAVEOK = 1 if it worked):
!        "ARCADE-KANBAN 2", the next card number, how many cards, then a
!        line per card, "<lane> <number> <text>".
! ======================================================================
9790 REM ---- changed ----
IF NOSAVE = 1 THEN
  LET MSG$ = MSG$ & " (not saved: S name saves it)"
  RETURN
END IF
GOSUB 9800
IF SAVEOK = 0 THEN LET MSG$ = MSG$ & " (couldn't save " & FILE$ & ")"
RETURN

9800 REM ---- save ----
LET FILE$ = BOARD$ & ".board"
LET SAVEOK = 0
WHEN EXCEPTION IN
  OPEN #1: NAME FILE$, ACCESS OUTPUT
  PRINT #1: "ARCADE-KANBAN 2"
  PRINT #1: LTRIM$(STR$(NEXTID))
  PRINT #1: LTRIM$(STR$(NT))
  FOR SVI = 1 TO NT
    PRINT #1: LTRIM$(STR$(LANE(SVI))) & " " & LTRIM$(STR$(ID(SVI))) & " " & TASK$(SVI)
  NEXT SVI
  CLOSE #1
  LET SAVEOK = 1
USE
  CLOSE #1                         ! safe no-op if it never opened
END WHEN
RETURN

! ======================================================================
! 9900  OPEN — replace the board with BOARD$.board. It reads into staging
!        arrays and only takes them if the whole file reads, so a bad or
!        foreign file leaves the board as it was. LOADOK = 1 if it worked;
!        LOADBAD = 1 if there's a file but it isn't a board. (Version 1 files,
!        "<lane> <text>" a line, get their cards numbered as they come.)
! ======================================================================
9900 REM ---- open a board ----
LET FILE$ = BOARD$ & ".board"
LET LOADOK = 0
LET LOADBAD = 0
LET LOPEN = 0
LET TN = 0
LET TNEXT = 1
LET LVER = 1
WHEN EXCEPTION IN
  OPEN #1: NAME FILE$, ACCESS INPUT
  LET LOPEN = 1
  LINE INPUT #1: LN$
  GOSUB 9940
  IF LEFT$(LN$, 14) = "ARCADE-KANBAN " THEN
    LET LVER = TONUM(MID$(LN$, 15))
    IF LVER >= 2 THEN
      LINE INPUT #1: LN$
      GOSUB 9940
      LET TNEXT = TONUM(LN$)
    END IF
    LINE INPUT #1: LN$
    GOSUB 9940
    LET TN = TONUM(LN$)
    IF LVER < 1 OR TN < 0 THEN
      LET LOADBAD = 1
    ELSE
      IF TN > MAXT THEN LET TN = MAXT
      FOR LI = 1 TO TN
        LINE INPUT #1: LN$
        GOSUB 9940
        GOSUB 9950                 ! "<lane> <number> <text>" → TLANE(LI), TID(LI), TTASK$(LI)
      NEXT LI
      LET LOADOK = 1
    END IF
  ELSE
    LET LOADBAD = 1
  END IF
  CLOSE #1
USE
  CLOSE #1
  IF LOPEN = 1 THEN LET LOADBAD = 1     ! it's there, but cut short
END WHEN
IF LOADOK = 1 THEN
  LET NT = TN
  LET MAXID = 0
  FOR LI = 1 TO NT
    LET LANE(LI) = TLANE(LI)
    LET TASK$(LI) = TTASK$(LI)
    LET ID(LI) = TID(LI)
    ! a number that's missing or taken: a new one, after the others
    FOR LJ = 1 TO LI - 1
      IF ID(LJ) = ID(LI) THEN LET ID(LI) = 0
    NEXT LJ
    IF ID(LI) > MAXID THEN LET MAXID = ID(LI)
  NEXT LI
  FOR LI = 1 TO NT
    IF ID(LI) < 1 THEN
      LET MAXID = MAXID + 1
      LET ID(LI) = MAXID
    END IF
  NEXT LI
  LET NEXTID = MAX(TNEXT, MAXID + 1)
  LET SELID = 0
  LET CARRY = 0
  FOR LI = 1 TO 3
    LET TOP(LI) = 0
  NEXT LI
END IF
RETURN

9940 REM ---- a line read from a file written with Windows line ends: drop its CR ----
IF RIGHT$(LN$, 1) = CHR$(13) THEN LET LN$ = LEFT$(LN$, LEN(LN$) - 1)
RETURN

! ----------------------------------------------------------------------
! 9950  One saved card, LN$: "<lane> <number> <text>" (version 1: "<lane>
!        <text>") into TLANE(LI), TID(LI), TTASK$(LI). A bad lane is To Do.
! ----------------------------------------------------------------------
9950 REM ---- one saved card ----
LET TID(LI) = 0
LET TLANE(LI) = 1
LET SP = POS(LN$, " ")
IF SP = 0 THEN
  LET TTASK$(LI) = LN$
ELSE
  LET TLANE(LI) = TONUM(LEFT$(LN$, SP - 1))
  LET RST$ = MID$(LN$, SP + 1)
  IF LVER >= 2 THEN
    LET SP = POS(RST$, " ")
    IF SP = 0 THEN
      LET TID(LI) = TONUM(RST$)
      LET RST$ = ""
    ELSE
      LET TID(LI) = TONUM(LEFT$(RST$, SP - 1))
      LET RST$ = MID$(RST$, SP + 1)
    END IF
  END IF
  LET TTASK$(LI) = RST$
END IF
IF TLANE(LI) < 1 OR TLANE(LI) > 3 THEN LET TLANE(LI) = 1
RETURN

! ----------------------------------------------------------------------
! 9990  TODAY — DAY$, as "Thu 29 Oct", from DATE$ (YYYYMMDD); the weekday by
!        Sakamoto's method (0 = Sunday).
! ----------------------------------------------------------------------
9990 REM ---- today ----
LET DS$ = DATE$
LET DYR = TONUM(LEFT$(DS$, 4))
LET DMO = TONUM(MID$(DS$, 5, 2))
LET DDY = TONUM(RIGHT$(DS$, 2))
LET DAY$ = ""
IF DYR > 0 AND DMO >= 1 AND DMO <= 12 AND DDY >= 1 THEN
  LET DYY = DYR
  IF DMO < 3 THEN LET DYY = DYR - 1
  LET DWK = (DYY + INT(DYY / 4) - INT(DYY / 100) + INT(DYY / 400) + TONUM(MID$("032503514624", DMO, 1)) + DDY) MOD 7
  LET DAY$ = MID$("SunMonTueWedThuFriSat", 3 * DWK + 1, 3) & " " & LTRIM$(STR$(DDY)) & " " & MID$("JanFebMarAprMayJunJulAugSepOctNovDec", 3 * DMO - 2, 3)
END IF
RETURN
