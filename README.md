# SMILE

SMILE stands for Simple Modern Interactive Learning Environment. It is an educational, BASIC-inspired, multi-target transpiler designed to bring a smile to new developers by showing that programming languages share the same fundamental ideas even when their syntax, compiler, runtime, and platform conventions differ.

Write a simple SMILE program once, then view equivalent programs in C#, C, COBOL, Windows x64 MASM Assembly, JavaScript, Java, Objective-C, Swift, Python, and C++.

## Mission

A programming language inspired by BASIC that makes it easy for newcomers to learn and understand how programming languages work across the board. Updated for the modern era, SMILE takes the classic BASIC programming language and takes it to the next level by offering to teach not just concepts and ideas of what a programming language can do but show them how various programming languages look like by transpiling (translating) and compiling their SMILE code to many other programming languages. So students can learn many programming languages simultaneously and arrive at one obvious conclusion: all programming languages share the same fundamentals. What's important is learning to think logically and understand how to solve problems with code, not learning the syntax of a particular programming language. SMILE is designed to be a fun and educational programming language that teaches students how to think like a programmer and understand the fundamentals of programming languages.

## Video Introduction

[![Watch the SMILE introduction on YouTube](https://img.youtube.com/vi/fgyIMCdHcug/hqdefault.jpg)](https://www.youtube.com/watch?v=fgyIMCdHcug)

[Watch the SMILE introduction on YouTube](https://www.youtube.com/watch?v=fgyIMCdHcug).

## Current Release

SMILE v0.6.0.1 — IF Hardening — preserves the v0.6.0 IF language and generated behavior while strengthening the compiler around it. The release adds independent Windows GitHub Actions validation, separates the binder and ten destination generators into focused source files, keeps the public generation facade small, rejects IF nesting beyond 128 levels with `SMILE1416`, and directly regresses function-shaped condition text without introducing function-call syntax.

IF semantics are unchanged: conditions are call-free and every Boolean leaf must be an explicit comparison, so `IF Ready = TRUE THEN` is valid while `IF Ready THEN` is not. `ELSE IF` is two keywords on the same logical header line; an `IF` after a standalone `ELSE` line is a nested statement with its own `END IF`. IF v1.0 permits `PRINT`, `SET`, nested `IF`, blank lines, and SET Block String Literals in branches, while `LET` remains top-level until scopes are formally introduced. All ten generators preserve every source branch, and branch-aware Known/Unknown analysis propagates a value after IF only when every possible outgoing path agrees.

```text
SMILE source
  -> lex source into tokens
  -> normalize SET Block String content entirely in the front end
  -> parse logical lines into recursive statement lists and canonical syntax nodes
  -> bind declarations, assignments, IF clauses, and typed expressions once
  -> analyze statement lists with branch-aware Known/Unknown environments
  -> simplify pure bound expressions with mutation-aware, branch-safe values and reachability
  -> identify variables mutated by SET
  -> map SMILE symbols to safe target identifiers once per target
  -> choose one idiomatic target Integer and String plan from every branch
  -> generate ten target programs with genuine control flow and a real storage update for every SET
  -> read and compute from current generated storage for every branch-aware Unknown expression
  -> compare generated runtime behavior to the SMILE reference evaluator in tests
  -> show three debounced live target previews with line numbers and syntax highlighting
  -> build and run locally when the matching toolchain is installed
  -> keep the desktop IDE responsive when a target toolchain fails or target languages are switched rapidly
```

The official language specifications are published in [docs/SMILE Language Specification](docs/SMILE%20Language%20Specification):

- [SMILE - LET Statement Official Specification v1.0](docs/SMILE%20Language%20Specification/SMILE%20-%20LET%20Statement%20Official%20Specification%20v1.0.md)
- [SMILE - SET Statement Official Specification v1.0](docs/SMILE%20Language%20Specification/SMILE%20-%20SET%20Statement%20Official%20Specification%20v1.0.md)
- [SMILE - IF Statement Official Specification v1.0](docs/SMILE%20Language%20Specification/SMILE%20-%20IF%20Statement%20Official%20Specification%20v1.0.md)
- [SMILE - PRINT Statement Official Specification v1.0](docs/SMILE%20Language%20Specification/SMILE%20-%20PRINT%20Statement%20Official%20Specification%20v1.0.md)
- [SMILE - String Literals Official Specification v1.0](docs/SMILE%20Language%20Specification/SMILE%20-%20String%20Literals%20Official%20Specification%20v1.0.md)
- [SMILE - Core Types and Expressions Official Specification v1.0](docs/SMILE%20Language%20Specification/SMILE%20-%20Core%20Types%20and%20Expressions%20Official%20Specification%20v1.0.md)

## Guiding Principles

KISS means the simplest complete solution wins. SMILE avoids speculative features, unnecessary abstractions, heavy frameworks, parser generators, CLI frameworks, and third-party MVVM or process libraries.

KISS v2, "The Sin Way," puts user-experience performance first and functional performance second. Typing should feel immediate, build/run work should be cancellable, and compiler/toolchain work must not block the WPF UI thread.

Generated code follows the same rules: complete, minimal, idiomatic, readable, deterministic, educational, and dependency-light.

## Simple SMILE Program

```basic
LET Name = ""
LET Counter = 0

SET Name ="
S
 I
  N
"

PRINT Hello:
PRINT {Name}
PRINT Counter={Counter}

SET Counter = Counter + 1
SET Name = "Louiery"

IF Counter = 1 THEN
    PRINT Hello {Name}. Counter={Counter}
ELSE
    PRINT Counter was not updated.
END IF
```

Output:

```text
Hello:
S
 I
  N
Counter=0
Hello Louiery. Counter=1
```

## LET, SET, PRINT, And IF Syntax

Implemented grammar:

```text
program          -> statement-list end-of-file
statement-list   -> (blank-line | statement)*
statement        -> let-statement | set-statement | print-statement | if-statement
let-statement    -> LET hspace+ identifier hspace* '=' hspace* expression
set-statement    -> SET hspace+ identifier hspace* '=' hspace* set-value
set-value        -> expression | set-block-string-literal
print-statement  -> PRINT
                  | PRINT hspace+ interpolated-string
                  | PRINT hspace+ quoted-expression
                  | PRINT hspace+ raw-template
if-statement     -> IF hspace+ if-condition hspace+ THEN hspace* line-end
                    branch-statement-list
                    else-if-clause*
                    else-clause?
                    END hspace+ IF hspace* statement-end
else-if-clause   -> ELSE hspace+ IF hspace+ if-condition hspace+ THEN hspace* line-end
                    branch-statement-list
else-clause      -> ELSE hspace* line-end branch-statement-list
branch-statement-list -> (blank-line | branch-statement)*
branch-statement -> print-statement | set-statement | if-statement
if-condition     -> expression subject to the explicit-comparison and call-free IF rules
expression       -> typed expression with precedence
```

A SET Block String Literal begins when the opening `"` is the final non-whitespace character on the physical SET line. Its closing delimiter is a line whose only non-whitespace character is `"`. The block token consumes its internal physical newlines, so the complete block remains one logical SET statement.

Implemented rules:

- `PRINT`, `LET`, `SET`, `IF`, `THEN`, `ELSE`, and `END` are case-insensitive.
- Variable lookup is ordinal case-insensitive.
- SMILE v1.0 identifiers use portable ASCII letters, digits, and `_`; identifiers must start with an ASCII letter or `_`.
- `LET`, `SET`, `PRINT`, `IF`, `THEN`, `ELSE`, `END`, `TRUE`, `FALSE`, `NOT`, `AND`, and `OR` are reserved SMILE keywords and cannot be variable names in any casing. `ELSEIF` and `ENDIF` are not combined keywords.
- `LET` declares a variable, fixes its type as `String`, `Integer`, or `Boolean`, evaluates its initializer, and stores the initial current value.
- A `LET` variable becomes visible only after its initializer binds and evaluates successfully, so forward references and self-references fail as undefined variables.
- `SET` changes an existing variable and never declares one. The target must have an earlier successful LET declaration.
- A SET right side must have exactly the variable's fixed type. SMILE performs no implicit assignment conversion.
- The complete SET right side evaluates before the current value changes, so `SET Counter = Counter + 1` reads the old value and updates atomically.
- `PRINT`, later LET initializers, interpolation holes, and later SET expressions read the current value established by earlier statements.
- `IF condition THEN` begins a block conditional and one `END IF` closes its complete IF / ELSE IF / ELSE chain.
- `ELSE IF` is a clause only when ELSE and IF occur on the same logical header line. An IF after a standalone ELSE line is nested and needs its own END IF.
- Every complete IF condition has type Boolean, invokes no function or procedure, and contains an explicit comparison at every Boolean leaf. Standalone Boolean variables and literals are not conditions.
- Conditions retain normal left-to-right `AND`/`OR` short-circuit evaluation, but parsing, binding, structural validation, and type checking still inspect both operands and every source branch.
- IF v1.0 branches permit `PRINT`, `SET`, nested `IF`, blank lines, and SET Block String Literals. `LET` is rejected inside every IF-related body because v0.6.0.1 has no block scope.
- Clauses are tested in order; only the first successful clause executes, otherwise ELSE executes when present. Only the selected branch mutates evaluator state.
- Every generator preserves all source branches even when current values make one branch predictable. Known-value analysis merges outgoing paths and propagates a post-IF value only when every possible path proves the same value.
- IF nesting depth 1 through 128 is supported. Attempting to enter depth 129 reports `SMILE1416` at that IF keyword and uses bounded recovery instead of recursing into the over-limit body.
- A SET Block String Literal is valid only as the complete SET value. It is not legal in LET, PRINT, interpolation, concatenation, or parentheses.
- Block delimiter lines are excluded. Each boundary between content lines becomes one logical `\n`, with no automatic leading or trailing newline.
- The exact spaces/tabs before the closing quote form the structural indentation margin. That exact margin is removed only from content lines that begin with it; all additional or nonmatching whitespace is preserved.
- Blank content lines, trailing spaces/tabs, quotes inside content, and `\\`, `\"`, `\n`, `\r`, `\t`, `\0`, `\b`, and `\f` are preserved with official String semantics.
- CRLF, LF, and accepted standalone CR source separators normalize to logical `\n` inside block values.
- Strings use official escapes: `\\`, `\"`, `\n`, `\r`, `\t`, `\0`, `\b`, and `\f`.
- Integers are signed 64-bit values.
- Booleans are `TRUE` and `FALSE`; display text is always `TRUE` or `FALSE`.
- Arithmetic supports `+`, `-`, `*`, and `/` on integers.
- String concatenation supports `+` on strings only.
- Comparison supports `=`, `<>`, `<`, `<=`, `>`, and `>=` where the type rules allow it.
- Boolean logic supports `NOT`, `AND`, and `OR`.
- `AND` and `OR` evaluate left to right and short-circuit at runtime: `FALSE AND ...` and `TRUE OR ...` do not evaluate their right operands. Both operands are still parsed, bound, and type-checked.
- After successful binding, one shared recursive statement-list analysis records current values, mutations, and branch outcomes for `LET`, `SET`, `PRINT`, and `IF`. The simplifier decides short-circuit reachability before visiting the right operand, never propagates an old value past SET, and never deletes IF clauses or bodies.
- Parentheses control expression grouping.
- SMILE does not perform implicit conversions in v0.6.0.1. For example, `"Age " + 49` is a type error.
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
- A SET Block String Literal is one logical statement spanning its delimiter and content lines.
- A second standalone `PRINT` keyword on the same line is a compiler error.
- Quote omission is a `PRINT` convenience only; it does not make quote-free strings legal in `LET`, ordinary `SET`, or other expression positions.
- Source loading, editing, saving, examples, and tests preserve trailing spaces and tabs because block content may depend on them.
- Target generators map valid SMILE identifiers when a destination language reserves the same spelling, when a spelling would shadow generator-owned runtime APIs, or when a target has reserved identifier patterns.
- Java and Swift map a single `_` SMILE identifier because those languages cannot use `_` as an ordinary readable local variable.
- C and Objective-C map implementation-reserved prefixes such as `__internal` and `_Upper`.
- C and Objective-C also map emitted runtime facility and type names such as `bool`, `int64_t`, `size_t`, `fwrite`, `fputc`, `fputs`, `strcmp`, `memcmp`, `memcpy`, `strlen`, and `snprintf`, so learner variables cannot shadow generated storage operations.
- C, Objective-C, and C++ map fixed-width Integer and limit macro names such as `INT64_MAX`, `INT64_C`, `UINT64_MAX`, and `SIZE_MAX`, preventing wide-profile headers from rewriting learner variables.
- C++ additionally maps `__` anywhere in a name. The readable `_smile_` result spells reserved underscore runs out so the emitted C++ identifier is itself safe.

Not implemented in v0.6.0.1: comments, `INPUT`, loops, functions, procedures, scopes, arrays, classes, floating-point numbers, one-line IF, assignment expressions, compound assignment, and user-defined types.

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

C++:

```cpp
#include <iostream>
#include <string>

int main()
{
    std::string Name = "Sin";
    int Age = 49;
    bool Adult = Age >= 18;
    std::string Message = std::string{"Hello "} + Name + "! Age=" +
        std::to_string(Age) + ", Adult=" + (Adult ? "TRUE" : "FALSE");

    std::cout << Message << '\n';
    std::cout << "2 + 3 = " << 2 + 3 << '\n';

    return 0;
}
```

For this SMILE assignment:

```basic
LET Counter = 0
SET Counter = Counter + 1
```

each destination emits a real update at the SET position:

| Target | Natural generated shape |
|---|---|
| C#, C, C++, Java, Objective-C | `Counter = Counter + 1;` |
| JavaScript | `Counter = Counter + 1;` after `let Counter = 0;` |
| Python | `Counter = Counter + 1` |
| Swift | `var Counter: Int = 0`, then `Counter = Counter + 1` |
| COBOL | `MOVE` into sized `PIC X` storage plus a logical-length update |
| MASM x64 | Update pointer/length text storage plus signed Integer storage; materialize runtime expressions when needed |

C# warns about a plain direct self-assignment such as `Name = Name`, while Swift rejects it. For that valid SMILE SET form, both generators emit the smallest type-preserving identity expression (`Name = Name + ""`, `Count = Count + 0`, or `Ready = Ready || false`) so the destination still contains a real assignment and compiles cleanly. This target-local rule is based on bound symbol identity, so case-insensitive and mapped-name self-assignment use the same generated target name; assigning a different variable remains a natural assignment.

For a SMILE IF / ELSE IF / ELSE chain, C#, C, C++, Java, JavaScript, Objective-C, and Swift emit natural `if / else if / else` blocks; Python emits `if / elif / else`; COBOL emits matching `IF / ELSE / END-IF`; and MASM x64 emits deterministic compare-and-jump labels. Empty Python branches use `pass`. Every destination retains every source branch. A value is embedded statically only when branch-aware analysis proves it on every incoming path; after an Unknown merge, later LET, SET, PRINT, interpolation, and IF expressions read and compute from generated runtime storage instead of copying the branch selected by the reference trace.

A SET Block String generates only as its normalized ordinary String value. No backend scans delimiters, removes indentation, or normalizes source newlines.

Generated target code is expected to be semantically correct, idiomatic for the destination language, and close to code a competent human developer would naturally write. SMILE `Integer` always means signed 64-bit semantically, but each complete bound program uses the smallest natural target representation that preserves every LET value, SET value, IF condition, branch value, operand, and intermediate. A shared branch-aware pass simplifies Boolean identities and known-value short circuits without traversing an unreachable right operand, carrying stale state past SET, leaking one branch into another, or removing genuine control flow. C++ uses RAII-owned `std::string`, exact assignment, length-aware embedded-NUL construction, native equality, and `std::cout`. Python preserves natural assignment and emits `_smile_text` or `_smile_div` only when needed; interpolation folding is limited to branch-aware Known holes. C and Objective-C keep mutable `const char *` pointers, add collision-safe logical lengths when NUL is possible, stream composite output by current segment length, and use bounded runtime buffers for Unknown composite assignments or comparisons. COBOL sizes `PIC X` across every branch and lowers Unknown values through current `WORKING-STORAGE`, logical lengths, numeric display storage, and runtime `STRING` plans. MASM uses deterministic UTF-8 labels, compare/jump control flow, pointer-plus-length text storage, signed Integer storage, bounded runtime buffers, and one small signed Integer formatter. See [SMILE Target Code Generation Standard v1.0](docs/SMILE%20Target%20Code%20Generation%20Standard%20v1.0.md).

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
| `cpp` | C++ | `Program.cpp` | Visual Studio x64 C++ tools |

Transpilation works even when optional target toolchains are missing. Build & Run is enabled only when the matching local tools are detected. Java Build & Run requires a complete JDK containing both `javac` and `java`; detection distinguishes a full JDK from a Java-runtime-only installation and a missing JDK. COBOL local Build & Run uses GnuCOBOL free-format source. Objective-C local Build & Run currently uses SMILE's Foundation-free console profile so generated `.m` files compile reliably with MSYS2 Clang on Windows.

## Requirements

- Windows
- .NET SDK 10 or newer
- Visual Studio 2026 Enterprise or Build Tools with Desktop development with C++ for C, C++, and MASM
- Optional: Node.js for JavaScript
- Optional: JDK 25 LTS or newer for Java
- Optional: MSYS2 with `mingw-w64-x86_64-gnucobol` for COBOL
- Optional: MSYS2 with `mingw-w64-x86_64-clang` for Objective-C
- Optional: Swift.Toolchain for Windows for Swift
- Optional: Python 3.10 or newer for Python

Visual Studio setup must include the x64 C++ tools and `VC\Auxiliary\Build\vcvars64.bat`. SMILE discovers Visual Studio with `vswhere.exe`; it does not hardcode an edition or install folder. Swift Build & Run also uses those Visual Studio linker tools plus Swift's Windows SDK.

Microsoft OpenJDK 25 LTS is a free Java toolchain and can be installed with `winget install --id Microsoft.OpenJDK.25 --exact`. Restart the terminal or SMILE after installing so the updated user `PATH` is visible.

The v0.6.0.1 Java acceptance suite invokes both `javac` and `java` and compares IF clause selection, nested branches, SET mutation across branches, Block Strings, embedded NUL, wide Integer planning, runtime-authenticity, and complete `examples/language.smile` output to `SmileEvaluator`.

## Build, Test, And Run

```bat
cmd /c "git clone https://github.com/Sincioco/SMILE.git C:\SMILE"
cmd /c "cd /d C:\SMILE && dotnet restore SMILE.sln"
cmd /c "cd /d C:\SMILE && dotnet build SMILE.sln -c Debug --no-restore -nologo"
cmd /c "cd /d C:\SMILE && dotnet test SMILE.sln -c Debug --no-build --no-restore -nologo"
cmd /c "cd /d C:\SMILE && dotnet build SMILE.sln -c Release --no-restore -nologo"
cmd /c "cd /d C:\SMILE && dotnet test SMILE.sln -c Release --no-build --no-restore -nologo"
```

The `SMILE CI` workflow at `.github/workflows/smile-ci.yml` independently restores, builds, and tests the solution in Debug and Release on `windows-latest` with .NET SDK 10.0.302. It runs for pushes to `main`, pull requests targeting `main`, and manual dispatch. After every direct push to `main`, this hosted check is mandatory: the task is not complete until the run for the exact current `main` commit finishes successfully, and an older green run cannot validate a newer commit. Hosted CI validates the SMILE solution and the unit/integration tests available on that runner; it deliberately does not install every destination-language toolchain.

Official release validation therefore remains a separate local requirement covering Java, all ten destination toolchains, zero generated compiler warnings, and evaluator-versus-target conformance. Before a release commit, restore once, make Java and all ten local toolchains mandatory instead of contributor-optional, and enable generated compiler-warning validation for both configurations:

```powershell
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

A warning-free `dotnet build SMILE.sln` proves the SMILE solution itself is clean; it does not prove generated target programs are warning-free. `SMILE_REQUIRE_ZERO_TARGET_WARNINGS=1` activates destination-specific warning detection for the compiler-backed targets. JavaScript and Python have no compile stage in their normal SMILE toolchains. Runtime conformance remains a separate all-ten-target evaluator comparison, and the official IF program must retain every branch, exit with code zero, emit zero detected compiler warnings, and match `SmileEvaluator` exactly.

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
cmd /c cd /d D:\SMILE && dotnet run --project src\SMILE.Cli -- examples\RuntimeVariablesSet.smile --target all
cmd /c cd /d D:\SMILE && dotnet run --project src\SMILE.Cli -- examples\RuntimeVariablesSet.smile --target csharp --run
cmd /c cd /d D:\SMILE && dotnet run --project src\SMILE.Cli -- examples\language.smile --target all
cmd /c cd /d C:\SMILE && dotnet run --project src\SMILE.Cli -- examples\FriendlyPrint.smile --target cobol --run
cmd /c cd /d C:\SMILE && dotnet run --project src\SMILE.Cli -- examples\FriendlyPrint.smile --target objective-c --run
cmd /c cd /d C:\SMILE && dotnet run --project src\SMILE.Cli -- examples\FriendlyPrint.smile --target swift --run
cmd /c cd /d C:\SMILE && dotnet run --project src\SMILE.Cli -- examples\TypedExpressionCore.smile --target python --run
cmd /c cd /d C:\SMILE && dotnet run --project src\SMILE.Cli -- examples\TypedExpressionCore.smile --target cpp --run
```

Valid targets are `csharp`, `c`, `masm-x64`, `javascript`, `java`, `cobol`, `objective-c`, `swift`, `python`, `cpp`, and `all`.

## Desktop Application

The desktop app title is `SMILE - Simple Modern Interactive Learning Environment`. It opens maximized and completes its first paint before doing language-reference I/O or compiler work. It then asynchronously loads the packaged [cumulative language reference](examples/language.smile) into the top-left editor and immediately transpiles only the three visible targets in the background. If a learner types or opens a file while that read is pending, the newer document wins and is never replaced by the late startup result. The reference preserves the full valid LET, PRINT, and SET tour and appends canonical IF, ELSE IF, ELSE, nested IF, branch mutation, and branch Block String scenarios. It is the committed file that future language syntax will extend instead of replacing earlier examples. The other three panes are read-only generated targets. They default to C#, Assembly - Windows x64 MASM, and C. Each generated pane can switch between C#, C, MASM x64, JavaScript, Java, COBOL, Objective-C, Swift, Python, and C++.

`examples/language.smile` is copied beside the Desktop executable in normal builds and deployment publishes as `language.smile`. New editor sessions reload that runtime file asynchronously without associating Save with the packaged copy, so learners can experiment safely and use Save As for their own programs.

![SMILE desktop app in maximized state](Requirements/Progress/2026-08-02-day-1-2-smile-desktop.png)

The four code panes use AvalonEdit. The SMILE source pane and all three generated target panes show line numbers and lexical syntax highlighting. `IF`, `THEN`, `ELSE`, and `END` are highlighted individually, so ELSE IF and END IF visibly remain two keywords. A SET Block String remains one String-colored span across physical lines inside or outside a branch until its whitespace-only closing delimiter; quotes in ordinary content do not end the span. Unterminated blocks and malformed IF text remain safe while typing. Target panes switch highlighting when their selected language changes. C++ uses AvalonEdit's built-in C++ definition. Objective-C uses the same mature C/C++ highlighting because SMILE's current Objective-C output is a Foundation-free C-compatible console profile. Language switching reuses generated code already cached for the current source revision and only schedules live transpilation for visible targets that are actually missing. The output area remains a plain build/program log without line numbers.

Hold Ctrl and rotate the mouse wheel over any code pane or the diagnostics/output pane to increase or decrease only that pane's font size in one-point steps from 8 through 48 points. Normal mouse-wheel scrolling is unchanged. Each pane keeps its own in-memory zoom level so presenters can enlarge the generated code or program output without changing the other panes.

Typing in the SMILE source editor schedules a short debounced live transpilation for the visible target languages only. Initial language-reference generation also targets only the visible languages, starts after the first window paint, and runs off the WPF dispatcher. The latest source revision always wins, so stale generated code is never used for Build & Run. The Transpile All command is asynchronous and regenerates all ten targets.

Each generated pane supports Copy, Save Source, Open Generated Folder, Build & Run, and the optional Press Any Key launcher. JavaScript and Python run directly with their interpreters, so their buttons say Run. C++, COBOL, Objective-C, Swift, and Python are enabled when their local toolchains are detected; otherwise the IDE reports the normal missing-toolchain message without closing SMILE.

Diagnostics, build output, program output, exit code, total duration, timeout, cancellation, generated workspace paths, pause-launcher paths, and missing-tool messages appear in the output area. The output area scrolls to the newest text as build/run messages are appended. Successful automatic live transpiles do not erase build logs. Very large process streams and desktop log history are bounded so runaway output cannot consume unbounded memory.

When Open Generated Folder is enabled, SMILE asks Windows Explorer to open the generated-code workspace after a pane build/run. For Build & Run Visible Languages, it opens the shared SMILE run folder so the generated-code workspaces for all visible targets can be inspected. Generated workspace folder names end with the target language so learners can quickly identify them. If Explorer cannot open the folder, the build result remains available and the output area shows a warning.

When Press Any Key Launcher is enabled, SMILE writes `Run Program - Press Any Key.cmd` into each successful build/run workspace. Double-clicking that launcher runs the generated program and then shows `Press any key to exit...`, which keeps the console window open long enough to inspect the output.

Current desktop build version: `0.6.0.1 IF Hardening`.

## Diagnostics

SMILE reports source errors as diagnostics instead of ordinary crashes. Current stable codes include:

| Code | Meaning |
|---|---|
| `SMILE1001` | Unknown statement or keyword |
| `SMILE1003` | Unterminated ordinary String literal or SET Block String Literal |
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
| `SMILE1301` | `SET` requires a variable name |
| `SMILE1302` | `SET` requires `=` after its target |
| `SMILE1303` | `SET` requires a value |
| `SMILE1304` | SET target variable is undefined |
| `SMILE1305` | SET value type does not match the variable's fixed type |
| `SMILE1306` | SET Block String Literal is valid only as the complete value of SET |
| `SMILE1307` | Unexpected content follows the closing block delimiter |
| `SMILE1308` | Block opening quote must end the physical SET line |
| `SMILE1401` | IF requires a condition |
| `SMILE1402` | Every atomic IF condition must be an explicit comparison |
| `SMILE1403` | The complete IF condition must have type Boolean |
| `SMILE1404` | An IF condition cannot invoke a function or procedure |
| `SMILE1405` | IF or ELSE IF requires THEN |
| `SMILE1406` | Unexpected content follows THEN |
| `SMILE1407` | ELSE must stand alone or be followed by IF on the same logical line |
| `SMILE1408` | ELSE IF requires a condition |
| `SMILE1409` | An IF may contain only one final ELSE |
| `SMILE1410` | ELSE IF cannot appear after ELSE |
| `SMILE1411` | ELSE, ELSE IF, or END IF has no matching IF |
| `SMILE1412` | IF is missing END IF |
| `SMILE1413` | END IF is malformed or has trailing content |
| `SMILE1414` | LET is not permitted inside IF v1.0 |
| `SMILE1415` | Statement is not permitted inside IF v1.0 |
| `SMILE1416` | Maximum IF nesting depth of 128 exceeded |

Desktop crash containment is intentionally defensive. Recoverable Build & Run, toolchain detection, process execution, command refresh, and folder-opening failures are reported in the output area without closing the IDE. Detailed desktop diagnostics are written to `%LOCALAPPDATA%\SMILE\Logs\SMILE-yyyy-MM-dd.log`, with `%TEMP%\SMILE\Logs` used as a fallback if the normal log folder is unavailable.

## Repository Structure

```text
SMILE.sln
README.md
LICENSE
AGENTS.md
.editorconfig
.gitignore
.github/
  workflows/
    smile-ci.yml
examples/
Requirements/
docs/
src/
  SMILE.Engine/
    Binder.cs
    Generation.cs
    Generation/
  SMILE.Toolchains/
  SMILE.Cli/
  SMILE.Desktop/
tests/
  SMILE.Tests/
```

`Requirements/` stores daily project instructions and can also hold future designs, sketches, diagrams, and ideas.

## Architecture

```text
Source -> Lexer -> Tokens -> Recursive Block Parser -> Syntax Tree -> Binder -> Bound Program
                                                                         -> Branch-Aware Statement Analysis
                                                                         -> Mutation-Aware Pure Simplifier
                                                                         -> Variable Mutation Analysis
                                                                         -> Target Integer/String Planning
                                                               -> Target Generator -> Generated Files
                                                                                      |
                                                                               Optional Toolchain
                                                                                      |
                                                                               Build and Run Result
```

`SMILE.Engine` owns lexing, block String normalization, recursive statement-list parsing, diagnostics, syntax nodes, binding, variable symbols, typed bound statements and expressions, the mutable reference evaluator, branch-aware Known/Unknown analysis, mutation-aware simplification, variable mutation analysis, bounded per-expression display facts, per-program Integer/String planning, target identifier mapping, and all ten generators. Parsing remains in `Parser.cs`; the behavior-preserving `Binder` phase lives in `Binder.cs`. `Generation.cs` is the small public transpilation facade, while shared helpers and one focused file per destination generator live under `src/SMILE.Engine/Generation/` without changing the existing APIs or emitted files. `BoundLetStatement` owns an initializer, not permanent current state; the evaluator environment owns current values. Every IF branch is analyzed from the same incoming environment and outgoing paths are merged before later statements. Simplification, NUL handling, String equality, Integer promotion, COBOL sizing, and MASM runtime-storage planning inspect the entire IF tree without deleting unselected branches. Every SET emits a real storage update, and only the selected evaluator branch mutates runtime state. The reference evaluator state and generated target runtime storage must agree at every observable expression, not only in final output; the selected concrete trace cannot replace a runtime value where branch-aware analysis reports Unknown. Block delimiter recognition, indentation removal, newline normalization, and escape decoding remain entirely in the front end; targets receive one ordinary bound String value. `SMILE.Toolchains` owns detection, temporary workspaces, async process execution, cancellation, timeouts, bounded process output, build, and run. `SMILE.Cli` and `SMILE.Desktop` reuse both projects, and the desktop keeps live transpilation and build/run work off the WPF UI thread.

SMILE-owned build/output artifacts older than 1 day may be cleaned from known generated locations such as `bin`, `obj`, `out`, and `%TEMP%\SMILE\Runs`.

## Current Limitations

- Only `String`, `Integer`, and `Boolean` core types are implemented.
- `SET` is the only assignment statement. Assignment expressions, compound assignment, increment/decrement syntax, and multiple assignment are not implemented.
- Multiline String source syntax is limited to one complete SET Block String value; general block Strings and block interpolation are not implemented.
- IF v1.0 is block-only. One-line IF, branch-local LET, and scopes are not implemented. Function/procedure calls in conditions are permanently prohibited, and standalone Boolean conditions are invalid because every atomic condition requires an explicit comparison.
- The supported IF nesting depth is limited to 128 as a compiler resource-safety boundary; ordinary programs within that depth retain the same IF syntax and behavior.
- SMILE Integer semantics are signed 64-bit. Generated storage is intentionally target-idiomatic and may be narrower when the complete bound program proves that safe; floating-point and decimal types are not implemented.
- Syntax highlighting is lexical only; semantic highlighting, autocomplete, and diagnostic squiggles are not implemented.
- C and MASM target output is focused on Windows local toolchains.
- COBOL local output uses GnuCOBOL free-format source and fixed-length storage for current SMILE display values.
- Objective-C local output currently uses a Foundation-free console profile on Windows; Foundation/NSString output remains future hardening.
- Swift local output requires Swift.Toolchain for Windows and Visual Studio C++ linker tools.
- Python local output requires Python 3.10 or newer; generated Python has no third-party package dependencies.
- C++ local output requires the Visual Studio x64 C++ tools and is compiled in C++20 mode.
- Unicode output beyond UTF-8 source text remains an area for later hardening.
- Full UI automation coverage is not included; manual WPF smoke testing is still required for release validation.

## Final Destination-Language Freeze

C++ is SMILE's tenth and final planned destination language. Target-language expansion is frozen so development can focus on input, loops, functions, scopes, debugging, and teaching tools. Do not add another destination language unless Sin explicitly reopens target expansion. Rust, Zig, and Go remain intentionally deferred and are not active targets.

## Roadmap

Future ideas, not implemented in v0.6.0.1:

1. v0.7.0 - `INPUT`
2. v0.8.0 - Loops
3. v0.9.0 - Functions and scopes
4. Floating-point and decimal numeric types
5. Debugging and source mapping
6. Semantic highlighting, autocomplete, and diagnostic squiggles
7. Reusable web interface
8. Evolution toward a full SMILE language

## License

SMILE is licensed under GNU Affero General Public License v3.0 only (`AGPL-3.0-only`). See [LICENSE](LICENSE).

SMILE.Desktop uses AvalonEdit 6.3.1.120 for WPF editor panes. AvalonEdit is licensed under MIT; its notice is recorded in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
