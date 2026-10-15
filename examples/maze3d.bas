! maze3d.bas — a first-person 3D maze in the style of Wolfenstein 3D: find the    @category Games
! green door before the time runs out. The view is ray cast: for each column of
! the screen a ray steps across the map grid (the DDA algorithm) until it hits a
! wall, and the distance sets how tall that column's slice of wall is. Each slice
! is one GRAPH AREA, so a frame is a floor and a row of coloured columns — about
! 160 rectangles, drawn with the ECMA-116 §13 graphics module. Walls facing
! north/south are shaded darker than east/west ones, and far walls darker still.
! In a terminal each column is half a character (block elements, 2 x 2 per cell);
! in the browser playground it's a 6-pixel strip.
!
! The minimap (top left) shows only the walls you've seen. KEYDOWN (held keys)
! walks and turns, INKEY$ reads the other keys, SLEEP paces the loop —
! extensions, not ISO/ECMA Full BASIC; see docs/conformance.md.
!
! Controls:   hold W / UP  walk     hold S / DOWN  back up
!             hold A / LEFT  turn left     hold D / RIGHT  turn right
!             M  minimap on/off     Q  quit
! After a game ends:  R  play again    Q  quit
!
! With no live keyboard (piped/headless) you stand still, the clock runs out and
! the program exits without prompting, so it still terminates.

OPTION ARITHMETIC NATIVE           ! IEEE doubles: much faster for the ray maths
OPTION BASE 0

! ---- the map: 16 x 16 tiles, row 0 at the top (north) ----------------------
! 0 floor, 1 blue stone, 2 red brick, 3 yellow, 4 cyan, 9 the exit door
LET MW = 16
LET MH = 16
DIM M(15, 15)                      ! M(row, column)
DIM SEEN(15, 15)                   ! 1 once a ray has hit that wall (the minimap shows it)
LET SR0 = 0                        ! the rows and columns those walls span
LET SR1 = 0
LET SQ0 = 0
LET SQ1 = 0
DATA "1111111111111111"
DATA "1000001000000001"
DATA "1011101011111101"
DATA "1010001010000101"
DATA "1010111010110101"
DATA "1000100000100001"
DATA "1110102222101111"
DATA "1000002002000001"
DATA "1033302002033301"
DATA "1000002002000001"
DATA "1011102202201101"
DATA "1010000000001001"
DATA "1010111144111011"
DATA "1000100000001001"
DATA "1110001110000009"
DATA "1111111111111111"
FOR R = 0 TO MH - 1
  READ ROW$
  FOR Q = 0 TO MW - 1
    LET M(R, Q) = VAL(MID$(ROW$, Q + 1, 1))
  NEXT Q
NEXT R

! ---- wall colours: lit (east/west faces, near) and shaded (north/south, far) --
DIM BRIGHT(9)
DIM DARK(9)
LET BRIGHT(1) = 4                  ! blue
LET DARK(1) = 11
LET BRIGHT(2) = 2                  ! red
LET DARK(2) = 9
LET BRIGHT(3) = 7                  ! yellow / brown
LET DARK(3) = 14
LET BRIGHT(4) = 6                  ! cyan
LET DARK(4) = 13
LET BRIGHT(9) = 3                  ! green: the way out
LET DARK(9) = 10

! ---- tunables ----------------------------------------------------------------
LET FPS = 25                       ! frames per second (SLEEP 1 / FPS)
LET LIMIT = 90                     ! seconds to find the door
LET SPEED = 0.12                   ! tiles walked per frame while W/S is held
LET TURN = 0.08                    ! radians turned per frame while A/D is held
LET BODY = 0.25                    ! how close you can get to a wall, in tiles
LET FOV = 0.66                     ! camera plane half-width: a 66-degree view
LET FAR = 6                        ! walls farther than this are shaded
LET TS = 2                         ! minimap tile size, in dots

! ---- state (pre-set so nothing is read before it's assigned) -----------------
LET PX = 0                         ! where you stand, in tiles (x east, y south)
LET PY = 0
LET DX = 0                         ! the way you face (a unit vector)
LET DY = 0
LET PLX = 0                        ! the camera plane, at right angles to it
LET PLY = 0
LET FC = 0                         ! frames played (the clock)
LET TLEFT = LIMIT                  ! seconds left
LET STATE = 0                      ! 0 playing, 1 escaped, 2 out of time
LET QUIT = 0
LET AGAIN = 0
LET ANYKEY = 0                     ! 1 once any key is seen — proves a keyboard is present
LET SHOWMAP = 1
LET FWD = 0                        ! this frame's input: +1 walk, -1 back up
LET ROT = 0                        ! -1 turn left, +1 turn right
LET K$ = ""
LET MSG$ = ""
LET DEVW = 0                       ! the screen (see 1700)
LET DEVH = 0
LET DEVU$ = ""
LET W = 160                        ! its size in dots, and in cells
LET H = 96
LET COLS = 80
LET ROWS = 24
LET NR = 160                       ! rays, and how wide each one's column is
LET CW = 1
LET VY0 = 4                        ! the 3D view's bottom, top and eye level
LET VY1 = 92
LET VC = 48
LET PROJ = 1

! ---- session loop: each pass plays one game; R restarts ----------------------
DO
  GOSUB 6000                       ! a new game
  DO
    GOSUB 2000                     ! read the keys
    IF QUIT = 1 THEN EXIT DO
    GOSUB 3000                     ! move, run the clock
    GOSUB 1000                     ! draw
    IF STATE <> 0 THEN EXIT DO     ! escaped or out of time: leave the frame up
    SLEEP 1 / FPS                  ! (also presents the frame)
  LOOP
  IF QUIT = 1 THEN EXIT DO
  ! Only wait for R / Q if a keyboard is actually there (a headless or piped
  ! run never pressed a key — don't hang it).
  IF ANYKEY = 0 THEN EXIT DO
  GOSUB 7000
  IF AGAIN = 0 THEN EXIT DO
LOOP
END

! ======================================================================
! 1000  RENDER — the 3D view, the minimap, the HUD (and the end message).
! ======================================================================
1000 REM ---- render ----
CLEAR
GOSUB 1700                         ! measure the screen
SET WINDOW 0, W, 0, H              ! one unit per dot
! The floor; the ceiling is the black background.
SET AREA COLOR 8
GRAPH AREA: 0, VY0; W, VY0; W, VC; 0, VC
! One ray per column, through the middle of the column.
FOR X = 0 TO NR - 1
  LET CAM = (2 * X + 1) / NR - 1   ! -1 at the left edge of the view, +1 at the right
  LET RX = DX + PLX * CAM
  LET RY = DY + PLY * CAM
  LET MX = INT(PX)                 ! the tile the ray is in
  LET MY = INT(PY)
  ! How far along the ray one whole tile is, across x and across y ...
  IF RX = 0 THEN LET DDX = 1E30 ELSE LET DDX = ABS(1 / RX)
  IF RY = 0 THEN LET DDY = 1E30 ELSE LET DDY = ABS(1 / RY)
  ! ... and to the first tile edge each way.
  IF RX < 0 THEN
    LET SX = -1
    LET SDX = (PX - MX) * DDX
  ELSE
    LET SX = 1
    LET SDX = (MX + 1 - PX) * DDX
  END IF
  IF RY < 0 THEN
    LET SY = -1
    LET SDY = (PY - MY) * DDY
  ELSE
    LET SY = 1
    LET SDY = (MY + 1 - PY) * DDY
  END IF
  ! Step to whichever edge is nearer until the ray enters a wall (the map's
  ! border is all wall, so it always does).
  DO
    IF SDX < SDY THEN
      LET SDX = SDX + DDX
      LET MX = MX + SX
      LET SIDE = 0                 ! crossed an x edge: an east/west face
    ELSE
      LET SDY = SDY + DDY
      LET MY = MY + SY
      LET SIDE = 1                 ! a north/south face
    END IF
    LET T = M(MY, MX)
    IF T > 0 THEN EXIT DO
  LOOP
  IF SEEN(MY, MX) = 0 THEN
    LET SEEN(MY, MX) = 1
    IF MY < SR0 THEN LET SR0 = MY
    IF MY > SR1 THEN LET SR1 = MY
    IF MX < SQ0 THEN LET SQ0 = MX
    IF MX > SQ1 THEN LET SQ1 = MX
  END IF
  ! Distance to the wall along the view direction (not along the ray, which
  ! would bulge the walls like a fish-eye lens), and so the slice's height.
  IF SIDE = 0 THEN LET PD = SDX - DDX ELSE LET PD = SDY - DDY
  IF PD < 0.05 THEN LET PD = 0.05
  LET LH = PROJ / PD
  LET Y0 = VC - LH / 2
  LET Y1 = VC + LH / 2
  IF Y0 < VY0 THEN LET Y0 = VY0
  IF Y1 > VY1 THEN LET Y1 = VY1
  IF SIDE = 1 OR PD > FAR THEN
    SET AREA COLOR DARK(T)
  ELSE
    SET AREA COLOR BRIGHT(T)
  END IF
  LET XL = X * CW
  GRAPH AREA: XL, Y0; XL + CW, Y0; XL + CW, Y1; XL, Y1
NEXT X
IF SHOWMAP = 1 THEN GOSUB 1500
GOSUB 1600                         ! the HUD
IF STATE <> 0 THEN GOSUB 1800      ! the end message
RETURN

! ======================================================================
! 1500  MINIMAP — the walls seen so far, you (white) and the way you face.
!        Tiles are TS dots square, each 1.5 dots wide and tall in the window
!        so it fills exactly TS dots (a fill covers both of its edges).
! ======================================================================
1500 REM ---- minimap ----
LET MX0 = 2                        ! left edge, in dots
LET MTOP = VY1 - 2                 ! top edge
! A black backing behind what's been explored (one tile's margin), so the view
! doesn't show through; it grows as you go.
LET XL = MX0 + SQ0 * TS - 2
LET XR = MX0 + (SQ1 + 1) * TS + 1.5
LET YT = MTOP - SR0 * TS + 1.5
LET YB = MTOP - (SR1 + 1) * TS - 2
SET AREA COLOR 0
GRAPH AREA: XL, YB; XR, YB; XR, YT; XL, YT
FOR R = 0 TO MH - 1
  FOR Q = 0 TO MW - 1
    IF SEEN(R, Q) = 1 THEN
      SET AREA COLOR DARK(M(R, Q))
      IF M(R, Q) = 9 THEN SET AREA COLOR BRIGHT(9)
      LET XL = MX0 + Q * TS
      LET YT = MTOP - R * TS
      GRAPH AREA: XL, YT - 1.5; XL + 1.5, YT - 1.5; XL + 1.5, YT; XL, YT
    END IF
  NEXT Q
NEXT R
! the way you face, then you
LET XL = MX0 + PX * TS
LET YT = MTOP - PY * TS
SET LINE COLOR 7
GRAPH LINES: XL, YT; XL + DX * 3 * TS, YT - DY * 3 * TS
SET AREA COLOR 1
LET XL = MX0 + INT(PX) * TS
LET YT = MTOP - INT(PY) * TS
GRAPH AREA: XL, YT - 1.5; XL + 1.5, YT - 1.5; XL + 1.5, YT; XL, YT
RETURN

! ======================================================================
! 1600  HUD — the clock along the top row, the keys along the bottom one.
! ======================================================================
1600 REM ---- hud ----
SET WINDOW 0, COLS, 0, ROWS        ! one unit per character cell
IF TLEFT <= 15 THEN SET TEXT COLOR 2 ELSE SET TEXT COLOR 7
GRAPH TEXT, AT 1, ROWS - 1: "TIME " & LTRIM$(STR$(TLEFT))
SET TEXT COLOR 1
GRAPH TEXT, AT 10, ROWS - 1: "find the green door"
SET TEXT COLOR 6
GRAPH TEXT, AT COLS - 8, ROWS - 1: "MAZE 3D"
GRAPH TEXT, AT 1, 0.2: MSG$
RETURN

! ======================================================================
! 1700  SCREEN — size the view to the device. On a character screen (2 x 4
!        dots a cell, up to 40 rows) the window is its dots, one ray per dot
!        column; on a finer device (SVG, pixels) it's a 160 x 96 screen, scaled.
! ======================================================================
1700 REM ---- screen ----
ASK DEVICE SIZE DEVW, DEVH, DEVU$
IF DEVW >= 64 AND DEVH >= 48 AND DEVH <= 160 THEN
  LET W = INT(DEVW / 2) * 2
  LET H = INT(DEVH / 4) * 4
ELSE
  LET W = 160
  LET H = 96
END IF
LET COLS = W / 2
LET ROWS = H / 4
LET NR = W                         ! rays (screen columns) ...
IF NR > 320 THEN LET NR = 320      ! ... capped, for very wide terminals
LET CW = W / NR                    ! each column's width, in dots
LET VY0 = 4                        ! the view sits between the HUD's two rows
LET VY1 = H - 4
LET VC = (VY0 + VY1) / 2           ! eye level
LET PROJ = W / 2 / FOV             ! a wall 1 tile tall, 1 tile away, in dots
RETURN

! ======================================================================
! 1800  END MESSAGE — a box in the middle of the view.
! ======================================================================
1800 REM ---- end message ----
LET RC = INT(ROWS / 2)
LET BX0 = INT(COLS / 2) - 15
LET BX1 = BX0 + 30
SET AREA COLOR 0
GRAPH AREA: BX0, RC - 1.9; BX1, RC - 1.9; BX1, RC + 2.9; BX0, RC + 2.9
SET LINE COLOR 1
GRAPH LINES: BX0, RC - 1.7; BX1, RC - 1.7; BX1, RC + 2.3; BX0, RC + 2.3; BX0, RC - 1.7
IF STATE = 1 THEN
  SET TEXT COLOR 3
  GRAPH TEXT, AT BX0 + 2, RC + 1: "YOU ESCAPED!"
  SET TEXT COLOR 1
  GRAPH TEXT, AT BX0 + 2, RC: "in " & LTRIM$(STR$(INT(FC / FPS))) & " seconds"
ELSE
  SET TEXT COLOR 2
  GRAPH TEXT, AT BX0 + 2, RC + 1: "OUT OF TIME"
  SET TEXT COLOR 1
  GRAPH TEXT, AT BX0 + 2, RC: "the door is still out there"
END IF
SET TEXT COLOR 6
GRAPH TEXT, AT BX0 + 2, RC - 1: "R play again    Q quit"
RETURN

! ======================================================================
! 2000  INPUT — this frame's key presses (non-blocking), then the held keys.
! ======================================================================
2000 REM ---- input ----
LET QUIT = 0
DO
  LET K$ = INKEY$
  IF K$ = "" THEN EXIT DO
  LET ANYKEY = 1                   ! a real key arrived — a keyboard is present
  IF K$ = "q" OR K$ = "Q" THEN LET QUIT = 1
  IF K$ = "m" OR K$ = "M" THEN LET SHOWMAP = 1 - SHOWMAP
LOOP
LET FWD = 0
LET ROT = 0
IF KEYDOWN("w") OR KEYDOWN("ArrowUp") THEN LET FWD = FWD + 1
IF KEYDOWN("s") OR KEYDOWN("ArrowDown") THEN LET FWD = FWD - 1
IF KEYDOWN("a") OR KEYDOWN("ArrowLeft") THEN LET ROT = ROT - 1
IF KEYDOWN("d") OR KEYDOWN("ArrowRight") THEN LET ROT = ROT + 1
IF FWD <> 0 OR ROT <> 0 THEN LET ANYKEY = 1
RETURN

! ======================================================================
! 3000  UPDATE — turn, walk (sliding along walls), and run the clock.
! ======================================================================
3000 REM ---- update ----
LET FC = FC + 1
IF ROT <> 0 THEN
  ! Rotate the view direction and the camera plane together. The map's y runs
  ! south (down the screen), so a negative angle turns left.
  LET A = TURN * ROT
  LET CA = COS(A)
  LET SA = SIN(A)
  LET T = DX
  LET DX = DX * CA - DY * SA
  LET DY = T * SA + DY * CA
  LET T = PLX
  LET PLX = PLX * CA - PLY * SA
  LET PLY = T * SA + PLY * CA
END IF
IF FWD <> 0 THEN
  ! Try x and y separately, so walking into a wall at an angle slides along it.
  ! Walking into the green door is the way out.
  LET NX = PX + DX * SPEED * FWD
  LET NY = PY + DY * SPEED * FWD
  LET T = M(INT(PY), INT(NX + BODY * SGN(NX - PX)))
  IF T = 0 THEN LET PX = NX
  IF T = 9 THEN LET STATE = 1
  LET T = M(INT(NY + BODY * SGN(NY - PY)), INT(PX))
  IF T = 0 THEN LET PY = NY
  IF T = 9 THEN LET STATE = 1
END IF
LET TLEFT = LIMIT - INT(FC / FPS)
IF STATE = 0 AND TLEFT <= 0 THEN
  LET TLEFT = 0
  LET STATE = 2
END IF
RETURN

! ======================================================================
! 6000  NEW GAME — back to the start, facing east, the map forgotten.
! ======================================================================
6000 REM ---- new game ----
LET PX = 1.5
LET PY = 1.5
LET DX = 1
LET DY = 0
LET PLX = 0
LET PLY = FOV
LET FC = 0
LET TLEFT = LIMIT
LET STATE = 0
LET QUIT = 0
LET SHOWMAP = 1
MAT SEEN = ZER
LET SR0 = INT(PY)                  ! (just your own tile, so far)
LET SR1 = SR0
LET SQ0 = INT(PX)
LET SQ1 = SQ0
LET MSG$ = "W/S walk   A/D turn   M map   Q quit"
RETURN

! ======================================================================
! 7000  GAME-OVER PROMPT — keep the final frame up until R or Q.
! ======================================================================
7000 REM ---- restart prompt ----
LET AGAIN = 0
DO
  LET K$ = INKEY$
  IF K$ = "r" OR K$ = "R" THEN
    LET AGAIN = 1
    EXIT DO
  ELSEIF K$ = "q" OR K$ = "Q" THEN
    EXIT DO
  END IF
  SLEEP 0.05                       ! pace the wait + keep presenting the frame
LOOP
RETURN
