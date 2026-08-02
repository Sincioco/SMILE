# SMILE

SMILE is an educational, BASIC-inspired, multi-target transpiler. It is designed to bring a smile to new developers by showing that programming languages share the same fundamental ideas even when their syntax, compiler, runtime, and platform conventions differ.

Write a simple SMILE program once, then view equivalent programs in C#, C, Windows x64 MASM Assembly, JavaScript, Java, Objective-C, and Swift.

## Mission

SMILE v0.2.0, "Friendly PRINT," implements the official beginner-friendly `PRINT` syntax:

```text
SMILE source
  -> parse once into syntax nodes
  -> bind variables and string expressions once
  -> generate seven target programs from the bound program
  -> show three debounced live target previews in a responsive WPF desktop app
  -> build and run locally when the matching toolchain is installed
```

The official language specification for `PRINT` is published at [docs/SMILE-PRINT-Statement-Specification-v1.0.md](docs/SMILE-PRINT-Statement-Specification-v1.0.md).

## Guiding Principles

KISS means the simplest complete solution wins. SMILE avoids speculative features, unnecessary abstractions, heavy frameworks, parser generators, CLI frameworks, and third-party MVVM or process libraries.

KISS v2, "The Sin Way," puts user-experience performance first and functional performance second. Typing should feel immediate, build/run work should be cancellable, and compiler/toolchain work must not block the WPF UI thread.

Generated code follows the same rules: complete, minimal, idiomatic, readable, deterministic, educational, and dependency-light.

## Simple SMILE Program

```basic
LET Name = "Sin"

PRINT
PRINT "Hello World!"
PRINT Hello World!
PRINT Hello {Name}!
PRINT $"Hello {Name}!"
PRINT "Hello " + Name + "!"
PRINT Name
PRINT {Name}
PRINT Literal braces: {{Name}}
PRINT A; B; C
```

Output:

```text

Hello World!
Hello World!
Hello Sin!
Hello Sin!
Hello Sin!
Name
Sin
Literal braces: {Name}
A; B; C
```

## Friendly PRINT Syntax

Implemented grammar:

```text
program          -> line* end-of-file
line             -> whitespace* statement? whitespace* newline
statement        -> let-statement | print-statement
let-statement    -> LET identifier = string-literal
print-statement  -> PRINT
                  | PRINT hspace+ interpolated-string
                  | PRINT hspace+ quoted-string-expression
                  | PRINT hspace+ raw-template
```

Implemented rules:

- `PRINT` and `LET` are case-insensitive.
- Variable lookup is ordinal case-insensitive.
- `LET` currently declares string variables with string literal initializers, such as `LET Name = "Sin"`.
- `PRINT` alone, or followed only by spaces/tabs, prints one blank line.
- `PRINT "Hello"` prints an ordinary quoted string.
- Ordinary quoted strings do not interpolate, so `PRINT "Hello {Name}!"` prints `{Name}` literally.
- `PRINT Hello World!` is a raw template and prints `Hello World!`.
- Raw templates and `$"..."` support `{Name}` interpolation.
- `{{` and `}}` produce literal braces in raw templates and `$"..."`.
- `PRINT Name` prints the literal text `Name`.
- `PRINT {Name}` evaluates the variable `Name`.
- `PRINT "Hello " + Name + "!"` supports string concatenation for `PRINT`.
- A semicolon is not a statement separator. In raw templates, `PRINT A; B; C` prints the semicolons literally.
- A physical line may contain only one statement.
- A second standalone `PRINT` keyword on the same line is a compiler error.
- Quote omission is a `PRINT` convenience only; it does not make quote-free strings legal in `LET` or future expression positions.

Not implemented in v0.2.0: numeric types, comments, `INPUT`, conditions, loops, functions, arrays, classes, escaping embedded quotes inside SMILE strings, and non-literal `LET` initializers.

## Generated Examples

C#:

```csharp
using System;

internal static class Program
{
    private static void Main()
    {
        string Name = "Sin";
        Console.WriteLine("Hello " + Name + "!");
    }
}
```

C:

```c
#include <stdio.h>

int main(void)
{
    const char *Name = "Sin";
    fputs("Hello ", stdout);
    fputs(Name, stdout);
    fputs("!", stdout);
    putchar('\n');
    return 0;
}
```

JavaScript:

```javascript
let Name = "Sin";
console.log("Hello " + Name + "!");
```

Java:

```java
public final class Program
{
    public static void main(String[] args)
    {
        String Name = "Sin";
        System.out.println("Hello " + Name + "!");
    }
}
```

Objective-C:

```objc
#import <Foundation/Foundation.h>
#include <stdio.h>

int main(void)
{
    @autoreleasepool
    {
        NSString *Name = @"Sin";
        fputs("Hello ", stdout);
        fputs([Name UTF8String], stdout);
        fputs("!", stdout);
        putchar('\n');
    }

    return 0;
}
```

Swift:

```swift
let Name = "Sin"
print("Hello " + Name + "!")
```

MASM output uses UTF-8 byte labels for literal segments, a pointer-plus-length pair for each string variable, and one `WriteFile` call per printed segment. The generated assembly keeps right-side comments to support learning.

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

Run the desktop app:

```bat
cmd /c cd /d C:\SMILE && dotnet run --project src\SMILE.Desktop
```

Run the CLI developer harness:

```bat
cmd /c cd /d C:\SMILE && dotnet run --project src\SMILE.Cli -- examples\FriendlyPrint.smile --target all
cmd /c cd /d C:\SMILE && dotnet run --project src\SMILE.Cli -- examples\FriendlyPrint.smile --target csharp --run
```

Valid targets are `csharp`, `c`, `masm-x64`, `javascript`, `java`, `objective-c`, `swift`, and `all`.

## Desktop Application

The desktop app opens maximized with the Friendly PRINT sample. The top-left pane is editable SMILE source. The other three panes are read-only generated targets. They default to C#, Assembly - Windows x64 MASM, and C. Each generated pane can switch between C#, C, MASM x64, JavaScript, Java, Objective-C, and Swift.

![SMILE desktop app in maximized state](Requirements/Progress/2026-08-02-day-1-2-smile-desktop.png)

Typing in the SMILE source editor schedules a short debounced live transpilation for the visible target languages only. The latest source revision always wins, so stale generated code is never used for Build & Run. The Transpile All command is asynchronous and regenerates all seven targets.

Each generated pane supports Copy, Save Source, and Build & Run. JavaScript runs directly with Node.js, so its button says Run. Objective-C and Swift are transpile-only on Windows, so their buttons say Transpile Only and visible-language build runs skip them without failing the whole operation.

Diagnostics, build output, program output, exit code, total duration, timeout, cancellation, generated workspace paths, pause-launcher paths, and missing-tool messages appear in the output area. The output area scrolls to the newest text as build/run messages are appended. Successful automatic live transpiles do not erase build logs.

When Open Generated Folder is enabled, SMILE opens the generated-code workspace after a pane build/run. For Build & Run Visible Languages, it opens the shared SMILE run folder so the generated-code workspaces for all visible targets can be inspected. Generated workspace folder names end with the target language so learners can quickly identify them. If that folder is already open, SMILE brings the existing Explorer window to the front instead of opening a duplicate.

When Press Any Key Launcher is enabled, SMILE writes `Run Program - Press Any Key.cmd` into each successful build/run workspace. Double-clicking that launcher runs the generated program and then shows `Press any key to exit...`, which keeps the console window open long enough to inspect the output.

Current desktop build version: `0.2.0 Friendly PRINT`.

## Diagnostics

SMILE reports source errors as diagnostics instead of ordinary crashes. Current stable codes include:

| Code | Meaning |
|---|---|
| `SMILE1001` | Unknown statement or keyword |
| `SMILE1003` | Unterminated string literal |
| `SMILE1005` | Invalid or unexpected character |
| `SMILE1101` | `PRINT` requires whitespace before its payload |
| `SMILE1102` | Only one `PRINT` statement is allowed per line |
| `SMILE1103` | Unterminated interpolation expression |
| `SMILE1104` | Unexpected closing brace in template |
| `SMILE1105` | Interpolation expression cannot be empty |
| `SMILE1106` | Undefined variable |
| `SMILE1107` | Duplicate variable declaration |
| `SMILE1108` | Invalid string expression |
| `SMILE1109` | Semicolons cannot separate SMILE statements |
| `SMILE1110` | Unterminated interpolated string |
| `SMILE1111` | Unexpected text after a string expression |
| `SMILE1112` | `LET` requires a variable name |
| `SMILE1113` | `LET` requires `=` before its initializer |
| `SMILE1114` | `LET` currently requires a string literal initializer |

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
Source -> Parser -> Syntax Tree -> Binder -> Bound Program -> Target Generator -> Generated Files
                                                        |
                                                 Optional Toolchain
                                                        |
                                                 Build and Run Result
```

`SMILE.Engine` owns parsing, diagnostics, syntax nodes, binding, variable symbols, bound expressions, target-neutral print segment flattening, and target generators. `SMILE.Toolchains` owns detection, temporary workspaces, async process execution, cancellation, timeouts, build, and run. `SMILE.Cli` and `SMILE.Desktop` reuse both projects.

SMILE-owned build/output artifacts older than 1 day may be cleaned from known generated locations such as `bin`, `obj`, `out`, and `%TEMP%\SMILE\Runs`.

## Current Limitations

- `LET` is limited to string variables with string literal initializers.
- Embedded quote escaping inside SMILE string literals is not implemented.
- C and MASM target output is focused on Windows local toolchains.
- Objective-C and Swift output is transpile-only on Windows in this version.
- Unicode output beyond UTF-8 source text remains an area for later hardening.
- Full UI automation coverage is not included; manual WPF smoke testing is still required for release validation.

## Roadmap

Future ideas, not implemented in v0.2.0:

1. Non-literal `LET` initializers
2. Numeric and boolean expressions
3. `INPUT`
4. `IF / THEN / ELSE`
5. Loops
6. Functions
7. Type checking beyond string-only expressions
8. Debugging and source mapping
9. Reusable web interface
10. Evolution toward a full SMILE language

## License

SMILE is licensed under GNU Affero General Public License v3.0 only (`AGPL-3.0-only`). See [LICENSE](LICENSE).
