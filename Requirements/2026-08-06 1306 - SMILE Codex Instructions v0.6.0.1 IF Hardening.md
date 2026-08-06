# Codex Implementation Instructions — SMILE v0.6.0.1 IF Hardening

## Repository and workflow

- Repository: `Sincioco/SMILE`
- Work directly on `main`.
- Sin is the only developer.
- Do not create or suggest a feature branch.
- Do not open a pull request.
- Re-read `AGENTS.md` before changing code.
- Inspect the current `main` branch, latest commit, tags, and working tree before editing.
- Do not discard, reset, overwrite, or commit unrelated work.
- Preserve all existing SMILE behavior unless this brief explicitly changes it.
- Follow KISS and KISS v2, “The Sin Way.”
- Do not add unnecessary abstractions, frameworks, packages, or dependencies.
- When all required tests are green, commit all changes and push to `main`.
- Use a detailed public commit message prefixed with:

```text
Sin and Codex:
```

The reviewed baseline when this brief was prepared was:

```text
0fa151ec5d53da80ea97afe6e115b074297a0193
Sin and Codex: Implement formal IF control flow
```

Do not assume that SHA is still current. Always begin from the newest `main`.

---

# 1. Objective

Create the maintenance release:

> **SMILE v0.6.0.1 — IF Hardening**

This is a hardening, maintainability, continuous-integration, and compiler-safety release for the existing v0.6.0 implementation of:

```smile
IF condition THEN
    statements
ELSE IF condition THEN
    statements
ELSE
    statements
END IF
```

The purpose of this release is to:

1. add independent GitHub continuous-integration validation;
2. split the oversized generator implementation into maintainable files without changing generated behavior;
3. move the binder out of the parser file without changing compiler behavior;
4. protect the compiler and Desktop application from pathological IF nesting;
5. add a direct source regression for function-shaped syntax in IF conditions;
6. rerun and preserve strict all-ten-target validation;
7. publish a stable v0.6.0.1 baseline.

This release must not redesign the IF statement.

---

# 2. Non-goals and strict scope boundaries

Do not add or implement:

- `INPUT`;
- loops;
- functions;
- procedures;
- scopes;
- arrays;
- comments;
- classes;
- floating-point or decimal values;
- one-line IF;
- assignment expressions;
- compound assignment;
- another destination language;
- a parser generator;
- a compiler framework;
- a control-flow-graph package;
- a template engine;
- a runtime framework;
- a package manager;
- a new dependency solely for this work.

Do not change these permanent SMILE IF rules:

1. Conditions are call-free.
2. Every atomic Boolean condition requires an explicit comparison and a right-hand operand.
3. Standalone Boolean variables and literals are invalid conditions.
4. `ELSE IF` consists of two keywords on the same logical line.
5. `ELSE` followed by `IF` on the next line means a nested IF.
6. One `END IF` closes one complete IF / ELSE IF / ELSE chain.
7. Every destination must preserve genuine branch structure.
8. `LET` remains prohibited inside IF v1.0 bodies.
9. Only the selected branch executes and mutates runtime state.
10. All branches are still parsed, bound, validated, and type-checked.

Do not alter the output, diagnostics, target code, or evaluation behavior of valid existing v0.6.0 programs except for the explicit maximum-nesting safety rule defined below.

---

# 3. Read and preserve the existing implementation

Before editing, inspect at minimum:

```text
AGENTS.md
README.md
docs/Architecture.md
docs/Roadmap.md
docs/Toolchains.md
docs/SMILE Target Code Generation Standard v1.0.md
docs/SMILE Language Specification/SMILE - IF Statement Official Specification v1.0.md
examples/language.smile

src/SMILE.Engine/Parser.cs
src/SMILE.Engine/Language.cs
src/SMILE.Engine/Evaluation.cs
src/SMILE.Engine/ExecutionTrace.cs
src/SMILE.Engine/Analysis.cs
src/SMILE.Engine/Generation.cs
src/SMILE.Engine/TargetIdentifierMap.cs

tests/SMILE.Tests/IfStatementConformanceTests.cs
tests/SMILE.Tests/IfTargetConformanceTests.cs
tests/SMILE.Tests/SyntaxHighlightingTests.cs
tests/SMILE.Tests/DesktopCommandTests.cs
```

Preserve:

- the canonical syntax and bound IF representations;
- recursive block parsing;
- exact same-line `ELSE IF` recognition;
- nested IF behavior;
- `SMILE1401` through `SMILE1415`;
- branch-aware Known/Unknown analysis;
- concrete evaluator behavior;
- conservative outgoing-path merging;
- short-circuit behavior;
- target identifier collision protection;
- exact String and embedded-NUL handling;
- signed 64-bit SMILE Integer semantics;
- target-idiomatic Integer profiles;
- deterministic target generation;
- all ten current targets;
- Desktop first-paint behavior;
- background live transpilation;
- cancellation and failure containment;
- the cumulative `examples/language.smile` reference.

---

# 4. Task 1 — Add GitHub Actions continuous integration

Create a normal push/pull-request CI workflow under:

```text
.github/workflows/
```

Use a clear filename such as:

```text
smile-ci.yml
```

## 4.1 Required triggers

Run the workflow on:

- pushes to `main`;
- pull requests targeting `main`;
- manual `workflow_dispatch`.

Even though the normal workflow is to work directly on `main`, pull-request validation should still exist for public contributors or future process changes.

## 4.2 Required runner

Use a Windows runner because the solution includes the WPF Desktop application.

Use the current stable GitHub Actions releases available at implementation time for:

- repository checkout;
- .NET SDK setup.

Do not guess obsolete action versions. Verify the current stable major versions when implementing.

Install the .NET SDK version required by the repository. Prefer, in this order:

1. the repository's `global.json`, when present;
2. otherwise the SDK matching the solution's current target framework.

Do not retarget the solution merely to simplify CI.

## 4.3 Required CI commands

The normal CI workflow must run:

```text
dotnet restore SMILE.sln
dotnet build SMILE.sln -c Debug --no-restore
dotnet test SMILE.sln -c Debug --no-build --no-restore
dotnet build SMILE.sln -c Release --no-restore
dotnet test SMILE.sln -c Release --no-build --no-restore
```

Use `-nologo` where appropriate.

The workflow must fail when any command fails.

Do not silently continue after a failed build or test command.

## 4.4 Standard CI versus strict local release validation

Normal GitHub CI does not need to install all ten destination-language toolchains.

The existing strict all-target release validation remains mandatory locally before committing and pushing this release.

Do not weaken or remove:

```text
SMILE_REQUIRE_JAVA
SMILE_REQUIRE_ALL_TARGETS
SMILE_REQUIRE_ZERO_TARGET_WARNINGS
```

Document clearly that:

- GitHub CI independently validates the SMILE solution and unit/integration tests available on the hosted runner;
- strict release validation additionally requires all ten local target toolchains.

## 4.5 CI documentation

Update the README and toolchain documentation with:

- the workflow name;
- what it validates;
- what it does not replace;
- the strict local commands required before a release commit.

If the repository uses a status badge convention, add a CI badge to the README. Otherwise, do not add decorative badges solely for appearance.

---

# 5. Task 2 — Split `Generation.cs` without changing behavior

`src/SMILE.Engine/Generation.cs` has become too large and is now a major maintenance hotspot.

Split it into focused source files while preserving the same namespace, visibility, APIs, generated file contents, and behavior.

## 5.1 Required principle

This is a file-organization refactor, not a generator redesign.

Do not introduce:

- a new generator framework;
- inheritance hierarchies solely for the split;
- reflection-based generator discovery;
- source generation;
- a dependency-injection container;
- target templates;
- dynamic loading;
- unnecessary interfaces beyond the current design.

Move existing types and helpers into logical files.

## 5.2 Recommended structure

Use a structure similar to:

```text
src/SMILE.Engine/Generation/
    GeneratedProgram.cs
    CodeGeneratorRegistry.cs
    BoundProgramSimplifier.cs
    BoundStatementTree.cs
    RuntimeTextPlan.cs
    GeneratorConditionFacts.cs
    TargetIntegerProfile.cs
    TargetExpression.cs
    TargetEscapes.cs
    TargetTypes.cs

    CSharpCodeGenerator.cs
    CCodeGenerator.cs
    MasmX64CodeGenerator.cs
    JavaScriptCodeGenerator.cs
    JavaCodeGenerator.cs
    CobolCodeGenerator.cs
    ObjectiveCCodeGenerator.cs
    SwiftCodeGenerator.cs
    PythonCodeGenerator.cs
    CppCodeGenerator.cs
```

This exact layout is not mandatory if the current code naturally groups some helpers differently.

The required outcome is:

- one clear file per destination generator;
- shared generator helpers separated from destination implementations;
- no giant replacement monolith;
- no duplicated shared logic.

## 5.3 Required preservation

The refactor must preserve:

- all public and internal APIs unless a purely internal file move requires a harmless declaration adjustment;
- target language order;
- stable target IDs;
- generated filenames;
- generated project files;
- deterministic labels;
- target identifier mapping;
- warning-safe condition wrappers;
- exact String behavior;
- embedded NUL behavior;
- Integer width selection;
- post-IF runtime storage reads;
- all source branches;
- exact evaluator conformance.

For representative programs, generated output should remain byte-for-byte identical unless a change is necessary only to accommodate the file split. A file split should normally require no generated-output change.

Do not perform unrelated generator cleanup while moving the code.

## 5.4 Validation for the split

Run all existing generator and determinism tests.

Add or strengthen a regression only when needed to prove that:

- all ten generators remain registered;
- each generator remains deterministic;
- the generated file set remains unchanged;
- IF branch bodies remain present;
- strict compiler-warning checks still pass.

Avoid tests that merely assert implementation filenames unless those filenames are part of the generated-program contract.

---

# 6. Task 3 — Move the binder out of `Parser.cs`

Move the existing `Binder` implementation from:

```text
src/SMILE.Engine/Parser.cs
```

into:

```text
src/SMILE.Engine/Binder.cs
```

## 6.1 Required principle

This is also a behavior-preserving file split.

Preserve:

- the `Binder` type name;
- its namespace;
- its visibility;
- all binding behavior;
- declaration-before-use rules;
- failed-LET symbol isolation;
- SET type checking;
- IF condition validation;
- `LET` rejection inside IF;
- execution-trace integration;
- diagnostic codes and messages;
- source spans.

Do not redesign the parser/binder boundary during this release.

Do not introduce a new semantic model or binding framework.

## 6.2 Parser file outcome

After the move, `Parser.cs` should contain parsing and parser-owned helper logic, not the binder implementation.

The parser and binder must continue to communicate through the existing syntax tree.

---

# 7. Task 4 — Add a maximum IF nesting safety limit

Protect the parser, binder, evaluator, analyzer, simplifier, generators, CLI, and Desktop application from pathological IF nesting that could otherwise cause a process-level stack overflow.

## 7.1 Official implementation limit

Use:

```text
Maximum IF nesting depth: 128
```

Depth 1 is the first outermost IF.

A program nested to exactly 128 IF levels is valid.

Attempting to enter IF level 129 must produce a normal SMILE diagnostic rather than crashing, hanging, or overflowing the process stack.

## 7.2 New diagnostic

Add:

```text
SMILE1416
Maximum IF nesting depth of 128 exceeded.
```

Use `DiagnosticSeverity.Error`.

The diagnostic span should identify the `IF` keyword that exceeds the limit.

Add the diagnostic to:

- the implementation;
- README diagnostics;
- the IF specification as an implementation safety limit;
- relevant architecture or compiler-limit documentation;
- tests.

Do not renumber or change `SMILE1401` through `SMILE1415`.

## 7.3 Parser recovery requirement

The parser must not recurse into an over-limit IF body.

It must recover deterministically and continue processing enough source to:

- avoid stack overflow;
- avoid an infinite loop;
- avoid cascading thousands of duplicate diagnostics;
- preserve later top-level diagnostics when reasonably possible;
- keep the Desktop editor responsive while the user is typing malformed or excessively nested code.

Use a small iterative recovery scan to skip or balance the over-limit IF block.

The recovery scan must respect current logical-line rules, including:

- same-line `ELSE IF`;
- standalone `ELSE`;
- `END IF`;
- nested IF counting;
- SET Block String Literals whose physical lines may contain text resembling IF, ELSE, or END IF.

Do not parse block-string content as control-flow headers.

## 7.4 Required nesting tests

Generate deep source programmatically in tests rather than committing enormous handwritten files.

Add tests for:

1. depth 1;
2. depth 128 succeeds;
3. depth 129 reports `SMILE1416`;
4. depth 1,000 does not crash, hang, or produce an unbounded diagnostic storm;
5. valid code after a recoverable over-limit block is still handled deterministically when recovery permits it;
6. text such as `END IF` inside a SET Block String does not affect depth recovery;
7. the Desktop/transpiler path returns diagnostics instead of throwing.

Do not require generation or evaluation of a program that has `SMILE1416`.

---

# 8. Task 5 — Add a direct source regression for function-shaped IF syntax

Functions do not exist in v0.6.0.1.

Do not add function-call grammar.

Add a direct source-level regression for:

```smile
IF FUNC(A) > 10 THEN
END IF
```

The required current behavior is:

- parsing or binding fails;
- the program is not generated;
- the evaluator does not run it;
- at least one error diagnostic is returned;
- the compiler does not crash.

Do not require `SMILE1404` for this source yet if the current expression grammar rejects it earlier with an existing expression diagnostic.

Document in the test why:

- the source must remain invalid today;
- `SMILE1404` is the reserved semantic diagnostic once function-call syntax exists;
- future function implementation must update this regression so a syntactically valid function invocation inside an IF condition produces `SMILE1404`.

Preserve the existing synthetic future-expression tests that prove unknown future expression kinds fail closed as condition calls.

Also add direct invalid regressions for the same function-shaped call in:

```smile
ELSE IF FUNC(A) > 10 THEN
```

and on the right side:

```smile
IF Result = FUNC(A) THEN
END IF
```

All must remain rejected without introducing function parsing.

---

# 9. Task 6 — Preserve and expand IF hardening tests

Keep all existing v0.6.0 IF tests.

Ensure coverage remains for:

- canonical IF / ELSE IF / ELSE syntax tree;
- same-line ELSE IF;
- newline-separated nested IF;
- empty branches;
- nested IF;
- mandatory THEN;
- mandatory END IF;
- malformed ELSE and END IF;
- rejection of `ELSEIF`;
- rejection of `ENDIF`;
- explicit Boolean comparisons;
- invalid standalone Boolean leaves;
- compound conditions;
- short-circuit behavior;
- unselected-branch validation;
- branch SET persistence;
- no branch-local LET;
- block strings inside branches;
- embedded NUL values;
- String length planning;
- Integer promotion across branches;
- deterministic MASM labels;
- helper-name collision safety;
- warning-safe constant conditions;
- all ten target structures;
- evaluator-versus-target runtime conformance.

Add the new CI, file-split, nesting-limit, and function-shaped-source regressions without weakening existing assertions.

Do not replace runtime conformance tests with source-text-only tests.

---

# 10. Task 7 — Version and documentation

Update the project release identity to:

```text
0.6.0.1 IF Hardening
```

Update all places where the current version is intentionally displayed or documented, including as applicable:

- Desktop project metadata;
- About dialog;
- README;
- roadmap;
- architecture;
- toolchain documentation;
- requirements/progress history;
- AGENTS guidance when the new permanent maintenance rules belong there.

## 10.1 Official IF specification update

Do not change IF syntax or semantics.

Add only a concise implementation-safety section documenting:

```text
Maximum supported IF nesting depth: 128
Diagnostic: SMILE1416
```

Clarify that this is a compiler safety/resource limit and not a change to ordinary IF behavior.

## 10.2 Generation architecture documentation

Update the architecture documentation to reflect the generator file split.

Do not claim a new abstraction that was not actually introduced.

## 10.3 CI documentation

Document the difference between:

- hosted GitHub CI;
- strict local all-ten-target release validation.

## 10.4 Cumulative language example

Do not rewrite or replace `examples/language.smile`.

No new language syntax is introduced, so only modify it if a small comment-free valid example is genuinely necessary. Prefer leaving its source behavior unchanged.

---

# 11. Required build and test validation

Run every command from the current repository root.

Examples below use `D:\SMILE`. Adjust only when the actual checkout is elsewhere.

## 11.1 Restore

```bat
cmd /c "cd /d D:\SMILE && dotnet restore SMILE.sln"
```

## 11.2 Debug build and tests

```bat
cmd /c "cd /d D:\SMILE && dotnet build SMILE.sln -c Debug --no-restore -nologo"
```

```bat
cmd /c "cd /d D:\SMILE && dotnet test SMILE.sln -c Debug --no-build --no-restore -nologo"
```

## 11.3 Release build and tests

```bat
cmd /c "cd /d D:\SMILE && dotnet build SMILE.sln -c Release --no-restore -nologo"
```

```bat
cmd /c "cd /d D:\SMILE && dotnet test SMILE.sln -c Release --no-build --no-restore -nologo"
```

## 11.4 Strict all-ten-target Debug validation

```bat
cmd /c "cd /d D:\SMILE && set SMILE_REQUIRE_JAVA=1 && set SMILE_REQUIRE_ALL_TARGETS=1 && set SMILE_REQUIRE_ZERO_TARGET_WARNINGS=1 && dotnet test SMILE.sln -c Debug --no-build --no-restore -nologo"
```

## 11.5 Strict all-ten-target Release validation

```bat
cmd /c "cd /d D:\SMILE && set SMILE_REQUIRE_JAVA=1 && set SMILE_REQUIRE_ALL_TARGETS=1 && set SMILE_REQUIRE_ZERO_TARGET_WARNINGS=1 && dotnet test SMILE.sln -c Release --no-build --no-restore -nologo"
```

Both strict suites must complete with:

- zero failures;
- zero unexpected skips;
- all ten target toolchains executed;
- Java required and executed;
- zero detected generated compiler warnings;
- evaluator-matching output;
- exit code 0 for every required target program.

## 11.6 Official IF acceptance program

Run the official IF acceptance source through the CLI for every target.

Use the existing acceptance source and tests rather than inventing a weaker replacement.

Confirm:

- every target retains every source branch;
- every target builds or runs successfully;
- output exactly matches `SmileEvaluator`;
- generated compiler warnings remain zero where a compile stage exists.

## 11.7 Cumulative language reference

Generate and run, where supported, the cumulative:

```text
examples/language.smile
```

Confirm that the file still:

- parses;
- binds;
- evaluates;
- generates for all ten targets;
- produces the established output;
- remains packaged beside the Desktop executable.

## 11.8 Desktop smoke test

Manually launch the real Desktop application and verify:

1. first paint still occurs before background compiler work;
2. the cumulative language reference loads;
3. the editor remains responsive;
4. IF, THEN, ELSE, and END highlighting still works;
5. nested IF editing does not crash;
6. a depth-129 program shows `SMILE1416`;
7. a depth-1,000 program does not terminate the Desktop process;
8. rapid target switching still works;
9. representative C#, MASM x64, and COBOL IF programs still build and run;
10. About displays:

```text
0.6.0.1 IF Hardening
```

---

# 12. Acceptance criteria

This task is complete only when all of the following are true.

## Continuous integration

- `.github/workflows/smile-ci.yml` or equivalent exists.
- It runs on pushes to `main`.
- It runs on pull requests targeting `main`.
- It supports manual dispatch.
- It restores, builds, and tests both Debug and Release on Windows.
- It fails normally when a build or test fails.
- Documentation distinguishes hosted CI from strict local all-target validation.

## Maintainability

- `Generation.cs` is no longer the generator monolith.
- Each destination generator has a clear dedicated source file.
- Shared generator utilities are separated without duplication.
- No new generator framework or dependency was introduced.
- `Binder` is in `Binder.cs`.
- Parser and binder behavior remain unchanged.

## IF safety

- IF nesting depth 128 succeeds.
- IF nesting depth 129 reports `SMILE1416`.
- Extremely deep input does not stack-overflow, hang, or crash the Desktop application.
- Recovery respects SET Block String content.
- Existing `SMILE1401` through `SMILE1415` remain unchanged.

## Function-shaped invalid source

- `IF FUNC(A) > 10 THEN` remains invalid.
- The equivalent ELSE IF and right-hand function-shaped forms remain invalid.
- No function grammar was introduced.
- Existing future-expression fail-closed tests remain present.

## Behavioral preservation

- Existing valid v0.6.0 programs retain the same meaning.
- Generated output remains deterministic.
- All ten generators preserve every branch.
- Evaluator results still match all ten targets.
- Embedded-NUL behavior remains exact.
- Integer width planning remains correct.
- Target identifier collisions remain protected.
- Generated compiler warnings remain zero under strict validation.
- Desktop behavior remains responsive and contained.

## Release quality

- Debug build: zero warnings and zero errors.
- Release build: zero warnings and zero errors.
- Debug tests: zero failures.
- Release tests: zero failures.
- Strict all-target Debug validation passes.
- Strict all-target Release validation passes.
- Documentation and version metadata say `0.6.0.1 IF Hardening`.
- Working tree is clean after commit.
- Changes are pushed to `main`.

---

# 13. Commit and publication

After all validation is green:

1. review `git diff`;
2. confirm no unrelated changes were included;
3. commit everything;
4. push to `main`.

Use a detailed commit message similar to:

```text
Sin and Codex: Harden IF infrastructure and CI

Release SMILE v0.6.0.1 IF Hardening.

Add Windows GitHub Actions validation for Debug and Release restore, build, and test. Split the generator monolith into focused shared and per-target source files without changing deterministic generated behavior, and move Binder from Parser.cs into its own compiler phase file.

Add the 128-level IF nesting safety limit and SMILE1416 recovery so pathological source cannot stack-overflow the compiler or Desktop editor. Add direct source regressions for function-shaped IF and ELSE IF conditions while preserving the permanent call-free rule and without introducing function grammar.

Keep all ten destination generators, branch-aware Known/Unknown analysis, exact String/NUL behavior, signed Integer planning, short-circuiting, target collision safety, cumulative language reference behavior, and Desktop responsiveness intact.

Validation: <insert exact final build, test, strict all-target, warning, runtime, and Desktop results>.
```

Do not copy placeholder validation text into the final commit. Replace it with the exact results actually obtained.

## Version tag

Inspect the repository's existing tag convention.

- If the project already uses version tags consistently, create an annotated tag:

```text
v0.6.0.1
```

and push that tag after the release commit.

- If the repository has no established version-tag practice, do not invent one silently. Leave the release commit and version metadata as the stable baseline and report that no tag was created.

Do not create a GitHub Release unless Sin explicitly asks for one.

---

# 14. Completion report to Sin

When finished, report:

- final commit SHA;
- whether it was pushed;
- whether a version tag was created;
- files moved or split;
- CI workflow path;
- new diagnostic and nesting limit;
- exact Debug build result;
- exact Release build result;
- exact Debug test count;
- exact Release test count;
- exact strict all-ten-target results;
- generated-warning result;
- official IF acceptance result;
- cumulative language reference result;
- Desktop smoke-test result;
- any remaining known limitation.

Highlight these completed items as ready for testing:

- **GitHub Actions CI**
- **Generator file split**
- **Binder file split**
- **IF nesting safety and SMILE1416**
- **Function-shaped IF source regressions**
- **SMILE v0.6.0.1 IF Hardening**
