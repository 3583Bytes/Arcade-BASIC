! editor.bas — a text editor for your own files: several open at once, each in a tab,   @category Graphics
! used with the mouse and the keyboard. Click to put the cursor anywhere, drag (or
! Shift+click) to select, double-click a word. The bar at the top has New, Open, Save,
! Save as and Close; each file gets a tab (click to show it, × to close it, + for a new
! one). The files it had open come back next time: it keeps their names in
! editor.files. (In the browser IDE files stay in the browser: File → Program files
! uploads one for the editor to open, or downloads what you've written.)
!
! Tabs: the Tab key puts in a tab character — drawn out to the next stop of 4, with a
! faint → — or spaces (the status bar's Tabs button switches). With several lines
! selected Tab indents them all and Shift+Tab undents; Enter keeps the line's indent.
!
! Keys: F1 help, F2 save, F3 open, F4 save as, F7 new file, F8 close, F9 next tab
! (Shift+F9 the one before); the arrows, Home, End, PgUp and PgDn move, and with Shift
! they select. The IDE keeps Ctrl and ⌘ shortcuts (and F5, F6, F10, F11) for itself —
! a program never sees them — hence the F keys. A terminal has no mouse here and
! doesn't pass F keys on, so there Ctrl+N, O, S, E (save as), W, T (next tab), G (help)
! and Q (quit) do the same, where the terminal lets them through.
!
! The screen is a grid of character cells, one window unit each (from ASK DEVICE SIZE,
! as in kanban.bas), and text is drawn in a monospace font, so the columns of the text
! line up with the cursor, the selection and the tab stops. INKEY$, KEYDOWN and
! GET MOUSE are Microsoft BASIC and Arcade BASIC extensions (see docs/conformance.md).

OPTION BASE 1

! ---- settings ----
LET TABW = 4            ! a tab stop every 4 columns
LET TABSP = 0           ! 1: the Tab key puts in spaces instead of a tab character
LET MAXTAB = 8          ! files open at once
LET MAXL = 3000         ! lines in a file
LET MAXREC = 20         ! files Open remembers
LET SESSION$ = "editor.files"

! ---- the files: tab T's lines are TX$(T, 1) to TX$(T, its line count) ----
DIM TX$(9, 3000)        ! (row 9: where a file is read before it gets a tab)
LET STG = 9
DIM TNAME$(8)           ! its file ("" until it's saved)
DIM TUNT(8)             ! until then it's "untitled-n"
DIM TCHG(8)             ! 1 when it has changes that aren't saved
DIM TNL(8)              ! the tabs not showing keep their line count, cursor, scroll
DIM TCY(8)              ! and selection here (the one showing keeps them in NL, CY,
DIM TCX(8)              ! CX, TOPL, LEFTC, WANTC, SELON, AY and AX)
DIM TTOP(8)
DIM TLEFT(8)
DIM TWANT(8)
DIM TSEL(8)
DIM TAY(8)
DIM TAX(8)
DIM TABX0(8)            ! where each tab was drawn: from column, to column, its ×
DIM TABX1(8)
DIM TCLX(8)
DIM RECENT$(20)         ! files opened or saved, the latest first
DIM SOPEN$(8)           ! the files open last time
DIM HLP$(30)            ! the help screen
DIM BLAB$(7)            ! the menu bar's buttons, their keys, and where they were drawn
DIM BKEY$(7)
DIM BX0(7)
DIM BX1(7)
DIM OBX0(2)             ! the Open box's buttons
DIM OBX1(2)

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

! The screen column (from 0) just after the first N characters of S$: a tab runs on
! to the next stop, a multiple of TABW.
FUNCTION DCOL(S$, N)
  IF POS(LEFT$(S$, N), CHR$(9)) = 0 THEN
    DCOL = MIN(N, LEN(S$))
  ELSE
    LET DCC = 0
    FOR DCI = 1 TO MIN(N, LEN(S$))
      IF MID$(S$, DCI, 1) = CHR$(9) THEN
        LET DCC = (INT(DCC / TABW) + 1) * TABW
      ELSE
        LET DCC = DCC + 1
      END IF
    NEXT DCI
    DCOL = DCC
  END IF
END FUNCTION

! How many characters of S$ come before screen column C: the most whose DCOL is no
! more than C (the way back from a column to a place in the line).
FUNCTION CHAT(S$, C)
  IF POS(S$, CHR$(9)) = 0 THEN
    CHAT = MAX(0, MIN(INT(C), LEN(S$)))
  ELSE
    LET CAC = 0
    LET CAN = 0
    FOR CAI = 1 TO LEN(S$)
      IF MID$(S$, CAI, 1) = CHR$(9) THEN
        LET CAC = (INT(CAC / TABW) + 1) * TABW
      ELSE
        LET CAC = CAC + 1
      END IF
      IF CAC > C THEN EXIT FOR
      LET CAN = CAI
    NEXT CAI
    CHAT = CAN
  END IF
END FUNCTION

! S$ as it shows: each tab turned into spaces, out to the next stop.
FUNCTION EXPAND$(S$)
  IF POS(S$, CHR$(9)) = 0 THEN
    EXPAND$ = S$
  ELSE
    LET EXR$ = ""
    FOR EXI = 1 TO LEN(S$)
      LET EXC$ = MID$(S$, EXI, 1)
      IF EXC$ = CHR$(9) THEN
        LET EXR$ = EXR$ & REPEAT$(" ", TABW - (LEN(EXR$) MOD TABW))
      ELSE
        LET EXR$ = EXR$ & EXC$
      END IF
    NEXT EXI
    EXPAND$ = EXR$
  END IF
END FUNCTION

! 1 if C$ is part of a word (letters, digits, _ and $), for a double-click.
FUNCTION ISWORD(C$)
  LET IWU$ = UCASE$(C$)
  IF (IWU$ >= "A" AND IWU$ <= "Z") OR (C$ >= "0" AND C$ <= "9") OR C$ = "_" OR C$ = "$" THEN
    ISWORD = 1
  ELSE
    ISWORD = 0
  END IF
END FUNCTION

! What tab T is called: its file, or untitled-n.
FUNCTION TABNAME$(T)
  IF TNAME$(T) <> "" THEN
    TABNAME$ = TNAME$(T)
  ELSE
    TABNAME$ = "untitled-" & STR$(TUNT(T))
  END IF
END FUNCTION

FUNCTION NLINES$(N)
  IF N = 1 THEN
    NLINES$ = "1 line"
  ELSE
    NLINES$ = STR$(N) & " lines"
  END IF
END FUNCTION

! ---- colours: SET COLOR MIX makes a calm palette (0, the background, included) ----
LET CBAR = 16           ! the menu, tab and status bars
LET CBTN = 17           ! a button
LET CHOV = 18           ! the button or tab under the pointer
LET CTAB = 19           ! a tab not showing
LET CTEXT = 20
LET CDIM = 21           ! line numbers, hints
LET CSELB = 22          ! the selection
LET CLINE = 23          ! the cursor's line; the scroll bar's track
LET CCUR = 24           ! the cursor
LET CACC = 25           ! the tab showing; a file's unsaved mark
LET CWARN = 26
LET CMARK = 27          ! a tab character's →
LET CTHUMB = 28         ! the scroll bar
LET CBOX = 29           ! the help and Open boxes
SET COLOR MIX (0) 0.11, 0.12, 0.15
SET COLOR MIX (CBAR) 0.07, 0.08, 0.10
SET COLOR MIX (CBTN) 0.17, 0.18, 0.23
SET COLOR MIX (CHOV) 0.25, 0.27, 0.35
SET COLOR MIX (CTAB) 0.13, 0.14, 0.18
SET COLOR MIX (CTEXT) 0.88, 0.90, 0.94
SET COLOR MIX (CDIM) 0.45, 0.49, 0.57
SET COLOR MIX (CSELB) 0.18, 0.29, 0.50
SET COLOR MIX (CLINE) 0.14, 0.15, 0.19
SET COLOR MIX (CCUR) 1.00, 0.80, 0.35
SET COLOR MIX (CACC) 0.40, 0.62, 1.00
SET COLOR MIX (CWARN) 1.00, 0.45, 0.42
SET COLOR MIX (CMARK) 0.30, 0.33, 0.40
SET COLOR MIX (CTHUMB) 0.30, 0.32, 0.40
SET COLOR MIX (CBOX) 0.16, 0.17, 0.22

! ---- keys, as INKEY$ spells them ----
LET KENTER$ = CHR$(13)
LET KESC$ = CHR$(27)
LET KBS$ = CHR$(8)
LET KBS2$ = CHR$(127)
LET KTAB$ = CHR$(9)
LET KUP$ = CHR$(0) & CHR$(72)
LET KDN$ = CHR$(0) & CHR$(80)
LET KLT$ = CHR$(0) & CHR$(75)
LET KRT$ = CHR$(0) & CHR$(77)
LET KHOME$ = CHR$(0) & CHR$(71)
LET KEND$ = CHR$(0) & CHR$(79)
LET KPGUP$ = CHR$(0) & CHR$(73)
LET KPGDN$ = CHR$(0) & CHR$(81)
LET KDEL$ = CHR$(0) & CHR$(83)
LET KF1$ = CHR$(0) & CHR$(59)
LET KF2$ = CHR$(0) & CHR$(60)
LET KF3$ = CHR$(0) & CHR$(61)
LET KF4$ = CHR$(0) & CHR$(62)
LET KF7$ = CHR$(0) & CHR$(65)
LET KF8$ = CHR$(0) & CHR$(66)
LET KF9$ = CHR$(0) & CHR$(67)

! ---- the tab showing ----
LET NTABS = 0
LET CT = 0              ! which tab shows
LET UNTN = 0            ! the last untitled-n
LET NL = 1              ! its lines
LET CY = 1              ! the cursor: on line CY, after CX characters
LET CX = 0
LET WANTC = 0           ! the column ↑ and ↓ aim for
LET TOPL = 1            ! the first line showing
LET LEFTC = 0           ! and the first column (a long line scrolls sideways)
LET SELON = 0           ! 1: the text from AY, AX to the cursor is selected
LET AY = 1
LET AX = 0
LET HASSEL = 0          ! 7000's answer: a selection, from SY, SX to EY, EX
LET SY = 1
LET SX = 0
LET EY = 1
LET EX = 0

! ---- what's going on ----
LET MSG$ = ""
LET MSGW = 0            ! 1: MSG$ is a warning
LET HINT$ = ""
LET HASMOUSE = 0
LET HELP = 0            ! 1 while the help box shows
LET MODE = 0            ! 1 typing a name for Save as, 2 the Open box
LET TYPED$ = ""
LET CURP = 0            ! the typing cursor, after this many characters
LET OSEL = 0            ! the file picked in the Open box
LET CONFIRM = 0         ! waiting for a second go: 1 close tab CONFT, 2 quit, 3 replace CONFN$
LET WASCONF = 0
LET CONFT = 0
LET CONFN$ = ""
LET QUIT = 0
LET REDRAW = 1          ! the screen needs drawing again
LET FOLLOW = 1          ! scroll so the cursor shows, next time it's drawn
LET FRAME = 0
LET BLINK0 = 0          ! the cursor blinks from here
LET BON = 1
LET SHIFT = 0
LET K$ = ""
LET ACT = 0             ! a menu button's number (8800)
LET HOVK = 0            ! what the pointer is over (9700's HITK, HITN)
LET HOVN = 0
LET HITK = 0            ! 1 a menu button, 2 a tab, 3 a tab's ×, 4 +, 5 the text,
LET HITN = 0            ! 6 the scroll bar, 7 Tabs/Spaces, 8 a file in the Open box,
LET DRAG = 0            ! 9 its buttons, 10 the rest of it; DRAG 1 text, 2 scroll bar
LET THGRAB = 0          ! where the scroll bar was grabbed, from its top
LET LASTCF = -100       ! the last click on the text: its frame, line, place (a double-click)
LET LASTCL = 0
LET LASTCC = 0
LET MX = 0
LET MY = 0
LET MS = -1
LET NEWT = 0            ! 7150: the tab to show
LET CLT = 0             ! 7300: the tab to close
LET OPN$ = ""           ! 7400: the file to open; LERR its error
LET LERR = 0
LET NFAIL = 0
LET SAVEOK = 0
LET RCN$ = ""           ! 7720, 7730: a file to remember or forget
LET INS$ = ""           ! 8380: the text to put in
LET NEWL$ = ""          ! 7020: a line to put in; 7030: DLY, the line to take out
LET DLY = 0
LET IY0 = 0             ! 7040: the lines to indent
LET IY1 = 0
LET PL = 1              ! 8680: the pointer's line, and place in it
LET PC = 0
LET EXN$ = ""           ! 7650: is there a file called EXN$? EXISTS
LET EXISTS = 0
LET NRECENT = 0
LET SNR = 0
LET SACT$ = ""
LET NHLP = 0
! the layout, measured as it's drawn (9000)
LET DEVW = 0
LET DEVH = 0
LET ROWS = 24
LET COLS = 80
LET TROWS = 21
LET GUT = 5
LET TW = 74
LET SCX0 = 79
LET MAXTOP = 1
LET THTOP = 0
LET THBOT = 0
LET THH = 1
LET SBX0 = 0
LET SBX1 = 0
LET PLX0 = 0
LET PLX1 = 0
LET ODX0 = 0
LET ODX1 = 0
LET OTOP = 0
LET OH = 0
LET OLR0 = 0
LET ONSHOW = 0
LET OBR = 0

! the help screen
DO
  READ H$
  IF H$ = "*" THEN EXIT DO
  LET NHLP = NHLP + 1
  LET HLP$(NHLP) = H$
LOOP
DATA "THE EDITOR'S KEYS AND CLICKS"
DATA ""
DATA "Click         put the cursor there"
DATA "Drag          select (Shift+click: from the cursor to there)"
DATA "Double-click  select a word"
DATA "Tabs          click one to show it, × to close it, + for a new file"
DATA "Scroll bar    drag it, or click above or below it"
DATA ""
DATA "F2  save          F3  open         F4  save as"
DATA "F7  new file      F8  close        F9  next tab (Shift: back)"
DATA "Arrows, Home, End, PgUp, PgDn move; hold Shift to select"
DATA "Tab           a tab (or spaces: the status bar's Tabs button)"
DATA "Tab, Shift+Tab with lines selected: indent or undent them all"
DATA "Enter         a new line, indented like this one"
DATA "Esc           no selection"
DATA ""
DATA "In a terminal: Ctrl+N new, O open, S save, E save as, W close,"
DATA "T next tab, G help, Q quit"
DATA ""
DATA "Any key or click goes back"
DATA "*"

! ---- start: the files open last time, or a new one that says how this works ----
GET MOUSE MX, MY, MS
IF MS >= 0 THEN LET HASMOUSE = 1
FOR BI = 1 TO 7
  READ BLAB$(BI), H$, K2$
  IF HASMOUSE = 1 THEN
    LET BKEY$(BI) = H$
  ELSE
    LET BKEY$(BI) = K2$
  END IF
NEXT BI
DATA "New", "F7", "^N", "Open", "F3", "^O", "Save", "F2", "^S", "Save as", "F4", "^E"
DATA "Close", "F8", "^W", "Help", "F1", "^G", "Quit", "", "^Q"
IF HASMOUSE = 1 THEN
  LET HINT$ = "F1 help · F2 save · F3 open · F4 save as"
ELSE
  LET HINT$ = "Ctrl+S save · Ctrl+O open · Ctrl+Q quit"
END IF

GOSUB 7700                         ! editor.files: the files open last time, and recent ones
FOR SI = 1 TO SNR
  LET OPN$ = SOPEN$(SI)
  GOSUB 7400
  IF LERR <> 0 THEN LET NFAIL = NFAIL + 1
NEXT SI
LET MSGW = 0
IF NTABS = 0 THEN
  GOSUB 7200
  GOSUB 7250                       ! a few words on how it works
  LET MSG$ = "Welcome: type away, or F1 for the keys"
  IF HASMOUSE = 0 THEN LET MSG$ = "Welcome: type away (Ctrl+Q quits)"
ELSE
  FOR SI = 1 TO NTABS
    IF TNAME$(SI) = SACT$ THEN LET NEWT = SI
  NEXT SI
  GOSUB 7150
  LET MSG$ = "Opened " & STR$(NTABS) & " file(s) from last time"
  IF NFAIL > 0 THEN
    LET MSG$ = MSG$ & " · " & STR$(NFAIL) & " couldn't be opened"
    LET MSGW = 1
  END IF
END IF

! --- main loop ----------------------------------------------------------
GOSUB 9000
LET REDRAW = 0
DO
  DO
    LET K$ = INKEY$
    IF K$ = "" THEN EXIT DO
    GOSUB 8000                     ! a key
    LET REDRAW = 1
    LET BLINK0 = FRAME             ! (the cursor shows while you type)
    IF QUIT = 1 THEN EXIT DO
  LOOP
  IF QUIT = 1 THEN EXIT DO
  IF REDRAW = 1 THEN               ! (so the mouse finds things where they are now)
    GOSUB 9000
    LET REDRAW = 0
  END IF
  IF HASMOUSE = 1 THEN
    ! the mouse: 2 = pressed, 1 = held, 3 = released, 0 = up
    GET MOUSE MX, MY, MS
    GOSUB 8500
    ! the cursor blinks: on for 20 frames, off for 14
    LET B = 1
    IF ((FRAME - BLINK0) MOD 34) >= 20 THEN LET B = 0
    IF B <> BON THEN
      LET BON = B
      LET REDRAW = 1
    END IF
  END IF
  ! the screen changed size (a resized terminal): lay it out again
  ASK DEVICE SIZE NEWW, NEWH, DEVU$
  IF NEWW <> DEVW OR NEWH <> DEVH THEN LET REDRAW = 1
  IF REDRAW = 1 THEN
    GOSUB 9000
    LET REDRAW = 0
  END IF
  LET FRAME = FRAME + 1
  SLEEP 0.03                       ! ~30 frames/second (also presents the frame)
LOOP
CLEAR
SET TEXT COLOR CDIM
GRAPH TEXT, AT 2, INT(ROWS / 2) + 0.25: "The editor has closed. Run it again to carry on where you were."
SLEEP 0.03
END

! ======================================================================
! 7000  THE SELECTION — HASSEL = 1 if there is one, from SY, SX to EY, EX
!        (in order: the anchor AY, AX may be before or after the cursor).
! ======================================================================
7000 REM ---- the selection ----
LET HASSEL = 0
IF SELON = 1 AND AY > NL THEN LET SELON = 0
IF SELON = 1 AND (AY <> CY OR AX <> CX) THEN
  LET HASSEL = 1
  IF AY < CY OR (AY = CY AND AX < CX) THEN
    LET SY = AY
    LET SX = AX
    LET EY = CY
    LET EX = CX
  ELSE
    LET SY = CY
    LET SX = CX
    LET EY = AY
    LET EX = AX
  END IF
END IF
RETURN

! ----------------------------------------------------------------------
! 7010  DELETE THE SELECTION (if there is one); the cursor goes where it began.
! ----------------------------------------------------------------------
7010 REM ---- delete the selection ----
GOSUB 7000
IF HASSEL = 0 THEN RETURN
LET TX$(CT, SY) = LEFT$(TX$(CT, SY), SX) & MID$(TX$(CT, EY), EX + 1)
LET DN = EY - SY
IF DN > 0 THEN
  FOR DSI = SY + 1 TO NL - DN
    LET TX$(CT, DSI) = TX$(CT, DSI + DN)
  NEXT DSI
  FOR DSI = NL - DN + 1 TO NL
    LET TX$(CT, DSI) = ""
  NEXT DSI
  LET NL = NL - DN
END IF
LET CY = SY
LET CX = SX
LET SELON = 0
GOSUB 7090
RETURN

! ----------------------------------------------------------------------
! 7020  PUT IN A LINE, NEWL$, after line CY.  7030  TAKE OUT line DLY.
! ----------------------------------------------------------------------
7020 REM ---- put in a line ----
FOR ILI = NL TO CY + 1 STEP -1
  LET TX$(CT, ILI + 1) = TX$(CT, ILI)
NEXT ILI
LET TX$(CT, CY + 1) = NEWL$
LET NL = NL + 1
RETURN

7030 REM ---- take out a line ----
FOR ILI = DLY TO NL - 1
  LET TX$(CT, ILI) = TX$(CT, ILI + 1)
NEXT ILI
LET TX$(CT, NL) = ""
LET NL = NL - 1
RETURN

! ----------------------------------------------------------------------
! 7040  INDENT (SHIFT = 0) or UNDENT (SHIFT = 1) lines IY0 to IY1 by one stop:
!        a tab, or TABW spaces. Blank lines stay blank. The cursor and the
!        anchor move with their text.
! ----------------------------------------------------------------------
7040 REM ---- indent or undent lines ----
LET IND$ = KTAB$
IF TABSP = 1 THEN LET IND$ = REPEAT$(" ", TABW)
FOR INI = IY0 TO IY1
  LET L$ = TX$(CT, INI)
  IF SHIFT = 0 THEN
    IF L$ <> "" THEN
      LET TX$(CT, INI) = IND$ & L$
      IF AY = INI AND AX > 0 THEN LET AX = AX + LEN(IND$)
      IF CY = INI AND CX > 0 THEN LET CX = CX + LEN(IND$)
      LET TCHG(CT) = 1
    END IF
  ELSE
    LET RM = 0
    IF LEFT$(L$, 1) = KTAB$ THEN
      LET RM = 1
    ELSE
      DO WHILE RM < TABW AND MID$(L$, RM + 1, 1) = " "
        LET RM = RM + 1
      LOOP
    END IF
    IF RM > 0 THEN
      LET TX$(CT, INI) = MID$(L$, RM + 1)
      IF AY = INI THEN LET AX = MAX(0, AX - RM)
      IF CY = INI THEN LET CX = MAX(0, CX - RM)
      LET TCHG(CT) = 1
    END IF
  END IF
NEXT INI
LET WANTC = DCOL(TX$(CT, CY), CX)
RETURN

! ----------------------------------------------------------------------
! 7090  CHANGED — the file has changes to save; the cursor's column is the one
!        ↑ and ↓ aim for now.
! ----------------------------------------------------------------------
7090 REM ---- changed ----
LET TCHG(CT) = 1
LET WANTC = DCOL(TX$(CT, CY), CX)
LET FOLLOW = 1
LET MSG$ = ""
RETURN

! ======================================================================
! 7100  KEEP the tab showing's cursor, scroll and selection with it;
! 7110  FETCH tab CT's; 7120 START AT THE TOP of a file just opened.
! ======================================================================
7100 REM ---- keep the tab's place ----
LET TNL(CT) = NL
LET TCY(CT) = CY
LET TCX(CT) = CX
LET TTOP(CT) = TOPL
LET TLEFT(CT) = LEFTC
LET TWANT(CT) = WANTC
LET TSEL(CT) = SELON
LET TAY(CT) = AY
LET TAX(CT) = AX
RETURN

7110 REM ---- fetch the tab's place ----
LET NL = TNL(CT)
LET CY = TCY(CT)
LET CX = TCX(CT)
LET TOPL = TTOP(CT)
LET LEFTC = TLEFT(CT)
LET WANTC = TWANT(CT)
LET SELON = TSEL(CT)
LET AY = TAY(CT)
LET AX = TAX(CT)
RETURN

7120 REM ---- start at the top ----
LET CY = 1
LET CX = 0
LET TOPL = 1
LET LEFTC = 0
LET WANTC = 0
LET SELON = 0
LET AY = 1
LET AX = 0
RETURN

! ----------------------------------------------------------------------
! 7150  SHOW TAB NEWT.
! ----------------------------------------------------------------------
7150 REM ---- show a tab ----
IF NEWT = CT OR NEWT < 1 OR NEWT > NTABS THEN RETURN
GOSUB 7100
LET CT = NEWT
GOSUB 7110
LET DRAG = 0
GOSUB 7710                         ! (editor.files remembers which one shows)
RETURN

! ======================================================================
! 7200  A NEW FILE, in a tab of its own.  7250  The welcome text in it.
! ======================================================================
7200 REM ---- a new file ----
IF NTABS >= MAXTAB THEN
  LET MSG$ = "That's " & STR$(MAXTAB) & " tabs: close one first"
  LET MSGW = 1
  RETURN
END IF
IF NTABS > 0 THEN GOSUB 7100
LET NTABS = NTABS + 1
LET CT = NTABS
LET UNTN = UNTN + 1
LET TUNT(CT) = UNTN
LET TNAME$(CT) = ""
LET TCHG(CT) = 0
LET NL = 1
LET TX$(CT, 1) = ""
GOSUB 7120
LET MSG$ = "A new file: Save as gives it a name"
RETURN

7250 REM ---- the welcome text ----
LET NL = 0
DO
  READ W$
  IF W$ = "*" THEN EXIT DO
  ! (^ stands for a tab character)
  DO WHILE POS(W$, "^") > 0
    LET WP = POS(W$, "^")
    LET W$ = LEFT$(W$, WP - 1) & KTAB$ & MID$(W$, WP + 1)
  LOOP
  LET NL = NL + 1
  LET TX$(CT, NL) = W$
LOOP
RETURN
DATA "Welcome to the Arcade BASIC editor."
DATA ""
DATA "Type anywhere. Click to move the cursor; drag to select."
DATA "The bar at the top opens and saves files, and each file gets a tab."
DATA ""
DATA "Tab characters show out to the next stop, with a faint arrow:"
DATA "^This line starts with a tab,"
DATA "^^and this one with two. Select lines and press Tab or Shift+Tab."
DATA "The Tabs button in the status bar makes the Tab key put in spaces."
DATA ""
DATA "This file has no name yet: Save as gives it one. Files you open"
DATA "come back next time. Help (F1) lists the keys."
DATA "*"

! ======================================================================
! 7300  CLOSE TAB CLT — with unsaved changes, only the second time of asking.
! ======================================================================
7300 REM ---- close a tab ----
IF TCHG(CLT) = 1 AND NOT (WASCONF = 1 AND CONFT = CLT) THEN
  LET NEWT = CLT
  GOSUB 7150                       ! (show it)
  LET CONFIRM = 1
  LET CONFT = CLT
  LET MSG$ = TABNAME$(CLT) & " has changes that aren't saved: close it again to lose them"
  RETURN
END IF
LET CLN$ = TABNAME$(CLT)
GOSUB 7100
FOR CLU = CLT TO NTABS - 1         ! the tabs after it move up one
  FOR CLI = 1 TO TNL(CLU + 1)
    LET TX$(CLU, CLI) = TX$(CLU + 1, CLI)
  NEXT CLI
  LET TNAME$(CLU) = TNAME$(CLU + 1)
  LET TUNT(CLU) = TUNT(CLU + 1)
  LET TCHG(CLU) = TCHG(CLU + 1)
  LET TNL(CLU) = TNL(CLU + 1)
  LET TCY(CLU) = TCY(CLU + 1)
  LET TCX(CLU) = TCX(CLU + 1)
  LET TTOP(CLU) = TTOP(CLU + 1)
  LET TLEFT(CLU) = TLEFT(CLU + 1)
  LET TWANT(CLU) = TWANT(CLU + 1)
  LET TSEL(CLU) = TSEL(CLU + 1)
  LET TAY(CLU) = TAY(CLU + 1)
  LET TAX(CLU) = TAX(CLU + 1)
NEXT CLU
FOR CLI = 1 TO TNL(NTABS)
  LET TX$(NTABS, CLI) = ""
NEXT CLI
LET NTABS = NTABS - 1
IF CT > CLT THEN
  LET CT = CT - 1
ELSEIF CT > NTABS THEN
  LET CT = NTABS
END IF
IF NTABS = 0 THEN
  GOSUB 7200                       ! never no tabs at all
ELSE
  GOSUB 7110
END IF
LET MSG$ = "Closed " & CLN$
GOSUB 7710
RETURN

! ======================================================================
! 7400  OPEN THE FILE OPN$ in a tab — a new one, or the one showing if it's a
!        new file with nothing typed in it. It's read into row STG first, so a
!        file that can't be read changes nothing. LERR = 0 if it worked.
! ======================================================================
7400 REM ---- open a file ----
LET LERR = 0
FOR OPT = 1 TO NTABS
  IF TNAME$(OPT) = OPN$ THEN LET LERR = -OPT
NEXT OPT
IF LERR < 0 THEN                   ! it's open already: show it
  LET NEWT = -LERR
  LET LERR = 0
  GOSUB 7150
  LET MSG$ = OPN$ & " is open already"
  RETURN
END IF
LET OTT = NTABS + 1
IF NTABS > 0 THEN
  IF TNAME$(CT) = "" AND TCHG(CT) = 0 THEN LET OTT = CT
END IF
IF OTT > MAXTAB THEN
  LET MSG$ = "That's " & STR$(MAXTAB) & " tabs: close one first"
  LET MSGW = 1
  LET LERR = 1
  RETURN
END IF
LET RN = 0
LET TRUNC = 0
LET LMSG$ = ""
WHEN EXCEPTION IN
  OPEN #1: NAME OPN$, ACCESS INPUT
  DO
    LINE INPUT #1: RL$
    IF RN = MAXL THEN
      LET TRUNC = 1
      EXIT DO
    END IF
    LET RN = RN + 1
    LET TX$(STG, RN) = RL$
  LOOP
  CLOSE #1
USE
  CLOSE #1
  IF EXTYPE <> 7020 THEN           ! (7020 is the end of the file: all read)
    LET LERR = EXTYPE
    LET LMSG$ = EXTEXT$
  END IF
END WHEN
IF LERR <> 0 THEN
  FOR OPT = 1 TO RN
    LET TX$(STG, OPT) = ""
  NEXT OPT
  IF LERR = 7010 THEN
    LET MSG$ = "There's no file called " & OPN$
    LET RCN$ = OPN$
    GOSUB 7730                     ! (so Open stops offering it)
  ELSE
    LET MSG$ = "Couldn't open " & OPN$ & ": " & LMSG$
  END IF
  LET MSGW = 1
  RETURN
END IF
IF NTABS > 0 THEN GOSUB 7100
IF OTT > NTABS THEN LET NTABS = OTT
LET CT = OTT
FOR OPT = 1 TO RN
  LET TX$(CT, OPT) = TX$(STG, OPT)
  LET TX$(STG, OPT) = ""
NEXT OPT
IF RN = 0 THEN                     ! an empty file is one empty line
  LET RN = 1
  LET TX$(CT, 1) = ""
END IF
LET NL = RN
GOSUB 7120
LET TNAME$(CT) = OPN$
LET TCHG(CT) = 0
LET MSG$ = "Opened " & OPN$ & " · " & NLINES$(NL)
IF TRUNC = 1 THEN
  LET MSG$ = "Opened the first " & STR$(MAXL) & " lines of " & OPN$ & ": saving it would lose the rest"
  LET MSGW = 1
END IF
LET RCN$ = OPN$
GOSUB 7720
GOSUB 7710
RETURN

7450 REM ---- Open: the box ----
LET MODE = 2
LET TYPED$ = ""
LET CURP = 0
LET OSEL = 0
LET DRAG = 0
RETURN

! ======================================================================
! 7500  SAVE the file showing (a new one asks for a name first).
! 7600  SAVE AS: ask for a name (8170 takes it).
! ======================================================================
7500 REM ---- save ----
IF TNAME$(CT) = "" THEN
  GOSUB 7600
  RETURN
END IF
LET SVN$ = TNAME$(CT)
LET SAVEOK = 0
WHEN EXCEPTION IN
  OPEN #1: NAME SVN$, ACCESS OUTPUT
  FOR SVI = 1 TO NL
    PRINT #1: TX$(CT, SVI)
  NEXT SVI
  CLOSE #1
  LET SAVEOK = 1
USE
  CLOSE #1
  LET MSG$ = "Couldn't save " & SVN$ & ": " & EXTEXT$
  LET MSGW = 1
END WHEN
IF SAVEOK = 1 THEN
  LET TCHG(CT) = 0
  LET MSG$ = "Saved " & SVN$ & " · " & NLINES$(NL)
  LET RCN$ = SVN$
  GOSUB 7720
  GOSUB 7710
END IF
RETURN

7600 REM ---- save as ----
LET MODE = 1
LET TYPED$ = TNAME$(CT)
IF TYPED$ = "" THEN LET TYPED$ = TABNAME$(CT) & ".txt"
LET CURP = LEN(TYPED$)
LET DRAG = 0
RETURN

! ----------------------------------------------------------------------
! 7650  IS THERE A FILE called EXN$? EXISTS = 1 if so.
! ----------------------------------------------------------------------
7650 REM ---- is there a file? ----
LET EXISTS = 0
WHEN EXCEPTION IN
  OPEN #2: NAME EXN$, ACCESS INPUT
  LET EXISTS = 1
  CLOSE #2
USE
  CLOSE #2
END WHEN
RETURN

! ======================================================================
! 7700  READ editor.files: the files open last time (SOPEN$(1 .. SNR)), the
!        one that showed (SACT$), and the recent ones. 7710 WRITE it.
!        "ARCADE-EDITOR 1", the one showing, how many open, their names, how
!        many recent, their names — a line each.
! ======================================================================
7700 REM ---- read editor.files ----
LET SNR = 0
LET NRECENT = 0
WHEN EXCEPTION IN
  OPEN #3: NAME SESSION$, ACCESS INPUT
  LINE INPUT #3: RL$
  IF RL$ = "ARCADE-EDITOR 1" THEN
    LINE INPUT #3: SACT$
    LINE INPUT #3: RL$
    LET SN = MIN(TONUM(RL$), MAXTAB)
    FOR SSI = 1 TO SN
      LINE INPUT #3: SOPEN$(SSI)
      LET SNR = SSI
    NEXT SSI
    LINE INPUT #3: RL$
    LET SN = MIN(TONUM(RL$), MAXREC)
    FOR SSI = 1 TO SN
      LINE INPUT #3: RECENT$(SSI)
      LET NRECENT = SSI
    NEXT SSI
  END IF
  CLOSE #3
USE
  CLOSE #3                         ! none yet, or cut short: what was read stands
END WHEN
RETURN

7710 REM ---- write editor.files ----
WHEN EXCEPTION IN
  OPEN #3: NAME SESSION$, ACCESS OUTPUT
  PRINT #3: "ARCADE-EDITOR 1"
  PRINT #3: TNAME$(CT)
  LET SN = 0
  FOR SSI = 1 TO NTABS
    IF TNAME$(SSI) <> "" THEN LET SN = SN + 1
  NEXT SSI
  PRINT #3: STR$(SN)
  FOR SSI = 1 TO NTABS
    IF TNAME$(SSI) <> "" THEN PRINT #3: TNAME$(SSI)
  NEXT SSI
  PRINT #3: STR$(NRECENT)
  FOR SSI = 1 TO NRECENT
    PRINT #3: RECENT$(SSI)
  NEXT SSI
  CLOSE #3
USE
  CLOSE #3
END WHEN
RETURN

! ----------------------------------------------------------------------
! 7720  REMEMBER RCN$ at the top of the recent files.  7730  FORGET it.
! ----------------------------------------------------------------------
7720 REM ---- remember a file ----
LET RF = 0
FOR RCI = 1 TO NRECENT
  IF RECENT$(RCI) = RCN$ THEN LET RF = RCI
NEXT RCI
IF RF = 0 THEN
  IF NRECENT < MAXREC THEN LET NRECENT = NRECENT + 1
  LET RF = NRECENT                 ! (a full list loses its last)
END IF
FOR RCI = RF TO 2 STEP -1
  LET RECENT$(RCI) = RECENT$(RCI - 1)
NEXT RCI
LET RECENT$(1) = RCN$
RETURN

7730 REM ---- forget a file ----
LET RF = 0
FOR RCI = 1 TO NRECENT
  IF RECENT$(RCI) = RCN$ THEN LET RF = RCI
NEXT RCI
IF RF > 0 THEN
  FOR RCI = RF TO NRECENT - 1
    LET RECENT$(RCI) = RECENT$(RCI + 1)
  NEXT RCI
  LET NRECENT = NRECENT - 1
  GOSUB 7710
END IF
RETURN

! ----------------------------------------------------------------------
! 7900  QUIT — with unsaved changes, only the second time of asking.
! ----------------------------------------------------------------------
7900 REM ---- quit ----
LET NCHG = 0
FOR QTI = 1 TO NTABS
  IF TCHG(QTI) = 1 THEN LET NCHG = NCHG + 1
NEXT QTI
IF NCHG > 0 AND WASCONF <> 2 THEN
  LET CONFIRM = 2
  LET MSG$ = STR$(NCHG) & " file(s) with changes that aren't saved: Quit again to lose them"
  RETURN
END IF
GOSUB 7710
LET QUIT = 1
RETURN

! ======================================================================
! 8000  A KEY (K$).
! ======================================================================
8000 REM ---- a key ----
LET WASCONF = CONFIRM              ! (a second go at closing or quitting counts only
LET CONFIRM = 0                    !  if it comes next)
LET MSGW = 0
IF HELP = 1 THEN
  LET HELP = 0                     ! any key leaves the help box
  RETURN
END IF
IF MODE > 0 THEN
  GOSUB 8100                       ! a file's name being typed
  RETURN
END IF
LET SHIFT = 0
IF KEYDOWN("ShiftLeft") OR KEYDOWN("ShiftRight") THEN LET SHIFT = 1
LET FOLLOW = 1
LET ACT = 0                        ! the menu's keys: F keys, or Ctrl in a terminal
IF K$ = KF7$ OR K$ = CHR$(14) THEN LET ACT = 1
IF K$ = KF3$ OR K$ = CHR$(15) THEN LET ACT = 2
IF K$ = KF2$ OR K$ = CHR$(19) THEN LET ACT = 3
IF K$ = KF4$ OR K$ = CHR$(5) THEN LET ACT = 4
IF K$ = KF8$ OR K$ = CHR$(23) THEN LET ACT = 5
IF K$ = KF1$ OR K$ = CHR$(7) THEN LET ACT = 6
IF K$ = CHR$(17) THEN LET ACT = 7
IF ACT > 0 THEN
  GOSUB 8800
ELSEIF K$ = KF9$ OR K$ = CHR$(20) THEN
  LET NEWT = CT + 1
  IF SHIFT = 1 THEN LET NEWT = CT - 1
  IF NEWT > NTABS THEN LET NEWT = 1
  IF NEWT < 1 THEN LET NEWT = NTABS
  GOSUB 7150
ELSEIF K$ = KESC$ THEN
  LET SELON = 0
ELSEIF K$ = KUP$ OR K$ = KDN$ OR K$ = KLT$ OR K$ = KRT$ OR K$ = KHOME$ OR K$ = KEND$ OR K$ = KPGUP$ OR K$ = KPGDN$ THEN
  GOSUB 8200
ELSEIF K$ = KENTER$ OR K$ = CHR$(10) THEN
  GOSUB 8300
ELSEIF K$ = KBS$ OR K$ = KBS2$ THEN
  GOSUB 8320
ELSEIF K$ = KDEL$ THEN
  GOSUB 8340
ELSEIF K$ = KTAB$ THEN
  GOSUB 8360
ELSEIF LEN(K$) = 1 AND K$ >= " " THEN
  LET INS$ = K$
  GOSUB 8380
END IF
RETURN

! ----------------------------------------------------------------------
! 8100  TYPING A FILE'S NAME — for Save as (MODE 1) or in the Open box (MODE 2,
!        where ↑ and ↓ pick from the recent files). Enter finishes, Esc cancels.
! ----------------------------------------------------------------------
8100 REM ---- typing a name ----
IF K$ = KENTER$ OR K$ = CHR$(10) THEN
  IF MODE = 1 THEN
    GOSUB 8170
  ELSE
    GOSUB 8160
  END IF
ELSEIF K$ = KESC$ THEN
  IF MODE = 1 THEN LET MSG$ = "Not saved"
  LET MODE = 0
ELSEIF K$ = KBS$ OR K$ = KBS2$ THEN
  IF CURP > 0 THEN
    LET TYPED$ = LEFT$(TYPED$, CURP - 1) & MID$(TYPED$, CURP + 1)
    LET CURP = CURP - 1
  END IF
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
ELSEIF (K$ = KUP$ OR K$ = KDN$) AND MODE = 2 THEN
  IF K$ = KUP$ AND OSEL > 1 THEN LET OSEL = OSEL - 1
  IF K$ = KDN$ AND OSEL < ONSHOW THEN LET OSEL = OSEL + 1
  IF OSEL > 0 THEN
    LET TYPED$ = RECENT$(OSEL)
    LET CURP = LEN(TYPED$)
  END IF
ELSEIF LEN(K$) = 1 AND K$ >= " " AND K$ <> KBS2$ AND LEN(TYPED$) < 200 THEN
  LET TYPED$ = LEFT$(TYPED$, CURP) & K$ & MID$(TYPED$, CURP + 1)
  LET CURP = CURP + 1
END IF
RETURN

8160 REM ---- Open: the name typed, or picked ----
LET OPN$ = LTRIM$(RTRIM$(TYPED$))
IF OPN$ = "" AND OSEL > 0 THEN LET OPN$ = RECENT$(OSEL)
IF OPN$ = "" THEN RETURN           ! (nothing to open yet: the box stays)
LET MODE = 0
GOSUB 7400
RETURN

8170 REM ---- Save as: the name typed ----
LET SAN$ = LTRIM$(RTRIM$(TYPED$))
IF SAN$ = "" THEN
  LET MODE = 0
  LET MSG$ = "Not saved"
  RETURN
END IF
LET SAT = 0
FOR SAI = 1 TO NTABS
  IF SAI <> CT AND TNAME$(SAI) = SAN$ THEN LET SAT = SAI
NEXT SAI
IF SAT > 0 THEN
  LET MODE = 0
  LET MSG$ = SAN$ & " is open in another tab: close it there first"
  LET MSGW = 1
  RETURN
END IF
IF SAN$ <> TNAME$(CT) AND NOT (WASCONF = 3 AND CONFN$ = SAN$) THEN
  LET EXN$ = SAN$
  GOSUB 7650
  IF EXISTS = 1 THEN               ! someone else's file: only the second Enter replaces it
    LET CONFIRM = 3
    LET CONFN$ = SAN$
    LET MSG$ = "There's a file called " & SAN$ & " already: Enter replaces it, Esc doesn't"
    RETURN
  END IF
END IF
LET MODE = 0
LET OLDN$ = TNAME$(CT)
LET TNAME$(CT) = SAN$
GOSUB 7500
IF SAVEOK = 0 THEN LET TNAME$(CT) = OLDN$
RETURN

! ----------------------------------------------------------------------
! 8200  MOVE THE CURSOR — the arrows, Home, End, PgUp, PgDn; with Shift,
!        selecting as it goes.
! ----------------------------------------------------------------------
8200 REM ---- move the cursor ----
GOSUB 7000
IF SHIFT = 1 THEN
  IF SELON = 0 THEN
    LET SELON = 1
    LET AY = CY
    LET AX = CX
  END IF
ELSE
  LET SELON = 0
  IF HASSEL = 1 AND (K$ = KLT$ OR K$ = KRT$) THEN
    ! ← or → on a selection: to that end of it
    IF K$ = KLT$ THEN
      LET CY = SY
      LET CX = SX
    ELSE
      LET CY = EY
      LET CX = EX
    END IF
    LET WANTC = DCOL(TX$(CT, CY), CX)
    RETURN
  END IF
END IF
LET L$ = TX$(CT, CY)
IF K$ = KLT$ THEN
  IF CX > 0 THEN
    LET CX = CX - 1
  ELSEIF CY > 1 THEN
    LET CY = CY - 1
    LET CX = LEN(TX$(CT, CY))
  END IF
  LET WANTC = DCOL(TX$(CT, CY), CX)
ELSEIF K$ = KRT$ THEN
  IF CX < LEN(L$) THEN
    LET CX = CX + 1
  ELSEIF CY < NL THEN
    LET CY = CY + 1
    LET CX = 0
  END IF
  LET WANTC = DCOL(TX$(CT, CY), CX)
ELSEIF K$ = KUP$ THEN
  IF CY > 1 THEN
    LET CY = CY - 1
    LET CX = CHAT(TX$(CT, CY), WANTC)
  ELSE
    LET CX = 0
    LET WANTC = 0
  END IF
ELSEIF K$ = KDN$ THEN
  IF CY < NL THEN
    LET CY = CY + 1
    LET CX = CHAT(TX$(CT, CY), WANTC)
  ELSE
    LET CX = LEN(L$)
    LET WANTC = DCOL(L$, CX)
  END IF
ELSEIF K$ = KHOME$ THEN
  ! to where the text starts, after the indent; again, to the very start
  LET HM = 0
  DO WHILE HM < LEN(L$) AND (MID$(L$, HM + 1, 1) = " " OR MID$(L$, HM + 1, 1) = KTAB$)
    LET HM = HM + 1
  LOOP
  IF CX = HM THEN LET HM = 0
  LET CX = HM
  LET WANTC = DCOL(L$, CX)
ELSEIF K$ = KEND$ THEN
  LET CX = LEN(L$)
  LET WANTC = DCOL(L$, CX)
ELSEIF K$ = KPGUP$ OR K$ = KPGDN$ THEN
  LET PG = TROWS - 1
  IF K$ = KPGUP$ THEN LET PG = -PG
  LET CY = MIN(MAX(CY + PG, 1), NL)
  LET TOPL = TOPL + PG             ! (9100 keeps it in range)
  LET CX = CHAT(TX$(CT, CY), WANTC)
END IF
RETURN

! ----------------------------------------------------------------------
! 8300  ENTER — a new line, as indented as this one.  8320  BACKSPACE.
! 8340  DELETE.  8360  TAB.  8380  PUT IN the text INS$ (typing).
! ----------------------------------------------------------------------
8300 REM ---- enter ----
GOSUB 7010
IF NL >= MAXL THEN
  LET MSG$ = "A file here can have up to " & STR$(MAXL) & " lines"
  LET MSGW = 1
  RETURN
END IF
LET L$ = TX$(CT, CY)
LET IND = 0
DO WHILE IND < CX AND (MID$(L$, IND + 1, 1) = " " OR MID$(L$, IND + 1, 1) = KTAB$)
  LET IND = IND + 1
LOOP
LET TX$(CT, CY) = LEFT$(L$, CX)
LET NEWL$ = LEFT$(L$, IND) & MID$(L$, CX + 1)
GOSUB 7020
LET CY = CY + 1
LET CX = IND
GOSUB 7090
RETURN

8320 REM ---- backspace ----
GOSUB 7000
IF HASSEL = 1 THEN
  GOSUB 7010
  RETURN
END IF
LET SELON = 0
IF CX > 0 THEN
  LET L$ = TX$(CT, CY)
  LET TX$(CT, CY) = LEFT$(L$, CX - 1) & MID$(L$, CX + 1)
  LET CX = CX - 1
ELSEIF CY > 1 THEN                 ! at the start of a line: join it to the one above
  LET CX = LEN(TX$(CT, CY - 1))
  LET TX$(CT, CY - 1) = TX$(CT, CY - 1) & TX$(CT, CY)
  LET DLY = CY
  GOSUB 7030
  LET CY = CY - 1
ELSE
  RETURN
END IF
GOSUB 7090
RETURN

8340 REM ---- delete ----
GOSUB 7000
IF HASSEL = 1 THEN
  GOSUB 7010
  RETURN
END IF
LET SELON = 0
LET L$ = TX$(CT, CY)
IF CX < LEN(L$) THEN
  LET TX$(CT, CY) = LEFT$(L$, CX) & MID$(L$, CX + 2)
ELSEIF CY < NL THEN                ! at the end of a line: join the next one to it
  LET TX$(CT, CY) = L$ & TX$(CT, CY + 1)
  LET DLY = CY + 1
  GOSUB 7030
ELSE
  RETURN
END IF
GOSUB 7090
RETURN

8360 REM ---- tab ----
GOSUB 7000
IF HASSEL = 1 AND SY <> EY THEN
  ! several lines: indent (or with Shift undent) them all — not a last line the
  ! selection only reaches the start of
  LET IY0 = SY
  LET IY1 = EY
  IF EX = 0 THEN LET IY1 = EY - 1
  GOSUB 7040
ELSEIF SHIFT = 1 THEN
  LET IY0 = CY
  LET IY1 = CY
  GOSUB 7040
ELSE
  LET INS$ = KTAB$
  IF TABSP = 1 THEN LET INS$ = REPEAT$(" ", TABW - (DCOL(TX$(CT, CY), CX) MOD TABW))
  GOSUB 8380
END IF
RETURN

8380 REM ---- put in text ----
GOSUB 7010                         ! (typing over a selection replaces it)
LET SELON = 0
LET L$ = TX$(CT, CY)
LET TX$(CT, CY) = LEFT$(L$, CX) & INS$ & MID$(L$, CX + 1)
LET CX = CX + LEN(INS$)
GOSUB 7090
RETURN

! ======================================================================
! 8500  THE MOUSE (MX, MY, MS): what it's over, presses, drags.
! ======================================================================
8500 REM ---- the mouse ----
GOSUB 9700
IF HITK <> HOVK OR HITN <> HOVN THEN
  LET HOVK = HITK
  LET HOVN = HITN
  LET REDRAW = 1
END IF
IF MS = 2 THEN
  GOSUB 8600
ELSEIF MS = 1 THEN
  GOSUB 8700
ELSEIF MS = 3 AND DRAG > 0 THEN
  LET DRAG = 0
  LET REDRAW = 1
END IF
RETURN

8600 REM ---- a press ----
LET WASCONF = CONFIRM
LET CONFIRM = 0
LET MSGW = 0
LET BLINK0 = FRAME
LET REDRAW = 1
IF HELP = 1 THEN
  LET HELP = 0
  RETURN
END IF
IF MODE = 2 THEN
  IF HITK = 8 THEN                 ! a recent file
    LET MODE = 0
    LET OPN$ = RECENT$(HITN)
    GOSUB 7400
  ELSEIF HITK = 9 AND HITN = 1 THEN
    GOSUB 8160
  ELSEIF HITK <> 10 THEN           ! Cancel, or outside the box
    LET MODE = 0
  END IF
  RETURN
END IF
IF MODE = 1 AND HITK <> 7 THEN     ! a click away from Save as: not saved after all
  LET MODE = 0
  LET MSG$ = "Not saved"
END IF
IF HITK = 1 THEN
  LET ACT = HITN
  GOSUB 8800
ELSEIF HITK = 2 THEN
  LET NEWT = HITN
  GOSUB 7150
ELSEIF HITK = 3 THEN
  LET CLT = HITN
  GOSUB 7300
ELSEIF HITK = 4 THEN
  GOSUB 7200
ELSEIF HITK = 5 THEN
  GOSUB 8650
ELSEIF HITK = 6 THEN
  GOSUB 8750
ELSEIF HITK = 7 THEN
  LET TABSP = 1 - TABSP
  IF TABSP = 1 THEN
    LET MSG$ = "The Tab key puts in spaces, to the next stop"
  ELSE
    LET MSG$ = "The Tab key puts in a tab character"
  END IF
END IF
RETURN

! ----------------------------------------------------------------------
! 8650  A PRESS ON THE TEXT — the cursor goes there (Shift: selecting to there),
!        and dragging selects; a double-click selects a word (8660).
! ----------------------------------------------------------------------
8650 REM ---- a press on the text ----
LET SHIFT = 0
IF KEYDOWN("ShiftLeft") OR KEYDOWN("ShiftRight") THEN LET SHIFT = 1
GOSUB 8680
IF SHIFT = 0 AND FRAME - LASTCF <= 12 AND PL = LASTCL AND ABS(PC - LASTCC) <= 1 THEN
  GOSUB 8660
  LET LASTCF = -100
  RETURN
END IF
LET LASTCF = FRAME
LET LASTCL = PL
LET LASTCC = PC
IF SHIFT = 1 THEN
  IF SELON = 0 THEN
    LET SELON = 1
    LET AY = CY
    LET AX = CX
  END IF
ELSE
  LET SELON = 1                    ! (nothing selected until it's dragged)
  LET AY = PL
  LET AX = PC
END IF
LET CY = PL
LET CX = PC
LET WANTC = DCOL(TX$(CT, CY), CX)
LET DRAG = 1
RETURN

8660 REM ---- select a word ----
LET L$ = TX$(CT, PL)
LET WA = PC
LET WB = PC
DO WHILE WA > 0
  IF ISWORD(MID$(L$, WA, 1)) = 0 THEN EXIT DO
  LET WA = WA - 1
LOOP
DO WHILE WB < LEN(L$)
  IF ISWORD(MID$(L$, WB + 1, 1)) = 0 THEN EXIT DO
  LET WB = WB + 1
LOOP
LET CY = PL
LET AY = PL
LET AX = WA
LET CX = WB
LET SELON = 1
LET DRAG = 0
LET WANTC = DCOL(L$, CX)
RETURN

! ----------------------------------------------------------------------
! 8680  WHERE THE POINTER IS IN THE TEXT: line PL, after PC characters. Above
!        or below the text, it's the first or last line showing.
! ----------------------------------------------------------------------
8680 REM ---- the pointer's place in the text ----
LET PK = ROWS - 1 - INT(MY)        ! its row, from the top
IF PK < 2 THEN LET PK = 2
IF PK > ROWS - 2 THEN LET PK = ROWS - 2
LET PL = TOPL + PK - 2
LET PCOL = INT(MX - GUT + LEFTC + 0.5)
IF PCOL < 0 THEN LET PCOL = 0
IF PL > NL THEN                    ! under the last line: its end
  LET PL = NL
  LET PC = LEN(TX$(CT, PL))
ELSE
  LET PC = CHAT(TX$(CT, PL), PCOL)
END IF
RETURN

! ----------------------------------------------------------------------
! 8700  HELD — selecting (scrolling when the pointer's past the text's edge),
!        or dragging the scroll bar.
! ----------------------------------------------------------------------
8700 REM ---- dragging ----
IF DRAG = 1 THEN
  LET WAS$ = STR$(TOPL) & " " & STR$(LEFTC) & " " & STR$(CY) & " " & STR$(CX)
  LET PK = ROWS - 1 - INT(MY)
  IF PK < 2 AND TOPL > 1 THEN LET TOPL = TOPL - 1
  IF PK > ROWS - 2 AND TOPL < MAXTOP THEN LET TOPL = TOPL + 1
  IF MX < GUT AND LEFTC > 0 THEN LET LEFTC = LEFTC - 1
  IF MX >= SCX0 AND LEFTC + TW < DCOL(TX$(CT, CY), LEN(TX$(CT, CY))) THEN LET LEFTC = LEFTC + 1
  GOSUB 8680
  LET CY = PL
  LET CX = PC
  LET WANTC = DCOL(TX$(CT, CY), CX)
  IF WAS$ <> STR$(TOPL) & " " & STR$(LEFTC) & " " & STR$(CY) & " " & STR$(CX) THEN LET REDRAW = 1
ELSEIF DRAG = 2 THEN
  LET NTOP = 1 + INT(((ROWS - 2) - (MY + THGRAB)) / (TROWS - THH) * (MAXTOP - 1) + 0.5)
  LET NTOP = MIN(MAX(NTOP, 1), MAXTOP)
  IF NTOP <> TOPL THEN
    LET TOPL = NTOP
    LET REDRAW = 1
  END IF
END IF
RETURN

8750 REM ---- a press on the scroll bar ----
IF MY <= THTOP AND MY >= THBOT THEN
  LET DRAG = 2                     ! on the thumb: drag it
  LET THGRAB = THTOP - MY
ELSEIF MY > THTOP THEN
  LET TOPL = TOPL - (TROWS - 1)    ! above it: a page up
ELSE
  LET TOPL = TOPL + (TROWS - 1)
END IF
RETURN

! ----------------------------------------------------------------------
! 8800  A MENU BUTTON, ACT: 1 New, 2 Open, 3 Save, 4 Save as, 5 Close, 6 Help, 7 Quit.
! ----------------------------------------------------------------------
8800 REM ---- a menu button ----
IF ACT = 1 THEN
  GOSUB 7200
ELSEIF ACT = 2 THEN
  GOSUB 7450
ELSEIF ACT = 3 THEN
  GOSUB 7500
ELSEIF ACT = 4 THEN
  GOSUB 7600
ELSEIF ACT = 5 THEN
  LET CLT = CT
  GOSUB 7300
ELSEIF ACT = 6 THEN
  LET HELP = 1
ELSE
  GOSUB 7900
END IF
RETURN

! ======================================================================
! 9000  DRAW the screen, measuring the layout as it goes: ROWS x COLS cells,
!        the menu bar on row 0 (counting from the top), the tabs on row 1, the
!        text from row 2 (TROWS rows: line numbers in GUT columns, then TW
!        columns of text, then the scroll bar), and the status bar at the bottom.
!        Text sits on a row's baseline, a quarter of a row up from its bottom.
! ======================================================================
9000 REM ---- draw ----
CLEAR
ASK DEVICE SIZE DEVW, DEVH, DEVU$
LET ROWS = 24                      ! when the device's size is unknown
IF DEVH >= 4 THEN LET ROWS = INT(DEVH / 4)
IF ROWS > 40 THEN LET ROWS = 40
IF ROWS < 12 THEN LET ROWS = 12
LET COLS = 80                      ! one window unit per column (see kanban.bas, 9060)
IF DEVW >= 2 AND DEVH <= 160 THEN LET COLS = INT(DEVW / 2)
IF COLS < 60 THEN LET COLS = 60
SET WINDOW 0, COLS, 0, ROWS
LET TROWS = ROWS - 3
LET GUT = MAX(5, LEN(STR$(NL)) + 2)
LET TW = COLS - GUT - 1
LET SCX0 = COLS - 1
GOSUB 9100                         ! scroll so the cursor shows
GOSUB 9150                         ! the menu bar
GOSUB 9200                         ! the tabs
GOSUB 7000                         ! the selection, in order
FOR RI = 0 TO TROWS - 1
  LET LI = TOPL + RI
  LET RK = RI + 2
  IF LI <= NL THEN GOSUB 9400
NEXT RI
GOSUB 9300                         ! the scroll bar
GOSUB 9350                         ! the status bar
IF MODE = 2 THEN GOSUB 9500
IF HELP = 1 THEN GOSUB 9600
RETURN

9100 REM ---- scroll so the cursor shows ----
LET MAXTOP = MAX(1, NL - TROWS + 1)
IF FOLLOW = 1 THEN
  LET FOLLOW = 0
  IF CY < TOPL THEN LET TOPL = CY
  IF CY > TOPL + TROWS - 1 THEN LET TOPL = CY - TROWS + 1
  LET FDC = DCOL(TX$(CT, CY), CX)
  IF FDC < LEFTC THEN LET LEFTC = MAX(0, FDC - INT(TW / 4))
  IF FDC > LEFTC + TW - 1 THEN LET LEFTC = FDC - INT(TW * 3 / 4)
END IF
LET TOPL = MIN(MAX(TOPL, 1), MAXTOP)
IF LEFTC < 0 THEN LET LEFTC = 0
RETURN

! ----------------------------------------------------------------------
! 9150  THE MENU BAR — New Open Save Save-as Close at the left, Help Quit at
!        the right, each with its key, dim.
! ----------------------------------------------------------------------
9150 REM ---- the menu bar ----
SET AREA COLOR CBAR
GRAPH AREA: 0, ROWS - 1; COLS, ROWS - 1; COLS, ROWS; 0, ROWS
LET BXX = 1
FOR BI = 1 TO 7
  LET BW = LEN(BLAB$(BI)) + 2
  IF BKEY$(BI) <> "" THEN LET BW = BW + LEN(BKEY$(BI)) + 1
  IF BI <= 5 THEN
    LET BX0(BI) = BXX
    LET BX1(BI) = BXX + BW
    LET BXX = BX1(BI) + 1
  ELSE                             ! Help and Quit from the right: Quit first
    LET BI2 = 13 - BI
    LET BW2 = LEN(BLAB$(BI2)) + 2
    IF BKEY$(BI2) <> "" THEN LET BW2 = BW2 + LEN(BKEY$(BI2)) + 1
    LET BX1(BI) = COLS - 1
    IF BI = 6 THEN LET BX1(BI) = COLS - 2 - BW2
    LET BX0(BI) = BX1(BI) - BW
  END IF
  SET AREA COLOR CBTN
  IF HOVK = 1 AND HOVN = BI THEN SET AREA COLOR CHOV
  GRAPH AREA: BX0(BI), ROWS - 0.92; BX1(BI), ROWS - 0.92; BX1(BI), ROWS - 0.08; BX0(BI), ROWS - 0.08
  SET TEXT COLOR CTEXT
  GRAPH TEXT, AT BX0(BI) + 1, ROWS - 0.75: BLAB$(BI)
  IF BKEY$(BI) <> "" THEN
    SET TEXT COLOR CDIM
    GRAPH TEXT, AT BX0(BI) + 2 + LEN(BLAB$(BI)), ROWS - 0.75: BKEY$(BI)
  END IF
NEXT BI
RETURN

! ----------------------------------------------------------------------
! 9200  THE TABS — " name × " each (● instead of × while it has unsaved
!        changes), the one showing in the text's colour; then + for a new one.
! ----------------------------------------------------------------------
9200 REM ---- the tabs ----
SET AREA COLOR CBAR
GRAPH AREA: 0, ROWS - 2; COLS, ROWS - 2; COLS, ROWS - 1; 0, ROWS - 1
LET TNW = INT((COLS - 5) / NTABS) - 4   ! the room for each name
IF TNW > 24 THEN LET TNW = 24
IF TNW < 3 THEN LET TNW = 3
LET BXX = 0
FOR TI = 1 TO NTABS
  LET TN$ = FIT$(TABNAME$(TI), TNW)
  LET TABX0(TI) = BXX
  LET TABX1(TI) = BXX + LEN(TN$) + 4
  LET TCLX(TI) = TABX1(TI) - 2
  IF TI = CT THEN
    SET AREA COLOR 0
  ELSEIF HOVK >= 2 AND HOVK <= 3 AND HOVN = TI THEN
    SET AREA COLOR CHOV
  ELSE
    SET AREA COLOR CTAB
  END IF
  GRAPH AREA: TABX0(TI), ROWS - 2; TABX1(TI) - 0.1, ROWS - 2; TABX1(TI) - 0.1, ROWS - 1; TABX0(TI), ROWS - 1
  IF TI = CT AND HASMOUSE = 1 THEN ! a line in the accent colour along its top
    SET AREA COLOR CACC
    GRAPH AREA: TABX0(TI), ROWS - 1.1; TABX1(TI) - 0.1, ROWS - 1.1; TABX1(TI) - 0.1, ROWS - 1; TABX0(TI), ROWS - 1
  END IF
  SET TEXT COLOR CDIM
  IF TI = CT THEN SET TEXT COLOR CTEXT
  GRAPH TEXT, AT TABX0(TI) + 1, ROWS - 1.75: TN$
  LET G$ = "×"
  SET TEXT COLOR CDIM
  IF TCHG(TI) = 1 THEN
    LET G$ = "●"
    SET TEXT COLOR CACC
  END IF
  IF HOVK = 3 AND HOVN = TI THEN
    LET G$ = "×"
    SET TEXT COLOR CWARN
  END IF
  GRAPH TEXT, AT TCLX(TI), ROWS - 1.75: G$
  LET BXX = TABX1(TI)
NEXT TI
LET PLX0 = BXX + 1
LET PLX1 = BXX + 4
SET AREA COLOR CBAR
IF HOVK = 4 THEN SET AREA COLOR CHOV
GRAPH AREA: PLX0, ROWS - 1.92; PLX1, ROWS - 1.92; PLX1, ROWS - 1.08; PLX0, ROWS - 1.08
SET TEXT COLOR CDIM
GRAPH TEXT, AT PLX0 + 1, ROWS - 1.75: "+"
RETURN

! ----------------------------------------------------------------------
! 9300  THE SCROLL BAR, when there's more text than shows: the thumb from
!        THTOP down to THBOT, THH rows tall.
! ----------------------------------------------------------------------
9300 REM ---- the scroll bar ----
LET THTOP = -1
LET THBOT = -1
IF NL <= TROWS THEN RETURN
LET SBL = COLS - 0.75
LET SBR = COLS - 0.25
IF HASMOUSE = 0 THEN               ! (a character screen: the whole column)
  LET SBL = COLS - 1
  LET SBR = COLS
END IF
LET THH = MAX(1, TROWS * TROWS / NL)
LET THTOP = (ROWS - 2) - (TOPL - 1) / (MAXTOP - 1) * (TROWS - THH)
LET THBOT = THTOP - THH
SET AREA COLOR CLINE
GRAPH AREA: SBL, 1; SBR, 1; SBR, ROWS - 2; SBL, ROWS - 2
SET AREA COLOR CTHUMB
IF DRAG = 2 OR HOVK = 6 THEN SET AREA COLOR CHOV
GRAPH AREA: SBL, THBOT; SBR, THBOT; SBR, THTOP; SBL, THTOP
RETURN

! ----------------------------------------------------------------------
! 9350  THE STATUS BAR — the message (or Save as's name being typed), where the
!        cursor is, and the Tabs/Spaces button.
! ----------------------------------------------------------------------
9350 REM ---- the status bar ----
SET AREA COLOR CBAR
GRAPH AREA: 0, 0; COLS, 0; COLS, 1; 0, 1
LET TB$ = "Tabs: " & STR$(TABW)
IF TABSP = 1 THEN LET TB$ = "Spaces: " & STR$(TABW)
LET SBX1 = COLS - 1
LET SBX0 = SBX1 - LEN(TB$) - 2
SET AREA COLOR CBTN
IF HOVK = 7 THEN SET AREA COLOR CHOV
GRAPH AREA: SBX0, 0.08; SBX1, 0.08; SBX1, 0.92; SBX0, 0.92
SET TEXT COLOR CTEXT
GRAPH TEXT, AT SBX0 + 1, 0.25: TB$
LET R$ = "Ln " & STR$(CY) & ", Col " & STR$(DCOL(TX$(CT, CY), CX) + 1)
SET TEXT COLOR CDIM
GRAPH TEXT, AT SBX0 - 2 - LEN(R$), 0.25: R$
LET AV = SBX0 - LEN(R$) - 6        ! the room left of that
IF MODE = 1 AND CONFIRM <> 3 THEN
  SET TEXT COLOR CACC
  GRAPH TEXT, AT 1, 0.25: "Save as:"
  LET TXX = 10
  LET TAV = AV - 9
  LET TYB = 0.25
  GOSUB 9380
ELSE
  LET M$ = MSG$
  SET TEXT COLOR CDIM
  IF M$ = "" THEN LET M$ = HINT$
  IF CONFIRM > 0 OR MSGW = 1 THEN SET TEXT COLOR CWARN
  IF MSG$ <> "" AND CONFIRM = 0 AND MSGW = 0 THEN SET TEXT COLOR CTEXT
  GRAPH TEXT, AT 1, 0.25: FIT$(M$, AV)
END IF
RETURN

! 9380  TYPED$ with its cursor, at column TXX on baseline TYB, in TAV columns
!       (scrolled so the cursor shows).
9380 REM ---- the name being typed ----
LET LT$ = LEFT$(TYPED$, CURP)
LET RT$ = MID$(TYPED$, CURP + 1)
LET CUR$ = "|"
IF RT$ = "" THEN LET CUR$ = "_"
IF LEN(LT$) > TAV - 2 AND TAV > 3 THEN LET LT$ = "…" & RIGHT$(LT$, TAV - 3)
SET TEXT COLOR CTEXT
GRAPH TEXT, AT TXX, TYB: FIT$(LT$ & CUR$ & RT$, TAV)
RETURN

! ----------------------------------------------------------------------
! 9400  ONE LINE OF TEXT, line LI on screen row RK: the cursor's line shaded,
!        the selection, the line's number, the text (tabs drawn out to their
!        stops, a faint → at each), the cursor.
! ----------------------------------------------------------------------
9400 REM ---- one line ----
LET L$ = TX$(CT, LI)
LET YB = ROWS - 1 - RK             ! the row's bottom edge
! (fills end just inside a column, so on a character screen they cover whole cells
! and don't spill into the next one, over its letter)
IF LI = CY AND HASSEL = 0 THEN
  SET AREA COLOR CLINE
  GRAPH AREA: GUT, YB; SCX0 - 0.02, YB; SCX0 - 0.02, YB + 1; GUT, YB + 1
END IF
IF HASSEL = 1 AND LI >= SY AND LI <= EY THEN
  LET SC0 = 0
  IF LI = SY THEN LET SC0 = DCOL(L$, SX)
  IF LI = EY THEN
    LET SC1 = DCOL(L$, EX)
  ELSE
    LET SC1 = DCOL(L$, LEN(L$)) + 1      ! (its line break, as a column)
  END IF
  LET SC0 = MAX(SC0, LEFTC) - LEFTC
  LET SC1 = MIN(SC1, LEFTC + TW) - LEFTC
  IF SC1 > SC0 THEN
    SET AREA COLOR CSELB
    GRAPH AREA: GUT + SC0, YB; GUT + SC1 - 0.02, YB; GUT + SC1 - 0.02, YB + 1; GUT + SC0, YB + 1
  END IF
END IF
LET N$ = STR$(LI)
SET TEXT COLOR CDIM
IF LI = CY THEN SET TEXT COLOR CTEXT
GRAPH TEXT, AT GUT - 1 - LEN(N$), YB + 0.25: N$
! the text, eight columns at a time: each piece starts on its own column, so a
! font a shade wider or narrower than the cells doesn't drift along the line
LET E$ = EXPAND$(L$)
LET V$ = MID$(E$, LEFTC + 1, TW)
SET TEXT COLOR CTEXT
FOR CK = 1 TO LEN(V$) STEP 8
  LET C8$ = RTRIM$(MID$(V$, CK, 8))
  IF C8$ <> "" THEN GRAPH TEXT, AT GUT + CK - 1, YB + 0.25: C8$
NEXT CK
IF POS(L$, KTAB$) > 0 THEN
  SET TEXT COLOR CMARK
  LET TC0 = 0
  FOR TCI = 1 TO LEN(L$)
    IF TC0 >= LEFTC + TW THEN EXIT FOR
    IF MID$(L$, TCI, 1) = KTAB$ THEN
      IF TC0 >= LEFTC THEN GRAPH TEXT, AT GUT + TC0 - LEFTC, YB + 0.25: "→"
      LET TC0 = (INT(TC0 / TABW) + 1) * TABW
    ELSE
      LET TC0 = TC0 + 1
    END IF
  NEXT TCI
END IF
IF LI = CY AND MODE = 0 AND HELP = 0 THEN
  LET CC = DCOL(L$, CX) - LEFTC
  IF CC >= 0 AND CC < TW THEN
    IF HASMOUSE = 1 THEN
      IF BON = 1 THEN              ! a bar before the character
        SET AREA COLOR CCUR
        GRAPH AREA: GUT + CC - 0.06, YB + 0.06; GUT + CC + 0.1, YB + 0.06; GUT + CC + 0.1, YB + 0.94; GUT + CC - 0.06, YB + 0.94
      END IF
    ELSE
      ! a character screen: a block, with the character on it in the bar's colour
      SET AREA COLOR CCUR
      GRAPH AREA: GUT + CC, YB; GUT + CC + 0.98, YB; GUT + CC + 0.98, YB + 1; GUT + CC, YB + 1
      LET UC$ = MID$(E$, LEFTC + CC + 1, 1)
      IF UC$ <> "" AND UC$ <> " " THEN
        SET TEXT COLOR CBAR
        GRAPH TEXT, AT GUT + CC, YB + 0.25: UC$
      END IF
    END IF
  END IF
END IF
RETURN

! ----------------------------------------------------------------------
! 9500  THE OPEN BOX — the recent files to click (or pick with ↑ ↓), a name to
!        type, Open and Cancel. Its rows, from the top: OTOP, OH of them; the
!        first file on row OLR0; the buttons on row OBR.
! ----------------------------------------------------------------------
9500 REM ---- the Open box ----
LET OW = MIN(COLS - 16, 60)
LET ODX0 = INT((COLS - OW) / 2)
LET ODX1 = ODX0 + OW
LET ONSHOW = MIN(NRECENT, TROWS - 8)
IF ONSHOW < 0 THEN LET ONSHOW = 0
LET OLINES = MAX(ONSHOW, 1)
LET OH = OLINES + 7
LET OTOP = 2 + INT((TROWS - OH) / 2)
LET OYT = ROWS - OTOP              ! its top edge
SET AREA COLOR CBOX
GRAPH AREA: ODX0, OYT - OH; ODX1, OYT - OH; ODX1, OYT; ODX0, OYT
SET TEXT COLOR CTEXT
GRAPH TEXT, AT ODX0 + 2, OYT - 1.75: "Open a file"
SET TEXT COLOR CDIM
IF NRECENT = 0 THEN
  GRAPH TEXT, AT ODX0 + 2, OYT - 2.75: "Type its name (File → Program files adds one)"
ELSE
  GRAPH TEXT, AT ODX0 + 2, OYT - 2.75: "Recent files, or type a name"
END IF
LET OLR0 = OTOP + 3
FOR OI = 1 TO ONSHOW
  LET OYB = ROWS - 1 - (OLR0 + OI - 1)
  IF OI = OSEL OR (HOVK = 8 AND HOVN = OI) THEN
    SET AREA COLOR CHOV
    IF OI = OSEL THEN SET AREA COLOR CSELB
    GRAPH AREA: ODX0 + 1, OYB; ODX1 - 1, OYB; ODX1 - 1, OYB + 1; ODX0 + 1, OYB + 1
  END IF
  SET TEXT COLOR CTEXT
  GRAPH TEXT, AT ODX0 + 2, OYB + 0.25: FIT$(RECENT$(OI), OW - 4)
NEXT OI
LET ONR = OLR0 + OLINES            ! the name's row
LET OYB = ROWS - 1 - ONR
SET TEXT COLOR CDIM
GRAPH TEXT, AT ODX0 + 2, OYB + 0.25: "Name:"
SET AREA COLOR CBAR
GRAPH AREA: ODX0 + 8, OYB; ODX1 - 2, OYB; ODX1 - 2, OYB + 1; ODX0 + 8, OYB + 1
LET TXX = ODX0 + 9
LET TAV = OW - 12
LET TYB = OYB + 0.25
GOSUB 9380
LET OBR = ONR + 2                  ! the buttons' row
LET OYB = ROWS - 1 - OBR
LET OBX1(2) = ODX1 - 2
LET OBX0(2) = OBX1(2) - 8
LET OBX1(1) = OBX0(2) - 1
LET OBX0(1) = OBX1(1) - 6
FOR OI = 1 TO 2
  SET AREA COLOR CBTN
  IF HOVK = 9 AND HOVN = OI THEN SET AREA COLOR CHOV
  GRAPH AREA: OBX0(OI), OYB + 0.08; OBX1(OI), OYB + 0.08; OBX1(OI), OYB + 0.92; OBX0(OI), OYB + 0.92
NEXT OI
SET TEXT COLOR CTEXT
GRAPH TEXT, AT OBX0(1) + 1, OYB + 0.25: "Open"
GRAPH TEXT, AT OBX0(2) + 1, OYB + 0.25: "Cancel"
SET TEXT COLOR CDIM
GRAPH TEXT, AT ODX0 + 2, OYB + 0.25: FIT$("Enter opens · Esc cancels", OBX0(1) - ODX0 - 3)
RETURN

! ----------------------------------------------------------------------
! 9600  THE HELP BOX.
! ----------------------------------------------------------------------
9600 REM ---- help ----
LET HW = 0
FOR HI = 1 TO NHLP
  LET HW = MAX(HW, LEN(HLP$(HI)))
NEXT HI
LET HW = MIN(HW + 4, COLS - 2)
LET HN = MIN(NHLP, ROWS - 2)
LET HX0 = INT((COLS - HW) / 2)
LET HTOP = INT((ROWS - HN - 2) / 2)      ! its top row, from the top
SET AREA COLOR CBOX
GRAPH AREA: HX0, ROWS - HTOP - HN - 2; HX0 + HW, ROWS - HTOP - HN - 2; HX0 + HW, ROWS - HTOP; HX0, ROWS - HTOP
FOR HI = 1 TO HN
  SET TEXT COLOR CTEXT
  IF HI = 1 THEN SET TEXT COLOR CACC
  IF HI = HN THEN SET TEXT COLOR CDIM
  GRAPH TEXT, AT HX0 + 2, ROWS - 1 - (HTOP + HI) + 0.25: FIT$(HLP$(HI), HW - 4)
NEXT HI
RETURN

! ======================================================================
! 9700  WHAT'S UNDER THE POINTER (MX, MY): HITK, HITN — see HITK's LET above.
! ======================================================================
9700 REM ---- what's under the pointer ----
LET HITK = 0
LET HITN = 0
IF MX < 0 OR MX >= COLS OR MY < 0 OR MY >= ROWS OR HELP = 1 THEN RETURN
LET HK = ROWS - 1 - INT(MY)        ! its row, from the top
IF MODE = 2 THEN
  IF MX >= ODX0 AND MX < ODX1 AND HK >= OTOP AND HK < OTOP + OH THEN
    LET HITK = 10
    IF HK >= OLR0 AND HK < OLR0 + ONSHOW THEN
      LET HITK = 8
      LET HITN = HK - OLR0 + 1
    ELSEIF HK = OBR THEN
      FOR OI = 1 TO 2
        IF MX >= OBX0(OI) AND MX < OBX1(OI) THEN
          LET HITK = 9
          LET HITN = OI
        END IF
      NEXT OI
    END IF
  END IF
  RETURN
END IF
IF HK = 0 THEN
  FOR BI = 1 TO 7
    IF MX >= BX0(BI) AND MX < BX1(BI) THEN
      LET HITK = 1
      LET HITN = BI
    END IF
  NEXT BI
ELSEIF HK = 1 THEN
  FOR TI = 1 TO NTABS
    IF MX >= TABX0(TI) AND MX < TABX1(TI) THEN
      LET HITK = 2
      IF MX >= TCLX(TI) - 0.5 AND MX < TCLX(TI) + 1.5 THEN LET HITK = 3
      LET HITN = TI
    END IF
  NEXT TI
  IF MX >= PLX0 AND MX < PLX1 THEN LET HITK = 4
ELSEIF HK = ROWS - 1 THEN
  IF MX >= SBX0 AND MX < SBX1 THEN LET HITK = 7
ELSE
  LET HITK = 5
  IF MX >= SCX0 AND NL > TROWS THEN LET HITK = 6
END IF
RETURN
