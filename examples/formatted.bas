! PRINT USING — picture-string formatted output.
PRINT USING ">###########": "TABULATED"

PRINT "  i      x       sin(x)"
PRINT "  -      ---     ------"
FOR I = 0 TO 10
  LET X = I / 5
  PRINT USING " ##    ##.##    +#.####": I, X, SIN(X)
NEXT I

PRINT
LET TOTAL = 12345.67
PRINT USING "balance: $$,$$$.##  (placeholder demo)": TOTAL
PRINT USING "balance: +#####.##": TOTAL
END
