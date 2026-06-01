! Graphics demo — ECMA-116 §13 graphics module.
! Render to SVG:   arcade-basic run examples/graphics.bas --svg out.svg
!            (or)  arcade-basic vm  examples/graphics.bas --svg out.svg
! Both engines produce byte-identical output.

! Problem coordinates: x over one full period in degrees, y over the sine range.
SET WINDOW 0, 360, -1.5, 1.5
SET VIEWPORT 0, 1, 0, 1

! Axes (gray; the y-axis dashed).
SET LINE COLOR 8
GRAPH LINES: 0, 0; 360, 0
SET LINE STYLE 2
GRAPH LINES: 0, -1.2; 0, 1.2
SET LINE STYLE 1

! A sine curve, drawn segment by segment — coordinate expressions and a loop
! build the polyline that a single literal GRAPH LINES couldn't.
SET LINE COLOR 4
LET PX = 0
LET PY = SIN(0)
FOR DEG = 6 TO 360 STEP 6
  LET X = DEG
  LET Y = SIN(DEG * PI / 180)
  GRAPH LINES: PX, PY; X, Y
  LET PX = X
  LET PY = Y
NEXT DEG

! A filled marker triangle near the first peak, plus point markers and a label.
SET AREA COLOR 2
GRAPH AREA: 80, 1.05; 100, 1.05; 90, 1.3
SET POINT COLOR 1
GRAPH POINTS: 90, 1; 270, -1
SET TEXT COLOR 1
GRAPH TEXT, AT 120, 1.25: "y = sin(x)"
END
