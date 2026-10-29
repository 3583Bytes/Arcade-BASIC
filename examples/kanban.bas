! kanban.bas — a Kanban board for your own tasks (To Do, Doing, Done), drawn with the  @category Graphics
! ECMA-116 §13 graphics module. It keeps itself in a file, kanban.board, saving after
! every change and opening it again next time — so it can track real work. (In the
! browser IDE the file stays in the browser: File → Program files.)
!
! With a mouse (in the browser IDE) click a card
! to select it (it opens up to show all its text), double-click it to edit it, and drag
! it to another lane or up and down; "+ add a card" adds one. The keyboard works too:
! the arrow keys select, Enter edits, Space picks the card up (then the arrows move it),
! Delete deletes. GET MOUSE and INKEY$ are an Arcade BASIC and a Microsoft BASIC
! extension (see docs/conformance.md).
!
! Without a mouse — a plain terminal, or piped input — type one command per line (LINE
! INPUT reads the whole line, so a card may have commas in it); H lists them. Headless,
! you can render a frame to SVG by piping commands and finishing with Q (it saves
! kanban.board in the current folder as it goes):
!   printf 'A Ship the demo\nM 5 doing\nQ\n' | \
!     dotnet run --project src/ArcadeBasic.Cli -- run examples/kanban.bas --svg board.svg
!
! Cards keep their number (#7) for good — the commands use it — and a lane keeps its
! cards in the order you put them. The screen is laid out in text rows (one window unit
! per row, from ASK DEVICE SIZE), so it looks the same in a terminal and in a window.

OPTION BASE 1

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
DIM CTOPY(200)          ! where each card was drawn: its top and bottom rows (-1: not drawn)
DIM CBOTY(200)
DIM TOK$(64)
DIM WRAP$(64)
LET NT = 0
LET NEXTID = 1
LET BOARD$ = "kanban"   ! the board's file is BOARD$ & ".board"

! ---- the lanes ----
DIM LNAME$(3)
DIM LACC(3)             ! each lane's colour
DIM LX(3)               ! its left edge
DIM TOP(3)              ! how many of its cards are scrolled out of sight above
DIM UPAT(3)             ! the rows of its "▲ n more" and "▼ n more" (-1: not shown)
DIM DNAT(3)
DIM LCNT(3)             ! how many cards it has
DIM VISN(3)             ! and how many showed
LET LNAME$(1) = "TO DO"
LET LNAME$(2) = "DOING"
LET LNAME$(3) = "DONE"

! ---- colours: SET COLOR MIX makes a calm palette from 16 up (0-15 stay as they are) ----
LET CPANEL = 16         ! a lane
LET CPANEL2 = 17        ! the lane a card is being dragged over
LET CCARD = 18
LET CHOVER = 19         ! the card under the pointer
LET CSEL = 20           ! the selected card
LET CTEXT = 21
LET CDIM = 22
LET CBAR = 23           ! the title bar
LET CWARN = 24
LET LACC(1) = 25
LET LACC(2) = 26
LET LACC(3) = 27
SET COLOR MIX (CPANEL) 0.09, 0.10, 0.13
SET COLOR MIX (CPANEL2) 0.13, 0.15, 0.20
SET COLOR MIX (CCARD) 0.17, 0.18, 0.23
SET COLOR MIX (CHOVER) 0.23, 0.25, 0.32
SET COLOR MIX (CSEL) 0.20, 0.29, 0.48
SET COLOR MIX (CTEXT) 0.90, 0.92, 0.95
SET COLOR MIX (CDIM) 0.50, 0.54, 0.61
SET COLOR MIX (CBAR) 0.05, 0.06, 0.08
SET COLOR MIX (CWARN) 1.00, 0.45, 0.42
SET COLOR MIX (LACC(1)) 0.40, 0.62, 1.00      ! blue
SET COLOR MIX (LACC(2)) 1.00, 0.76, 0.30      ! amber
SET COLOR MIX (LACC(3)) 0.40, 0.82, 0.50      ! green

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
LET HELP = 0            ! 1 while the help screen shows
LET QUIT = 0
LET DIRTY = 1           ! the screen needs drawing again
LET FRAME = 0
LET SELID = 0           ! the selected card's number, or 0
LET KEEPSEL = 0         ! scroll so the selected card shows, next time it's drawn
LET CARRY = 0           ! 1 while the selected card is picked up (Space)
LET CONFIRMID = 0       ! the card Delete was pressed on once
LET HOVERID = 0         ! the card under the pointer
LET HOVERK = 0          ! what else is under it (a HITKIND) and in which lane
LET HOVERL = 0
LET PRESSID = 0         ! the card pressed on, and where
LET PRESSX = 0
LET PRESSY = 0
LET DRAGGING = 0        ! 1 once it's being dragged
LET GRABDX = 0
LET GRABDY = 0
LET DROPLN = 0          ! where it would land: the lane, the card it would go before (0: the end)
LET DROPBEFORE = 0
LET MARKY = 0
LET LASTID = 0          ! the last card clicked, and when (for a double-click)
LET LASTF = -100
LET MODE = 0            ! typing: 1 a command, 2 a card's new text, 3 a new card
LET TYPED$ = ""
LET CURP = 0            ! the cursor, after this many characters
LET EDITID = 0
LET ADDLN = 1
LET NOSAVE = 0          ! 1 when the file there couldn't be read: don't save over it
LET SAVEOK = 0
LET LOADOK = 0
LET LOADBAD = 0
LET BADD0 = 0           ! the buttons: from x to x
LET BADD1 = 0
LET BHELP0 = 0
LET BHELP1 = 0
LET RAW$ = ""
LET NTOK = 0
LET HITKIND = 0         ! 9700: 1 a card, 2 "▲ more", 3 "▼ more", 4 "+ add a card", 5 + Add, 6 Help
LET HITID = 0
LET HITLN = 0
LET FILE$ = BOARD$ & ".board"
LET DAY$ = ""
LET FI = 0              ! 8950's answer: a card's index
LET LV = 0              ! 9140's: a lane
LET NAMEOK = 0          ! 9130's and 9120's: a board's name is fine, and there's one by that name
LET EXISTS = 0
LET WRAPN = 0           ! 9200's: how many lines
! the layout, measured as the board is drawn (9000)
LET DEVW = 0
LET DEVH = 0
LET COLW = 25
LET HEADY = 22
LET CTOP = 20

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
DATA "Add one with + add a card, or type A Buy milk", 1
DATA "Select a card and press Delete (twice) to remove it", 1
DATA "Arrow keys select; Space picks a card up, arrows move it", 2
DATA "The board saves itself as you go", 3
DATA "Press H for help", 3
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
    IF DIRTY = 1 THEN              ! (so the mouse finds the cards where they are now)
      GOSUB 9010
      LET DIRTY = 0
    END IF
    ! the mouse: 2 = pressed, 1 = held, 3 = released, 0 = up
    GET MOUSE MX, MY, MS
    GOSUB 8500
    ! the screen changed size (a resized window): lay the board out again
    ASK DEVICE SIZE NEWW, NEWH, DEVU$
    IF NEWW <> DEVW OR NEWH <> DEVH THEN LET DIRTY = 1
    IF DIRTY = 1 THEN
      GOSUB 9010
      LET DIRTY = 0
    END IF
    LET FRAME = FRAME + 1
    SLEEP 0.03                     ! ~30 frames/second (also presents the frame)
  LOOP
END IF
END

! ======================================================================
! 8000  A KEY (K$), with a mouse: typing, or the selected card.
! ======================================================================
8000 REM ---- a key ----
IF HELP = 1 THEN
  LET HELP = 0                     ! any key leaves the help screen
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
  GOSUB 8100                       ! the line being typed
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
  LET TYPED$ = K$
  LET CURP = 1
  LET CARRY = 0
END IF
RETURN

! ----------------------------------------------------------------------
! 8050  EDIT the selected card: its text goes on the typing line.
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
LET TYPED$ = TASK$(FI)
LET CURP = LEN(TYPED$)
LET CARRY = 0
RETURN

! ----------------------------------------------------------------------
! 8100  TYPING — K$ edits TYPED$ at the cursor; Enter finishes, Esc cancels.
! ----------------------------------------------------------------------
8100 REM ---- typing ----
IF K$ = KENTER$ OR K$ = CHR$(10) THEN
  GOSUB 8150
ELSEIF K$ = KESC$ THEN
  IF MODE = 2 THEN
    LET MSG$ = "Left #" & LTRIM$(STR$(EDITID)) & " as it was"
  ELSE
    LET MSG$ = "Cancelled"
  END IF
  LET MODE = 0
  LET TYPED$ = ""
ELSEIF K$ = KBS$ OR K$ = KBS2$ THEN
  IF CURP > 0 THEN
    LET TYPED$ = LEFT$(TYPED$, CURP - 1) & MID$(TYPED$, CURP + 1)
    LET CURP = CURP - 1
  END IF
  IF MODE = 1 AND TYPED$ = "" THEN LET MODE = 0   ! a command rubbed out
ELSEIF K$ = KDEL$ THEN
  IF CURP < LEN(TYPED$) THEN LET TYPED$ = LEFT$(TYPED$, CURP) & MID$(TYPED$, CURP + 2)
ELSEIF K$ = KLT$ THEN
  IF CURP > 0 THEN LET CURP = CURP - 1
ELSEIF K$ = KRT$ THEN
  IF CURP < LEN(TYPED$) THEN LET CURP = CURP + 1
ELSEIF K$ = KHOME$ THEN
  LET CURP = 0
ELSEIF K$ = KEND$ THEN
  LET CURP = LEN(TYPED$)
ELSEIF LEN(K$) = 1 AND K$ >= " " AND K$ <> KBS2$ AND LEN(TYPED$) < 300 THEN
  LET TYPED$ = LEFT$(TYPED$, CURP) & K$ & MID$(TYPED$, CURP + 1)
  LET CURP = CURP + 1
END IF
RETURN

! ----------------------------------------------------------------------
! 8150  FINISH TYPING — run the command, change the card, or add the new one.
! ----------------------------------------------------------------------
8150 REM ---- finish typing ----
LET TXT$ = LTRIM$(RTRIM$(TYPED$))
LET WASMODE = MODE
LET MODE = 0
LET TYPED$ = ""
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
! 8500  THE MOUSE (MX, MY, MS): select, drag and drop, the buttons, scrolling.
! ======================================================================
8500 REM ---- the mouse ----
IF HELP = 1 THEN
  IF MS = 2 THEN
    LET HELP = 0                   ! a click leaves the help screen
    LET DIRTY = 1
  END IF
  RETURN
END IF
IF MS = 2 THEN
  GOSUB 9700                       ! what's under the pointer
  IF CONFIRMID > 0 THEN
    LET CONFIRMID = 0
    LET MSG$ = "Kept it"
  END IF
  IF MODE >= 2 THEN GOSUB 8150     ! clicking away keeps what was typed
  IF HITKIND = 1 THEN
    IF HITID = LASTID AND FRAME - LASTF <= 12 THEN
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
      LET LASTF = FRAME
    END IF
  ELSEIF HITKIND = 2 THEN
    LET TOP(HITLN) = MAX(0, TOP(HITLN) - MAX(1, VISN(HITLN) - 1))
  ELSEIF HITKIND = 3 THEN
    LET TOP(HITLN) = TOP(HITLN) + MAX(1, VISN(HITLN) - 1)
  ELSEIF HITKIND = 4 OR HITKIND = 5 THEN
    LET ADDLN = HITLN              ! "+ add a card" in a lane, or + Add
    IF HITKIND = 5 THEN
      LET ADDLN = 1
      LET FID = SELID
      GOSUB 8950
      IF FI > 0 THEN LET ADDLN = LANE(FI)
    END IF
    LET MODE = 3
    LET TYPED$ = ""
    LET CURP = 0
    LET CARRY = 0
  ELSEIF HITKIND = 6 THEN
    LET HELP = 1
  ELSE
    LET SELID = 0                  ! a click on nothing
    LET CARRY = 0
  END IF
  LET DIRTY = 1
ELSEIF MS = 1 THEN
  IF PRESSID > 0 THEN
    IF DRAGGING = 0 AND (ABS(MX - PRESSX) >= 1 OR ABS(MY - PRESSY) >= 0.6) THEN
      LET FID = PRESSID
      GOSUB 8950
      IF FI > 0 AND CTOPY(FI) >= 0 THEN
        LET DRAGGING = 1
        LET GRABDX = PRESSX - LX(LANE(FI))
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
ELSEIF MS = 0 THEN
  GOSUB 9700                       ! hover: the card, or the button, under the pointer
  IF HITKIND <> 1 THEN LET HITID = 0
  IF HITID <> HOVERID OR HITKIND <> HOVERK OR HITLN <> HOVERL THEN
    LET HOVERID = HITID
    LET HOVERK = HITKIND
    LET HOVERL = HITLN
    LET DIRTY = 1
  END IF
END IF
RETURN

! ----------------------------------------------------------------------
! 8700  DROP — where the card being dragged would land, from the pointer:
!        the lane (DROPLN, 0 for none), the card it would go before (DROPBEFORE,
!        0 for the end of the lane), and the row edge to mark it at (MARKY).
! ----------------------------------------------------------------------
8700 REM ---- where it would land ----
LET DROPLN = 0
LET DROPBEFORE = 0
FOR DI = 1 TO 3
  IF MX >= LX(DI) - 1 AND MX <= LX(DI) + COLW + 1 AND MY >= 1 AND MY <= HEADY + 1 THEN LET DROPLN = DI
NEXT DI
IF DROPLN = 0 THEN RETURN
LET MARKY = -1
LET LASTVIS = 0
FOR DI = 1 TO NT
  IF LANE(DI) = DROPLN AND ID(DI) <> PRESSID AND DROPBEFORE = 0 THEN
    IF CTOPY(DI) >= 0 THEN
      IF MY > (CTOPY(DI) + CBOTY(DI) + 1) / 2 THEN
        LET DROPBEFORE = DI        ! above this card's middle: before it
        LET MARKY = CTOPY(DI) + 1
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
    LET MARKY = CBOTY(LASTVIS)
  ELSE
    LET MARKY = CTOP + 1
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
! 9010  DRAW — the help screen or the board, scrolled (KEEPSEL) so the
!        selected card shows.
! ======================================================================
9010 REM ---- draw ----
IF HELP = 1 THEN
  GOSUB 9050
  RETURN
END IF
IF KEEPSEL = 1 AND SELID > 0 THEN
  LET KEEPSEL = 0
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
    RETURN
  END IF
END IF
LET KEEPSEL = 0
GOSUB 9000
RETURN

! ======================================================================
! 9060  COLUMNS — the window width for the device measured in DEVW, DEVH. On
!       a character screen (2 x 4 dots a cell, up to 40 rows) it's the screen's
!       columns, so x = k is column k: an 80-unit window on 78 columns would put
!       x = 0 and x = 1 in one cell, and on 116 space the columns unevenly. On a
!       finer device (SVG, pixels) it's 80, scaled to fit.
! ======================================================================
9060 REM ---- columns ----
LET COLS = 80
IF DEVW >= 2 AND DEVH <= 160 THEN LET COLS = INT(DEVW / 2)
IF COLS < 60 THEN LET COLS = 60    ! floor: any narrower and the board is clipped
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

! ======================================================================
! 9000  RENDER — a title bar; three lanes of one-line cards (the selected one
!       opened up to show all its text), each scrolled by TOP(); then a row of
!       buttons and hints, and the status (or what's being typed).
!       One text row per window unit, row 0 at the bottom:
!         ROWS-1 title   ROWS-2 lane names   ROWS-3 their rule   ROWS-4 .. CBOT cards
!         2 "+ add a card"   1 buttons and hints   0 status
! ======================================================================
9000 REM ---- render ----
CLEAR
ASK DEVICE SIZE DEVW, DEVH, DEVU$  ! re-measured every frame: the board follows a resize
LET ROWS = 24                      ! when the device's size is unknown
IF DEVH >= 4 THEN LET ROWS = INT(DEVH / 4)
IF ROWS > 40 THEN LET ROWS = 40    ! a huge surface still gets readable rows
IF ROWS < 14 THEN LET ROWS = 14
GOSUB 9060                         ! COLS: one window unit per column
SET WINDOW 0, COLS, 0, ROWS
LET COLW = INT((COLS - 4) / 3)     ! three lanes, two columns apart
FOR LN = 1 TO 3
  LET LX(LN) = (LN - 1) * (COLW + 2)
NEXT LN
LET TITLEY = ROWS - 1
LET HEADY = ROWS - 2
LET CTOP = ROWS - 4
LET CBOT = 3
IF HASMOUSE = 0 THEN LET CBOT = 2  ! (no "+ add a card" row)
LET ADDY = 2
LET HINTY = 1
LET STATY = 0
FOR I = 1 TO NT
  LET CTOPY(I) = -1
NEXT I

! --- the title bar: the board, and today ---------------------------------
LET NDONE = 0
FOR I = 1 TO NT
  IF LANE(I) = 3 THEN LET NDONE = NDONE + 1
NEXT I
SET AREA COLOR CBAR
GRAPH AREA: 0, TITLEY; COLS, TITLEY; COLS, ROWS; 0, ROWS
SET TEXT COLOR CTEXT
GRAPH TEXT, AT 1, TITLEY + 0.25: "KANBAN"
SET TEXT COLOR CDIM
GRAPH TEXT, AT 9, TITLEY + 0.25: FIT$(BOARD$ & ".board", COLS - 46)
LET R$ = DAY$ & "  ·  " & LTRIM$(STR$(NDONE)) & " of " & LTRIM$(STR$(NT)) & " done"
GRAPH TEXT, AT COLS - 1 - LEN(R$), TITLEY + 0.25: R$

! --- the lanes ------------------------------------------------------------
FOR LN = 1 TO 3
  LET X0 = LX(LN)
  LET X1 = X0 + COLW
  LET LCNT(LN) = 0
  FOR I = 1 TO NT
    IF LANE(I) = LN THEN LET LCNT(LN) = LCNT(LN) + 1
  NEXT I
  IF DRAGGING = 1 AND DROPLN = LN THEN
    SET AREA COLOR CPANEL2
  ELSE
    SET AREA COLOR CPANEL
  END IF
  ! (from just under its rule, so on a character screen the rule stays a thin line;
  ! edges just inside whole columns, so a character screen fills whole cells)
  GRAPH AREA: X0, 1.95; X1 - 0.02, 1.95; X1 - 0.02, HEADY - 1; X0, HEADY - 1
  ! its name and how many cards it has, over a thin rule in its colour
  SET TEXT COLOR LACC(LN)
  GRAPH TEXT, AT X0 + 1, HEADY + 0.25: LNAME$(LN)
  SET TEXT COLOR CDIM
  GRAPH TEXT, AT X0 + 2 + LEN(LNAME$(LN)), HEADY + 0.25: LTRIM$(STR$(LCNT(LN)))
  SET AREA COLOR LACC(LN)
  GRAPH AREA: X0 + 1, HEADY - 0.32; X1 - 1, HEADY - 0.32; X1 - 1, HEADY - 0.17; X0 + 1, HEADY - 0.17
  ! its cards, from TOP(LN) on, while they fit
  LET SKIPN = TOP(LN)
  IF SKIPN > LCNT(LN) - 1 THEN LET SKIPN = MAX(0, LCNT(LN) - 1)
  LET TOP(LN) = SKIPN
  LET UPAT(LN) = -1
  LET DNAT(LN) = -1
  LET VISN(LN) = 0
  LET ROW = CTOP
  IF SKIPN > 0 THEN
    SET TEXT COLOR CDIM
    IF HOVERK = 2 AND HOVERL = LN THEN SET TEXT COLOR CTEXT
    GRAPH TEXT, AT X0 + 2, ROW + 0.25: "▲ " & LTRIM$(STR$(SKIPN)) & " more"
    LET UPAT(LN) = ROW
    LET ROW = ROW - 1
  END IF
  LET K = 0
  LET FULL = 0
  LET HID = 0
  FOR I = 1 TO NT
    IF LANE(I) = LN THEN
      LET K = K + 1
      IF K > SKIPN AND FULL = 0 THEN
        ! its lines: one, or — selected — all of its text; and its number, when it's
        ! needed (typing commands) or pointed at, else the room goes to the text
        LET CIDS$ = ""
        IF HASMOUSE = 0 OR ID(I) = SELID OR ID(I) = HOVERID THEN LET CIDS$ = LTRIM$(STR$(ID(I)))
        LET TW = COLW - 3
        IF CIDS$ <> "" THEN LET TW = COLW - 4 - LEN(CIDS$)
        IF ID(I) = SELID AND DRAGGING = 0 THEN
          LET WRAPSRC$ = TASK$(I)
          LET WRAPW = TW
          GOSUB 9200
          LET MAXH = CTOP - CBOT - 1
          IF WRAPN > MAXH THEN
            LET WRAPN = MAXH
            LET WRAP$(MAXH) = FIT$(WRAP$(MAXH) & "…", TW)
          END IF
          LET H = WRAPN
        ELSE
          LET WRAP$(1) = FIT$(TASK$(I), TW)
          LET H = 1
        END IF
        LET NEED = H
        IF K < LCNT(LN) THEN LET NEED = H + 1   ! (room for "▼ n more" after it)
        IF NEED > ROW - CBOT + 1 THEN
          LET FULL = 1
          LET HID = LCNT(LN) - K + 1
        ELSE
          ! draw it
          LET CXL = X0
          LET CROW = ROW
          LET CH = H
          LET CSTRIPE = LACC(LN)
          LET CTCOL = CTEXT
          IF LN = 3 THEN LET CTCOL = CDIM    ! done: quieter
          LET CFILL = CCARD
          IF ID(I) = HOVERID THEN LET CFILL = CHOVER
          IF ID(I) = SELID THEN
            LET CFILL = CSEL
            LET CTCOL = CTEXT
            IF CARRY = 1 THEN LET CSTRIPE = CTEXT
          END IF
          IF ID(I) = PRESSID AND DRAGGING = 1 THEN   ! the card being dragged, left behind
            LET CFILL = CPANEL2
            LET CTCOL = CDIM
          END IF
          GOSUB 9300
          LET CTOPY(I) = ROW
          LET CBOTY(I) = ROW - H + 1
          LET VISN(LN) = VISN(LN) + 1
          LET ROW = ROW - H
        END IF
      END IF
    END IF
  NEXT I
  IF HID > 0 THEN
    SET TEXT COLOR CDIM
    IF HOVERK = 3 AND HOVERL = LN THEN SET TEXT COLOR CTEXT
    GRAPH TEXT, AT X0 + 2, CBOT + 0.25: "▼ " & LTRIM$(STR$(HID)) & " more"
    LET DNAT(LN) = CBOT
  END IF
  IF HASMOUSE = 1 THEN
    SET TEXT COLOR CDIM
    IF HOVERK = 4 AND HOVERL = LN THEN SET TEXT COLOR CTEXT
    IF MODE = 3 AND ADDLN = LN THEN SET TEXT COLOR LACC(LN)
    GRAPH TEXT, AT X0 + 2, ADDY + 0.25: "+ add a card"
  END IF
NEXT LN

! --- dragging: mark where it would land, and draw it at the pointer --------
IF DRAGGING = 1 THEN
  IF DROPLN > 0 THEN
    SET AREA COLOR LACC(DROPLN)
    GRAPH AREA: LX(DROPLN) + 1, MARKY - 0.07; LX(DROPLN) + COLW - 1, MARKY - 0.07; LX(DROPLN) + COLW - 1, MARKY + 0.07; LX(DROPLN) + 1, MARKY + 0.07
    SET TEXT COLOR LACC(DROPLN)
    GRAPH TEXT, AT LX(DROPLN), MARKY - 0.35: "▶"
  END IF
  LET FID = PRESSID
  GOSUB 8950
  IF FI > 0 THEN
    LET CIDS$ = LTRIM$(STR$(ID(FI)))
    LET WRAP$(1) = FIT$(TASK$(FI), COLW - 4 - LEN(CIDS$))
    LET CXL = MX - GRABDX
    LET CROW = MY - GRABDY
    LET CH = 1
    LET CFILL = CSEL
    LET CSTRIPE = LACC(LANE(FI))
    LET CTCOL = CTEXT
    GOSUB 9300
  END IF
END IF

! --- the buttons and hints, then the status (or what's being typed) --------
IF MODE = 1 THEN
  LET HINT$ = "Enter runs the command  ·  Esc cancels  ·  H Enter: help"
ELSEIF MODE = 2 THEN
  LET HINT$ = "Enter saves  ·  Esc cancels  ·  ← → Home End move the cursor"
ELSEIF MODE = 3 THEN
  LET HINT$ = "Type the card, then Enter  ·  Esc cancels"
ELSEIF CONFIRMID > 0 THEN
  LET HINT$ = "Delete again deletes it  ·  any other key keeps it"
ELSEIF CARRY = 1 THEN
  LET HINT$ = "↑ ↓ ← → move the card  ·  Space or Enter puts it down"
ELSEIF SELID > 0 THEN
  LET HINT$ = "Enter edit  ·  Space pick up  ·  Del delete  ·  ↑ ↓ ← → select"
ELSE
  LET HINT$ = "Click a card, or ↑ ↓ ← →  ·  or type a command (H: help)"
END IF
IF HASMOUSE = 1 THEN
  LET BADD0 = 0.5
  LET BADD1 = 6.5
  LET BHELP0 = 7.5
  LET BHELP1 = 12.5
  SET AREA COLOR CCARD
  IF HOVERK = 5 THEN SET AREA COLOR CHOVER
  GRAPH AREA: BADD0, HINTY + 0.08; BADD1, HINTY + 0.08; BADD1, HINTY + 0.92; BADD0, HINTY + 0.92
  SET AREA COLOR CCARD
  IF HOVERK = 6 THEN SET AREA COLOR CHOVER
  GRAPH AREA: BHELP0, HINTY + 0.08; BHELP1, HINTY + 0.08; BHELP1, HINTY + 0.92; BHELP0, HINTY + 0.92
  SET TEXT COLOR CTEXT
  GRAPH TEXT, AT 1, HINTY + 0.25: "+ Add"
  GRAPH TEXT, AT 8, HINTY + 0.25: "Help"
  SET TEXT COLOR CDIM
  GRAPH TEXT, AT 14, HINTY + 0.25: FIT$(HINT$, COLS - 15)
ELSE
  SET TEXT COLOR CDIM
  GRAPH TEXT, AT 1, HINTY + 0.25: FIT$("A add  M move  E edit  D delete  N new  S save  L open  H help  Q quit", COLS - 2)
END IF
IF MODE > 0 THEN
  IF MODE = 1 THEN
    LET P$ = "> "
    SET TEXT COLOR CTEXT
  ELSEIF MODE = 2 THEN
    LET P$ = "Edit #" & LTRIM$(STR$(EDITID)) & ": "
    SET TEXT COLOR LACC(2)
  ELSE
    LET P$ = "Add to " & LANENAME$(ADDLN) & ": "
    SET TEXT COLOR LACC(ADDLN)
  END IF
  GRAPH TEXT, AT 1, STATY + 0.25: P$
  ! the text, with the cursor; scrolled so the cursor shows
  LET AVAIL = COLS - 2 - LEN(P$)
  LET LT$ = LEFT$(TYPED$, CURP)
  LET RT$ = MID$(TYPED$, CURP + 1)
  LET CUR$ = "|"
  IF RT$ = "" THEN LET CUR$ = "_"
  IF LEN(LT$) > AVAIL - 2 AND AVAIL > 3 THEN LET LT$ = "…" & RIGHT$(LT$, AVAIL - 3)
  SET TEXT COLOR CTEXT
  GRAPH TEXT, AT 1 + LEN(P$), STATY + 0.25: FIT$(LT$ & CUR$ & RT$, AVAIL)
ELSE
  SET TEXT COLOR CDIM
  IF CONFIRMID > 0 THEN SET TEXT COLOR CWARN
  GRAPH TEXT, AT 1, STATY + 0.25: FIT$(MSG$, COLS - 2)
END IF
RETURN

! ----------------------------------------------------------------------
! 9300  CARD — a filled box from column CXL + 1, CH rows down from row CROW,
!        a stripe in CSTRIPE down its left side, its lines WRAP$(1 .. CH) in
!        CTCOL, and its number CIDS$ (if any) at the right of the first one, dim.
! ----------------------------------------------------------------------
9300 REM ---- one card ----
LET BX0 = CXL + 1
LET BX1 = CXL + COLW - 1.02
LET BYT = CROW + 0.92
LET BYB = CROW - CH + 1.08
SET AREA COLOR CFILL
GRAPH AREA: BX0, BYB; BX1, BYB; BX1, BYT; BX0, BYT
SET AREA COLOR CSTRIPE
GRAPH AREA: BX0, BYB; BX0 + 0.35, BYB; BX0 + 0.35, BYT; BX0, BYT
SET TEXT COLOR CTCOL
FOR WJ = 1 TO CH
  GRAPH TEXT, AT BX0 + 1, CROW - WJ + 1.25: WRAP$(WJ)
NEXT WJ
IF CIDS$ <> "" THEN
  SET TEXT COLOR CDIM
  GRAPH TEXT, AT BX1 - LEN(CIDS$), CROW + 0.25: CIDS$
END IF
RETURN

! ======================================================================
! 9050  HELP — what the mouse, the keys and the commands do.
! ======================================================================
9050 REM ---- help screen ----
CLEAR
ASK DEVICE SIZE DEVW, DEVH, DEVU$
LET HROWS = 24
IF DEVH >= 4 THEN LET HROWS = INT(DEVH / 4)
IF HROWS > 40 THEN LET HROWS = 40
IF HROWS < 14 THEN LET HROWS = 14
GOSUB 9060
SET WINDOW 0, COLS, 0, HROWS
SET AREA COLOR CPANEL
GRAPH AREA: 0, 0; COLS, 0; COLS, HROWS; 0, HROWS
LET Y = HROWS - 2
SET TEXT COLOR CTEXT
GRAPH TEXT, AT 2, Y + 0.25: "KANBAN  ·  HELP"
LET Y = Y - 2
IF HASMOUSE = 1 AND HROWS >= 23 THEN
  SET TEXT COLOR LACC(1)
  GRAPH TEXT, AT 2, Y + 0.25: "MOUSE"
  GRAPH TEXT, AT 42, Y + 0.25: "KEYBOARD"
  SET TEXT COLOR CTEXT
  GRAPH TEXT, AT 2, Y - 0.75: "Click a card      select it"
  GRAPH TEXT, AT 2, Y - 1.75: "Double-click      edit it"
  GRAPH TEXT, AT 2, Y - 2.75: "Drag a card       move or reorder it"
  GRAPH TEXT, AT 2, Y - 3.75: "+ add a card      add one to a lane"
  GRAPH TEXT, AT 2, Y - 4.75: "▲ / ▼ more        scroll a lane"
  GRAPH TEXT, AT 42, Y - 0.75: "↑ ↓ ← →   select a card"
  GRAPH TEXT, AT 42, Y - 1.75: "Enter     edit it"
  GRAPH TEXT, AT 42, Y - 2.75: "Space     pick it up: ↑ ↓ ← → move it"
  GRAPH TEXT, AT 42, Y - 3.75: "Delete    delete it (press it twice)"
  GRAPH TEXT, AT 42, Y - 4.75: "Esc       cancel, or let go"
  LET Y = Y - 7
END IF
SET TEXT COLOR LACC(2)
IF HASMOUSE = 1 THEN
  GRAPH TEXT, AT 2, Y + 0.25: "COMMANDS  (type one on the board; Enter runs it)"
ELSE
  GRAPH TEXT, AT 2, Y + 0.25: "COMMANDS"
END IF
SET TEXT COLOR CTEXT
GRAPH TEXT, AT 2, Y - 0.75: "A text             add a card to To Do"
GRAPH TEXT, AT 2, Y - 1.75: "M n lane [place]   move card n to a lane: 1-3, todo, doing or done"
GRAPH TEXT, AT 2, Y - 2.75: "                   (place 1: the top of the lane)"
GRAPH TEXT, AT 2, Y - 3.75: "E n text           change card n's text"
GRAPH TEXT, AT 2, Y - 4.75: "D n                delete card n"
GRAPH TEXT, AT 2, Y - 5.75: "N name  /  L name  start a new board  /  open another (name.board)"
GRAPH TEXT, AT 2, Y - 6.75: "S [name]           save now  /  save as name.board, and carry on there"
GRAPH TEXT, AT 2, Y - 7.75: "H  /  Q            this help  /  quit"
SET TEXT COLOR CDIM
GRAPH TEXT, AT 2, Y - 9.75: FIT$("The board saves itself to " & BOARD$ & ".board after every change.", COLS - 4)
IF HASMOUSE = 1 THEN
  GRAPH TEXT, AT 2, Y - 10.75: "Press any key, or click, to go back."
ELSE
  GRAPH TEXT, AT 2, Y - 10.75: "Press Enter to go back."
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
IF WLINE$ <> "" AND WRAPN < 64 THEN
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
DO WHILE LEN(WWORD$) > WRAPW AND WRAPN < 62
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
ELSEIF WRAPN < 63 THEN
  LET WRAPN = WRAPN + 1
  LET WRAP$(WRAPN) = WLINE$
  LET WLINE$ = WWORD$
END IF
RETURN

! ----------------------------------------------------------------------
! 9700  HIT — what's under the pointer (MX, MY): HITKIND (0 nothing, 1 a card,
!        2 "▲ more", 3 "▼ more", 4 "+ add a card", 5 the + Add button, 6 Help),
!        the card's number HITID and the lane HITLN.
! ----------------------------------------------------------------------
9700 REM ---- what's under the pointer ----
LET HITKIND = 0
LET HITID = 0
LET HITLN = 0
FOR HI = 1 TO 3
  IF MX >= LX(HI) AND MX <= LX(HI) + COLW THEN LET HITLN = HI
NEXT HI
LET HROW = INT(MY)
IF HROW = HINTY THEN
  IF MX >= BADD0 AND MX <= BADD1 THEN LET HITKIND = 5
  IF MX >= BHELP0 AND MX <= BHELP1 THEN LET HITKIND = 6
  RETURN
END IF
IF HITLN = 0 THEN RETURN
IF HROW = ADDY AND HASMOUSE = 1 THEN
  LET HITKIND = 4
ELSEIF HROW = UPAT(HITLN) THEN
  LET HITKIND = 2
ELSEIF HROW = DNAT(HITLN) THEN
  LET HITKIND = 3
ELSEIF MX >= LX(HITLN) + 1 AND MX <= LX(HITLN) + COLW - 1 THEN
  FOR HI = 1 TO NT
    IF CTOPY(HI) >= 0 AND LANE(HI) = HITLN THEN
      IF MY >= CBOTY(HI) AND MY < CTOPY(HI) + 1 THEN
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
