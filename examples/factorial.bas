! Recursive factorial — demonstrates FUNCTION with self-call.
FUNCTION FACT(N)
  IF N <= 1 THEN
    FACT = 1
  ELSE
    FACT = N * FACT(N - 1)
  END IF
END FUNCTION

FOR I = 1 TO 10
  PRINT "fact("; I; ") ="; FACT(I)
NEXT I
END
