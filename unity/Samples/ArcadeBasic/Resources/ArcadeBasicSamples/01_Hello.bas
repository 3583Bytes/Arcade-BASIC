100 REM hello world example
110 LET X = 42
120 LET MSG$ = "answer is"
130 PRINT MSG$; X
140 IF X > 0 THEN PRINT "positive" ELSE PRINT "non-positive"
150 FOR I = 1 TO 5 STEP 1
160   PRINT I, I^2
170 NEXT I
180 END
