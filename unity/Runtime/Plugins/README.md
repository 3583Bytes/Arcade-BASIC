# Plugins folder

This folder receives the pre-built `netstandard2.1` DLLs that ship with the published Unity package.

In the git repo, the folder is left empty so we don't check in binaries. The release CI publishes the netstandard2.1 build and copies the resulting DLLs here before zipping the package as a release asset.

For local Unity development against this repo, run the helper script from the repo root:

```sh
./unity/scripts/copy-dlls.sh
```

(or the equivalent PowerShell on Windows). That builds the netstandard2.1 target and copies the DLLs in place. Unity will then pick them up on next reload.

DLLs that land here:

```
FullBasic.Core.dll
FullBasic.Lexer.dll
FullBasic.Parser.dll
FullBasic.Sema.dll
FullBasic.Runtime.dll
FullBasic.Interpreter.dll
FullBasic.Bytecode.dll
FullBasic.Compiler.dll
FullBasic.Vm.dll
Singulink.Numerics.BigDecimal.dll
Singulink.Numerics.BigIntegerExtensions.dll
```

System.* assemblies that `dotnet publish` produces for netstandard2.1 (`System.Memory`, `System.Buffers`, `System.Numerics.Vectors`, `System.Runtime.CompilerServices.Unsafe`) are **not** copied — Unity ships its own and double-shipping causes ambiguous-reference warnings.
