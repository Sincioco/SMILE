# Toolchains

Transpilation does not require target compilers or runtimes. Build & Run requires the matching local toolchain.

## Detection

| Target | Detection |
|---|---|
| C# | `dotnet --version` |
| C | Visual Studio `vswhere.exe`, then `VC\Auxiliary\Build\vcvars64.bat` |
| MASM x64 | Same Visual Studio C++ environment, plus `ml64` and `link.exe` |
| JavaScript | `node --version` |
| Java | `javac -version` and `java -version` |
| COBOL | MSYS2 `mingw64\bin\cobc.exe --version` |
| Objective-C | MSYS2 `mingw64\bin\clang.exe --version` |
| Swift | Swift.Toolchain layout plus Visual Studio C++ linker tools |
| Python | A real `python --version`, then `py -3 --version` or `py --version`; Python 3.10+ required |
| C++ | Visual Studio `vswhere.exe`, then `VC\Auxiliary\Build\vcvars64.bat` |

SMILE uses `vswhere.exe` from `%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe` to locate Visual Studio. It does not hardcode the Visual Studio year, edition, or installation path. Swift Build & Run uses this same Visual Studio environment because Swift for Windows links through the Microsoft toolchain.

## Commands

C#:

```bat
dotnet build GeneratedProgram.csproj -nologo
dotnet run --project GeneratedProgram.csproj --no-build
```

C:

```bat
call "<vcvars64.bat>" >nul
cl.exe /nologo /TC /utf-8 Program.c /Fe:Program.exe
Program.exe
```

MASM x64:

```bat
call "<vcvars64.bat>" >nul
ml64 /nologo /c Program.asm /Fo:Program.obj
link.exe /nologo Program.obj kernel32.lib /subsystem:console /entry:main /out:Program.exe
Program.exe
```

JavaScript:

```bat
node Program.js
```

Java:

```bat
javac -encoding UTF-8 Program.java
java Program
```

SMILE accepts Java as build-capable only when one real directory contains both
`javac.exe` and `java.exe` and both version probes succeed. Detection checks
ordinary `PATH` entries first, then `%JAVA_HOME%\bin`, then the existing known
JDK vendor folders under Program Files. It resolves the executable paths before
running them, so a runtime from one installation cannot be paired with a
compiler from another. Windows Store aliases under `Microsoft\WindowsApps` are
ignored and are never launched.

The Desktop status distinguishes `Full JDK detected`, `Java runtime detected,
but javac is missing`, and `JDK missing`. A runtime-only installation can run
already compiled classes, but SMILE correctly leaves Java Build & Run disabled
because it cannot compile `Program.java`.

A free Microsoft OpenJDK 25 LTS installation can be added with:

```bat
winget install --id Microsoft.OpenJDK.25 --exact
```

Restart the terminal or desktop app after installation so its process receives the updated `PATH`. A user-local extracted JDK also works when `JAVA_HOME` points at its root and `%JAVA_HOME%\bin` is on the user `PATH`.

### Hosted CI and strict local release validation

The `SMILE CI` workflow in `.github/workflows/smile-ci.yml` runs on pushes to
`main`, pull requests targeting `main`, and manual dispatch. Its
`windows-latest` job uses .NET SDK 10.0.302 to restore the solution, then build
and test Debug and Release independently. It validates the SMILE solution and
the unit/integration tests supported by the hosted runner. It does not install
or claim coverage for every destination-language toolchain.

The v0.7.0 Java and generated-target acceptance tests remain environment-aware
for contributors who do not have every local toolchain. Official release
validation is therefore a separate local gate that makes Java and all ten
targets mandatory and enables generated compiler-warning validation. This
ensures the normative INPUT, invalid-input, checked-arithmetic, and full-line
comment/source-layout programs, the cumulative `language.smile` reference, and
the established all-target runtime conformance programs execute through every
destination instead of skipping. Scripted runs provide stdin directly rather
than through shell `echo`, then compare exact stdout, stderr, exit code, and
generated compiler warnings with the reference evaluator. Structural generation
tests cover nested branches, native comment preservation, source blank-line
boundaries, comment-safe Block String/IF recovery, and wide/NUL/input planning
without requiring a local toolchain. Run the complete local gate from
the repository root before a release commit:

```powershell
$ErrorActionPreference = 'Stop'
dotnet restore SMILE.sln
$env:SMILE_REQUIRE_JAVA = '1'
$env:SMILE_REQUIRE_ALL_TARGETS = '1'
$env:SMILE_REQUIRE_ZERO_TARGET_WARNINGS = '1'
dotnet build SMILE.sln -c Debug --no-restore -nologo
dotnet test SMILE.sln -c Debug --no-build --no-restore -nologo
dotnet build SMILE.sln -c Release --no-restore -nologo
dotnet test SMILE.sln -c Release --no-build --no-restore -nologo
Remove-Item Env:SMILE_REQUIRE_JAVA
Remove-Item Env:SMILE_REQUIRE_ALL_TARGETS
Remove-Item Env:SMILE_REQUIRE_ZERO_TARGET_WARNINGS
```

Each acceptance test records the selected tools, compiler success, program exit
code, and exact logical UTF-8 stdout and stderr comparison with `SmileEvaluator`.
Machine-specific paths are validation output only and are never stored in the
repository. The warning suite inspects retained compiler output with
destination-specific diagnostic patterns. C#, C, MASM x64, Java, COBOL,
Objective-C, Swift, and C++ are compiler-backed; JavaScript and Python are
interpreted targets with no compile stage in their normal SMILE toolchains.
Every compiler-backed INPUT/IF run must report zero detected warnings, and every
generated source must retain all INPUT operations and IF, ELSE IF, and ELSE bodies.

COBOL:

```bat
set "PATH=<msys64>\mingw64\bin;<msys64>\usr\bin;%PATH%"
set "COB_CONFIG_DIR=<msys64>\mingw64\share\gnucobol\config"
"<msys64>\mingw64\bin\cobc.exe" -x -free Program.cob -o Program.exe
Program.exe
```

SMILE generates free-format COBOL with `>>SOURCE FORMAT IS FREE`. `COB_CONFIG_DIR` is set explicitly because launching `cobc.exe` from a native Windows process can otherwise confuse MSYS-style and Windows-style config paths.

Objective-C:

```bat
set "PATH=<msys64>\mingw64\bin;<msys64>\usr\bin;%PATH%"
"<msys64>\mingw64\bin\clang.exe" -x objective-c Program.m -o Program.exe
Program.exe
```

Objective-C currently uses SMILE's Foundation-free console profile on Windows. The generated file is still compiled as Objective-C (`.m`), but SMILE avoids Foundation/NSString until that runtime path is hardened locally.

Swift:

```bat
call "<vcvars64.bat>" >nul
set "PATH=<Swift toolchain>\usr\bin;<Swift runtime>\usr\bin;<Swift Python>\usr\bin;%PATH%"
"<Swift toolchain>\usr\bin\swiftc.exe" -sdk "<Swift Windows.sdk>" Program.swift -o Program.exe
Program.exe
```

Python:

```bat
python -B Program.py
```

If the Python launcher is selected, SMILE uses `py -3 -B Program.py`. Detection resolves real executables from `PATH`, ignores the Windows Store `WindowsApps` alias so it cannot trigger an on-demand installation, rejects Python 2 and Python versions older than 3.10, and reports the selected executable and version. `-B` prevents `__pycache__` bytecode output in the temporary workspace.

C++:

```bat
call "<vcvars64.bat>" >nul
cl.exe /nologo /EHsc /std:c++20 /utf-8 Program.cpp /Fe:Program.exe
Program.exe
```

SMILE uses the same `VisualStudioLocator` as C and MASM, but C++ has its own toolchain and is compiled as C++20 rather than through C's `/TC` mode. No package manager, third-party C++ library, or SMILE runtime library is required.

## Standard Input Modes

`ProcessRunner` and generated-program execution distinguish three standard-input modes:

| Mode | Purpose | Behavior |
|---|---|---|
| `Closed` | Detection, compilers, and captured programs that do not need input | Redirect stdin and close it immediately so a child cannot wait invisibly |
| `ScriptedText` | Deterministic evaluator-versus-target tests | Redirect stdin, write the complete supplied input, flush, close, and capture stdout/stderr/exit |
| `InteractiveInherited` | Normal CLI and visible Desktop INPUT execution | Use the invoking terminal or a visible console, stream prompts and errors live, and preserve the program exit code |

Compiler and linker processes always use closed input. Scripted input is supplied only to the generated executable. Tests do not make shell-specific `echo` pipelines the conformance path because those pipelines can alter Unicode, embedded NUL, quoting, CR/LF distinctions, and a final line without a newline.

When the CLI runs a generated program containing INPUT, the learner's standard input is inherited naturally, including redirected stdin, and output is not held until process exit. A Desktop Build & Run of an INPUT program builds through the ordinary captured path, then opens exactly one visible interactive console so each PRINT prompt is visible before the learner enters the corresponding line. The Desktop reports that launch and remains responsive while the generated program waits; it does not start a second hidden copy with stdin closed.

Interactive programs may legitimately wait for the learner, while scripted tests retain the normal program timeout and cancellation behavior. Detection and build processes must never wait for input. Runtime input failures write the canonical SMILE runtime line to stderr and return exit code 1; successful generated programs return 0.

## Temporary Workspaces

Each build/run writes generated files to:

```text
%TEMP%\SMILE\Runs\<unique-id> - <language>\
```

SMILE never builds generated targets inside the repository.

The language suffix helps learners identify which generated-code workspace belongs to each target.

SMILE-owned temporary run workspaces older than 1 day may be deleted automatically.

Cleanup failures are non-fatal. SMILE may report or log them, but it should still create the new workspace needed for the current build/run.

## Pause Launcher

When requested by the desktop app, a successful build/run also writes:

```text
Run Program - Press Any Key.cmd
```

That launcher runs the generated program from the temporary workspace with normal interactive stdin, prints `Press any key to exit...` after it finishes, and waits for a key before closing. Keeping this behavior in a companion script preserves idiomatic target-language source while giving learners a double-click path that supports INPUT and leaves the console visible.

## Timeout And Cancellation

The default captured or scripted program timeout is 10 seconds. Cancellation and timeout terminate the entire child process tree where possible. Standard output and standard error are captured asynchronously to avoid deadlocks and UI freezes. A deliberately visible interactive INPUT program may wait for the learner; its process work still stays off the WPF UI thread and failure containment remains best-effort.

Each process stream is retained up to 1,000,000 characters. SMILE keeps draining the child process pipes after the display cap is reached, then appends a truncation marker so learners can see that output was shortened. This keeps runaway generated programs from consuming unbounded desktop memory.

Detection, build, run, timeout, cancellation, folder opening, and process-launch failures are expected to be recoverable desktop events. A failing target should produce an output message and, when available, a diagnostic log path instead of closing the IDE or blocking other visible targets.

## Troubleshooting

- Missing .NET SDK: install .NET SDK 10 or newer.
- Missing C, C++, or MASM: install Visual Studio 2026 Enterprise or Build Tools with Desktop development with C++.
- Missing JavaScript runtime: install Node.js.
- Java runtime detected but `javac` missing: install a full JDK such as Microsoft OpenJDK 25 LTS, not only a JRE, then restart SMILE so it receives the updated environment.
- JDK missing: install a full JDK and expose its `bin` directory through `PATH` or point `JAVA_HOME` at the JDK root. SMILE does not launch Windows Store aliases or installers.
- Missing COBOL compiler: install MSYS2 and `mingw-w64-x86_64-gnucobol`.
- Missing Objective-C compiler: install MSYS2 and `mingw-w64-x86_64-clang`.
- Missing Swift compiler: install Swift.Toolchain for Windows and the Visual Studio C++ linker tools.
- Missing Python interpreter: install Python 3.10 or newer. No Python packages or virtual environment are required.
- Desktop diagnostic logs: check `%LOCALAPPDATA%\SMILE\Logs`; if that folder is unavailable, SMILE falls back to `%TEMP%\SMILE\Logs`.
