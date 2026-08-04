# SMILE

SMILE stands for Simple Modern Interactive Learning Environment. It is an educational, BASIC-inspired, multi-target transpiler designed to bring a smile to new developers by showing that programming languages share the same fundamental ideas even when their syntax, compiler, runtime, and platform conventions differ.

Write a simple SMILE program once, then view equivalent programs in C#, C, COBOL, Windows x64 MASM Assembly, JavaScript, Java, Objective-C, Swift, and Python.

## Mission

A programming language inspired by BASIC that makes it easy for newcomers to learn and understand how programming languages work across the board. Updated for the modern era, SMILE takes the classic BASIC programming language and takes it to the next level by offering to teach not just concepts and ideas of what a programming language can do but show them how various programming languages look like by transpiling (translating) and compiling their SMILE code to many other programming languages. So students can learn many programming languages simultaneously and arrive at one obvious conclusion: all programming languages share the same fundamentals. What's important is learning to think logically and understand how to solve problems with code, not learning the syntax of a particular programming language. SMILE is designed to be a fun and educational programming language that teaches students how to think like a programmer and understand the fundamentals of programming languages.

## Video Introduction

[![Watch the SMILE introduction on YouTube](https://img.youtube.com/vi/fgyIMCdHcug/hqdefault.jpg)](https://www.youtube.com/watch?v=fgyIMCdHcug)

[Watch the SMILE introduction on YouTube](https://www.youtube.com/watch?v=fgyIMCdHcug).

## Current Release

SMILE v0.4.2.1, "Exact String and Target-Safe Expression Hardening," preserves Python as the ninth first-class destination while making embedded-NUL Strings exact in C and Objective-C and applying known-Boolean short-circuit simplification in every expression position:

```text
SMILE source
  -> lex source into tokens
  -> parse once into syntax nodes
  -> bind variables and typed expressions once
  -> evaluate compile-time constants once
  -> simplify pure bound expressions once with known constant values and reachability
  -> map SMILE symbols to safe target identifiers once per target
  -> choose one idiomatic target Integer profile for the complete bound program
  -> generate nine target programs from the bound program
  -> compare generated runtime behavior to the SMILE reference evaluator in tests
  -> show three debounced live target previews with line numbers and syntax highlighting
  -> build and run locally when the matching toolchain is installed
  -> keep the desktop IDE responsive when a target toolchain fails or target languages are switched rapidly
```

The official language specifications are published in [docs/SMILE Language Specification](docs/SMILE%20Language%20Specification):

- [SMILE - LET Statement Official Specification v1.0](docs/SMILE%20Language%20Specification/SMILE%20-%20LET%20Statement%20Official%20Specification%20v1.0.md)
- [SMILE - PRINT Statement Official Specification v1.0](docs/SMILE%20Language%20Specification/SMILE%20-%20PRINT%20Statement%20Official%20Specification%20v1.0.md)
- [SMILE - String Literals Official Specification v1.0](docs/SMILE%20Language%20Specification/SMILE%20-%20String%20Literals%20Official%20Specification%20v1.0.md)
- [SMILE - Core Types and Expressions Official Specification v1.0](docs/SMILE%20Language%20Specification/SMILE%20-%20Core%20Types%20and%20Expressions%20Official%20Specification%20v1.0.md)

## Guiding Principles

KISS means the simplest complete solution wins. SMILE avoids speculative features, unnecessary abstractions, heavy frameworks, parser generators, CLI frameworks, and third-party MVVM or process libraries.

KISS v2, "The Sin Way," puts user-experience performance first and functional performance second. Typing should feel immediate, build/run work should be cancellable, and compiler/toolchain work must not block the WPF UI thread.

Generated code follows the same rules: complete, minimal, idiomatic, readable, deterministic, educational, and dependency-light.

## Simple SMILE Program

```basic
LET Name = "Sin"
LET Age = 49
LET Adult = Age >= 18
LET Message = $"Hello {Name}! Age={Age}, Adult={Adult}"

PRINT {Message}
PRINT 2 + 3 = {2 + 3}
PRINT Literal braces: {{Name}}
PRINT A; B; C
```

Output:

```text
Hello Sin! Age=49, Adult=TRUE
2 + 3 = 5
Literal braces: {Name}
A; B; C
```

## LET And PRINT Syntax

Implemented grammar:

```text
program          -> line* end-of-file
line             -> whitespace* statement? whitespace* newline
statement        -> let-statement | print-statement
let-statement    -> LET hspace+ identifier hspace* '=' hspace* expression
print-statement  -> PRINT
                  | PRINT hspace+ interpolated-string
                  | PRINT hspace+ quoted-expression
                  | PRINT hspace+ raw-template
expression       -> typed expression with precedence
```

Implemented rules:

- `PRINT` and `LET` are case-insensitive.
- Variable lookup is ordinal case-insensitive.
- SMILE v1.0 identifiers use portable ASCII letters, digits, and `_`; identifiers must start with an ASCII letter or `_`.
- `LET`, `PRINT`, `TRUE`, `FALSE`, `NOT`, `AND`, and `OR` are reserved SMILE keywords and cannot be variable names in any casing.
- `LET` declares immutable compile-time constants of type `String`, `Integer`, or `Boolean`.
- A `LET` variable becomes visible only after its initializer binds and evaluates successfully, so forward references and self-references fail as undefined variables.
- Strings use official escapes: `\\`, `\"`, `\n`, `\r`, `\t`, `\0`, `\b`, and `\f`.
- Integers are signed 64-bit values.
- Booleans are `TRUE` and `FALSE`; display text is always `TRUE` or `FALSE`.
- Arithmetic supports `+`, `-`, `*`, and `/` on integers.
- String concatenation supports `+` on strings only.
- Comparison supports `=`, `<>`, `<`, `<=`, `>`, and `>=` where the type rules allow it.
- Boolean logic supports `NOT`, `AND`, and `OR`.
- `AND` and `OR` evaluate left to right and short-circuit at runtime: `FALSE AND ...` and `TRUE OR ...` do not evaluate their right operands. Both operands are still parsed, bound, and type-checked.
- After successful binding, the shared simplifier uses known Boolean constants in `LET`, direct `PRINT`, raw-template holes, interpolated String holes, and nested expressions. It decides reachability before simplifying the right operand, so unreachable division or overflow cannot leak into a strict target compiler.
- Parentheses control expression grouping.
- SMILE does not perform implicit conversions in v0.4.2.1. For example, `"Age " + 49` is a type error.
- `PRINT` alone, or followed only by spaces/tabs, prints one blank line.
- `PRINT "Hello"` prints an ordinary quoted string.
- Ordinary quoted strings do not interpolate, so `PRINT "Hello {Name}!"` prints `{Name}` literally.
- `PRINT Hello World!` is a raw template and prints `Hello World!`.
- Raw templates and `$"..."` support `{expression}` interpolation.
- Raw placeholders are interpolation-oriented friendly syntax, so targets with native interpolation should show interpolation.
- `{{` and `}}` produce literal braces in raw templates and `$"..."`.
- `PRINT Name` prints the literal text `Name`.
- `PRINT {Name}` evaluates the variable `Name`; `PRINT {Age + 1}` evaluates the expression.
- `PRINT "Hello " + Name + "!"` supports string concatenation for `PRINT`.
- Explicit concatenation remains concatenation in generated code when the target language supports it.
- A semicolon is not a statement separator. In raw templates, `PRINT A; B; C` prints the semicolons literally.
- A physical line may contain only one statement.
- A second standalone `PRINT` keyword on the same line is a compiler error.
- Quote omission is a `PRINT` convenience only; it does not make quote-free strings legal in `LET` or future expression positions.
- Target generators map valid SMILE identifiers when a destination language reserves the same spelling, when a spelling would shadow generator-owned runtime APIs, or when a target has reserved identifier patterns.
- Java and Swift map a single `_` SMILE identifier because those languages cannot use `_` as an ordinary readable local variable.
- C and Objective-C map implementation-reserved prefixes such as `__internal` and `_Upper`.

Not implemented in v0.4.2.1: comments, `INPUT`, conditions, loops, functions, arrays, classes, floating-point numbers, reassignment, and user-defined types.

## Generated Examples

C#:

```csharp
using System;
using System.Globalization;

internal static class Program
{
    private static void Main()
    {
        string Name = "Sin";
        int Age = 49;
        bool Adult = Age >= 18;
        string Message = $"Hello {Name}! Age={Age.ToString(CultureInfo.InvariantCulture)}, Adult={(Adult ? "TRUE" : "FALSE")}";
        Console.WriteLine(Message);
        Console.WriteLine($"2 + 3 = {(2 + 3).ToString(CultureInfo.InvariantCulture)}");
    }
}
```

C:

```c
#include <stdio.h>
#include <stdbool.h>

int main(void)
{
    const char *Name = "Sin";
    int Age = 49;
    bool Adult = Age >= 18;
    const char *Message = "Hello Sin! Age=49, Adult=TRUE";

    printf("%s\n", Message);
    printf("2 + 3 = %d\n", 2 + 3);

    return 0;
}
```

JavaScript:

```javascript
let Name = "Sin";
let Age = 49;
let Adult = Age >= 18;
let Message = `Hello ${Name}! Age=${(Age).toString()}, Adult=${(Adult ? "TRUE" : "FALSE")}`;
console.log(Message);
console.log(`2 + 3 = ${(2 + 3).toString()}`);
```

Java:

```java
public final class Program
{
    public static void main(String[] args)
    {
        String Name = "Sin";
        int Age = 49;
        boolean Adult = Age >= 18;
        String Message = "Hello " + Name + "! Age=" + Integer.toString(Age) + ", Adult=" + (Adult ? "TRUE" : "FALSE");
        System.out.println(Message);
        System.out.println("2 + 3 = " + Integer.toString(2 + 3));
    }
}
```

COBOL:

```cobol
>>SOURCE FORMAT IS FREE
IDENTIFICATION DIVISION.
PROGRAM-ID. Program.

DATA DIVISION.
WORKING-STORAGE SECTION.
*> SMILE LET values are stored before PROCEDURE DIVISION.
01 Name PIC X(3) VALUE "Sin".
01 Age PIC X(2) VALUE "49".
01 Adult PIC X(4) VALUE "TRUE".
01 SMILE-Message PIC X(29) VALUE "Hello Sin! Age=49, Adult=TRUE".

PROCEDURE DIVISION.
*> Each SMILE PRINT becomes one DISPLAY operation.
    DISPLAY "Hello Sin! Age=49, Adult=TRUE".
    DISPLAY "2 + 3 = 5".
    STOP RUN.
```

Objective-C:

```objc
#include <stdio.h>
#include <stdbool.h>

int main(void)
{
    const char *Name = "Sin";
    int Age = 49;
    bool Adult = Age >= 18;
    const char *Message = "Hello Sin! Age=49, Adult=TRUE";

    printf("%s\n", Message);
    printf("2 + 3 = %d\n", 2 + 3);

    return 0;
}
```

Swift:

```swift
let Name: String = "Sin"
let Age: Int = 49
let Adult: Bool = Age >= 18
let Message: String = "Hello \(Name)! Age=\(String(Age)), Adult=\((Adult ? "TRUE" : "FALSE"))"
print(Message)
print("2 + 3 = \(String(2 + 3))")
```

Python:

```python
def _smile_text(value: object) -> str:
    if isinstance(value, bool):
        return "TRUE" if value else "FALSE"

    return str(value)


def main() -> None:
    Name = "Sin"
    Age = 49
    Adult = Age >= 18
    Message = f"Hello {Name}! Age={_smile_text(Age)}, Adult={_smile_text(Adult)}"
    print(Message)
    print(f"2 + 3 = {_smile_text(2 + 3)}")


if __name__ == "__main__":
    main()
```

Generated target code is expected to be semantically correct, idiomatic for the destination language, and close to code a competent human developer would naturally write. SMILE `Integer` always means signed 64-bit semantically, but each complete bound program uses the smallest natural target representation that preserves every Integer literal, value, operand, and intermediate: C and Objective-C use `int` or `int64_t`; C# and Java use `int` or `long`; JavaScript uses `Number` or consistent `BigInt`; Swift uses `Int` or `Int64`; and Python uses normal `int`. Ordinary profiles do not carry unnecessary `L`, `LL`, or `n` suffixes. A shared pure-expression pass simplifies Boolean identities and known-value short circuits before every target generator runs, without traversing an unreachable right operand. Python uses f-strings for interpolation, a generated `_smile_text` helper only when canonical Integer or Boolean display is needed, and `_smile_div` only when signed Integer division must truncate toward zero. C and Objective-C preserve native integer and boolean declaration expressions and keep ordinary NUL-free output on readable `%s` and ordinal `strcmp`. When a complete String value contains embedded NUL, only that `PRINT` uses compiler-owned UTF-8 byte data with `fwrite` and an exact length; only that NUL-sensitive equality is lowered to its exact evaluated Boolean. COBOL and MASM lower current compile-time values where that keeps those targets small and reliable; COBOL uses free-format `DISPLAY`, while MASM uses UTF-8 byte labels, pointer-plus-length variables, and `WriteFile`. The generated assembly and COBOL include short comments to support learning. See [SMILE Target Code Generation Standard v1.0](docs/SMILE%20Target%20Code%20Generation%20Standard%20v1.0.md).

## Supported Targets

| Stable ID | Display name | Generated files | Build & Run toolchain |
|---|---|---|---|
| `csharp` | C# | `Program.cs`, `GeneratedProgram.csproj` | .NET SDK 10 or newer |
| `c` | C | `Program.c` | Visual Studio C++ x64 tools |
| `masm-x64` | Assembly - Windows x64 MASM | `Program.asm` | Visual Studio C++ x64 tools with `ml64` and `link.exe` |
| `javascript` | JavaScript | `Program.js` | Node.js |
| `java` | Java | `Program.java` | JDK with `javac` and `java` |
| `cobol` | COBOL | `Program.cob` | GnuCOBOL through MSYS2 MinGW64 |
| `objective-c` | Objective-C | `Program.m` | MSYS2 MinGW64 Clang |
| `swift` | Swift | `Program.swift` | Swift.Toolchain for Windows plus Visual Studio C++ linker tools |
| `python` | Python | `Program.py` | Python 3.10 or newer |

Transpilation works even when optional target toolchains are missing. Build & Run is enabled only when the matching local tools are detected. COBOL local Build & Run uses GnuCOBOL free-format source. Objective-C local Build & Run currently uses SMILE's Foundation-free console profile so generated `.m` files compile reliably with MSYS2 Clang on Windows.

## Requirements

- Windows
- .NET SDK 10 or newer
- Visual Studio 2026 Enterprise or Build Tools with Desktop development with C++ for C and MASM
- Optional: Node.js for JavaScript
- Optional: JDK 25 LTS or newer for Java
- Optional: MSYS2 with `mingw-w64-x86_64-gnucobol` for COBOL
- Optional: MSYS2 with `mingw-w64-x86_64-clang` for Objective-C
- Optional: Swift.Toolchain for Windows for Swift
- Optional: Python 3.10 or newer for Python

Visual Studio setup must include the x64 C++ tools and `VC\Auxiliary\Build\vcvars64.bat`. SMILE discovers Visual Studio with `vswhere.exe`; it does not hardcode an edition or install folder. Swift Build & Run also uses those Visual Studio linker tools plus Swift's Windows SDK.

Microsoft OpenJDK 25 LTS is a free Java toolchain and can be installed with `winget install --id Microsoft.OpenJDK.25 --exact`. Restart the terminal or SMILE after installing so the updated user `PATH` is visible.

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
cmd /c cd /d C:\SMILE && dotnet run --project src\SMILE.Cli -- examples\CompleteLetV1.smile --target all
cmd /c cd /d C:\SMILE && dotnet run --project src\SMILE.Cli -- examples\TypedExpressionCore.smile --target all
cmd /c cd /d C:\SMILE && dotnet run --project src\SMILE.Cli -- examples\LetEmptyStringHardening.smile --target all
cmd /c cd /d C:\SMILE && dotnet run --project src\SMILE.Cli -- examples\LetIdentifierHardening.smile --target all
cmd /c cd /d C:\SMILE && dotnet run --project src\SMILE.Cli -- examples\FriendlyPrint.smile --target cobol --run
cmd /c cd /d C:\SMILE && dotnet run --project src\SMILE.Cli -- examples\FriendlyPrint.smile --target objective-c --run
cmd /c cd /d C:\SMILE && dotnet run --project src\SMILE.Cli -- examples\FriendlyPrint.smile --target swift --run
cmd /c cd /d C:\SMILE && dotnet run --project src\SMILE.Cli -- examples\TypedExpressionCore.smile --target python --run
```

Valid targets are `csharp`, `c`, `masm-x64`, `javascript`, `java`, `cobol`, `objective-c`, `swift`, `python`, and `all`.

## Desktop Application

The desktop app title is `SMILE - Simple Modern Interactive Learning Environment`. It opens maximized with a typed LET/PRINT learning sample that covers string, integer, boolean, interpolation, concatenation, raw templates, and expression placeholders. The top-left pane is editable SMILE source. The other three panes are read-only generated targets. They default to C#, Assembly - Windows x64 MASM, and C. Each generated pane can switch between C#, C, MASM x64, JavaScript, Java, COBOL, Objective-C, Swift, and Python.

![SMILE desktop app in maximized state](Requirements/Progress/2026-08-02-day-1-2-smile-desktop.png)

The four code panes use AvalonEdit. The SMILE source pane and all three generated target panes show line numbers and lexical syntax highlighting. Target panes switch highlighting when their selected language changes. Objective-C uses AvalonEdit's mature C/C++ highlighting because SMILE's current Objective-C output is a Foundation-free C-compatible console profile. Language switching reuses generated code already cached for the current source revision and only schedules live transpilation for visible targets that are actually missing. The output area remains a plain build/program log without line numbers.

Hold Ctrl and rotate the mouse wheel over any code pane or the diagnostics/output pane to increase or decrease only that pane's font size in one-point steps from 8 through 48 points. Normal mouse-wheel scrolling is unchanged. Each pane keeps its own in-memory zoom level so presenters can enlarge the generated code or program output without changing the other panes.

Typing in the SMILE source editor schedules a short debounced live transpilation for the visible target languages only. The latest source revision always wins, so stale generated code is never used for Build & Run. The Transpile All command is asynchronous and regenerates all nine targets.

Each generated pane supports Copy, Save Source, and Build & Run. JavaScript and Python run directly with their interpreters, so their buttons say Run. COBOL, Objective-C, Swift, and Python are enabled when their local toolchains are detected; otherwise the IDE reports the normal missing-toolchain message without closing SMILE.

Diagnostics, build output, program output, exit code, total duration, timeout, cancellation, generated workspace paths, pause-launcher paths, and missing-tool messages appear in the output area. The output area scrolls to the newest text as build/run messages are appended. Successful automatic live transpiles do not erase build logs. Very large process streams and desktop log history are bounded so runaway output cannot consume unbounded memory.

When Open Generated Folder is enabled, SMILE asks Windows Explorer to open the generated-code workspace after a pane build/run. For Build & Run Visible Languages, it opens the shared SMILE run folder so the generated-code workspaces for all visible targets can be inspected. Generated workspace folder names end with the target language so learners can quickly identify them. If Explorer cannot open the folder, the build result remains available and the output area shows a warning.

When Press Any Key Launcher is enabled, SMILE writes `Run Program - Press Any Key.cmd` into each successful build/run workspace. Double-clicking that launcher runs the generated program and then shows `Press any key to exit...`, which keeps the console window open long enough to inspect the output.

Current desktop build version: `0.4.2.1 Exact String and Target-Safe Expression Hardening`.

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
| `SMILE1109` | Semicolons cannot separate SMILE statements |
| `SMILE1110` | Unterminated interpolated string |
| `SMILE1111` | Unexpected text after a string expression |
| `SMILE1112` | `LET` requires a valid variable name |
| `SMILE1113` | `LET` requires `=` before its initializer |
| `SMILE1115` | Reserved SMILE keyword used as a variable name |
| `SMILE1116` | `LET` requires an initializer expression |
| `SMILE1201` | Invalid or unexpected token in expression |
| `SMILE1202` | Integer literal is outside the signed 64-bit range |
| `SMILE1203` | Unary operator is not defined for the operand type |
| `SMILE1204` | Binary operator is not defined for the operand types |
| `SMILE1205` | Missing closing parenthesis |
| `SMILE1206` | Integer arithmetic overflow |
| `SMILE1207` | Division by zero |
| `SMILE1208` | Unknown or invalid string escape sequence |
| `SMILE1209` | Unterminated string escape sequence |

Desktop crash containment is intentionally defensive. Recoverable Build & Run, toolchain detection, process execution, command refresh, and folder-opening failures are reported in the output area without closing the IDE. Detailed desktop diagnostics are written to `%LOCALAPPDATA%\SMILE\Logs\SMILE-yyyy-MM-dd.log`, with `%TEMP%\SMILE\Logs` used as a fallback if the normal log folder is unavailable.

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
Source -> Lexer -> Tokens -> Parser -> Syntax Tree -> Binder -> Bound Program
                                                               -> Constant-Aware Pure Simplifier
                                                               -> Target Integer Profile
                                                               -> Target Generator -> Generated Files
                                                                                      |
                                                                               Optional Toolchain
                                                                                      |
                                                                               Build and Run Result
```

`SMILE.Engine` owns lexing, parsing, diagnostics, syntax nodes, binding, variable symbols, typed bound expressions, compile-time constant evaluation, constant-aware bound-expression simplification, per-program target Integer profiling, the SMILE reference evaluator, target identifier mapping, and target generators. The simplifier walks statements in order, records each bound constant, and decides short-circuit reachability before visiting the right operand. SMILE Strings remain complete length-aware values even when C-family targets normally use NUL-terminated pointers; those targets switch only NUL-sensitive output to exact UTF-8 bytes and `fwrite`. The target identifier map is symbol-based and uses exact reserved-word checks plus target-specific pattern rules, such as C implementation-reserved prefixes, COBOL reserved words and data-name spelling, Java/Swift `_`, and Python keywords, soft keywords, built-ins, and generated helper names. `SMILE.Toolchains` owns detection, temporary workspaces, async process execution, cancellation, timeouts, bounded process output, build, and run, including safe discovery of Python 3.10+ without invoking Windows Store aliases. `SMILE.Cli` and `SMILE.Desktop` reuse both projects. `SMILE.Desktop` uses AvalonEdit for the four code panes and keeps build/run work isolated from the WPF UI thread.

SMILE-owned build/output artifacts older than 1 day may be cleaned from known generated locations such as `bin`, `obj`, `out`, and `%TEMP%\SMILE\Runs`.

## Current Limitations

- Only `String`, `Integer`, and `Boolean` core types are implemented.
- SMILE Integer semantics are signed 64-bit. Generated storage is intentionally target-idiomatic and may be narrower when the complete bound program proves that safe; floating-point and decimal types are not implemented.
- Syntax highlighting is lexical only; semantic highlighting, autocomplete, and diagnostic squiggles are not implemented.
- C and MASM target output is focused on Windows local toolchains.
- COBOL local output uses GnuCOBOL free-format source and fixed-length storage for current SMILE display values.
- Objective-C local output currently uses a Foundation-free console profile on Windows; Foundation/NSString output remains future hardening.
- Swift local output requires Swift.Toolchain for Windows and Visual Studio C++ linker tools.
- Python local output requires Python 3.10 or newer; generated Python has no third-party package dependencies.
- Unicode output beyond UTF-8 source text remains an area for later hardening.
- Full UI automation coverage is not included; manual WPF smoke testing is still required for release validation.

## Deferred Destination Languages

Rust, Zig, and Go are intentionally not part of the active SMILE roadmap at this stage. After Python, target-language expansion is paused while SMILE focuses on runtime variables, assignment, input, conditions, loops, functions, and scopes. These targets may be reconsidered later when the runtime language model is mature.

## Roadmap

Future ideas, not implemented in v0.4.2.1:

The next major milestone is **v0.5.0 - Runtime Variables and `SET`**. This correction release does not implement reassignment.

1. Runtime variables and assignment
2. `INPUT`
3. `IF / THEN / ELSE`
4. Loops
5. Functions and scopes
6. Floating-point and decimal numeric types
7. Debugging and source mapping
8. Semantic highlighting, autocomplete, and diagnostic squiggles
9. Reusable web interface
10. Evolution toward a full SMILE language

## License

SMILE is licensed under GNU Affero General Public License v3.0 only (`AGPL-3.0-only`). See [LICENSE](LICENSE).

SMILE.Desktop uses AvalonEdit 6.3.1.120 for WPF editor panes. AvalonEdit is licensed under MIT; its notice is recorded in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
