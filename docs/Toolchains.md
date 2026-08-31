# Toolchains

## Policy

All ten generators are always available for source generation. Local Build & Run is available independently when a destination toolchain is detected. Missing optional tools do not remove a target or weaken canonical parsing.

Detection, compilation, linking, and execution are asynchronous and cancellation-aware. Desktop must remain responsive. Generated files and compiler outputs use unique verified workspaces beneath `%TEMP%\SMILE\Runs`.

## Registered destinations

| Target | Detection/build direction | Run direction |
|---|---|---|
| C# | .NET SDK builds the generated minimal project | generated console executable |
| C | Visual Studio x64 C compiler | generated executable |
| MASM x64 | Visual Studio `ml64` plus `link` and UCRT/Kernel32 libraries; MSVC compiles the feature-gated Text companion when needed | generated executable |
| JavaScript (Node.js) | Node.js | `node Program.js` |
| Java | JDK `javac` and `java` | generated `Program` class |
| COBOL | GnuCOBOL | generated executable |
| Objective-C | MSYS2/MinGW Clang | generated executable |
| Swift | Swift for Windows plus Visual Studio linker tools | generated executable with runtime path |
| Python | supported CPython | `python program.py` |
| C++ | Visual Studio x64 C++ compiler | generated executable |

Toolchain messages expose the detected version/location or a concise installation requirement. Detection failure is recoverable.

The Text-Game Foundation milestone was validated locally with .NET SDK 10.0.400, Visual Studio 2026 22.9.2 (MSVC 19.51.36256 and MASM 14.51.36256), Node.js 24.14.0, JDK 21.0.12.1, GnuCOBOL 3.2.0, Clang 21.1.6, Swift 6.3.3 for Windows, and Python 3.13.12. These are the versions tested by this repository, not a promise about older releases.

## C#

Generated C# contains `Program.cs` and a minimal `GeneratedProgram.csproj` targeting .NET 10. The companion file is required for deterministic local compilation and contains no SMILE runtime package.

## C, C++, and Objective-C

The Windows C and C++ paths use Visual Studio's x64 environment. Objective-C uses the dependency-light Clang path available through MSYS2/MinGW and therefore generates portable C-compatible Objective-C console source without requiring Foundation.

## MASM x64

The linker direction is equivalent to:

```text
link.exe /nologo /ignore:4210 Program.obj kernel32.lib legacy_stdio_definitions.lib ucrt.lib /subsystem:console /entry:main /out:Program.exe
```

Generated assembly follows the Windows x64 ABI, including required shadow space and stack alignment. It uses recognizable CRT output and `ExitProcess`. The known `LNK4210` warning associated with direct UCRT use and a custom assembly entry point is suppressed by the focused link command.

Programs without Text concatenation remain a single `Program.asm`. A program that uses Text `+` also receives dependency-free `SmileTextRuntime.c`; the assemble script compiles it with MSVC and the link step adds its object. That companion owns only allocation/root collection and optional lifetime counters. Generated assembly retains learner expressions, assignments, arrays, calls, returns, branches, and loops.

## JavaScript (Node.js) and Python

These are direct-run targets. `javascript` remains the stable CLI ID, the display name is JavaScript (Node.js), and generation produces dependency-free `Program.js` with no npm package or module requirement. Node syntax is checked before execution. Programs using `Get Key` or `Wait` receive a feature-driven async main; Wait uses a Promise, key input uses a raw-mode queue, and `finally` restores stdin. Python remains a top-level executable script and is syntax-compiled before execution; it uses Windows `msvcrt` only when key polling is present.

## Text-game console integration

- C#, C, Objective-C, MASM, Swift, and C++ use their normal Windows console/CRT facilities.
- Java requires JDK 21 and uses the standard Foreign Function & Memory API with `--enable-preview` to call `_kbhit`/`_getwch`; no JNA or external JAR is used.
- Swift uses Windows CRT symbols for key polling and WinSDK only for screen operations.
- GnuCOBOL links a generated `SmileRuntime.c` only when a used primitive needs C/Win32 interop. The companion contains terminal mechanics, never learner or game logic.
- `Clear Screen` erases the visible attached console, homes the cursor, and is a safe no-op when output is redirected. `Get Key` returns `KEY_NONE` when no attached interactive input event exists. Wait clamps to `4,294,967,295` milliseconds; reversed Random returns its lower bound without consuming randomness.
- Interactive conformance uses Windows ConPTY to prove W/A/S/D, real arrow sequences, Enter, Escape, Space, no-input polling, redraw, exit, and restored launcher input on every target.

## Build/run result model

Every toolchain returns the target, detected status, build output, stdout, stderr, exit code, duration, timeout/cancellation flags, temporary working directory, and stage. A failed compiler or program does not crash Desktop.

Compiler/linker stdin is closed. Ordinary runner execution supplies no input, so nonblocking `Get Key` observes `KEY_NONE`. Real-time key tests and games launch through an attached pseudo-console. Core BASIC 2.1 deliberately has no blocking console Input statement.

## Validation tiers

### Focused change

Run the smallest relevant project build and tests. Language/generator work also runs:

```powershell
dotnet test tests/SMILE.Tests/SMILE.Tests.csproj -c Debug --filter TestCategory=MissionGuardrail -nologo
```

### Normal generator change

Add focused generation assertions and run installed toolchains for the changed destinations. Report unavailable tools rather than claiming they ran.

### Language milestone or release

Run canonical conformance, deterministic generation for all ten, installed all-target build/run smoke, and pinned cross-repository parity:

```powershell
dotnet test tests/SMILE.Tests/SMILE.Tests.csproj -c Debug --filter TestCategory=CoreBasic -nologo
dotnet test tests/SMILE.Tests/SMILE.Tests.csproj -c Debug --filter TestCategory=Toolchain -nologo
dotnet test tests/SMILE.Tests/SMILE.Tests.csproj -c Debug --filter TestCategory=MilestoneMatrix -nologo
powershell -ExecutionPolicy Bypass -File scripts/Test-CoreBasicParity.ps1
```

Velocity Mode avoids duplicated Debug/Release matrices by default. It never authorizes skipping a known directly relevant failure.

## Manual CI

`SMILE CI` remains manually runnable with `workflow_dispatch`. Automatic triggers and the exact-SHA post-push gate remain suspended in Velocity Mode. Restore them only through an explicit strategy change.

## Failure containment

Build and run operations have bounded timeouts, honor cancellation, and terminate their child process tree when cancelled or timed out. Temporary cleanup is restricted to verified SMILE-owned roots. Never write generated target files into SMILE 2.0 during parity verification.
