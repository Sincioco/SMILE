# Codex Implementation Instructions — Complete SMILE `LET` v1.0 Conformance

## Repository and milestone

- Repository: `Sincioco/SMILE`
- Work from the latest `main`.
- Re-read `AGENTS.md` before making changes.
- The latest reviewed public baseline when this brief was prepared was:
  - `c702e814e2eb45734831aae2ef154e603763255f`
  - `Sin and Codex: Keep rapid language switching responsive`
- Do not assume the SHA is still current; inspect `main` before editing.
- Do not discard, reset, overwrite, or commit unrelated user work.
- Do not commit or push unless Sin explicitly instructs you to do so in the Codex session.
- Follow KISS and KISS v2.
- Do not add a parser generator, runtime framework, compiler framework, or external dependency.

Create the next language milestone:

> **SMILE v0.3.0 — Complete `LET` v1.0 and the String Expression Core**

This is a language-conformance release. Do not add a third SMILE keyword.

---

# 1. Objective

Fully implement the already published official specification:

```text
docs/SMILE Language Specification/
    SMILE - LET Statement Official Specification v1.0.md
```

and preserve compatibility with:

```text
docs/SMILE Language Specification/
    SMILE - PRINT Statement Official Specification v1.0.md
```

At the end of this task:

- the official `LET` specification;
- the parser;
- the syntax tree;
- the binder;
- the bound semantic model;
- the reference evaluator;
- all seven target generators;
- generator tests;
- runtime integration tests;
- README;
- roadmap;
- architecture documentation;

must describe and implement the same language.

The current implementation supports only:

```smile
LET Name = "Sin"
```

even though the official `LET` v1.0 specification also defines these as valid:

```smile
LET FirstName = "Sin"
LET CopyOfName = FirstName
LET FullName = FirstName + " Cioco"
LET Greeting = $"Hello {FirstName}!"
```

Remove that specification/implementation discrepancy.

---

# 2. Required `LET` v1.0 behavior

## 2.1 Ordinary string literal

```smile
LET Name = "Sin"
```

## 2.2 Previously declared variable

```smile
LET FirstName = "Sin"
LET CopyOfFirstName = FirstName
```

## 2.3 String concatenation

```smile
LET FirstName = "Sin"
LET LastName = "Cioco"
LET FullName = FirstName + " " + LastName
```

The `+` operator remains left-associative.

## 2.4 Interpolated quoted string

```smile
LET FirstName = "Sin"
LET Greeting = $"Hello {FirstName}!"
```

## 2.5 Combined acceptance program

This exact program must compile and run:

```smile
LET FirstName = "Sin"
LET LastName = "Cioco"
LET CopyOfFirstName = FirstName
LET FullName = FirstName + " " + LastName
LET Greeting = $"Hello {FullName}!"

PRINT {CopyOfFirstName}
PRINT {FullName}
PRINT {Greeting}
```

Required output:

```text
Sin
Sin Cioco
Hello Sin Cioco!
```

It must produce the same normalized output in every currently runnable target:

- C#
- C
- Windows x64 MASM
- JavaScript
- Java

Objective-C and Swift must transpile correctly and remain transpile-only on Windows unless an independently supported toolchain is already present.

---

# 3. Preserve the existing compiler architecture

Keep this architecture:

```text
Source
  -> Parser
  -> Syntax Tree
  -> Binder
  -> Bound Program
  -> Target Generator
  -> Generated Files
```

Do not:

- reparse SMILE source inside a target generator;
- detect variable references with string matching;
- evaluate expressions independently in each backend;
- convert the compiler into source-text replacement logic;
- merge parsing and binding;
- make WPF classes part of `SMILE.Engine`.

The current syntax and bound expression categories are the correct foundation:

```text
StringLiteralExpression
NameExpression
ConcatenationExpression
InterpolatedStringExpression
```

Retain them.

---

# 4. Remove the artificial literal-only binder restriction

The current binder emits `SMILE1114` whenever a `LET` initializer is not a bound string literal.

Remove that restriction.

These must bind successfully:

```smile
LET Original = "Sin"
LET Copy = Original
LET Full = Original + " Cioco"
LET Message = $"Hello {Full}!"
```

Do not merely delete the diagnostic and leave lower-level generators receiving empty strings.

A complete fix must carry the evaluated string value needed by C, Objective-C, and MASM.

---

# 5. Correct declaration binding semantics

A `LET` declaration becomes visible only after its initializer binds successfully.

## 5.1 Declaration before use

Valid:

```smile
LET FirstName = "Sin"
LET FullName = FirstName + " Cioco"
```

Invalid:

```smile
LET FullName = FirstName + " Cioco"
LET FirstName = "Sin"
```

## 5.2 No self-reference

Invalid:

```smile
LET Name = Name + "!"
```

The name must be undefined while its own initializer is being bound.

## 5.3 Failed declarations do not create symbols

Given:

```smile
LET Broken = MissingName
PRINT {Broken}
```

`Broken` must not become a usable symbol after its initializer fails.

A later reference may therefore receive an undefined-variable diagnostic.

## 5.4 Duplicate declarations do not create a second bound variable

Invalid:

```smile
LET Name = "Sin"
LET NAME = "Joy"
```

The invalid duplicate statement must not add another symbol or another usable bound declaration.

## 5.5 Suggested binder flow

Use behavior equivalent to:

1. Parse and validate the declaration name.
2. Check whether the name is already declared.
3. Bind the initializer before adding the new symbol.
4. Reject self-reference naturally because the new symbol is absent.
5. Evaluate the bound initializer as a compile-time string constant.
6. Add the symbol and constant value only if the declaration is valid.
7. Create the bound `LET` statement.

Exact implementation details may differ.

---

# 6. Add a small string constant evaluator

All official `LET` v1.0 values are immutable strings derived from:

- string literals;
- previously declared string variables;
- string concatenation;
- interpolated strings.

Therefore every valid v1.0 `LET` initializer can be evaluated at compile time.

Add a small target-neutral evaluator inside `SMILE.Engine`.

It must evaluate:

```text
BoundStringLiteralExpression
BoundVariableExpression
BoundConcatenationExpression
BoundInterpolatedStringExpression
```

Maintain a binder-side mapping equivalent to:

```csharp
VariableSymbol -> string constant value
```

A possible focused helper is:

```csharp
internal static class BoundStringConstantEvaluator
{
    public static bool TryEvaluate(
        BoundExpression expression,
        IReadOnlyDictionary<VariableSymbol, string> values,
        out string value);
}
```

Exact names may differ.

## 6.1 Bound declaration value

Carry the evaluated value with the bound declaration or through another explicit semantic structure.

A simple KISS-compatible shape is:

```csharp
public sealed record BoundLetStatement(
    VariableSymbol Variable,
    BoundExpression Initializer,
    string ConstantValue)
    : BoundStatement;
```

Another clear design is acceptable.

Do not repeatedly re-evaluate all prior source statements inside each target generator.

## 6.2 Why this is required

High-level targets can preserve the original initializer expression.

For example, C# should generate:

```csharp
string FullName = FirstName + " " + LastName;
string Greeting = $"Hello {FullName}!";
```

C has no native string `+` operator. For current immutable compile-time string values, it may safely generate:

```c
const char *FullName = "Sin Cioco";
const char *Greeting = "Hello Sin Cioco!";
```

MASM and Objective-C may use the same evaluated value where their natural target representation requires it.

This avoids:

- heap allocation;
- generated `strcat`;
- temporary buffers;
- a premature SMILE runtime library;
- target-specific reimplementation of expression semantics.

---

# 7. Add a minimal reference evaluator for SMILE semantics

Create a small evaluator in `SMILE.Engine`, for example:

```text
Evaluation.cs
```

A possible public API is:

```csharp
public sealed record EvaluationResult(
    bool Success,
    string Output,
    IReadOnlyList<Diagnostic> Diagnostics);

public sealed class SmileEvaluator
{
    public EvaluationResult Evaluate(string source);
}
```

Exact naming may differ.

## 7.1 Evaluator responsibility

Execute the bound program directly:

- `LET` evaluates and stores a string value.
- `PRINT` evaluates its expression and appends one newline.
- blank `PRINT` appends one newline.
- expected source errors return diagnostics rather than exceptions.

## 7.2 Purpose

The evaluator is the canonical semantic oracle for tests.

The relationship should be:

```text
SMILE reference evaluator output
    ==
C# generated program output
    ==
C generated program output
    ==
MASM generated program output
    ==
JavaScript generated program output
    ==
Java generated program output
```

Do not replace target transpilation with the evaluator.

Do not add a new runtime project.

Do not integrate the evaluator into the desktop Build & Run workflow unless it is a tiny, clearly useful addition. Its primary purpose in this milestone is semantic verification.

---

# 8. Enforce official reserved SMILE keywords

The official specification says `LET` and `PRINT` cannot be variable names in any casing.

Reject:

```smile
LET LET = "Value"
LET let = "Value"
LET PRINT = "Value"
LET pRiNt = "Value"
```

Allow longer identifiers:

```smile
LET Letter = "A"
LET Reprint = "Again"
LET Printable = "Yes"
LET LetValue = "Value"
```

Add a central language-facts method such as:

```csharp
SyntaxFacts.IsReservedKeyword(string text)
```

or a more appropriately named equivalent.

Do not duplicate keyword lists across parser branches.

Add one new stable diagnostic code for a reserved keyword used as an identifier.

Document the code in README and the official specification's diagnostic section.

---

# 9. Make identifier implementation match the official v1.0 grammar

The official v1.0 identifier grammar is portable ASCII:

```text
identifier-start
    ::= ASCII letter
      | '_'

identifier-part
    ::= ASCII letter
      | ASCII digit
      | '_'
```

The current implementation uses:

```csharp
char.IsLetter
char.IsLetterOrDigit
```

which accepts many Unicode identifiers.

For v1.0, change the implementation to the published ASCII rule.

A clear implementation is:

```csharp
public static bool IsAsciiLetter(char value) =>
    value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

public static bool IsIdentifierStart(char value) =>
    IsAsciiLetter(value) || value == '_';

public static bool IsIdentifierPart(char value) =>
    IsIdentifierStart(value) || value is >= '0' and <= '9';
```

Exact formatting may differ.

Add tests for:

Valid:

```smile
LET Name = "Sin"
LET first_name = "Sin"
LET Name2 = "Sin"
LET _temporary = "Sin"
```

Invalid:

```smile
LET 2Name = "Sin"
LET First-Name = "Sin"
LET Näme = "Sin"
```

Do not silently broaden the official language.

A future Unicode-identifier specification may deliberately revise this rule.

---

# 10. Add deterministic target identifier mapping

A valid SMILE identifier may be:

- a reserved word in a destination language;
- a name that shadows a runtime API used by the generator;
- a name that collides after target mapping.

Examples:

```smile
LET class = "A"
LET namespace = "B"
LET Console = "C"
LET console = "D"
LET System = "E"
LET printf = "F"
LET print = "G"
```

The official specification requires every target to map valid SMILE names safely.

## 10.1 Create a symbol-based map

Add a target-specific identifier map built from `BoundProgram.Variables`.

Conceptually:

```csharp
TargetIdentifierMap identifiers =
    TargetIdentifierMap.Create(program, TargetLanguage.CSharp);
```

Then generators must use:

```csharp
identifiers.Get(variableSymbol)
```

for both declarations and references.

Do not perform text replacement on generated source.

Do not use the source reference casing. Use the canonical declaration symbol.

## 10.2 Mapping requirements

The mapping must be:

- deterministic;
- target-specific;
- collision-safe;
- consistent for every reference to the same symbol;
- readable;
- stable across repeated generation.

Preserve the declared SMILE spelling when it is already safe.

When mapping is required, use a readable convention such as:

```text
_smile_class
_smile_printf
_smile_print
```

If that mapped name is already used by another SMILE declaration, append a deterministic suffix:

```text
_smile_class_2
```

Exact prefix is flexible, but it must be documented and tested.

## 10.3 Reserve target language words

Include the relevant reserved words for:

- C#
- C
- JavaScript
- Java
- Objective-C
- Swift
- MASM where source identifiers are emitted

## 10.4 Reserve generator/runtime names that would break output

Also reserve names used by generated code in the same namespace or scope.

At minimum consider:

### C#

```text
Console
Program
Main
```

### C

```text
printf
main
stdout
```

### JavaScript

```text
console
```

### Java

```text
System
String
Program
main
args
```

### Objective-C

```text
printf
NSString
main
```

### Swift

```text
print
```

### MASM

Reserve generator-owned labels and assembler keywords if original SMILE names are ever emitted as symbols.

The target map should be the single authority.

## 10.5 Required mapping tests

Test at least:

```smile
LET class = "A"
LET Console = "B"
LET printf = "C"
LET print = "D"

PRINT {class}
PRINT {Console}
PRINT {printf}
PRINT {print}
```

Every generated target must compile or transpile into valid source, and runnable targets must print:

```text
A
B
C
D
```

Also test a collision:

```smile
LET class = "A"
LET _smile_class = "B"
```

The two variables must receive distinct target names.

---

# 11. Target-generation requirements

## 11.1 C#

Preserve source expression intent:

SMILE:

```smile
LET FirstName = "Sin"
LET Copy = FirstName
LET FullName = FirstName + " Cioco"
LET Greeting = $"Hello {FullName}!"
```

Preferred C#:

```csharp
string FirstName = "Sin";
string Copy = FirstName;
string FullName = FirstName + " Cioco";
string Greeting = $"Hello {FullName}!";
```

Do not rewrite valid expressions into folded literals in C#.

Do not change the approved `PRINT` behavior.

## 11.2 JavaScript

Preserve expression intent:

```javascript
let FirstName = "Sin";
let Copy = FirstName;
let FullName = FirstName + " Cioco";
let Greeting = `Hello ${FullName}!`;
```

Keep `let`, not `const`, because the official language reserves the possibility of a future separate assignment statement.

Do not change existing `PRINT` behavior.

## 11.3 Java

Generate:

```java
String FirstName = "Sin";
String Copy = FirstName;
String FullName = FirstName + " Cioco";
String Greeting = "Hello " + FullName + "!";
```

Java uses concatenation as the interpolation fallback.

## 11.4 Swift

Generate:

```swift
let FirstName = "Sin"
let Copy = FirstName
let FullName = FirstName + " Cioco"
let Greeting = "Hello \(FullName)!"
```

## 11.5 C

Use the evaluated string values:

```c
const char *FirstName = "Sin";
const char *Copy = "Sin";
const char *FullName = "Sin Cioco";
const char *Greeting = "Hello Sin Cioco!";
```

Continue using safe idiomatic `printf` for `PRINT`.

Do not add runtime buffer construction.

## 11.6 Objective-C

A simple accepted form is:

```objective-c
NSString *FirstName = @"Sin";
NSString *Copy = @"Sin";
NSString *FullName = @"Sin Cioco";
NSString *Greeting = @"Hello Sin Cioco!";
```

Continue using safe stdout `printf` for `PRINT`.

## 11.7 MASM x64

Use the evaluated UTF-8 bytes for each declared string variable.

The existing pointer-plus-length representation may remain:

```text
variableNValue
variableNLength
variableNPtr
```

Ensure:

- copied variables receive the correct bytes;
- concatenated values receive the complete bytes;
- interpolated values receive the complete bytes;
- printing any declared variable uses the correct pointer and length.

Do not introduce the C runtime merely for this task.

---

# 12. Preserve `PRINT` behavior

Do not regress any current `PRINT` rule.

The following distinction must remain:

```smile
LET Name = "Sin"

PRINT Name
PRINT {Name}
```

Output:

```text
Name
Sin
```

Bare `PRINT` text must never become a variable reference merely because a matching `LET` exists.

Preserve:

- blank `PRINT`;
- ordinary quoted strings;
- raw templates;
- interpolation;
- explicit concatenation;
- literal braces;
- required whitespace;
- second-`PRINT` diagnostics;
- one statement per line;
- target expression intent;
- idiomatic target code;
- C format-string safety.

---

# 13. Scope exclusions

Do not implement these in v0.3.0:

- reassignment;
- `SET`;
- numeric values;
- decimal values;
- booleans;
- arrays;
- comments;
- `INPUT`;
- `IF`;
- loops;
- functions;
- nested scopes;
- multi-line expressions;
- embedded quote escaping unless separately required by an existing official string specification;
- runtime dynamic string concatenation;
- a SMILE virtual machine;
- a SMILE runtime library.

This milestone completes the existing string-only `LET` contract.

---

# 14. Required parser and binder diagnostics

Preserve all existing stable diagnostic codes unless correcting a documented mistake.

Add coverage for:

- missing variable name;
- invalid identifier;
- reserved SMILE keyword as identifier;
- missing `=`;
- missing initializer;
- invalid initializer expression;
- unterminated ordinary string;
- unterminated interpolated string;
- malformed interpolation;
- unexpected closing brace;
- undefined variable;
- duplicate declaration;
- self-reference;
- forward reference;
- unexpected text after initializer;
- semicolon statement separation;
- two statements on one line;
- unsupported future value syntax.

Expected language errors must return diagnostics and must not crash:

- engine;
- CLI;
- desktop application.

---

# 15. Specification conformance test suite

Create a focused test file such as:

```text
tests/SMILE.Tests/LetSpecificationConformanceTests.cs
```

Convert the official specification into executable tests.

## 15.1 Valid programs

Test:

```smile
LET Name = "Sin"
```

```smile
LET FirstName = "Sin"
LET Copy = FirstName
```

```smile
LET FirstName = "Sin"
LET LastName = "Cioco"
LET FullName = FirstName + " " + LastName
```

```smile
LET Name = "Sin"
LET Greeting = $"Hello {Name}!"
```

```smile
LET Placeholder = $"Use {{Name}} as a placeholder."
```

```smile
LET Steps = "First; second; third"
PRINT {Steps}
```

## 15.2 Required invalid programs

Test every program listed under the official specification's **Required errors** section, including:

```smile
LETName = "Sin"
LET = "Sin"
LET 2Name = "Sin"
LET First Name = "Sin"
LET LET = "Sin"
LET PRINT = "Sin"
LET Name
LET Name =
LET Name = Hello World!
LET Name = "Sin" +
LET Name = MissingName
LET Name = $"Hello {
LET Name = $"Hello {}
LET Name = $"Hello {MissingName}!"
LET Name = "Sin"; PRINT {Name}
LET Name = "Sin" LET Other = "Joy"
```

And duplicates:

```smile
LET Name = "Sin"
LET Name = "Joy"
```

```smile
LET Name = "Sin"
LET NAME = "Joy"
```

## 15.3 Declaration timing

Test:

```smile
LET Greeting = FirstName + "!"
LET FirstName = "Sin"
```

and:

```smile
LET Name = Name + "!"
```

## 15.4 Failed symbol leakage

Test that a failed `LET` does not become available to later statements.

## 15.5 Bound tree shape

Verify:

```smile
LET Copy = Name
```

binds to a `BoundVariableExpression`.

Verify:

```smile
LET Full = Name + "!"
```

binds to a `BoundConcatenationExpression`.

Verify:

```smile
LET Greeting = $"Hello {Name}!"
```

binds to a `BoundInterpolatedStringExpression`.

Do not flatten these expressions before high-level target generation.

## 15.6 Constant values

Verify evaluated declaration values:

```text
Name     -> Sin
Copy     -> Sin
FullName -> Sin Cioco
Greeting -> Hello Sin Cioco!
```

---

# 16. Reference evaluator tests

Test the evaluator independently.

## 16.1 Combined program

```smile
LET FirstName = "Sin"
LET LastName = "Cioco"
LET FullName = FirstName + " " + LastName
LET Greeting = $"Hello {FullName}!"

PRINT {FullName}
PRINT {Greeting}
```

Expected evaluator output:

```text
Sin Cioco
Hello Sin Cioco!
```

## 16.2 Literal versus evaluated PRINT

```smile
LET Name = "Sin"

PRINT Name
PRINT {Name}
```

Expected:

```text
Name
Sin
```

## 16.3 Blank and empty

```smile
PRINT
PRINT ""
```

Expected: two newline characters.

## 16.4 Errors

Invalid source must return diagnostics and no successful evaluation.

---

# 17. Exact generator tests

Add exact or strong structural tests for each target.

## 17.1 C#

Assert variable reference, concatenation, and interpolation are preserved.

## 17.2 JavaScript

Assert native template literals and explicit concatenation are preserved.

## 17.3 Java

Assert interpolation falls back to concatenation.

## 17.4 Swift

Assert native interpolation.

## 17.5 C and Objective-C

Assert evaluated declaration literals contain the correct final strings.

## 17.6 MASM

Assert expected complete UTF-8 byte data and lengths are emitted.

## 17.7 Identifier mapping

Assert mapped declarations and references use exactly the same target name.

## 17.8 Determinism

Generate the same program twice for every target and compare all generated files exactly.

---

# 18. Cross-target runtime conformance

For each installed runnable toolchain:

1. evaluate the SMILE program with `SmileEvaluator`;
2. generate the target program;
3. build and run it;
4. normalize line endings;
5. compare target stdout to evaluator output.

Use at least these programs:

## Program A — copy

```smile
LET Name = "Sin"
LET Copy = Name

PRINT {Copy}
```

## Program B — concatenation

```smile
LET FirstName = "Sin"
LET LastName = "Cioco"
LET FullName = FirstName + " " + LastName

PRINT {FullName}
```

## Program C — interpolation

```smile
LET Name = "Sin"
LET Greeting = $"Hello {Name}!"

PRINT {Greeting}
```

## Program D — chained expressions

```smile
LET A = "A"
LET B = A + "B"
LET C = $"{B}C"
LET D = C + A

PRINT {D}
```

Expected:

```text
ABCA
```

## Program E — target-reserved identifiers

Use valid SMILE identifiers that require target mapping and verify output.

---

# 19. Update existing tests that intentionally expect the old limitation

Remove or rewrite tests that expect:

```text
SMILE1114 LET currently requires a string literal initializer
```

Do not retain a test that treats an officially valid initializer as invalid.

If `SMILE1114` is no longer used, remove it from:

- README diagnostic table;
- tests;
- documentation;
- source.

Do not silently reuse the diagnostic number for a different meaning unless that change is clearly documented.

---

# 20. Documentation updates

## 20.1 README

Update:

- version to v0.3.0;
- implemented grammar;
- `LET` examples;
- current limitations;
- generated examples;
- diagnostics;
- roadmap;
- architecture summary.

Remove statements saying:

```text
LET is limited to string literal initializers.
```

Replace with an accurate description:

> `LET` v1.0 accepts string literals, previously declared string variables, string concatenation, and interpolated quoted strings.

## 20.2 Roadmap

Move:

```text
Non-literal LET initializers
```

from future ideas to implemented v0.3.0.

Do not move numeric or boolean work.

## 20.3 Architecture

Document:

- string constant evaluation;
- reference evaluator;
- target identifier mapping;
- high-level expression preservation;
- low-level constant lowering.

## 20.4 Official LET specification

The specification is already intended to be authoritative.

Only change it where needed to:

- add exact diagnostic codes;
- clarify target identifier mapping;
- clarify compile-time constant lowering as a valid implementation strategy;
- correct any genuine ambiguity found during implementation.

Do not weaken the specification to match the old literal-only implementation.

## 20.5 Official PRINT specification

Update only cross-references or shared expression wording if required.

Do not change `PRINT` semantics.

## 20.6 AGENTS.md

Add permanent rules:

> Published official language specifications and compiler behavior must remain synchronized.

> Every normative valid and invalid example in an official language specification should be represented in the conformance test suite.

> Target generators must use a symbol-based target identifier map and must not emit raw SMILE identifiers when they conflict with destination-language syntax or generator-owned runtime names.

---

# 21. Desktop and CLI expectations

No major UI redesign is required.

Preserve:

- AvalonEdit;
- syntax highlighting;
- rapid target switching;
- debounced latest-source-wins generation;
- cached generated programs;
- Build & Run crash containment;
- bounded output;
- cancellation;
- timeouts;
- diagnostic logging.

Update the default sample to demonstrate completed `LET` only if it remains readable.

A suitable sample is:

```smile
LET FirstName = "Sin"
LET LastName = "Cioco"
LET FullName = FirstName + " " + LastName
LET Greeting = $"Hello {FullName}!"

PRINT {Greeting}
```

The CLI must transpile and run the new valid programs.

---

# 22. Acceptance criteria

This task is complete only when all of the following are true:

1. `LET Name = "Sin"` still works.
2. `LET Copy = ExistingVariable` works.
3. `LET Full = Left + " " + Right` works.
4. `LET Greeting = $"Hello {Name}!"` works.
5. declarations are visible only after successful initialization.
6. forward references fail.
7. self-references fail.
8. failed declarations do not leak symbols.
9. duplicate declarations fail case-insensitively.
10. `LET` and `PRINT` cannot be variable names.
11. longer names containing keyword text remain valid.
12. v1.0 identifiers follow the published ASCII grammar.
13. the bound tree preserves initializer expression shape.
14. every valid initializer has a target-neutral evaluated string value.
15. C uses correct evaluated declaration strings.
16. Objective-C uses correct evaluated declaration strings.
17. MASM uses correct evaluated declaration bytes.
18. C#, JavaScript, Java, and Swift preserve the closest natural expression syntax.
19. target reserved words are mapped safely.
20. generator/runtime API names are not shadowed.
21. mapped identifiers are deterministic and collision-safe.
22. every reference uses the same mapped name as its declaration.
23. `PRINT Name` remains literal.
24. `PRINT {Name}` remains evaluated.
25. the reference evaluator produces canonical output.
26. runnable target output matches reference evaluator output.
27. every normative `LET` example has a conformance test.
28. every required `LET` error has a conformance test.
29. obsolete literal-only tests and documentation are removed.
30. no new keyword or value type is introduced.
31. existing `PRINT` tests remain green.
32. desktop responsiveness and stability tests remain green.
33. Debug and Release builds have zero warnings.
34. Debug and Release tests pass.
35. CLI generation succeeds for all seven targets.
36. installed runnable targets compile and run the acceptance programs.
37. generated output is deterministic.
38. documentation matches the implementation.
39. no build artifacts or unrelated files are committed.

---

# 23. Validation commands

Run from the repository root:

```bat
cmd /c git status --short --branch
```

```bat
cmd /c dotnet restore SMILE.sln
```

```bat
cmd /c dotnet build SMILE.sln -c Debug
```

```bat
cmd /c dotnet test SMILE.sln -c Debug --no-build
```

```bat
cmd /c dotnet build SMILE.sln -c Release
```

```bat
cmd /c dotnet test SMILE.sln -c Release --no-build
```

Run CLI generation for all targets using the combined acceptance program.

Run Build & Run for every installed local target:

- C#
- C
- MASM x64
- JavaScript
- Java

Compare normalized output to `SmileEvaluator`.

Before any commit:

```bat
cmd /c git diff --check
```

Review:

```bat
cmd /c git diff --stat
```

and:

```bat
cmd /c git status --short
```

Do not include generated workspaces, `bin`, `obj`, temporary logs, screenshots not required by documentation, or unrelated files.

---

# 24. Suggested implementation sequence

Use this order:

1. Create the `LET` conformance tests that expose current failures.
2. Enforce reserved keywords and ASCII identifiers.
3. Remove the literal-only binder restriction.
4. Correct declaration timing and failed-symbol behavior.
5. Add target-neutral string constant evaluation.
6. Carry evaluated values in the bound semantic model.
7. Add the reference evaluator.
8. Update C, Objective-C, and MASM declarations.
9. Add target identifier mapping.
10. Refactor high-level generators to use mapped identifiers.
11. Add exact generator tests.
12. Add evaluator-versus-toolchain integration tests.
13. Update README, roadmap, architecture, specifications, and AGENTS.
14. Run complete Debug/Release and Windows toolchain validation.
15. Perform a final specification-versus-test audit.

Do not begin numeric types or another keyword until this sequence is complete.

---

# 25. Completion report

At the end, report:

- files changed;
- old `LET` limitation removed;
- valid initializer forms implemented;
- declaration binding behavior;
- constant-evaluation design;
- evaluator design;
- target identifier mapping design;
- target generator changes;
- diagnostic changes;
- tests added and updated;
- exact Debug test totals;
- exact Release test totals;
- installed target build/run results;
- documentation changes;
- any official specification example not yet covered;
- whether any issue remains before declaring `LET` v1.0 complete.

Do not state that `LET` v1.0 is complete if any normative valid example or required error still disagrees with the implementation.

---

# 26. Commit guidance

Do not commit or push unless Sin explicitly authorizes it.

When authorized, use a focused commit subject such as:

```text
Sin and Codex: Complete LET v1.0 string expression semantics
```

The commit body should mention:

- variable, concatenation, and interpolation initializers;
- declaration-before-use and failed-symbol behavior;
- compile-time string constant evaluation;
- reference evaluator;
- target identifier mapping;
- low-level target constant lowering;
- specification conformance suite;
- cross-target runtime equivalence;
- exact validation results.
