# SMILE

SMILE is an educational, BASIC-inspired, multi-target transpiler. It is designed to bring a smile to new developers by showing that programming languages share the same fundamental ideas even when their syntax, compiler, runtime, and platform conventions differ.

Write a simple SMILE program once, then view, build, and run equivalent programs in C#, C, Windows x64 MASM Assembly, JavaScript, and Java.

## Mission

SMILE v0.1, "PRINT Everywhere," proves one small vertical slice:

```text
SMILE PRINT source
  -> parse once
  -> generate five target programs directly from the SMILE syntax tree
  -> show three target programs at a time in a responsive WPF desktop app
  -> build and run locally when the matching toolchain is installed
```

## Guiding Principles

KISS means the simplest complete solution wins. SMILE avoids speculative features, unnecessary abstractions, heavy frameworks, parser generators, CLI frameworks, and third-party MVVM or process libraries.

KISS v2, "The Sin Way," puts user-experience performance first and functional performance second. The app should feel immediate, clear, cancellable, and stable. After that, the code avoids repeated parsing, unnecessary process launches, and needless disk work.

Generated code follows the same rules: complete, minimal, idiomatic, readable, deterministic, educational, and dependency-light.

## WPF Responsiveness

The WPF UI thread must not block on toolchain detection, compilation, linking, execution, process output, long file operations, or other noticeable work. Build and run operations use async APIs, cancellation tokens, timeouts, and process-tree termination.

## Simple SMILE Program

```basic
PRINT "Hello from SMILE!"
PRINT "Different syntax, same idea."
```

## Generated Examples

C#:

```csharp
using System;

internal static class Program
{
    private static void Main()
    {
        Console.WriteLine("Hello from SMILE!");
        Console.WriteLine("Different syntax, same idea.");
    }
}
```

C:

```c
#include <stdio.h>

int main(void)
{
    puts("Hello from SMILE!");
    puts("Different syntax, same idea.");
    return 0;
}
```

Assembly - Windows x64 MASM:

```asm
option casemap:none

EXTERN GetStdHandle:PROC
EXTERN WriteFile:PROC
EXTERN ExitProcess:PROC

STD_OUTPUT_HANDLE EQU -11

.data
message0 BYTE "Hello from SMILE!", 13, 10
message0Length EQU $ - message0
message1 BYTE "Different syntax, same idea.", 13, 10
message1Length EQU $ - message1
bytesWritten DWORD ?

.code
main PROC
    sub rsp, 28h

    mov ecx, STD_OUTPUT_HANDLE
    call GetStdHandle

    mov rcx, rax
    lea rdx, message0
    mov r8d, message0Length
    lea r9, bytesWritten
    mov QWORD PTR [rsp + 20h], 0
    call WriteFile

    mov ecx, STD_OUTPUT_HANDLE
    call GetStdHandle

    mov rcx, rax
    lea rdx, message1
    mov r8d, message1Length
    lea r9, bytesWritten
    mov QWORD PTR [rsp + 20h], 0
    call WriteFile

    xor ecx, ecx
    call ExitProcess
main ENDP

END
```

JavaScript:

```javascript
console.log("Hello from SMILE!");
console.log("Different syntax, same idea.");
```

Java:

```java
public final class Program
{
    public static void main(String[] args)
    {
        System.out.println("Hello from SMILE!");
        System.out.println("Different syntax, same idea.");
    }
}
```

## Supported Targets

| Stable ID | Display name | Generated files | Build & Run toolchain |
|---|---|---|---|
| `csharp` | C# | `Program.cs`, `GeneratedProgram.csproj` | .NET SDK 10 or newer |
| `c` | C | `Program.c` | Visual Studio C++ x64 tools |
| `masm-x64` | Assembly - Windows x64 MASM | `Program.asm` | Visual Studio C++ x64 tools with `ml64` and `link.exe` |
| `javascript` | JavaScript | `Program.js` | Node.js |
| `java` | Java | `Program.java` | JDK with `javac` and `java` |

Transpilation works even when optional target toolchains are missing. Build & Run is enabled only when the matching local tools are detected.

## Requirements

- Windows
- .NET SDK 10 or newer
- Visual Studio 2026 Enterprise or Build Tools with Desktop development with C++ for C and MASM
- Optional: Node.js for JavaScript
- Optional: JDK for Java

Visual Studio setup must include the x64 C++ tools and `VC\Auxiliary\Build\vcvars64.bat`. SMILE discovers Visual Studio with `vswhere.exe`; it does not hardcode an edition or install folder.

## Build, Test, And Run

```bat
cmd /c git clone https://github.com/Sincioco/SMILE.git C:\SMILE
cmd /c cd /d C:\SMILE && dotnet restore SMILE.sln
cmd /c cd /d C:\SMILE && dotnet build SMILE.sln -c Debug
cmd /c cd /d C:\SMILE && dotnet test SMILE.sln -c Debug --no-build
cmd /c cd /d C:\SMILE && dotnet build SMILE.sln -c Release
cmd /c cd /d C:\SMILE && dotnet test SMILE.sln -c Release --no-build
```

Open `SMILE.sln` in Visual Studio 2026 Enterprise to work with the solution.

Run the desktop app:

```bat
cmd /c cd /d C:\SMILE && dotnet run --project src\SMILE.Desktop
```

## Desktop Application

The desktop app opens with this example:

```basic
PRINT "Hello from SMILE!"
PRINT "Different syntax, same idea."
```

The top-left pane is editable SMILE source. The other three panes are read-only generated targets. They default to C#, Assembly - Windows x64 MASM, and C. Each generated pane can switch between C#, C, MASM x64, JavaScript, and Java.

Main actions:

- New
- Open `.smile`
- Save
- Save As
- Transpile All
- Build & Run Visible Languages
- Cancel while work is active

Each generated pane supports Copy, Save Source, and Build & Run. JavaScript runs directly with Node.js, so its button says Run.

Diagnostics, build output, program output, exit code, duration, timeout, cancellation, and missing-tool messages appear in the output area.

## CLI Developer Harness

Generate all targets:

```bat
cmd /c cd /d C:\SMILE && dotnet run --project src\SMILE.Cli -- examples\PrintEverywhere.smile --target all
```

Build and run one target:

```bat
cmd /c cd /d C:\SMILE && dotnet run --project src\SMILE.Cli -- examples\PrintEverywhere.smile --target csharp --run
```

Valid targets are `csharp`, `c`, `masm-x64`, `javascript`, `java`, and `all`.

## SMILE v0.1 Syntax

Implemented grammar:

```text
program         -> line* end-of-file
line            -> whitespace* statement? whitespace* newline
statement       -> print-statement
print-statement -> PRINT whitespace+ string-literal
```

Rules:

- `PRINT` is case-insensitive.
- Blank lines are allowed.
- Multiple `PRINT` statements are allowed.
- The final newline is optional.
- Straight and smart double-quote delimiters are accepted.
- `PRINT` appends a newline.
- Syntax errors produce stable diagnostics instead of ordinary crashes.

Not implemented in v0.1: variables, `LET`, `INPUT`, expressions, comments, conditions, loops, labels, `GOTO`, functions, classes, debugging, syntax highlighting, package management, cloud compilation, and arbitrary editing or compilation of generated target code.

## Repository Structure

```text
SMILE.sln
README.md
LICENSE
AGENTS.md
.editorconfig
.gitignore
examples/
docs/
src/
  SMILE.Engine/
  SMILE.Toolchains/
  SMILE.Cli/
  SMILE.Desktop/
tests/
  SMILE.Tests/
```

## Architecture

```text
Source -> Lexer -> Parser -> Syntax Tree -> Target Generator -> Generated Files
                                             |
                                      Optional Toolchain
                                             |
                                      Build and Run Result
```

`SMILE.Engine` owns the lexer, parser, diagnostics, AST, transpiler facade, and target generators. `SMILE.Toolchains` owns detection, temporary workspaces, async process execution, cancellation, timeouts, build, and run. `SMILE.Cli` and `SMILE.Desktop` reuse both projects.

SMILE-owned build/output artifacts older than 2 days may be cleaned from known generated locations such as `bin`, `obj`, `out`, and `%TEMP%\SMILE\Runs`.

## Current Limitations

- Only `PRINT "text"` is supported.
- Embedded quote escaping inside SMILE string literals is not implemented.
- C and MASM target output is focused on Windows local toolchains.
- Unicode output beyond simple source text is an area for later hardening.
- WPF launch, resize, Build & Run command availability, invocation, and Cancel enabled state were smoke-tested with UI Automation; full UI automation coverage is not included.

## Roadmap

Future ideas, not implemented in v0.1:

1. `LET` and variables
2. Printing variables and expressions
3. `INPUT`
4. Numeric and string expressions
5. `IF / THEN / ELSE`
6. Loops
7. Functions
8. Type checking
9. Debugging and source mapping
10. Reusable web interface
11. Evolution toward a full SMILE language

## License

SMILE is licensed under GNU Affero General Public License v3.0 only (`AGPL-3.0-only`). See [LICENSE](LICENSE).
