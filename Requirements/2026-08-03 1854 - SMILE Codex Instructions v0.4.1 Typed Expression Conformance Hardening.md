# Codex Implementation Instructions — SMILE v0.4.1 Typed Expression Conformance Hardening

## Repository and workflow

- Repository: `Sincioco/SMILE`
- Work **directly on `main` only**.
- Sin is the only developer.
- **Do not create, suggest, or use a feature branch.**
- Do not open a pull request.
- Re-read `AGENTS.md` before changing code.
- Inspect the current `main` branch and working tree before editing.
- The reviewed v0.4.0 baseline when this brief was prepared was:
  - Commit: `9e907b128a24059309bb066e6c13aa5f6d83e702`
  - Subject: `Sin and Codex: Add the typed expression core`
- Do not assume that SHA is still current; always use the newest `main`.
- Do not discard, reset, overwrite, or commit unrelated user work.
- Do not commit or push unless Sin explicitly authorizes it in the Codex session.
- Follow KISS and KISS v2, “The Sin Way.”
- Do not add a parser generator, property-testing framework, compiler framework, runtime framework, or unnecessary dependency.

---

# 1. Milestone

Create the focused hardening release:

> **SMILE v0.4.1 — Typed Expression Conformance Hardening**

SMILE v0.4.0 introduced:

- a real lexer;
- typed expressions;
- String, Integer, and Boolean;
- precedence-aware parsing;
- checked 64-bit integer semantics;
- string escapes;
- typed constant evaluation;
- typed reference evaluation;
- typed target generation.

This task stabilizes that foundation before SMILE adds another statement keyword.

Do **not** add:

- `SET`;
- `INPUT`;
- `IF`;
- loops;
- functions;
- reassignment;
- another type;
- another target language.

---

# 2. Objectives

Complete all of the following:

1. Define and implement official short-circuit semantics for `AND` and `OR`.
2. Remove obsolete string-concatenation syntax and bound nodes.
3. Add missing signed 64-bit arithmetic edge tests.
4. Strengthen string equality and string-escape conformance tests.
5. Preserve native Integer and Boolean expression intent in C and Objective-C.
6. Run typed-expression Build & Run validation through every installed target.
7. Add deterministic evaluator-versus-target generated-expression testing.
8. Preserve all stable `LET`, `PRINT`, string, lexer, and typed-expression behavior.
9. Keep all work directly on `main`.

---

# 3. Preserve all current language behavior

Do not regress this program:

```smile
LET Name = "Sin"
LET Age = 49
LET Adult = Age >= 18
LET Message = $"{Name} is {Age}. Adult={Adult}"

PRINT {Message}
PRINT 2 + 3 = {2 + 3}
```

Required output:

```text
Sin is 49. Adult=TRUE
2 + 3 = 5
```

Preserve:

- case-insensitive keywords;
- case-insensitive identifiers;
- portable ASCII identifiers;
- reserved SMILE keywords;
- declaration-before-use;
- failed declarations not leaking symbols;
- duplicate declaration diagnostics;
- `PRINT Name` as literal text;
- `PRINT {Name}` as evaluated output;
- raw `PRINT` templates;
- quoted `PRINT` expressions;
- interpolated strings;
- string escapes;
- checked signed 64-bit arithmetic;
- strict typing;
- no implicit String/Integer/Boolean conversion;
- canonical Integer display;
- canonical `TRUE` / `FALSE` display;
- target identifier mapping;
- MASM empty-string logical length zero;
- all eight targets;
- responsive desktop live transpilation;
- Build & Run crash containment.

---

# 4. Official short-circuit semantics

## 4.1 Required language rule

Publish and implement:

> `AND` and `OR` evaluate operands from left to right and use short-circuit evaluation.

Rules:

```text
FALSE AND right
```

does not evaluate `right`.

```text
TRUE OR right
```

does not evaluate `right`.

```text
TRUE AND right
```

evaluates `right`.

```text
FALSE OR right
```

evaluates `right`.

## 4.2 Binding versus evaluation

The binder must still parse, resolve, and type-check both operands.

Therefore these remain invalid even when the right operand is unreachable at evaluation time:

```smile
LET Result = FALSE AND MissingName
```

Undefined variable.

```smile
LET Result = TRUE OR 42
```

Invalid Boolean operand type.

Short-circuiting affects evaluation, not syntax or type checking.

## 4.3 Evaluation-time errors in an unreachable operand

Evaluation-time errors in an unreachable operand must not be produced.

These must be valid:

```smile
LET Result = FALSE AND (1 / 0 = 0)
PRINT {Result}
```

Output:

```text
FALSE
```

```smile
LET Result = TRUE OR (1 / 0 = 0)
PRINT {Result}
```

Output:

```text
TRUE
```

These must still fail because the right operand is evaluated:

```smile
LET Result = TRUE AND (1 / 0 = 0)
```

Diagnostic:

```text
SMILE1207
```

```smile
LET Result = FALSE OR (9223372036854775807 + 1 = 0)
```

Diagnostic:

```text
SMILE1206
```

## 4.4 Constant evaluator implementation

`BoundConstantEvaluator` must not evaluate both operands before deciding the result for logical operators.

Use behavior equivalent to:

```csharp
Evaluate left.

If operator is AND and left is FALSE:
    return FALSE without evaluating right.

If operator is OR and left is TRUE:
    return TRUE without evaluating right.

Otherwise:
    evaluate right and finish the operation.
```

Do not duplicate this logic in each target generator.

## 4.5 Reference evaluator

`SmileEvaluator` must inherit the same behavior through the shared bound evaluator or an equivalent single semantic implementation.

## 4.6 Target generators

High-level target operators remain:

```text
C#          &&  ||
C           &&  ||
JavaScript  &&  ||
Java        &&  ||
Objective-C &&  ||
Swift       &&  ||
```

For COBOL and MASM, constant lowering is still allowed in v0.4.1 because all current `LET` values are compile-time evaluable.

Generated target behavior must match `SmileEvaluator`.

## 4.7 Specification update

Update:

```text
SMILE - Core Types and Expressions Official Specification v1.0.md
```

with a normative short-circuit section covering:

- left-to-right evaluation;
- `AND`;
- `OR`;
- binding versus evaluation;
- unreachable evaluation errors;
- future compatibility with runtime expressions and functions.

---

# 5. Remove obsolete concatenation nodes

## 5.1 Problem

The v0.4.0 parser now represents `+` through the general typed nodes:

```text
BinaryExpressionSyntax
BoundBinaryExpression
```

The older nodes remain only for compatibility:

```text
ConcatenationExpressionSyntax
BoundConcatenationExpression
```

Keeping both creates two representations for one semantic operation.

## 5.2 Required result

Remove:

```text
ConcatenationExpressionSyntax
BoundConcatenationExpression
```

and all special handling for them.

String concatenation must be represented only as:

```text
BinaryExpressionSyntax
    operator: PlusToken
```

and after binding:

```text
BoundBinaryExpression
    operator: StringConcatenation
```

## 5.3 Update all consumers

Remove obsolete cases from:

- binder;
- constant evaluator;
- output flattening;
- target generators;
- tests;
- architecture documentation;
- old comments;
- specification guidance.

## 5.4 Preserve expression intent

This SMILE:

```smile
LET FullName = FirstName + " " + LastName
```

must still generate natural concatenation in high-level targets.

The internal cleanup must not flatten the high-level expression into one literal unless that target intentionally uses low-level constant lowering.

## 5.5 API compatibility

SMILE is still pre-1.0 and has no supported external compiler API contract.

Prefer one clean representation over retaining dead public types for hypothetical compatibility.

Document this cleanup in architecture notes.

---

# 6. Signed 64-bit edge-case hardening

Add explicit tests for every important boundary behavior.

## 6.1 Valid boundaries

```smile
LET Min = -9223372036854775808
LET Max = 9223372036854775807

PRINT {Min}
PRINT {Max}
```

Expected:

```text
-9223372036854775808
9223372036854775807
```

## 6.2 Division overflow

Reject:

```smile
LET Invalid = -9223372036854775808 / -1
```

Diagnostic:

```text
SMILE1206
```

This is signed 64-bit overflow.

## 6.3 Unary negation overflow

Reject:

```smile
LET Invalid = -(-9223372036854775808)
```

Diagnostic:

```text
SMILE1206
```

## 6.4 Other arithmetic overflow

Keep explicit regression tests for:

```smile
LET Invalid = 9223372036854775807 + 1
LET Invalid = -9223372036854775808 - 1
LET Invalid = 3037000500 * 3037000500
```

## 6.5 Division semantics

Verify truncation toward zero:

```smile
LET A = 7 / 2
LET B = -7 / 2
LET C = 7 / -2
LET D = -7 / -2

PRINT {A}
PRINT {B}
PRINT {C}
PRINT {D}
```

Expected:

```text
3
-3
-3
3
```

## 6.6 Parentheses and associativity

Verify:

```smile
LET A = 10 - 3 - 1
LET B = 10 - (3 - 1)
LET C = 100 / 10 / 2
LET D = 100 / (10 / 2)

PRINT {A}
PRINT {B}
PRINT {C}
PRINT {D}
```

Expected:

```text
6
8
5
20
```

---

# 7. String equality conformance

String equality is case-sensitive because string data is case-sensitive.

Verify:

```smile
LET A = "Sin" = "Sin"
LET B = "Sin" = "sin"
LET C = "Sin" <> "sin"
LET D = "" = ""
LET E = "A\nB" = "A\nB"

PRINT {A}
PRINT {B}
PRINT {C}
PRINT {D}
PRINT {E}
```

Expected:

```text
TRUE
FALSE
TRUE
TRUE
TRUE
```

Also test equality through variables and interpolation-produced strings:

```smile
LET Name = "Sin"
LET Copy = Name
LET Greeting = $"Hello {Name}"
LET SameName = Name = Copy
LET SameGreeting = Greeting = "Hello Sin"
```

Required:

```text
SameName = TRUE
SameGreeting = TRUE
```

## 7.1 Target-specific requirements

- C# may use `==` for strings.
- JavaScript may use `===`.
- Java must use `.equals`.
- Swift may use `==`.
- C must use `strcmp(...) == 0` or semantically equivalent code when expression intent is preserved.
- Objective-C Windows console profile currently uses C strings and should use `strcmp`.
- COBOL and MASM may lower compile-time results.

Include `<string.h>` only when generated C or Objective-C actually needs it.

---

# 8. String-escape conformance

Add exact source-to-value and output tests for every official escape:

| Escape | Required value |
|---|---|
| `\\` | backslash |
| `\"` | double quotation mark |
| `\n` | line feed |
| `\r` | carriage return |
| `\t` | horizontal tab |
| `\0` | NUL |
| `\b` | backspace |
| `\f` | form feed |

## 8.1 Required program

```smile
LET Backslash = "\\"
LET Quote = "\""
LET Newline = "A\nB"
LET CarriageReturn = "A\rB"
LET Tab = "A\tB"
LET Nul = "A\0B"
LET Backspace = "A\bB"
LET FormFeed = "A\fB"
```

Do not rely only on visually rendered console output.

Validate evaluator values and generated target source structurally.

For runtime tests:

- compare exact captured strings or bytes where practical;
- do not trim NUL, backspace, form-feed, tabs, or carriage returns;
- normalize only platform line endings in tests that specifically compare `PRINT` line endings;
- keep dedicated byte-aware tests for embedded control characters.

## 8.2 Invalid escapes

Retain or add tests for:

```smile
LET Invalid = "\q"
LET Invalid = "\x"
```

and a string ending with a lone backslash.

Use:

```text
SMILE1208
SMILE1209
```

as already specified.

## 8.3 Raw PRINT templates

Confirm raw templates do not process backslash escapes:

```smile
PRINT C:\SMILE\n
```

The backslash and `n` must remain literal text.

---

# 9. Preserve Integer and Boolean expression intent in C and Objective-C

## 9.1 Current issue

C and Objective-C may currently lower typed expressions to constants even when the target can naturally express them.

Example SMILE:

```smile
LET Age = 49
LET Adult = Age >= 18
LET WorkingAge = Adult AND NOT FALSE
```

Avoid unnecessarily generating:

```c
long long Age = 49LL;
bool Adult = true;
bool WorkingAge = true;
```

## 9.2 Preferred C and Objective-C output

Generate natural typed expressions where practical:

```c
long long Age = 49LL;
bool Adult = Age >= 18LL;
bool WorkingAge = Adult && !false;
```

The Objective-C Windows console profile may use the same C-compatible representation in `.m` source.

## 9.3 Scope of preservation

Preserve native expression intent for:

- Integer literals;
- Boolean literals;
- variable references;
- unary Integer operators;
- unary `NOT`;
- Integer arithmetic;
- Integer comparisons;
- Integer/Boolean equality;
- Boolean `AND` and `OR`;
- parentheses as required.

## 9.4 String expressions

C and Objective-C may continue lowering String-producing `LET` expressions to evaluated constants in v0.4.1.

Do not add runtime string allocation, concatenation buffers, or a SMILE runtime library.

## 9.5 C and Objective-C PRINT

Improve typed `PRINT` when practical.

Examples:

SMILE:

```smile
PRINT {Age}
PRINT {Adult}
PRINT Result: {Age + 1}
```

Preferred C shape:

```c
printf("%lld\n", Age);
printf("%s\n", Adult ? "TRUE" : "FALSE");
printf("Result: %lld\n", Age + 1LL);
```

Requirements:

- compiler-owned format strings;
- user-authored `%` remains escaped as `%%`;
- `%s` for String and canonical Boolean text;
- `%lld` for signed 64-bit Integer;
- one natural `printf` call per SMILE `PRINT` where practical;
- no format-string injection;
- no hidden locale dependence.

Do not regress current C/Objective-C safe `printf` behavior.

---

# 10. Precedence-aware target generation tests

Add exact or structural tests for:

```smile
LET A = 2 + 3 * 4
LET B = (2 + 3) * 4
LET C = 10 - (3 - 1)
LET D = NOT TRUE OR TRUE
LET E = TRUE OR FALSE AND FALSE
```

Verify every high-level target preserves meaning.

Required expectations include:

```text
A = 14
B = 20
C = 8
D = TRUE
E = TRUE
```

Specifically catch accidental output such as:

```text
10 - 3 - 1
```

for:

```smile
10 - (3 - 1)
```

Also test nested right operands for:

- subtraction;
- division;
- equality;
- relational operators;
- logical operators.

---

# 11. Deterministic generated-expression conformance corpus

Do not add a property-testing library.

Create a deterministic test helper using a fixed seed, for example:

```csharp
const int Seed = 20260401;
```

## 11.1 Corpus strategy

Generate a single or small number of larger SMILE programs containing many valid expressions.

Each expression should be assigned and printed:

```smile
LET Case001 = ...
PRINT {Case001}
```

This avoids compiling one target program per random expression.

## 11.2 Expression categories

Generate safe combinations of:

- small Integer literals;
- unary plus/minus;
- addition;
- subtraction;
- multiplication within safe bounds;
- nonzero division;
- parentheses;
- Integer comparisons;
- Integer equality;
- Boolean literals;
- Boolean equality;
- `NOT`;
- `AND`;
- `OR`;
- String literals;
- String concatenation;
- String equality;
- interpolation with String, Integer, and Boolean.

## 11.3 Safety

The ordinary generated corpus should avoid:

- accidental overflow;
- accidental division by zero;
- invalid operand combinations;
- excessively deep expression trees.

Keep deliberate error cases in separate explicit diagnostic tests.

## 11.4 Determinism

Given the same seed:

- generated SMILE source must be byte-for-byte identical;
- evaluator output must be identical;
- generated target source must be deterministic.

## 11.5 Runtime comparison

For every installed runnable target:

1. evaluate the generated corpus with `SmileEvaluator`;
2. transpile it;
3. build and run it;
4. normalize line endings only;
5. compare stdout exactly.

Targets:

```text
C#
C
MASM x64
JavaScript
Java
COBOL
Objective-C
Swift
```

A missing optional toolchain may be reported as inconclusive by automated tests, but the completion report must list exactly which targets were actually executed.

---

# 12. Full typed acceptance validation for all targets

The v0.4.0 completion record explicitly mentioned successful C# CLI Build & Run.

For v0.4.1, deliberately run the typed acceptance program through every installed target.

Use:

```text
examples/TypedExpressionCore.smile
```

and the new deterministic conformance corpus.

For each target, report:

- toolchain detected or unavailable;
- transpilation result;
- build result;
- execution result;
- exit code;
- stdout comparison with `SmileEvaluator`.

Do not claim all-target runtime conformance when a target was only transpiled.

---

# 13. Automated test organization

Recommended new or updated files:

```text
tests/SMILE.Tests/TypedExpressionHardeningTests.cs
tests/SMILE.Tests/TypedExpressionGeneratedCorpusTests.cs
```

Exact organization may differ.

Include focused tests for:

- short-circuit behavior;
- unreachable evaluation errors;
- reachable evaluation errors;
- binding errors in unreachable operands;
- removal of legacy concatenation nodes;
- signed 64-bit edge cases;
- string equality;
- string escapes;
- target precedence;
- C/Objective-C expression intent;
- typed C/Objective-C `printf`;
- deterministic corpus generation;
- evaluator-versus-target output.

All existing tests must remain green.

---

# 14. Specifications and documentation

Update:

- `README.md`;
- `AGENTS.md`;
- `docs/Architecture.md`;
- `docs/Roadmap.md`;
- `SMILE - Core Types and Expressions Official Specification v1.0.md`;
- `SMILE - String Literals Official Specification v1.0.md` if clarification is needed;
- `SMILE - LET Statement Official Specification v1.0.md` only for shared-expression cross-references;
- `SMILE - PRINT Statement Official Specification v1.0.md` only for shared-expression and display cross-references;
- target code generation standard;
- Day 3 or next requirement-history file used by the project.

## 14.1 AGENTS.md permanent rules

Preserve:

> All SMILE development is performed directly on `main`. Sin is the only developer. Do not create or recommend feature branches unless Sin explicitly changes this rule.

Add:

> SMILE `AND` and `OR` use left-to-right short-circuit evaluation. Binding and type checking still examine both operands, but evaluation-time failures in an unreachable operand are not produced.

Add:

> Each expression concept must have one canonical syntax and bound representation. Remove obsolete parallel representations rather than maintaining duplicate compiler paths.

## 14.2 Architecture

Document:

- short-circuit constant evaluation;
- one canonical binary-expression representation;
- C/Objective-C native typed-expression preservation;
- deterministic generated-expression corpus testing.

## 14.3 Roadmap

Record v0.4.1 as typed-expression hardening.

Do not mark assignment, input, conditions, or loops as implemented.

---

# 15. Scope exclusions

Do not implement:

- `SET`;
- reassignment;
- mutable variables;
- `INPUT`;
- `IF`;
- loops;
- functions;
- comments;
- Decimal or floating-point types;
- arrays;
- nested scopes;
- user-defined types;
- runtime string concatenation;
- a VM;
- a SMILE runtime library;
- another target;
- a feature branch.

---

# 16. Acceptance criteria

This task is complete only when all of the following are true:

1. `AND` is officially short-circuiting.
2. `OR` is officially short-circuiting.
3. operands are evaluated left to right.
4. unreachable division by zero is not reported.
5. unreachable arithmetic overflow is not reported.
6. undefined variables remain binding errors even in unreachable operands.
7. type errors remain binding errors even in unreachable operands.
8. reachable division by zero still reports `SMILE1207`.
9. reachable overflow still reports `SMILE1206`.
10. `ConcatenationExpressionSyntax` is removed.
11. `BoundConcatenationExpression` is removed.
12. string concatenation uses typed binary-expression nodes only.
13. all consumers use the canonical representation.
14. `long.MinValue / -1` reports overflow.
15. negating `long.MinValue` reports overflow.
16. signed division truncates toward zero.
17. subtraction and division associativity are correct.
18. string equality is case-sensitive.
19. Java string equality uses `.equals`.
20. C and Objective-C string equality uses `strcmp` or equivalent.
21. all official escapes remain correct.
22. control-character tests do not hide NUL or other bytes.
23. raw `PRINT` templates keep backslashes literal.
24. C preserves Integer expression intent where practical.
25. C preserves Boolean expression intent where practical.
26. Objective-C preserves Integer expression intent where practical.
27. Objective-C preserves Boolean expression intent where practical.
28. C/Objective-C typed `PRINT` remains format-safe.
29. high-level target parentheses preserve SMILE semantics.
30. deterministic generated corpus source is stable.
31. generated corpus output matches `SmileEvaluator`.
32. every installed target builds and runs the typed acceptance program.
33. every installed target builds and runs the generated corpus.
34. all eight targets transpile successfully.
35. existing `LET` and `PRINT` behavior remains unchanged.
36. MASM empty strings remain correct.
37. target identifier mapping remains correct.
38. desktop rapid language switching remains responsive.
39. Build & Run errors do not close SMILE.
40. Debug build has zero warnings.
41. Release build has zero warnings.
42. Debug tests pass.
43. Release tests pass.
44. documentation matches implementation.
45. no unrelated files or build artifacts are committed.
46. all work is performed directly on `main`.

---

# 17. Validation commands

Run from the repository root:

```bat
cmd /c git status --short --branch
```

Confirm the active branch is:

```text
main
```

Do not create another branch.

Run:

```bat
cmd /c dotnet restore SMILE.sln
```

```bat
cmd /c dotnet build SMILE.sln -c Debug -nologo
```

```bat
cmd /c dotnet test SMILE.sln -c Debug --no-build -nologo
```

```bat
cmd /c dotnet build SMILE.sln -c Release -nologo
```

```bat
cmd /c dotnet test SMILE.sln -c Release --no-build -nologo
```

Generate all targets:

```bat
cmd /c dotnet run --project src\SMILE.Cli -- examples\TypedExpressionCore.smile --target all
```

Run each installed target explicitly:

```bat
cmd /c dotnet run --project src\SMILE.Cli -- examples\TypedExpressionCore.smile --target csharp --run
```

```bat
cmd /c dotnet run --project src\SMILE.Cli -- examples\TypedExpressionCore.smile --target c --run
```

```bat
cmd /c dotnet run --project src\SMILE.Cli -- examples\TypedExpressionCore.smile --target masm-x64 --run
```

```bat
cmd /c dotnet run --project src\SMILE.Cli -- examples\TypedExpressionCore.smile --target javascript --run
```

```bat
cmd /c dotnet run --project src\SMILE.Cli -- examples\TypedExpressionCore.smile --target java --run
```

```bat
cmd /c dotnet run --project src\SMILE.Cli -- examples\TypedExpressionCore.smile --target cobol --run
```

```bat
cmd /c dotnet run --project src\SMILE.Cli -- examples\TypedExpressionCore.smile --target objective-c --run
```

```bat
cmd /c dotnet run --project src\SMILE.Cli -- examples\TypedExpressionCore.smile --target swift --run
```

Run equivalent commands for the deterministic generated-expression corpus if it is written to an example file during validation.

Before any authorized commit:

```bat
cmd /c git diff --check
```

```bat
cmd /c git diff --stat
```

```bat
cmd /c git status --short --branch
```

---

# 18. Manual desktop validation

In SMILE Desktop:

1. Load or paste `TypedExpressionCore.smile`.
2. Switch rapidly among C, Swift, Java, COBOL, Objective-C, MASM, JavaScript, and C#.
3. Confirm the UI remains responsive.
4. Build and run each installed target.
5. Verify identical output.
6. Enter:

```smile
LET Result = FALSE AND (1 / 0 = 0)
PRINT {Result}
```

7. Confirm output is `FALSE`.
8. Enter:

```smile
LET Result = TRUE AND (1 / 0 = 0)
```

9. Confirm `SMILE1207` appears without closing the application.
10. Enter:

```smile
LET Invalid = -9223372036854775808 / -1
```

11. Confirm `SMILE1206` appears.
12. Correct the source and verify live generation recovers.
13. Inspect generated C and Objective-C and confirm Integer/Boolean expressions remain visible.
14. Confirm Boolean output is exactly `TRUE` or `FALSE`.

---

# 19. Suggested implementation sequence

1. Add failing short-circuit tests.
2. Implement short-circuit constant evaluation.
3. Update official expression specification.
4. Remove legacy concatenation syntax and bound nodes.
5. Update all consumers and tests.
6. Add signed 64-bit edge tests.
7. Add string equality tests.
8. Add string escape byte-aware tests.
9. Refactor C and Objective-C typed declaration generation.
10. Refactor C and Objective-C typed `PRINT`.
11. Add precedence/parentheses generator tests.
12. Add deterministic generated-expression corpus.
13. Run evaluator-versus-target integration tests.
14. Run all eight explicit CLI Build & Run commands.
15. Perform desktop smoke validation.
16. Update documentation.
17. Commit directly to `main` only when Sin authorizes it.

---

# 20. Completion report

Report:

- exact files changed;
- short-circuit semantic rule;
- binder versus evaluator behavior;
- tests for unreachable and reachable errors;
- obsolete nodes removed;
- arithmetic edge cases covered;
- string equality behavior;
- string escape validation;
- C generation changes;
- Objective-C generation changes;
- deterministic corpus design and seed;
- exact Debug test total;
- exact Release test total;
- zero-warning build results;
- transpilation result for all eight targets;
- Build & Run result for each target;
- evaluator-versus-target comparisons;
- desktop smoke-test results;
- documentation changes;
- any unresolved concern.

Do not state that v0.4.1 is complete if an installed target was not actually run or if it disagrees with `SmileEvaluator`.

---

# 21. Commit guidance

Do not commit or push unless Sin explicitly authorizes it.

When authorized, commit directly to `main`.

Suggested subject:

```text
Sin and Codex: Harden typed expression conformance
```

The commit body should mention:

- short-circuit `AND` / `OR`;
- canonical binary-expression representation;
- signed 64-bit edge hardening;
- string equality and escape conformance;
- C and Objective-C typed-expression preservation;
- deterministic evaluator-versus-target corpus;
- all eight target validation results;
- exact Debug and Release test totals.
