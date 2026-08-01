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

SMILE uses `vswhere.exe` from `%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe` to locate Visual Studio. It does not hardcode the Visual Studio year, edition, or installation path.

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

## Temporary Workspaces

Each build/run writes generated files to:

```text
%TEMP%\SMILE\Runs\<unique-id>\
```

SMILE never builds generated targets inside the repository.

## Timeout And Cancellation

The default program timeout is 10 seconds. Cancellation and timeout terminate the entire child process tree where possible. Standard output and standard error are captured asynchronously to avoid deadlocks and UI freezes.

## Troubleshooting

- Missing .NET SDK: install .NET SDK 10 or newer.
- Missing C or MASM: install Visual Studio 2026 Enterprise or Build Tools with Desktop development with C++.
- Missing JavaScript runtime: install Node.js.
- Missing Java compiler: install a full JDK, not only a JRE.
