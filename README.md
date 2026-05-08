# Full BASIC

A Full BASIC (ISO/IEC 10279:1991, ANSI X3.113-1987) interpreter and compiler in C#.

## Status

Phase 0 — foundations. No language features implemented yet.

## Goals

- **Full conformance** to ISO/IEC 10279:1991, with documented deviations in [`docs/conformance.md`](docs/conformance.md).
- **Tree-walking interpreter → bytecode VM → standalone-binary compiler**, in that order.
- **Cross-platform**: Mac, Windows, Linux, all ARM64 + x64, via .NET 9 Native AOT.

## Build

Requires .NET 9 SDK.

```sh
dotnet build                                  # debug build
dotnet test                                   # run tests
dotnet run --project src/FullBasic.Cli -- ... # run interpreter

# AOT-publish a standalone binary for the current platform
dotnet publish src/FullBasic.Cli -c Release /p:PublishAot=true
```

## Conformance

We use the [NBS Minimal BASIC Test Programs](https://archive.org/details/nbsminimalbasic5007cugi_0) (Cugini et al., NBSIR 78-1420) as the conformance oracle for the subset of Full BASIC inherited from Minimal BASIC. Full BASIC features not covered by NBS (`MAT`, `WHEN EXCEPTION`, structured `SUB`/`FUNCTION`, modules, file I/O) are validated against our own spec-derived test corpus.

## License

TBD.
