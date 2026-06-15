! snake.bas — the classic Snake, a companion to invaders.bas for showing off  @category Games
! INKEY$ (non-blocking keyboard) + SLEEP (frame delay) driving a real-time game
! loop on the ECMA-116 §13 graphics module. Both INKEY$ and SLEEP are Microsoft
! BASIC extensions, not ISO/ECMA Full BASIC — see docs/conformance.md. It draws
! on the Braille console: it runs in the IDE, via
! `arcade-basic run examples/snake.bas`, and as a standalone binary.
!
! Controls:   W A S D  or  arrow keys   steer        Q  quit
! After a game ends:  R  play again    Q  quit
!
! The high score is saved to snake.score (RECTYPE INTERNAL) and shown as HI.
! With no live keyboard (piped/headless) the snake runs straight into a wall —
! INKEY$ just returns "" — and the program exits without prompting, so it still
! terminates.

OPTION BASE 1

! ---- play field (logical units; the device stretches it to the terminal) ----
LET FW = 40                ! field width  (columns 0 .. FW-1)
LET FH = 24                ! field height (the top row is reserved for the HUD)
LET PLAYTOP = FH - 2       ! highest row the snake may occupy
LET PLAYRIGHT = FW - 1     ! rightmost column the snake may occupy
LET MAXLEN = FW * FH       ! the snake can never be longer than the whole board

! ---- snake body: X(1),Y(1) is the HEAD, X(SLEN),Y(SLEN) is the TAIL ----
DIM X(960)                 ! MAXLEN = 40*24 = 960 (DIM needs a literal bound)
DIM Y(960)

! ---- mutable state (pre-set so nothing is read before it's assigned) ----
LET SLEN = 0                ! current snake length
LET DX = 1                 ! heading: start moving right
LET DY = 0
LET PDX = 1                ! the heading actually applied last frame (anti-reverse)
LET PDY = 0
LET FX = 0                 ! food cell
LET FY = 0
LET SC = 0                 ! score
LET STATE = 0              ! 0 playing, 1 dead
LET QUIT = 0
LET MSG$ = "WASD or arrows steer - Q quit"
LET K$ = ""
LET NX = 0                 ! candidate new head
LET NY = 0
LET HIT = 0                ! self-collision flag
LET I = 0
LET TRIES = 0
LET HISCORE = 0            ! best score, persisted to snake.score (RECTYPE INTERNAL)
LET PREVHI = 0             ! high score to beat this game (to detect a new record)
LET NEWREC = 0
LET ANYKEY = 0             ! 1 once any key is seen — proves a keyboard is present
LET AGAIN = 0             ! 1 = play again, set by the game-over prompt

RANDOMIZE
GOSUB 5000                 ! load the saved high score (once)

! ---- session loop: each pass plays one game; R restarts ----
DO
  GOSUB 6000                       ! reset state for a new game
  ! ---- game loop ----
  DO
    GOSUB 2000                     ! read input
    IF QUIT = 1 THEN EXIT DO
    IF STATE = 0 THEN GOSUB 3000   ! advance the world
    GOSUB 1000                     ! draw
    IF STATE <> 0 THEN EXIT DO     ! dead: leave the final frame up
    SLEEP 0.1                      ! ~10 fps (also presents the frame)
  LOOP
  IF QUIT = 1 THEN
    GOSUB 5100                     ! persist the high score on quit too
    LET MSG$ = "Bye!  -  press Enter"
    GOSUB 1000
    EXIT DO
  END IF
  ! Game over. Only wait for a restart key if a keyboard is actually there
  ! (a headless or piped run never pressed a key — don't hang it).
  IF ANYKEY = 0 THEN EXIT DO
  GOSUB 7000                       ! prompt + poll: R play again / Q quit -> AGAIN
  IF AGAIN = 0 THEN EXIT DO
LOOP
END

! ======================================================================
! 1000  RENDER
! ======================================================================
1000 REM ---- render ----
CLEAR
SET WINDOW 0, FW, 0, FH
! arena border (gray box around the play field)
SET LINE COLOR 8
GRAPH LINES: 0, 0; PLAYRIGHT + 1, 0; PLAYRIGHT + 1, PLAYTOP + 1; 0, PLAYTOP + 1; 0, 0
! food
SET AREA COLOR 2
GRAPH AREA: FX, FY; FX + 1, FY; FX + 1, FY + 1; FX, FY + 1
! snake: head a brighter colour than the body
FOR I = 1 TO SLEN
  IF I = 1 THEN
    SET AREA COLOR 6
  ELSE
    SET AREA COLOR 3
  END IF
  GRAPH AREA: X(I), Y(I); X(I) + 1, Y(I); X(I) + 1, Y(I) + 1; X(I), Y(I) + 1
NEXT I
! HUD (in the reserved top row)
SET TEXT COLOR 1
GRAPH TEXT, AT 1, FH - 1: "SCORE " & LTRIM$(STR$(SC)) & "    HI " & LTRIM$(STR$(HISCORE)) & "    SLEN " & LTRIM$(STR$(SLEN))
GRAPH TEXT, AT 1, 0: MSG$
RETURN

! ======================================================================
! 2000  INPUT — drain every pending key this frame (non-blocking). A 180°
!        reversal is ignored: you can't double back into your own neck.
! ======================================================================
2000 REM ---- input ----
LET QUIT = 0
DO
  LET K$ = INKEY$
  IF K$ = "" THEN EXIT DO
  LET ANYKEY = 1                   ! a real key arrived — a keyboard is present
  IF K$ = "q" OR K$ = "Q" THEN
    LET QUIT = 1
  ELSEIF K$ = "a" OR K$ = "A" OR K$ = CHR$(0) & CHR$(75) THEN
    IF PDX <> 1 THEN
      LET DX = -1
      LET DY = 0
    END IF
  ELSEIF K$ = "d" OR K$ = "D" OR K$ = CHR$(0) & CHR$(77) THEN
    IF PDX <> -1 THEN
      LET DX = 1
      LET DY = 0
    END IF
  ELSEIF K$ = "w" OR K$ = "W" OR K$ = CHR$(0) & CHR$(72) THEN
    IF PDY <> -1 THEN
      LET DX = 0
      LET DY = 1
    END IF
  ELSEIF K$ = "s" OR K$ = "S" OR K$ = CHR$(0) & CHR$(80) THEN
    IF PDY <> 1 THEN
      LET DX = 0
      LET DY = -1
    END IF
  END IF
LOOP
RETURN

! ======================================================================
! 3000  UPDATE — advance the head, test walls / self / food, then grow or
!        slither (shift the body up to follow the head).
! ======================================================================
3000 REM ---- update ----
LET PDX = DX                       ! lock in the heading this frame moves on
LET PDY = DY
LET NX = X(1) + DX
LET NY = Y(1) + DY
! wall?
IF NX < 0 OR NX > PLAYRIGHT OR NY < 0 OR NY > PLAYTOP THEN
  LET STATE = 1
  LET MSG$ = "GAME OVER  score " & LTRIM$(STR$(SC))
  GOSUB 5200
  RETURN
END IF
! self? (the tail will vacate its cell this step, so it's not a real obstacle)
LET HIT = 0
FOR I = 1 TO SLEN - 1
  IF X(I) = NX AND Y(I) = NY THEN LET HIT = 1
NEXT I
IF HIT = 1 THEN
  LET STATE = 1
  LET MSG$ = "GAME OVER  score " & LTRIM$(STR$(SC))
  GOSUB 5200
  RETURN
END IF
! food? grow by one before the shift so the tail cell is duplicated, not dropped
IF NX = FX AND NY = FY THEN
  IF SLEN < MAXLEN THEN LET SLEN = SLEN + 1
  LET SC = SC + 10
  IF SC > HISCORE THEN LET HISCORE = SC      ! HUD shows the live best
  IF SC > PREVHI THEN LET NEWREC = 1         ! beat the saved record
  GOSUB 4000                                 ! drop new food
END IF
! every segment takes the place of the one ahead of it, then the head advances
FOR I = SLEN TO 2 STEP -1
  LET X(I) = X(I - 1)
  LET Y(I) = Y(I - 1)
NEXT I
LET X(1) = NX
LET Y(1) = NY
RETURN

! ======================================================================
! 4000  NEW FOOD — pick a random empty cell. With the board nearly full this
!        could reject many times; cap the tries and accept a rare overlap
!        rather than spin (you've all but won at that point anyway).
! ======================================================================
4000 REM ---- new food ----
LET TRIES = 0
DO
  LET FX = INT(RND * (PLAYRIGHT + 1))
  LET FY = INT(RND * (PLAYTOP + 1))
  LET HIT = 0
  FOR I = 1 TO SLEN
    IF X(I) = FX AND Y(I) = FY THEN LET HIT = 1
  NEXT I
  LET TRIES = TRIES + 1
  IF HIT = 0 OR TRIES > 200 THEN EXIT DO
LOOP
RETURN

! ======================================================================
! 5000  LOAD high score from snake.score (INTERNAL/exact). Missing file on
!        the first run is fine — the handler just leaves HISCORE at 0.
! ======================================================================
5000 REM ---- load high score ----
LET HISCORE = 0
WHEN EXCEPTION IN
  OPEN #2: NAME "snake.score", ACCESS INPUT, RECTYPE INTERNAL
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
  OPEN #2: NAME "snake.score", ACCESS OUTPUT, RECTYPE INTERNAL
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
IF ANYKEY = 1 THEN
  LET MSG$ = MSG$ & "  -  R play again, Q quit"
ELSE
  LET MSG$ = MSG$ & "  -  press Enter"
END IF
RETURN

! ======================================================================
! 6000  NEW GAME — reset everything that changes between games. HISCORE
!        persists across restarts; PREVHI is the score to beat this game.
!        The snake starts length 3, heading right, near the left-centre.
! ======================================================================
6000 REM ---- new game ----
LET SLEN = 3
LET X(1) = 6
LET Y(1) = INT(PLAYTOP / 2)
LET X(2) = 5
LET Y(2) = Y(1)
LET X(3) = 4
LET Y(3) = Y(1)
LET DX = 1
LET DY = 0
LET PDX = 1
LET PDY = 0
LET SC = 0
LET STATE = 0
LET QUIT = 0
LET NEWREC = 0
LET PREVHI = HISCORE
LET MSG$ = "WASD or arrows steer - Q quit"
GOSUB 4000                         ! place the first food
RETURN

! ======================================================================
! 7000  GAME-OVER PROMPT — keep the final frame up and wait for the player
!        to choose: R plays again, Q (or anything else here) quits.
! ======================================================================
7000 REM ---- restart prompt ----
LET AGAIN = 0
DO
  LET K$ = INKEY$
  IF K$ = "r" OR K$ = "R" THEN
    LET AGAIN = 1
    EXIT DO
  ELSEIF K$ = "q" OR K$ = "Q" THEN
    LET AGAIN = 0
    EXIT DO
  END IF
  SLEEP 0.05                       ! pace the wait + keep presenting the frame
LOOP
RETURN
