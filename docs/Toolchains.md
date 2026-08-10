# Toolchains

## Current Policy

Transpilation itself does not require a destination compiler or runtime. Build & Run requires the matching local toolchain.

All ten implemented targets are active for normal detection, Desktop/CLI exposure, and Build & Run: C#, C, Windows x64 MASM Assembly, JavaScript, Java, COBOL, Objective-C, Swift, Python, and C++.

Only the toolchains needed for the learner's selected targets are prerequisites. A missing toolchain disables that target's local Build & Run but does not disable transpilation or the other targets.

The central `ActiveTargetLanguages` policy is the source that product, CLI, toolchain, and test enumeration share. Do not maintain independent target lists in each layer.

## Active Toolchain Detection

| Target | Detection | Normal use |
|---|---|---|
| C# | `dotnet --version` | Generate, build, and run with the .NET SDK |
| C | Visual Studio `vswhere.exe`, then `VC\Auxiliary\Build\vcvars64.bat` | Compile and link with the x64 C toolchain |
| MASM x64 | Same Visual Studio environment, plus `ml64` and `link.exe` | Assemble and link a Windows x64 console program |
| JavaScript | `node --version` | Run generated JavaScript with Node.js |
| Java | Detect both `javac` and `java` | Compile and run with a full JDK |
| COBOL | Detect MSYS2 GnuCOBOL `cobc` | Compile and run a Windows executable |
| Objective-C | Detect MSYS2 MinGW64 `clang` | Compile and run a Windows executable |
| Swift | Detect Windows `swiftc`, its SDK, and Visual Studio linker tools | Compile and run a Windows executable |
| Python | Detect Python 3.10 or newer through `python` or `py` | Run generated Python without bytecode output |
| C++ | Visual Studio `vswhere.exe`, then `VC\Auxiliary\Build\vcvars64.bat` | Compile and link with the x64 C++ toolchain |

SMILE uses `vswhere.exe` from the normal Visual Studio Installer location and does not hard-code a Visual Studio year, edition, or install directory.

Default detection probes all active targets asynchronously after first paint. An unavailable toolchain is reported per target and does not block the IDE.

## Active Build And Run Direction

These commands describe the current toolchain shape. Generated filenames and required libraries remain determined by the actual generator output.

### C#

```bat
dotnet build GeneratedProgram.csproj -nologo
dotnet run --project GeneratedProgram.csproj --no-build
```

### C

```bat
call "<vcvars64.bat>" >nul
cl.exe /nologo /TC /utf-8 Program.c /Fe:Program.exe
Program.exe
```

### Windows x64 MASM

```bat
call "<vcvars64.bat>" >nul
ml64 /nologo /c Program.asm /Fo:Program.obj
link.exe /nologo /ignore:4210 Program.obj kernel32.lib legacy_stdio_definitions.lib ucrt.lib /subsystem:console /entry:main /out:Program.exe
Program.exe
```

The reset's beginner-oriented MASM direction uses recognizable CRT/Win64 facilities such as `printf`, `scanf`, and `ExitProcess` when suitable. Current MSVC resolves the familiar plain `printf` and `scanf` spellings through `legacy_stdio_definitions.lib` backed by the Universal CRT (`ucrt.lib`). The known harmless `LNK4210` warning from direct UCRT use with a custom assembly entry point is suppressed by the focused MASM link command.

### JavaScript, Java, COBOL, Objective-C, Swift, Python, And C++

The remaining active toolchains use these direct target commands or their detected absolute-path equivalents:

```text
node Program.js
javac -encoding UTF-8 Program.java && java Program
cobc -x -free Program.cob [support.c] -o Program.exe
clang -x objective-c Program.m -o Program.exe
swiftc -sdk <Windows SDK> Program.swift -o Program.exe
python -B Program.py
cl.exe /nologo /EHsc /std:c++20 /utf-8 Program.cpp /Fe:Program.exe
```

COBOL, Objective-C, and Swift add their detected MSYS2 or Swift runtime paths. Swift and C++ also initialize the Visual Studio x64 environment. SMILE owns these process details in the toolchain layer rather than adding them to learner-facing generated source.

## Native INPUT Behavior

The current INPUT specification no longer requires one byte-level runtime implementation across targets.

Generated programs should use normal destination facilities. The reset-reference direction is:

- C#: `Console.ReadLine()` with conventional conversion;
- C: `scanf` for simple numeric input and `fgets` for line-oriented String input when appropriate;
- MASM: clear CRT/Win64 calls and ordinary target storage.

The toolchain runner supplies stdin and hosts the process. It does not require generated programs to share strict UTF-8 validation, a 4096-byte line limit, embedded-NUL console behavior, identical parser internals, or canonical cross-target error text.

## Standard Input Modes

`ProcessRunner` and generated-program execution retain three host modes:

| Mode | Purpose | Behavior |
|---|---|---|
| `Closed` | Detection, compilers, and captured programs that need no input | Prevent a hidden child from waiting for input |
| `ScriptedText` | Focused deterministic tests | Supply scripted ordinary input, then capture completion and output |
| `InteractiveInherited` | Normal CLI and visible Desktop INPUT execution | Use the invoking terminal or a visible console and keep output live |

Compiler and linker processes always use closed input. Only the generated executable receives interactive or scripted input.

When a CLI run contains INPUT, it inherits the invoking terminal and streams output normally. Desktop Build & Run opens one visible interactive console and remains responsive while the learner enters data. It must not launch a second hidden copy with closed stdin.

## Velocity Mode Validation

Run commands from the repository root.

### Tier 1 — Tiny or local change

For documentation only, perform focused static checks. For a small code change, run the narrowest directly related test plus the smallest Debug build that proves compilation.

Example focused class filter:

```powershell
dotnet test tests/SMILE.Tests/SMILE.Tests.csproj -c Debug --filter "FullyQualifiedName~InputStatementConformanceTests" -nologo
```

### Tier 2 — Normal feature or fix

Run the focused subsystem classes, active-target generation coverage, MissionGuardrail, and one affected Debug build or smoke test.

INPUT-focused example:

```powershell
dotnet test tests/SMILE.Tests/SMILE.Tests.csproj -c Debug --filter "FullyQualifiedName~InputStatementConformanceTests|FullyQualifiedName~InputEvaluatorTests|FullyQualifiedName~ActiveTargetLanguageTests" -nologo
```

Mandatory generated-readability guardrail after output-policy changes:

```powershell
dotnet test tests/SMILE.Tests/SMILE.Tests.csproj -c Debug --filter TestCategory=MissionGuardrail -nologo
```

Active-target policy example:

```powershell
dotnet test tests/SMILE.Tests/SMILE.Tests.csproj -c Debug --filter "FullyQualifiedName~ActiveTargetLanguageTests" -nologo
```

### Tier 3 — Major milestone

Restore and build the affected solution, then run the comprehensive suite appropriate to the milestone:

```powershell
dotnet restore SMILE.sln
dotnet build SMILE.sln -c Debug --no-restore -nologo
dotnet test tests/SMILE.Tests/SMILE.Tests.csproj -c Debug --no-build --no-restore --filter "TestCategory!=HistoricalExactInput" -nologo
```

`HistoricalExactInput` keeps the superseded strict 4096-byte and exact INPUT suite available for explicit historical checks without letting it override the current native INPUT contract.

Add Release validation when release risk justifies it. Do not automatically duplicate every Debug check in Release.

Legacy environment gates such as `SMILE_REQUIRE_JAVA`, `SMILE_REQUIRE_ALL_TARGETS`, and cross-target `SMILE_REQUIRE_ZERO_TARGET_WARNINGS` are not routine requirements. Use them only for explicit milestone or historical verification where they remain relevant.

## Manual SMILE CI

`.github/workflows/smile-ci.yml` retains its Windows Debug/Release restore, build, and test job. During Velocity Mode it has only the manual `workflow_dispatch` trigger.

Run it from GitHub Actions or with GitHub CLI:

```powershell
gh workflow run "SMILE CI" --ref main
```

Inspect a manually requested run when it is part of the milestone plan:

```powershell
gh run list --workflow "SMILE CI" --branch main --limit 10
```

Normal pushes do not require a workflow run, and the old exact-SHA post-push completion gate is suspended.

## Restoring Automatic CI Later

When Sin ends Velocity Mode, restore the workflow triggers in one small change:

```yaml
on:
  push:
    branches:
      - main
  pull_request:
    branches:
      - main
  workflow_dispatch:
```

Then update `AGENTS.md` and current documentation together if the exact-SHA completion gate is intentionally reactivated.

## Ten-Target Reactivation

On 2026-08-10 the seven retained toolchains rejoined C#, C, and MASM in the central active-target policy. Desktop selectors, CLI target IDs, `--target all`, Transpile All, detection, and Build & Run now share the same ten-target set.

Velocity Mode remains in force. Routine changes validate the affected targets; a strict ten-toolchain matrix is reserved for milestones, releases, or explicit requests.

## Temporary Workspaces

Each build/run writes generated files under:

```text
%TEMP%\SMILE\Runs\<unique-id> - <language>\
```

SMILE never builds generated targets inside the repository. The language suffix helps identify each generated-code workspace.

SMILE-owned temporary run workspaces older than one day may be deleted. Cleanup failures are non-fatal and must not prevent a new build/run workspace.

## Pause Launcher

When requested by the Desktop, a successful active-target build/run may also write:

```text
Run Program - Press Any Key.cmd
```

The launcher runs the generated program with normal interactive stdin and waits after completion. Keeping this behavior in a companion script avoids polluting learner-facing target source.

## Timeout, Cancellation, And Failure Containment

Captured or scripted programs retain bounded output, timeout, cancellation, and process-tree termination. A visible interactive program may wait for the learner while its process work stays off the WPF UI thread and remains cancellable through its host path.

Detection, build, run, timeout, cancellation, folder opening, and process-launch failures are recoverable Desktop events. Report them concisely and keep the IDE open.

## Active Troubleshooting

- Missing .NET SDK: install .NET SDK 10 or newer.
- Missing C or MASM tools: install Visual Studio or Build Tools with Desktop development with C++ and the x64 tools.
- Missing JavaScript, Java, or Python tools: install Node.js, a full JDK, or Python 3.10 or newer respectively.
- Missing COBOL or Objective-C tools: install the corresponding MSYS2 MinGW64 GnuCOBOL or Clang package.
- Missing Swift tools: install Swift.Toolchain for Windows and Visual Studio x64 linker tools.
- Desktop diagnostic logs: check `%LOCALAPPDATA%\SMILE\Logs`; if unavailable, SMILE falls back to `%TEMP%\SMILE\Logs`.
