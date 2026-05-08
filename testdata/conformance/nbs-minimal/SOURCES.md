# NBS Minimal BASIC Test Programs — sources

We use the four-volume NBS Minimal BASIC Test Programs (NBSIR 78-1420, Cugini et al., NBS 1978–1980) as the conformance oracle for the subset of Full BASIC inherited from Minimal BASIC.

The PDFs and OCR text are not redistributed in this repo; download them from archive.org / nvlpubs.nist.gov as needed.

## OCR text (archive.org `*_djvu.txt` extracts)

| Volume | Title | URL |
|---|---|---|
| 1 | Test system overview | https://archive.org/stream/nbsminimalbasict7714gils/nbsminimalbasict7714gils_djvu.txt |
| 2 | Program structure, output, assignment, simple control structures, simple expressions | https://archive.org/stream/nbsminimalbasic7714gils_0/nbsminimalbasic7714gils_0_djvu.txt |
| 3 | Control statements, data structure, program input | https://archive.org/stream/nbsminimalbasic7714gils_1/nbsminimalbasic7714gils_1_djvu.txt |
| 4 | Mathematical and user-defined functions, compound expressions | https://archive.org/stream/nbsminimalbasic7714gils_2/nbsminimalbasic7714gils_2_djvu.txt |

## PDF originals (NIST nvlpubs / archive.org)

- Vol 1: NBSIR 78-1420 (test system overview), 46 pp
- Vol 2: NBSIR 78-1420-2, 204 pp
- Vol 3: NBSIR 78-1420-3, 242 pp
- Vol 4: NBSIR 78-1420-4, 168 pp

## Workflow

1. Fetch the four `_djvu.txt` files into `raw/vol{1,2,3,4}.txt`.
2. Run the (forthcoming) extractor that scans `BEGIN TEST` / `END TEST` markers and emits one `.bas` + `.expected` per test.
3. Manually verify each extracted program against the corresponding PDF page; clean OCR artifacts (`★` → `*`, occasional quote-character confusion).
4. Test runner diffs `.bas` execution output vs `.expected`; CI gates on this.

## Notes on coverage

The NBS suite tests **Minimal BASIC (X3.60-1978)** only. It exercises the subset of Full BASIC inherited from Minimal BASIC. Full BASIC features outside the suite — `MAT`, `SELECT CASE`, structured `SUB`/`FUNCTION`, `WHEN EXCEPTION`, modules, file I/O, picture/graphics — are validated against our own spec-derived test corpus, not against NBS.
