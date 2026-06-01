# Example Arcade BASIC programs

Run any of these with the interpreter:

```sh
dotnet run --project src/ArcadeBasic.Cli -- run examples/<file>.bas
# or, after `dotnet publish ... /p:PublishAot=true`:
./publish/aot/ArcadeBasic.Cli run examples/<file>.bas
```

| File             | Demonstrates                                    | Tree-walker | Bytecode VM |
|------------------|-------------------------------------------------|:-----------:|:-----------:|
| `hello.bas`      | PRINT, IF/THEN/ELSE, FOR, ^                     | ✓           | ✓           |
| `factorial.bas`  | recursive FUNCTION                              | ✓           | ✓           |
| `fibonacci.bas`  | DIM array, FOR loop                             | ✓           | ✓           |
| `primes.bas`     | sieve, nested FOR, IF inside loop               | ✓           | ✓           |
| `strings.bas`    | LEN/MID$/LEFT$/RIGHT$/UCASE$/REPEAT$/CHR$/ORD   | ✓           | ✓           |
| `matrix.bas`     | MAT + / * / TRN / INV / PRINT                   | ✓           | ✓           |
| `exception.bas`  | WHEN/USE/END WHEN, CAUSE, EXLINE, EXTYPE, RETRY | ✓           | ✓           |
| `fileio.bas`     | OPEN / PRINT # / LINE INPUT # / CLOSE           | ✓           | ✓           |
| `formatted.bas`  | PRINT USING with picture strings                | ✓           | ✓           |
| `modules.bas`    | MODULE block, PUBLIC vs private declarations    | ✓           | ✓           |
| `pi.bas`         | Leibniz series, MOD, ABS, PI constant           | ✓           | ✓           |
| `guess.bas`      | INPUT loop, IF/ELSEIF/ELSE, EXIT DO             | ✓           | ✓           |
| `kanban.bas`     | interactive graphics board: colored GRAPH LINES lane boxes + GRAPH TEXT cards, INPUT command loop, file save/load | ✓ | ✓ |
| `startrek.bas`   | Super Star Trek (Ahl 1978) — GOSUB-heavy port   | ✓           | ✓           |
| `lunar.bas`      | Lunar Lander (Storer 1969) — physics simulation | ✓           | ✓           |
| `graphics.bas`   | §13 graphics: SET WINDOW/VIEWPORT, GRAPH LINES/AREA/POINTS/TEXT (render with `--svg`) | ✓ | ✓ |

A ✓ in the **Bytecode VM** column means the example also runs via
`arcade-basic vm <file>` with output byte-identical to the tree-walker
(`startrek.bas` uses non-deterministic `RND`, so the two engines produce
structurally-identical output up to RNG draws).

To produce a self-contained native binary for any example:

```sh
./publish/aot/ArcadeBasic.Cli build examples/factorial.bas -o factorial
./factorial
```

## `startrek.bas` — Super Star Trek

The full Mike Mayfield / Bob Leedom / Dave Ahl Super Star Trek (Microsoft 8K
BASIC, March 1978), ported to Arcade BASIC. Recognisable commands:
`NAV`, `SRS`, `LRS`, `PHA`, `TOR`, `SHE`, `DAM`, `COM`, `XXX`.

```sh
dotnet run --project src/ArcadeBasic.Cli -- run examples/startrek.bas
```

Translation notes (preserved at the top of the source file):
- Original line numbers kept as labels, so the structure still maps 1:1 to
  the 1978 listing.
- `ON i GOTO L1,L2,...` expanded into explicit `IF i=k THEN GOTO Lk` chains.
- `IF cond THEN <line>` rewritten as `IF cond THEN GOTO <line>`.
- Inline `FOR ... NEXT` collapsed onto multiple lines (Arcade BASIC requires a
  newline after the FOR header).
- String concat `+` → `&` and the scalar/array name `N` renamed where it
  collided.

Porting this game surfaced (and we fixed) three latent bugs in the
interpreter/sema: GOSUB-from-inside-a-FOR-body never resumed the loop; RND
output occasionally produced scientific notation that BigDecimal.Parse
rejected; and implicit declarations inside DEF FN bodies allocated a fresh
local slot, breaking closure over program-level vars.
