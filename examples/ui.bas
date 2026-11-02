! ui.bas — the parts an application's screen is made of, for a program that draws    @library
! (LIBRARY "ui.bas"): a theme of colours and text sizes; rounded boxes, outlines and
! shadows; text that's measured, cut to fit and wrapped; buttons, badges and a progress
! bar; a text field's typing; and a dialog over a dimmed screen. ui-gallery.bas shows every
! part, and kanban.bas is made with it.
!
! It's "immediate mode": each frame the program calls UIBEGIN (the screen and the
! pointer), then UICLEAR and draws everything — a button is drawn by the call that says
! whether it was clicked — and SLEEPs. Everything is in points, on a screen 960 across
! (UIW) and as tall as its proportions make it (UIH: 576 in the browser), (0, 0) at the top
! left and y going down, as an application's is. The colours, sizes and the pointer are
! PUBLIC variables a program DECLAREs:
!
!   LIBRARY "ui.bas"
!   DECLARE PUBLIC UIW, UIH, UCBG, UCINK, UIBODY
!   DO
!     CALL UIBEGIN
!     CALL UICLEAR(UCBG)
!     CALL UITEXT(20, 30, "Hello", UIBODY, "SANS", UCINK)
!     IF UIBUTTON(20, 60, 120, 36, "Click me", "PRIMARY") THEN PRINT "Clicked"
!     SLEEP 0.03
!   LOOP
!
! On a terminal (UICELLS = 1) text is one size, a character to a cell, boxes are square
! and see-through colours aren't drawn; a layout in rows UIROW high still fits.

MODULE UI
  ! ---- the screen and the pointer, as UIBEGIN finds them ------------------------------
  PUBLIC UIW, UIH                  ! the screen, in points
  PUBLIC UIMX, UIMY, UIMS          ! the pointer: where, and 0 up, 1 held, 2 just pressed, 3 just let go (-1: no mouse)
  PUBLIC UIDOUBLE                  ! 1 when this press is the second of a double-click
  PUBLIC UICHANGED                 ! 1 when the pointer or the screen's size changed since the last frame
  PUBLIC UICELLS, UIROW, UICOL     ! 1 on a screen of character cells; a row of text's height; a cell's width
  PUBLIC UIFRAME                   ! frames (UIBEGINs) so far

  ! ---- the theme: colours 240-254 (UITHEME sets them) and text sizes ---------------------
  PUBLIC UCBG, UCSURFACE, UCRAISED, UCHOVER, UCLINE   ! the background, a panel, a card, under the pointer, a rule
  PUBLIC UCINK, UCMUTED, UCFAINT                      ! text: the main, the secondary, the least
  PUBLIC UCACCENT, UCONACCENT, UCGOOD, UCWARN, UCDANGER
  PUBLIC UCSHADOW, UCSCRIM, UCLIGHTEN, UCDARKEN       ! see-through: a shadow, a dimmed screen, hover and press
  PUBLIC UISMALL, UIBODY, UITITLE, UIRADIUS

  ! ---- typing (UIEDITSTART, UIEDITKEY) --------------------------------------------------
  PUBLIC UITYPED$, UICARET         ! the text being typed, and the caret: after this many characters
  PUBLIC UIMAXTYPED

  SHARE UIPX, UIPY                 ! where the pointer was pressed
  SHARE UILOCKED                   ! 1 while what's drawn doesn't answer the pointer (UILOCK)
  SHARE UILASTF, UILASTX, UILASTY  ! the press before: its frame and place, for a double-click
  SHARE UIOX, UIOY, UIOS, UIDW, UIDH
  SHARE UIWL$(1 TO 64), UIWP(1 TO 64)              ! a wrapping's lines and where each starts
  SHARE UINC, UICX(1 TO 64), UICY(1 TO 64), UICW(1 TO 64), UICH(1 TO 64)   ! the controls drawn

  LET UCBG = 240
  LET UCSURFACE = 241
  LET UCRAISED = 242
  LET UCHOVER = 243
  LET UCLINE = 244
  LET UCINK = 245
  LET UCMUTED = 246
  LET UCFAINT = 247
  LET UCACCENT = 248
  LET UCONACCENT = 249
  LET UCGOOD = 250
  LET UCWARN = 251
  LET UCDANGER = 252
  LET UCSHADOW = 253
  LET UCSCRIM = 254
  LET UCLIGHTEN = 239
  LET UCDARKEN = 238
  LET UISMALL = 13
  LET UIBODY = 16
  LET UITITLE = 20
  LET UIRADIUS = 8
  LET UIW = 960
  LET UIH = 576
  LET UIROW = 24
  LET UICOL = 12
  LET UIMS = -1
  LET UILASTF = -100
  LET UIMAXTYPED = 300
  CALL UITHEME("DARK")

  ! ---- the theme ------------------------------------------------------------------------

  ! UITHEME("DARK") or UITHEME("LIGHT"): the colours. (A program can SET COLOR MIX any of
  ! them after, to make its own.)
  PUBLIC SUB UITHEME(T$)
    IF UCASE$(T$) = "LIGHT" THEN
      SET COLOR MIX (UCBG) 0.95, 0.96, 0.97
      SET COLOR MIX (UCSURFACE) 0.91, 0.92, 0.94
      SET COLOR MIX (UCRAISED) 1, 1, 1
      SET COLOR MIX (UCHOVER) 0.97, 0.97, 0.98
      SET COLOR MIX (UCLINE) 0.84, 0.85, 0.89
      SET COLOR MIX (UCINK) 0.11, 0.13, 0.19
      SET COLOR MIX (UCMUTED) 0.36, 0.40, 0.47
      SET COLOR MIX (UCFAINT) 0.60, 0.63, 0.69
      SET COLOR MIX (UCACCENT) 0.18, 0.44, 0.89
      SET COLOR MIX (UCGOOD) 0.18, 0.62, 0.42
      SET COLOR MIX (UCWARN) 0.79, 0.54, 0.08
      SET COLOR MIX (UCDANGER) 0.83, 0.24, 0.24
      SET COLOR MIX (UCSHADOW) 0.06, 0.09, 0.16, 0.07
      SET COLOR MIX (UCSCRIM) 0.06, 0.09, 0.16, 0.35
      SET COLOR MIX (UCLIGHTEN) 0, 0, 0, 0.04
      SET COLOR MIX (UCDARKEN) 0, 0, 0, 0.10
    ELSE
      SET COLOR MIX (UCBG) 0.06, 0.07, 0.09
      SET COLOR MIX (UCSURFACE) 0.10, 0.11, 0.14
      SET COLOR MIX (UCRAISED) 0.15, 0.16, 0.20
      SET COLOR MIX (UCHOVER) 0.19, 0.21, 0.26
      SET COLOR MIX (UCLINE) 0.22, 0.24, 0.30
      SET COLOR MIX (UCINK) 0.91, 0.92, 0.95
      SET COLOR MIX (UCMUTED) 0.61, 0.64, 0.71
      SET COLOR MIX (UCFAINT) 0.40, 0.43, 0.50
      SET COLOR MIX (UCACCENT) 0.36, 0.55, 0.96
      SET COLOR MIX (UCGOOD) 0.32, 0.78, 0.55
      SET COLOR MIX (UCWARN) 0.95, 0.72, 0.32
      SET COLOR MIX (UCDANGER) 0.95, 0.40, 0.40
      SET COLOR MIX (UCSHADOW) 0, 0, 0, 0.16
      SET COLOR MIX (UCSCRIM) 0.02, 0.03, 0.05, 0.62
      SET COLOR MIX (UCLIGHTEN) 1, 1, 1, 0.06
      SET COLOR MIX (UCDARKEN) 0, 0, 0, 0.18
    END IF
    SET COLOR MIX (UCONACCENT) 1, 1, 1
  END SUB

  ! ---- each frame -----------------------------------------------------------------------

  ! UIBEGIN: the screen (a window in points, y down) and the pointer.
  PUBLIC SUB UIBEGIN
    LET UIFRAME = UIFRAME + 1
    LET UICHANGED = 0
    ASK DEVICE SIZE DW, DH, DU$
    LET UIW = 960
    LET UIH = 576
    IF DW > 0 AND DH > 0 THEN LET UIH = 960 * DH / DW
    SET WINDOW 0, UIW, UIH, 0
    IF DW <> UIDW OR DH <> UIDH THEN
      LET UIDW = DW
      LET UIDH = DH
      LET UICHANGED = 1
      ! A screen of character cells draws every character the same width, whatever the font.
      SET TEXT HEIGHT 0
      SET TEXT FONT "SANS"
      LET UICELLS = 0
      IF TEXTWIDTH("iiii") = TEXTWIDTH("MMMM") THEN LET UICELLS = 1
      ASK TEXT HEIGHT UIROW
      LET UICOL = TEXTWIDTH("M")
    END IF
    GET MOUSE UIMX, UIMY, UIMS
    IF UIMX <> UIOX OR UIMY <> UIOY OR UIMS <> UIOS THEN LET UICHANGED = 1
    LET UIOX = UIMX
    LET UIOY = UIMY
    LET UIOS = UIMS
    LET UIDOUBLE = 0
    IF UIMS = 2 THEN
      LET UIPX = UIMX
      LET UIPY = UIMY
      IF UIFRAME - UILASTF <= 12 AND ABS(UIMX - UILASTX) < 8 AND ABS(UIMY - UILASTY) < 8 THEN
        LET UIDOUBLE = 1
        LET UILASTF = -100
      ELSE
        LET UILASTF = UIFRAME
        LET UILASTX = UIMX
        LET UILASTY = UIMY
      END IF
    END IF
  END SUB

  ! UICLEAR(C): start drawing a frame — the screen cleared to colour C.
  PUBLIC SUB UICLEAR(C)
    CLEAR
    LET UILOCKED = 0
    CALL UIBOX(0, 0, UIW, UIH, 0, C)
    LET UINC = 0
  END SUB

  ! ---- shapes ---------------------------------------------------------------------------

  ! A box X, Y (its top left), W across and H down, filled with colour C, its corners
  ! rounded to R.
  PUBLIC SUB UIBOX(X, Y, W, H, R, C)
    IF W > 0 AND H > 0 THEN
      SET AREA COLOR C
      IF R > 0 AND UICELLS = 0 THEN
        GRAPH AREA, ROUND R: X, Y; X + W, Y; X + W, Y + H; X, Y + H
      ELSE
        GRAPH AREA: X, Y; X + W, Y; X + W, Y + H; X, Y + H
      END IF
    END IF
  END SUB

  ! A box's outline, in colour C.
  PUBLIC SUB UIRING(X, Y, W, H, R, C)
    IF W > 0 AND H > 0 THEN
      SET LINE COLOR C
      IF R > 0 AND UICELLS = 0 THEN
        GRAPH LINES, ROUND R: X, Y; X + W, Y; X + W, Y + H; X, Y + H; X, Y
      ELSE
        GRAPH LINES: X, Y; X + W, Y; X + W, Y + H; X, Y + H; X, Y
      END IF
    END IF
  END SUB

  ! The shadow a box X, Y, W, H (corners R) casts, lifted LIFT points off the page: soft,
  ! a few see-through boxes each a little wider and lower. (None on a terminal.)
  PUBLIC SUB UISHADOW(X, Y, W, H, R, LIFT)
    IF UICELLS = 0 AND LIFT > 0 THEN
      FOR I = 1 TO 3
        LET D = LIFT * I / 3
        CALL UIBOX(X - D / 2, Y + D, W + D, H, R + D / 2, UCSHADOW)
      NEXT I
    END IF
  END SUB

  ! The whole screen dimmed, for a dialog over it.
  PUBLIC SUB UISCRIM
    CALL UIBOX(0, 0, UIW, UIH, 0, UCSCRIM)
  END SUB

  ! A dialog's panel: raised, with a shadow and an edge.
  PUBLIC SUB UIPANEL(X, Y, W, H)
    CALL UISHADOW(X, Y, W, H, 14, 18)
    CALL UIBOX(X, Y, W, H, 14, UCRAISED)
    CALL UIRING(X, Y, W, H, 14, UCLINE)
  END SUB

  ! A bar FRACTION (0-1) full, W across and H high, in colour C.
  PUBLIC SUB UIPROGRESS(X, Y, W, H, FRACTION, C)
    CALL UIBOX(X, Y, W, H, H / 2, UCLINE)
    LET F = MAX(0, MIN(1, FRACTION))
    IF F > 0 THEN CALL UIBOX(X, Y, MAX(H, W * F), H, H / 2, C)
  END SUB

  ! ---- text -----------------------------------------------------------------------------
  ! Text is SZ points high (UISMALL, UIBODY, UITITLE ...) in font F$: "SANS", "SANS BOLD",
  ! "MONO" or "MONO BOLD".

  ! Text from X, its middle at Y.
  PUBLIC SUB UITEXT(X, Y, S$, SZ, F$, C)
    CALL UIFONT(SZ, F$)
    SET TEXT COLOR C
    SET TEXT JUSTIFY "LEFT", "HALF"
    GRAPH TEXT, AT X, Y: S$
  END SUB

  ! Text in a box X, Y, W, H: its middle in the box's, from its left (ALIGN$ "LEFT"),
  ! centred ("CENTER") or to its right ("RIGHT"); cut to fit, with "…".
  PUBLIC SUB UILABEL(X, Y, W, H, S$, SZ, F$, C, ALIGN$)
    LET T$ = UIFIT$(S$, W, SZ, F$)
    SET TEXT COLOR C
    LET A$ = UCASE$(ALIGN$)
    IF A$ = "CENTER" THEN
      SET TEXT JUSTIFY "CENTER", "HALF"
      GRAPH TEXT, AT X + W / 2, Y + H / 2: T$
    ELSEIF A$ = "RIGHT" THEN
      SET TEXT JUSTIFY "RIGHT", "HALF"
      GRAPH TEXT, AT X + W, Y + H / 2: T$
    ELSE
      SET TEXT JUSTIFY "LEFT", "HALF"
      GRAPH TEXT, AT X, Y + H / 2: T$
    END IF
  END SUB

  ! How wide S$ is.
  PUBLIC FUNCTION UIWIDTH(S$, SZ, F$)
    CALL UIFONT(SZ, F$)
    UIWIDTH = TEXTWIDTH(S$)
  END FUNCTION

  ! S$ cut to fit W, ending in "…" when it doesn't.
  PUBLIC FUNCTION UIFIT$(S$, W, SZ, F$)
    CALL UIFONT(SZ, F$)
    IF TEXTWIDTH(S$) <= W THEN
      UIFIT$ = S$
    ELSE
      LET LO = 0                         ! the most characters that fit before the "…"
      LET HI = LEN(S$) - 1
      DO WHILE LO < HI
        LET M = INT((LO + HI + 1) / 2)
        IF TEXTWIDTH(RTRIM$(LEFT$(S$, M)) & "…") <= W THEN
          LET LO = M
        ELSE
          LET HI = M - 1
        END IF
      LOOP
      IF LO = 0 AND TEXTWIDTH("…") > W THEN
        UIFIT$ = ""
      ELSE
        UIFIT$ = RTRIM$(LEFT$(S$, LO)) & "…"
      END IF
    END IF
  END FUNCTION

  ! S$ broken into lines no wider than W, at spaces (a word too long for a line, wherever
  ! it has to be): the lines in L$(1 ...), and where in S$ each starts in P(1 ...). How
  ! many lines — at least one, and no more than L$() has room for.
  PUBLIC FUNCTION UIWRAP(S$, W, SZ, F$, L$(), P())
    CALL UIFONT(SZ, F$)
    LET N = 0
    LET MOST = MIN(UBOUND(L$), UBOUND(P))
    LET I = 1
    LET CUR$ = ""
    LET STARTS = 1
    DO WHILE I <= LEN(S$) AND N < MOST
      IF MID$(S$, I, 1) = " " THEN
        LET I = I + 1                    ! (spaces between words: the line's own, or the break)
      ELSE
        LET J = POS(S$ & " ", " ", I)    ! the word: I to J - 1
        LET WORD$ = MID$(S$, I, J - I)
        IF CUR$ = "" THEN
          LET TRY$ = WORD$
        ELSE
          LET TRY$ = CUR$ & " " & WORD$
        END IF
        IF TEXTWIDTH(TRY$) <= W THEN
          IF CUR$ = "" THEN LET STARTS = I
          LET CUR$ = TRY$
          LET I = J
        ELSEIF CUR$ <> "" THEN
          LET N = N + 1                  ! the line is full
          LET L$(N) = CUR$
          LET P(N) = STARTS
          LET CUR$ = ""
        ELSE
          LET K = 1                      ! a word wider than a line: as much as fits
          DO WHILE K < LEN(WORD$) AND TEXTWIDTH(LEFT$(WORD$, K + 1)) <= W
            LET K = K + 1
          LOOP
          LET N = N + 1
          LET L$(N) = LEFT$(WORD$, K)
          LET P(N) = I
          LET I = I + K
        END IF
      END IF
    LOOP
    IF CUR$ <> "" AND N < MOST THEN
      LET N = N + 1
      LET L$(N) = CUR$
      LET P(N) = STARTS
    END IF
    IF N = 0 THEN
      LET N = 1
      LET L$(1) = ""
      LET P(1) = 1
    END IF
    UIWRAP = N
  END FUNCTION

  ! The text size and font, for TEXTWIDTH and GRAPH TEXT.
  SUB UIFONT(SZ, F$)
    SET TEXT HEIGHT SZ
    SET TEXT FONT F$
  END SUB

  ! ---- the pointer ----------------------------------------------------------------------

  ! Is the pointer in the box X, Y, W, H?
  PUBLIC FUNCTION UIIN(X, Y, W, H)
    UIIN = 0
    IF UILOCKED = 0 AND UIMS >= 0 AND UIMX >= X AND UIMX < X + W AND UIMY >= Y AND UIMY < Y + H THEN UIIN = 1
  END FUNCTION

  ! Was the pointer pressed in the box X, Y, W, H (and is it still held, or just let go)?
  PUBLIC FUNCTION UIPRESSEDIN(X, Y, W, H)
    UIPRESSEDIN = 0
    IF UILOCKED = 0 AND UIMS >= 1 AND UIPX >= X AND UIPX < X + W AND UIPY >= Y AND UIPY < Y + H THEN UIPRESSEDIN = 1
  END FUNCTION

  ! UILOCK(1): what's drawn from now on doesn't answer the pointer — buttons don't light up
  ! or click, UIIN says no — until UILOCK(0). For the screen behind a dialog: lock, draw
  ! it, unlock, draw the dialog. (UICLEAR unlocks.)
  PUBLIC SUB UILOCK(L)
    LET UILOCKED = L
  END SUB

  ! Forget this press: it's done its work (closed a dialog, say), so letting go of it clicks
  ! nothing.
  PUBLIC SUB UIFORGETPRESS
    LET UIPX = -1E9
    LET UIPY = -1E9
  END SUB

  ! Was a press on one of the controls (buttons) drawn last time? — so a program's own
  ! clicking can leave it to them.
  PUBLIC FUNCTION UIONCONTROL
    UIONCONTROL = 0
    FOR I = 1 TO UINC
      IF UIPX >= UICX(I) AND UIPX < UICX(I) + UICW(I) AND UIPY >= UICY(I) AND UIPY < UICY(I) + UICH(I) THEN UIONCONTROL = 1
    NEXT I
  END FUNCTION

  ! ---- controls -------------------------------------------------------------------------

  ! A button X, Y, W, H with LABEL$ on it, KIND$ "PRIMARY" (the accent colour), "GHOST"
  ! (just its label until the pointer's on it), "DANGER", or "" (raised); lighter under the
  ! pointer, darker while pressed. 1 when it's clicked: pressed, and let go, on it.
  PUBLIC FUNCTION UIBUTTON(X, Y, W, H, LABEL$, KIND$)
    IF UINC < 64 THEN
      LET UINC = UINC + 1
      LET UICX(UINC) = X
      LET UICY(UINC) = Y
      LET UICW(UINC) = W
      LET UICH(UINC) = H
    END IF
    LET OVER = UIIN(X, Y, W, H)
    LET K$ = UCASE$(KIND$)
    LET INK = UCINK
    LET FILL = UCRAISED
    IF K$ = "PRIMARY" THEN
      LET FILL = UCACCENT
      LET INK = UCONACCENT
    ELSEIF K$ = "DANGER" THEN
      LET FILL = UCDANGER
      LET INK = UCONACCENT
    ELSEIF K$ = "GHOST" THEN
      LET FILL = -1
      LET INK = UCMUTED
      IF OVER = 1 THEN
        LET FILL = UCHOVER
        LET INK = UCINK
      END IF
    END IF
    LET R = MIN(UIRADIUS, H / 2)
    IF FILL >= 0 THEN CALL UIBOX(X, Y, W, H, R, FILL)
    IF OVER = 1 AND K$ <> "GHOST" THEN CALL UIBOX(X, Y, W, H, R, UCLIGHTEN)
    IF OVER = 1 AND UIPRESSEDIN(X, Y, W, H) = 1 THEN CALL UIBOX(X, Y, W, H, R, UCDARKEN)
    LET F$ = "SANS BOLD"
    IF K$ = "GHOST" THEN LET F$ = "SANS"
    LET PADX = MIN(14, W / 6)
    IF UICELLS = 1 THEN LET PADX = 0
    LET ALIGN$ = "CENTER"
    IF K$ = "GHOST" THEN LET ALIGN$ = "LEFT"
    CALL UILABEL(X + PADX, Y, W - 2 * PADX, H, LABEL$, UIBODY - 1, F$, INK, ALIGN$)
    UIBUTTON = 0
    IF UIMS = 3 AND OVER = 1 AND UIPRESSEDIN(X, Y, W, H) = 1 THEN UIBUTTON = 1
  END FUNCTION

  ! A badge: S$ on a pill of colour BG, from X with its middle at Y, in colour FG. How wide
  ! it is.
  PUBLIC FUNCTION UIPILL(X, Y, S$, SZ, BG, FG)
    LET H = SZ + 8
    LET W = UIWIDTH(S$, SZ, "SANS BOLD") + 14
    IF UICELLS = 1 THEN
      LET H = UIROW
      LET W = UIWIDTH(S$, SZ, "SANS BOLD") + 2 * UICOL
    END IF
    CALL UIBOX(X, Y - H / 2, W, H, H / 2, BG)
    CALL UILABEL(X, Y - H / 2, W, H, S$, SZ, "SANS BOLD", FG, "CENTER")
    UIPILL = W
  END FUNCTION

  ! ---- typing ---------------------------------------------------------------------------
  ! UIEDITSTART(S$) starts typing S$ (the caret at its end); UIEDITKEY(K$) takes each key
  ! (INKEY$'s); UIEDITLINES and UIEDITDRAW draw it, wrapped, with the caret.

  PUBLIC SUB UIEDITSTART(S$)
    LET UITYPED$ = S$
    LET UICARET = LEN(S$)
  END SUB

  ! A key: it edits UITYPED$ at the caret — a character goes in, Backspace and Delete take
  ! one out, ← → Home End move. 1 when it's Enter (done), -1 Esc (cancel), else 0.
  PUBLIC FUNCTION UIEDITKEY(K$)
    UIEDITKEY = 0
    IF K$ = CHR$(13) OR K$ = CHR$(10) THEN
      UIEDITKEY = 1
    ELSEIF K$ = CHR$(27) THEN
      UIEDITKEY = -1
    ELSEIF K$ = CHR$(8) OR K$ = CHR$(127) THEN
      IF UICARET > 0 THEN
        LET UITYPED$ = LEFT$(UITYPED$, UICARET - 1) & MID$(UITYPED$, UICARET + 1)
        LET UICARET = UICARET - 1
      END IF
    ELSEIF K$ = CHR$(0) & CHR$(83) THEN
      IF UICARET < LEN(UITYPED$) THEN LET UITYPED$ = LEFT$(UITYPED$, UICARET) & MID$(UITYPED$, UICARET + 2)
    ELSEIF K$ = CHR$(0) & CHR$(75) THEN
      IF UICARET > 0 THEN LET UICARET = UICARET - 1
    ELSEIF K$ = CHR$(0) & CHR$(77) THEN
      IF UICARET < LEN(UITYPED$) THEN LET UICARET = UICARET + 1
    ELSEIF K$ = CHR$(0) & CHR$(71) THEN
      LET UICARET = 0
    ELSEIF K$ = CHR$(0) & CHR$(79) THEN
      LET UICARET = LEN(UITYPED$)
    ELSEIF LEN(K$) = 1 AND K$ >= " " AND K$ <> CHR$(127) AND LEN(UITYPED$) < UIMAXTYPED THEN
      LET UITYPED$ = LEFT$(UITYPED$, UICARET) & K$ & MID$(UITYPED$, UICARET + 1)
      LET UICARET = UICARET + 1
    END IF
  END FUNCTION

  ! How many lines what's typed takes, wrapped to W.
  PUBLIC FUNCTION UIEDITLINES(W, SZ, F$)
    UIEDITLINES = UIWRAP(UITYPED$, W, SZ, F$, UIWL$, UIWP)
  END FUNCTION

  ! What's typed, wrapped to W, from X with its first line's middle at Y and the next ones
  ! LH below; and the caret, in the accent colour. (Off a terminal, PLACEHOLDER$ shows,
  ! faint, while there's nothing typed.)
  PUBLIC SUB UIEDITDRAW(X, Y, W, SZ, F$, C, LH, PLACEHOLDER$)
    LET N = UIWRAP(UITYPED$, W, SZ, F$, UIWL$, UIWP)
    ! the line the caret's on: the last one starting at or before it
    LET CL = 1
    FOR I = 2 TO N
      IF UIWP(I) - 1 <= UICARET THEN LET CL = I
    NEXT I
    LET CX = X + UIWIDTH(MID$(UITYPED$, UIWP(CL), UICARET - UIWP(CL) + 1), SZ, F$)
    IF UICELLS = 1 THEN
      CALL UIBOX(CX, Y + (CL - 1) * LH - UIROW / 2, UICOL, UIROW, 0, UCACCENT)
    ELSE
      CALL UIBOX(CX, Y + (CL - 1) * LH - SZ * 0.6, 2, SZ * 1.2, 0, UCACCENT)
    END IF
    IF UITYPED$ = "" THEN
      IF UICELLS = 0 THEN CALL UITEXT(X + 4, Y, PLACEHOLDER$, SZ, F$, UCFAINT)
    ELSE
      FOR I = 1 TO N
        CALL UITEXT(X, Y + (I - 1) * LH, UIWL$(I), SZ, F$, C)
      NEXT I
    END IF
  END SUB
END MODULE
