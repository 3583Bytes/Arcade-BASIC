! File I/O round-trip: write three lines, read them back.
LET PATH$ = "/tmp/full-basic-example.txt"

OPEN #1: NAME PATH$, ACCESS OUTPUT
PRINT #1: "line one"
PRINT #1: "line two"
PRINT #1: "line three"
CLOSE #1

PRINT "wrote three lines to "; PATH$
PRINT "reading them back:"

OPEN #1: NAME PATH$, ACCESS INPUT
LINE INPUT #1: A$
LINE INPUT #1: B$
LINE INPUT #1: C$
CLOSE #1

PRINT "  1: "; A$
PRINT "  2: "; B$
PRINT "  3: "; C$
END
