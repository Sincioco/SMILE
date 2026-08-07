# Codex Implementation Instructions — SMILE v0.8.0 WHILE Loops

## Repository and workflow

- Repository: `Sincioco/SMILE`
- Work directly on `main`.
- Sin is the only developer.
- Do not create or suggest a feature branch.
- Do not open a pull request.
- Re-read `AGENTS.md` before changing code.
- Inspect the newest `main` commit, working tree, current official specifications, current roadmap, Desktop state-management rules, and current `SMILE CI` workflow before editing.
- Do not discard, reset, overwrite, or commit unrelated work.
- Do not force-push or rewrite published history.
- Follow KISS and KISS v2, “The Sin Way.”
- Preserve the permanent ten-target destination-language freeze.
- Commit all intended changes and push only after required validation is green.
- After pushing, verify that the `SMILE CI` run for the exact final `main` SHA completes successfully before reporting completion.

The last reviewed public baseline before the target-editor hardening task was:

```text
ac9fbe19ada91c8d2eeb52f60283e4a50f4684aa
Sin and Codex: Make target editors interactive
```

This WHILE brief is intended to run **after** SMILE v0.7.0.1 Target Editor Hardening is complete.

Do not assume the reviewed SHA is still current.

Begin from the newest `main`, and preserve all accepted v0.7.0.1 behavior, including:

- independent duplicate-language pane builds;
- latest-target-edit-wins generation ordering;
- later-SMILE-edit authority;
- target-pane edited markers;
- New race protection;
- Maximize/Restore state preservation;
- exact-SHA post-push CI verification.

---

# 1. Companion official specification

Use the complete companion document:

```text
009 - SMILE - WHILE Statement Official Specification v1.0.md
```

Publish it at:

```text
docs/SMILE Language Specification/009 - SMILE - WHILE Statement Official Specification v1.0.md
```

The numeric specification prefixes are intentional repository organization chosen by Sin.

Do not rename or renumber specifications `001` through `008`.

The implementation must conform to specification `009`.

Do not silently change the language design while coding.

---

# 2. Milestone

Create:

> **SMILE v0.8.0 — WHILE Loops**

Implement the canonical block:

```smile
WHILE condition
    statements
END WHILE
```

WHILE is:

- case-insensitive;
- pre-test;
- block-only;
- terminated by two-keyword `END WHILE`;
- governed by the same explicit, call-free condition rules as IF;
- allowed to execute zero or more times;
- preserved as genuine runtime control flow across all ten targets.

---

# 3. Permanent syntax decisions

Implement exactly these rules:

- `WHILE` requires at least one space or tab before its condition.
- The header ends after the condition.
- No `THEN`.
- No `DO`.
- No one-line body.
- `END WHILE` is mandatory.
- `END WHILE` must stand alone.
- `WEND`, `ENDWHILE`, and `LOOP` are not aliases.
- `WHILE` becomes reserved.
- `BREAK` and `CONTINUE` are not implemented or reserved.
- The body may contain PRINT, SET, INPUT, IF, nested WHILE, comments, blank lines, and SET Block String Literals.
- LET remains prohibited anywhere inside a WHILE body until scopes are formally introduced.
- Conditions use explicit comparison leaves and cannot invoke functions or procedures.
- INPUT is a statement and cannot appear inside a condition.
- Comments and blank lines remain ordered non-semantic items.
- Marker-looking text inside SET Block Strings remains String data.

---

# 4. Non-goals

Do not add:

- `FOR`;
- `NEXT`;
- `DO`;
- `LOOP`;
- `REPEAT`;
- `UNTIL`;
- `WEND`;
- `BREAK`;
- `CONTINUE`;
- `EXIT WHILE`;
- one-line WHILE;
- block-local LET;
- lexical scopes;
- functions;
- procedures;
- arrays;
- floating-point or decimal values;
- exceptions;
- another destination language;
- an implicit iteration cap;
- loop unrolling;
- a parser generator;
- an external compiler framework;
- an unnecessary dependency.

Do not weaken current INPUT, IF, arithmetic, String, comment, or source-layout semantics.

---

# 5. Inspect and preserve the current architecture

Inspect at minimum:

```text
AGENTS.md
README.md
docs/Roadmap.md
docs/Architecture.md
docs/Toolchains.md
docs/SMILE Target Code Generation Standard v1.0.md

docs/SMILE Language Specification/
    001 through 008

examples/language.smile
examples/input.smile

src/SMILE.Engine/Language.cs
src/SMILE.Engine/SyntaxKind.cs
src/SMILE.Engine/Lexer.cs
src/SMILE.Engine/Parser.cs
src/SMILE.Engine/Binder.cs
src/SMILE.Engine/Evaluation.cs
src/SMILE.Engine/ExecutionTrace.cs
src/SMILE.Engine/Analysis.cs
src/SMILE.Engine/Generation.cs
src/SMILE.Engine/Generation/

src/SMILE.Toolchains/ProcessRunner.cs
src/SMILE.Toolchains/Toolchains.cs

src/SMILE.Cli/
src/SMILE.Desktop/
src/SMILE.Desktop/Highlighting/SMILE.xshd

tests/SMILE.Tests/
.github/workflows/smile-ci.yml
```

Preserve:

- ordered syntax and bound source items;
- branch-aware Known/Unknown analysis;
- strict runtime INPUT behavior;
- checked signed 64-bit arithmetic;
- exact String and embedded-NUL behavior;
- deterministic target code;
- zero generated warnings;
- first-paint Desktop startup;
- live visible-target transpilation;
- target-editor hardening;
- process cancellation and process-tree termination;
- all ten targets.

---

# 6. Task 1 — Add WHILE token and syntax

Add:

```csharp
WhileKeyword
```

to the syntax-kind and keyword model.

Add a canonical syntax node equivalent to:

```csharp
public sealed record WhileStatementSyntax : StatementSyntax
{
    public WhileStatementSyntax(
        ExpressionSyntax condition,
        IReadOnlyList<SourceItemSyntax> sourceItems,
        TextSpan span)
        : base(span)
    {
        Condition = condition;
        SourceItems = sourceItems;
        Statements = sourceItems.OfType<StatementSyntax>().ToArray();
    }

    public ExpressionSyntax Condition { get; }

    public IReadOnlyList<SourceItemSyntax> SourceItems { get; }

    public IReadOnlyList<StatementSyntax> Statements { get; }
}
```

The exact style should match the existing IF syntax records.

Do not represent WHILE as IF plus a jump in the syntax tree.

Do not create separate syntax forms for known-true, known-false, empty, or nested loops.

---

# 7. Task 2 — Parse WHILE headers

Parse:

```text
WHILE hspace+ condition hspace* line-end
```

Required valid cases:

```smile
WHILE Count < 5
WHILE	Count < 5
while Count < 5
WHILE (Count < 5)
WHILE Count < 5 AND Continue = TRUE
```

Required invalid cases:

```smile
WHILE
WHILE(Count < 5)
WHILE Count < 5 THEN
WHILE Count < 5 DO
WHILE Count < 5 extra
```

Implement official diagnostics:

| Code | Meaning |
|---|---|
| `SMILE1601` | WHILE must be followed by whitespace |
| `SMILE1602` | WHILE requires a condition |
| `SMILE1606` | Unexpected content follows the WHILE condition |

Ordinary expression diagnostics remain applicable inside malformed expressions.

`WHILECount` remains an ordinary unknown statement or identifier-shaped word.

---

# 8. Task 3 — Parse END WHILE

Recognize only:

```smile
END WHILE
```

with:

- spaces or tabs between the keywords;
- optional trailing horizontal whitespace;
- line ending or EOF after the line.

Add diagnostics:

| Code | Meaning |
|---|---|
| `SMILE1607` | WHILE requires a matching END WHILE |
| `SMILE1608` | END WHILE must contain two keywords and stand alone |
| `SMILE1609` | END WHILE has no matching WHILE |

Do not accept:

```text
ENDWHILE
WEND
LOOP
END WHILE extra
```

`END IF` must not close WHILE.

`END WHILE` must not close IF.

---

# 9. Task 4 — Generalize block terminator classification

The parser currently handles IF-related terminators.

Extend the statement-list machinery so it can distinguish:

```text
ELSE IF
ELSE
END IF
END WHILE
```

without ad hoc duplicated scans.

A small internal terminator classification enum is appropriate.

The parser must:

- stop a WHILE body only at its matching END WHILE;
- allow nested IF and WHILE;
- report misplaced terminators;
- preserve same-line ELSE IF behavior;
- ignore comments;
- skip complete SET Block String content;
- preserve blank-line items;
- recover deterministically from malformed endings.

Do not turn the parser into a general parser-generator framework.

---

# 10. Task 5 — Use one combined control-flow nesting depth

Replace IF-only recursive depth tracking with one shared control-flow block depth covering:

```text
IF
WHILE
```

Use the existing limit:

```text
128
```

Required behavior:

- depth 128 succeeds;
- the 129th IF reports `SMILE1416`;
- the 129th WHILE reports `SMILE1611`;
- alternating IF/WHILE cannot bypass the limit;
- parser recovery does not recurse into the rejected block;
- 1,000-level mixed input does not stack-overflow or hang.

Keep opener-specific spans.

Update the IF specification wording so `SMILE1416` refers to the shared IF/WHILE depth when the rejected opener is IF.

Do not renumber existing IF diagnostics.

---

# 11. Task 6 — Mixed-block iterative recovery

Extend bounded iterative recovery to understand mixed IF and WHILE structure.

It must correctly balance:

```text
IF ... THEN
ELSE IF ... THEN
ELSE
END IF
WHILE ...
END WHILE
```

It must ignore structural-looking text inside:

- full-line comments;
- ordinary Strings;
- interpolated Strings where relevant to header scanning;
- canonical SET Block String Literals;
- malformed-but-recoverable block String spans handled by the current parser policy.

Required stress cases:

- 1,000 nested WHILE blocks;
- alternating IF/WHILE depth 1,000;
- comments spelling END WHILE;
- Block Strings spelling WHILE and END WHILE;
- mismatched END IF and END WHILE;
- later recoverable top-level source.

Avoid a diagnostic storm.

---

# 12. Task 7 — Reuse condition validation

Do not fork IF condition semantics.

Extract or generalize the current condition validator so IF and WHILE share:

- complete Boolean requirement;
- explicit comparison leaves;
- call-free traversal;
- AND/OR/NOT/parentheses handling;
- future-expression fail-closed behavior.

Allow the validator to receive a small diagnostic profile or context so WHILE emits:

| Code | Meaning |
|---|---|
| `SMILE1603` | Every atomic WHILE condition must be an explicit comparison |
| `SMILE1604` | The complete WHILE condition must have type Boolean |
| `SMILE1605` | A WHILE condition cannot invoke a function or procedure |

Preserve all existing IF codes and wording.

Do not validate a simplified expression; validate the unsimplified bound condition so explicit comparisons remain visible.

---

# 13. Task 8 — Add the bound WHILE node

Add a canonical node equivalent to:

```csharp
public sealed record BoundWhileStatement : BoundStatement
{
    public BoundWhileStatement(
        BoundExpression condition,
        IReadOnlyList<BoundSourceItem> sourceItems)
    {
        Condition = condition;
        SourceItems = sourceItems;
        Statements = sourceItems.OfType<BoundStatement>().ToArray();
    }

    public BoundExpression Condition { get; }

    public IReadOnlyList<BoundSourceItem> SourceItems { get; }

    public IReadOnlyList<BoundStatement> Statements { get; }
}
```

Do not represent WHILE as a bound IF plus a synthetic recursive call.

Keep comments and blank lines in `SourceItems`.

---

# 14. Task 9 — Bind WHILE bodies once

Bind every body statement exactly once.

Binding must:

- resolve all names;
- type-check all statements;
- validate the condition;
- validate nested blocks;
- retain comments and blank lines;
- reject undefined variables;
- reject invalid SET types;
- reject invalid INPUT targets;
- reject source-known arithmetic failures according to existing rules;
- generate no runtime iteration during binding.

A known-false condition does not make body source semantically unchecked.

---

# 15. Task 10 — Prohibit LET inside WHILE

Report:

```text
SMILE1610
LET is not permitted inside WHILE v1.0.
```

The prohibition applies recursively to any LET lexically contained inside the WHILE body.

Examples that must fail:

```smile
WHILE Count < 5
    LET X = 1
END WHILE
```

```smile
WHILE Count < 5
    IF Ready = TRUE THEN
        LET X = 1
    END IF
END WHILE
```

```smile
WHILE Count < 5
    WHILE Other < 5
        LET X = 1
    END WHILE
END WHILE
```

Use the LET keyword span.

Do not introduce scopes.

---

# 16. Task 11 — Keep binding independent of concrete loop execution

The binder must not execute WHILE to decide whether binding succeeds.

Do not:

- simulate iterations;
- use a made-up maximum iteration count;
- require termination;
- invent input values;
- unroll a source-known loop;
- reject an infinite loop merely because it is infinite.

Successful binding is based on syntax, names, types, static validity, and the bounded-String rule.

---

# 17. Task 12 — Add loop facts to BoundProgramAnalysis

Add WHILE-specific analysis facts, for example:

```csharp
public sealed record BoundWhileStatementAnalysis(
    int Ordinal,
    IReadOnlyDictionary<VariableSymbol, AnalyzedValue> ValuesAtHead,
    IReadOnlyDictionary<VariableSymbol, AnalyzedValue> ValuesAfter,
    IReadOnlyDictionary<VariableSymbol, SmileValue> ConcreteValuesAtHead,
    bool IncomingConditionIsKnownFalse);
```

The exact record may differ.

Add deterministic loop ordinals:

```text
GetWhileOrdinal
```

Keep IF ordinals stable.

`EnumerateStatements()` must include each source statement exactly once, regardless of how many abstract fixed-point passes are required.

---

# 18. Task 13 — Implement a two-phase fixed-point analysis

Do not record global statement facts repeatedly while solving the loop.

Use two conceptual phases:

## Phase A — Solve the loop-head environment

Starting from the incoming environment:

1. include the zero-iteration incoming state;
2. analyze one abstract body transfer;
3. merge body outgoing state back into the loop head;
4. repeat until stable under the required widening rules.

This phase must use a pure or isolated transfer operation that does not:

- append duplicate statements;
- consume global ordinals repeatedly;
- duplicate assigned-value records;
- duplicate mutation records;
- mutate final analyzer output on every trial pass.

## Phase B — Record body facts once

After the loop-head environment is stable:

1. analyze the source body once using the stable head facts;
2. record each statement and nested block once;
3. compute the post-loop merge;
4. expose facts to generators and tests.

This separation is mandatory for deterministic statement identity and performance.

---

# 19. Task 14 — Fixed-point lattice rules

At minimum, define monotone merge/widening for:

## Known values

```text
Known(v) merged with Known(v) -> Known(v)
Known(v) merged with Known(other) -> Unknown
Known merged with Unknown -> Unknown
```

This domain stabilizes quickly.

## Boolean possible values

Use exact:

```text
FALSE
TRUE
FALSE or TRUE
```

## Exact candidate sets

Keep the current finite candidate policy where it remains linear.

When loop recurrence would repeatedly expand the candidate set:

- mark it inexact;
- preserve type, range, String size, and NUL facts.

Do not perform Cartesian explosion.

## Integer ranges

Merge incoming and body ranges.

When a repeated pass expands a lower bound again, widen it to:

```text
long.MinValue
```

When a repeated pass expands an upper bound again, widen it to:

```text
long.MaxValue
```

Use a deterministic widening strategy that converges in a small number of passes.

## String facts

Merge:

- finite maximum UTF-8 length;
- may-contain-NUL;
- exact versus inexact candidates.

If the loop-carried maximum continues increasing when the body transfer is reapplied, mark it unbounded for semantic validation rather than allowing an integer size to grow forever.

---

# 20. Task 15 — Known-false special case

If the incoming condition is statically known to be FALSE and evaluating it cannot fail:

- post-loop abstract and concrete environments may remain equal to incoming;
- the body remains fully bound, validated, analyzed for storage safety, and generated;
- statement facts remain available;
- the loop is not deleted from target output.

Add direct tests.

Do not use this special case when the condition is Unknown or may fail at runtime.

---

# 21. Task 16 — Conservative post-loop merge

Unless the incoming condition is proved FALSE, model zero or more iterations.

The post-loop state must include:

- the zero-iteration incoming path;
- one-or-more-iteration states from the stable loop transfer.

A value after WHILE is Known only if all represented exit states agree.

Do not assume at least one iteration merely because the condition currently appears true under one concrete environment.

Do not use a selected concrete reference trace to justify folding later code.

---

# 22. Task 17 — Concrete-value handling

The existing analysis carries optional concrete facts for useful source-known cases.

For WHILE:

- preserve concrete values when the incoming condition is known FALSE;
- otherwise remove or conservatively merge concrete values affected by the loop;
- do not run the loop in the compiler to discover a final concrete value;
- do not hang on `WHILE TRUE = TRUE`;
- do not infer a trip count.

The reference evaluator, not the compiler analysis, executes actual iterations.

---

# 23. Task 18 — Add portable bounded-String validation

Implement the official v1.0 rule:

> Every String value assigned through a WHILE loop must have a finite compile-time maximum UTF-8 byte length under zero-or-more-iteration analysis.

Add:

```text
SMILE1612
A WHILE loop produces a String value without a finite compile-time UTF-8 size bound.
```

Report the diagnostic on the WHILE keyword span.

Required invalid example:

```smile
LET Text = ""
LET Continue = TRUE

WHILE Continue = TRUE
    SET Text = Text + "x"
    INPUT Continue
END WHILE
```

Required valid examples:

```smile
SET Text = "Fixed"
```

```smile
INPUT Text
```

```smile
SET Text = OtherBoundedText
```

```smile
SET Text = Text + ""
```

Analyze the complete body transfer.

Do not reject solely by textual self-reference.

Do not add a new arbitrary global String-size limit.

Do not implement dynamic unbounded String allocation in this milestone.

---

# 24. Task 19 — No WHILE trip-count proof

Do not add induction-variable or symbolic trip-count analysis merely to accept:

```smile
WHILE Count < 5
    SET Text = Text + "x"
    SET Count = Count + 1
END WHILE
```

The official v1.0 analysis treats WHILE as zero or more iterations.

Reject unbounded String recurrence with `SMILE1612`.

This keeps all ten targets deterministic and avoids target-specific heap models.

---

# 25. Task 20 — Update expression and storage facts

Ensure loop-fixed-point facts feed:

- `TargetIntegerProfile`;
- String storage lengths;
- runtime String buffers;
- exact logical lengths;
- embedded-NUL planning;
- interpolation maximum sizes;
- comparison lowering;
- PRINT lowering;
- runtime checked arithmetic;
- target helper selection.

A variable mutated inside a WHILE must not retain a stale narrow Integer profile or stale String length from its LET initializer.

---

# 26. Task 21 — Update BoundProgramSimplifier

Add recursive WHILE support.

The simplifier must:

- preserve the WHILE node;
- preserve its condition;
- preserve every body statement;
- preserve comments and blank lines;
- simplify expressions only from facts valid at the stable loop head;
- remove known-value assumptions after loop according to analysis;
- never unroll the loop;
- never delete the loop;
- never duplicate INPUT;
- never move SET or INPUT across the condition;
- preserve short-circuit behavior.

A known-false loop remains in generated output.

---

# 27. Task 22 — Update statement-tree traversal

Update every recursive statement helper, including:

- `BoundStatementTree`;
- generator feature scans;
- runtime-helper scans;
- identifier scans;
- mutation scans;
- input detection;
- warning hygiene scans;
- cumulative program analysis.

WHILE bodies must be traversed once structurally.

Do not accidentally omit nested INPUT or checked arithmetic helpers.

Do not recursively traverse runtime iterations.

---

# 28. Task 23 — Reference evaluator execution

Add evaluator handling equivalent to:

```csharp
while (EvaluateCondition())
{
    ExecuteStatements(body);
}
```

Preserve:

- pre-test behavior;
- current variable environment;
- SET atomicity;
- INPUT atomicity;
- short-circuiting;
- runtime arithmetic errors;
- stdout already produced;
- canonical stderr and exit code;
- nested loops;
- IF inside loop;
- loop inside IF.

---

# 29. Task 24 — Add evaluator cancellation

Add cancellation-capable overloads without breaking existing callers.

Suitable forms include:

```csharp
Evaluate(string source, CancellationToken cancellationToken)
Evaluate(string source, string scriptedInput, CancellationToken cancellationToken)
Evaluate(string source, TextReader input, CancellationToken cancellationToken)
Evaluate(string source, Stream input, CancellationToken cancellationToken)
```

Check cancellation:

- before each WHILE condition evaluation;
- before or between body statements;
- in nested control flow.

Cancellation is host control, not a SMILE runtime error.

Propagate `OperationCanceledException` or the established host cancellation signal.

Do not add an invisible loop-iteration cap.

---

# 30. Task 25 — Execution trace architecture

Do not make `BoundProgramExecutionTrace` execute an arbitrary WHILE to produce compile-time facts.

Choose the smallest safe design:

- make concrete loop facts optional/unknown;
- or stop using the exact trace for loop-dependent compiler decisions;
- or represent WHILE as one structural trace step without expanding iterations.

Required:

- no compiler hang;
- no unbounded trace growth;
- no fake final values;
- no generator dependence on simulated loop output;
- existing non-loop trace tests remain valid.

Document the decision in `docs/Architecture.md`.

---

# 31. Task 26 — C# generation

Generate genuine C#:

```csharp
while (condition)
{
    ...
}
```

Requirements:

- re-evaluate condition every iteration;
- use current storage;
- preserve checked runtime Integer operations;
- preserve input helpers;
- preserve comments and blank lines;
- avoid constant-condition warnings under the generated-warning gate;
- preserve empty body correctly.

If a warning-safe condition wrapper is required for a constant condition, use the existing shared strategy rather than changing semantics.

---

# 32. Task 27 — C generation

Generate:

```c
while (condition)
{
    ...
}
```

Requirements:

- exact pre-test semantics;
- checked signed 64-bit arithmetic;
- current String pointer and logical length reads;
- existing bounded buffers;
- exact NUL behavior;
- warning-safe constant conditions;
- no unbounded String program reaches generation;
- no target compiler warnings.

---

# 33. Task 28 — C++ generation

Generate modern C++20:

```cpp
while (condition)
{
    ...
}
```

Preserve:

- `std::string`;
- complete embedded-NUL behavior;
- checked `std::int64_t` helpers;
- source layout;
- deterministic helper emission;
- no unrolling.

---

# 34. Task 29 — JavaScript generation

Generate:

```javascript
while (condition) {
    ...
}
```

Requirements:

- use current `BigInt` values for INPUT-dependent Integers;
- preserve signed 64-bit checked helper behavior;
- preserve short-circuiting;
- no Number approximation;
- exact input sequence;
- exact runtime errors.

---

# 35. Task 30 — Java generation

Generate:

```java
while (condition) {
    ...
}
```

Preserve:

- `long` runtime values;
- exact checked arithmetic;
- current variable reads;
- input helpers;
- warning-free compilation;
- source comments and blanks.

---

# 36. Task 31 — Objective-C generation

Generate C-compatible Objective-C loop syntax.

Reuse the proven C-family expression and runtime helpers where appropriate.

Preserve pointer-plus-length String semantics and exact NUL handling.

Do not introduce Foundation solely for WHILE.

---

# 37. Task 32 — Swift generation

Generate:

```swift
while condition {
    ...
}
```

Preserve:

- `Int64` where loop analysis requires it;
- overflow-reporting operations;
- runtime input;
- current mutable storage;
- layout;
- exact runtime errors.

---

# 38. Task 33 — Python generation

Generate:

```python
while condition:
    ...
```

When the semantic body is empty, append:

```python
pass
```

after preserved comments and blank lines.

Preserve:

- signed-64 validation;
- checked helper calls;
- input behavior;
- short-circuiting;
- source layout;
- deterministic output.

---

# 39. Task 34 — COBOL generation

Use structured GnuCOBOL-compatible control flow.

The condition must be recomputed before every possible SMILE body execution.

A valid strategy is conceptually:

```cobol
PERFORM UNTIL SMILE-WHILE-EXIT = 1
    *> recompute the complete condition
    IF condition-is-false
        MOVE 1 TO SMILE-WHILE-EXIT
    ELSE
        *> body
    END-IF
END-PERFORM
```

Other structured lowering is acceptable.

Requirements:

- pre-test semantics;
- zero iterations;
- condition re-evaluation;
- current storage;
- exact input;
- checked arithmetic;
- empty-body `CONTINUE` when needed;
- deterministic loop fields/labels;
- comments and blanks;
- no compiler warnings;
- no unbounded String program reaches generation.

Do not accidentally evaluate the body once when the condition is initially false.

---

# 40. Task 35 — MASM x64 generation

Generate deterministic compare/jump control flow equivalent to:

```text
while_N_condition:
    evaluate condition
    jump false to while_N_end

while_N_body:
    body
    jump while_N_condition

while_N_end:
```

Requirements:

- collision-safe deterministic labels;
- nested IF/WHILE labels do not collide;
- condition re-evaluated;
- current pointer/length storage;
- checked overflow and division;
- exact INPUT behavior;
- empty loop body;
- no compiler-owned fixed iteration count.

---

# 41. Task 36 — Target condition facts

Existing IF generators may use clause facts.

Add WHILE-head facts so every target expression writer receives values valid for any iteration, not merely the first.

Do not write a condition or body expression from pre-loop `Known` facts after the fixed point made it Unknown.

This is critical for:

```smile
LET Count = 0

WHILE Count < 3
    PRINT {Count}
    SET Count = Count + 1
END WHILE
```

Generated PRINT must read current runtime `Count`.

---

# 42. Task 37 — Runtime failure reachability

Preserve current compile-time versus runtime rules.

Examples:

## Source-known failure

```smile
LET Value = 1 / 0
```

remains compile-time `SMILE1207`.

## Loop-carried runtime failure

```smile
LET Divisor = 1
LET Continue = TRUE

WHILE Continue = TRUE
    INPUT Divisor
    PRINT {10 / Divisor}
    INPUT Continue
END WHILE
```

Input `0` produces runtime `SMILER1207`.

## Short-circuit suppression

```smile
LET Divisor = 0

WHILE FALSE = TRUE AND (1 / Divisor = 0)
END WHILE
```

The right side is not evaluated at runtime.

Keep binding and type checking of both sides.

---

# 43. Task 38 — Process and Desktop safety

WHILE makes long-running and infinite generated programs normal possibilities.

Preserve and test:

- captured-process timeout;
- process-tree kill;
- cancellation;
- bounded stdout/stderr capture;
- interactive inherited console;
- Desktop Cancel behavior;
- WPF responsiveness;
- no compiler process waiting for stdin;
- no live transpilation running target code.

Do not shorten current timeouts merely because loops exist.

Do not impose an evaluator iteration cap.

---

# 44. Task 39 — Target-editor hardening integration

Preserve accepted v0.7.0.1 behavior.

WHILE live generation must respect:

- per-pane user-edit revisions;
- latest target edit winning over older in-flight generation;
- later SMILE edit reasserting authority;
- duplicate-language visible pane builds;
- edited `*` marker;
- independent current target primary sources;
- generated companion files;
- INPUT metadata;
- New and Maximize/Restore behavior.

Add at least one Desktop regression where a WHILE source edit triggers generation while a newer target-pane edit occurs.

---

# 45. Task 40 — Syntax highlighting

Add `WHILE` to the SMILE keyword highlighting definition.

`END WHILE` should visibly highlight both keywords according to the existing teaching palette.

Preserve:

- comments green;
- keywords blue;
- learner identifiers black;
- Strings red;
- numbers dark blue;
- no purple-family colors;
- Block String ownership;
- comment ownership;
- INPUT and IF highlighting.

Test:

- every WHILE casing;
- END WHILE;
- WHILE text inside comments;
- WHILE text inside ordinary and Block Strings;
- malformed/incomplete WHILE while typing.

---

# 46. Task 41 — Cumulative language reference

Extend:

```text
examples/language.smile
```

Do not replace or remove prior LET, PRINT, SET, Block String, IF, comment/layout, or INPUT sections.

Add a finite WHILE section such as:

```smile
LET LoopCount = 1

WHILE LoopCount <= 3
    PRINT LoopCount={LoopCount}
    SET LoopCount = LoopCount + 1
END WHILE
```

Keep cumulative scripted INPUT deterministic.

Update expected output in all cumulative tests.

---

# 47. Task 42 — Focused WHILE example

Add:

```text
examples/while.smile
```

Include:

- a counter loop;
- INPUT-driven runtime bound;
- IF inside WHILE;
- comments;
- blank lines;
- a nested loop;
- no unbounded String growth.

Package it beside the Desktop executable and in publish output according to current example conventions.

---

# 48. Task 43 — Documentation and version identity

Update identity to:

```text
0.8.0 WHILE Loops
```

Update applicable files:

- Desktop project metadata;
- About dialog;
- README;
- roadmap;
- architecture;
- toolchain guidance;
- target-generation standard;
- high-level language specification;
- AGENTS permanent rules;
- requirements/progress history;
- official specification index and cross-links;
- IF specification combined nesting wording;
- INPUT specification allowed-context wording if needed;
- comment/layout specification body-context wording if needed.

Add:

```text
009 - SMILE - WHILE Statement Official Specification v1.0.md
```

Do not renumber prior specifications.

---

# 49. Task 44 — Parser conformance tests

Add a focused file such as:

```text
tests/SMILE.Tests/WhileStatementConformanceTests.cs
```

Required coverage:

- canonical syntax;
- all keyword casing combinations;
- whitespace and tabs;
- missing whitespace;
- missing condition;
- non-Boolean condition;
- implicit Boolean condition;
- compound explicit conditions;
- function-shaped invalid conditions;
- THEN and DO rejection;
- mandatory END WHILE;
- ENDWHILE, WEND, LOOP rejection;
- stray END WHILE;
- mismatched END IF;
- empty body;
- layout-only body;
- nested WHILE;
- IF/WHILE nesting;
- LET prohibition;
- comments;
- Block Strings;
- exact diagnostics and spans;
- EOF terminator behavior.

---

# 50. Task 45 — Depth and recovery tests

Generate source programmatically.

Test:

- one WHILE;
- depth 128 WHILE;
- depth 129 WHILE -> `SMILE1611`;
- 1,000 WHILE blocks without stack overflow;
- alternating IF/WHILE depth 128 succeeds;
- alternating depth 129 reports opener-specific code;
- comments spelling terminators;
- Block String terminator text;
- malformed mixed endings;
- later top-level source recovery;
- bounded diagnostic count;
- Desktop live path remains responsive.

---

# 51. Task 46 — Evaluator tests

Test:

- zero iterations;
- one iteration;
- multiple iterations;
- condition mutation;
- nested loops;
- IF inside WHILE;
- WHILE inside IF;
- INPUT-driven count;
- repeated INPUT;
- unselected body INPUT consumes no line;
- runtime errors in condition;
- runtime errors in body;
- short-circuiting;
- stdout before error;
- exact stderr and exit code;
- embedded NUL String comparisons/prints in bounded loops;
- cancellation of an infinite loop.

Do not use uncontrolled infinite test execution.

---

# 52. Task 47 — Analysis tests

Add focused fixed-point tests for:

- Known unchanged value;
- Unknown zero-or-more mutation;
- known-false incoming condition;
- Boolean loop variable;
- full-range Integer widening;
- monotonic Count increment;
- decrement;
- nested loops;
- INPUT in body;
- mutation inside IF in body;
- statement ordinals recorded once;
- deterministic WHILE ordinals;
- exact candidate widening;
- embedded-NUL propagation;
- finite String bounds;
- unbounded String diagnostic;
- no analysis hang.

Verify generators receive stable loop-head facts.

---

# 53. Task 48 — Bounded-String tests

Required valid cases:

```smile
SET Text = "Fixed"
```

```smile
INPUT Text
```

```smile
SET Text = Other
```

```smile
SET Text = Text
```

```smile
SET Text = Text + ""
```

Required invalid cases:

```smile
SET Text = Text + "x"
```

```smile
SET A = B + "x"
SET B = A
```

Test nested IF paths and nested WHILE.

Assert `SMILE1612` on the WHILE opener.

No generator should receive an invalid unbounded program.

---

# 54. Task 49 — Simplifier and authenticity tests

Prove:

- loop remains when condition known false;
- loop remains when condition known true;
- body remains;
- INPUT is not duplicated;
- SET is not hoisted;
- condition uses current storage;
- PRINT uses current loop-carried storage;
- all source branches inside body remain;
- repeated generation is byte-identical;
- MASM labels are deterministic;
- no generator unrolls.

---

# 55. Task 50 — All-ten-target structural tests

For each target, assert the expected genuine loop structure.

At minimum:

- C# `while`;
- C `while`;
- C++ `while`;
- JavaScript `while`;
- Java `while`;
- Objective-C `while`;
- Swift `while`;
- Python `while`;
- COBOL structured `PERFORM`;
- MASM back-edge and exit labels.

Also test:

- nested loop structure;
- zero-iteration source still contains loop;
- empty body placeholders;
- comments and blank lines;
- INPUT inside loop;
- checked runtime arithmetic helpers;
- collision-safe identifiers.

---

# 56. Task 51 — Normative all-target runtime test

Use the acceptance program from specification `009`.

Scripted input:

```text
3
```

For every target:

1. transpile;
2. build or run;
3. provide scripted stdin directly, not through shell echo;
4. capture exact stdout;
5. capture exact stderr;
6. capture exit code;
7. compare with `SmileEvaluator`;
8. require zero generated compiler warnings.

Require all ten targets under strict gates.

---

# 57. Task 52 — Additional runtime corpus

Run a focused finite corpus across all ten targets:

- zero iterations;
- one iteration;
- 10 iterations;
- nested 2x2 loop;
- INPUT Boolean exit;
- IF mutation inside loop;
- negative Integer countdown;
- truncating division;
- runtime overflow;
- runtime division by zero;
- bounded String INPUT and equality;
- layout/comment preservation.

Use deterministic inputs.

Do not use excessive iteration counts that make CI slow.

---

# 58. Task 53 — Infinite-loop safety tests

Test compiler phases on:

```smile
WHILE TRUE = TRUE
END WHILE
```

Require:

- parse completes;
- bind completes;
- analysis completes;
- simplification completes;
- all ten generation paths complete;
- output is deterministic.

Test evaluator with explicit cancellation.

Test one generated captured target with a short test-only cancellation or timeout and confirm process-tree termination.

Do not weaken production timeouts.

---

# 59. Task 54 — Generated warning hygiene

Preserve:

```text
SMILE_REQUIRE_ZERO_TARGET_WARNINGS=1
```

Test constant conditions:

```smile
WHILE TRUE = TRUE
END WHILE
```

```smile
WHILE FALSE = TRUE
END WHILE
```

Use target-safe lowering so compilers do not emit warnings that fail strict validation.

Do not remove the source loop merely to avoid a warning.

---

# 60. Required normal validation

Run from the actual repository root.

Examples use `D:\SMILE`.

```bat
cmd /c "cd /d D:\SMILE && dotnet restore SMILE.sln"
```

```bat
cmd /c "cd /d D:\SMILE && dotnet build SMILE.sln -c Debug --no-restore -nologo"
```

```bat
cmd /c "cd /d D:\SMILE && dotnet test SMILE.sln -c Debug --no-build --no-restore -nologo"
```

```bat
cmd /c "cd /d D:\SMILE && dotnet build SMILE.sln -c Release --no-restore -nologo"
```

```bat
cmd /c "cd /d D:\SMILE && dotnet test SMILE.sln -c Release --no-build --no-restore -nologo"
```

Required:

- zero build warnings;
- zero build errors;
- zero test failures;
- zero unexpected skips.

---

# 61. Required strict validation

```bat
cmd /c "cd /d D:\SMILE && set SMILE_REQUIRE_JAVA=1 && set SMILE_REQUIRE_ALL_TARGETS=1 && set SMILE_REQUIRE_ZERO_TARGET_WARNINGS=1 && dotnet test SMILE.sln -c Debug --no-build --no-restore -nologo"
```

```bat
cmd /c "cd /d D:\SMILE && set SMILE_REQUIRE_JAVA=1 && set SMILE_REQUIRE_ALL_TARGETS=1 && set SMILE_REQUIRE_ZERO_TARGET_WARNINGS=1 && dotnet test SMILE.sln -c Release --no-build --no-restore -nologo"
```

Both strict runs must require:

- Java;
- all ten targets;
- zero generated warnings;
- exact stdout;
- exact stderr;
- exact exit codes;
- scripted stdin;
- WHILE acceptance program;
- INPUT regressions;
- cumulative reference;
- no target skips.

---

# 62. CLI smoke test

Verify:

- transpile WHILE to all ten targets;
- run the normative finite program;
- scripted stdin works;
- exact output matches evaluator;
- a captured infinite loop can be cancelled or times out safely;
- no stale generated target is used;
- exit codes remain correct.

---

# 63. Desktop smoke test

Launch the real WPF app and verify:

1. first paint remains responsive;
2. cumulative source loads;
3. WHILE and both END/WHILE keywords highlight correctly;
4. a finite loop transpiles live;
5. all visible panes show real loop code;
6. comments and blank lines remain preserved;
7. target-editor `*` ordering rules remain intact;
8. duplicate-language pane builds remain independent;
9. an INPUT-driven WHILE launches one visible interactive console;
10. prompts appear before typing;
11. the WPF UI remains responsive while input is awaited;
12. Cancel can terminate a running or infinite child process;
13. Maximize/Restore preserves editor state;
14. New clears every editor;
15. representative C#, MASM, COBOL, Python, and JavaScript loops build/run;
16. About displays:

```text
0.8.0 WHILE Loops
```

---

# 64. Acceptance criteria

This work is complete only when all requirements below are true.

## Language

- canonical WHILE/END WHILE parses;
- pre-test semantics are correct;
- zero or more iterations work;
- condition rules match IF;
- no THEN or DO;
- LET is rejected inside WHILE;
- INPUT works in reached bodies;
- nested IF/WHILE works;
- unsupported aliases remain invalid;
- exact diagnostics are implemented.

## Safety

- combined IF/WHILE depth 128 succeeds;
- depth 129 reports opener-specific diagnostic;
- 1,000-level mixed source does not crash;
- infinite source does not hang compiler phases;
- evaluator supports cancellation;
- process-tree cancellation remains safe.

## Analysis

- fixed point terminates;
- statement facts are recorded once;
- values after loop are conservative;
- Integer ranges widen safely;
- INPUT remains Unknown;
- stale LET initializers are not propagated;
- finite String storage is preserved;
- unbounded String recurrence reports `SMILE1612`.

## Generation

- all ten targets emit genuine loops;
- conditions are re-evaluated;
- no unrolling or deletion;
- current runtime storage is read;
- exact String/NUL behavior remains;
- checked arithmetic remains;
- comments and blanks remain;
- deterministic labels/helpers remain;
- zero generated warnings remain.

## Tooling

- CLI finite and cancellation paths work;
- Desktop remains responsive;
- target-editor hardening remains intact;
- syntax highlighting works;
- examples are packaged;
- version/docs are synchronized.

## Release

- Debug and Release builds are clean;
- normal and strict suites pass;
- all ten targets execute under strict gates;
- docs identify `0.8.0 WHILE Loops`;
- specification `009` exists with the exact numbered name;
- changes are committed and pushed;
- exact final-main SHA has successful `SMILE CI`.

---

# 65. Commit message

Use a detailed public commit message similar to:

```text
Sin and Codex: Add WHILE loops

Release SMILE v0.8.0 WHILE Loops.

Add case-insensitive pre-test WHILE condition / END WHILE blocks with the same explicit-comparison and call-free condition rules as IF. Permit PRINT, SET, INPUT, IF, nested WHILE, comments, blank lines, and SET Block Strings while retaining the no-LET-before-scopes rule and a shared 128-level IF/WHILE nesting safety limit.

Introduce canonical syntax and bound nodes, cancellation-aware evaluator execution, deterministic loop ordinals, and zero-or-more-iteration fixed-point analysis with conservative Known/Unknown merging, Integer range widening, runtime-failure facts, and portable bounded-String validation.

Generate genuine warning-free loop control flow for all ten targets, including Python pass handling, structured GnuCOBOL PERFORM lowering, deterministic MASM labels, current runtime storage reads, checked signed-64 arithmetic, exact INPUT behavior, and preserved comments and blank lines.

Preserve SMILE v0.7.0.1 target-editor hardening, cumulative examples, responsive Desktop behavior, strict all-target conformance, and the exact-SHA CI completion gate.

Validation: <insert exact normal, strict, target, warning, CLI, and Desktop results>. Post-push SMILE CI: <insert exact final run ID and successful conclusion>.
```

Replace placeholders with actual results.

Commit all intended changes and push to `main`.

Do not create a Git tag or GitHub Release unless Sin explicitly asks or the repository later establishes that convention.

---

# 66. Mandatory post-push completion gate

After pushing:

1. read the exact final `main` SHA;
2. locate `SMILE CI` for that exact SHA;
3. wait for completion;
4. require conclusion `success`;
5. verify Restore, Debug Build/Test, and Release Build/Test succeeded.

Do not use an older run.

If CI fails:

- inspect the failed step and logs;
- fix the root cause;
- rerun applicable validation;
- create a normal follow-up commit;
- push;
- verify the replacement exact-SHA run.

Do not force-push or hide failed history.

---

# 67. Completion report to Sin

Report:

- final commit SHA;
- push status;
- version identity;
- files added and changed;
- specification path;
- syntax and bound WHILE nodes;
- shared condition validator changes;
- combined nesting implementation;
- parser recovery behavior;
- fixed-point and widening design;
- bounded-String validation;
- evaluator cancellation API;
- target-by-target loop lowering;
- target-editor hardening preservation;
- exact focused test counts;
- exact Debug build/test results;
- exact Release build/test results;
- exact strict Debug/Release results;
- all-ten-target results;
- generated-warning result;
- normative acceptance stdout/stderr/exit;
- infinite-loop cancellation result;
- cumulative example result;
- Desktop smoke result;
- GitHub Actions run ID and conclusion;
- whether a corrective follow-up commit was required;
- remaining known limitations.

Highlight these as ready for testing:

- **WHILE / END WHILE**
- **Runtime INPUT-driven loops**
- **Nested IF and WHILE**
- **Zero-or-more fixed-point analysis**
- **Checked loop-carried Integer arithmetic**
- **All-ten-target genuine loop generation**
- **Infinite-loop cancellation safety**
- **SMILE v0.8.0 WHILE Loops**

After this milestone is accepted, the next planned language-depth milestone remains:

> **SMILE v0.9.0 — Functions and scopes**
