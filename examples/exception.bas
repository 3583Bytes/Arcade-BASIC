! Exception handling — implicit division-by-zero plus user CAUSE,  @category Basics
! demonstrating EXTYPE/EXLINE and RETRY.

PRINT "-- catch division by zero --"
WHEN EXCEPTION IN
  LET X = 1 / 0
  PRINT "this line is skipped"
USE
  PRINT "caught at line"; EXLINE; "type"; EXTYPE
END WHEN

PRINT
PRINT "-- retry until ATTEMPTS = 3 --"
LET ATTEMPTS = 0
WHEN EXCEPTION IN
  LET ATTEMPTS = ATTEMPTS + 1
  PRINT "attempt"; ATTEMPTS
  IF ATTEMPTS < 3 THEN CAUSE EXCEPTION 9000 + ATTEMPTS
  PRINT "succeeded after"; ATTEMPTS; "tries"
USE
  PRINT "  handler saw type"; EXTYPE
  RETRY
END WHEN
END
