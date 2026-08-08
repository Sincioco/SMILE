# Codex Implementation Instructions — SMILE v0.5.1.1 Generated Warning Hygiene

> [!IMPORTANT]
> **HISTORICAL / PARTIALLY SUPERSEDED**
>
> This document records the original generated-warning milestone. Warning hygiene remains valuable for changed active targets and milestone validation. Requirements for routine Java/all-ten-target gates, duplicated full configurations, and paused-target maintenance are superseded by `docs/SMILE Core Principles.md`, the active three-target policy, and Velocity Mode.

## Repository and workflow

- Repository: `Sincioco/SMILE`
- Work **directly on `main` only**.
- Sin is the only developer.
- **Do not create, suggest, or use a feature branch.**
- Do not open a pull request.
- Re-read `AGENTS.md` before changing code.
- Inspect the current `main` branch and working tree before editing.
- Do not discard, reset, overwrite, or commit unrelated work.
- Do not commit or push unless Sin explicitly authorizes it in the Codex session.
- Follow KISS and KISS v2, “The Sin Way.”
- Do not add another destination language.
- Do not add any new SMILE syntax.
- Do not begin `IF`, `INPUT`, loops, functions, scopes, arrays, floating-point values, comments, or another language feature.
- Preserve `examples/language.smile` as the cumulative language reference.
- Preserve asynchronous Desktop startup, first-paint loading, visible-target-only live transpilation, cancellation, and failure containment.
- Do not add a parser generator, compiler framework, runtime framework, package manager, analyzer package, or unnecessary dependency.

The reviewed baseline when this brief was prepared was:

```text
0ebfea9bbbb4e9c6bed57ca7628c9d316cf23cb5
Sin and Codex: Complete v0.5.1 runtime storage readiness
```

Do not assume that SHA is still current. Always start from the newest `main`.

---

# 1. Milestone

Create the focused maintenance release:

> **SMILE v0.5.1.1 — Generated Warning Hygiene**

This release fixes generated-code compiler warnings caused by valid direct SMILE self-assignment.

Primary example:

```smile
LET LastName = "Cioco"
SET LastName = LastName
PRINT {LastName}
```

SMILE defines this as a valid no-op assignment.

The generated C# target must not emit:

```csharp
LastName = LastName;
```

because the C# compiler reports warning:

```text
CS1717: Assignment made to same variable
```

The release must:

1. preserve valid SMILE self-assignment semantics;
2. keep a real target assignment;
3. eliminate C# compiler warnings;
4. add generated-target warning validation;
5. preserve all ten targets;
6. add no syntax.

---

# 2. Preserve all existing v0.5.1 behavior

Do not regress:

- `LET`;
- `SET`;
- `PRINT`;
- SET Block String Literal — The SMILE Way;
- case-insensitive variable lookup;
- fixed variable types;
- atomic SET evaluation;
- direct self-assignment as a valid no-op;
- mutable evaluator state;
- statement-order execution tracing;
- mutation-aware simplification;
- mutation-aware Integer profiling;
- direct target-storage reads;
- C and Objective-C pointer-plus-length behavior;
- C-family length-aware equality;
- COBOL current-storage output;
- MASM pointer-and-length output;
- Java full-JDK validation;
- exact String and embedded-NUL behavior;
- deterministic generation;
- all-ten-target evaluator conformance;
- cumulative `language.smile`;
- Desktop responsiveness;
- destination-language freeze.

---

# 3. Direct self-assignment semantics

These are valid SMILE statements:

```smile
LET Name = "Sin"
SET Name = Name
```

```smile
LET Count = 49
SET Count = Count
```

```smile
LET Ready = TRUE
SET Ready = Ready
```

They are no-op assignments.

Required observable behavior:

```text
The variable retains its current value.
The SET statement remains present semantically.
The target generator emits an actual assignment operation.
The target compiler produces no warning caused by that assignment.
```

Do not optimize the SET statement away.

---

# 4. C# self-assignment lowering

For a direct self-assignment:

```text
SET target = target
```

the C# generator must emit the smallest type-preserving identity expression.

## String

SMILE:

```smile
SET Name = Name
```

C#:

```csharp
Name = Name + "";
```

## Integer

SMILE:

```smile
SET Count = Count
```

C#:

```csharp
Count = Count + 0;
```

The literal must respect the selected C# Integer profile naturally.

For both `int` and `long`, `+ 0` is valid and warning-free.

## Boolean

SMILE:

```smile
SET Ready = Ready
```

C#:

```csharp
Ready = Ready || false;
```

This preserves value and emits a real assignment.

---

# 5. Detect only direct self-assignment

Apply identity lowering only when:

```text
BoundSetStatement.Value is BoundVariableExpression
and
the referenced VariableSymbol is the same symbol as the SET target
```

Conceptually:

```csharp
if (set.Value is BoundVariableExpression variable &&
    variable.Variable == set.Variable)
{
    // emit identity assignment
}
```

Do not apply the rule merely because two variables have equal compile-time values.

Example:

```smile
LET A = 1
LET B = 1
SET A = B
```

must remain a normal assignment:

```csharp
A = B;
```

Do not lower it to `A = A + 0`.

---

# 6. Keep the implementation target-local and small

The self-assignment workaround belongs in the C# generator.

Do not change:

- SMILE language semantics;
- binder behavior;
- evaluator behavior;
- bound-tree shape;
- execution trace;
- parser;
- lexer;
- other generators unless testing discovers an actual warning.

A small shared helper may be introduced only if it clearly reduces duplication with the existing Swift self-assignment logic.

Acceptable helper concept:

```text
TryWriteIdentityAssignment(
    TargetLanguage,
    SmileType,
    targetExpression)
```

However, do not build an abstraction more complex than the two current target cases require.

KISS is preferred.

---

# 7. Review Swift behavior without changing semantics

Swift already requires identity lowering for direct self-assignment.

Preserve:

```swift
Name = Name + ""
Count = Count + 0
Ready = Ready || false
```

Add shared regression tests that confirm C# and Swift both handle all three SMILE types.

Do not change other targets unless their compilers produce warnings or errors for direct self-assignment.

---

# 8. Generated C# warning validation

The current solution build warning count is not enough.

The generated C# program must itself compile with zero warnings.

Create test infrastructure that:

1. transpiles a SMILE source to C#;
2. builds the generated C# project;
3. captures generated-target compiler output;
4. verifies successful build;
5. verifies zero warnings.

Use the existing C# toolchain and process infrastructure.

Do not invoke a separate compiler implementation or add Roslyn packages merely for warning inspection.

---

# 9. Required warning-count behavior

For official strict release validation, generated C# warning output must be treated as a failure.

At minimum, fail when build output contains a C# compiler warning pattern such as:

```text
warning CS####
```

Prefer structured process/build data if the current toolchain exposes warning counts.

Avoid a fragile check for the word `warning` alone because SDK informational text may contain unrelated wording.

A reasonable helper:

```text
ContainsCSharpCompilerWarning(buildOutput)
```

must detect compiler diagnostics reliably.

---

# 10. Strict generated-target warning gate

Add an environment-controlled strict gate consistent with current release validation.

Recommended environment variable:

```text
SMILE_REQUIRE_ZERO_TARGET_WARNINGS
```

When set to `1`:

- generated-target warning validation must execute;
- a compiler warning in a supported strict target must fail the test;
- the completion report must show the strict gate was enabled.

For v0.5.1.1, C# is mandatory.

Java may also be included if its current compile commands reliably expose warnings, but do not expand scope unnecessarily.

Do not claim that all ten targets are warning-free unless each compiler's warning model has actually been checked.

---

# 11. Required direct self-assignment test program

Use:

```smile
LET Name = "Sin"
LET Count = 49
LET Ready = TRUE

SET Name = Name
SET Count = Count
SET Ready = Ready

PRINT {Name}
PRINT {Count}
PRINT {Ready}
```

Expected output:

```text
Sin
49
TRUE
```

Required C# generated structure:

```csharp
Name = Name + "";
Count = Count + 0;
Ready = Ready || false;
```

Forbidden:

```csharp
Name = Name;
Count = Count;
Ready = Ready;
```

---

# 12. C# wide Integer self-assignment

Use:

```smile
LET Count = 5000000000
SET Count = Count
PRINT {Count}
```

Required C# profile:

```csharp
long Count = 5000000000L;
Count = Count + 0;
```

or the repository's established exact idiomatic literal form.

Required behavior:

- generated project compiles;
- zero C# warnings;
- output is `5000000000`.

Do not accidentally force an `int` profile.

---

# 13. Case-insensitive self-assignment

Use:

```smile
LET Name = "Sin"
SET name = NAME
PRINT {NaMe}
```

The binder should resolve both names to the same `VariableSymbol`.

The C# generator must detect this as direct self-assignment and emit the identity form.

Do not compare source-text casing.

Compare symbols.

---

# 14. Mapped identifier self-assignment

Use an identifier requiring C# target mapping.

Choose a current valid SMILE identifier that conflicts with a C# keyword or generator-owned name.

Example, if valid under current rules:

```smile
LET class = "Sin"
SET CLASS = class
PRINT {Class}
```

The test must verify:

- declaration uses the mapped name;
- assignment uses the same mapped name;
- identity lowering uses the mapped target expression;
- generated project has zero warnings;
- runtime output matches `SmileEvaluator`.

Use an actual identifier known to be valid in the current SMILE front end and reserved in C#.

---

# 15. Non-self assignment must remain natural

Use:

```smile
LET A = 1
LET B = 2
SET A = B
PRINT {A}
```

Required C#:

```csharp
A = B;
```

Forbidden:

```csharp
A = A + 0;
```

Also test Strings and Booleans.

---

# 16. Expression identity must not be mistaken for direct self-assignment

These remain ordinary expressions:

```smile
SET Count = Count + 0
SET Name = Name + ""
SET Ready = Ready OR FALSE
```

The generator should emit the expression as written or as simplified under current shared rules.

The special handling applies only to the direct bound variable form:

```smile
SET Count = Count
```

Do not create duplicate identity operations.

---

# 17. `language.smile` validation

The cumulative reference currently contains direct self-assignment:

```smile
SET LastName = LastName
```

Run the complete:

```text
examples/language.smile
```

through the generated C# toolchain.

Required:

- compile exit code zero;
- run exit code zero;
- output matches `SmileEvaluator`;
- no `CS1717`;
- no other C# compiler warnings.

Do not remove the self-assignment example from `language.smile`.

It is a valid SET permutation and belongs in the cumulative reference.

---

# 18. All-ten-target regression

Run the direct self-assignment test program through all ten installed targets.

Required:

- generation succeeds;
- build/run succeeds;
- output matches `SmileEvaluator`;
- no existing target regresses.

Structural expectations:

| Target | Expected direct self-assignment |
|---|---|
| C# | type-preserving identity assignment |
| C | ordinary self-assignment unless compiler-warning validation proves adjustment necessary |
| MASM x64 | real pointer/length or value storage update |
| JavaScript | ordinary assignment |
| Java | ordinary assignment unless warning policy requires otherwise |
| COBOL | real `MOVE` |
| Objective-C | ordinary self-assignment unless warning policy proves adjustment necessary |
| Swift | type-preserving identity assignment |
| Python | ordinary assignment |
| C++ | ordinary assignment unless compiler-warning validation proves adjustment necessary |

Do not proactively rewrite every target.

---

# 19. Optional warning discovery

During validation, record warnings emitted by every installed target compiler.

If another target reports a warning caused specifically by direct self-assignment:

1. add a focused test;
2. implement the smallest idiomatic warning-free lowering;
3. document it;
4. keep scope limited to direct self-assignment.

Do not turn this task into a broad warning-cleanup campaign.

---

# 20. C# toolchain output preservation

Ensure the C# toolchain retains enough build output for warning validation.

Do not truncate away compiler diagnostic lines before tests inspect them.

Preserve existing bounded-output safeguards.

If build output is separated into stdout and stderr, inspect both.

---

# 21. Documentation updates

Update:

- `README.md`;
- `AGENTS.md`;
- `docs/Architecture.md`;
- `docs/Roadmap.md`;
- `docs/SMILE Target Code Generation Standard v1.0.md`;
- requirements/history;
- desktop About/version metadata.

## README

Document:

- v0.5.1.1 adds no syntax;
- valid C# direct self-assignment uses a warning-free identity assignment;
- strict release validation checks generated C# compiler warnings;
- `language.smile` remains cumulative.

## Architecture

Document the distinction:

```text
SMILE semantic operation:
    direct no-op assignment

Destination constraint:
    target may reject or warn about target = target

Generator solution:
    emit smallest type-preserving identity assignment
```

## Target generation standard

Add normative wording equivalent to:

> A valid direct SMILE self-assignment must remain an explicit assignment in generated code. When a destination rejects or warns about `target = target`, emit the smallest type-preserving identity expression.

Add:

> Release validation must distinguish warnings from the SMILE solution build and warnings from generated target programs.

---

# 22. AGENTS.md additions

Preserve all current rules.

Add wording equivalent to:

> Direct SMILE self-assignment is valid and must remain a real generated assignment. For destinations that reject or warn about `target = target`, use the smallest type-preserving identity expression.

Add:

> Generated-target compiler warnings are separate from SMILE solution warnings. Strict release validation must inspect generated compiler output where supported.

Add:

> v0.5.1.1 is a syntax-free warning-hygiene release. Do not add IF, INPUT, or another language feature while implementing it.

---

# 23. Roadmap

Add:

## Implemented in v0.5.1.1

- warning-free C# direct self-assignment;
- generated C# compiler-warning gate;
- `language.smile` C# warning validation;
- all-ten-target self-assignment regression coverage.

Keep:

```text
Next Major Milestone:
v0.6.0 — IF / THEN / ELSE
```

Do not implement IF in this task.

---

# 24. Version

Use:

```text
SMILE v0.5.1.1 — Generated Warning Hygiene
```

Align:

- project version;
- assembly version;
- file version;
- informational version;
- About dialog;
- README;
- roadmap;
- requirements history.

---

# 25. Required unit tests

## String self-assignment

Assert:

```csharp
Name = Name + "";
```

and no:

```csharp
Name = Name;
```

## Integer self-assignment

Assert:

```csharp
Count = Count + 0;
```

and no:

```csharp
Count = Count;
```

## Boolean self-assignment

Assert:

```csharp
Ready = Ready || false;
```

and no:

```csharp
Ready = Ready;
```

## Wide Integer

Assert correct `long` declaration and identity assignment.

## Case-insensitive symbol

Assert identity lowering still occurs.

## Mapped identifier

Assert mapped target name is used consistently.

## Different variable

Assert normal assignment remains.

## Determinism

Generate twice and compare all files byte-for-byte.

---

# 26. Required integration tests

## Generated C# self-assignment program

Build and run.

Verify:

```text
success
exit code 0
stdout matches SmileEvaluator
zero C# compiler warnings
```

## `language.smile`

Build and run generated C#.

Verify:

```text
success
exit code 0
stdout matches SmileEvaluator
no CS1717
zero C# compiler warnings
```

## All ten targets

Run the direct self-assignment acceptance program.

Verify exact evaluator comparison.

---

# 27. Warning parser tests

Add deterministic tests for the warning detector.

Must detect:

```text
Program.cs(10,9): warning CS1717: Assignment made to same variable
```

Must not treat these as C# compiler warnings:

```text
Build succeeded.
0 Warning(s)
```

```text
Warnings: 0
```

```text
No warnings were produced.
```

Prefer matching:

```regex
\bwarning\s+CS\d{4}\b
```

case-insensitively.

Do not use this exact regex if the current toolchain returns structured diagnostics instead.

---

# 28. Strict validation environment

Document and use:

```powershell
$env:SMILE_REQUIRE_JAVA = '1'
$env:SMILE_REQUIRE_ALL_TARGETS = '1'
$env:SMILE_REQUIRE_ZERO_TARGET_WARNINGS = '1'
```

Run both Debug and Release test suites under the strict environment.

Remove environment variables after validation.

---

# 29. Desktop validation

1. Launch SMILE Desktop.
2. Confirm first-paint startup remains responsive.
3. Confirm `language.smile` loads.
4. Select C#.
5. Find or retain `SET LastName = LastName`.
6. Confirm generated C# uses the identity form.
7. Build and run C#.
8. Confirm no C# warning appears in output.
9. Confirm runtime output matches expected reference behavior.
10. Rapidly switch targets.
11. Confirm responsiveness.
12. Confirm About shows v0.5.1.1.

---

# 30. Performance and containment

Do not:

- run generated compiler validation on the WPF dispatcher;
- slow live transpilation by invoking compilers;
- run warning checks during normal editor typing;
- make Build & Run synchronous;
- remove output limits;
- weaken cancellation.

Warning validation belongs in tests and normal explicit Build & Run output handling, not live transpilation.

---

# 31. Scope exclusions

Do not implement:

- `IF`;
- `THEN`;
- `ELSE`;
- `INPUT`;
- loops;
- functions;
- scopes;
- comments;
- arrays;
- floating-point values;
- assignment expressions;
- compound assignment;
- a general optimizer;
- another destination language;
- a new runtime library;
- a feature branch.

---

# 32. Acceptance criteria

The task is complete only when all are true:

1. v0.5.1.1 adds no syntax.
2. direct String self-assignment remains valid.
3. direct Integer self-assignment remains valid.
4. direct Boolean self-assignment remains valid.
5. C# emits a String identity assignment.
6. C# emits an Integer identity assignment.
7. C# emits a Boolean identity assignment.
8. C# does not emit direct `target = target` for self-assignment.
9. wide Integer profile remains correct.
10. case-insensitive self-assignment is detected by symbol identity.
11. mapped identifiers work.
12. different-variable assignment remains natural.
13. complex expressions are not mistaken for direct self-assignment.
14. Swift behavior remains correct.
15. all other targets retain semantics.
16. `language.smile` retains its self-assignment example.
17. generated `language.smile` C# compiles.
18. generated `language.smile` C# runs.
19. generated `language.smile` output matches `SmileEvaluator`.
20. no `CS1717` is emitted.
21. generated C# compiler warning count is zero.
22. warning detection tests pass.
23. strict warning gate exists.
24. Debug SMILE solution build has zero warnings.
25. Release SMILE solution build has zero warnings.
26. Debug tests pass.
27. Release tests pass.
28. strict Java gate passes.
29. strict all-target gate passes.
30. strict generated-warning gate passes.
31. all ten targets run the self-assignment program.
32. all ten outputs match `SmileEvaluator`.
33. generation remains deterministic.
34. Desktop remains responsive.
35. documentation matches implementation.
36. destination-language expansion remains frozen.
37. no unrelated feature is added.
38. no unapproved dependency is added.
39. no build artifacts are committed.
40. all work is performed directly on `main`.

---

# 33. Suggested implementation sequence

1. Confirm newest `main`.
2. Reproduce C# `CS1717` using generated `language.smile`.
3. Add failing C# generator tests for all three types.
4. Implement direct self-assignment identity lowering in C#.
5. Add wide Integer, case-insensitive, and mapped-identifier tests.
6. Add generated C# warning detector tests.
7. Add strict warning-gate integration test.
8. Build/run the direct self-assignment program through C#.
9. Build/run `language.smile` through C#.
10. Run all ten targets with the self-assignment program.
11. Run Debug strict validation.
12. Run Release strict validation.
13. perform Desktop smoke testing.
14. update documentation and version metadata.
15. commit directly to `main` only when Sin explicitly authorizes it.

---

# 34. Validation commands

Run from the repository root.

```bat
cmd /c git status --short --branch
```

Confirm:

```text
main
```

```bat
cmd /c dotnet restore SMILE.sln
```

```bat
cmd /c dotnet build SMILE.sln -c Debug -nologo
```

Strict Debug validation from PowerShell:

```powershell
$env:SMILE_REQUIRE_JAVA = '1'
$env:SMILE_REQUIRE_ALL_TARGETS = '1'
$env:SMILE_REQUIRE_ZERO_TARGET_WARNINGS = '1'
dotnet test SMILE.sln -c Debug --no-build -nologo
Remove-Item Env:SMILE_REQUIRE_JAVA
Remove-Item Env:SMILE_REQUIRE_ALL_TARGETS
Remove-Item Env:SMILE_REQUIRE_ZERO_TARGET_WARNINGS
```

```bat
cmd /c dotnet build SMILE.sln -c Release -nologo
```

Strict Release validation:

```powershell
$env:SMILE_REQUIRE_JAVA = '1'
$env:SMILE_REQUIRE_ALL_TARGETS = '1'
$env:SMILE_REQUIRE_ZERO_TARGET_WARNINGS = '1'
dotnet test SMILE.sln -c Release --no-build -nologo
Remove-Item Env:SMILE_REQUIRE_JAVA
Remove-Item Env:SMILE_REQUIRE_ALL_TARGETS
Remove-Item Env:SMILE_REQUIRE_ZERO_TARGET_WARNINGS
```

Generate all targets:

```bat
cmd /c dotnet run --project src\SMILE.Cli -- <SELF_ASSIGNMENT_ACCEPTANCE.smile> --target all
```

Run C# self-assignment acceptance program:

```bat
cmd /c dotnet run --project src\SMILE.Cli -- <SELF_ASSIGNMENT_ACCEPTANCE.smile> --target csharp --run
```

Run cumulative reference through C#:

```bat
cmd /c dotnet run --project src\SMILE.Cli -- examples\language.smile --target csharp --run
```

Before an authorized commit:

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

# 35. Completion report

Report:

- exact baseline commit;
- exact files changed;
- proof that `CS1717` was reproduced before the fix;
- C# String identity lowering;
- C# Integer identity lowering;
- C# Boolean identity lowering;
- wide Integer result;
- case-insensitive result;
- mapped-identifier result;
- non-self-assignment regression result;
- warning detector design;
- strict warning-gate environment;
- generated C# warning count;
- `language.smile` C# build/run result;
- exact Debug test count;
- exact Release test count;
- skipped test count;
- all-ten-target self-assignment results;
- zero-warning SMILE solution results;
- Desktop smoke results;
- documentation changes;
- unresolved concerns.

Do not claim generated warning hygiene if:

- `CS1717` still appears;
- `language.smile` is altered merely to remove self-assignment;
- the SET statement is optimized away;
- C# warning validation checks only the SMILE solution build rather than the generated target build.

---

# 36. Commit guidance

Do not commit or push unless Sin explicitly authorizes it.

Suggested subject:

```text
Sin and Codex: Eliminate generated self-assignment warnings
```

Suggested commit body topics:

- warning-free C# direct self-assignment;
- generated C# warning gate;
- `language.smile` warning validation;
- all-ten-target self-assignment regression;
- no new syntax;
- exact Debug/Release validation totals.
