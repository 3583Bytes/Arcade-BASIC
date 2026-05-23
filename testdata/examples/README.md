# Example Full BASIC programs

Run any of these with the interpreter:

```sh
dotnet run --project src/FullBasic.Cli -- run testdata/examples/<file>.bas
# or, after `dotnet publish ... /p:PublishAot=true`:
./publish/aot/FullBasic.Cli run testdata/examples/<file>.bas
```

| File             | Demonstrates                                    | Tree-walker | Bytecode VM |
|------------------|-------------------------------------------------|:-----------:|:-----------:|
| `hello.bas`      | PRINT, IF/THEN/ELSE, FOR, ^                     | ✓           | ✓           |
| `factorial.bas`  | recursive FUNCTION                              | ✓           | ✓           |
| `fibonacci.bas`  | DIM array, FOR loop                             | ✓           | —           |
| `primes.bas`     | sieve, nested FOR, IF inside loop               | ✓           | —           |
| `strings.bas`    | LEN/MID$/LEFT$/RIGHT$/UCASE$/REPEAT$/CHR$/ORD   | ✓           | ✓           |
| `matrix.bas`     | MAT + / * / TRN / INV / PRINT                   | ✓           | —           |
| `exception.bas`  | WHEN/USE/END WHEN, CAUSE, EXLINE, EXTYPE, RETRY | ✓           | —           |
| `fileio.bas`     | OPEN / PRINT # / LINE INPUT # / CLOSE           | ✓           | —           |
| `formatted.bas`  | PRINT USING with picture strings                | ✓           | —           |
| `modules.bas`    | MODULE block, PUBLIC vs private declarations    | ✓           | —           |
| `pi.bas`         | Leibniz series, MOD, ABS, PI constant           | ✓           | ✓           |
| `guess.bas`      | INPUT loop, IF/ELSEIF/ELSE, EXIT DO             | ✓           | —           |

A ✓ in the **Bytecode VM** column means the example also runs via
`full-basic vm <file>`. The VM doesn't yet support arrays, MAT, file I/O,
exception handling, modules, PRINT USING, or INPUT — those programs run
on the tree-walker only.

To produce a self-contained native binary for any example that the VM
supports:

```sh
./publish/aot/FullBasic.Cli build testdata/examples/factorial.bas -o factorial
./factorial
```
