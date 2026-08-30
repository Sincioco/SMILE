# Toolchains

## Policy

All ten generators are always available for source generation. Local Build & Run is available independently when a destination toolchain is detected. Missing optional tools do not remove a target or weaken canonical parsing.

Detection, compilation, linking, and execution are asynchronous and cancellation-aware. Desktop must remain responsive. Generated files and compiler outputs use unique verified workspaces beneath `%TEMP%\SMILE\Runs`.

## Registered destinations

| Target | Detection/build direction | Run direction |
|---|---|---|
| C# | .NET SDK builds the generated minimal project | generated console executable |
| C | Visual Studio x64 C compiler | generated executable |
| MASM x64 | Visual Studio `ml64` plus `link` and UCRT/Kernel32 libraries | generated executable |
| JavaScript | Node.js | `node program.js` |
| Java | JDK `javac` and `java` | generated `Program` class |
| COBOL | GnuCOBOL | generated executable |
| Objective-C | MSYS2/MinGW Clang | generated executable |
| Swift | Swift for Windows plus Visual Studio linker tools | generated executable with runtime path |
| Python | supported CPython | `python program.py` |
| C++ | Visual Studio x64 C++ compiler | generated executable |

Toolchain messages expose the detected version/location or a concise installation requirement. Detection failure is recoverable.

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

## JavaScript and Python

These are direct-run targets. Detection still validates an available supported runtime, and run results use the same timeout/cancellation model as compiled destinations. Python source remains a top-level executable script.

## Build/run result model

Every toolchain returns the target, detected status, build output, stdout, stderr, exit code, duration, timeout/cancellation flags, temporary working directory, and stage. A failed compiler or program does not crash Desktop.

Compiler/linker stdin is closed. Core BASIC 1 has no input statement, so generated programs do not request interactive stdin.

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
powershell -ExecutionPolicy Bypass -File scripts/Test-CoreBasicParity.ps1
```

Velocity Mode avoids duplicated Debug/Release matrices by default. It never authorizes skipping a known directly relevant failure.

## Manual CI

`SMILE CI` remains manually runnable with `workflow_dispatch`. Automatic triggers and the exact-SHA post-push gate remain suspended in Velocity Mode. Restore them only through an explicit strategy change.

## Failure containment

Build and run operations have bounded timeouts, honor cancellation, and terminate their child process tree when cancelled or timed out. Temporary cleanup is restricted to verified SMILE-owned roots. Never write generated target files into SMILE 2.0 during parity verification.
