! breakout.bas — a small Breakout, the third real-time arcade game alongside    @category Games
! invaders.bas and snake.bas. The new trick here is ball physics: the ball
! reflects off the walls and bricks, and the paddle steers it — where it strikes
! the paddle sets the rebound angle. INKEY$ (non-blocking keyboard) + SLEEP
! (frame delay) drive the loop; graphics use the ECMA-116 §13 module, so it draws
! on the Braille console: in the IDE, via `arcade-basic run examples/breakout.bas`,
! and as a standalone binary. INKEY$/SLEEP are Microsoft BASIC extensions, not
! ISO/ECMA Full BASIC — see docs/conformance.md.
!
! Controls:   A / LEFT  move left     D / RIGHT  move right     Q  quit
! After a game ends:  R  play again    Q  quit
!
! The high score is saved to breakout.score (RECTYPE INTERNAL) and shown as HI.
! With no live keyboard (piped/headless) the paddle stays put — INKEY$ returns ""
! — the ball eventually slips past it, lives run out, and the program exits
! without prompting, so it still terminates (a frame cap backstops a stray orbit).

OPTION BASE 1

! ---- play field (logical units; the device stretches it to the terminal) ----
LET FW = 60                ! field width
LET FH = 30                ! field height (top row reserved for the HUD)

! ---- brick grid ----
LET NCOL = 10
LET NROW = 5
LET NBRICK = NCOL * NROW   ! 50 bricks
DIM BRICK(50)              ! 1 = intact, 0 = broken (DIM needs a literal bound)
LET BW = FW / NCOL         ! brick width  (6)
LET BH = 1.5               ! brick height
LET BRICKTOP = FH - 2      ! y of the top brick row's top edge (28)
LET BRICKBOT = BRICKTOP - NROW * BH   ! y below the lowest brick row (20.5)

! ---- paddle ----
LET PHW = 5                ! paddle half-width
LET PADY = 2               ! paddle centre row
LET PADTOP = PADY + 0.5    ! paddle top surface (the ball rebounds here)
LET PADSPEED = 3           ! columns moved per key press
LET SHMIN = PHW            ! paddle-centre travel limits
LET SHMAX = FW - PHW

! ---- ball ----
LET SPEED = 0.8            ! vertical speed magnitude
LET MAXVX = 0.9            ! horizontal speed at the paddle edges

! ---- mutable state (pre-set so nothing is read before it's assigned) ----
LET PX = FW / 2            ! paddle centre x
LET BX = 0                 ! ball position
LET BLY = 0
LET VX = 0                 ! ball velocity
LET VY = 0
LET LIVES = 3
LET SC = 0                 ! score
LET NLEFT = NBRICK         ! bricks remaining
LET STATE = 0              ! 0 playing, 1 win, 2 lose
LET QUIT = 0
LET FC = 0                 ! frame counter (also the headless backstop)
LET MAXFRAMES = 3000       ! a stray never-miss orbit can't hang a headless run
LET MSG$ = "A/D or arrows move - Q quit"
LET K$ = ""
LET I = 0                  ! loop / collision temporaries
LET BC = 0
LET BR = 0
LET BIDX = 0
LET LX = 0
LET TY = 0
LET HITP = 0
LET HISCORE = 0            ! best score, persisted to breakout.score (RECTYPE INTERNAL)
LET PREVHI = 0             ! score to beat this game (to detect a new record)
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
    IF STATE <> 0 THEN EXIT DO     ! win/lose: leave the final frame up
    SLEEP 0.04                     ! ~25 fps (also presents the frame)
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
! bricks (colour by row)
FOR I = 1 TO NBRICK
  IF BRICK(I) = 1 THEN
    LET BR = INT((I - 1) / NCOL)
    LET BC = (I - 1) MOD NCOL
    LET LX = BC * BW
    LET TY = BRICKTOP - BR * BH
    IF BR = 0 THEN
      SET AREA COLOR 5
    ELSEIF BR = 1 THEN
      SET AREA COLOR 6
    ELSEIF BR = 2 THEN
      SET AREA COLOR 4
    ELSEIF BR = 3 THEN
      SET AREA COLOR 3
    ELSE
      SET AREA COLOR 2
    END IF
    GRAPH AREA: LX + 0.2, TY - BH + 0.2; LX + BW - 0.2, TY - BH + 0.2; LX + BW - 0.2, TY - 0.2; LX + 0.2, TY - 0.2
  END IF
NEXT I
! paddle
SET AREA COLOR 2
GRAPH AREA: PX - PHW, PADY - 0.5; PX + PHW, PADY - 0.5; PX + PHW, PADY + 0.5; PX - PHW, PADY + 0.5
! ball
SET AREA COLOR 7
GRAPH AREA: BX - 0.5, BLY - 0.5; BX + 0.5, BLY - 0.5; BX + 0.5, BLY + 0.5; BX - 0.5, BLY + 0.5
! HUD
SET TEXT COLOR 1
GRAPH TEXT, AT 1, FH - 1: "SCORE " & LTRIM$(STR$(SC)) & "    HI " & LTRIM$(STR$(HISCORE)) & "    LIVES " & LTRIM$(STR$(LIVES)) & "    BRICKS " & LTRIM$(STR$(NLEFT))
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
  LET ANYKEY = 1                   ! a real key arrived — a keyboard is present
  IF K$ = "q" OR K$ = "Q" THEN
    LET QUIT = 1
  ELSEIF K$ = "a" OR K$ = "A" OR K$ = CHR$(0) & CHR$(75) THEN
    LET PX = PX - PADSPEED
  ELSEIF K$ = "d" OR K$ = "D" OR K$ = CHR$(0) & CHR$(77) THEN
    LET PX = PX + PADSPEED
  END IF
LOOP
IF PX < SHMIN THEN LET PX = SHMIN
IF PX > SHMAX THEN LET PX = SHMAX
RETURN

! ======================================================================
! 3000  UPDATE — move the ball; reflect off walls, bricks and the paddle;
!        handle a miss (lose a life) and the win (all bricks cleared).
! ======================================================================
3000 REM ---- update ----
LET FC = FC + 1
LET BX = BX + VX
LET BLY = BLY + VY
! side walls
IF BX < 0.5 THEN
  LET BX = 0.5
  LET VX = -VX
END IF
IF BX > FW - 0.5 THEN
  LET BX = FW - 0.5
  LET VX = -VX
END IF
! top wall
IF BLY > FH - 0.5 THEN
  LET BLY = FH - 0.5
  LET VY = -VY
END IF
! bricks: map the ball's position to a grid cell; if it's intact, break it
IF BLY <= BRICKTOP AND BLY > BRICKBOT THEN
  LET BC = INT(BX / BW)
  LET BR = INT((BRICKTOP - BLY) / BH)
  IF BC >= 0 AND BC < NCOL AND BR >= 0 AND BR < NROW THEN
    LET BIDX = BR * NCOL + BC + 1
    IF BRICK(BIDX) = 1 THEN
      LET BRICK(BIDX) = 0
      LET VY = -VY
      LET SC = SC + (NROW - BR) * 10      ! higher rows score more
      LET NLEFT = NLEFT - 1
      IF SC > HISCORE THEN LET HISCORE = SC    ! HUD shows the live best
      IF SC > PREVHI THEN LET NEWREC = 1       ! beat the saved record
    END IF
  END IF
END IF
! win?
IF NLEFT <= 0 THEN
  LET STATE = 1
  LET MSG$ = "YOU WIN!  score " & LTRIM$(STR$(SC))
  GOSUB 5200
  RETURN
END IF
! paddle: catch the ball anywhere from its top surface down (so a fast ball
! can't tunnel through), then steer by where it struck relative to centre
IF VY < 0 AND BLY <= PADTOP AND BLY >= 0 AND BX >= PX - PHW AND BX <= PX + PHW THEN
  LET BLY = PADTOP
  LET VY = SPEED
  LET HITP = (BX - PX) / PHW       ! -1 (left edge) .. +1 (right edge)
  LET VX = HITP * MAXVX
END IF
! miss? the ball fell below the paddle
IF BLY < 0 THEN
  LET LIVES = LIVES - 1
  IF LIVES <= 0 THEN
    LET STATE = 2
    LET MSG$ = "GAME OVER  score " & LTRIM$(STR$(SC))
    GOSUB 5200
    RETURN
  END IF
  GOSUB 4000                       ! serve a fresh ball
END IF
! headless / stuck-orbit guard: end the game rather than loop forever
IF FC > MAXFRAMES THEN
  LET STATE = 2
  LET MSG$ = "TIME UP  score " & LTRIM$(STR$(SC))
  GOSUB 5200
END IF
RETURN

! ======================================================================
! 4000  SERVE — place the ball just above the paddle, launched upward at a
!        random angle. The angle is never near-vertical, so the ball always
!        drifts across columns (a dead-straight bounce could orbit forever).
! ======================================================================
4000 REM ---- serve ball ----
LET BX = PX
LET BLY = PADTOP + 2
LET VY = SPEED
LET VX = (RND - 0.5) * 2 * MAXVX
IF VX >= 0 AND VX < 0.3 THEN LET VX = 0.3
IF VX < 0 AND VX > -0.3 THEN LET VX = -0.3
RETURN

! ======================================================================
! 5000  LOAD high score from breakout.score (INTERNAL/exact). Missing file
!        on the first run is fine — the handler leaves HISCORE at 0.
! ======================================================================
5000 REM ---- load high score ----
LET HISCORE = 0
WHEN EXCEPTION IN
  OPEN #2: NAME "breakout.score", ACCESS INPUT, RECTYPE INTERNAL
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
  OPEN #2: NAME "breakout.score", ACCESS OUTPUT, RECTYPE INTERNAL
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
! ======================================================================
6000 REM ---- new game ----
LET PX = FW / 2
LET LIVES = 3
LET SC = 0
LET NLEFT = NBRICK
LET STATE = 0
LET QUIT = 0
LET FC = 0
LET NEWREC = 0
LET PREVHI = HISCORE
LET MSG$ = "A/D or arrows move - Q quit"
FOR I = 1 TO NBRICK
  LET BRICK(I) = 1
NEXT I
GOSUB 4000                         ! serve the first ball
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
