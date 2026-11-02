! ui-gallery.bas — every part of ui.bas, the components library, on one screen: the    @category Graphics
! theme (dark or light), text in its sizes and fonts, buttons of each kind, badges and a
! progress bar, text wrapped and cut to fit, a text field to type in, boxes and their
! shadows, and a dialog over the dimmed screen. Click about; Q quits.
!
! Each panel ends with a line of the BASIC that draws it, to copy into a program of your
! own. Such a program starts LIBRARY "ui.bas" and DECLAREs the variables it uses; then
! each frame it calls UIBEGIN (the screen and the pointer), UICLEAR, and draws — a button
! by the IF that asks whether it was clicked. It's made for a window (the browser, or
! arcade-basic run with the desktop player); a terminal shows it roughly, a row of text at
! a time.

OPTION BASE 1
LIBRARY "ui.bas"
DECLARE PUBLIC UIW, UIH, UIMS, UICHANGED
DECLARE PUBLIC UCBG, UCSURFACE, UCRAISED, UCLINE, UCINK, UCMUTED, UCFAINT
DECLARE PUBLIC UCACCENT, UCONACCENT, UCGOOD, UCWARN, UCDANGER
DECLARE PUBLIC UISMALL, UIBODY, UITITLE, UITYPED$, UIMAXTYPED

DIM L$(16), P(16)          ! a wrapping's lines, and where in the text each starts
LET THEME$ = "DARK"
LET CLICKS = 0             ! the buttons: how many clicks, and the last one's name
LET LAST$ = ""
LET PROGRESS = 0.6
LET WRAPW = 240            ! how wide the paragraph is wrapped
LET NAME$ = ""             ! the text field's text, and 1 while it's being typed in
LET TYPING = 0
LET SAID$ = "Click the field, type, then Enter"
LET DIALOG = 0             ! 1 while the dialog is up
LET DIRTY = 1              ! the screen needs drawing again
LET QUIT = 0
LET K$ = ""
LET PX = 0                 ! the panel being drawn (1100): its left, top, width and height
LET PY = 0
LET PW = 0
LET PH = 0
LET UIMAXTYPED = 80
LET PARA$ = "Text is measured as it's drawn, so a paragraph wraps to whatever width it's given, in any font and at any size."

CALL UIBEGIN
IF UIMS < 0 THEN
  ! No mouse (piped input, or a terminal that doesn't report one): the gallery, once.
  GOSUB 1000
  LINE INPUT ""; K$
  STOP
END IF
DO
  DO
    LET K$ = INKEY$
    IF K$ = "" THEN EXIT DO
    GOSUB 500                      ! a key
    LET DIRTY = 1
  LOOP
  IF QUIT = 1 THEN EXIT DO
  CALL UIBEGIN
  IF DIRTY = 1 OR UICHANGED = 1 THEN
    LET DIRTY = 0
    GOSUB 1000                     ! (which may make it DIRTY again: a click changes what shows)
  END IF
  SLEEP 0.03
LOOP
END

! ----------------------------------------------------------------------
! 500  A KEY (K$): for the text field while it's typed in, the dialog while
!      it's up; else Q quits.
! ----------------------------------------------------------------------
500 REM ---- a key ----
IF TYPING = 1 THEN
  LET R = UIEDITKEY(K$)
  IF R = 1 THEN
    LET NAME$ = UITYPED$
    LET TYPING = 0
    LET SAID$ = "Enter: kept """ & NAME$ & """"
  ELSEIF R = -1 THEN
    LET TYPING = 0
    LET SAID$ = "Esc: left it as it was"
  END IF
ELSEIF DIALOG = 1 THEN
  IF K$ = CHR$(27) OR K$ = CHR$(13) THEN LET DIALOG = 0
ELSEIF UCASE$(K$) = "Q" THEN
  LET QUIT = 1
END IF
RETURN

! ======================================================================
! 1000  THE GALLERY — a title bar, six panels, a button for the dialog, and
!       the dialog. Behind the dialog nothing answers the pointer (UILOCK).
! ======================================================================
1000 REM ---- the gallery ----
CALL UICLEAR(UCBG)
CALL UILOCK(DIALOG)
CALL UITEXT(20, 30, "ui.bas gallery", UITITLE, "SANS BOLD", UCINK)
LET W = UIPILL(20 + UIWIDTH("ui.bas gallery", UITITLE, "SANS BOLD") + 12, 30, "LIBRARY ""ui.bas""", UISMALL, UCSURFACE, UCMUTED)
LET K1$ = "GHOST"
LET K2$ = "PRIMARY"
IF THEME$ = "DARK" THEN
  LET K1$ = "PRIMARY"
  LET K2$ = "GHOST"
END IF
IF UIBUTTON(UIW - 20 - 156, 14, 74, 32, "Dark", K1$) THEN
  LET THEME$ = "DARK"
  CALL UITHEME(THEME$)
  LET DIRTY = 1
END IF
IF UIBUTTON(UIW - 20 - 74, 14, 74, 32, "Light", K2$) THEN
  LET THEME$ = "LIGHT"
  CALL UITHEME(THEME$)
  LET DIRTY = 1
END IF

! --- buttons --------------------------------------------------------------
LET C = 1
LET R = 1
LET T$ = "BUTTONS"
LET CODE$ = "IF UIBUTTON(X,Y,W,H,""Save"",""PRIMARY"")"
GOSUB 1100
LET BW = (PW - 40) / 2
IF UIBUTTON(PX + 16, PY + 44, BW, 34, "Save", "PRIMARY") THEN
  LET LAST$ = "Save"
  GOSUB 1200
END IF
IF UIBUTTON(PX + 24 + BW, PY + 44, BW, 34, "Open", "") THEN
  LET LAST$ = "Open"
  GOSUB 1200
END IF
IF UIBUTTON(PX + 16, PY + 88, BW, 34, "Details", "GHOST") THEN
  LET LAST$ = "Details"
  GOSUB 1200
END IF
IF UIBUTTON(PX + 24 + BW, PY + 88, BW, 34, "Delete", "DANGER") THEN
  LET LAST$ = "Delete"
  GOSUB 1200
END IF
LET S$ = "Primary, raised, ghost and danger"
IF CLICKS > 0 THEN LET S$ = "Clicked " & LAST$ & "  ·  " & LTRIM$(STR$(CLICKS)) & " click(s) so far"
CALL UILABEL(PX + 16, PY + 132, PW - 32, 24, S$, UISMALL, "SANS", UCMUTED, "LEFT")

! --- text -----------------------------------------------------------------
LET C = 2
LET R = 1
LET T$ = "TEXT"
LET CODE$ = "CALL UITEXT(X,Y,S$,UIBODY,""SANS"",UCINK)"
GOSUB 1100
CALL UITEXT(PX + 16, PY + 56, "A title", UITITLE, "SANS BOLD", UCINK)
CALL UITEXT(PX + 16, PY + 86, "Body text, the usual size", UIBODY, "SANS", UCINK)
CALL UITEXT(PX + 16, PY + 110, "Small and muted, for what's less important", UISMALL, "SANS", UCMUTED)
CALL UITEXT(PX + 16, PY + 132, "Faint, for hints", UISMALL, "SANS", UCFAINT)
CALL UITEXT(PX + 16, PY + 158, "MONO for code: 10 PRINT", UIBODY, "MONO", UCACCENT)

! --- badges and progress --------------------------------------------------
LET C = 3
LET R = 1
LET T$ = "BADGES AND PROGRESS"
LET CODE$ = "CALL UIPROGRESS(X,Y,W,8,0.6,UCACCENT)"
GOSUB 1100
LET X = PX + 16
LET X = X + UIPILL(X, PY + 56, "New", UISMALL, UCACCENT, UCONACCENT) + 8
LET X = X + UIPILL(X, PY + 56, "Done", UISMALL, UCRAISED, UCGOOD) + 8
LET X = X + UIPILL(X, PY + 56, "Late", UISMALL, UCRAISED, UCWARN) + 8
LET X = X + UIPILL(X, PY + 56, "Blocked", UISMALL, UCDANGER, UCONACCENT) + 8
CALL UIPROGRESS(PX + 16, PY + 96, PW - 32, 8, PROGRESS, UCACCENT)
CALL UITEXT(PX + 16, PY + 128, LTRIM$(STR$(ROUND(PROGRESS * 100))) & "% done", UIBODY, "SANS BOLD", UCINK)
IF UIBUTTON(PX + PW - 16 - 84, PY + 112, 40, 32, "−", "") THEN
  LET PROGRESS = MAX(0, ROUND(PROGRESS * 10 - 1) / 10)
  LET DIRTY = 1
END IF
IF UIBUTTON(PX + PW - 16 - 40, PY + 112, 40, 32, "+", "") THEN
  LET PROGRESS = MIN(1, ROUND(PROGRESS * 10 + 1) / 10)
  LET DIRTY = 1
END IF

! --- wrapping and fitting -------------------------------------------------
LET C = 1
LET R = 2
LET T$ = "WRAPPED AND CUT TO FIT"
LET CODE$ = "N = UIWRAP(S$,W,UIBODY,""SANS"",L$,P)"
GOSUB 1100
LET N = UIWRAP(PARA$, WRAPW, UIBODY, "SANS", L$, P)
LET ROOM = 4                       ! lines it has room for; the last says "…" if there are more
FOR I = 1 TO MIN(N, ROOM)
  LET S$ = L$(I)
  IF I = ROOM AND N > ROOM THEN LET S$ = UIFIT$(MID$(PARA$, P(I)), WRAPW, UIBODY, "SANS")
  CALL UITEXT(PX + 16, PY + 50 + (I - 1) * 20, S$, UIBODY, "SANS", UCINK)
NEXT I
CALL UIBOX(PX + 16 + WRAPW + 2, PY + 40, 1, ROOM * 20, 0, UCLINE)   ! (where it wraps)
IF UIBUTTON(PX + 16, PY + 128, 32, 28, "◀", "") THEN
  LET WRAPW = MAX(80, WRAPW - 32)
  LET DIRTY = 1
END IF
CALL UILABEL(PX + 52, PY + 128, 96, 28, LTRIM$(STR$(WRAPW)) & " across", UISMALL, "SANS", UCMUTED, "CENTER")
IF UIBUTTON(PX + 152, PY + 128, 32, 28, "▶", "") THEN
  LET WRAPW = MIN(PW - 36, WRAPW + 32)
  LET DIRTY = 1
END IF
CALL UILABEL(PX + 16, PY + 160, WRAPW, 24, UIFIT$("And one line cut to fit, with an ellipsis where it stops", WRAPW, UISMALL, "SANS"), UISMALL, "SANS", UCMUTED, "LEFT")

! --- typing ---------------------------------------------------------------
LET C = 2
LET R = 2
LET T$ = "A TEXT FIELD"
LET CODE$ = "R = UIEDITKEY(K$)  ! 1 Enter, -1 Esc"
GOSUB 1100
LET FX = PX + 16
LET FY = PY + 42
LET FW = PW - 32
IF TYPING = 1 THEN
  LET NL = UIEDITLINES(FW - 24, UIBODY, "SANS")
ELSE
  LET NL = UIWRAP(NAME$, FW - 24, UIBODY, "SANS", L$, P)
END IF
LET FH = 20 + MIN(NL, 3) * 20
! a click in it starts typing; a click elsewhere keeps what's typed
IF UIMS = 2 AND DIALOG = 0 THEN
  IF UIIN(FX, FY, FW, FH) = 1 AND TYPING = 0 THEN
    LET TYPING = 1
    CALL UIEDITSTART(NAME$)
    LET SAID$ = "Typing: Enter keeps it, Esc leaves it"
    LET DIRTY = 1
  ELSEIF UIIN(FX, FY, FW, FH) = 0 AND TYPING = 1 THEN
    LET NAME$ = UITYPED$
    LET TYPING = 0
    LET SAID$ = "Clicked away: kept it"
    LET DIRTY = 1
  END IF
END IF
CALL UIBOX(FX, FY, FW, FH, 8, UCRAISED)
IF TYPING = 1 THEN
  CALL UIRING(FX, FY, FW, FH, 8, UCACCENT)
  CALL UIEDITDRAW(FX + 12, FY + 20, FW - 24, UIBODY, "SANS", UCINK, 20, "Your name")
ELSE
  CALL UIRING(FX, FY, FW, FH, 8, UCLINE)
  IF NAME$ = "" THEN
    CALL UITEXT(FX + 12, FY + 20, "Click here, and type", UIBODY, "SANS", UCFAINT)
  ELSE
    FOR I = 1 TO MIN(NL, 3)
      CALL UITEXT(FX + 12, FY + 20 + (I - 1) * 20, L$(I), UIBODY, "SANS", UCINK)
    NEXT I
  END IF
END IF
CALL UILABEL(FX, FY + FH + 8, FW, 24, SAID$, UISMALL, "SANS", UCMUTED, "LEFT")

! --- boxes and shadows ----------------------------------------------------
LET C = 3
LET R = 2
LET T$ = "BOXES AND SHADOWS"
LET CODE$ = "CALL UISHADOW(X,Y,W,H,10,8)"
GOSUB 1100
LET BW = (PW - 32 - 2 * 16) / 3
FOR I = 1 TO 3
  LET LIFT = 2
  IF I = 2 THEN LET LIFT = 8
  IF I = 3 THEN LET LIFT = 18
  LET X = PX + 16 + (I - 1) * (BW + 16)
  CALL UISHADOW(X, PY + 46, BW, 56, 10, LIFT)
  CALL UIBOX(X, PY + 46, BW, 56, 10, UCRAISED)
  CALL UILABEL(X, PY + 46, BW, 56, "lift " & LTRIM$(STR$(LIFT)), UISMALL, "SANS", UCMUTED, "CENTER")
NEXT I
LET X = PX + 16
CALL UIBOX(X, PY + 122, BW, 36, 0, UCLINE)
CALL UIRING(X + BW + 16, PY + 122, BW, 36, 10, UCACCENT)
CALL UIBOX(X + 2 * (BW + 16), PY + 122, BW, 36, 18, UCACCENT)
CALL UILABEL(X, PY + 160, BW, 20, "square", UISMALL, "SANS", UCFAINT, "CENTER")
CALL UILABEL(X + BW + 16, PY + 160, BW, 20, "an outline", UISMALL, "SANS", UCFAINT, "CENTER")
CALL UILABEL(X + 2 * (BW + 16), PY + 160, BW, 20, "a pill", UISMALL, "SANS", UCFAINT, "CENTER")

! --- the dialog -----------------------------------------------------------
IF UIBUTTON(20, UIH - 46, 150, 34, "Open a dialog", "PRIMARY") THEN
  LET DIALOG = 1
  LET DIRTY = 1
END IF
CALL UILABEL(190, UIH - 46, UIW - 210, 34, "Q quits  ·  every part is in examples/ui.bas", UISMALL, "SANS", UCFAINT, "RIGHT")
CALL UILOCK(0)
IF DIALOG = 1 THEN
  CALL UISCRIM
  LET DW = 440
  LET DH = 220
  LET DX = (UIW - DW) / 2
  LET DY = (UIH - DH) / 2
  CALL UIPANEL(DX, DY, DW, DH)
  CALL UITEXT(DX + 24, DY + 36, "A dialog", UITITLE, "SANS BOLD", UCINK)
  LET N = UIWRAP("The screen behind it is dimmed by a see-through colour (UISCRIM), and nothing behind it answers the pointer (UILOCK) until it's closed.", DW - 48, UIBODY, "SANS", L$, P)
  FOR I = 1 TO N
    CALL UITEXT(DX + 24, DY + 72 + (I - 1) * 21, L$(I), UIBODY, "SANS", UCMUTED)
  NEXT I
  IF UIBUTTON(DX + DW - 24 - 96, DY + DH - 24 - 34, 96, 34, "Close", "PRIMARY") THEN
    LET DIALOG = 0
    LET DIRTY = 1
  END IF
  IF UIBUTTON(DX + DW - 24 - 96 - 8 - 96, DY + DH - 24 - 34, 96, 34, "Cancel", "") THEN
    LET DIALOG = 0
    LET DIRTY = 1
  END IF
END IF
RETURN

! ----------------------------------------------------------------------
! 1100  A PANEL in column C, row R (of 3 x 2), titled T$, with the line of
!       code CODE$ along its bottom: its left, top, width and height are PX,
!       PY, PW and PH.
! ----------------------------------------------------------------------
1100 REM ---- a panel ----
LET PW = (UIW - 40 - 32) / 3
LET PH = (UIH - 60 - 56 - 16) / 2
LET PX = 20 + (C - 1) * (PW + 16)
LET PY = 60 + (R - 1) * (PH + 16)
CALL UIBOX(PX, PY, PW, PH, 12, UCSURFACE)
CALL UITEXT(PX + 16, PY + 22, T$, UISMALL, "SANS BOLD", UCMUTED)
CALL UILABEL(PX + 16, PY + PH - 30, PW - 32, 22, CODE$, UISMALL - 2, "MONO", UCFAINT, "LEFT")
RETURN

! 1200  A button was clicked.
1200 REM ---- a click ----
LET CLICKS = CLICKS + 1
LET DIRTY = 1
RETURN
