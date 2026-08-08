# SMILE — IF Statement Official Specification v1.0

## Status

This document is the complete official language specification for SMILE conditional execution.

The language definition was introduced in:

> **SMILE v0.6.0 — IF / ELSE IF / ELSE**

The current implementation baseline is:

> **SMILE v0.8.0 — WHILE Loops**

This specification defines three permanent SMILE rules:

1. **Condition evaluation rule**
   Every value used by an IF condition must already be available without invoking a function or procedure during condition evaluation.

2. **Explicit comparison rule**
   Every atomic condition must contain an explicit comparison operator and a right-hand operand. Standalone Boolean variables and Boolean literals are not permitted as conditions.

3. **ELSE IF clause rule**
   When `ELSE` is immediately followed by `IF` on the same logical statement line, the two keywords form an `ELSE IF` clause belonging to the current IF statement.

This specification works together with the numbered official SMILE specifications for LET, SET, PRINT, String literals, core expressions, [007 - Full-Line Comments and Source Layout Preservation](007%20-%20SMILE%20-%20Full-Line%20Comments%20and%20Source%20Layout%20Preservation%20Official%20Specification%20v1.0.md), [008 - INPUT](008%20-%20SMILE%20-%20INPUT%20Statement%20Official%20Specification%20v1.0.md), and [009 - WHILE](009%20-%20SMILE%20-%20WHILE%20Statement%20Official%20Specification%20v1.0.md).

---

# 1. Purpose

`IF` conditionally executes one statement block.

```smile
LET Age = 49
LET Category = ""

IF Age >= 18 THEN
    SET Category = "Adult"
ELSE
    SET Category = "Minor"
END IF

PRINT {Category}
```

Output:

```text
Adult
```

---

# 2. Basic block syntax

```text
IF condition THEN
    statements
END IF
```

Optional ELSE:

```text
IF condition THEN
    statements
ELSE
    statements
END IF
```

Optional ELSE IF chain:

```text
IF condition THEN
    statements
ELSE IF condition THEN
    statements
ELSE IF condition THEN
    statements
ELSE
    statements
END IF
```

Only one `END IF` closes the complete IF / ELSE IF / ELSE chain.

---

# 3. Formal grammar

```text
if-statement ->
    IF hspace+ if-condition hspace+ THEN hspace* line-end
    statement-list
    else-if-clause*
    else-clause?
    END hspace+ IF hspace* statement-end

else-if-clause ->
    ELSE hspace+ IF hspace+ if-condition hspace+ THEN hspace* line-end
    statement-list

else-clause ->
    ELSE hspace* line-end
    statement-list

statement-list ->
    zero or more permitted statements, full-line comments, or blank-line layout items

statement-end ->
    line-end
    | end-of-file

hspace ->
    space
    | tab
```

Keywords and variable names remain case-insensitive.

---

# 4. Block-only IF

SMILE v0.6.0 supports block IF only.

Valid:

```smile
IF Age >= 18 THEN
    PRINT Adult
END IF
```

Invalid:

```smile
IF Age >= 18 THEN PRINT Adult
```

`THEN` must be the final non-whitespace token on an IF or ELSE IF header line.

---

# 5. Mandatory THEN

Every IF and ELSE IF header requires `THEN`.

Valid:

```smile
IF Age >= 18 THEN
END IF
```

Invalid:

```smile
IF Age >= 18
END IF
```

Invalid:

```smile
ELSE IF Age >= 13
```

---

# 6. Mandatory END IF

Every IF requires one matching:

```smile
END IF
```

`END IF` consists of two keywords.

Invalid:

```smile
ENDIF
```

Invalid:

```smile
END
```

Invalid:

```smile
END IF extra
```

---

# 7. Empty blocks are valid

Valid:

```smile
IF X > 10 THEN
END IF
```

Also valid:

```smile
IF X = 1 THEN
ELSE IF X = 2 THEN
ELSE
END IF
```

---

# 8. Condition result

The complete IF condition must evaluate to Boolean.

Valid:

```smile
IF Age >= 18 THEN
END IF
```

Invalid Integer condition:

```smile
IF Age + 1 THEN
END IF
```

Invalid String condition:

```smile
IF Name + "!" THEN
END IF
```

The explicit-comparison rule is stricter than merely requiring a Boolean result.

---

# 9. Condition evaluation rule

Every value used by an IF condition must already be available without invoking a function or procedure during condition evaluation.

Valid future usage:

```smile
LET Result = FUNC(A)

IF Result > 10 THEN
    PRINT Greater
END IF
```

Invalid:

```smile
IF FUNC(A) > 10 THEN
    PRINT Greater
END IF
```

Invalid:

```smile
IF Result = FUNC(A) THEN
    PRINT Match
END IF
```

Invalid:

```smile
IF IsReady() = TRUE THEN
    PRINT Ready
END IF
```

The restriction applies recursively to the complete condition, including both sides of comparisons, parentheses, AND, OR, and NOT.

Functions and procedures do not exist in v0.6.0, but this rule is normative for future milestones.

---

# 10. Conditional future function execution

A future function result may be computed before IF:

```smile
LET Result = FUNC(A)

IF Result > 10 THEN
    PRINT Greater
END IF
```

When a future function must run only after another condition succeeds:

```smile
LET Result = 0

IF Ready = TRUE THEN
    SET Result = FUNC(A)

    IF Result > 10 THEN
        PRINT Greater
    END IF
END IF
```

---

# 11. Explicit comparison rule

Every atomic condition must contain a left operand, a comparison operator, and a right operand.

Valid:

```smile
IF Ready = TRUE THEN
END IF
```

Invalid standalone Boolean variable:

```smile
IF Ready THEN
END IF
```

Invalid standalone Boolean literal:

```smile
IF TRUE THEN
END IF
```

Invalid parenthesized standalone Boolean:

```smile
IF (Ready) THEN
END IF
```

Invalid negated standalone Boolean:

```smile
IF NOT Ready THEN
END IF
```

Valid explicit negation:

```smile
IF NOT (Ready = TRUE) THEN
END IF
```

---

# 12. Comparison operators

Atomic conditions use:

```text
=
<>
<
<=
>
>=
```

Examples:

```smile
IF Name = "Sin" THEN
END IF
```

```smile
IF Name <> "Sin" THEN
END IF
```

```smile
IF Age >= 18 THEN
END IF
```

Existing type rules remain in effect.

---

# 13. Atomic condition

Examples:

```text
Age >= 18
Ready = TRUE
Name <> ""
Score + Bonus >= 100
FirstName + LastName = "SinCioco"
```

Each side may use existing SMILE value expressions provided that they are type-compatible, invoke no function or procedure, and contain no assignment.

---

# 14. Compound conditions

Atomic comparisons may be combined using AND, OR, NOT, and parentheses.

Valid:

```smile
IF Age >= 18 AND Ready = TRUE THEN
END IF
```

Valid:

```smile
IF Name = "Sin" OR Name = "Louiery" THEN
END IF
```

Valid:

```smile
IF (Age >= 18 AND Age <= 65) OR Override = TRUE THEN
END IF
```

Valid:

```smile
IF NOT (Ready = TRUE) THEN
END IF
```

Invalid because one leaf is standalone:

```smile
IF Age >= 18 AND Ready THEN
END IF
```

Invalid:

```smile
IF TRUE OR Age >= 18 THEN
END IF
```

Every leaf in the Boolean condition tree must be an explicit comparison.

---

# 15. Boolean values as operands

Boolean variables and literals may appear as comparison operands.

Valid:

```smile
IF Ready = TRUE THEN
END IF
```

Valid:

```smile
IF Ready <> FALSE THEN
END IF
```

Valid, although redundant:

```smile
IF TRUE = TRUE THEN
END IF
```

---

# 16. Short circuiting

AND and OR retain left-to-right short-circuit behavior.

Both sides are still parsed, bound, structurally validated, and type-checked.

---

# 17. ELSE IF clause rule

When `ELSE` is immediately followed by `IF` on the same logical statement line, the two keywords form one ELSE IF clause.

```smile
IF Score >= 90 THEN
    PRINT A
ELSE IF Score >= 80 THEN
    PRINT B
ELSE IF Score >= 70 THEN
    PRINT C
ELSE
    PRINT Below C
END IF
```

Clauses are tested from top to bottom. The first true clause executes. No later clause executes.

---

# 18. ELSE IF uses two keywords

Valid:

```smile
ELSE IF Score >= 80 THEN
```

Invalid:

```smile
ELSEIF Score >= 80 THEN
```

Invalid:

```smile
ELSE-IF Score >= 80 THEN
```

Whitespace between ELSE and IF may contain spaces or tabs.

---

# 19. Same-line distinction

This is an ELSE IF clause:

```smile
ELSE IF B = 2 THEN
```

This is a nested IF in an ELSE block:

```smile
ELSE
    IF B = 2 THEN
    END IF
```

The nested form requires two matching END IF statements:

```smile
IF A = 1 THEN
    PRINT A
ELSE
    IF B = 2 THEN
        PRINT B
    END IF
END IF
```

---

# 20. Multiple ELSE IF clauses

Any number of ELSE IF clauses is allowed. Only one final ELSE is allowed.

---

# 21. ELSE placement

ELSE must stand alone on its logical line.

Valid:

```smile
ELSE
```

Invalid:

```smile
ELSE PRINT Fallback
```

ELSE IF must appear before the final ELSE.

---

# 22. Nested IF

Nested IF is valid:

```smile
IF Age >= 18 THEN
    IF Ready = TRUE THEN
        PRINT Accepted
    END IF
END IF
```

Every nested IF requires its own END IF.

---

# 23. Statements allowed inside IF v1.0

Permitted:

- PRINT;
- SET;
- INPUT;
- nested IF;
- WHILE;
- full-line `REM`, `//`, `#`, and `--` comments;
- blank lines;
- Block String Literals as complete SET values. LET remains prohibited in every IF-related body.

Comments and blank lines are ordered non-semantic source items. Comment payloads that spell `IF`, `ELSE`, `ELSE IF`, `END IF`, malformed terminators, or Block String delimiters have no control-flow meaning. Marker-looking and blank physical lines inside a Block String used by SET remain String data rather than branch layout.

INPUT targets an existing variable declared outside the IF. Only the selected branch executes its INPUT statements and consumes lines. An unselected INPUT still participates in binding, mutation analysis, conservative internal Integer/String planning, and target generation, but consumes no input at runtime. Internal planning facts are not public INPUT limits and must not dictate a shared generated runtime.

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

---

# 24. LET is not permitted inside IF v1.0

SMILE v0.8.0 does not introduce block-local declarations or scopes.

Valid:

```smile
LET Result = ""

IF Ready = TRUE THEN
    SET Result = "Ready"
END IF
```

Invalid:

```smile
IF Ready = TRUE THEN
    LET Result = "Ready"
END IF
```

---

# 25. SET and INPUT changes survive the selected branch

```smile
LET Result = ""

IF Age >= 18 THEN
    SET Result = "Adult"
ELSE
    SET Result = "Minor"
END IF

PRINT {Result}
```

At evaluator runtime, later statements see the selected branch's current value. For target generation, a value after IF may be propagated statically only when every possible outgoing path merges to the same branch-aware `Known` value. A selected concrete reference trace never authorizes static lowering of an `Unknown` later LET, SET, PRINT, interpolation, or IF condition; those expressions must read and compute from current generated runtime storage.

An INPUT in the selected branch reads and atomically updates its target before later statements. An INPUT in every unselected clause consumes no line. After merging a path that executes INPUT with a path that does not, the target value is Unknown unless every outgoing path later proves the same known value.

---

# 26. Only one branch executes

Clauses execute in order. The first true IF or ELSE IF branch executes, then the IF finishes. ELSE executes only when no condition succeeds.

---

# 27. All branches must be valid

Every branch is parsed, name-resolved, structurally validated, and type-checked even when not selected in a particular execution.

---

# 28. Genuine control flow

Every destination generator must preserve the complete branch structure.

A generator must not delete unselected branches or replace the whole IF with only the currently selected branch.

---

# 29. Source order

Conditions observe current values produced by earlier LET, SET, and INPUT statements.

```smile
LET Value = 1
SET Value = 2

IF Value = 2 THEN
    PRINT Two
END IF
```

---

# 30. Case insensitivity

IF, THEN, ELSE, END, Boolean literals, and identifiers remain case-insensitive.

---

# 31. Reserved keywords

These become reserved:

```text
IF
THEN
ELSE
END
```

`ELSEIF` is not the ELSE IF clause spelling and does not become a combined keyword.

---

# 32. One logical header per line

These occupy one logical line:

```text
IF ... THEN
ELSE IF ... THEN
ELSE
END IF
```

A Block String may span physical lines as a SET value inside a branch but remains one SET statement. The same source form is valid for top-level LET, but LET remains invalid in IF v1.0.

---

# 33. Invalid examples

```smile
IF Ready THEN
END IF
```

```smile
IF TRUE THEN
END IF
```

```smile
IF NOT Ready THEN
END IF
```

```smile
IF Age >= THEN
END IF
```

```smile
IF FUNC(A) > 10 THEN
END IF
```

```smile
IF Age >= 18
END IF
```

```smile
IF Age >= 18 THEN PRINT Adult
END IF
```

```smile
ELSEIF Age >= 13 THEN
```

```smile
ELSE IF Ready THEN
```

```smile
IF X = 1 THEN
ELSE
ELSE IF X = 2 THEN
END IF
```

```smile
IF X = 1 THEN
    LET Result = "One"
END IF
```

---

# 34. Diagnostics

| Code | Meaning |
|---|---|
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
| `SMILE1416` | Maximum combined IF/WHILE nesting depth of 128 exceeded at IF |

Existing expression and type diagnostics remain applicable.

---

# 35. Syntax representation

Recommended:

```csharp
public sealed record ConditionalClauseSyntax(
    ExpressionSyntax Condition,
    IReadOnlyList<StatementSyntax> Statements,
    TextSpan Span);

public sealed record IfStatementSyntax(
    IReadOnlyList<ConditionalClauseSyntax> Clauses,
    IReadOnlyList<StatementSyntax> ElseStatements,
    bool HasElseClause,
    TextSpan Span)
    : StatementSyntax(Span);
```

The first clause is IF. Later clauses are ELSE IF.

`HasElseClause` is false when ELSE is absent and true when an explicit final ELSE is present, including an explicit empty ELSE body. `ElseStatements` alone cannot distinguish those two valid source forms because both may contain zero statements.

---

# 36. Bound representation

Recommended:

```csharp
public sealed record BoundConditionalClause(
    BoundExpression Condition,
    IReadOnlyList<BoundStatement> Statements);

public sealed record BoundIfStatement(
    IReadOnlyList<BoundConditionalClause> Clauses,
    IReadOnlyList<BoundStatement> ElseStatements,
    bool HasElseClause)
    : BoundStatement;
```

All targets and the evaluator consume this shared model. Targets use `HasElseClause` to preserve an explicit empty ELSE instead of treating it as an absent clause.

---

# 37. Reference execution model

```text
for each conditional clause in order:
    evaluate condition using current runtime values
    if true:
        execute its statements
        finish IF

if none succeeded:
    execute ELSE statements
```

Only the selected branch updates runtime state or consumes input.

---

# 38. Target mapping

- C#, C, C++, Java, JavaScript, Objective-C, Swift: `if / else if / else`
- Python: `if / elif / else`
- COBOL: valid `IF / ELSE / END-IF`
- MASM x64: deterministic compare/jump labels

The active C#, C, and MASM targets retain every branch-local INPUT at its source position, evaluate runtime-dependent conditions from current storage, and preserve genuine branch structure. The seven other generator implementations are paused and do not form part of the current support or routine conformance promise; each must complete catch-up validation before re-enablement.

---

# 39. Normative acceptance program

```smile
LET Score = 85
LET Ready = TRUE
LET Grade = ""
LET Message = ""

IF Score >= 90 AND Ready = TRUE THEN
    SET Grade = "A"
ELSE IF Score >= 80 AND Ready = TRUE THEN
    SET Grade = "B"
ELSE IF Score >= 70 AND Ready = TRUE THEN
    SET Grade = "C"
ELSE
    SET Grade = "Below C"
END IF

IF Grade = "B" THEN
    SET Message ="
Grade B
Ready for the next lesson.
"
ELSE
    SET Message = "Unexpected grade"
END IF

PRINT Grade={Grade}
PRINT {Message}
```

Required output:

```text
Grade=B
Grade B
Ready for the next lesson.
```

---

# 40. Implementation safety limit

The maximum supported combined IF/WHILE control-flow nesting depth is 128, where the first outermost block is depth 1. A program nested to exactly depth 128 is valid. Attempting to enter an IF at depth 129 produces the `DiagnosticSeverity.Error` diagnostic `SMILE1416` at that IF keyword; attempting to enter a WHILE there produces `SMILE1611` under specification 009. The compiler recovers without recursively processing the over-limit body, and alternating block kinds cannot bypass the limit.

Iterative recovery balances IF, ELSE IF, ELSE, END IF, WHILE, and END WHILE. It gives the canonical shared Block String scanner first ownership of block content, then recognizes and ignores valid full-line comments before examining structural headers. Comment payloads such as `// END IF`, `# WHILE TRUE = TRUE`, `-- END WHILE`, and `REM ENDIF` therefore cannot change the nesting balance or prevent recovery to later top-level code.

This is a compiler safety and resource limit. It does not change IF syntax, clause selection, branch validation, or the behavior of ordinary valid programs within the supported depth.

---

# 41. Future compatibility

Future function syntax must preserve the call-free condition rule.

Future Boolean functions must assign their results before explicit comparison.

SMILE v0.7.0 INPUT introduces runtime-unknown values. IF analysis must merge every possible outgoing path conservatively, and only an executed branch consumes input or produces a reached runtime error.

Future scopes may permit LET in IF, but v1.0 does not.

The same-line ELSE IF rule remains distinct from a nested IF after a standalone ELSE.
