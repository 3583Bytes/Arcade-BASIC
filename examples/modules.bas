! Single-file MODULE block — PUBLIC vs PRIVATE visibility.
MODULE MATHLIB
  ! Private helper, not callable from outside the module.
  FUNCTION HELPER(X)
    HELPER = X * X + 1
  END FUNCTION

  PUBLIC FUNCTION SQUARE(X)
    SQUARE = X * X
  END FUNCTION

  PUBLIC FUNCTION POLY(X)
    POLY = HELPER(X) * 2
  END FUNCTION
END MODULE

PRINT "SQUARE(5) ="; SQUARE(5)
PRINT "POLY(3)   ="; POLY(3)
END
