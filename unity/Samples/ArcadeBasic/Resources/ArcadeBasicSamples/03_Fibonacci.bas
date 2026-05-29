! Fibonacci sequence via DIM array. First 15 terms.
OPTION BASE 1
DIM F(15)
LET F(1) = 1
LET F(2) = 1
FOR I = 3 TO 15
  LET F(I) = F(I - 1) + F(I - 2)
NEXT I

PRINT "first 15 Fibonacci numbers:"
FOR I = 1 TO 15
  PRINT F(I);
NEXT I
PRINT
END
