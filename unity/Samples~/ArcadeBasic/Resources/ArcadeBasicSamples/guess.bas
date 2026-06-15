! Number-guessing game. Stdin form: pipe in your guesses, one per line.  @category Games
! Example:   echo "50\n25\n12\n6\n" | arcade-basic run guess.bas
LET TARGET = 7
LET TRIES = 0
PRINT "guess the number from 1 to 10:"
DO
  INPUT G
  LET TRIES = TRIES + 1
  IF G < TARGET THEN
    PRINT "too low"
  ELSEIF G > TARGET THEN
    PRINT "too high"
  ELSE
    PRINT "got it in"; TRIES; "tries!"
    EXIT DO
  END IF
LOOP
END
