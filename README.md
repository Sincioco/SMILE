# SMILE

SMILE is the **Simple Modern Interactive Learning Environment**: a beginner-first educational programming language and transpiler. SMILE source is small and direct, and its generated target source uses the normal constructs a learner would ordinarily meet in that destination language.

Version 1.0 is a deliberate breaking alignment with the SMILE 2.0 BASIC Core language. SMILE 1.0 now accepts one canonical language only—**SMILE Core BASIC 1**. There is no legacy mode, dialect selector, syntax auto-detection, or fallback parser.

## A small program

```smile
' Variables use direct assignment.
Const Greeting = "Hello"
Dim Total As Number
Name = "Sin"

For I = 1 To 3
    Total = Total + I
End For

If Total = 6 Then
    Print Greeting; ", "; Name; "!"
Else
    Print "Unexpected total"
End If
```

Expected output:

```text
Hello, Sin!
```

## Canonical language

Core BASIC 1 provides:

- case-insensitive Unicode identifiers and apostrophe comments;
- `Number`, `Boolean`, and `Text` scalar values with exact fixed types;
- implicit variables by first direct assignment, explicit `Dim ... As ...`, and immutable `Const` values;
- typed expressions with normal precedence, short-circuit `And`/`Or`, Text concatenation, truncating division, and signed `Mod`;
- expression-list `Print`, including blank Print and trailing-semicolon newline suppression;
- `If / Else If / Else / End If`;
- ascending `For ... To` and descending `For ... Down To`, closed by `End For`;
- post-tested `Do / Loop Until`, unconditional `Do / Loop`, `Exit For`, and `Exit Do`;
- `End Program`.

The complete current language is defined by the [SMILE Core BASIC 1 Official Specification](docs/SMILE%20Language%20Specification/001%20-%20SMILE%20Core%20BASIC%201%20Official%20Specification.md). The compact [SMILE Core BASIC Profile 1.0](docs/SMILE%20Core%20BASIC%20Profile%201.0.md) records its SMILE 2.0 provenance, fixture hashes, and exclusions.

This release intentionally rejects earlier SMILE 1.0-only syntax. The compiler does not silently reinterpret old source. [Migrating to Core BASIC 1](docs/Migrating%20to%20Core%20BASIC%201.md) shows explicit source rewrites.

## Ten active targets

The same parsed and bound program generates all ten active destinations:

| CLI ID | Destination | Primary file |
|---|---|---|
| `csharp` | C# | `Program.cs` plus a minimal project file |
| `c` | C | `Program.c` |
| `masm-x64` | Windows x64 MASM Assembly | `Program.asm` |
| `javascript` | JavaScript | `Program.js` |
| `java` | Java | `Program.java` |
| `cobol` | COBOL | `Program.cob` |
| `objective-c` | Objective-C | `Program.m` |
| `swift` | Swift | `Program.swift` |
| `python` | Python | `Program.py` |
| `cpp` | C++ | `Program.cpp` |

Generated code uses native destination constructs whenever practical: normal variables, `if`, native counted loops, post-test loops, direct output, and direct process termination. Helpers appear only when the destination lacks a clear equivalent, such as C Text concatenation or Python's typed exit across differently nested loop kinds. Python output is a direct module-level script—no synthetic `main()` wrapper.

See [Architecture](docs/Architecture.md), [Toolchains](docs/Toolchains.md), and the [Target Code Generation Standard](docs/SMILE%20Target%20Code%20Generation%20Standard%20v1.0.md).

## Examples

- [`examples/language.smile`](examples/language.smile) — cumulative valid language reference packaged with Desktop;
- [`examples/core-basic.smile`](examples/core-basic.smile) — compact end-to-end example;
- [`examples/control-flow.smile`](examples/control-flow.smile) — counted loops, post-test loops, and typed exits;
- [`tests/CoreBasicParity/canonical.smile`](tests/CoreBasicParity/canonical.smile) — unchanged fixture compiled by both SMILE repositories.

All examples use only Core BASIC 1.

## Requirements

- Windows with the .NET 10 SDK for the solution, CLI, tests, and Desktop application;
- an optional destination toolchain to build/run generated output locally.

Target detection is independent. Missing optional compilers do not prevent transpilation or use of installed targets.

## Build, test, and run

Restore and build:

```powershell
dotnet restore SMILE.sln
dotnet build SMILE.sln -c Debug --no-restore -nologo
```

Run the focused mission guardrail:

```powershell
dotnet test tests/SMILE.Tests/SMILE.Tests.csproj -c Debug --filter TestCategory=MissionGuardrail -nologo
```

Run canonical conformance and generation tests:

```powershell
dotnet test tests/SMILE.Tests/SMILE.Tests.csproj -c Debug --filter TestCategory=CoreBasic -nologo
```

Run the pinned cross-repository parity check:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Test-CoreBasicParity.ps1
```

The parity command reads SMILE 2.0, verifies its pinned commit and clean status before and after, and writes all executable output beneath the system temporary directory. It never modifies SMILE 2.0.

Transpile or build/run with the CLI:

```powershell
dotnet run --project src/SMILE.Cli -- examples/core-basic.smile --target python
dotnet run --project src/SMILE.Cli -- examples/core-basic.smile --target csharp --run
dotnet run --project src/SMILE.Cli -- examples/core-basic.smile --target all
```

Run Desktop:

```powershell
dotnet run --project src/SMILE.Desktop
```

## Desktop

Desktop loads the cumulative Core BASIC reference after first paint and asynchronously transpiles the visible active target. It exposes all ten targets and Build & Run where the corresponding local toolchain is available. There is no language-profile selector: every source pane uses the same canonical front end.

Long process, detection, file, build, link, and run work stays off the WPF UI thread. Recoverable failures remain visible without closing the IDE.

Current Desktop build version: `1.0.0 SMILE 2.0 Core BASIC alignment`.

## Project policy

The active destination set is frozen at ten until Sin explicitly changes it. Routine validation follows Velocity Mode: use the smallest directly relevant checks, while broad all-target and cross-repository verification is appropriate for milestones such as this language replacement. Manual `SMILE CI` remains available through `workflow_dispatch`.

Historical requirement files are retained for research context, not as current language authority. Current behavior is governed by `AGENTS.md`, [Core Principles](docs/SMILE%20Core%20Principles.md), and the single current official specification.

## License

SMILE is licensed under the GNU Affero General Public License v3.0. See [LICENSE](LICENSE).
