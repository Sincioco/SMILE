# Codex Instructions — Implement the Official SMILE PRINT Syntax

## Target release: SMILE v0.2.0 — Friendly PRINT

Use this document as the complete implementation brief for the official SMILE `PRINT` statement syntax.

The normative syntax is defined by the companion document:

```text
SMILE_PRINT_Statement_Official_Specification_v1.0.md
```

Copy that document into the repository as:

```text
docs/SMILE-PRINT-Statement-Specification-v1.0.md
```

Do not stop after writing a plan. Inspect the current repository, implement the syntax, create comprehensive tests, run all validation locally on Sin's Windows VM, update documentation, commit the work, and push the feature branch when all local validation is green.

Do not add GitHub Actions or any other remote CI service.

---

# 1. Repository and prerequisites

- Repository: `https://github.com/Sincioco/SMILE.git`
- Expected local folder: `C:\SMILE`
- Primary IDE: Visual Studio 2026 Enterprise
- Framework: .NET 10 and WPF
- Development and validation: entirely local on Sin's Windows VM
- License: `AGPL-3.0-only`

The implementation must preserve all existing working features:

- C#, C, Windows x64 MASM, JavaScript, Java, Objective-C, and Swift output.
- Local Build & Run where a Windows toolchain exists.
- Objective-C and Swift transpile-only status on Windows.
- Four-quadrant WPF interface.
- Debounced, asynchronous, latest-source-wins live transpilation.
- Press Any Key Launcher.
- Generated workspace inspection.
- KISS and KISS v2.
- Living README and documentation rules.

If the responsive live-transpilation hardening branch has not yet been merged, do not overwrite or regress it. Start this work from the latest clean branch that contains those fixes, or finish that work first.

---

# 2. Git workflow

Inspect before editing:

```bat
cmd /c cd /d C:\SMILE && git status --short --branch
cmd /c cd /d C:\SMILE && git remote -v
cmd /c cd /d C:\SMILE && git log --oneline --decorate -10
```

Never discard or overwrite user work.

When safe:

```bat
cmd /c cd /d C:\SMILE && git fetch origin
cmd /c cd /d C:\SMILE && git switch main
cmd /c cd /d C:\SMILE && git pull --ff-only origin main
cmd /c cd /d C:\SMILE && git switch -c feature/v0.2-friendly-print
```

If that branch already exists, inspect and continue it.

Commit subjects must begin with:

```text
Sin and Codex:
```

Do not force-push.

Do not merge automatically.

---

# 3. Scope

Implement the complete official `PRINT` syntax:

```basic
PRINT
PRINT "Hello World!"
PRINT Hello World!
PRINT Hello {Name}!
PRINT $"Hello {Name}!"
PRINT "Hello " + Name + "!"
```

Also implement the minimum string-variable support required to execute and test:

```basic
LET Name = "Sin"
```

This release is not a full general-purpose expression/type release.

Implement only the expression and variable behavior needed for:

- String literals.
- String variables.
- Case-insensitive identifier references.
- String concatenation with `+`.
- Raw template interpolation.
- `$"..."` interpolation.
- Blank `PRINT`.

Do not add numeric types, loops, conditions, functions, arrays, classes, package management, or unrelated language features.

The parser and semantic model must nevertheless be structured so those features can be added later without reparsing source in every backend.

---

# 4. KISS and KISS v2

## KISS

Use the smallest complete compiler architecture that preserves the future of SMILE.

Do not add:

- ANTLR.
- Roslyn as the SMILE parser.
- A parser-generator dependency.
- A third-party MVVM framework.
- A third-party compiler framework.
- A plugin architecture.
- A service container.
- Reflection-based dispatch.
- Multiple projects for individual generators.
- Unnecessary syntax wrappers.
- A large generic type system.

## KISS v2

Typing must remain immediate.

The new parser and generators must not reintroduce synchronous live-transpilation work on the WPF thread.

Automatic live preview must remain:

- Debounced.
- Cancellable.
- Background-executed.
- Latest-source-wins.
- Limited to visible distinct targets.

Manual `Transpile All` may generate all targets asynchronously.

---

# 5. Publish the official specification

Add:

```text
docs/SMILE-PRINT-Statement-Specification-v1.0.md
```

Use the companion official specification verbatim except for repository-relative links or minor formatting corrections.

Update `README.md` to:

- Link to the official `PRINT` specification.
- Explain the beginner-friendly raw template form.
- Explain ordinary quotes, `$"..."`, and `{...}` interpolation.
- Explain one statement per line.
- Explain case-insensitivity.
- Show `PRINT Name` versus `PRINT {Name}`.
- List the supported syntax actually implemented.
- Clearly distinguish implemented behavior from future expression types.

Update `AGENTS.md` with permanent rules equivalent to:

```markdown
- The official PRINT syntax is defined by docs/SMILE-PRINT-Statement-Specification-v1.0.md.
- PRINT parsing must be deterministic and must never guess whether bare text is a variable.
- Bare PRINT text is literal template text; expressions require braces.
- Ordinary quoted strings do not interpolate; $"..." and raw templates do.
- A physical source line normally contains one statement, and semicolons do not separate statements.
- A second standalone PRINT keyword on the same line is a compiler error.
- New language work must preserve asynchronous debounced WPF live transpilation.
```

---

# 6. Language architecture

The current literal-only node:

```csharp
PrintStatementSyntax(string Text)
```

is no longer sufficient.

Refactor toward:

```text
Source
  -> Lexer
  -> Parser
  -> Syntax Tree
  -> Binder / Semantic Analysis
  -> Bound Program
  -> Target Generators
```

Do not let target generators inspect or reparse raw SMILE source.

## 6.1 Syntax nodes

Use minimal immutable nodes similar to:

```csharp
public abstract record ExpressionSyntax(TextSpan Span)
    : SyntaxNode(Span);

public sealed record StringLiteralExpressionSyntax(
    string Value,
    TextSpan Span)
    : ExpressionSyntax(Span);

public sealed record NameExpressionSyntax(
    string Name,
    TextSpan Span)
    : ExpressionSyntax(Span);

public sealed record BinaryExpressionSyntax(
    ExpressionSyntax Left,
    SyntaxToken OperatorToken,
    ExpressionSyntax Right,
    TextSpan Span)
    : ExpressionSyntax(Span);

public sealed record InterpolatedStringExpressionSyntax(
    IReadOnlyList<InterpolatedPartSyntax> Parts,
    TextSpan Span)
    : ExpressionSyntax(Span);

public abstract record InterpolatedPartSyntax(TextSpan Span)
    : SyntaxNode(Span);

public sealed record InterpolatedTextPartSyntax(
    string Text,
    TextSpan Span)
    : InterpolatedPartSyntax(Span);

public sealed record InterpolationExpressionPartSyntax(
    ExpressionSyntax Expression,
    TextSpan Span)
    : InterpolatedPartSyntax(Span);

public sealed record PrintStatementSyntax(
    ExpressionSyntax Value,
    TextSpan Span)
    : StatementSyntax(Span);

public sealed record LetStatementSyntax(
    string Name,
    ExpressionSyntax Initializer,
    TextSpan Span)
    : StatementSyntax(Span);
```

Exact names may differ. Keep the model small.

## 6.2 Bound semantic model

Add a minimal binder.

Use a case-insensitive symbol table:

```csharp
new Dictionary<string, VariableSymbol>(
    StringComparer.OrdinalIgnoreCase)
```

Recommended minimal bound model:

```text
BoundProgram
BoundLetStatement
BoundPrintStatement
BoundLiteralExpression
BoundVariableExpression
BoundConcatenationExpression
BoundInterpolatedStringExpression
```

Only the `String` type is required in this release.

A tiny type representation is enough:

```csharp
public enum SmileType
{
    String
}
```

Do not create a generic multi-type framework yet.

## 6.3 Why a binder is required

The binder must:

- Resolve identifiers case-insensitively.
- Detect undefined variables.
- Detect duplicate declarations case-insensitively.
- Keep target generators independent of SMILE source spelling.
- Give all generators the same meaning.
- Allow future types and expressions without rewriting every parser/backend boundary.

---

# 7. Minimal LET support

Implement enough `LET` syntax to make `PRINT` interpolation executable:

```basic
LET Name = "Sin"
```

Required v0.2 behavior:

- `LET` is case-insensitive.
- Identifier lookup is case-insensitive.
- The initializer must currently evaluate to a string.
- A declaration must occur before use.
- Duplicate declarations differing only by case are errors.

Examples:

```basic
LET Name = "Sin"
PRINT Hello {Name}!
```

Valid.

```basic
LET Name = "Sin"
PRINT Hello {name}!
```

Valid.

```basic
LET Name = "Sin"
LET NAME = "Joy"
```

Compiler error: duplicate variable.

Do not silently turn undeclared identifiers into strings.

Whether future SMILE permits reassignment through `LET` is outside this task. For this release, treat `LET` as a string variable declaration and document that limitation.

---

# 8. PRINT parser dispatch

After recognizing the case-insensitive `PRINT` keyword:

1. If only whitespace remains, create a blank-line print containing an empty string.
2. If a payload exists, require at least one space or tab after `PRINT`.
3. If the payload begins with `$"`, parse an interpolated quoted string.
4. Else if it begins with `"`, parse the remainder of the line as a string expression.
5. Else parse the remainder as a raw template.
6. Reject a second standalone `PRINT` keyword on the same physical line outside quoted text.
7. Require end-of-line/end-of-file after the selected form.

Do not implement this by repeatedly attempting grammars and falling back on failure.

The first visible payload characters determine the mode.

---

# 9. Raw template parser

A raw template:

```basic
PRINT Hello {Name}!
```

must lower to:

```text
InterpolatedStringExpression
  TextPart("Hello ")
  ExpressionPart(NameExpression("Name"))
  TextPart("!")
```

A raw template with no interpolation:

```basic
PRINT Hello World!
```

may lower to either:

```text
StringLiteralExpression("Hello World!")
```

or an interpolated string with one text part.

Prefer the simpler representation that keeps generators straightforward.

## Required rules

- Strip separator whitespace after `PRINT`.
- Ignore trailing spaces/tabs at line end.
- Preserve internal spaces/tabs.
- `{expression}` is an interpolation hole.
- `{{` becomes `{`.
- `}}` becomes `}`.
- An unmatched `{` is an error.
- An unmatched `}` is an error.
- `{}` is an error.
- A raw template consumes the rest of the physical line.
- Comment markers are ordinary text in a raw template.
- Semicolons are ordinary text in a raw template.

Do not use regular expressions to parse nested/quoted interpolation expressions.

Use source-aware scanning with line/column spans.

---

# 10. Interpolated quoted strings

Parse:

```basic
PRINT $"Hello {Name}!"
```

into the same semantic interpolated-string representation used by raw templates.

Required behavior:

- Text outside braces is literal.
- `{expression}` is evaluated.
- `{{` and `}}` produce literal braces.
- The closing `"` must appear on the same line.
- Ordinary strings inside an interpolation expression must not prematurely close the outer string.
- Diagnostics must identify malformed braces and unterminated strings.

This release may limit interpolation expressions to:

- Identifiers.
- String literals.
- String concatenation with `+`.

Structure the parser so the expression grammar can expand later.

---

# 11. Quoted string expressions and concatenation

Support:

```basic
PRINT "Hello"
PRINT "Hello " + Name
PRINT "Hello " + Name + "!"
```

Minimal grammar:

```text
string-expression
    -> string-term ('+' string-term)*

string-term
    -> string-literal
     | identifier
     | interpolated-string
```

`+` is left-associative.

All operands must currently be strings.

This line is raw text because it starts with an unquoted identifier:

```basic
PRINT Name + "!"
```

It outputs:

```text
Name + "!"
```

To evaluate it:

```basic
PRINT {Name + "!"}
```

or:

```basic
PRINT $"{Name}!"
```

The parser must never use symbol-table state to decide whether a line is raw text or an expression.

---

# 12. Case-insensitive keywords and identifiers

Update keyword recognition so:

```basic
PRINT
Print
print
pRiNt
```

are identical.

Do the same for `LET`.

Variable lookup must use ordinal case-insensitive comparison.

Preserve source spelling for diagnostics.

Add tests proving:

```basic
LET CustomerName = "Sin"
PRINT {customername}
PRINT {CUSTOMERNAME}
```

resolve to the same variable.

---

# 13. One statement per line and duplicate PRINT

A physical line may contain only one statement.

Reject:

```basic
PRINT Hello PRINT World
print Hello PrInT World
PRINT "Hello"; PRINT "World"
```

Use a stable diagnostic such as:

```text
SMILE1102
Only one PRINT statement is allowed per line.
```

The second keyword's span should be highlighted.

Do not flag these:

```basic
PRINT "Use PRINT to display text."
PRINT Reprint this report.
PRINT PRINTABLE text.
PRINT Use "PRINT" as the command name.
PRINT Use {"PRINT"} as the command name.
```

A second `PRINT` counts only when it is a standalone, case-insensitive keyword token outside quoted text.

Use lexical identifier boundaries. Do not use `Contains`, simple substring matching, or a case-insensitive regex over the entire line without lexical context.

---

# 14. Whitespace rule

Require horizontal whitespace between `PRINT` and a payload.

Reject:

```basic
PRINT"Hello"
PRINT$"Hello"
```

Accept:

```basic
PRINT "Hello"
PRINT Hello
PRINT	Hello
PRINT
PRINT    
```

Use a stable diagnostic such as:

```text
SMILE1101
PRINT requires a space or tab before its payload.
```

---

# 15. Diagnostics

Preserve current stable diagnostic meanings. Do not silently reuse an existing diagnostic code for a different error.

Add stable codes for the new syntax. Recommended codes:

```text
SMILE1101  PRINT requires whitespace before its payload.
SMILE1102  Only one PRINT statement is allowed per line.
SMILE1103  Unterminated interpolation expression.
SMILE1104  Unexpected closing brace in template.
SMILE1105  Interpolation expression cannot be empty.
SMILE1106  Undefined variable.
SMILE1107  Duplicate variable declaration.
SMILE1108  Invalid string expression.
SMILE1109  Semicolons cannot separate SMILE statements.
SMILE1110  Unterminated interpolated string.
SMILE1111  Unexpected text after PRINT expression.
```

The exact numbers may be adjusted to fit the current diagnostic catalog, but meanings must remain stable.

Every diagnostic requires:

- Code.
- Severity.
- Message.
- One-based line.
- One-based column.
- Span.

Expected source errors must not throw ordinary exceptions.

---

# 16. Generator strategy

All seven generators must consume the bound language-neutral program.

Do not parse raw PRINT text inside a generator.

## 16.1 C#

Example source:

```basic
LET Name = "Sin"
PRINT Hello {Name}!
```

Generate idiomatic code similar to:

```csharp
string Name = "Sin";
Console.WriteLine($"Hello {Name}!");
```

Quoted concatenation may generate:

```csharp
Console.WriteLine("Hello " + Name + "!");
```

## 16.2 JavaScript

Generate:

```javascript
let Name = "Sin";
console.log(`Hello ${Name}!`);
```

Use valid target-specific escaping.

## 16.3 Java

Generate:

```java
String Name = "Sin";
System.out.println("Hello " + Name + "!");
```

Use `javac -encoding UTF-8`.

Do not emit invalid Java escapes such as `\a` or `\v`.

## 16.4 Swift

Generate:

```swift
let Name = "Sin"
print("Hello \(Name)!")
```

Swift remains transpile-only on Windows unless a supported local compiler is already deliberately configured.

## 16.5 Objective-C

Generate plain stdout behavior rather than `NSLog` metadata.

Example:

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
        puts("!");
    }

    return 0;
}
```

Objective-C remains transpile-only on Windows.

## 16.6 C

For string-only v0.2 expressions, avoid a general runtime concatenation library.

Generate simple native output segments:

```c
const char *Name = "Sin";

fputs("Hello ", stdout);
fputs(Name, stdout);
puts("!");
```

Blank `PRINT` may generate:

```c
putchar('\n');
```

Flatten concatenation/interpolation into ordered string segments when possible.

This is simple, native, and future-compatible.

## 16.7 Windows x64 MASM

Do not constant-substitute variables into text in a way that destroys runtime variable semantics.

For this string-only release:

- Emit each string literal as UTF-8 bytes and a length.
- Represent each variable as a pointer and length, or an equally simple runtime representation.
- Emit one `WriteFile` call per text or variable segment.
- Emit CR/LF once at the end of each `PRINT`.
- Preserve educational right-side comments.
- Acquire/reuse stdout in a clear way.
- Keep stack alignment and Win64 calling convention correct.

Conceptual data:

```asm
nameValue BYTE "Sin"
nameValueLength EQU $ - nameValue

NamePtr QWORD ?
NameLength DWORD ?
```

Conceptual initialization:

```asm
lea rax, nameValue
mov NamePtr, rax
mov NameLength, nameValueLength
```

Conceptual print:

```text
write "Hello "
write NamePtr/NameLength
write "!\r\n"
```

Do not introduce a large runtime library for this release.

---

# 17. Common lowering helper

To simplify C and MASM generation, add a target-neutral helper that can flatten a bound string expression into ordered printable segments when the expression contains only:

- String literals.
- String variables.
- Concatenation.
- Interpolated string parts.

Example:

```text
"Hello " + Name + "!"
```

and:

```text
Hello {Name}!
```

both lower to:

```text
LiteralSegment("Hello ")
VariableSegment(Name)
LiteralSegment("!")
```

This helper belongs in the engine/semantic layer, not in one target generator.

Do not add a general optimizer framework.

---

# 18. Desktop behavior

Update the WPF editor and generated panes to support the syntax.

Required behavior:

- Typing remains immediate.
- Live transpilation remains debounced and background-executed.
- Syntax diagnostics appear after the debounce.
- Stale results never replace current results.
- Build & Run never uses output from an older source revision.
- Generated panes update for the current visible languages.
- Successful live transpilation does not erase build/run logs.

Load a richer sample:

```basic
LET Name = "Sin"

PRINT
PRINT "Hello World!"
PRINT Hello World!
PRINT Hello {Name}!
PRINT $"Hello {Name}!"
PRINT "Hello " + Name + "!"
```

Do not make the sample so large that the first screen becomes cluttered.

---

# 19. CLI behavior

The CLI must accept `.smile` files containing `LET` and all official `PRINT` forms.

The same source must produce equivalent output across every installed runtime target.

Ordinary syntax errors must report clean diagnostics and nonzero exit status.

Do not print stack traces for user syntax mistakes.

---

# 20. Mandatory unit-test matrix

Create data-driven tests.

The following cases are mandatory.

## 20.1 Keyword casing

Each must parse and print `Hello`:

```basic
PRINT Hello
Print Hello
print Hello
pRiNt Hello
```

## 20.2 Quoted and raw equivalence

Each must print `Hello World!`:

```basic
PRINT "Hello World!"
PRINT Hello World!
```

## 20.3 Blank line

Each must print one newline:

```basic
PRINT
PRINT    
```

## 20.4 Variable interpolation

Given:

```basic
LET Name = "Sin"
```

each must print `Hello Sin!`:

```basic
PRINT Hello {Name}!
PRINT Hello {name}!
PRINT Hello {NAME}!
PRINT $"Hello {Name}!"
PRINT "Hello " + Name + "!"
```

## 20.5 Bare word is literal

Given:

```basic
LET Name = "Sin"
```

verify:

```basic
PRINT Name
```

prints:

```text
Name
```

and:

```basic
PRINT {Name}
```

prints:

```text
Sin
```

## 20.6 Ordinary quotes do not interpolate

Given:

```basic
LET Name = "Sin"
PRINT "Hello {Name}!"
```

must print:

```text
Hello {Name}!
```

## 20.7 Literal braces

Each must print `Use {Name}`:

```basic
PRINT Use {{Name}}
PRINT $"Use {{Name}}"
```

## 20.8 Internal whitespace

```basic
PRINT Hello     World
```

must preserve the five internal spaces.

Separator whitespace must not appear in output:

```basic
PRINT       Hello
```

must print `Hello`.

## 20.9 Semicolon

```basic
PRINT A; B; C
```

must print `A; B; C`.

These must be errors:

```basic
PRINT "A"; "B"
PRINT "A"; PRINT "B"
```

## 20.10 Missing separator whitespace

These must report the whitespace diagnostic:

```basic
PRINT"Hello"
PRINT$"Hello"
```

## 20.11 Duplicate PRINT keyword

These must report the second keyword's location:

```basic
PRINT Hello PRINT World
print Hello PrInT World
PRINT "Hello"; PRINT "World"
PRINT Use PRINT to display text.
```

These must remain valid:

```basic
PRINT "Use PRINT to display text."
PRINT Reprint this report.
PRINT PRINTABLE text.
PRINT Use "PRINT" as the command name.
PRINT Use {"PRINT"} as the command name.
```

## 20.12 Interpolation errors

Each must produce a stable syntax diagnostic without throwing:

```basic
PRINT Hello {
PRINT Hello {}
PRINT Hello {Name
PRINT Hello Name}
PRINT $"Hello {Name"
PRINT $"Hello }"
```

## 20.13 Undefined and duplicate identifiers

Undefined variable:

```basic
PRINT Hello {MissingName}!
```

Duplicate declaration:

```basic
LET Name = "Sin"
LET NAME = "Joy"
```

Both must be compiler errors.

## 20.14 Concatenation errors

These must fail cleanly:

```basic
PRINT "Hello" +
PRINT "Hello" + MissingName
PRINT "Hello" "World"
```

## 20.15 Statement-per-line rules

These must fail:

```basic
PRINT "A"; PRINT "B"
LET Name = "Sin" PRINT {Name}
```

No semicolon or keyword sequence may create a second statement on one line.

---

# 21. AST and semantic tests

Add tests proving:

- Raw `PRINT Hello World!` produces a string value expression.
- Raw `PRINT Hello {Name}!` produces text/expression/text parts.
- `$"Hello {Name}!"` produces the same bound interpolation meaning.
- `"Hello " + Name + "!"` produces concatenation.
- Identifier binding is case-insensitive.
- Declaration casing is preserved for display/generated code.
- Undefined identifiers produce semantic diagnostics.
- Duplicate identifiers are detected case-insensitively.
- The binder is target-independent.
- No generator reparses raw SMILE text.

---

# 22. Golden generator tests

For every target:

```text
C#
C
Assembly - Windows x64 MASM
JavaScript
Java
Objective-C
Swift
```

add exact-output tests for:

1. Blank `PRINT`.
2. Quoted literal.
3. Raw literal.
4. Raw interpolation.
5. `$"..."` interpolation.
6. Concatenation.
7. Case-insensitive variable reference.
8. Literal braces.
9. Multiple `PRINT` lines.
10. Deterministic output.
11. Exactly one trailing newline in generated files.
12. Correct target-specific escaping.

Golden tests should make unnecessary generated boilerplate visible in code review.

---

# 23. Target escaping tests

Test at least:

- Backslash.
- Tab.
- NUL.
- Bell.
- Vertical tab.
- Unicode text.
- A control character followed by a digit.
- Literal braces.
- Dollar signs.
- Semicolons.

Prove:

- Java never emits illegal `\a` or `\v`.
- JavaScript uses valid escapes.
- C escapes do not consume following digits accidentally.
- MASM emits correct UTF-8 byte sequences.
- Swift interpolation escaping remains valid.
- Objective-C source remains valid.

---

# 24. Local compile-and-run integration tests

Run locally on the VM.

For installed targets:

- C#
- C
- MASM x64
- JavaScript
- Java

compile/run this program:

```basic
LET Name = "Sin"

PRINT
PRINT "Hello World!"
PRINT Hello World!
PRINT Hello {Name}!
PRINT $"Hello {Name}!"
PRINT "Hello " + Name + "!"
PRINT Literal braces: {{Name}}
PRINT A; B; C
```

Expected normalized output:

```text

Hello World!
Hello World!
Hello Sin!
Hello Sin!
Hello Sin!
Literal braces: {Name}
A; B; C
```

All installed targets must produce identical normalized output.

Objective-C and Swift must pass generation/golden tests and report transpile-only on Windows.

Use the existing local conditional-integration approach. A missing optional toolchain is inconclusive, not a false success.

---

# 25. WPF manual tests

Run the desktop app locally.

Verify:

1. Rapid typing remains smooth.
2. Paste at least 100 mixed valid `PRINT` lines.
3. Raw and quoted forms update correctly.
4. An incomplete interpolation shows a diagnostic after debounce.
5. Fixing the interpolation clears the diagnostic.
6. Existing build output is not erased by successful live transpilation.
7. Switching target language displays current-source generated code.
8. Build & Run uses the latest source revision.
9. Cancel remains responsive.
10. Window movement, resizing, pane splitters, and scrolling remain responsive.
11. A second `PRINT` on one line highlights the correct location.
12. `PRINT Name` and `PRINT {Name}` visibly demonstrate different behavior.

Do not claim responsiveness without testing on the Windows VM.

---

# 26. Documentation

Update:

- `README.md`
- `AGENTS.md`
- `docs/Architecture.md`
- `docs/Roadmap.md`
- `docs/SMILE-Language-Specification-v0.1.md` or replace/link appropriately
- `docs/SMILE-PRINT-Statement-Specification-v1.0.md`
- Daily `Requirements` notes
- Progress screenshot

The README must clearly show:

```basic
LET Name = "Sin"

PRINT "Hello World!"
PRINT Hello World!
PRINT Hello {Name}!
PRINT $"Hello {Name}!"
PRINT "Hello " + Name + "!"
```

Explain:

```basic
PRINT Name
```

is literal text, while:

```basic
PRINT {Name}
```

evaluates the variable.

Document that quote omission is a `PRINT` convenience only.

---

# 27. Versioning

Update the desktop/app version to:

```text
0.2.0
0.2.0.0
0.2.0 Friendly PRINT
SMILE v0.2.0 - Friendly PRINT
```

Keep synchronized:

- Project version.
- Assembly version.
- File version.
- Informational version.
- WPF window title.
- About dialog.
- README.
- Screenshot.
- Requirements/progress notes.

The official PRINT specification remains version 1.0.

---

# 28. Validation commands

From `C:\SMILE`:

```bat
cmd /c cd /d C:\SMILE && dotnet restore SMILE.sln
cmd /c cd /d C:\SMILE && dotnet build SMILE.sln -c Debug
cmd /c cd /d C:\SMILE && dotnet test SMILE.sln -c Debug --no-build
cmd /c cd /d C:\SMILE && dotnet build SMILE.sln -c Release
cmd /c cd /d C:\SMILE && dotnet test SMILE.sln -c Release --no-build
```

CLI generation:

```bat
cmd /c cd /d C:\SMILE && dotnet run --project src\SMILE.Cli -- examples\FriendlyPrint.smile --target all
```

Run installed targets:

```bat
cmd /c cd /d C:\SMILE && dotnet run --project src\SMILE.Cli -- examples\FriendlyPrint.smile --target csharp --run
cmd /c cd /d C:\SMILE && dotnet run --project src\SMILE.Cli -- examples\FriendlyPrint.smile --target c --run
cmd /c cd /d C:\SMILE && dotnet run --project src\SMILE.Cli -- examples\FriendlyPrint.smile --target masm-x64 --run
cmd /c cd /d C:\SMILE && dotnet run --project src\SMILE.Cli -- examples\FriendlyPrint.smile --target javascript --run
cmd /c cd /d C:\SMILE && dotnet run --project src\SMILE.Cli -- examples\FriendlyPrint.smile --target java --run
```

Run WPF:

```bat
cmd /c cd /d C:\SMILE && dotnet run --project src\SMILE.Desktop
```

Check Git:

```bat
cmd /c cd /d C:\SMILE && git status --short
```

Do not commit build or generated-output artifacts.

---

# 29. Suggested commits

Suggested logical commits:

```text
Sin and Codex: Publish the official SMILE PRINT specification
Sin and Codex: Add string expressions and case-insensitive binding
Sin and Codex: Implement friendly PRINT templates and interpolation
Sin and Codex: Generate friendly PRINT across all target languages
Sin and Codex: Complete PRINT compliance tests and v0.2 documentation
```

A different split is acceptable when each commit remains coherent and builds.

Push after all local validation is green:

```bat
cmd /c cd /d C:\SMILE && git push -u origin feature/v0.2-friendly-print
```

No pull request is required unless Sin explicitly requests one.

---

# 30. Definition of done

## Specification

- [ ] Official specification exists in `docs`.
- [ ] README links to it.
- [ ] AGENTS permanently enforces it.

## Syntax

- [ ] Keywords are case-insensitive.
- [ ] Identifiers are case-insensitive.
- [ ] `PRINT` alone prints a blank line.
- [ ] Quoted PRINT works.
- [ ] Quote-free raw PRINT works.
- [ ] Raw `{...}` interpolation works.
- [ ] `$"..."` interpolation works.
- [ ] String concatenation works.
- [ ] `PRINT Name` is literal.
- [ ] `PRINT {Name}` evaluates.
- [ ] `{{` and `}}` work.
- [ ] Whitespace after PRINT is required for payloads.
- [ ] Semicolons never separate statements.
- [ ] Only one statement is allowed per line.
- [ ] A second standalone PRINT is an error.
- [ ] Ordinary quoted strings do not interpolate.

## Architecture

- [ ] PRINT stores an expression rather than raw literal text.
- [ ] Minimal binder resolves variables.
- [ ] Symbol lookup is ordinal case-insensitive.
- [ ] Raw and `$` interpolation lower to a shared semantic form.
- [ ] Generators consume bound language-neutral nodes.
- [ ] No backend reparses SMILE source.
- [ ] Live transpilation remains asynchronous and responsive.

## Targets

- [ ] C# generation passes.
- [ ] C generation passes.
- [ ] MASM generation passes.
- [ ] JavaScript generation passes.
- [ ] Java generation passes.
- [ ] Objective-C generation passes.
- [ ] Swift generation passes.
- [ ] Installed runtime targets produce identical output.
- [ ] Objective-C and Swift remain transpile-only on Windows.

## Testing

- [ ] Mandatory parser test matrix passes.
- [ ] AST/binder tests pass.
- [ ] Diagnostic span tests pass.
- [ ] Golden generator tests pass for all seven targets.
- [ ] Escaping tests pass.
- [ ] Local compile/run integration tests pass.
- [ ] WPF manual responsiveness tests pass.
- [ ] Debug build/tests pass.
- [ ] Release build/tests pass.

## Documentation

- [ ] Version is 0.2.0 everywhere.
- [ ] README matches actual behavior.
- [ ] Architecture docs are updated.
- [ ] Language docs are updated.
- [ ] Requirements notes are updated.
- [ ] Current screenshot is updated.
- [ ] No GitHub Actions or remote CI was added.

---

# 31. Final Codex report

Report:

1. Branch name.
2. Commit hashes and subjects.
3. Files added and changed.
4. Final grammar implemented.
5. AST and binder design.
6. Diagnostic codes added.
7. Number of unit tests added.
8. Full test totals.
9. Golden generator results for all seven targets.
10. Installed toolchains executed.
11. Cross-target normalized output.
12. WPF rapid-typing and 100-line-paste observations.
13. Confirmation that stale live-transpilation results are prevented.
14. Updated version and screenshot.
15. Documentation updates.
16. Confirmation that no remote CI was added.
17. Anything not completed and the exact reason.

Do not claim compliance with the official specification unless every mandatory test category has been implemented and passed locally.
