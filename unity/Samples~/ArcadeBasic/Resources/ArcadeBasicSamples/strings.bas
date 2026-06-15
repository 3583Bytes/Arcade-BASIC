! String built-ins.  @category Basics
LET S$ = "hello, BASIC world"
PRINT "original   : "; S$
PRINT "length     :"; LEN(S$)
PRINT "uppercase  : "; UCASE$(S$)
PRINT "lowercase  : "; LCASE$(S$)
PRINT "first 5    : "; LEFT$(S$, 5)
PRINT "last 5     : "; RIGHT$(S$, 5)
PRINT "middle 7-5 : "; MID$(S$, 8, 5)
PRINT "repeat 3   : "; REPEAT$("ab", 3)
PRINT "chr(65)    : "; CHR$(65)
PRINT "ord(A)     :"; ORD("A")
PRINT "unicode π  :"; LEN("π"); "codepoint(s)"
END
