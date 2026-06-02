! invaders.bas — a small Space Invaders, the showcase for INKEY$ (non-blocking
! keyboard) and SLEEP (frame delay). Both are Microsoft BASIC extensions, not
! ISO/ECMA Full BASIC — see docs/conformance.md. Graphics use the ECMA-116 §13
! module, so it draws on the Braille console: it runs in the IDE, via
! `arcade-basic run examples/invaders.bas`, and as a standalone binary.
!
! Controls:   A / LEFT  move left     D / RIGHT  move right
!             SPACE  fire             Q  quit
!
! With no live keyboard (piped/headless) it plays itself to a loss — INKEY$ just
! returns "" — so it still terminates.

OPTION BASE 1

! ---- play field (logical units; the device stretches it to the terminal) ----
LET FW = 60
LET FH = 24

! ---- alien grid ----
LET NCOL = 5
LET NROW = 3
LET NA = NCOL * NROW       ! 15 aliens
DIM ALIVE(15)
LET COLSP = 8              ! column spacing
LET ROWSP = 3              ! row spacing
LET AW = 4                 ! alien width
LET AH = 1                 ! alien height

! ---- mutable state (pre-set so nothing is read before it's assigned) ----
LET PX = FW / 2            ! ship centre x
LET PY = 2                 ! ship row
LET SHMIN = 3
LET SHMAX = FW - 3
LET BLIVE = 0              ! bullet in flight?
LET BX = 0
LET BLY = 0
LET GX = 2                 ! alien grid left edge
LET GY = FH - 4            ! alien grid top row
LET DX = 2                 ! grid march step
LET FC = 0                 ! frame counter (drives the march cadence)
LET SC = 0                 ! score
LET NLEFT = NA             ! aliens remaining
LET STATE = 0              ! 0 playing, 1 win, 2 lose
LET QUIT = 0
LET MSG$ = "A/D or arrows move - SPACE fire - Q quit"
LET K$ = ""
LET CC = 0
LET RR = 0
LET AX = 0
LET AY = 0
LET STEPF = 0
LET GRIDW = 0
LET LOWY = 0
LET I = 0
LET HISCORE = 0            ! best score, persisted to invaders.score (RECTYPE INTERNAL)
LET PREVHI = 0            ! the high score as loaded (to detect a new record)
LET NEWREC = 0

FOR I = 1 TO NA
  LET ALIVE(I) = 1
NEXT I

GOSUB 5000                 ! load the saved high score
LET PREVHI = HISCORE

! ---- main loop ----
DO
  GOSUB 2000                       ! read input
  IF QUIT = 1 THEN
    GOSUB 5100                     ! persist the high score on quit too
    LET MSG$ = "Bye!  -  press Enter"
    GOSUB 1000
    EXIT DO
  END IF
  IF STATE = 0 THEN GOSUB 3000     ! advance the world
  GOSUB 1000                       ! draw
  IF STATE <> 0 THEN EXIT DO       ! win/lose: leave the final frame up
  SLEEP 0.05                       ! ~20 fps (also presents the frame)
LOOP
END

! ======================================================================
! 1000  RENDER
! ======================================================================
1000 REM ---- render ----
CLEAR
SET WINDOW 0, FW, 0, FH
! aliens (colour by row)
FOR I = 1 TO NA
  IF ALIVE(I) = 1 THEN
    LET CC = (I - 1) MOD NCOL
    LET RR = INT((I - 1) / NCOL)
    LET AX = GX + CC * COLSP
    LET AY = GY - RR * ROWSP
    IF RR = 0 THEN
      SET AREA COLOR 5
    ELSEIF RR = 1 THEN
      SET AREA COLOR 6
    ELSE
      SET AREA COLOR 3
    END IF
    GRAPH AREA: AX, AY; AX + AW, AY; AX + AW, AY + AH; AX, AY + AH
  END IF
NEXT I
! bullet
IF BLIVE = 1 THEN
  SET LINE COLOR 7
  GRAPH LINES: BX, BLY; BX, BLY + 1.4
END IF
! ship: body + gun
SET AREA COLOR 2
GRAPH AREA: PX - 2, 1; PX + 2, 1; PX + 2, 1.8; PX - 2, 1.8
SET LINE COLOR 2
GRAPH LINES: PX, 1.8; PX, 2.8
! HUD
SET TEXT COLOR 1
GRAPH TEXT, AT 1, FH - 1: "SCORE " & LTRIM$(STR$(SC)) & "    HI " & LTRIM$(STR$(HISCORE)) & "    ALIENS " & LTRIM$(STR$(NLEFT))
GRAPH TEXT, AT 1, 0: MSG$
RETURN

! ======================================================================
! 2000  INPUT — drain every pending key this frame (non-blocking).
! ======================================================================
2000 REM ---- input ----
LET QUIT = 0
DO
  LET K$ = INKEY$
  IF K$ = "" THEN EXIT DO
  IF K$ = "q" OR K$ = "Q" THEN
    LET QUIT = 1
  ELSEIF K$ = "a" OR K$ = "A" OR K$ = CHR$(0) & CHR$(75) THEN
    LET PX = PX - 2
  ELSEIF K$ = "d" OR K$ = "D" OR K$ = CHR$(0) & CHR$(77) THEN
    LET PX = PX + 2
  ELSEIF K$ = " " THEN
    IF BLIVE = 0 THEN
      LET BLIVE = 1
      LET BX = PX
      LET BLY = 3
    END IF
  END IF
LOOP
IF PX < SHMIN THEN LET PX = SHMIN
IF PX > SHMAX THEN LET PX = SHMAX
RETURN

! ======================================================================
! 3000  UPDATE — bullet, collisions, alien march, win/lose.
! ======================================================================
3000 REM ---- update ----
LET FC = FC + 1
! move the bullet
IF BLIVE = 1 THEN
  LET BLY = BLY + 1.4
  IF BLY > FH THEN LET BLIVE = 0
END IF
! bullet vs aliens
IF BLIVE = 1 THEN
  FOR I = 1 TO NA
    IF ALIVE(I) = 1 THEN
      LET CC = (I - 1) MOD NCOL
      LET RR = INT((I - 1) / NCOL)
      LET AX = GX + CC * COLSP
      LET AY = GY - RR * ROWSP
      IF BX >= AX AND BX <= AX + AW AND BLY >= AY - 0.5 AND BLY <= AY + AH + 1 THEN
        LET ALIVE(I) = 0
        LET BLIVE = 0
        LET SC = SC + 10
        LET NLEFT = NLEFT - 1
        IF SC > HISCORE THEN LET HISCORE = SC     ! HUD shows the live best
        IF SC > PREVHI THEN LET NEWREC = 1        ! beat the saved record
      END IF
    END IF
  NEXT I
END IF
! win?
IF NLEFT <= 0 THEN
  LET STATE = 1
  LET MSG$ = "YOU WIN!  score " & LTRIM$(STR$(SC))
  GOSUB 5200                       ! flag a new record + persist the high score
  RETURN
END IF
! march the grid every STEPF frames (faster as the swarm thins)
LET STEPF = 1 + INT(NLEFT / 3)
IF FC >= STEPF THEN
  LET FC = 0
  LET GRIDW = (NCOL - 1) * COLSP + AW
  LET GX = GX + DX
  IF GX < 2 OR GX + GRIDW > FW - 2 THEN
    LET DX = -DX
    LET GX = GX + DX               ! step back inside the field
    LET GY = GY - 1                ! and drop one row
  END IF
END IF
! lose? the lowest live alien reached the ship
LET LOWY = FH
FOR I = 1 TO NA
  IF ALIVE(I) = 1 THEN
    LET RR = INT((I - 1) / NCOL)
    LET AY = GY - RR * ROWSP
    IF AY < LOWY THEN LET LOWY = AY
  END IF
NEXT I
IF LOWY <= PY + 2 THEN
  LET STATE = 2
  LET MSG$ = "GAME OVER  score " & LTRIM$(STR$(SC))
  GOSUB 5200                       ! flag a new record + persist the high score
END IF
RETURN

! ======================================================================
! 5000  LOAD high score from invaders.score (INTERNAL/exact). Missing file
!        on the first run is fine — the handler just leaves HISCORE at 0.
! ======================================================================
5000 REM ---- load high score ----
LET HISCORE = 0
WHEN EXCEPTION IN
  OPEN #2: NAME "invaders.score", ACCESS INPUT, RECTYPE INTERNAL
  READ #2: HISCORE
  CLOSE #2
USE
  CLOSE #2                         ! safe even if it never opened
  LET HISCORE = 0
END WHEN
RETURN

! ======================================================================
! 5100  SAVE the high score (exact, via WRITE # to an INTERNAL file).
! ======================================================================
5100 REM ---- save high score ----
WHEN EXCEPTION IN
  OPEN #2: NAME "invaders.score", ACCESS OUTPUT, RECTYPE INTERNAL
  WRITE #2: HISCORE
  CLOSE #2
USE
  CLOSE #2
END WHEN
RETURN

! ======================================================================
! 5200  Finalize a game: flag a new record in the message, then persist.
! ======================================================================
5200 REM ---- finalize ----
IF NEWREC = 1 THEN LET MSG$ = MSG$ & "  -  NEW HIGH!"
GOSUB 5100
LET MSG$ = MSG$ & "  -  press Enter"
RETURN
