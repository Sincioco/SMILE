# Modules, application projects and libraries

SMILE 1.0 accepts source-local imports, split modules and source-owned library
packages from the current SMILE 2.0 language. Start with
[the Scoreboard project](../examples/modules/Scoreboard.smileproj).

```smile
Module Lessons.Counters
    Public Const Caption = "Learning together"
    Private Const Bonus = 1
End Module
```

```smile
Import Lessons.Counters As Counters
Print Counters.Caption
```

A module file contains one `Module ... End Module` and no outside statements.
Several files from the same provider can contribute to that module. Declarations
are Private unless marked Public. Types and values have separate name spaces.
Public signatures cannot expose private nominal types. Type/Class member privacy
retains its own rules. Imports precede declarations, require `As Alias`, and apply
only to their physical source. `Option Explicit` is also source-local. Module
code cannot capture consuming-program globals. Imports cannot form a cycle.

Module globals initialize after imported modules and before application startup.
Other non-module support sources can contain declarations and initializers; their
executable statements belong in routines. The selected startup source owns the
application's executable statements. Routine locals can shadow import aliases.

## Run several source files

```powershell
dotnet run --project src/SMILE.Cli -- Program.smile --source Counters.smile --target python --run
dotnet run --project src/SMILE.Cli -- --project examples/modules/Scoreboard.smileproj --target all --run
```

An application `.smileproj` declares `SmileSource` items and a `StartupFile`
(default `Program.smile`). `StartupOnly="true"` excludes alternate startup files
unless selected. `SmileProjectReference` points to `.smilelibproj`;
`SmileLibraryReference` points to `.smilelib`. References and imports are explicit:
a dependency of a library does not become an implicit application import.

Desktop **Open** accepts `.smileproj` and edits its startup `.smile` source.
Saving writes that source. Support files, packages and assets reload on background
transpilation; editing them in another editor takes effect on the next transpile
or run. Changing `StartupFile` requires reopening the project. Save As to another
source path detaches the editor from the project. Desktop formats the startup
source using the complete compilation context. Build library packages with the CLI.

## Build and consume a library

```powershell
dotnet run --project src/SMILE.Cli -- --project examples/modules/library/Counters.smilelibproj --target library -o out/Counters.smilelib
dotnet run --project src/SMILE.Cli -- Program.smile --library out/Counters.smilelib --target csharp --run
```

A library requires `ProjectKind` Library (implied by `.smilelibproj`), `LibraryName`,
an exact `major.minor.patch` Version, and module sources. It has no StartupFile,
ApplicationId, executable entry point or application assets. `OutputName` defaults
to LibraryName. Without `-o`, the CLI writes `<OutputName>.smilelib` beside the project.

Packages use SMILE 2.0 format 7: a deterministic ZIP with fixed timestamps,
uncompressed UTF-8/LF sources, a manifest, SHA-256 source hashes and canonical
public API metadata. Readers bind the sources and their exact declared dependency
graph, then regenerate and compare the metadata. Packages carry no native binary.
Missing dependencies, cycles, provider/version conflicts, unexpected payloads,
unsafe paths, changed hashes and stale metadata fail visibly. Older formats must
be rebuilt. No download or package restore occurs. Supplied package dependencies
must have explicit project/CLI references. Archive processing is bounded to 64 MiB,
1,026 entries and 1,024 sources; individual source/API/manifest limits also apply.
Builds stage and validate a package before atomic replacement, under a bounded
exclusive output lock. Package sources remain in memory instead of being extracted.

## Assets and persistent identity

Application `Asset Include` entries use project-relative paths and `*`, `?`, `**`
patterns. Exact path components must match filesystem case. Duplicate matches are
deduplicated; collisions, missing explicit files and escaping paths are errors.
Use ordinary files inside the project, not links/junctions. Assets copy after a
successful native build and before launch, beside the executable/script. Every
run has a fresh output directory; stale assets cannot survive an earlier run.
An asset cannot overwrite an existing generated source or build output.

`ApplicationId` is optional for Console/Game project metadata. It uses 3–128
lowercase ASCII characters in at least two dot-separated segments. Each segment
starts with a letter and contains letters, digits and non-trailing hyphens.
Without it, identity falls back to OutputName, then the project filename stem.
Loose files use their source filename stem; `--application-id` supplies an explicit
identity. An override must agree with an explicit project ApplicationId.
Libraries use the consuming application's identity for Number/Data persistence.
SMILE 1.0 keeps its own `%LOCALAPPDATA%\SMILE` storage namespace.

```powershell
scripts/Format-Smile.ps1 -Path examples/modules/Scoreboard.smileproj -Check
```

The batch formatter and living-source tests check project sources in their full
compilation context. The CLI `--format`/`--check` modes operate on the selected
project's own compilation sources, preserving referenced library files.
