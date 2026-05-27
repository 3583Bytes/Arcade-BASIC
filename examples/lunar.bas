REM LUNAR — Jim Storer, 1969. Ported to Arcade BASIC.
REM From David Ahl's "BASIC Computer Games" (Creative Computing, 1973).
REM
REM You're piloting an Apollo lunar landing capsule by setting the
REM retro-rocket burn rate every 10 seconds. Goal: touch down with
REM impact velocity <= 1.2 MPH for a perfect landing.
REM
REM Burn rate: 0 (free fall) or 8..200 (lb/s). The fuel mass burned
REM gives thrust; the equations of motion are integrated by a fifth-
REM order series expansion (subroutine at 420).
REM
REM Translation notes:
REM   - Original line numbers preserved as labels.
REM   - "IF cond THEN <line>" rewritten as "IF cond THEN GOTO <line>".
REM   - Scientific-notation literals (1E-03, 5E-03) expanded to decimal.

REM Pre-declare names whose first lexical read is inside the GOSUB 420
REM body (the integrator), to keep the analyzer quiet (FB0308).
LET I = 0
LET J = 0
10 PRINT TAB(33); "LUNAR"
20 PRINT TAB(15); "CREATIVE COMPUTING MORRISTOWN, NEW JERSEY"
25 PRINT
   PRINT
   PRINT
30 PRINT "THIS IS A COMPUTER SIMULATION OF AN APOLLO LUNAR"
40 PRINT "LANDING CAPSULE."
   PRINT
   PRINT
50 PRINT "THE ON-BOARD COMPUTER HAS FAILED (IT WAS MADE BY"
60 PRINT "XEROX) SO YOU HAVE TO LAND THE CAPSULE MANUALLY."
70 PRINT
   PRINT "SET BURN RATE OF RETRO ROCKETS TO ANY VALUE BETWEEN"
80 PRINT "0 (FREE FALL) AND 200 (MAXIMUM BURN) POUNDS PER SECOND."
90 PRINT "SET NEW BURN RATE EVERY 10 SECONDS."
   PRINT
100 PRINT "CAPSULE WEIGHT 32,500 LBS; FUEL WEIGHT 16,000 LBS."
110 PRINT
    PRINT
    PRINT
    PRINT "GOOD LUCK"
120 LET L = 0
130 PRINT
    PRINT "SEC", "MI + FT", "MPH", "LB FUEL", "BURN RATE"
    PRINT
140 LET A = 120
    LET V = 1
    LET M = 33000
    LET N = 16500
    LET G = 0.001
    LET Z = 1.8
150 PRINT L, INT(A); INT(5280*(A-INT(A))), 3600*V, M-N,
    INPUT K
    LET T = 10
160 IF M - N < 0.001 THEN GOTO 240
170 IF T < 0.001 THEN GOTO 150
180 LET S = T
    IF M >= N + S*K THEN GOTO 200
190 LET S = (M - N) / K
200 GOSUB 420
    IF I <= 0 THEN GOTO 340
210 IF V <= 0 THEN GOTO 230
220 IF J < 0 THEN GOTO 370
230 GOSUB 330
    GOTO 160
240 PRINT "FUEL OUT AT"; L; "SECONDS"
    LET S = (-V + SQR(V*V + 2*A*G)) / G
250 LET V = V + G*S
    LET L = L + S
260 LET W = 3600 * V
    PRINT "ON MOON AT"; L; "SECONDS - IMPACT VELOCITY"; W; "MPH"
274 IF W <= 1.2 THEN PRINT "PERFECT LANDING!" : GOTO 440
280 IF W <= 10 THEN PRINT "GOOD LANDING (COULD BE BETTER)" : GOTO 440
282 IF W > 60 THEN GOTO 300
284 PRINT "CRAFT DAMAGE... YOU'RE STRANDED HERE UNTIL A RESCUE"
286 PRINT "PARTY ARRIVES. HOPE YOU HAVE ENOUGH OXYGEN!"
288 GOTO 440
300 PRINT "SORRY THERE WERE NO SURVIVORS. YOU BLEW IT!"
310 PRINT "IN FACT, YOU BLASTED A NEW LUNAR CRATER"; W * 0.227; "FEET DEEP!"
320 GOTO 440
330 LET L = L + S
    LET T = T - S
    LET M = M - S*K
    LET A = I
    LET V = J
    RETURN
340 IF S < 0.005 THEN GOTO 260
350 LET D = V + SQR(V*V + 2*A*(G - Z*K/M))
    LET S = 2*A / D
360 GOSUB 420
    GOSUB 330
    GOTO 340
370 LET W = (1 - M*G/(Z*K)) / 2
    LET S = M*V / (Z*K*(W + SQR(W*W + V/Z))) + 0.05
    GOSUB 420
380 IF I <= 0 THEN GOTO 340
390 GOSUB 330
    IF J > 0 THEN GOTO 160
400 IF V > 0 THEN GOTO 370
410 GOTO 160
420 LET Q = S*K / M
    LET J = V + G*S + Z*(-Q - Q*Q/2 - Q^3/3 - Q^4/4 - Q^5/5)
430 LET I = A - G*S*S/2 - V*S + Z*S*(Q/2 + Q^2/6 + Q^3/12 + Q^4/20 + Q^5/30)
    RETURN
440 PRINT
    PRINT
    PRINT
    PRINT "TRY AGAIN??"
    GOTO 70
