! maze3d.bas — a first-person 3D maze in the style of Wolfenstein 3D: find the    @category Games
! key, then the green door, before the time runs out; each gem you pick up adds
! ten seconds. The view is ray cast: for each column of
! the screen a ray steps across the map grid (the DDA algorithm) until it hits a
! wall, and the distance sets how tall that column's slice of wall is. The walls
! are textured: where the ray hit along the wall picks a column of a 16 x 16
! texture, and MAT GRAPH CELLS (ANSI Full BASIC's cell array) stretches that
! column over the slice — the device does the scaling, as Wolfenstein's renderer
! did. The floor and ceiling are textured too, a dot at a time: each row of the
! screen below eye level looks at the floor a known distance away, so following
! the column's ray that far finds the spot — and its texel — that dot shows. Only
! the rows the wall leaves visible are worked out; they're drawn as two cell
! arrays, and the walls over them. The key and the gems are sprites: pictures
! stood upright on the floor, scaled by distance like the walls, drawn a column at
! a time — only where they're nearer than that column's wall — farthest first,
! with see-through parts (cells coloured -1, which MAT GRAPH CELLS leaves undrawn).
! SET COLOR MIX gives the textures their own
! colours, with darker copies for walls facing north/south and anything far
! away. In the browser playground a dot is 6 pixels; in a terminal, half a
! character (block elements, 2 x 2 per cell, one colour per character), so the
! textures show best up close.
!
! The minimap (top left) shows only the walls you've seen. KEYDOWN (held keys)
! walks and turns, INKEY$ reads the other keys, SLEEP paces the loop —
! extensions, not ISO/ECMA Full BASIC; see docs/conformance.md.
!
! The sound effects are PLAY "MB…": background music (GW-BASIC), which plays
! while the game runs on.
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

! ---- the level and the textures: modules in maze3d-level.bas and maze3d-textures.bas,
! set up before this runs; DECLARE PUBLIC shows their variables here --------------
LIBRARY "maze3d-level.bas", "maze3d-textures.bas"
DECLARE PUBLIC MW, MH, M(), NOBJ, OKIND(), OX(), OY()
DECLARE PUBLIC TW, TSLOT(), FLOORTEX, TEX()

! ---- what the game keeps about the map and the objects --------------------------
DIM SEEN(15, 15)                   ! 1 once a ray has hit that wall (the minimap shows it)
LET SR0 = 0                        ! the rows and columns those walls span
LET SR1 = 0
LET SQ0 = 0
LET SQ1 = 0
DIM STRIP(0, 15)                   ! a wall column being drawn: 1 across, 16 down
DIM OGOT(5)                        ! 1 once picked up
DIM ODEP(5)                        ! this frame: how far in front of you, how far across
DIM OACR(5)
DIM OORD(5)                        ! the ones in front, farthest first
DIM OSLOT(2)                       ! each kind's sprite
LET OSLOT(1) = 14
LET OSLOT(2) = 16
LET OSIZE = 0.5                    ! a sprite's height (and width), in tiles

! ---- the floor and ceiling, a dot at a time --------------------------------------
! FLR(x, r) is the floor's colour r dots below eye level in screen column x, CEI the
! ceiling's r dots above; both are sized to the screen in 1700. Each frame's ray
! for a column remembers its wall slice in CTT/CTX/CLH, so the walls can be drawn
! after (over) the floor and ceiling.
DIM FLR(0, 0)
DIM CEI(0, 0)
DIM FDIST(159)                     ! how far away the floor is, r dots below eye level
DIM FSLOT(159)                     ! and which copy of its texture (shaded far away)
DIM CTT(319)                       ! per column: the wall's texture, its column, its height,
DIM CTX(319)                       ! and its distance (what's nearer is in front of it)
DIM CLH(319)
DIM CPD(319)

! ---- minimap colours: lit (east/west faces, near) and shaded (north/south, far) --
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
LET GEMTIME = 10                   ! seconds each gem adds
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
LET HASKEY = 0                     ! 1 once you have the key
LET GEMS = 0                       ! gems picked up
LET BONUS = 0                      ! seconds they've added
LET NOTE$ = ""                     ! a message on the bottom row, for NOTEFC more frames
LET NOTEFC = 0
LET DET = 1                        ! drawing objects: the camera's determinant, how many
LET NV = 0                         ! are in front, and each one's size and place
LET SZ = 0
LET SC = 0
LET SB = 0
LET X0 = 0
LET X1 = 0
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
LET WX = 0                         ! where a ray hit along the wall (0-1)
LET TX = 0                         ! and so the texture column it shows
LET TT = 0                         ! which texture
LET FR = 44                        ! the floor: its rows, and the screen they were sized for
LET LASTW = 0
LET LASTH = 0
LET D = 0                          ! a floor dot: how far away, where, which texel
LET FX = 0
LET FY = 0
LET U = 0
LET V = 0
LET FT = 0

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
! The 3D view, between the HUD's two rows: one unit per dot, as for the rest of
! the screen, but clipped there so near walls don't run over the HUD.
SET VIEWPORT 0, 1, VY0 / H, VY1 / H
SET WINDOW 0, W, VY0, VY1
! One ray per column, through the middle of the column: it finds the column's
! wall, then the floor and ceiling the wall leaves showing.
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
  ! Where along the wall the ray hit (0-1) is the texture's column. Seen from the
  ! west or the north it would read right to left, so turn it round.
  IF SIDE = 0 THEN LET WX = PY + PD * RY ELSE LET WX = PX + PD * RX
  LET TX = INT((WX - INT(WX)) * TW)
  IF SIDE = 0 AND RX < 0 THEN LET TX = TW - 1 - TX
  IF SIDE = 1 AND RY > 0 THEN LET TX = TW - 1 - TX
  LET TT = TSLOT(T)
  IF SIDE = 1 OR PD > FAR THEN LET TT = TT + 1   ! the shaded copy
  LET CTT(X) = TT
  LET CTX(X) = TX
  LET CLH(X) = LH
  LET CPD(X) = PD
  ! The rows below the wall's foot (and above its top): the ray reaches the floor
  ! FDIST(R) away at (PX, PY) + FDIST(R) * (RX, RY), in some tile; where in the tile
  ! is the texel.
  FOR R = INT(LH / 2) TO FR - 1
    LET D = FDIST(R)
    LET FX = PX + D * RX
    LET FY = PY + D * RY
    LET U = INT((FX - INT(FX)) * TW)
    LET V = INT((FY - INT(FY)) * TW)
    LET FT = FSLOT(R)
    LET FLR(X, R) = TEX(FT, U, V)
    LET CEI(X, R) = TEX(FT + 2, U, V)
  NEXT R
NEXT X
! The floor down from eye level and the ceiling up from it (FLR(0, 0) and
! CEI(0, 0) sit at eye level, on the left), then the walls over them, each its
! texture's column stretched from the top of the slice (where STRIP(0, 0) goes)
! to the bottom. Rows the walls hide still hold earlier frames' floor: hidden.
MAT GRAPH CELLS, IN 0, VC; W, VC - FR: FLR
MAT GRAPH CELLS, IN 0, VC; W, VC + FR: CEI
FOR X = 0 TO NR - 1
  LET TT = CTT(X)
  LET TX = CTX(X)
  FOR K = 0 TO TW - 1
    LET STRIP(0, K) = TEX(TT, TX, K)
  NEXT K
  LET XL = X * CW
  LET LH = CLH(X)
  MAT GRAPH CELLS, IN XL, VC + LH / 2; XL + CW, VC - LH / 2: STRIP
NEXT X
GOSUB 1300                         ! the key and the gems
SET VIEWPORT 0, 1, 0, 1            ! the whole screen again
SET WINDOW 0, W, 0, H
IF SHOWMAP = 1 THEN GOSUB 1500
GOSUB 1600                         ! the HUD
IF STATE <> 0 THEN GOSUB 1800      ! the end message
RETURN

! ======================================================================
! 1300  OBJECTS — the key and the gems still lying about, as sprites. Each one's
!        place seen from the camera — A along the view direction (its distance,
!        like a wall's), B across along the camera plane — solves
!        (OX - PX, OY - PY) = A * (DX, DY) + B * (PLX, PLY). Its size on screen is
!        a wall's at that distance times OSIZE; it stands on the floor there.
! ======================================================================
1300 REM ---- objects ----
LET DET = DX * PLY - PLX * DY
LET NV = 0
FOR I = 1 TO NOBJ
  IF OGOT(I) = 0 THEN
    LET RX = OX(I) - PX
    LET RY = OY(I) - PY
    LET A = (RX * PLY - PLX * RY) / DET
    IF A > 0.2 THEN                  ! in front of you (and not inside you)
      LET NV = NV + 1
      LET OORD(NV) = I
      LET ODEP(I) = A
      LET OACR(I) = (DX * RY - DY * RX) / DET
    END IF
  END IF
NEXT I
! Farthest first, so nearer ones cover them (an insertion sort: there are few).
FOR J = 2 TO NV
  LET I = OORD(J)
  LET K = J - 1
  DO WHILE K >= 1
    IF ODEP(OORD(K)) >= ODEP(I) THEN EXIT DO
    LET OORD(K + 1) = OORD(K)
    LET K = K - 1
  LOOP
  LET OORD(K + 1) = I
NEXT J
FOR J = 1 TO NV
  LET I = OORD(J)
  LET A = ODEP(I)
  LET SZ = OSIZE * PROJ / A          ! its height and width, in dots
  LET SC = (OACR(I) / A + 1) * W / 2 ! its middle, across the screen
  LET SB = VC - 0.5 * PROJ / A       ! the floor under it
  LET X0 = INT((SC - SZ / 2) / CW)   ! the screen columns it covers
  LET X1 = INT((SC + SZ / 2) / CW)
  IF X0 < 0 THEN LET X0 = 0
  IF X1 > NR - 1 THEN LET X1 = NR - 1
  LET TT = OSLOT(OKIND(I))
  IF A > FAR THEN LET TT = TT + 1    ! the shaded copy
  FOR X = X0 TO X1
    IF A < CPD(X) THEN               ! nearer than this column's wall
      LET TX = INT(((X + 0.5) * CW - (SC - SZ / 2)) / SZ * TW)
      IF TX >= 0 AND TX < TW THEN
        FOR K = 0 TO TW - 1
          LET STRIP(0, K) = TEX(TT, TX, K)
        NEXT K
        MAT GRAPH CELLS, IN X * CW, SB + SZ; X * CW + CW, SB: STRIP
      END IF
    END IF
  NEXT X
NEXT J
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
IF HASKEY = 1 THEN
  GRAPH TEXT, AT 10, ROWS - 1: "KEY - now find the green door"
ELSE
  GRAPH TEXT, AT 10, ROWS - 1: "find the key"
END IF
SET TEXT COLOR 6
GRAPH TEXT, AT COLS - 18, ROWS - 1: "GEMS " & LTRIM$(STR$(GEMS)) & "   MAZE 3D"
IF NOTEFC > 0 THEN
  SET TEXT COLOR 7
  GRAPH TEXT, AT 1, 0.2: NOTE$
ELSE
  GRAPH TEXT, AT 1, 0.2: MSG$
END IF
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
LET FR = VC - VY0                  ! dot rows of floor below eye level (as many of ceiling above)
IF W <> LASTW OR H <> LASTH THEN
  ! A new screen size: new floor and ceiling buffers, and the floor's distance for
  ! each row. A dot r rows below eye level (its middle r + 0.5 down) shows the
  ! floor, half a tile below the eye, at the distance where half a tile looks
  ! that tall: 0.5 * PROJ / (r + 0.5).
  MAT REDIM FLR(NR - 1, FR - 1)
  MAT REDIM CEI(NR - 1, FR - 1)
  FOR R = 0 TO FR - 1
    LET FDIST(R) = 0.5 * PROJ / (R + 0.5)
    LET FSLOT(R) = FLOORTEX
    IF FDIST(R) > FAR THEN LET FSLOT(R) = FLOORTEX + 1
  NEXT R
  LET LASTW = W
  LET LASTH = H
END IF
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
  GRAPH TEXT, AT BX0 + 2, RC: "in " & LTRIM$(STR$(INT(FC / FPS))) & " s, with " & LTRIM$(STR$(GEMS)) & " gems"
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
  ! Walking into the green door is the way out — once you have the key.
  LET NX = PX + DX * SPEED * FWD
  LET NY = PY + DY * SPEED * FWD
  LET T = M(INT(PY), INT(NX + BODY * SGN(NX - PX)))
  IF T = 0 THEN LET PX = NX
  IF T = 9 THEN GOSUB 3500
  LET T = M(INT(NY + BODY * SGN(NY - PY)), INT(PX))
  IF T = 0 THEN LET PY = NY
  IF T = 9 THEN GOSUB 3500
  ! Walking over an object picks it up.
  FOR I = 1 TO NOBJ
    IF OGOT(I) = 0 AND (OX(I) - PX) ^ 2 + (OY(I) - PY) ^ 2 < 0.25 THEN
      LET OGOT(I) = 1
      IF OKIND(I) = 1 THEN
        LET HASKEY = 1
        LET NOTE$ = "You found the key!"
        PLAY "MB T200 L16 O5 C E G O6 C8"
      ELSE
        PLAY "MB T255 L32 O5 G O6 C E"
        LET GEMS = GEMS + 1
        LET BONUS = BONUS + GEMTIME
        LET NOTE$ = "A gem: +" & LTRIM$(STR$(GEMTIME)) & " seconds"
      END IF
      LET NOTEFC = 2 * FPS
    END IF
  NEXT I
END IF
IF NOTEFC > 0 THEN LET NOTEFC = NOTEFC - 1
LET LASTT = TLEFT
LET TLEFT = LIMIT + BONUS - INT(FC / FPS)
IF STATE = 0 AND TLEFT < LASTT AND TLEFT <= 10 AND TLEFT > 0 THEN PLAY "MB T255 L32 O5 A"   ! the last ten seconds tick
IF STATE = 0 AND TLEFT <= 0 THEN
  LET TLEFT = 0
  LET STATE = 2
  PLAY "MB T120 L8 O3 G F E D C2"
END IF
RETURN

! ======================================================================
! 3500  THE DOOR — you walked into it.
! ======================================================================
3500 REM ---- door ----
IF HASKEY = 1 THEN
  IF STATE = 0 THEN PLAY "MB T160 L16 O4 C E G O5 C E G O6 C4"
  LET STATE = 1
ELSE
  ! (Walking into the door tries it every frame: buzz once per message.)
  IF NOTEFC = 0 OR NOTE$ <> "The door is locked: find the key" THEN PLAY "MB T255 L16 O2 C P32 C"
  LET NOTE$ = "The door is locked: find the key"
  LET NOTEFC = 2 * FPS
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
LET HASKEY = 0
LET GEMS = 0
LET BONUS = 0
LET NOTEFC = 0
MAT OGOT = ZER
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
