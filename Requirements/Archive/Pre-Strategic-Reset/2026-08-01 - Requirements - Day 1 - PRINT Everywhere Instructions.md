# Project SMILE v0.1 — Codex Implementation Instructions

## “PRINT Everywhere” MVP

Use this document as the complete implementation brief for Codex.

**Do not stop after producing another plan.** Inspect the existing repository, preserve all working behavior, create a feature branch, implement the MVP in logical milestones, build and test the solution, update the documentation, commit the work, and push the feature branch when GitHub authentication is available.

The project must remain simple, fast, understandable, educational, and pleasant to use.

---

# 1. Repository and development environment

- **Project:** SMILE
- **Repository:** `https://github.com/Sincioco/SMILE.git`
- **Expected local folder:** `C:\SMILE`
- **Primary environment:** Local Windows VM
- **Primary IDE:** Visual Studio 2026 Enterprise
- **Current platform:** C# and .NET 10
- **License:** GNU Affero General Public License v3.0 only (`AGPL-3.0-only`)

The repository currently contains a small C# console proof of concept that recognizes a BASIC-style statement such as:

```basic
Print "Hello World"
```

The current proof of concept:

- Recognizes one `PRINT` statement.
- Accepts straight and smart quotation marks.
- Translates the statement to C#.
- Executes the parsed statement directly.
- Targets .NET 10.

Preserve the useful behavior, but refactor it into a reusable multi-target transpiler.

Before editing anything, inspect the actual repository because it may have changed since this brief was written.

Run:

```bat
cmd /c cd /d C:\SMILE && git status --short --branch
cmd /c cd /d C:\SMILE && git remote -v
cmd /c cd /d C:\SMILE && git log --oneline --decorate -10
cmd /c dotnet --info
```

If `C:\SMILE` does not exist, clone it:

```bat
cmd /c git clone https://github.com/Sincioco/SMILE.git C:\SMILE
```

Do not reset, clean, overwrite, discard, or revert uncommitted user work.

---

# 2. Project mission

SMILE is no longer merely a tiny C# interpreter demonstration.

Its mission is:

> **SMILE is an educational, BASIC-inspired, multi-target transpiler designed to bring a smile to new developers by showing that programming languages share the same fundamental ideas even when their syntax, compiler, runtime, and platform conventions differ.**

A student should be able to write a simple SMILE program:

```basic
PRINT "Hello from SMILE!"
PRINT "Different syntax, same idea."
```

Then immediately see complete equivalent programs in:

1. C#
2. C
3. Assembly — Windows x64 MASM
4. JavaScript
5. Java

C#, Assembly, and C are the three default visible targets. JavaScript and Java must be selectable in the generated-language panes.

The application must allow each generated program to be built and run when its required local toolchain is installed.

The generated source must be valid, readable, idiomatic, and native to the conventions of its respective language.

Correct design:

```text
                              ┌── C# generator
                              ├── C generator
SMILE source → Lexer → Parser → AST ── MASM x64 generator
                              ├── JavaScript generator
                              └── Java generator
```

Incorrect design:

```text
SMILE → C# → C → Assembly
```

Every target generator must consume the same SMILE syntax tree directly.

“Native to the target language” means a complete, normal program written in that language and using its normal compiler, runtime, standard library, or operating-system facilities. It does not mean that C#, Java, or JavaScript must run without their normal runtimes.

The long-term possibility of evolving SMILE into a full programming language should guide clean separation of responsibilities, but do not attempt to build the full future language in this MVP.

---

# 3. Non-negotiable guiding principles

These principles govern the **entire SMILE project**:

- Product scope
- Architecture
- Source code
- User interface
- Performance
- Dependencies
- File and folder structure
- Documentation
- Tests
- Toolchain integration
- Generated C# code
- Generated C code
- Generated Assembly code
- Generated JavaScript code
- Generated Java code
- All future SMILE features

Add these principles prominently to the root `README.md`.

Also add permanent enforcement rules to `AGENTS.md` so every future Codex instance follows them.

## 3.1 KISS — Keep It Simple Stupid

> **The simpler the better. Avoid over-engineering. Avoid unnecessary complexity. Avoid unnecessary features. Avoid unnecessary bells and whistles. Avoid unnecessary abstractions. Avoid unnecessary patterns. Avoid unnecessary frameworks. Avoid unnecessary libraries. Avoid unnecessary dependencies. Avoid unnecessary code. Avoid unnecessary files. Avoid unnecessary folders. Avoid unnecessary classes. Avoid unnecessary methods. Avoid unnecessary variables.**

Apply this rule to every design and code decision.

Practical rules:

- Prefer the smallest solution that completely satisfies the current requirement.
- Do not implement speculative features.
- Do not add infrastructure for hypothetical future deployment.
- Do not create abstractions merely because they are fashionable.
- Do not create an interface unless it represents a real boundary or has more than one useful implementation.
- Do not create a class merely to hold one trivial method.
- Do not create folders that add no navigational value.
- Do not pre-create empty folders for possible future code.
- Do not install a package when the .NET base class library already provides a clear solution.
- Do not add a framework to avoid writing a few understandable lines of code.
- Prefer readable code over clever code.
- Prefer direct code over reflection, metaprogramming, or configuration-heavy systems.
- Delete obsolete implementations instead of leaving parallel versions.
- Do not build a full IDE when the MVP needs only simple source panes and build/run controls.
- Keep comments useful. Do not narrate obvious code.
- Avoid excessive XML documentation on private implementation details.
- Add only tests that protect meaningful behavior.

Necessary architectural boundaries are still permitted. Separating the language engine, local compiler toolchains, and WPF UI is justified because they have materially different responsibilities and because a future web UI should be able to reuse the transpiler engine.

## 3.2 KISS v2 — Keep It Speedy Sin, “The Sin Way”

> **Follow the original KISS method but with the mindset of creating a solution that gives the best possible performance both in terms of user experience, first priority, and functional performance, second priority. The user experience may take precedence over functional performance for any task.**

Use this priority order:

### First priority: user-experience performance

The application must feel:

- Immediate
- Responsive
- Smooth
- Predictable
- Clear
- Stable
- Easy to understand

The user must always know:

- What SMILE is doing
- Which operation is running
- Whether a compiler or runtime is available
- Whether an operation succeeded
- Why an operation failed
- How to cancel a long-running operation

A technically fast operation that freezes the interface or gives no feedback is not acceptable.

### Second priority: functional performance

After user experience is protected:

- Parse the source only once when generating several targets.
- Avoid unnecessary allocations in hot paths.
- Avoid repeated disk reads.
- Avoid repeated toolchain detection when cached results remain valid.
- Avoid launching unnecessary processes.
- Avoid compiling a target that has not changed when a safe, simple cache is eventually justified.
- Keep startup time fast.
- Keep generated source deterministic.
- Keep build and run operations efficient.
- Measure before introducing complicated optimization code.

Do not sacrifice readability and maintainability for an unmeasured micro-optimization.

## 3.3 KISS applies to generated code

The code SMILE generates is part of the product and must also follow KISS and KISS v2.

Every generator must produce code that is:

- Complete
- Correct
- Minimal
- Idiomatic
- Readable by a beginner
- Deterministic
- Fast enough for its purpose
- Free of unnecessary dependencies
- Free of unnecessary classes
- Free of unnecessary methods
- Free of unnecessary variables
- Free of unnecessary wrappers
- Free of unnecessary boilerplate beyond what the target language requires

Maintain a clear relationship between one SMILE statement and its generated target-language equivalent whenever practical.

Examples:

- Generate `Console.WriteLine(...)` in C#.
- Generate `puts(...)` in C when a newline is required.
- Generate `console.log(...)` in JavaScript.
- Generate `System.out.println(...)` in Java.
- Generate the minimum correct Windows API calls in MASM x64.
- Do not generate a dependency-injection container.
- Do not generate helper classes when a direct statement is clearer.
- Do not generate a framework project around a two-line program.
- Do not generate comments that merely repeat the code.
- Do not optimize generated code in a way that hides the educational relationship between SMILE and the target language.

When simplicity, educational clarity, and raw runtime performance conflict, use this order:

1. Correctness
2. User understanding
3. Simple idiomatic target-language code
4. Runtime performance

For the small educational programs in SMILE v0.1, clarity normally wins over micro-optimization.

## 3.4 Complexity check required for every feature

Before adding a dependency, layer, abstraction, pattern, service, class, folder, or generated-code helper, ask:

1. Is this required by a current accepted feature?
2. Does it remove real duplication or protect a real boundary?
3. Is there a simpler implementation?
4. Will a beginning developer understand it?
5. Does it improve the user experience?
6. Does it improve measured performance enough to justify the added complexity?

If the answer does not justify the addition, do not add it.

---

# 4. Permanent WPF responsiveness rule

The WPF UI thread must never be blocked by work that can take a noticeable amount of time.

This is a non-negotiable application requirement under KISS v2.

## 4.1 Never perform these operations synchronously on the UI thread

- Toolchain discovery
- Compiler version checks
- File generation involving substantial disk I/O
- C# compilation
- C compilation
- MASM assembly
- Linking
- Java compilation
- Running Node.js
- Running Java
- Running generated executables
- Reading redirected process output
- Waiting for a process to exit
- Recursive directory cleanup
- Large future parse/transpile operations
- Any operation shown by measurement to cause visible UI hesitation

## 4.2 Forbidden blocking patterns in WPF code

Do not use these on the UI thread:

```csharp
task.Wait();
task.Result;
Thread.Sleep(...);
process.WaitForExit();
process.StandardOutput.ReadToEnd();
process.StandardError.ReadToEnd();
```

Do not use synchronous shell/process calls from button handlers.

Do not wrap every operation blindly in `Task.Run`. Use true asynchronous APIs for process and I/O work. Use `Task.Run` only for genuinely CPU-bound work that would otherwise block the UI long enough to be noticeable.

Avoid `async void` except for unavoidable WPF event-handler boundaries. Prefer `Task`-returning commands and methods.

## 4.3 Required responsiveness behavior

- All build/run operations must use `async` and `await`.
- Standard output and standard error must be consumed asynchronously.
- Every long operation must accept a `CancellationToken`.
- The user must have a visible **Cancel** action while work is active.
- Cancellation or timeout must terminate the entire child process tree.
- Disable only controls that would cause an invalid conflicting operation.
- Keep source viewing, scrolling, pane resizing, and output viewing responsive.
- Show a busy state immediately.
- Show the current target and stage, such as Detecting, Generating, Building, Linking, Running, Completed, Failed, Cancelled, or Timed Out.
- Restore controls reliably in `finally` blocks.
- Catch expected errors and show useful messages instead of allowing an unhandled exception to close the application.
- Marshal only the final UI property updates back to the dispatcher.
- Keep library code independent of the WPF dispatcher.

## 4.4 Responsiveness acceptance test

During a C, MASM, C#, JavaScript, or Java build/run operation, the user must still be able to:

- Move the window.
- Resize the window.
- Resize the four panes.
- Scroll existing source and output.
- Click Cancel.
- See status updates.

Any visible freeze is a defect even when the compiler eventually succeeds.

Add this rule to `AGENTS.md` and describe it in the README under the project methodology or architecture section.

---

# 5. Living README policy

## 5.1 Update `README.md` now

Rewrite the existing `README.md` so it reflects the new mission and the functionality that actually exists at the end of this feature branch.

The README must no longer describe SMILE only as a tiny C# interpreter.

A suitable opening is:

```markdown
# SMILE

SMILE is an educational, BASIC-inspired, multi-target transpiler. It is designed to bring a smile to new developers by showing that programming languages share the same fundamental ideas even when their syntax, compiler, runtime, and platform conventions differ.

Write a simple SMILE program once, then view, build, and run equivalent programs in C#, C, Windows x64 MASM Assembly, JavaScript, and Java.
```

Adapt that wording to the implementation that actually exists.

The README must contain:

1. Project mission
2. Guiding principles: KISS and KISS v2, “The Sin Way”
3. The permanent WPF responsiveness principle
4. Current release status: `SMILE v0.1 — PRINT Everywhere`
5. A simple SMILE program
6. Generated examples for all supported targets
7. Supported-target table
8. Required local toolchain for Build & Run
9. Visual Studio 2026 setup requirements
10. How to clone, open, build, test, and run the solution
11. How to use the desktop application
12. How to use the CLI developer harness
13. Current SMILE v0.1 syntax
14. Repository structure
15. High-level architecture
16. Current limitations
17. Roadmap
18. License

Do not claim that a future feature already exists. Clearly distinguish implemented behavior from roadmap items.

## 5.2 Make README maintenance permanent

Update `AGENTS.md` with rules equivalent to:

```markdown
# Project Instructions

- Whenever Codex creates a commit, prefix the commit subject with `Sin and Codex:`.
- Commit messages must include a detailed summary of what changed.

## Guiding Principles

- KISS and KISS v2, “The Sin Way,” are the governing principles for the entire SMILE project, including architecture, UI, runtime behavior, documentation, tests, and all generated target-language code.
- Choose the simplest complete solution. Avoid unnecessary complexity, abstractions, frameworks, dependencies, code, files, folders, classes, methods, variables, features, and bells and whistles.
- User-experience performance is the first performance priority. Functional performance is second.
- The WPF UI thread must never be blocked by toolchain detection, compilation, linking, execution, process output, long file operations, or other noticeable work.
- Generated code must be minimal, idiomatic, readable, deterministic, educational, dependency-light, and fast without sacrificing clarity.

## Living Documentation

- `README.md` is the living source of truth for SMILE’s current mission, principles, features, supported syntax, target languages, toolchain requirements, setup steps, UI behavior, limitations, and roadmap.
- Every feature, command, target language, toolchain, UI behavior, build/run behavior, prerequisite, architecture change, renamed path, changed limitation, or changed generated output must update `README.md` in the same commit.
- Documentation must describe the code that actually exists after the change. Never present a roadmap item as implemented.
- When a change genuinely requires no README update, explain why in the commit or pull-request summary.
```

Preserve any existing valid repository instructions.

## 5.3 README completion rule

At the end of every milestone:

1. Review the README.
2. Update every affected section in the same commit as the code.
3. Test every command shown in the README.
4. Ensure paths, project names, prerequisites, target names, and examples match the application.
5. Remove stale or contradictory statements.
6. Confirm KISS, KISS v2, and generated-code guidance still match actual project behavior.

---

# 6. Product decision: desktop first, reusable engine

Build the initial proof of concept as a **WPF desktop application** because it runs locally in a Windows VM and needs to start local compilers and executables.

Use:

- C#
- .NET 10
- WPF
- Visual Studio 2026 Enterprise
- Standard .NET libraries
- MSTest

Do not use for this MVP:

- ASP.NET
- Blazor
- Electron
- A remote server
- Cloud compilation
- Docker
- Monaco Editor
- Roslyn as the SMILE parser
- ANTLR or another parser generator
- A third-party MVVM framework
- A third-party process-running library
- A third-party dependency-injection framework

Desktop first must not mean desktop coupled. The lexer, parser, syntax tree, diagnostics, and target generators must not reference WPF. A future web interface must be able to reuse the same transpiler engine.

---

# 7. Git workflow

Create a feature branch before implementation:

```bat
cmd /c cd /d C:\SMILE && git fetch origin
cmd /c cd /d C:\SMILE && git switch main
cmd /c cd /d C:\SMILE && git pull --ff-only origin main
cmd /c cd /d C:\SMILE && git switch -c feature/mvp-print-everywhere
```

If the branch already exists, inspect it and continue it rather than creating a conflicting branch.

Rules:

- Every Codex commit subject begins with `Sin and Codex:`.
- Commit messages include a detailed summary.
- Do not commit `bin`, `obj`, `.vs`, temporary compiler workspaces, generated executables, or compiler output.
- Never force-push.
- Never push feature work directly to `main`.
- Update README in the same commit as every relevant feature.

Suggested milestone commits:

```text
Sin and Codex: Create SMILE solution and language engine
Sin and Codex: Add multi-language PRINT generators
Sin and Codex: Add local build and run toolchains
Sin and Codex: Add the responsive four-quadrant WPF interface
Sin and Codex: Complete SMILE v0.1 tests and documentation
```

A different commit split is acceptable when it better matches the work. Every committed state must build and its relevant tests must pass.

Push when authentication is available:

```bat
cmd /c cd /d C:\SMILE && git push -u origin feature/mvp-print-everywhere
```

Open a draft pull request if supported. Do not merge it automatically.

---

# 8. Minimal justified solution structure

Do not create an excessive project tree or many empty folders merely because this document lists possible responsibilities.

Use the smallest solution structure that preserves the real boundaries:

```text
SMILE.sln
README.md
LICENSE
AGENTS.md
.gitignore
.editorconfig

examples/
    PrintEverywhere.smile

docs/
    SMILE-Language-Specification-v0.1.md
    Architecture.md
    Toolchains.md
    Roadmap.md

src/
    SMILE.Engine/
        SMILE.Engine.csproj
        language and generator source files

    SMILE.Toolchains/
        SMILE.Toolchains.csproj
        tool detection and process execution source files

    SMILE.Cli/
        SMILE.Cli.csproj
        Program.cs

    SMILE.Desktop/
        SMILE.Desktop.csproj
        WPF application source files

tests/
    SMILE.Tests/
        SMILE.Tests.csproj
        test source files
```

These five projects have justified responsibilities:

- `SMILE.Engine`: SMILE syntax, parsing, diagnostics, AST, and generation.
- `SMILE.Toolchains`: local compiler/runtime detection, build, and execution.
- `SMILE.Cli`: simple developer harness and preservation of console use.
- `SMILE.Desktop`: WPF user interface.
- `SMILE.Tests`: automated tests.

Do not split each target generator into its own project.

Do not create folders containing one small file unless the folder improves navigation.

Do not pre-create unused files or placeholder classes.

Project references:

```text
SMILE.Cli        → SMILE.Engine
SMILE.Cli        → SMILE.Toolchains

SMILE.Desktop    → SMILE.Engine
SMILE.Desktop    → SMILE.Toolchains

SMILE.Toolchains → SMILE.Engine

SMILE.Tests      → SMILE.Engine
SMILE.Tests      → SMILE.Toolchains
```

Target frameworks:

```text
SMILE.Engine      net10.0
SMILE.Toolchains  net10.0
SMILE.Cli         net10.0
SMILE.Desktop     net10.0-windows
SMILE.Tests       net10.0
```

The desktop project must include:

```xml
<UseWPF>true</UseWPF>
```

Use `git mv` when relocating existing tracked files.

Do not leave an obsolete duplicate root project.

Do not put two entry points in the same project. Separate entry points in `SMILE.Cli` and `SMILE.Desktop` are correct.

Preserve the `AGPL-3.0-only` package-license expression where appropriate.

---

# 9. SMILE language specification v0.1

Create:

```text
docs/SMILE-Language-Specification-v0.1.md
```

The first version is deliberately tiny.

## 9.1 Grammar

Implement and document behavior equivalent to:

```text
program             → line* end-of-file
line                → whitespace* statement? whitespace* newline
statement           → print-statement
print-statement     → PRINT whitespace+ string-literal
```

## 9.2 Valid source

```basic
PRINT "Hello World"
Print "Keywords are case-insensitive"
print “Smart quote delimiters are accepted”

PRINT "Multiple statements are supported"
PRINT "Every PRINT ends with a newline"
```

Required behavior:

- `PRINT` is case-insensitive.
- Blank lines are allowed.
- Multiple `PRINT` statements are allowed.
- A final newline is optional.
- Straight double-quote delimiters are accepted.
- Smart opening and closing double-quote delimiters are accepted.
- `PRINT` appends a newline.
- Source positions are preserved for diagnostics.
- User-visible line and column values are one-based.

Keep v0.1 narrow.

Do not add:

- Variables
- Numeric expressions
- Comments
- `INPUT`
- Conditions
- Loops
- Labels
- `GOTO`
- Functions
- Classes

Do not silently invent complicated string-escape syntax. Preserve the current straightforward quoted-text behavior. Embedded quote escaping may remain a documented v0.1 limitation unless the existing source already has a tested convention.

Every target generator must still escape the parsed string value correctly for its own language.

## 9.3 Invalid source and diagnostics

These must produce friendly diagnostics rather than crashes:

```basic
PRINT
PRINT Hello
PRINT "Unclosed
PRONT "Typo"
PRINT "Hello" extra
```

Every ordinary source error must return:

- Stable diagnostic code
- Severity
- Human-readable message
- One-based line
- One-based column
- Source span length when available

Suggested codes:

```text
SMILE1001  Unknown statement or keyword
SMILE1002  PRINT requires a quoted string
SMILE1003  Unterminated string literal
SMILE1004  Unexpected text after statement
SMILE1005  Invalid or unexpected character
```

The exact numbers may differ, but they must remain stable and be covered by tests.

Do not use exceptions for expected syntax errors.

---

# 10. Lexer, parser, syntax tree, and transpiler facade

Do not continue expanding one `SmileInterpreter` class with methods such as:

```csharp
TranslateToC()
TranslateToAssembly()
TranslateToJava()
```

Build a small genuine language front end without overengineering it.

## 10.1 Minimal syntax concepts

Provide the minimum concepts needed for v0.1:

```csharp
public enum SyntaxKind
{
    BadToken,
    EndOfFileToken,
    NewLineToken,
    PrintKeyword,
    StringLiteralToken
}
```

Whitespace may be skipped as long as source positions remain correct.

## 10.2 Source span

Use one small immutable type similar to:

```csharp
public readonly record struct TextSpan(
    int Start,
    int Length,
    int Line,
    int Column);
```

## 10.3 Minimal syntax tree

Use an immutable language-neutral tree similar to:

```csharp
public abstract record SyntaxNode(TextSpan Span);

public sealed record SmileProgramSyntax(
    IReadOnlyList<StatementSyntax> Statements,
    TextSpan Span)
    : SyntaxNode(Span);

public abstract record StatementSyntax(TextSpan Span)
    : SyntaxNode(Span);

public sealed record PrintStatementSyntax(
    string Text,
    TextSpan Span)
    : StatementSyntax(Span);
```

Do not add a separate expression hierarchy until SMILE actually supports more than a string literal. KISS applies.

When future expression support is added, refactor then.

## 10.4 Parse result

Expose a result similar to:

```csharp
public sealed record ParseResult(
    SmileProgramSyntax? Program,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool Success =>
        Program is not null &&
        Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);
}
```

## 10.5 Public facade

Create one simple public entry point:

```csharp
public sealed class SmileTranspiler
{
    public ParseResult Parse(string source);

    public TranspileResult Transpile(
        string source,
        TargetLanguage targetLanguage);

    public IReadOnlyList<TranspileResult> TranspileMany(
        string source,
        IEnumerable<TargetLanguage> targetLanguages);
}
```

`TranspileMany` must parse the source only once and give the same successful syntax tree to each generator.

The desktop application and CLI should primarily use this facade.

---

# 11. Target-language and generated-program abstractions

Use:

```csharp
public enum TargetLanguage
{
    CSharp,
    C,
    MasmX64,
    JavaScript,
    Java
}
```

Stable IDs and display names:

| Stable ID | Display name |
|---|---|
| `csharp` | C# |
| `c` | C |
| `masm-x64` | Assembly — Windows x64 MASM |
| `javascript` | JavaScript |
| `java` | Java |

A small generator interface is justified because five real implementations exist:

```csharp
public interface ICodeGenerator
{
    TargetLanguage Language { get; }

    GeneratedProgram Generate(SmileProgramSyntax program);
}
```

Represent generated output simply:

```csharp
public sealed record GeneratedFile(
    string RelativePath,
    string Content,
    bool IsPrimary);

public sealed record GeneratedProgram(
    TargetLanguage Language,
    IReadOnlyList<GeneratedFile> Files)
{
    public GeneratedFile PrimaryFile =>
        Files.Single(file => file.IsPrimary);
}
```

This allows C# to generate `Program.cs` and a small project file while the UI displays only `Program.cs`.

Every generated text file must:

- Be deterministic.
- Use consistent formatting.
- Use UTF-8 without a BOM unless a tool requires otherwise.
- End with exactly one newline.
- Avoid timestamps, GUIDs, random names, or machine-specific paths.
- Be suitable for exact-output tests.

Use one small registry or factory for generators. Do not scatter target-language switches throughout the application.

---

# 12. Required generators

Every generator consumes `SmileProgramSyntax` directly.

## 12.1 C#

Generate simple idiomatic code:

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

Generate:

```text
Program.cs
GeneratedProgram.csproj
```

The project targets .NET 10.

Do not add namespaces, dependency injection, configuration, logging frameworks, or unnecessary classes.

## 12.2 C

Generate:

```c
#include <stdio.h>

int main(void)
{
    puts("Hello from SMILE!");
    puts("Different syntax, same idea.");
    return 0;
}
```

Generate:

```text
Program.c
```

Use `puts` because SMILE `PRINT` appends a newline.

Do not generate a custom output framework.

For v0.1 cross-target acceptance tests, ASCII output is sufficient. Keep source text and internal strings as .NET Unicode strings so broader Unicode support can be improved later.

## 12.3 Assembly — Windows x64 MASM

Always label this target:

```text
Assembly — Windows x64 MASM
```

Generate real MASM x64 source using:

```text
GetStdHandle
WriteFile
ExitProcess
```

Do not:

- Generate pseudo-assembly.
- Use C compiler disassembly as output.
- Call a large runtime for a tiny `PRINT` program.
- Add unnecessary procedures.

A one-line output should be structurally similar to:

```asm
option casemap:none

EXTERN GetStdHandle:PROC
EXTERN WriteFile:PROC
EXTERN ExitProcess:PROC

STD_OUTPUT_HANDLE EQU -11

.data
message0 BYTE "Hello from SMILE!", 13, 10
message0Length EQU $ - message0
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

    xor ecx, ecx
    call ExitProcess
main ENDP

END
```

For multiple `PRINT` statements:

- Use deterministic labels such as `message0`, `message1`, and so on.
- Emit one clear `WriteFile` operation per SMILE `PRINT` statement.
- Preserve the educational one-to-one relationship unless a later measured need justifies a different strategy.
- Respect the Windows x64 calling convention, stack alignment, and shadow space.
- Do not modify nonvolatile registers without preserving them.

Generate:

```text
Program.asm
```

## 12.4 JavaScript

Generate:

```javascript
console.log("Hello from SMILE!");
console.log("Different syntax, same idea.");
```

Generate:

```text
Program.js
```

Do not add modules, packages, classes, or helper functions.

The button may say **Run** instead of **Build & Run**.

Transpilation works even when Node.js is absent.

## 12.5 Java

Generate:

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

Generate:

```text
Program.java
```

The class and `main` method are required Java structure. Add nothing beyond that requirement.

Transpilation works even when a JDK is absent.

## 12.6 Target-specific escaping

Create small focused escaping helpers for each target.

They must correctly handle the parsed text supported by SMILE v0.1 without injecting invalid target-language syntax.

Do not build generated code by directly inserting unescaped user text.

---

# 13. Toolchain architecture

Place compiler/runtime detection and process execution in `SMILE.Toolchains`.

The engine must never start a process.

The WPF window must never directly compose compiler commands.

A small interface is justified because five target toolchains exist:

```csharp
public interface IToolchain
{
    TargetLanguage Language { get; }

    Task<ToolchainStatus> DetectAsync(
        CancellationToken cancellationToken);

    Task<BuildRunResult> BuildAndRunAsync(
        GeneratedProgram generatedProgram,
        CancellationToken cancellationToken);
}
```

Keep result types focused. They must provide enough information for:

- Availability
- Version
- Location
- Build success
- Run success
- Build log
- Standard output
- Standard error
- Exit code
- Duration
- Timeout
- Cancellation
- Working directory

Implement:

```text
DotNetToolchain
MsvcCToolchain
MasmX64Toolchain
NodeToolchain
JavaToolchain
```

Do not add a general plugin system for v0.1.

---

# 14. Toolchain detection and commands

Missing tools must never prevent transpilation.

## 14.1 C# / .NET

Detect:

```bat
cmd /c dotnet --version
```

Generate `Program.cs` and `GeneratedProgram.csproj`.

Build:

```bat
cmd /c dotnet build GeneratedProgram.csproj
```

Run the built executable or use a separate controlled `dotnet run` step.

Keep build and run results distinguishable in the UI.

## 14.2 Microsoft C compiler

Do not hardcode a Visual Studio year, edition, or install path.

Use:

```text
%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe
```

Locate the newest suitable Visual Studio installation containing Visual C++ x64 tools.

Use:

```text
VC\Auxiliary\Build\vcvars64.bat
```

Compile in C mode:

```bat
cmd /c call "<vcvars64.bat>" && cl /nologo /TC /utf-8 Program.c /Fe:Program.exe
```

Run:

```bat
cmd /c Program.exe
```

Raw SMILE source must never enter a command line.

## 14.3 MASM x64

Use the same detected Visual Studio developer environment.

Assemble:

```bat
cmd /c call "<vcvars64.bat>" && ml64 /nologo /c Program.asm /Fo:Program.obj
```

Link:

```bat
cmd /c call "<vcvars64.bat>" && link /nologo Program.obj kernel32.lib /subsystem:console /entry:main /out:Program.exe
```

Run:

```bat
cmd /c Program.exe
```

## 14.4 JavaScript

Detect:

```bat
cmd /c node --version
```

Run:

```bat
cmd /c node Program.js
```

## 14.5 Java

Detect both:

```bat
cmd /c javac -version
cmd /c java -version
```

Build:

```bat
cmd /c javac Program.java
```

Run:

```bat
cmd /c java Program
```

A JRE without `javac` is not sufficient for Build & Run.

---

# 15. Safe asynchronous process execution

Build one small reusable process runner with `System.Diagnostics.Process`.

Requirements:

- `UseShellExecute = false`
- Redirect standard output
- Redirect standard error
- No visible console window
- Asynchronous output consumption
- Asynchronous wait for exit
- Cancellation token support
- Configurable timeout
- Kill the entire process tree on timeout or cancellation
- Capture exit code
- Capture duration
- Avoid stdout/stderr deadlocks
- Quote paths safely
- Never place raw source contents in arguments
- Do not depend on the desktop application's current directory

Use `ProcessStartInfo.ArgumentList` when invoking an executable directly.

Use `cmd.exe` only where required to initialize the Visual Studio batch environment.

Put each build in a unique workspace:

```text
%TEMP%\SMILE\Runs\<unique-id>\
```

Write generated files only inside that workspace.

Do not build generated targets in the repository.

Clean old SMILE-owned workspaces safely. Never delete outside the SMILE temporary root.

Use a reasonable default program timeout, such as 10 seconds.

Because generated panes are read-only and SMILE v0.1 has a constrained grammar, do not add a feature that compiles arbitrary edited target-language code.

---

# 16. Four-quadrant WPF interface

Build a simple, responsive desktop application rather than a full IDE.

```text
┌──────────────────────────────┬──────────────────────────────┐
│ SMILE                        │ C#                     [▼]    │
│                              │                              │
│ PRINT "Hello from SMILE!"    │ using System;                │
│ PRINT "Different syntax..."  │ ...                          │
│                              │                              │
│ [Transpile]                  │ [Copy] [Save] [Build & Run]  │
├──────────────────────────────┼──────────────────────────────┤
│ Assembly — MASM x64   [▼]    │ C                      [▼]    │
│                              │                              │
│ option casemap:none          │ #include <stdio.h>           │
│ ...                          │ ...                          │
│                              │                              │
│ [Copy] [Save] [Build & Run]  │ [Copy] [Save] [Build & Run]  │
└──────────────────────────────┴──────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│ Diagnostics, Build, and Program Output                      │
└─────────────────────────────────────────────────────────────┘
```

## 16.1 Pane behavior

- Top-left is always editable SMILE source.
- Top-right defaults to C#.
- Bottom-left defaults to Assembly — Windows x64 MASM.
- Bottom-right defaults to C.
- Each generated pane has a language selector.
- Selectors offer C#, C, MASM x64, JavaScript, and Java.
- Generated panes are read-only.
- Use a monospace font such as Consolas.
- A standard WPF `TextBox` is sufficient.
- Do not add Monaco or another heavyweight editor.
- Use `GridSplitter` controls for resizing.

## 16.2 Main actions

Provide:

- New
- Open `.smile`
- Save
- Save As
- Transpile All
- Build & Run Visible Languages
- Cancel while work is active

Each generated pane provides:

- Language selector
- Copy
- Save Source
- Build & Run, or Run for JavaScript
- Toolchain availability
- Current status

Run visible targets sequentially for this MVP. It is simpler, easier to understand, and avoids unnecessary concurrent compiler processes.

## 16.3 Diagnostics and output

Display:

- SMILE diagnostics
- Target name
- Toolchain status
- Current stage
- Build output
- Compiler errors
- Program standard output
- Program standard error
- Exit code
- Duration
- Timeout
- Cancellation

When syntax is invalid:

- Show diagnostic code, line, column, and message.
- Disable Build & Run for the current invalid source.
- Do not crash.
- Keep the UI responsive.

When a toolchain is missing:

- Still display generated source.
- Keep Copy and Save enabled.
- Disable Build & Run for that target.
- Explain exactly what is missing.

## 16.4 Minimal WPF architecture

Use a small MVVM-style separation with standard .NET/WPF code.

A tiny in-repository `ViewModelBase`, `RelayCommand`, and `AsyncRelayCommand` are acceptable when they remove real duplication.

Do not add a third-party MVVM framework.

Do not mechanically create a view model for every label or button.

Code-behind may contain strictly visual wiring, but not:

- Lexer logic
- Parser logic
- Generator logic
- Toolchain command construction
- External process execution

Reuse one target-pane control or one target-pane view model rather than copying the same code three times.

Load this example on first launch:

```basic
PRINT "Hello from SMILE!"
PRINT "Different syntax, same idea."
```

---

# 17. CLI developer harness

Preserve the console capability as a small developer harness.

Support a command equivalent to:

```bat
cmd /c dotnet run --project src\SMILE.Cli -- examples\PrintEverywhere.smile --target all
```

Target values:

```text
csharp
c
masm-x64
javascript
java
all
```

Support `--run` when practical:

```bat
cmd /c dotnet run --project src\SMILE.Cli -- examples\PrintEverywhere.smile --target csharp --run
```

Use a small manual argument parser. Do not add a CLI framework for this MVP.

The CLI must use `SMILE.Engine` and `SMILE.Toolchains`.

Exit behavior:

- `0` on success
- Nonzero for invalid arguments
- Nonzero for SMILE diagnostics
- Nonzero for generation failure
- Nonzero for compiler failure
- Nonzero for runtime failure
- Nonzero for timeout

Do not print stack traces for ordinary user errors.

---

# 18. Automated tests

Use MSTest.

The MVP is not complete without tests.

## 18.1 Lexer/parser tests

Cover:

- `PRINT` in upper, lower, and mixed case
- Straight quotes
- Smart quotes
- CRLF and LF
- Blank lines
- End-of-file without final newline
- One `PRINT`
- Multiple `PRINT` statements
- Empty program
- Missing string
- Unquoted string
- Unterminated string
- Unknown keyword
- Extra text
- Correct line and column
- Expected syntax errors do not throw

## 18.2 Generator golden-output tests

For each target, compare exact generated output.

Cover:

- Empty program
- One `PRINT`
- Multiple `PRINT` statements
- Target-specific escaping
- Deterministic output
- Exactly one trailing newline
- Correct filename
- C# project file
- Unique MASM labels
- Minimal generated structure
- No unnecessary generated helpers, classes, methods, or variables

The tests should protect KISS in generated code. A future change that adds unnecessary boilerplate should be visible in the golden-output diff.

## 18.3 Process and toolchain tests

Abstract process execution only enough to test meaningful behavior.

Cover:

- Tool available
- Tool missing
- Nonzero exit code
- Timeout
- Cancellation
- Standard output capture
- Standard error capture
- Process-tree termination behavior where practical

## 18.4 Conditional integration tests

Use:

```basic
PRINT "Hello from SMILE!"
PRINT "Different syntax, same idea."
```

Expected normalized output:

```text
Hello from SMILE!
Different syntax, same idea.
```

Run when available:

1. C# with .NET
2. C with MSVC
3. MASM x64 with `ml64` and `link`
4. JavaScript with Node.js
5. Java with `javac` and `java`

A missing optional toolchain should be reported as skipped or inconclusive, not as a false success and not as a unit-test failure.

## 18.5 Cross-target equivalence

For every installed target, assert that normalized program output is identical.

## 18.6 WPF responsiveness verification

At minimum, manually verify and document that during build/run:

- The window moves.
- The window resizes.
- Panes resize.
- Existing text scrolls.
- Cancel responds.
- Status changes are visible.
- No UI freeze occurs.

Where practical, unit-test command state, cancellation, and asynchronous view-model behavior without attempting fragile full UI automation.

---

# 19. Documentation

Create only useful documentation. Do not create empty placeholder documents.

## `docs/SMILE-Language-Specification-v0.1.md`

Document only implemented syntax.

## `docs/Architecture.md`

Explain:

```text
Source → Lexer → Parser → Syntax Tree → Target Generator → Generated Files
                                              ↓
                                    Optional Local Toolchain
                                              ↓
                                      Build and Run Result
```

Explain:

- Why each target is generated directly from the AST.
- Why the engine has no WPF dependency.
- Why process execution is asynchronous.
- How KISS and KISS v2 guide the architecture.

## `docs/Toolchains.md`

Document:

- Detection
- Visual Studio C++ requirement
- .NET requirement
- Optional Node.js
- Optional JDK
- Commands
- Temporary workspaces
- Timeout and cancellation
- Missing-tool troubleshooting

## `docs/Roadmap.md`

Clearly separate implemented work from future ideas:

1. `LET` and variables
2. Printing variables and expressions
3. `INPUT`
4. Numeric and string expressions
5. `IF / THEN / ELSE`
6. Loops
7. Functions
8. Type checking
9. Debugging/source mapping
10. Reusable web interface
11. Evolution toward a full SMILE language

Do not implement these items in v0.1.

---

# 20. Implementation milestones

Work in this order.

## Milestone 0 — Baseline

1. Inspect Git status and history.
2. Build and run the current project.
3. Record existing behavior.
4. Create the feature branch.
5. Confirm license and repository instructions.
6. Preserve uncommitted work.

## Milestone 1 — Mission, principles, and language engine

1. Update `README.md` with the new mission.
2. Add KISS and KISS v2 as governing principles.
3. Add the WPF responsiveness rule.
4. Update `AGENTS.md` with permanent enforcement rules.
5. Create the solution and minimum justified projects.
6. Move existing code carefully.
7. Implement diagnostics, lexer, parser, and minimal immutable syntax tree.
8. Support multiple `PRINT` statements.
9. Preserve straight and smart quote support.
10. Add language tests.
11. Add the language specification.
12. Build and test.
13. Commit with README updates included.

## Milestone 2 — Multi-language generation

1. Implement the generator contract and registry.
2. Implement C#.
3. Implement C.
4. Implement MASM x64.
5. Implement JavaScript.
6. Implement Java.
7. Apply KISS and KISS v2 to all generated output.
8. Add exact golden-output tests.
9. Add `examples\PrintEverywhere.smile`.
10. Update README with actual generated examples.
11. Build and test.
12. Commit.

## Milestone 3 — Asynchronous local toolchains

1. Implement the asynchronous process runner.
2. Implement timeout and cancellation.
3. Implement .NET build/run.
4. Discover Visual Studio without hardcoded paths.
5. Implement C build/run.
6. Implement MASM assemble/link/run.
7. Implement Node.js run.
8. Implement Java build/run.
9. Add conditional integration tests.
10. Confirm no operation requires the WPF UI thread.
11. Update README and toolchain documentation.
12. Build and test.
13. Commit.

## Milestone 4 — Responsive four-quadrant WPF application

1. Build the main four-quadrant layout.
2. Add fixed SMILE editor.
3. Add three switchable generated panes.
4. Default to C#, MASM x64, and C.
5. Include JavaScript and Java in selectors.
6. Add Transpile All.
7. Add Copy.
8. Add Save Source.
9. Add per-pane Build & Run.
10. Add Build & Run Visible Languages.
11. Add output and diagnostics area.
12. Add toolchain status.
13. Add Open and Save `.smile`.
14. Add Cancel.
15. Keep all long work off the UI thread.
16. Manually verify responsiveness.
17. Update README with actual desktop behavior.
18. Build and test.
19. Commit.

## Milestone 5 — Hardening

1. Run the full test suite.
2. Run every installed target.
3. Verify identical normalized output.
4. Verify missing tools fail gracefully.
5. Verify paths containing spaces.
6. Verify CRLF and LF.
7. Verify syntax diagnostics.
8. Verify cancellation.
9. Verify timeout.
10. Verify the UI never freezes.
11. Verify generated code remains minimal and idiomatic.
12. Verify no build artifacts appear in Git.
13. Review all documentation.
14. Confirm README exactly matches the final implementation.
15. Commit, push, and open a draft PR when supported.

---

# 21. Definition of done

## Mission and principles

- [ ] README describes the new educational multi-target mission.
- [ ] README contains KISS.
- [ ] README contains KISS v2, “The Sin Way.”
- [ ] README states that these guide the entire project.
- [ ] README states that the principles also govern generated code.
- [ ] README documents the no-blocking WPF UI rule.
- [ ] `AGENTS.md` permanently enforces all of these rules.
- [ ] README maintenance is required for every future relevant feature.

## Language

- [ ] One source supports multiple `PRINT` statements.
- [ ] Parsing produces a language-neutral syntax tree.
- [ ] Source is parsed once for multiple targets.
- [ ] Syntax errors return diagnostics.
- [ ] Smart quote delimiters continue to work.

## Generation

- [ ] C# output is complete, minimal, and buildable.
- [ ] C output is complete, minimal, and buildable.
- [ ] MASM output is real Windows x64 MASM.
- [ ] JavaScript output is complete and minimal.
- [ ] Java output is complete and minimal.
- [ ] All target output follows KISS and KISS v2.
- [ ] Each generator consumes the same AST directly.
- [ ] Output is deterministic.
- [ ] Generated panes are read-only.

## Toolchains

- [ ] Transpilation works without installed target tools.
- [ ] Build & Run works for every installed toolchain.
- [ ] Missing tools are explained clearly.
- [ ] Standard output and error are captured.
- [ ] Exit code and duration are captured.
- [ ] Timeout works.
- [ ] Cancellation works.
- [ ] Child process trees are terminated.
- [ ] Build artifacts stay out of the repository.

## WPF user experience

- [ ] Four code quadrants are visible.
- [ ] Top-left remains SMILE.
- [ ] Defaults are C#, MASM x64, and C.
- [ ] JavaScript and Java are selectable.
- [ ] Transpile All works.
- [ ] Copy and Save Source work.
- [ ] Open and Save `.smile` work.
- [ ] Build & Run works where available.
- [ ] Build & Run Visible Languages runs sequentially.
- [ ] Status and diagnostics are visible.
- [ ] Cancel is visible during work.
- [ ] The WPF UI thread never blocks on long work.
- [ ] The window stays responsive during every build and run.

## Validation

- [ ] `dotnet build SMILE.sln` succeeds.
- [ ] `dotnet test SMILE.sln` succeeds.
- [ ] Debug and Release builds succeed.
- [ ] Installed targets produce identical normalized output.
- [ ] README commands were actually tested.
- [ ] Documentation and code agree.
- [ ] Feature branch is pushed when authentication is available.

---

# 22. Required validation commands

Run from `C:\SMILE`:

```bat
cmd /c dotnet restore SMILE.sln
cmd /c dotnet build SMILE.sln -c Debug
cmd /c dotnet test SMILE.sln -c Debug --no-build
cmd /c dotnet build SMILE.sln -c Release
cmd /c dotnet test SMILE.sln -c Release --no-build
```

Exercise the CLI:

```bat
cmd /c dotnet run --project src\SMILE.Cli -- examples\PrintEverywhere.smile --target all
```

Exercise every available target:

```bat
cmd /c dotnet run --project src\SMILE.Cli -- examples\PrintEverywhere.smile --target csharp --run
cmd /c dotnet run --project src\SMILE.Cli -- examples\PrintEverywhere.smile --target c --run
cmd /c dotnet run --project src\SMILE.Cli -- examples\PrintEverywhere.smile --target masm-x64 --run
cmd /c dotnet run --project src\SMILE.Cli -- examples\PrintEverywhere.smile --target javascript --run
cmd /c dotnet run --project src\SMILE.Cli -- examples\PrintEverywhere.smile --target java --run
```

Commands for unavailable optional toolchains may report that the tool is missing. They must do so clearly and without crashing.

Check repository cleanliness:

```bat
cmd /c git status --short
```

Do not commit transient build files.

---

# 23. Final Codex report

When finished, provide:

1. Branch name
2. Commit hashes and subjects
3. Summary of implemented features
4. Final solution structure
5. Test totals and results
6. Toolchains detected
7. Targets actually compiled and run
8. Normalized output from each executed target
9. Missing toolchains and exact prerequisites
10. WPF responsiveness validation performed
11. README and AGENTS changes
12. Known limitations
13. Draft pull-request link, if created
14. Exact remaining work, if anything could not be completed

Do not claim a compiler, runtime, UI action, test, or generated target was verified unless it was actually verified.

---

# 24. Scope boundary

Do not add these features during this MVP:

- Variables
- `LET`
- `INPUT`
- Conditions
- Loops
- `GOTO`
- Functions
- Classes
- Debugger
- IntelliSense
- Syntax highlighting framework
- Package manager
- Cloud deployment
- User accounts
- Online compilation
- Arbitrary editing and compilation of generated target code
- Multiple Assembly architectures
- Plugin systems
- Extension marketplaces
- Telemetry
- Automatic updates

The completed vertical slice is:

```text
SMILE PRINT source
        ↓
Parse once
        ↓
Generate simple native code for five targets
        ↓
Show three targets at a time in a responsive four-quadrant WPF UI
        ↓
Build and run locally when the relevant toolchain exists
        ↓
Display identical output
```

That is the complete SMILE v0.1 proof of concept.

---

# 25. Final implementation instruction

Begin with the smallest complete implementation that proves the concept.

At every decision point:

1. Apply KISS.
2. Apply KISS v2, “The Sin Way.”
3. Protect the WPF user experience.
4. Keep generated code simple and educational.
5. Update README in the same change.
6. Build and test before committing.
7. Do not add anything the MVP does not need.
