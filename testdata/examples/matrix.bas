! MAT operations — assignment, multiply, transpose, inverse.
OPTION BASE 1
DIM A(2, 2), B(2, 2), C(2, 2), I(2, 2)

LET A(1, 1) = 4
LET A(1, 2) = 7
LET A(2, 1) = 2
LET A(2, 2) = 6

LET B(1, 1) = 1
LET B(1, 2) = 0
LET B(2, 1) = 0
LET B(2, 2) = 1

PRINT "A ="
MAT PRINT A
PRINT "B ="
MAT PRINT B

MAT C = A + B
PRINT "A + B ="
MAT PRINT C

MAT C = A * B
PRINT "A * B ="
MAT PRINT C

MAT C = TRN(A)
PRINT "TRN(A) ="
MAT PRINT C

MAT I = INV(A)
PRINT "INV(A) ="
MAT PRINT I
END
