! maze3d-level.bas — the 3D maze's level: its map, and where the key and the gems     @library
! are. A library of maze3d.bas (LIBRARY "maze3d-level.bas"): a MODULE whose PUBLIC
! variables the game DECLAREs, set up — its DATA read — before the game starts.

MODULE LEVEL
  ! ---- the map: 16 x 16 tiles, row 0 at the top (north) ----------------------
  ! 0 floor, 1 blue stone, 2 red brick, 3 yellow, 4 cyan, 9 the exit door
  PUBLIC MW, MH, M(15, 15)           ! M(row, column)
  LET MW = 16
  LET MH = 16
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

  ! ---- objects: the key and the gems, standing on the floor ------------------------
  ! (kind, column, row): kind 1 the key, 2 a gem, in the middle of that map tile.
  PUBLIC NOBJ, OKIND(5), OX(5), OY(5)
  LET NOBJ = 5
  DATA 1, 7, 8
  DATA 2, 3, 3
  DATA 2, 13, 5
  DATA 2, 6, 13
  DATA 2, 14, 9
  FOR I = 1 TO NOBJ
    READ OKIND(I), Q, R
    LET OX(I) = Q + 0.5
    LET OY(I) = R + 0.5
  NEXT I
END MODULE
