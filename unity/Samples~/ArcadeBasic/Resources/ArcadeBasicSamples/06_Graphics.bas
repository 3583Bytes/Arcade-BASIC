! §13 graphics demo — runs on the Graphics tab.
! Click Run; the IDE auto-switches to the Graphics screen on the first draw.

SET WINDOW 0, 100, 0, 100

! A cyan triangle outline.
SET LINE COLOR 6
GRAPH LINES: 10, 10; 90, 10; 50, 90; 10, 10

! A filled blue triangle inside it.
SET AREA COLOR 4
GRAPH AREA: 40, 30; 60, 30; 50, 55

! Yellow corner markers.
SET POINT COLOR 7
GRAPH POINTS: 10, 10; 90, 10; 50, 90

! A title.
SET TEXT COLOR 7
GRAPH TEXT, AT 12, 95: "ARCADE BASIC"

END
