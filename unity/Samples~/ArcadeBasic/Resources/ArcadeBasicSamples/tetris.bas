! tetris.bas — falling-block puzzle, the fourth real-time arcade game alongside  @category Games
! invaders.bas, snake.bas and breakout.bas. The new tricks here are a piece
! table read from DATA, live 90-degree rotation by the (r,c) -> (c,-r) transform,
! and full-row clearing. INKEY$ (non-blocking keyboard) + SLEEP (frame delay)
! drive the loop; graphics use the ECMA-116 §13 module, so it draws on the
! Braille console: in the IDE, via `arcade-basic run examples/tetris.bas`, and as
! a standalone binary. INKEY$/SLEEP are Microsoft BASIC extensions, not ISO/ECMA
! Full BASIC — see docs/conformance.md.
!
! Controls:   A / LEFT  move left     D / RIGHT  move right
!             W / UP     rotate        S / DOWN   soft drop
!             SPACE      hard drop      Q  quit
! After a game ends:  R  play again    Q  quit
!
! The high score is saved to tetris.score (RECTYPE INTERNAL) and shown as HI.
! With no live keyboard (piped/headless) nothing steers the pieces — INKEY$
! returns "" — so they stack straight down, the well tops out, and the program
! exits without prompting (a frame cap backstops it), so it still terminates.

OPTION BASE 1

! ---- well geometry (logical units; the device stretches them to the terminal) ----
LET ROWS = 20             ! well height in cells
LET COLS = 10             ! well width in cells

! WELL(idx) holds the colour (1..7) of a landed cell, or 0 if empty.
! Cell (R,C) — R = 1 at the TOP, C = 1 at the LEFT — maps to idx (R-1)*COLS + C.
DIM WELL(200)             ! ROWS*COLS = 200 (DIM needs a literal bound)

! ---- piece table, filled from DATA below ----
! Seven tetrominoes. Each is four cells given as (row,col) OFFSETS from a pivot,
! so a clockwise turn is just (r,c) -> (c,-r) about that pivot. SOR/SOC hold the
! offsets (idx (P-1)*4 + K), PCOL the colour, PNOROT 1 for the square O (no turn).
DIM SOR(28)
DIM SOC(28)
DIM PCOL(7)
DIM PNOROT(7)

! ---- the current piece: live offsets CR/CC + pivot position (PR,PC) ----
DIM CR(4)
DIM CC(4)
! GR/GC/GPR/GPC are a scratch "candidate" — build a move there, test it with the
! 8000 fit check, and only commit it back to CR/CC/PR/PC if it fits.
DIM GR(4)
DIM GC(4)

! ---- mutable state (pre-set so nothing is read before it's assigned) ----
LET PR = 2                ! current pivot row / column in the well
LET PC = 5
LET CURP = 1              ! current piece index (1..7)
LET CURC = 1              ! current piece colour
LET NXTP = 1              ! next piece index (shown in the NEXT box)
LET SC = 0                ! score
LET NLN = 0               ! total lines cleared
LET LVL = 1               ! level (rises every 10 lines; speeds up the fall)
LET FALLFR = 12           ! frames between gravity steps (smaller = faster)
LET GRAVCNT = 0           ! frames since the last gravity step
LET STATE = 0             ! 0 playing, 2 game over
LET QUIT = 0
LET FC = 0                ! frame counter (also the headless backstop)
LET MAXFRAMES = 6000      ! a headless run can't loop forever
LET MSG$ = "TETRIS"
LET K$ = ""
LET SOFT = 0              ! this frame's intents, set by the input routine
LET HARD = 0
LET HISCORE = 0           ! best score, persisted to tetris.score (RECTYPE INTERNAL)
LET PREVHI = 0            ! score to beat this game (to detect a new record)
LET NEWREC = 0
LET ANYKEY = 0            ! 1 once any key is seen — proves a keyboard is present
LET AGAIN = 0             ! 1 = play again, set by the game-over prompt
! scratch temporaries (declared up front so nothing is read before assignment)
LET P = 0
LET K = 0
LET R = 0
LET KC = 0
LET V = 0
LET OK = 0
LET RR = 0
LET FULL = 0
LET CLR = 0
LET TOPOUT = 0
LET GPR = 0
LET GPC = 0
LET I = 0

! ---- load the seven piece shapes from DATA ----
FOR P = 1 TO 7
  READ PCOL(P)
  READ PNOROT(P)
  FOR K = 1 TO 4
    READ SOR((P - 1) * 4 + K)
    READ SOC((P - 1) * 4 + K)
  NEXT K
NEXT P

RANDOMIZE
GOSUB 5000                ! load the saved high score (once)

! ---- session loop: each pass plays one game; R restarts ----
DO
  GOSUB 6000                       ! reset state for a new game
  ! ---- game loop ----
  DO
    GOSUB 2000                     ! read input
    IF QUIT = 1 THEN EXIT DO
    IF STATE = 0 THEN GOSUB 3000   ! advance the world
    GOSUB 1000                     ! draw
    IF STATE <> 0 THEN EXIT DO     ! game over: leave the final frame up
    SLEEP 0.05                     ! ~20 fps (also presents the frame)
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
SET WINDOW 0, 40, 0, 22
! well border
SET LINE COLOR 8
GRAPH LINES: 0, 0; COLS, 0; COLS, ROWS; 0, ROWS; 0, 0
! landed blocks
FOR R = 1 TO ROWS
  FOR KC = 1 TO COLS
    LET V = WELL((R - 1) * COLS + KC)
    IF V <> 0 THEN
      SET AREA COLOR V
      GRAPH AREA: KC - 1, ROWS - R; KC, ROWS - R; KC, ROWS - R + 1; KC - 1, ROWS - R + 1
    END IF
  NEXT KC
NEXT R
! current falling piece (skip any cell still above the top of the well)
SET AREA COLOR CURC
FOR K = 1 TO 4
  LET R = PR + CR(K)
  LET KC = PC + CC(K)
  IF R >= 1 AND R <= ROWS THEN
    GRAPH AREA: KC - 1, ROWS - R; KC, ROWS - R; KC, ROWS - R + 1; KC - 1, ROWS - R + 1
  END IF
NEXT K
! HUD (to the right of the well; text renders rightward across the wide window)
SET TEXT COLOR 7
GRAPH TEXT, AT 12, 19: "SCORE " & LTRIM$(STR$(SC))
SET TEXT COLOR 1
GRAPH TEXT, AT 12, 18: "LINES " & LTRIM$(STR$(NLN))
GRAPH TEXT, AT 12, 17: "LEVEL " & LTRIM$(STR$(LVL))
GRAPH TEXT, AT 12, 16: "HI    " & LTRIM$(STR$(HISCORE))
GRAPH TEXT, AT 12, 14: "NEXT"
! next-piece preview (drawn from its spawn offsets around a local origin)
SET AREA COLOR PCOL(NXTP)
FOR K = 1 TO 4
  LET R = SOR((NXTP - 1) * 4 + K)
  LET KC = SOC((NXTP - 1) * 4 + K)
  GRAPH AREA: 15 + KC, 11 - R; 16 + KC, 11 - R; 16 + KC, 12 - R; 15 + KC, 12 - R
NEXT K
! controls
SET TEXT COLOR 8
GRAPH TEXT, AT 12, 8: "A / D   move"
GRAPH TEXT, AT 12, 7: "W       rotate"
GRAPH TEXT, AT 12, 6: "S       soft drop"
GRAPH TEXT, AT 12, 5: "SPACE   hard drop"
GRAPH TEXT, AT 12, 4: "Q       quit"
! status / game-over line (top strip, full width to the right)
SET TEXT COLOR 1
GRAPH TEXT, AT 0, 21: MSG$
RETURN

! ======================================================================
! 2000  INPUT — drain every pending key this frame (non-blocking). Sideways
!        moves and rotation are applied at once via the fit check; soft/hard
!        drop just set a flag that the update routine acts on.
! ======================================================================
2000 REM ---- input ----
LET QUIT = 0
LET SOFT = 0
LET HARD = 0
DO
  LET K$ = INKEY$
  IF K$ = "" THEN EXIT DO
  LET ANYKEY = 1                   ! a real key arrived — a keyboard is present
  IF K$ = "q" OR K$ = "Q" THEN
    LET QUIT = 1
  ELSEIF K$ = "a" OR K$ = "A" OR K$ = CHR$(0) & CHR$(75) THEN
    GOSUB 8200                     ! copy current piece into the candidate
    LET GPR = PR
    LET GPC = PC - 1
    GOSUB 8000
    IF OK = 1 THEN LET PC = PC - 1
  ELSEIF K$ = "d" OR K$ = "D" OR K$ = CHR$(0) & CHR$(77) THEN
    GOSUB 8200
    LET GPR = PR
    LET GPC = PC + 1
    GOSUB 8000
    IF OK = 1 THEN LET PC = PC + 1
  ELSEIF K$ = "w" OR K$ = "W" OR K$ = CHR$(0) & CHR$(72) THEN
    GOSUB 3800                     ! rotate
  ELSEIF K$ = "s" OR K$ = "S" OR K$ = CHR$(0) & CHR$(80) THEN
    LET SOFT = 1
  ELSEIF K$ = " " THEN
    LET HARD = 1
  END IF
LOOP
RETURN

! ======================================================================
! 3000  UPDATE — apply gravity (or a drop). When the piece can't fall, lock
!        it into the well, clear any full rows, and spawn the next piece.
! ======================================================================
3000 REM ---- update ----
LET FC = FC + 1
IF HARD = 1 THEN
  ! hard drop: fall until something blocks the way, scoring 2 per cell
  DO
    GOSUB 8200
    LET GPR = PR + 1
    LET GPC = PC
    GOSUB 8000
    IF OK = 0 THEN EXIT DO
    LET PR = PR + 1
    LET SC = SC + 2
  LOOP
  GOSUB 3500                       ! lock + clear + spawn
  RETURN
END IF
IF SOFT = 1 THEN LET GRAVCNT = FALLFR   ! force a gravity step this frame
LET GRAVCNT = GRAVCNT + 1
IF GRAVCNT >= FALLFR THEN
  GOSUB 8200
  LET GPR = PR + 1
  LET GPC = PC
  GOSUB 8000
  IF OK = 1 THEN
    LET PR = PR + 1
  ELSE
    GOSUB 3500                     ! can't fall — lock it
  END IF
  LET GRAVCNT = 0
END IF
! headless backstop: end the game rather than loop forever
IF FC > MAXFRAMES THEN
  LET STATE = 2
  LET MSG$ = "TIME UP  score " & LTRIM$(STR$(SC))
  GOSUB 5200
END IF
RETURN

! ======================================================================
! 3500  LOCK — stamp the current piece into the well, then clear lines and
!        spawn the next piece. A cell that locks above the top tops the well
!        out (game over).
! ======================================================================
3500 REM ---- lock piece ----
LET TOPOUT = 0
FOR K = 1 TO 4
  LET RR = PR + CR(K)
  LET KC = PC + CC(K)
  IF RR < 1 THEN
    LET TOPOUT = 1
  ELSE
    LET WELL((RR - 1) * COLS + KC) = CURC
  END IF
NEXT K
IF TOPOUT = 1 THEN
  LET STATE = 2
  LET MSG$ = "GAME OVER  score " & LTRIM$(STR$(SC))
  GOSUB 5200
  RETURN
END IF
GOSUB 3700                         ! clear full rows + score them
GOSUB 3600                         ! spawn the next piece (may top out)
RETURN

! ======================================================================
! 3600  SPAWN — make NXTP the current piece, roll a fresh NXTP, and place it
!        at the top. If it doesn't fit, the well has topped out (game over).
! ======================================================================
3600 REM ---- spawn next piece ----
LET CURP = NXTP
LET CURC = PCOL(CURP)
LET NXTP = INT(RND * 7) + 1
LET PR = 2
LET PC = 5
FOR K = 1 TO 4
  LET CR(K) = SOR((CURP - 1) * 4 + K)
  LET CC(K) = SOC((CURP - 1) * 4 + K)
NEXT K
GOSUB 8200                         ! candidate = the freshly spawned piece
LET GPR = PR
LET GPC = PC
GOSUB 8000
IF OK = 0 THEN
  LET STATE = 2
  LET MSG$ = "GAME OVER  score " & LTRIM$(STR$(SC))
  GOSUB 5200
END IF
RETURN

! ======================================================================
! 3700  CLEAR LINES — drop every full row, shifting the rows above it down,
!        then score by how many cleared at once (the classic 1/2/3/4 curve).
! ======================================================================
3700 REM ---- clear full lines ----
LET CLR = 0
LET R = ROWS
DO
  IF R < 1 THEN EXIT DO
  LET FULL = 1
  FOR KC = 1 TO COLS
    IF WELL((R - 1) * COLS + KC) = 0 THEN LET FULL = 0
  NEXT KC
  IF FULL = 1 THEN
    LET CLR = CLR + 1
    ! pull every row above R down by one
    FOR RR = R TO 2 STEP -1
      FOR KC = 1 TO COLS
        LET WELL((RR - 1) * COLS + KC) = WELL((RR - 2) * COLS + KC)
      NEXT KC
    NEXT RR
    ! the top row is now empty; re-test row R, which holds new contents
    FOR KC = 1 TO COLS
      LET WELL(KC) = 0
    NEXT KC
  ELSE
    LET R = R - 1
  END IF
LOOP
IF CLR = 1 THEN LET SC = SC + 100 * LVL
IF CLR = 2 THEN LET SC = SC + 300 * LVL
IF CLR = 3 THEN LET SC = SC + 500 * LVL
IF CLR = 4 THEN LET SC = SC + 800 * LVL
LET NLN = NLN + CLR
LET LVL = INT(NLN / 10) + 1
LET FALLFR = 13 - LVL
IF FALLFR < 2 THEN LET FALLFR = 2
IF SC > HISCORE THEN LET HISCORE = SC      ! HUD shows the live best
IF SC > PREVHI THEN LET NEWREC = 1         ! beat the saved record
RETURN

! ======================================================================
! 3800  ROTATE — turn the current piece 90 degrees clockwise about its pivot
!        with (r,c) -> (c,-r); keep it only if it still fits (no wall kick).
! ======================================================================
3800 REM ---- rotate ----
IF PNOROT(CURP) = 1 THEN RETURN
FOR K = 1 TO 4
  LET GR(K) = CC(K)
  LET GC(K) = -CR(K)
NEXT K
LET GPR = PR
LET GPC = PC
GOSUB 8000
IF OK = 1 THEN
  FOR K = 1 TO 4
    LET CR(K) = GR(K)
    LET CC(K) = GC(K)
  NEXT K
END IF
RETURN

! ======================================================================
! 5000  LOAD high score from tetris.score (INTERNAL/exact). Missing file on
!        the first run is fine — the handler just leaves HISCORE at 0.
! ======================================================================
5000 REM ---- load high score ----
LET HISCORE = 0
WHEN EXCEPTION IN
  OPEN #2: NAME "tetris.score", ACCESS INPUT, RECTYPE INTERNAL
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
  OPEN #2: NAME "tetris.score", ACCESS OUTPUT, RECTYPE INTERNAL
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
FOR I = 1 TO ROWS * COLS
  LET WELL(I) = 0
NEXT I
LET SC = 0
LET NLN = 0
LET LVL = 1
LET FALLFR = 12
LET GRAVCNT = 0
LET STATE = 0
LET QUIT = 0
LET FC = 0
LET NEWREC = 0
LET PREVHI = HISCORE
LET MSG$ = "TETRIS  -  clear lines!"
LET NXTP = INT(RND * 7) + 1
GOSUB 3600                         ! spawn the first piece
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

! ======================================================================
! 8000  FIT CHECK — does the candidate piece (offsets GR/GC at pivot
!        GPR/GPC) fit? Sets OK = 1 if every cell is inside the side walls,
!        not below the floor, and not overlapping a landed block. Cells
!        above the top (row < 1) are allowed, so a piece can spawn/turn there.
! ======================================================================
8000 REM ---- fit check ----
LET OK = 1
FOR K = 1 TO 4
  LET RR = GPR + GR(K)
  LET KC = GPC + GC(K)
  IF KC < 1 OR KC > COLS THEN LET OK = 0
  IF RR > ROWS THEN LET OK = 0
  IF RR >= 1 AND RR <= ROWS THEN
    IF WELL((RR - 1) * COLS + KC) <> 0 THEN LET OK = 0
  END IF
NEXT K
RETURN

! ======================================================================
! 8200  COPY CURRENT — load the live piece offsets into the candidate so a
!        position-only move can be tested without disturbing the original.
! ======================================================================
8200 REM ---- copy current piece into the candidate ----
FOR K = 1 TO 4
  LET GR(K) = CR(K)
  LET GC(K) = CC(K)
NEXT K
RETURN

! ======================================================================
! PIECE DATA — colour, no-rotate flag, then four (row,col) offsets from the
! pivot. Row grows downward, col grows rightward.
!   I  ████      O  ██     T  ███    S   ██    Z  ██     J  █      L    █
!                   ██         █         ██         ██       ███      ███
! ======================================================================
DATA 6, 0,  0,-1,  0,0,  0,1,  0,2      ! I (cyan)
DATA 3, 1,  0,0,   0,1,  1,0,  1,1      ! O (yellow)
DATA 5, 0,  0,-1,  0,0,  0,1,  1,0      ! T (magenta)
DATA 2, 0,  0,0,   0,1,  1,-1, 1,0      ! S (green)
DATA 4, 0,  0,-1,  0,0,  1,0,  1,1      ! Z (red)
DATA 1, 0,  -1,-1, 0,-1, 0,0,  0,1      ! J (blue)
DATA 7, 0,  -1,1,  0,-1, 0,0,  0,1      ! L (white)
