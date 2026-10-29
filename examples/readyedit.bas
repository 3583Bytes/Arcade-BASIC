! readyedit.bas — Ready Edit, a text editor for your own files: several open at once,   @category Graphics
! each in a tab, used with the mouse and the keyboard (named for the READY. prompt of
! the Commodore and Microsoft BASICs). Click to put the cursor anywhere, drag (or
! Shift+click) to select, double-click a word. The bar at the top has New, Open, Save,
! Save as and Close; each file gets a tab (click to show it, × to close it, + for a new
! one). The files it had open come back next time: it keeps their names in
! readyedit.files. (In the browser IDE files stay in the browser: File → Program
! files uploads one for Ready Edit to open, or downloads what you've written.)
!
! Tabs: the Tab key puts in a tab character, drawn out to the next stop of 4 with a
! faint →. With several lines selected Tab indents them all and Shift+Tab undents (a
! tab, or up to 4 spaces); Enter keeps the line's indent.
!
! Cut, Copy and Paste work between all the tabs; with nothing selected, Cut and Copy
! take the whole line, and Paste puts it back above the cursor's line. The clipboard
! is the editor's own — a program can't reach the computer's.
!
! Keys: every command is Esc, then a letter — S save, E save as, O open, N new, W close
! the tab, T and P the next and previous tab, B select, A select all, X cut, C copy,
! V paste, Q quit — and the bar at the bottom lists them. In a terminal Ctrl and the
! letter does it straight away (Ctrl+K copies: Ctrl+C never reaches a program). B starts
! selecting without a mouse or Shift: then the arrows, Home, End, PgUp and PgDn select as
! they move (↑ and ↓ by whole lines). Shift with the arrows, Home and End selects too
! (KEYDOWN sees Shift in a terminal as well as the browser); and the F keys still work:
! F2 save, F3 open, F4 save as, F7 new, F8 close, F9 next tab, Shift+Del cut, Shift+Ins
! paste. (The IDE keeps Ctrl and ⌘ for itself, so a program in the browser never sees
! them: hence Esc.)
!
! The screen is a grid of character cells, one window unit each (from ASK DEVICE SIZE,
! as in kanban.bas), and text is drawn in a monospace font, so the columns of the text
! line up with the cursor, the selection and the tab stops. INKEY$, KEYDOWN and
! GET MOUSE are Microsoft BASIC and Arcade BASIC extensions (see docs/conformance.md).

OPTION BASE 1

! ---- settings ----
LET TABW = 4            ! a tab stop every 4 columns
LET MAXTAB = 8          ! files open at once
LET MAXL = 3000         ! lines in a file
LET MAXREC = 20         ! files Open remembers
LET SESSION$ = "readyedit.files"

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
DIM CLIP$(3000)         ! the clipboard: CLIP$(1 .. NCLIP)
DIM BLAB$(9)           ! the menu bar's buttons, what they do (shown when the pointer's
DIM BTIP$(9)           ! on one), and where they were drawn
DIM BX0(9)
DIM BX1(9)
DIM OBX0(2)             ! the Open box's buttons
DIM OBX1(2)
DIM FKEY$(16)           ! the keys bar at the bottom: each key, what it does, its
DIM FLAB$(16)           ! command (8800's ACT), and where it was drawn ("/" starts
DIM FACT(16)            ! its second row)
DIM FX0(16)
DIM FX1(16)
DIM FROW(16)

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
LET CBAR = 16           ! the menu, tab and keys bars
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
LET CBOX = 29           ! the Open box
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
LET KINS$ = CHR$(0) & CHR$(82)
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
LET NCLIP = 0           ! the clipboard's lines (none yet)
LET CLIPLN = 0          ! 1: they're whole lines (cut or copied with nothing selected)
LET CLD$ = ""           ! 7065: what's on it, in words

! ---- what's going on ----
LET MSG$ = ""
LET MSGW = 0            ! 1: MSG$ is a warning
LET HINT$ = ""
LET HASMOUSE = 0        ! 1: a pointer from the start — the browser's screen, drawn finely.
                        !    0: a terminal (its mouse, if any, reports once it's used),
                        !    drawn in whole character cells, with Ctrl keys
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
LET TIPOFF = 0          ! 1: a click or key since it got there, so the bottom row shows
                        !    what came of it, not what the button does
LET CMDKEY = 0          ! 1 after Esc: the next key is a command's letter
LET MARK = 0            ! 1 while selecting from the keyboard (B): moving extends it
LET C$ = ""             ! 8050: a command's letter
LET NFOOT = 0
LET HITK = 0            ! 1 a menu button, 2 a tab, 3 a tab's ×, 4 +, 5 the text,
LET HITN = 0            ! 6 the scroll bar, 8 a file in the Open box,
                        ! 9 its buttons, 10 the rest of it, 11 a key in the keys bar
LET DRAG = 0            ! 1 selecting text, 2 the scroll bar
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
LET PLX0 = 0
LET PLX1 = 0
LET ODX0 = 0
LET ODX1 = 0
LET OTOP = 0
LET OH = 0
LET OLR0 = 0
LET ONSHOW = 0
LET OBR = 0

! ---- start: the files open last time, or a new one that says how this works ----
GET MOUSE MX, MY, MS
IF MS >= 0 THEN LET HASMOUSE = 1
FOR BI = 1 TO 9
  READ BLAB$(BI), BTIP$(BI)
NEXT BI
DATA "New", "A new file  ·  Esc N", "Open", "Open a file  ·  Esc O"
DATA "Save", "Save this file  ·  Esc S", "Save as", "Save it under a new name  ·  Esc E"
DATA "Close", "Close this tab  ·  Esc W"
DATA "Cut", "Cut what's selected, or the whole line  ·  Esc X"
DATA "Copy", "Copy what's selected, or the whole line  ·  Esc C"
DATA "Paste", "Paste what was cut or copied  ·  Esc V"
DATA "Quit", "Close Ready Edit  ·  Esc Q"
! the keys bar: in the browser the letters (after Esc), in a terminal Ctrl and the letter
DO
  READ K$, L$, A
  IF K$ = "*" THEN EXIT DO
  LET NFOOT = NFOOT + 1
  LET FKEY$(NFOOT) = K$
  LET FLAB$(NFOOT) = L$
  LET FACT(NFOOT) = A
  IF HASMOUSE = 0 AND K$ <> "/" THEN LET FKEY$(NFOOT) = "^" & K$
  IF HASMOUSE = 0 AND K$ = "C" THEN LET FKEY$(NFOOT) = "^K"
LOOP
DATA "S", "Save", 3, "E", "Save as", 4, "O", "Open", 2, "N", "New", 1, "W", "Close", 5
DATA "T", "Next tab", 10, "P", "Prev tab", 11, "/", "", 0
DATA "B", "Select", 12, "A", "Select all", 13, "X", "Cut", 6, "C", "Copy", 7
DATA "V", "Paste", 8, "Q", "Quit", 9, "*", "", 0

GOSUB 7700                         ! readyedit.files: the files open last time, and recent ones
FOR SI = 1 TO SNR
  LET OPN$ = SOPEN$(SI)
  GOSUB 7400
  IF LERR <> 0 THEN LET NFAIL = NFAIL + 1
NEXT SI
LET MSGW = 0
IF NTABS = 0 THEN
  GOSUB 7200
  GOSUB 7250                       ! a few words on how it works
  LET MSG$ = ""
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
  ! the mouse: 2 = pressed, 1 = held, 3 = released, 0 = up; -1 none (a terminal's
  ! counts once it has reported, which it does once it's moved or clicked)
  GET MOUSE MX, MY, MS
  IF MS >= 0 THEN GOSUB 8500
  IF HASMOUSE = 1 THEN
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
GRAPH TEXT, AT 2, INT(ROWS / 2) + 0.25: "Ready Edit has closed. Run it again to carry on where you were."
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
! 7060  COPY the selection onto the clipboard — or, with nothing selected, the
!        cursor's whole line (CLIPLN = 1). 7065 describes it in CLD$.
! 7070  CUT: copy it, then take it out.  7080  PASTE at the cursor (over the
!        selection); a whole line goes in above the cursor's line.
! ----------------------------------------------------------------------
7060 REM ---- copy ----
GOSUB 7000
IF HASSEL = 1 THEN
  LET CLIPLN = 0
  LET NCLIP = EY - SY + 1
  IF NCLIP = 1 THEN
    LET CLIP$(1) = MID$(TX$(CT, SY), SX + 1, EX - SX)
  ELSE
    LET CLIP$(1) = MID$(TX$(CT, SY), SX + 1)
    FOR CPI = 2 TO NCLIP - 1
      LET CLIP$(CPI) = TX$(CT, SY + CPI - 1)
    NEXT CPI
    LET CLIP$(NCLIP) = LEFT$(TX$(CT, EY), EX)
  END IF
ELSE
  LET CLIPLN = 1
  LET NCLIP = 1
  LET CLIP$(1) = TX$(CT, CY)
END IF
GOSUB 7065
RETURN

7065 REM ---- what's on the clipboard, in words ----
IF CLIPLN = 1 THEN
  LET CLD$ = "the line"
ELSEIF NCLIP > 1 THEN
  LET CLD$ = NLINES$(NCLIP)
  IF CLIP$(NCLIP) = "" THEN LET CLD$ = NLINES$(NCLIP - 1)   ! (to the start of a line: the ones before it)
ELSEIF LEN(CLIP$(1)) = 1 THEN
  LET CLD$ = "1 character"
ELSE
  LET CLD$ = STR$(LEN(CLIP$(1))) & " characters"
END IF
RETURN

7070 REM ---- cut ----
GOSUB 7060
IF HASSEL = 1 THEN
  GOSUB 7010
ELSE
  IF NL = 1 THEN                   ! the only line: it's left empty
    LET TX$(CT, 1) = ""
  ELSE
    LET DLY = CY
    GOSUB 7030
    IF CY > NL THEN LET CY = NL
  END IF
  LET CX = 0
  LET SELON = 0
  GOSUB 7090
END IF
LET MSG$ = "Cut " & CLD$
RETURN

7080 REM ---- paste ----
IF NCLIP = 0 THEN
  LET MSG$ = "Nothing to paste yet: Cut or Copy something first"
  RETURN
END IF
LET PADD = NCLIP - 1               ! the lines it adds
IF CLIPLN = 1 THEN LET PADD = NCLIP
IF NL + PADD > MAXL THEN
  LET MSG$ = "A file here can have up to " & STR$(MAXL) & " lines"
  LET MSGW = 1
  RETURN
END IF
GOSUB 7010                         ! (pasting over a selection replaces it)
LET SELON = 0
IF CLIPLN = 1 THEN
  FOR PSI = NL TO CY STEP -1
    LET TX$(CT, PSI + PADD) = TX$(CT, PSI)
  NEXT PSI
  FOR PSI = 1 TO NCLIP
    LET TX$(CT, CY + PSI - 1) = CLIP$(PSI)
  NEXT PSI
  LET NL = NL + PADD
  LET CY = CY + PADD               ! (the cursor stays with its line)
ELSE
  LET L$ = TX$(CT, CY)
  LET PRT$ = MID$(L$, CX + 1)      ! what comes after the cursor ends up after the paste
  IF NCLIP = 1 THEN
    LET TX$(CT, CY) = LEFT$(L$, CX) & CLIP$(1) & PRT$
    LET CX = CX + LEN(CLIP$(1))
  ELSE
    FOR PSI = NL TO CY + 1 STEP -1
      LET TX$(CT, PSI + PADD) = TX$(CT, PSI)
    NEXT PSI
    LET TX$(CT, CY) = LEFT$(L$, CX) & CLIP$(1)
    FOR PSI = 2 TO NCLIP - 1
      LET TX$(CT, CY + PSI - 1) = CLIP$(PSI)
    NEXT PSI
    LET TX$(CT, CY + PADD) = CLIP$(NCLIP) & PRT$
    LET NL = NL + PADD
    LET CY = CY + PADD
    LET CX = LEN(CLIP$(NCLIP))
  END IF
END IF
GOSUB 7090
GOSUB 7065
LET MSG$ = "Pasted " & CLD$
RETURN

! ----------------------------------------------------------------------
! 7090  CHANGED — the file has changes to save; the cursor's column is the one
!        ↑ and ↓ aim for now.
! ----------------------------------------------------------------------
7090 REM ---- changed ----
LET MARK = 0
LET TCHG(CT) = 1
LET WANTC = DCOL(TX$(CT, CY), CX)
LET FOLLOW = 1
LET MSG$ = ""
RETURN

! ----------------------------------------------------------------------
! 7095  SELECT (B): start selecting from the cursor — moving then extends the
!        selection, as Shift does — or stop.  7097  SELECT ALL.
! ----------------------------------------------------------------------
7095 REM ---- start or stop selecting ----
IF MARK = 1 THEN
  LET MARK = 0
  LET MSG$ = "Stopped selecting"
ELSE
  LET MARK = 1
  LET SELON = 1
  LET AY = CY
  LET AX = CX
END IF
RETURN

7097 REM ---- select all ----
LET MARK = 0
LET SELON = 1
LET AY = 1
LET AX = 0
LET CY = NL
LET CX = LEN(TX$(CT, NL))
LET WANTC = DCOL(TX$(CT, CY), CX)
LET MSG$ = "Selected all " & NLINES$(NL)
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
LET MARK = 0
GOSUB 7710                         ! (readyedit.files remembers which one shows)
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
DATA "Welcome to Ready Edit, a text editor written in Arcade BASIC."
DATA ""
DATA "Type anywhere. Click to move the cursor; drag to select."
DATA "The bar at the top opens and saves files, and each file gets a tab."
DATA ""
DATA "Tab characters show out to the next stop, with a faint arrow:"
DATA "^This line starts with a tab,"
DATA "^^and this one with two. Select lines and press Tab or Shift+Tab."
DATA ""
DATA "This file has no name yet: Save as gives it one. Files you open"
DATA "come back next time. The bar at the bottom lists the keys."
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
! 7700  READ readyedit.files: the files open last time (SOPEN$(1 .. SNR)), the
!        one that showed (SACT$), and the recent ones. 7710 WRITE it.
!        "READY-EDIT 1", the one showing, how many open, their names, how
!        many recent, their names — a line each.
! ======================================================================
7700 REM ---- read readyedit.files ----
LET SNR = 0
LET NRECENT = 0
WHEN EXCEPTION IN
  OPEN #3: NAME SESSION$, ACCESS INPUT
  LINE INPUT #3: RL$
  IF RL$ = "READY-EDIT 1" THEN
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

7710 REM ---- write readyedit.files ----
WHEN EXCEPTION IN
  OPEN #3: NAME SESSION$, ACCESS OUTPUT
  PRINT #3: "READY-EDIT 1"
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
IF K$ = KESC$ AND CMDKEY = 0 AND MODE = 0 THEN
  LET CMDKEY = 1                   ! the next key is a command (and a second go at
  IF CONFIRM = 0 THEN LET MSG$ = ""   !  closing or quitting still counts after it,
  RETURN                           !  its question still showing)
END IF
LET WASCONF = CONFIRM              ! (a second go at closing or quitting counts only
LET CONFIRM = 0                    !  if it comes next)
LET MSGW = 0
LET TIPOFF = 1
LET MSG$ = ""                      ! (a message shows until the next key or click)
IF MODE > 0 THEN
  GOSUB 8100                       ! a file's name being typed
  RETURN
END IF
LET SHIFT = 0
IF KEYDOWN("ShiftLeft") OR KEYDOWN("ShiftRight") THEN LET SHIFT = 1
LET FOLLOW = 1
! a command: Esc then its letter, Ctrl and the letter (a terminal), or an F key
LET C$ = ""
LET WASCMD = CMDKEY
LET CMDKEY = 0
IF WASCMD = 1 THEN
  IF K$ = KESC$ THEN               ! Esc Esc: no selection
    LET SELON = 0
    LET MARK = 0
    RETURN
  END IF
  IF LEN(K$) = 1 THEN LET C$ = UCASE$(K$)
ELSEIF LEN(K$) = 1 THEN
  LET KC = ORD(K$)                 ! (Ctrl+H, I, J and M are Backspace, Tab and Enter)
  IF KC >= 1 AND KC <= 26 AND KC <> 8 AND KC <> 9 AND KC <> 10 AND KC <> 13 THEN LET C$ = CHR$(KC + 64)
END IF
GOSUB 8050
IF ACT > 0 THEN
  GOSUB 8800
ELSEIF WASCMD = 1 AND LEN(K$) = 1 AND K$ >= " " THEN
  LET MSG$ = "Esc " & UCASE$(K$) & " isn't a command: see the bar above"
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
! 8050  WHICH COMMAND — ACT (8800) for the letter C$, or for an F key; 0 if none.
! ----------------------------------------------------------------------
8050 REM ---- which command ----
LET ACT = 0
IF C$ = "N" OR K$ = KF7$ THEN LET ACT = 1
IF C$ = "O" OR K$ = KF3$ THEN LET ACT = 2
IF C$ = "S" OR K$ = KF2$ THEN LET ACT = 3
IF C$ = "E" OR K$ = KF4$ THEN LET ACT = 4
IF C$ = "W" OR K$ = KF8$ THEN LET ACT = 5
IF C$ = "X" OR (K$ = KDEL$ AND SHIFT = 1) THEN LET ACT = 6
IF C$ = "C" OR C$ = "K" THEN LET ACT = 7
IF C$ = "V" OR (K$ = KINS$ AND SHIFT = 1) THEN LET ACT = 8
IF C$ = "Q" THEN LET ACT = 9
IF C$ = "T" OR (K$ = KF9$ AND SHIFT = 0) THEN LET ACT = 10
IF C$ = "P" OR (K$ = KF9$ AND SHIFT = 1) THEN LET ACT = 11
IF C$ = "B" THEN LET ACT = 12
IF C$ = "A" THEN LET ACT = 13
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
IF (SHIFT = 1 OR MARK = 1) AND (K$ = KUP$ OR K$ = KDN$) THEN
  GOSUB 8250                       ! selecting up or down: whole lines
  RETURN
END IF
IF SHIFT = 1 OR MARK = 1 THEN
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
! 8250  SELECTING UP OR DOWN — by whole lines. The first ↓ or ↑ selects the line
!        the cursor is on; each one after adds the line below or above (or, back
!        the other way, gives one up). The selection's other end, the anchor, is
!        kept on the edge of a line, so a selection made some other way becomes
!        whole lines too. The cursor goes to the start of a line — or the end of
!        the last one.
! ----------------------------------------------------------------------
8250 REM ---- select whole lines ----
IF HASSEL = 0 OR SY = EY THEN
  LET SELON = 1
  IF K$ = KDN$ THEN                ! from the start of this line down
    LET AY = CY
    LET AX = 0
    GOSUB 8270
  ELSE                             ! from the end of this line up
    IF CY < NL THEN
      LET AY = CY + 1
      LET AX = 0
    ELSE
      LET AY = CY
      LET AX = LEN(TX$(CT, CY))
    END IF
    LET CX = 0
  END IF
ELSE
  IF AY < CY OR (AY = CY AND AX < CX) THEN
    LET AX = 0                     ! the anchor above: to the start of its line
  ELSEIF AX > 0 THEN               ! below: to the start of the next (or the end)
    IF AY < NL THEN
      LET AY = AY + 1
      LET AX = 0
    ELSE
      LET AX = LEN(TX$(CT, AY))
    END IF
  END IF
  IF K$ = KDN$ THEN
    GOSUB 8270
  ELSEIF CX > 0 THEN
    LET CX = 0
  ELSEIF CY > 1 THEN
    LET CY = CY - 1
  END IF
END IF
LET WANTC = DCOL(TX$(CT, CY), CX)
RETURN

8270 REM ---- the cursor to the start of the next line (or the end of the last) ----
IF CY < NL THEN
  LET CY = CY + 1
  LET CX = 0
ELSE
  LET CX = LEN(TX$(CT, CY))
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
  LET TIPOFF = 0
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
LET TIPOFF = 1
LET CMDKEY = 0
LET MSG$ = ""
LET BLINK0 = FRAME
LET REDRAW = 1
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
IF HITK <> 5 THEN LET LASTCF = -100   ! (a double-click is two presses in a row on the text)
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
ELSEIF HITK = 11 THEN
  LET ACT = FACT(HITN)
  GOSUB 8800
END IF
RETURN

! ----------------------------------------------------------------------
! 8650  A PRESS ON THE TEXT — the cursor goes there (Shift: selecting to there),
!        and dragging selects; a double-click selects a word (8660).
! ----------------------------------------------------------------------
8650 REM ---- a press on the text ----
LET MARK = 0
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
IF PK > ROWS - 3 THEN LET PK = ROWS - 3
LET PL = TOPL + PK - 2
LET PCOL = INT(MX - GUT + LEFTC + 0.5)       ! (the nearer edge of the character...
IF HASMOUSE = 0 THEN LET PCOL = INT(MX - GUT + LEFTC)   ! ... a terminal says only which: before it)
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
  IF PK > ROWS - 3 AND TOPL < MAXTOP THEN LET TOPL = TOPL + 1
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
! 8800  A COMMAND, ACT: the menu's 1 New, 2 Open, 3 Save, 4 Save as, 5 Close,
!        6 Cut, 7 Copy, 8 Paste, 9 Quit; and 10 next tab, 11 previous tab,
!        12 select (from the keyboard), 13 select all.
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
  GOSUB 7070
ELSEIF ACT = 7 THEN
  GOSUB 7060
  LET MARK = 0
  LET MSG$ = "Copied " & CLD$
ELSEIF ACT = 8 THEN
  GOSUB 7080
ELSEIF ACT = 9 THEN
  GOSUB 7900
ELSEIF ACT = 10 OR ACT = 11 THEN
  LET NEWT = CT + 1
  IF ACT = 11 THEN LET NEWT = CT - 1
  IF NEWT > NTABS THEN LET NEWT = 1
  IF NEWT < 1 THEN LET NEWT = NTABS
  GOSUB 7150
ELSEIF ACT = 12 THEN
  GOSUB 7095
ELSE
  GOSUB 7097
END IF
LET FOLLOW = 1
RETURN

! ======================================================================
! 9000  DRAW the screen, measuring the layout as it goes: ROWS x COLS cells,
!        the menu bar on row 0 (counting from the top), the tabs on row 1, the
!        text from row 2 (TROWS rows: line numbers in GUT columns, then TW
!        columns of text, then the scroll bar), and the keys bar's two rows at
!        the bottom.
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
LET TROWS = ROWS - 4
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
GOSUB 9460                         ! the keys bar
IF MODE = 2 THEN GOSUB 9500
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
! 9150  THE MENU BAR — the file's buttons, then Cut Copy Paste, at the left;
!        Quit at the right. (The bottom row says what the one under the pointer
!        does, and its key.)
! ----------------------------------------------------------------------
9150 REM ---- the menu bar ----
SET AREA COLOR CBAR
GRAPH AREA: 0, ROWS - 1; COLS, ROWS - 1; COLS, ROWS; 0, ROWS
LET BXX = 1
FOR BI = 1 TO 9
  LET BW = LEN(BLAB$(BI)) + 2
  IF BI <= 8 THEN
    IF BI = 6 THEN LET BXX = BXX + 2   ! (a gap before Cut Copy Paste)
    LET BX0(BI) = BXX
    LET BX1(BI) = BXX + BW
    LET BXX = BX1(BI) + 1
  ELSE                             ! Quit at the right
    LET BX1(BI) = COLS - 1
    LET BX0(BI) = BX1(BI) - BW
  END IF
  SET AREA COLOR CBTN
  IF HOVK = 1 AND HOVN = BI THEN SET AREA COLOR CHOV
  GRAPH AREA: BX0(BI), ROWS - 0.92; BX1(BI), ROWS - 0.92; BX1(BI), ROWS - 0.08; BX0(BI), ROWS - 0.08
  SET TEXT COLOR CTEXT
  IF BI = 8 AND NCLIP = 0 THEN SET TEXT COLOR CDIM    ! nothing to paste yet
  GRAPH TEXT, AT BX0(BI) + 1, ROWS - 0.75: BLAB$(BI)
NEXT BI
RETURN

! ----------------------------------------------------------------------
! 9200  THE TABS — " name × " each (● instead of × while it has unsaved
!        changes), the one showing in the text's colour; then + for a new one.
! ----------------------------------------------------------------------
9200 REM ---- the tabs ----
SET AREA COLOR CBAR
GRAPH AREA: 0, ROWS - 2; COLS, ROWS - 2; COLS, ROWS - 1; 0, ROWS - 1
! at the right: where the cursor is
LET R$ = "Ln " & STR$(CY) & ", Col " & STR$(DCOL(TX$(CT, CY), CX) + 1)
SET TEXT COLOR CDIM
GRAPH TEXT, AT COLS - 1 - LEN(R$), ROWS - 1.75: R$
LET TNW = INT((COLS - LEN(R$) - 8) / NTABS) - 4   ! the room for each name
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
GRAPH AREA: SBL, 2; SBR, 2; SBR, ROWS - 2; SBL, ROWS - 2
SET AREA COLOR CTHUMB
IF DRAG = 2 OR HOVK = 6 THEN SET AREA COLOR CHOV
GRAPH AREA: SBL, THBOT; SBR, THBOT; SBR, THTOP; SBL, THTOP
RETURN

! ----------------------------------------------------------------------
! 9350  THE MESSAGE — on the bottom row, in place of the keys' second row, while
!        there's something to say: Save as's name being typed, what the menu
!        button under the pointer does, or MSG$ (until the next key or click).
!        SAYS = 1 if it said something.
! ----------------------------------------------------------------------
9350 REM ---- the message ----
LET SAYS = 0
LET AV = COLS - 2
IF MODE = 1 AND CONFIRM <> 3 THEN
  SET TEXT COLOR CACC
  GRAPH TEXT, AT 1, 0.25: "Save as:"
  LET TXX = 10
  LET TAV = AV - 9
  LET TYB = 0.25
  GOSUB 9380
  LET SAYS = 1
ELSEIF HOVK = 1 AND CONFIRM = 0 AND TIPOFF = 0 THEN   ! the pointer's on a menu button
  SET TEXT COLOR CDIM
  GRAPH TEXT, AT 1, 0.25: FIT$(BTIP$(HOVN), AV)
  LET SAYS = 1
ELSEIF MSG$ <> "" THEN
  SET TEXT COLOR CTEXT
  IF CONFIRM > 0 OR MSGW = 1 THEN SET TEXT COLOR CWARN
  GRAPH TEXT, AT 1, 0.25: FIT$(MSG$, AV)
  LET SAYS = 1
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
IF LI = CY AND MODE = 0 THEN
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
! 9460  THE KEYS BAR — the bottom two rows: each command's key and what it does
!        (Esc, then the letter; in a terminal ^ for Ctrl). Clicking one does it
!        too. A message (9350) takes the second row's place while it shows.
! ----------------------------------------------------------------------
9460 REM ---- the keys bar ----
SET AREA COLOR CBAR
GRAPH AREA: 0, 0; COLS, 0; COLS, 2; 0, 2
GOSUB 9350                         ! a message instead of the second row?
LET FXX = 1
IF HASMOUSE = 1 THEN
  SET TEXT COLOR CDIM
  IF CMDKEY = 1 THEN SET TEXT COLOR CCUR
  GRAPH TEXT, AT 1, 1.25: "Esc, then"
  LET FXX = 12
END IF
LET FXS = FXX                      ! (both rows start there)
LET FRW = ROWS - 2                 ! the row, from the top
FOR FI = 1 TO NFOOT
  LET FROW(FI) = -1                ! (not drawn: not clickable)
  IF FKEY$(FI) = "/" THEN
    LET FRW = ROWS - 1
    LET FXX = FXS
  ELSEIF FRW = ROWS - 2 OR SAYS = 0 THEN
    LET FW = LEN(FKEY$(FI)) + 1 + LEN(FLAB$(FI))
    LET FX0(FI) = FXX - 0.5
    LET FX1(FI) = FXX + FW + 0.5
    LET FROW(FI) = FRW
    LET FYB = ROWS - 1 - FRW
    IF HOVK = 11 AND HOVN = FI THEN
      SET AREA COLOR CHOV
      GRAPH AREA: FX0(FI), FYB + 0.08; FX1(FI), FYB + 0.08; FX1(FI), FYB + 0.92; FX0(FI), FYB + 0.92
    END IF
    SET TEXT COLOR CACC
    IF CMDKEY = 1 THEN SET TEXT COLOR CCUR
    GRAPH TEXT, AT FXX, FYB + 0.25: FKEY$(FI)
    SET TEXT COLOR CDIM
    IF FACT(FI) = 12 AND MARK = 1 THEN SET TEXT COLOR CTEXT
    GRAPH TEXT, AT FXX + LEN(FKEY$(FI)) + 1, FYB + 0.25: FLAB$(FI)
    LET FXX = FXX + FW + 2
  END IF
NEXT FI
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

! ======================================================================
! 9700  WHAT'S UNDER THE POINTER (MX, MY): HITK, HITN — see HITK's LET above.
! ======================================================================
9700 REM ---- what's under the pointer ----
LET HITK = 0
LET HITN = 0
IF MX < 0 OR MX >= COLS OR MY < 0 OR MY >= ROWS THEN RETURN
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
  FOR BI = 1 TO 9
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
ELSEIF HK >= ROWS - 2 THEN
  FOR FI = 1 TO NFOOT
    IF FKEY$(FI) <> "/" AND FROW(FI) = HK AND MX >= FX0(FI) AND MX < FX1(FI) THEN
      LET HITK = 11
      LET HITN = FI
    END IF
  NEXT FI
ELSE
  LET HITK = 5
  IF MX >= SCX0 AND NL > TROWS THEN LET HITK = 6
END IF
RETURN
