! Estimate π via the Leibniz series π/4 = 1 - 1/3 + 1/5 - 1/7 + ...
LET SUM = 0
FOR I = 0 TO 999
  LET TERM = 1 / (2 * I + 1)
  IF I MOD 2 = 0 THEN
    LET SUM = SUM + TERM
  ELSE
    LET SUM = SUM - TERM
  END IF
NEXT I
LET PI_ESTIMATE = SUM * 4

PRINT "Leibniz π (1000 terms): "; PI_ESTIMATE
PRINT "built-in PI           : "; PI
PRINT "absolute error        : "; ABS(PI_ESTIMATE - PI)
END
