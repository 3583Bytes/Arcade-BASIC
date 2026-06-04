! Real-time input demo (INKEY$ + SLEEP, a Microsoft-BASIC extension).
! Move the dot with the ARROW KEYS. Press Q to quit.
! Runs on the Graphics tab; click Run, then click the Graphics screen so it
! has focus and start pressing keys.

SET WINDOW 0, 100, 0, 100
LET X = 50
LET Y = 50

DO
   CLEAR
   SET TEXT COLOR 6
   GRAPH TEXT, AT 4, 95: "ARROWS MOVE - Q QUITS"
   SET POINT COLOR 7
   GRAPH POINTS: X, Y

   LET K$ = INKEY$
   IF K$ = CHR$(0) & CHR$(72) THEN LET Y = Y + 3
   IF K$ = CHR$(0) & CHR$(80) THEN LET Y = Y - 3
   IF K$ = CHR$(0) & CHR$(75) THEN LET X = X - 3
   IF K$ = CHR$(0) & CHR$(77) THEN LET X = X + 3

   SLEEP 0.03
LOOP UNTIL K$ = "Q" OR K$ = "q"

END
