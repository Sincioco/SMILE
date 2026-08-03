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
javac Program.java
java Program
```

A free Microsoft OpenJDK 25 LTS installation can be added with:

```bat
winget install --id Microsoft.OpenJDK.25 --exact
```

Restart the terminal or desktop app after installation so its process receives the updated `PATH`. A user-local extracted JDK also works when `JAVA_HOME` points at its root and `%JAVA_HOME%\bin` is on the user `PATH`.

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

That launcher runs the generated program from the temporary workspace, prints `Press any key to exit...`, and waits for a key before closing. Keeping this behavior in a companion script preserves idiomatic target-language source while giving learners a double-click path that leaves the console visible.

## Timeout And Cancellation

The default program timeout is 10 seconds. Cancellation and timeout terminate the entire child process tree where possible. Standard output and standard error are captured asynchronously to avoid deadlocks and UI freezes.

Each process stream is retained up to 1,000,000 characters. SMILE keeps draining the child process pipes after the display cap is reached, then appends a truncation marker so learners can see that output was shortened. This keeps runaway generated programs from consuming unbounded desktop memory.

Detection, build, run, timeout, cancellation, folder opening, and process-launch failures are expected to be recoverable desktop events. A failing target should produce an output message and, when available, a diagnostic log path instead of closing the IDE or blocking other visible targets.

## Troubleshooting

- Missing .NET SDK: install .NET SDK 10 or newer.
- Missing C or MASM: install Visual Studio 2026 Enterprise or Build Tools with Desktop development with C++.
- Missing JavaScript runtime: install Node.js.
- Missing Java compiler: install a full JDK such as Microsoft OpenJDK 25 LTS, not only a JRE.
- Missing COBOL compiler: install MSYS2 and `mingw-w64-x86_64-gnucobol`.
- Missing Objective-C compiler: install MSYS2 and `mingw-w64-x86_64-clang`.
- Missing Swift compiler: install Swift.Toolchain for Windows and the Visual Studio C++ linker tools.
- Missing Python interpreter: install Python 3.10 or newer. No Python packages or virtual environment are required.
- Desktop diagnostic logs: check `%LOCALAPPDATA%\SMILE\Logs`; if that folder is unavailable, SMILE falls back to `%TEMP%\SMILE\Logs`.
