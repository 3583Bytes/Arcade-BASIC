! Sieve of Eratosthenes up to 50.
OPTION BASE 1
DIM SIEVE(50)

! Mark all as prime (0 = prime, 1 = composite).
FOR I = 1 TO 50
  LET SIEVE(I) = 0
NEXT I
LET SIEVE(1) = 1

! Cross out composites.
FOR I = 2 TO 50
  IF SIEVE(I) = 0 THEN
    FOR J = I * 2 TO 50 STEP I
      LET SIEVE(J) = 1
    NEXT J
  END IF
NEXT I

PRINT "primes up to 50:"
FOR I = 2 TO 50
  IF SIEVE(I) = 0 THEN PRINT I;
NEXT I
PRINT
END
