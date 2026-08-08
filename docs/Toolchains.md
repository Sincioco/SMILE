# Toolchains

## Current Policy

Transpilation itself does not require a destination compiler or runtime. Build & Run requires the matching local toolchain.

During the current three-target phase, normal detection, Desktop/CLI exposure, Build & Run work, and routine validation focus on:

1. C#
2. C
3. Windows x64 MASM Assembly

JavaScript, Java, COBOL, Objective-C, Swift, Python, and C++ toolchain implementations remain in source but are paused. They are not normal prerequisites and must not block active-target work.

The central `ActiveTargetLanguages` policy is the source that product, CLI, toolchain, and test enumeration share. Do not maintain independent target lists in each layer.

## Active Toolchain Detection

| Target | Detection | Normal use |
|---|---|---|
| C# | `dotnet --version` | Generate, build, and run with the .NET SDK |
| C | Visual Studio `vswhere.exe`, then `VC\Auxiliary\Build\vcvars64.bat` | Compile and link with the x64 C toolchain |
| MASM x64 | Same Visual Studio environment, plus `ml64` and `link.exe` | Assemble and link a Windows x64 console program |

SMILE uses `vswhere.exe` from the normal Visual Studio Installer location and does not hard-code a Visual Studio year, edition, or install directory.

Default detection should probe only active targets. A retained paused toolchain may still be detected by explicit re-enablement or focused maintenance work, but it is not part of ordinary startup readiness.

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

## Native INPUT Behavior

The current INPUT specification no longer requires one byte-level runtime implementation across targets.

Active generated programs should use normal destination facilities:

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

Restore and build the affected solution, then run the comprehensive suite appropriate to the active target set. Once paused tests are categorized, the intended milestone filter is:

```powershell
dotnet restore SMILE.sln
dotnet build SMILE.sln -c Debug --no-restore -nologo
dotnet test tests/SMILE.Tests/SMILE.Tests.csproj -c Debug --no-build --no-restore --filter "TestCategory!=HistoricalExactInput" -nologo
```

`HistoricalExactInput` keeps the superseded strict 4096-byte and exact all-target INPUT suite available for explicit historical or re-enablement checks without letting it override the current native INPUT contract. Categorize remaining paused-only coverage incrementally rather than delaying active work for a wholesale test reorganization.

Add Release validation when release risk justifies it. Do not automatically duplicate every Debug check in Release.

Legacy all-target environment gates such as `SMILE_REQUIRE_JAVA`, `SMILE_REQUIRE_ALL_TARGETS`, and cross-target `SMILE_REQUIRE_ZERO_TARGET_WARNINGS` are not routine requirements. Use them only for explicit historical verification or a target re-enablement milestone where they remain relevant.

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

## Paused Toolchain Reference

These retained implementations are not normal current prerequisites:

| Paused target | Retained toolchain implementation |
|---|---|
| JavaScript | Node.js |
| Java | A JDK containing both `javac` and `java` |
| COBOL | MSYS2 GnuCOBOL |
| Objective-C | MSYS2 MinGW64 Clang |
| Swift | Swift.Toolchain for Windows plus Visual Studio linker tools |
| Python | Python 3.10 or newer |
| C++ | Visual Studio x64 C++ tools |

Their detailed pre-reset commands and validation history are preserved under `Requirements/Archive/Pre-Strategic-Reset/` and in git history. Do not present a retained toolchain as currently supported until its backend completes re-enablement.

## Target Re-Enablement

For one paused target:

1. add the target to the central active-target list;
2. review every language and bound-tree change made while it was paused;
3. revise generated output to satisfy beginner-first native generation;
4. update detection and Build & Run for its current supported toolchain;
5. add readability guardrails and focused runtime/toolchain tests;
6. run the active milestone gate plus the target's catch-up conformance;
7. restore Desktop/CLI exposure and current documentation.

Do not re-enable a target only because its historical generator still produces a file.

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
- Desktop diagnostic logs: check `%LOCALAPPDATA%\SMILE\Logs`; if unavailable, SMILE falls back to `%TEMP%\SMILE\Logs`.
