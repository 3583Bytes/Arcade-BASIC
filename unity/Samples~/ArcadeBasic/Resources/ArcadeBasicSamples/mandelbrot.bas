! mandelbrot.bas — escape-time Mandelbrot set rendered as ASCII art.  @category Basics
! A pure-compute showcase: the inner loop is z = z*z + c iterated until |z| > 2
! or MAXIT is reached, mapping the escape count to a shading character. No input,
! no RND, no graphics device — just arithmetic and PRINT — so it runs identically
! on the interpreter and the VM and makes a clean performance benchmark.
!
!   arcade-basic run examples/mandelbrot.bas
!
! Tune the three parameters below to trade detail for speed.

LET WIDTH = 64            ! output columns
LET HEIGHT = 28           ! output rows
LET MAXIT = 50            ! iteration cap (the escape-time budget)

! Complex-plane window: real in [XMIN,XMAX], imaginary in [YMIN,YMAX].
LET XMIN = -2.5
LET XMAX = 1.0
LET YMIN = -1.25
LET YMAX = 1.25

LET DX = (XMAX - XMIN) / (WIDTH - 1)
LET DY = (YMAX - YMIN) / (HEIGHT - 1)

! Shading ramp: dense (escaped fast) -> sparse (escaped slow) -> space (inside).
LET RAMP$ = "@%#*+=-:. "
LET NRAMP = LEN(RAMP$)

LET PX = 0                ! pixel column / row
LET PY = 0
LET CX = 0                ! c = CX + i*CY (the point under test)
LET CY = 0
LET ZX = 0                ! z, iterated
LET ZY = 0
LET ZX2 = 0               ! cached squares
LET ZY2 = 0
LET IT = 0                ! escape iteration count
LET TMP = 0
LET IDX = 0               ! ramp index
LET LINE$ = ""

FOR PY = 0 TO HEIGHT - 1
  LET CY = YMIN + PY * DY
  LET LINE$ = ""
  FOR PX = 0 TO WIDTH - 1
    LET CX = XMIN + PX * DX
    LET ZX = 0
    LET ZY = 0
    LET IT = 0
    DO
      LET ZX2 = ZX * ZX
      LET ZY2 = ZY * ZY
      IF ZX2 + ZY2 > 4 THEN EXIT DO
      LET TMP = ZX2 - ZY2 + CX
      LET ZY = 2 * ZX * ZY + CY
      LET ZX = TMP
      LET IT = IT + 1
      IF IT >= MAXIT THEN EXIT DO
    LOOP
    ! map escape count -> shading character (inside the set -> last ramp char)
    IF IT >= MAXIT THEN
      LET IDX = NRAMP
    ELSE
      LET IDX = 1 + INT(IT * (NRAMP - 1) / MAXIT)
      IF IDX > NRAMP THEN LET IDX = NRAMP
    END IF
    LET LINE$ = LINE$ & MID$(RAMP$, IDX, 1)
  NEXT PX
  PRINT LINE$
NEXT PY
END
