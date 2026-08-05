# Codex Implementation Instructions — SMILE v0.6.0 IF / ELSE IF / ELSE

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
- Do not add `INPUT`, loops, functions, procedures, scopes, arrays, comments, floating-point values, assignment expressions, or another unrelated feature.
- Do not add a parser generator, compiler framework, CFG package, runtime framework, template engine, package manager, or unnecessary dependency.
- Preserve `examples/language.smile` as the cumulative language reference.
- Preserve first-paint Desktop startup, asynchronous language-file loading, visible-target-only live transpilation, cancellation, failure containment, deterministic generation, Java/all-target validation, and generated-warning validation.

The reviewed baseline when this brief was prepared was:

```text
e3b0873f6aaebcd4ce949b052347d48e8d24e78c
Sin and Codex: Eliminate generated self-assignment warnings
```

Do not assume that SHA is still current. Always start from the newest `main`.

---

# 1. Companion official specification

Use the complete companion file:

```text
SMILE - IF Statement Official Specification v1.0.md
```

Publish it at:

```text
docs/SMILE Language Specification/SMILE - IF Statement Official Specification v1.0.md
```

The implementation must conform to that specification.

Do not silently change the language design while coding.

---

# 2. Milestone

Create:

> **SMILE v0.6.0 — IF / ELSE IF / ELSE**

Implement:

```smile
IF condition THEN
    statements
ELSE IF condition THEN
    statements
ELSE
    statements
END IF
```

`ELSE IF` and `ELSE` are optional.

Multiple ELSE IF clauses are allowed.

One `END IF` closes the complete clause chain.

---

# 3. Three permanent SMILE IF rules

## A. Conditions cannot invoke functions or procedures

Every condition value must already exist without a call during condition evaluation.

Future valid:

```smile
LET Result = FUNC(A)

IF Result > 10 THEN
END IF
```

Future invalid:

```smile
IF FUNC(A) > 10 THEN
END IF
```

There is no call syntax yet. Design the condition validator so future call/invocation nodes are rejected by default.

## B. Every atomic condition requires explicit comparison

Valid:

```smile
IF Ready = TRUE THEN
END IF
```

Invalid:

```smile
IF Ready THEN
END IF
```

Invalid:

```smile
IF TRUE THEN
END IF
```

Valid compound condition:

```smile
IF Age >= 18 AND Ready = TRUE THEN
END IF
```

Invalid compound condition:

```smile
IF Age >= 18 AND Ready THEN
END IF
```

## C. ELSE IF is same-line syntax

This is one ELSE IF clause:

```smile
ELSE IF Score >= 80 THEN
```

This is a nested IF in an ELSE body:

```smile
ELSE
    IF Score >= 80 THEN
    END IF
END IF
```

Do not collapse newline-separated ELSE and IF.

---

# 4. IF v1.0 branch scope

Inside IF, ELSE IF, and ELSE, permit only:

- `PRINT`;
- `SET`;
- nested `IF`;
- blank lines;
- SET Block String Literals within SET.

Reject `LET` inside every IF-related body.

All variables must already be declared before entering the IF.

Do not introduce scopes in v0.6.0.

---

# 5. Keywords

Add case-insensitive keyword kinds:

```text
IfKeyword
ThenKeyword
ElseKeyword
EndKeyword
```

Do not add a combined `ElseIfKeyword`.

Do not accept `ELSEIF`.

`END IF` consists of `EndKeyword` and `IfKeyword`.

Update:

- lexer keyword lookup;
- reserved-keyword validation;
- syntax highlighting;
- keyword tests;
- documentation;
- cumulative examples.

---

# 6. Parser architecture

The current parser is line-oriented.

Evolve it into a small recursive block parser while retaining the existing source-line model.

Recommended structure:

```text
ParseProgram
    ParseStatementList(no terminators)

ParseIfStatement
    parse IF header
    ParseStatementList(until ELSE IF, ELSE, or END IF)
    parse zero or more ELSE IF clauses
    parse optional ELSE
    require END IF
```

A suitable internal shape:

```csharp
private IReadOnlyList<StatementSyntax> ParseStatementList(
    ref int lineIndex,
    StatementListTerminators terminators);
```

Do not introduce a parser generator.

Do not flatten nested IF using target-specific or ad hoc searches.

---

# 7. Terminator recognition

Before parsing an ordinary statement, recognize:

```text
ELSE IF ... THEN
ELSE
END IF
```

The nearest unmatched IF owns its terminators.

A nested IF consumes its own ELSE/END IF before returning to the outer parser.

Unexpected top-level ELSE, ELSE IF, or END IF must produce `SMILE1411`.

---

# 8. Header grammar

## IF

Require:

```text
IF
one or more spaces/tabs
condition
one or more spaces/tabs
THEN
optional spaces/tabs
end of physical line
```

## ELSE IF

Require:

```text
ELSE
one or more spaces/tabs
IF
one or more spaces/tabs
condition
one or more spaces/tabs
THEN
optional spaces/tabs
end of physical line
```

## ELSE

Require only:

```text
ELSE
optional spaces/tabs
end of line
```

## END IF

Require:

```text
END
one or more spaces/tabs
IF
optional spaces/tabs
line ending or EOF
```

Reject combined `ELSEIF` and `ENDIF`.

Reject trailing content after THEN or END IF.

---

# 9. Locate THEN lexically

Do not locate THEN with plain substring search.

Use token-aware scanning so `THEN` inside a String or interpolation is not treated as the header terminator.

Parse the condition between IF and the actual THEN token.

Apply the same behavior to ELSE IF.

---

# 10. Syntax model

Add a canonical syntax model.

Recommended:

```csharp
public sealed record ConditionalClauseSyntax(
    ExpressionSyntax Condition,
    IReadOnlyList<StatementSyntax> Statements,
    TextSpan Span);

public sealed record IfStatementSyntax(
    IReadOnlyList<ConditionalClauseSyntax> Clauses,
    IReadOnlyList<StatementSyntax> ElseStatements,
    TextSpan Span)
    : StatementSyntax(Span);
```

Rules:

- `Clauses[0]` is the initial IF;
- later entries are ELSE IF clauses;
- `ElseStatements` is empty when ELSE is absent;
- span extends through matching END IF.

---

# 11. Explicit-comparison condition validator

Create one shared validator, such as:

```text
IfConditionValidator
```

Run it on the unsimplified expression tree.

Conceptual algorithm:

```text
ValidateCondition(expression):
    logical AND:
        ValidateCondition(left)
        ValidateCondition(right)

    logical OR:
        ValidateCondition(left)
        ValidateCondition(right)

    logical NOT:
        ValidateCondition(operand)

    parentheses:
        ValidateCondition(inner)

    comparison (=, <>, <, <=, >, >=):
        validate both value operands contain no call
        accept as one atomic condition

    otherwise:
        SMILE1402
```

Every Boolean leaf must therefore be an explicit comparison.

Do not validate after simplification because simplification may erase `= TRUE`.

---

# 12. Call prohibition

Recursively inspect both operands of every atomic comparison.

Current v0.6 expressions contain no call node.

Still:

- reject any future call/invocation expression kind;
- make unknown future callable nodes fail closed;
- add a code comment identifying this permanent rule;
- add AGENTS wording requiring future function tests;
- do not add placeholder function syntax.

Future condition calls produce `SMILE1404`.

---

# 13. Condition examples to encode in tests

## Valid

```smile
IF Age >= 18 THEN
END IF
```

```smile
IF Ready = TRUE THEN
END IF
```

```smile
IF Age >= 18 AND Ready = TRUE THEN
END IF
```

```smile
IF (Age >= 18 AND Age <= 65) OR Override = TRUE THEN
END IF
```

```smile
IF NOT (Ready = TRUE) THEN
END IF
```

```smile
IF Score + Bonus >= 100 THEN
END IF
```

## Invalid

```smile
IF Ready THEN
END IF
```

```smile
IF TRUE THEN
END IF
```

```smile
IF (Ready) THEN
END IF
```

```smile
IF NOT Ready THEN
END IF
```

```smile
IF Age >= 18 AND Ready THEN
END IF
```

```smile
IF TRUE OR Age >= 18 THEN
END IF
```

---

# 14. Diagnostics

Implement:

| Code | Meaning |
|---|---|
| `SMILE1401` | IF requires a condition |
| `SMILE1402` | Every atomic IF condition must be an explicit comparison |
| `SMILE1403` | Complete IF condition must have type Boolean |
| `SMILE1404` | IF condition cannot invoke a function or procedure |
| `SMILE1405` | IF or ELSE IF requires THEN |
| `SMILE1406` | Unexpected content follows THEN |
| `SMILE1407` | ELSE must stand alone or be followed by IF on the same logical line |
| `SMILE1408` | ELSE IF requires a condition |
| `SMILE1409` | Duplicate final ELSE |
| `SMILE1410` | ELSE IF cannot follow ELSE |
| `SMILE1411` | Unexpected ELSE, ELSE IF, or END IF |
| `SMILE1412` | Missing END IF |
| `SMILE1413` | Malformed END IF or trailing content |
| `SMILE1414` | LET is not permitted inside IF v1.0 |
| `SMILE1415` | Statement is not permitted inside IF v1.0 |

Use parser diagnostics for block shape and binder/validator diagnostics for semantic condition rules.

Preserve existing expression, name, type, overflow, division, and String diagnostics.

---

# 15. Bound model

Add:

```csharp
public sealed record BoundConditionalClause(
    BoundExpression Condition,
    IReadOnlyList<BoundStatement> Statements);

public sealed record BoundIfStatement(
    IReadOnlyList<BoundConditionalClause> Clauses,
    IReadOnlyList<BoundStatement> ElseStatements)
    : BoundStatement;
```

All ten generators and `SmileEvaluator` consume this representation.

Do not make targets inspect IF source text.

---

# 16. Binder behavior

For every IF and ELSE IF clause:

1. bind the condition;
2. run explicit-comparison structural validation;
3. run call-prohibition validation;
4. require Boolean result;
5. bind every body statement.

Bind the ELSE body.

Bind all branches even if a previous condition is currently known true.

Reject LET in any branch using `SMILE1414`.

SET target lookup remains case-insensitive.

---

# 17. Evaluator

Implement recursive execution:

```text
for each conditional clause in order:
    evaluate condition using current runtime values
    if true:
        execute its statements
        stop the IF

if no clause succeeds:
    execute ELSE statements
```

Only the selected branch mutates runtime values.

Nested IF recursively uses the same logic.

---

# 18. Branch-aware analysis

The existing execution trace is linear.

Do not force nested IF into a fake flat `Steps[index]` relationship.

Introduce a small recursive statement-list analyzer.

Use an abstract value equivalent to:

```text
Known(SmileValue)
Unknown
```

An implementation may use:

```csharp
public readonly record struct AnalyzedValue(
    bool IsKnown,
    SmileValue Value);
```

Do not add a third-party dataflow or CFG package.

---

# 19. Branch merge

Analyze each branch from the same incoming environment.

Merge outgoing paths:

```text
same known value on every possible path
    -> Known(value)

different known values
    -> Unknown

known on one path, unknown on another
    -> Unknown

unchanged on every path
    -> preserve incoming

changed in only one branch
    -> merge changed path with unchanged path
```

An IF without ELSE has an implicit unchanged path.

Multiple ELSE IF clauses contribute possible paths in source order.

A statically known condition may guide known-value analysis, but it must not remove clauses or bodies from the bound program or generated code.

This analysis prepares for future INPUT.

---

# 20. Analysis facts required by targets

Provide branch-aware facts for:

- values before/after statements where known;
- possible assigned values for each variable;
- mutated variables;
- maximum String UTF-8 byte length across branches;
- possible embedded NUL across branches;
- Integer profile across conditions and all branch expressions;
- target facilities used anywhere in the IF tree.

A hierarchical dictionary keyed by bound node is acceptable.

Do not assume one flat step per top-level statement.

---

# 21. Simplification

Keep safe expression simplification.

Do not:

- replace an entire IF with one branch;
- delete ELSE IF clauses;
- delete ELSE;
- carry a branch value into another branch;
- propagate a post-IF value unless merge proves it known.

Validate explicit comparison before simplification.

Short-circuit simplification within conditions remains allowed.

---

# 22. Integer and String planning

Inspect every branch, not only the branch selected by current values.

Integer profile includes:

- IF and ELSE IF comparisons;
- operands and intermediates;
- SET expressions in every branch;
- nested IF;
- later merged use.

String planning includes:

- every branch-assigned value;
- maximum byte size;
- NUL possibility;
- logical-length requirements;
- post-IF direct variable reads.

---

# 23. High-level target generation

Emit natural control flow.

## C#, C, C++, Java, JavaScript, Objective-C, Swift

```text
if (...)
{
}
else if (...)
{
}
else
{
}
```

## Python

```python
if condition:
    ...
elif condition:
    ...
else:
    ...
```

Use `pass` for an empty Python branch.

Preserve deterministic indentation and expression precedence.

---

# 24. COBOL generation

Emit valid warning-free GnuCOBOL free-format control flow.

A chained or nested target form is acceptable:

```cobol
IF condition-1
    ...
ELSE
    IF condition-2
        ...
    ELSE
        ...
    END-IF
END-IF.
```

Requirements:

- preserve clause order;
- preserve every body;
- preserve current WORKING-STORAGE reads;
- preserve logical lengths and exact empty behavior;
- compile with zero warnings.

Direct comparisons that can read current target storage should do so.

When the current educational storage profile cannot safely express a complex condition, isolate a branch-aware known Boolean behind a small COBOL condition plan. Do not scatter compiler-time evaluation or delete source branches.

---

# 25. MASM x64 generation

Emit genuine deterministic compare/jump control flow.

Conceptual:

```asm
; evaluate clause 1
test eax, eax
jz if1Clause2

; clause 1
jmp if1End

if1Clause2:
; evaluate clause 2
test eax, eax
jz if1Else

; clause 2
jmp if1End

if1Else:
; else

if1End:
```

Requirements:

- deterministic collision-safe labels;
- nested IF labels never collide;
- all bodies remain present;
- current pointer/length storage remains correct;
- zero assembler/linker warnings.

Create one MASM condition-emission abstraction.

Direct comparisons that can read current storage should do so.

Where the current storage model cannot express a complex condition safely, a branch-aware proven Boolean may be lowered to a runtime compare/jump, but the complete structure and all bodies must remain. Document the boundary for future INPUT work.

---

# 26. ELSE IF target mapping

Map the bound clause list as:

- `else if` for C-like targets and Swift;
- `elif` for Python;
- valid nested/chained IF for COBOL;
- next conditional label for MASM.

Do not require identical target spelling.

---

# 27. Empty branches

Empty branches are valid.

Use:

- empty braces where warning-free;
- Python `pass`;
- valid no-op/empty imperative form in COBOL;
- label/comment-only flow in MASM when valid.

Do not introduce observable output.

---

# 28. Genuine branch preservation

Use this regression:

```smile
LET X = 1

IF X = 1 THEN
    PRINT Then branch
ELSE
    PRINT Else branch
END IF
```

Every generated target source must contain both branch bodies.

Runtime output contains only:

```text
Then branch
```

Do not constant-fold away the ELSE body.

---

# 29. ELSE IF versus nested IF

Test distinct syntax and bound shapes.

## Clause chain

```smile
IF A = 1 THEN
ELSE IF B = 2 THEN
END IF
```

One BoundIfStatement with two conditional clauses.

## Nested

```smile
IF A = 1 THEN
ELSE
    IF B = 2 THEN
    END IF
END IF
```

One outer BoundIfStatement whose ELSE body contains a nested BoundIfStatement.

---

# 30. SET Block String inside IF

Preserve current block-String parsing and exact whitespace rules.

Example:

```smile
LET Message = ""

IF Ready = TRUE THEN
    SET Message ="
Ready
to begin
"
END IF
```

The block's internal lines must not be mistaken for IF terminators.

---

# 31. Syntax highlighting

Highlight:

```text
IF
THEN
ELSE
END
```

Requirements:

- ELSE IF highlights as two keywords;
- END IF highlights as two keywords;
- nested IF remains responsive;
- Block Strings inside branches retain highlighting;
- malformed blocks do not crash AvalonEdit.

Do not add semantic code folding unless already trivial and explicitly in scope.

---

# 32. Warning validation

Preserve:

```text
SMILE_REQUIRE_ZERO_TARGET_WARNINGS
```

Add IF programs to strict warning tests.

Compiler-backed targets must produce zero detected warnings.

JavaScript and Python remain interpreter-only.

Do not weaken warning detection.

---

# 33. Cumulative language reference

Append an IF section to:

```text
examples/language.smile
```

Preserve all existing cumulative sections.

Include executable valid examples of:

- simple IF;
- IF/ELSE;
- multiple ELSE IF;
- explicit Boolean comparison;
- AND/OR/NOT with explicit comparisons;
- nested IF;
- SET in branches;
- Block String SET in a branch;
- PRINT after IF.

Do not include invalid examples in the executable reference.

---

# 34. Version and documentation

Update to:

```text
SMILE v0.6.0 — IF / ELSE IF / ELSE
```

Synchronize:

- Desktop project/assembly/file/informational versions;
- About;
- README;
- AGENTS;
- architecture;
- roadmap;
- target code-generation standard;
- cumulative language overview;
- core expression specification;
- LET, SET, and PRINT cross-references;
- Toolchains validation instructions;
- requirements/history.

---

# 35. AGENTS.md rules

Add wording equivalent to:

> IF conditions are call-free. Every value used by a condition must already exist without invoking a function or procedure during condition evaluation.

> Every atomic IF condition must contain an explicit comparison and right-hand operand. Standalone Boolean variables and literals are invalid.

> ELSE IF consists of ELSE and IF on the same logical header line. An IF after a standalone ELSE line is nested and requires its own END IF.

> IF v1.0 permits PRINT, SET, nested IF, and blank lines in branches. LET is not permitted until scopes are formally introduced.

> Every target must preserve genuine branch structure. Do not delete unselected source branches merely because current values are known.

> Branch-aware known-value analysis may propagate a value after IF only when outgoing-path merge proves it known.

---

# 36. Parser tests

Cover:

- IF only;
- IF/ELSE;
- one and multiple ELSE IF;
- final ELSE;
- empty blocks;
- nested IF;
- ELSE followed by nested IF on next line;
- same-line ELSE IF;
- case-insensitive keywords;
- whitespace and tabs;
- Block String inside branch;
- EOF after END IF.

---

# 37. Invalid block tests

Test:

```smile
IF X = 1
END IF
```

`SMILE1405`.

```smile
IF X = 1 THEN PRINT One
END IF
```

`SMILE1406`.

```smile
ELSEIF X = 1 THEN
```

Not ELSE IF.

```smile
ELSE PRINT One
```

`SMILE1407`.

Missing END IF:

`SMILE1412`.

Duplicate ELSE:

`SMILE1409`.

ELSE IF after ELSE:

`SMILE1410`.

Malformed END/ENDIF/trailing text:

`SMILE1413` or the documented unexpected-keyword diagnostic.

---

# 38. Explicit-condition tests

Reject:

```smile
LET Ready = TRUE
IF Ready THEN
END IF
```

```smile
IF TRUE THEN
END IF
```

```smile
LET Ready = TRUE
IF (Ready) THEN
END IF
```

```smile
LET Ready = TRUE
IF NOT Ready THEN
END IF
```

```smile
LET Age = 49
LET Ready = TRUE
IF Age >= 18 AND Ready THEN
END IF
```

Accept:

```smile
LET Ready = TRUE
IF Ready = TRUE THEN
END IF
```

```smile
IF TRUE = TRUE THEN
END IF
```

```smile
LET Ready = TRUE
IF NOT (Ready = TRUE) THEN
END IF
```

```smile
LET Age = 49
LET Ready = TRUE
IF Age >= 18 AND Ready = TRUE THEN
END IF
```

Run identical validation for every ELSE IF condition.

---

# 39. LET-in-branch tests

Reject LET in:

- IF body;
- ELSE IF body;
- ELSE body;
- nested IF body.

Use `SMILE1414`.

A failed branch declaration must not leak a symbol after IF.

---

# 40. Evaluator tests

Cover:

- true IF;
- false IF without ELSE;
- IF/ELSE;
- multiple ELSE IF, first match wins;
- ELSE fallback;
- nested IF;
- SET persistence after selected branch;
- current values from earlier SET;
- empty branch;
- exact Block String branch.

---

# 41. Branch merge tests

## Same outgoing value

Both branches assign the same value. Post-state may remain Known.

## Different values

Post-state must become Unknown.

## IF without ELSE

Merge changed path with unchanged incoming path.

## Multiple ELSE IF

Merge all possible outgoing paths.

## Nested IF

Merge recursively.

Do not propagate stale or branch-specific values.

---

# 42. Target structural tests

Require genuine control flow:

| Target | Required structure |
|---|---|
| C# | `if`, `else if`, `else` |
| C | `if`, `else if`, `else` |
| MASM | conditional jumps and deterministic labels |
| JavaScript | `if`, `else if`, `else` |
| Java | `if`, `else if`, `else` |
| COBOL | `IF`, `ELSE`, matching `END-IF` |
| Objective-C | `if`, `else if`, `else` |
| Swift | `if`, `else if`, `else` |
| Python | `if`, `elif`, `else` |
| C++ | `if`, `else if`, `else` |

Assert every body text/value appears in generated source.

---

# 43. Normative all-target acceptance program

Use the program from the official specification.

For each target:

1. evaluate with SmileEvaluator;
2. generate;
3. verify all source branches are present;
4. build/run;
5. require exit code zero;
6. require zero detected warnings for compiler-backed targets;
7. compare stdout exactly, normalizing only physical CRLF/LF where already established;
8. use exact bytes for Block String/control-byte cases.

Run with:

```text
SMILE_REQUIRE_JAVA=1
SMILE_REQUIRE_ALL_TARGETS=1
SMILE_REQUIRE_ZERO_TARGET_WARNINGS=1
```

---

# 44. Clause-selection matrix

Create tests selecting:

- initial IF;
- first ELSE IF;
- middle ELSE IF;
- final ELSE IF;
- ELSE;
- no branch when ELSE absent.

Confirm only the appropriate body executes.

---

# 45. Determinism

Generate nested and multi-clause programs twice for all targets.

Compare every generated file byte-for-byte.

MASM labels and COBOL compiler-owned names must be stable.

---

# 46. Desktop validation

1. Launch Desktop.
2. Confirm first paint remains responsive.
3. Confirm cumulative language reference loads.
4. Confirm IF/THEN/ELSE/END highlighting.
5. Confirm ELSE IF and END IF highlight as two keywords.
6. Enter nested IF.
7. Confirm live panes remain responsive.
8. Inspect C#, Python, COBOL, and MASM control flow.
9. Build/run official acceptance through all ten targets.
10. Test Block String in a branch.
11. Test standalone Boolean rejection.
12. Test missing END IF.
13. Test ELSE newline nested IF distinction.
14. Rapidly switch targets.
15. Test cancellation/failure containment.
16. Confirm About v0.6.0.

---

# 47. Performance

Do not:

- invoke toolchains during live typing;
- run parser/generator work on the WPF dispatcher;
- introduce exponential branch analysis;
- copy environments at every expression node;
- remove timeouts or bounded output.

A small environment clone per branch is acceptable.

---

# 48. Scope exclusions

Do not implement:

- INPUT;
- loops;
- functions;
- procedures;
- branch-local LET;
- scopes;
- one-line IF;
- combined ELSEIF;
- combined ENDIF;
- SELECT CASE;
- ternary expressions;
- assignment expressions;
- comments;
- another target;
- a feature branch.

---

# 49. Acceptance criteria

The task is complete only when:

1. IF, THEN, ELSE, and END are case-insensitive reserved keywords.
2. Block IF works.
3. THEN and END IF are mandatory.
4. ELSE is optional.
5. Multiple ELSE IF clauses work.
6. ELSE IF is two same-line keywords.
7. ELSEIF is not accepted as the clause spelling.
8. ELSE newline IF is nested.
9. Nested IF and empty branches work.
10. Complete conditions are Boolean.
11. Every atomic condition is an explicit comparison.
12. Standalone Boolean variables/literals are rejected.
13. NOT of standalone Boolean is rejected.
14. Every compound leaf is an explicit comparison.
15. Future call nodes are rejected by design.
16. LET is rejected in branches.
17. PRINT, SET, nested IF, blank lines, and Block String SET are allowed.
18. Selected SET mutations survive after IF.
19. First successful clause wins.
20. All branches are parsed, bound, and emitted.
21. No target deletes unselected source branches.
22. Evaluator executes branch semantics.
23. Branch-aware Known/Unknown analysis exists.
24. Merge does not leak branch-specific values.
25. Integer and String planning inspect all branches.
26. All ten targets emit genuine branch structure.
27. All ten targets build/run and match SmileEvaluator.
28. Compiler-backed targets emit zero detected warnings.
29. Debug/Release builds have zero warnings/errors.
30. Strict Debug/Release tests pass with zero skips.
31. Generation is deterministic.
32. Cumulative language.smile remains intact and grows.
33. Desktop remains responsive.
34. Documentation matches implementation.
35. Destination-language expansion remains frozen.
36. No unrelated feature/dependency/artifact is added.
37. Work is performed directly on main.

---

# 50. Suggested implementation sequence

1. Confirm newest main.
2. Publish official IF specification.
3. Add keywords/highlighting.
4. Add recursive statement-list parsing.
5. Add syntax nodes.
6. Parse IF/ELSE IF/ELSE/END IF.
7. Add block diagnostics.
8. Add explicit-comparison/call validator.
9. Add bound nodes.
10. Bind all branches and reject LET.
11. Implement evaluator.
12. Add branch-aware Known/Unknown analysis.
13. Update simplifier and target planning.
14. Implement C#, C, JavaScript, Java, Objective-C, Swift, Python, C++.
15. Implement COBOL.
16. Implement MASM.
17. Add front-end/evaluator/analysis tests.
18. Add target structural/runtime/warning tests.
19. Append cumulative examples.
20. Run strict Debug.
21. Run strict Release.
22. Run Desktop smoke.
23. Update docs/version.
24. Commit/push only when Sin explicitly authorizes it.

---

# 51. Validation commands

```bat
cmd /c git status --short --branch
```

Confirm `main`.

```bat
cmd /c dotnet restore SMILE.sln
cmd /c dotnet build SMILE.sln -c Debug -nologo
```

Strict Debug:

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

Strict Release:

```powershell
$env:SMILE_REQUIRE_JAVA = '1'
$env:SMILE_REQUIRE_ALL_TARGETS = '1'
$env:SMILE_REQUIRE_ZERO_TARGET_WARNINGS = '1'
dotnet test SMILE.sln -c Release --no-build -nologo
Remove-Item Env:SMILE_REQUIRE_JAVA
Remove-Item Env:SMILE_REQUIRE_ALL_TARGETS
Remove-Item Env:SMILE_REQUIRE_ZERO_TARGET_WARNINGS
```

Generate:

```bat
cmd /c dotnet run --project src\SMILE.Cli -- <IF_ACCEPTANCE_EXAMPLE.smile> --target all
```

Run all targets explicitly with `--run`.

Cumulative reference:

```bat
cmd /c dotnet run --project src\SMILE.Cli -- examples\language.smile --target all
```

Before authorized commit:

```bat
cmd /c git diff --check
cmd /c git diff --stat
cmd /c git status --short --branch
```

---

# 52. Completion report

Report:

- baseline commit;
- files changed;
- official specification path;
- keyword changes;
- recursive parser design;
- ELSE IF same-line recognition;
- nested ELSE/newline/IF distinction;
- condition validator;
- explicit-comparison diagnostics;
- future call-prohibition design;
- syntax/bound representations;
- branch LET restriction;
- evaluator behavior;
- Known/Unknown analysis and merge;
- simplifier behavior;
- Integer/String/NUL planning;
- each target's control-flow strategy;
- COBOL and MASM strategies;
- Debug/Release test counts;
- skips;
- warning results;
- all-ten runtime results;
- deterministic results;
- Desktop smoke;
- documentation updates;
- unresolved concerns.

Do not claim completion when any standalone Boolean condition is accepted, ELSEIF is treated as ELSE IF, newline-separated ELSE/IF is collapsed, a branch is deleted, or branch-specific state leaks past IF.
