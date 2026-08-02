# SMILE

SMILE is an educational, BASIC-inspired, multi-target transpiler. It is designed to bring a smile to new developers by showing that programming languages share the same fundamental ideas even when their syntax, compiler, runtime, and platform conventions differ.

Write a simple SMILE program once, then view equivalent programs in C#, C, Windows x64 MASM Assembly, JavaScript, Java, Objective-C, and Swift.

## Mission

SMILE v0.1.2, "PRINT Everywhere," proves one small vertical slice:

```text
SMILE PRINT source
  -> parse once
  -> generate seven target programs directly from the SMILE syntax tree
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
option casemap:none                             ; Keep symbol names case-sensitive.

EXTERN GetStdHandle:PROC                        ; Windows API: get standard console handles.
EXTERN WriteFile:PROC                           ; Windows API: write bytes to the console.
EXTERN ExitProcess:PROC                         ; Windows API: terminate the process.

STD_OUTPUT_HANDLE EQU -11                       ; Magic value for the console output handle.

.data                                           ; Static bytes and variables live here.
message0 BYTE "Hello from SMILE!", 13, 10       ; PRINT text #1, ending with CR/LF.
message0Length EQU $ - message0                 ; Length equals current address minus the label.
message1 BYTE "Different syntax, same idea.", 13, 10 ; PRINT text #2, ending with CR/LF.
message1Length EQU $ - message1                 ; Length equals current address minus the label.
bytesWritten DWORD ?                            ; WriteFile stores how many bytes it wrote.

.code                                           ; CPU instructions live here.
main PROC                                       ; Program entry point.
    sub rsp, 28h                                ; Reserve Win64 shadow space and align the stack.

    mov ecx, STD_OUTPUT_HANDLE                  ; First argument: ask for stdout.
    call GetStdHandle                           ; RAX now holds the stdout handle.

    mov rcx, rax                                ; WriteFile arg 1: stdout handle.
    lea rdx, message0                           ; WriteFile arg 2: address of message bytes.
    mov r8d, message0Length                     ; WriteFile arg 3: byte count.
    lea r9, bytesWritten                        ; WriteFile arg 4: address for bytes-written result.
    mov QWORD PTR [rsp + 20h], 0                ; WriteFile arg 5 on stack: no overlapped I/O.
    call WriteFile                              ; Emit the PRINT line.

    mov ecx, STD_OUTPUT_HANDLE                  ; First argument: ask for stdout.
    call GetStdHandle                           ; RAX now holds the stdout handle.

    mov rcx, rax                                ; WriteFile arg 1: stdout handle.
    lea rdx, message1                           ; WriteFile arg 2: address of message bytes.
    mov r8d, message1Length                     ; WriteFile arg 3: byte count.
    lea r9, bytesWritten                        ; WriteFile arg 4: address for bytes-written result.
    mov QWORD PTR [rsp + 20h], 0                ; WriteFile arg 5 on stack: no overlapped I/O.
    call WriteFile                              ; Emit the PRINT line.

    xor ecx, ecx                                ; ExitProcess arg 1: process exit code 0.
    call ExitProcess                            ; End the program.
main ENDP                                       ; End of the main procedure.

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

Objective-C:

```objc
#import <Foundation/Foundation.h>

int main(int argc, const char * argv[])
{
    @autoreleasepool
    {
        NSLog(@"Hello from SMILE!");
        NSLog(@"Different syntax, same idea.");
    }

    return 0;
}
```

Swift:

```swift
print("Hello from SMILE!")
print("Different syntax, same idea.")
```

## Supported Targets

| Stable ID | Display name | Generated files | Build & Run toolchain |
|---|---|---|---|
| `csharp` | C# | `Program.cs`, `GeneratedProgram.csproj` | .NET SDK 10 or newer |
| `c` | C | `Program.c` | Visual Studio C++ x64 tools |
| `masm-x64` | Assembly - Windows x64 MASM | `Program.asm` | Visual Studio C++ x64 tools with `ml64` and `link.exe` |
| `javascript` | JavaScript | `Program.js` | Node.js |
| `java` | Java | `Program.java` | JDK with `javac` and `java` |
| `objective-c` | Objective-C | `Program.m` | Transpile only on Windows |
| `swift` | Swift | `Program.swift` | Transpile only on Windows |

Transpilation works even when optional target toolchains are missing. Build & Run is enabled only when the matching local tools are detected. Objective-C and Swift are available for source inspection, copy, and save on Windows, but local Build & Run is intentionally disabled until SMILE has a supported compiler path for them.

## Requirements

- Windows
- .NET SDK 10 or newer
- Visual Studio 2026 Enterprise or Build Tools with Desktop development with C++ for C and MASM
- Optional: Node.js for JavaScript
- Optional: JDK 25 LTS or newer for Java

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

The desktop app opens maximized. The top-left pane is editable SMILE source. The other three panes are read-only generated targets. They default to C#, Assembly - Windows x64 MASM, and C. Each generated pane can switch between C#, C, MASM x64, JavaScript, Java, Objective-C, and Swift.

![SMILE desktop app in maximized state](Requirements/Progress/2026-08-02-day-1-2-smile-desktop.png)

Main actions:

- New
- Open `.smile`
- Save
- Save As
- Transpile All
- Build & Run Visible Languages
- Cancel while work is active
- Open Generated Folder toggle
- Press Any Key Launcher toggle
- File menu for New, Open `.smile`, Save, Save As, and Exit
- Help menu with About SMILE and the current desktop build version

Each generated pane supports Copy, Save Source, and Build & Run. JavaScript runs directly with Node.js, so its button says Run.

Diagnostics, build output, program output, exit code, duration, timeout, cancellation, generated workspace paths, pause-launcher paths, and missing-tool messages appear in the output area. The output area scrolls to the newest text as build/run messages are appended.

When Open Generated Folder is enabled, SMILE opens the generated-code workspace after a pane build/run. For Build & Run Visible Languages, it opens the shared SMILE run folder so the generated-code workspaces for all visible targets can be inspected. Generated workspace folder names end with the target language so learners can quickly identify them. If that folder is already open, SMILE brings the existing Explorer window to the front instead of opening a duplicate.

When Press Any Key Launcher is enabled, SMILE writes `Run Program - Press Any Key.cmd` into each successful build/run workspace. Double-clicking that launcher runs the generated program and then shows `Press any key to exit...`, which keeps the console window open long enough to inspect the output.

Current desktop build version: `0.1.2 PRINT Everywhere`.

## CLI Developer Harness

Generate all targets:

```bat
cmd /c cd /d C:\SMILE && dotnet run --project src\SMILE.Cli -- examples\PrintEverywhere.smile --target all
```

Build and run one target:

```bat
cmd /c cd /d C:\SMILE && dotnet run --project src\SMILE.Cli -- examples\PrintEverywhere.smile --target csharp --run
```

Valid targets are `csharp`, `c`, `masm-x64`, `javascript`, `java`, `objective-c`, `swift`, and `all`.

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
Requirements/
docs/
src/
  SMILE.Engine/
  SMILE.Toolchains/
  SMILE.Cli/
  SMILE.Desktop/
tests/
  SMILE.Tests/
```

`Requirements/` stores daily project instructions and can also hold future designs, sketches, diagrams, and ideas.

## Architecture

```text
Source -> Lexer -> Parser -> Syntax Tree -> Target Generator -> Generated Files
                                             |
                                      Optional Toolchain
                                             |
                                      Build and Run Result
```

`SMILE.Engine` owns the lexer, parser, diagnostics, AST, transpiler facade, and target generators. `SMILE.Toolchains` owns detection, temporary workspaces, async process execution, cancellation, timeouts, build, and run. `SMILE.Cli` and `SMILE.Desktop` reuse both projects.

SMILE-owned build/output artifacts older than 1 day may be cleaned from known generated locations such as `bin`, `obj`, `out`, and `%TEMP%\SMILE\Runs`.

## Current Limitations

- Only `PRINT "text"` is supported.
- Embedded quote escaping inside SMILE string literals is not implemented.
- C and MASM target output is focused on Windows local toolchains.
- Objective-C and Swift output is transpile-only on Windows in this version.
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
