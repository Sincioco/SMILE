# 009 - SMILE - WHILE Statement Official Specification v1.0

## Status

This document is the complete official language specification for the SMILE `WHILE` statement.

> **Strategic Reset note (2026-08-08):** WHILE syntax, pre-test control flow, nesting, fixed-point analysis, and genuine target loop structure remain current. References later in this document to a universal 4096-byte INPUT limit, embedded-NUL console preservation, exact all-target INPUT parity, or mandatory all-ten-target execution are superseded by the current INPUT specification, `docs/SMILE Core Principles.md`, and the active-target policy. Conservative capacity/NUL facts may remain internal planner details; they are not learner-facing runtime requirements.

It is introduced by:

> **SMILE v0.8.0 — WHILE Loops**

`WHILE` is SMILE's first official loop construct.

This specification works together with:

- `001 - SMILE - SET Statement Official Specification v1.0.md`
- `002 - SMILE - PRINT Statement Official Specification v1.0.md`
- `003 - SMILE - String Literals Official Specification v1.0.md`
- `004 - SMILE - Core Types and Expressions Official Specification v1.0.md`
- `005 - SMILE - LET Statement Official Specification v1.0.md`
- `006 - SMILE - IF Statement Official Specification v1.0.md`
- `007 - SMILE - Full-Line Comments and Source Layout Preservation Official Specification v1.0.md`
- `008 - SMILE - INPUT Statement Official Specification v1.0.md`

When this specification introduces loop-specific control-flow, analysis, or runtime behavior, this specification is normative for SMILE v0.8.0 and later unless superseded by a newer official specification.

---

# 1. Purpose

`WHILE` repeatedly executes a statement block while its condition evaluates to `TRUE`.

Example:

```smile
LET Count = 1

WHILE Count <= 5
    PRINT {Count}
    SET Count = Count + 1
END WHILE
```

Output:

```text
1
2
3
4
5
```

The condition is tested before the first iteration and again after every completed iteration.

---

# 2. Canonical syntax

The official v1.0 syntax is:

```text
WHILE condition
    statements
END WHILE
```

Example:

```smile
WHILE Count < Limit
    PRINT {Count}
    SET Count = Count + 1
END WHILE
```

The header does not use `THEN` or `DO`.

The terminator is exactly:

```text
END WHILE
```

---

# 3. Formal grammar

```text
while-statement ->
    WHILE hspace+ while-condition hspace* line-end
    statement-list
    END hspace+ WHILE hspace* statement-end

while-condition ->
    the IF-condition grammar and restrictions defined by specification 006

statement-list ->
    zero or more permitted statements, full-line comments, or blank-line layout items

statement-end ->
    line-end
    | end-of-file

hspace ->
    space
    | tab
```

`WHILE`, `END`, identifiers, Boolean literals, and all other SMILE keywords remain case-insensitive.

---

# 4. Case insensitivity

All of these are equivalent:

```smile
WHILE Count < 5
END WHILE
```

```smile
while Count < 5
end while
```

```smile
WhIlE Count < 5
EnD wHiLe
```

`WHILE` becomes a reserved SMILE keyword.

---

# 5. Required whitespace

`WHILE` must be followed by at least one ASCII space or tab before the condition.

Valid:

```smile
WHILE Count < 5
```

Valid:

```smile
WHILE	Count < 5
```

Invalid:

```smile
WHILE(Count < 5)
```

`WHILECount` is an ordinary identifier-shaped word, not the `WHILE` keyword followed by `Count`.

---

# 6. Block-only loop

SMILE v0.8.0 supports block `WHILE` only.

Valid:

```smile
WHILE Count < 5
    SET Count = Count + 1
END WHILE
```

Invalid:

```smile
WHILE Count < 5 SET Count = Count + 1
```

Invalid:

```smile
WHILE Count < 5: SET Count = Count + 1
```

One-line loops are not part of v1.0.

---

# 7. No THEN or DO keyword

The condition occupies the rest of the `WHILE` header line.

Valid:

```smile
WHILE Count < 5
```

Invalid:

```smile
WHILE Count < 5 THEN
```

Invalid:

```smile
WHILE Count < 5 DO
```

`THEN` remains specific to `IF` and `ELSE IF` headers.

---

# 8. Mandatory END WHILE

Every `WHILE` statement requires one matching:

```smile
END WHILE
```

`END WHILE` consists of two case-insensitive keywords on the same logical line.

Invalid:

```smile
ENDWHILE
```

Invalid:

```smile
WEND
```

Invalid:

```smile
LOOP
```

Invalid:

```smile
END WHILE extra
```

`WEND`, `ENDWHILE`, and `LOOP` do not become aliases in v1.0.

---

# 9. Pre-test execution

`WHILE` is a pre-test loop.

Execution order is:

1. evaluate the condition;
2. if the result is `FALSE`, leave the loop;
3. if the result is `TRUE`, execute the body from top to bottom;
4. return to step 1.

Example:

```smile
LET Count = 3

WHILE Count > 0
    PRINT {Count}
    SET Count = Count - 1
END WHILE
```

Output:

```text
3
2
1
```

---

# 10. Zero iterations

A `WHILE` body may execute zero times.

```smile
LET Count = 0

WHILE Count > 0
    PRINT Never
END WHILE

PRINT Done
```

Output:

```text
Done
```

The body is still parsed, bound, validated, analyzed, and generated even when the incoming condition is statically known to be `FALSE`.

---

# 11. Re-evaluation after each iteration

The condition reads current variable values.

```smile
LET Count = 1

WHILE Count <= 3
    PRINT {Count}
    SET Count = Count + 1
END WHILE
```

The condition observes `Count` as:

```text
1
2
3
4
```

The body runs for values `1`, `2`, and `3`. The condition becomes false at `4`.

A target generator must not evaluate the condition only once.

---

# 12. Condition result

The complete `WHILE` condition must have type `Boolean`.

Valid:

```smile
WHILE Count < 10
END WHILE
```

Invalid Integer condition:

```smile
WHILE Count + 1
END WHILE
```

Invalid String condition:

```smile
WHILE Name
END WHILE
```

The explicit-comparison rule is stricter than merely requiring a Boolean result.

---

# 13. Explicit comparison rule

Every atomic Boolean leaf in a `WHILE` condition must contain:

1. a left operand;
2. a comparison operator;
3. a right operand.

Valid:

```smile
WHILE Continue = TRUE
END WHILE
```

Invalid standalone Boolean variable:

```smile
WHILE Continue
END WHILE
```

Invalid standalone Boolean literal:

```smile
WHILE TRUE
END WHILE
```

Invalid parenthesized standalone Boolean:

```smile
WHILE (Continue)
END WHILE
```

Invalid negated standalone Boolean:

```smile
WHILE NOT Continue
END WHILE
```

Valid explicit negation:

```smile
WHILE NOT (Continue = FALSE)
END WHILE
```

This is the same permanent explicit-condition rule used by `IF`.

---

# 14. Comparison operators

Atomic conditions may use:

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
WHILE Count < Limit
END WHILE
```

```smile
WHILE Name <> "QUIT"
END WHILE
```

```smile
WHILE Ready = TRUE
END WHILE
```

Existing type compatibility rules remain in effect.

---

# 15. Compound conditions

Atomic comparisons may be combined using:

```text
AND
OR
NOT
parentheses
```

Valid:

```smile
WHILE Count < Limit AND Continue = TRUE
END WHILE
```

Valid:

```smile
WHILE Command <> "Q" AND Command <> "QUIT"
END WHILE
```

Valid:

```smile
WHILE (Count >= 0 AND Count <= 10) OR Override = TRUE
END WHILE
```

Invalid because one leaf is implicit:

```smile
WHILE Count < Limit AND Continue
END WHILE
```

Every leaf must be an explicit comparison.

---

# 16. Call-free condition rule

Every value used by a `WHILE` condition must already be available without invoking a function or procedure during condition evaluation.

Valid future pattern:

```smile
LET Result = FUNC(A)

WHILE Result > 10
    SET Result = Result - 1
END WHILE
```

Invalid future pattern:

```smile
WHILE FUNC(A) > 10
END WHILE
```

Invalid:

```smile
WHILE Ready = CheckReady()
END WHILE
```

The restriction applies recursively to the complete condition, including both sides of comparisons and all `AND`, `OR`, `NOT`, and parenthesized subexpressions.

Functions and procedures are not yet implemented, but this rule is permanent.

---

# 17. INPUT is not a condition operation

`INPUT` is a statement and cannot appear in a condition.

Invalid:

```smile
WHILE INPUT Continue = TRUE
END WHILE
```

Read first, then test the variable:

```smile
LET Continue = TRUE

INPUT Continue

WHILE Continue = TRUE
    PRINT Continuing
    INPUT Continue
END WHILE
```

---

# 18. Short-circuit behavior

`AND` and `OR` retain left-to-right short-circuit behavior every time the condition is evaluated.

Both sides are still parsed, name-resolved, structurally validated, and type-checked.

A runtime arithmetic failure in a right operand occurs only when that operand is reached.

---

# 19. Statements permitted inside WHILE v1.0

A `WHILE` body may contain:

- `PRINT`;
- `SET`;
- `INPUT`;
- `IF`;
- nested `WHILE`;
- full-line `REM`, `//`, `#`, and `--` comments;
- blank source lines;
- Block String Literals as the complete value of `SET`. Although the same form is valid for top-level LET, LET remains prohibited lexically inside WHILE v1.0.

Example:

```smile
LET Count = 0
LET Continue = TRUE
LET Message = ""

WHILE Continue = TRUE
    PRINT Enter a number:
    INPUT Count

    IF Count < 0 THEN
        SET Message ="
Negative value
ends the loop.
"
        SET Continue = FALSE
    ELSE
        PRINT You entered {Count}
    END IF
END WHILE

PRINT {Message}
```

---

# 20. LET is prohibited inside WHILE v1.0

SMILE v0.8.0 does not introduce block scopes.

Variables used by a loop must be declared before the loop.

Valid:

```smile
LET Count = 0

WHILE Count < 5
    SET Count = Count + 1
END WHILE
```

Invalid:

```smile
WHILE Count < 5
    LET Temporary = Count + 1
    SET Count = Temporary
END WHILE
```

The prohibition applies anywhere lexically inside a `WHILE` body, including inside nested `IF` or nested `WHILE` statements.

Block-local declarations will be addressed with formal scopes in a future milestone.

---

# 21. SET changes persist

A `SET` performed in one iteration changes the value observed by:

- later statements in the same iteration;
- the next condition evaluation;
- later iterations;
- statements after the loop if the loop exits.

```smile
LET Total = 0
LET Count = 1

WHILE Count <= 3
    SET Total = Total + Count
    SET Count = Count + 1
END WHILE

PRINT {Total}
```

Output:

```text
6
```

---

# 22. INPUT changes persist

An executed `INPUT` inside a loop atomically changes its existing target variable.

```smile
LET Command = ""

INPUT Command

WHILE Command <> "Q"
    PRINT Command={Command}
    INPUT Command
END WHILE
```

Only reached `INPUT` statements consume input lines.

If the loop body does not execute, its `INPUT` consumes nothing.

All `INPUT` conversion, size, UTF-8, runtime error, and exit-code rules remain defined by specification `008`.

---

# 23. Empty bodies

An empty `WHILE` body is syntactically valid.

```smile
WHILE Ready = FALSE
END WHILE
```

A body containing only comments and blank lines is also semantically empty:

```smile
WHILE Ready = FALSE
    REM No executable statement.

    // Still no executable statement.
END WHILE
```

Target languages that require an executable placeholder must emit one.

Examples include:

- Python `pass`;
- COBOL `CONTINUE` or equivalent structured no-op lowering.

---

# 24. Nested WHILE

A `WHILE` may contain another `WHILE`.

```smile
LET Row = 1
LET Column = 1

WHILE Row <= 2
    SET Column = 1

    WHILE Column <= 3
        PRINT Row={Row}, Column={Column}
        SET Column = Column + 1
    END WHILE

    SET Row = Row + 1
END WHILE
```

Each loop requires its own `END WHILE`.

---

# 25. IF and WHILE nesting

`IF` and `WHILE` may be nested in either direction.

WHILE inside IF:

```smile
IF Ready = TRUE THEN
    WHILE Count < Limit
        SET Count = Count + 1
    END WHILE
END IF
```

IF inside WHILE:

```smile
WHILE Count < Limit
    IF Count = 5 THEN
        PRINT Halfway
    END IF

    SET Count = Count + 1
END WHILE
```

---

# 26. Combined control-flow nesting limit

SMILE supports a maximum combined `IF` and `WHILE` nesting depth of:

```text
128
```

The first outermost `IF` or `WHILE` has depth 1.

Any nested `IF` or `WHILE` increases the same shared depth.

Depth 128 is valid.

Attempting to enter depth 129 is rejected with an opener-specific diagnostic:

- `SMILE1416` when the rejected opener is `IF`;
- `SMILE1611` when the rejected opener is `WHILE`.

This replaces the narrower implementation wording that described `SMILE1416` as IF-only depth. Existing valid IF programs through depth 128 remain unchanged.

Parser recovery must not recurse into the rejected subtree.

Comments and Block String content do not affect depth counting.

---

# 27. Comments cannot alter loop structure

Comment payloads have no structural meaning.

```smile
WHILE Count < 5
    // END WHILE
    # WHILE Count < 100
    -- END IF
    REM WEND

    SET Count = Count + 1
END WHILE
```

The comment text does not open or close a loop.

The same rule applies during deep-nesting recovery.

---

# 28. Block String content cannot alter loop structure

Inside a Block String Literal used by SET, marker-looking lines remain String data. LET remains invalid anywhere lexically inside the loop, including a LET with a Block String initializer.

```smile
LET Text = ""
LET Count = 0

WHILE Count < 1
    SET Text ="
WHILE Count < 100
END WHILE
END IF
"
    SET Count = Count + 1
END WHILE

PRINT {Text}
```

The inner text does not create or close source blocks.

---

# 29. Source comments and blank lines are preserved

Specification `007` applies inside and around `WHILE`.

```smile
LET Count = 0

// Begin counting.
WHILE Count < 3

    PRINT {Count}

    SET Count = Count + 1
END WHILE

PRINT Done
```

Generated targets preserve:

- target-native comments;
- authored blank-line boundaries;
- relative source order;
- indentation appropriate to the target loop body.

---

# 30. Infinite loops are valid

This is valid:

```smile
WHILE TRUE = TRUE
    PRINT Running
END WHILE
```

It does not terminate normally.

SMILE imposes no implicit language-level iteration limit.

A host application may provide:

- cancellation;
- timeout;
- process termination;
- bounded captured output.

Those are execution-environment protections, not changes to program semantics.

---

# 31. Runtime cancellation is host control

The reference evaluator and development tools must provide a way for the host to cancel long-running or infinite loops.

Cancellation is not a SMILE runtime error and does not use a `SMILER` code.

A cancellation-capable evaluator may throw or propagate the host platform's normal cancellation signal.

Generated programs do not receive an invisible compiler-inserted iteration cap.

---

# 32. All body statements must be valid

Every body statement is:

- parsed;
- name-resolved;
- type-checked;
- structurally validated;
- included in static analysis;
- generated for every target.

This remains true when the initial condition is known to be `FALSE`.

No body is treated as arbitrary ignored text.

---

# 33. Genuine target control flow

Every destination generator must emit a genuine runtime loop.

A generator must not:

- execute the loop during transpilation;
- unroll it into a fixed number of copies;
- delete it because the incoming condition is currently known;
- emit only one iteration;
- replace a runtime condition with a stale pre-loop value.

The complete body and condition must remain represented in target code.

---

# 34. Static analysis uses zero-or-more iterations

Unless the initial condition is proved `FALSE`, static analysis must model a `WHILE` body as executing:

```text
zero or more times
```

The analyzer must compute a conservative fixed point for:

- known versus unknown values;
- possible Boolean values;
- possible Integer ranges;
- String maximum UTF-8 lengths;
- possible embedded NUL;
- mutation tracking;
- runtime-failure reachability;
- target storage requirements.

The analyzer must terminate even when the source loop does not.

---

# 35. Known values after WHILE

A value after a loop is `Known` only when it is proved identical on every possible loop-exit path represented by the analysis.

Example:

```smile
LET Value = 1

WHILE Continue = TRUE
    SET Value = 1
    INPUT Continue
END WHILE

PRINT {Value}
```

`Value` may remain known as `1` because the zero-iteration path and every body assignment agree.

Example:

```smile
LET Value = 1

WHILE Continue = TRUE
    SET Value = 2
    INPUT Continue
END WHILE

PRINT {Value}
```

`Value` is not one statically known value after the loop because the loop may execute zero times or one or more times.

---

# 36. Known-false incoming condition

When the incoming condition is proved `FALSE` and evaluating it cannot fail, analysis may preserve the incoming value environment after the loop.

The body is still validated and generated.

Example:

```smile
LET Value = 1

WHILE FALSE = TRUE
    SET Value = 2
END WHILE

PRINT {Value}
```

Output:

```text
1
```

---

# 37. Integer loop-carried values

Integer values may change an arbitrary number of times.

```smile
LET Count = 0

WHILE Continue = TRUE
    SET Count = Count + 1
    INPUT Continue
END WHILE
```

Static analysis must widen the possible range conservatively when repeated transfer would continue expanding it.

Generated code must retain the signed 64-bit checked runtime arithmetic semantics introduced by specifications `004` and `008`.

Overflow and division-by-zero errors occur only if the failing operation is reached at runtime.

---

# 38. Portable bounded-String rule for WHILE v1.0

Current low-level SMILE targets plan exact or conservatively bounded UTF-8 storage.

Therefore, SMILE v0.8.0 requires every String value assigned through a `WHILE` loop to have a finite compile-time maximum UTF-8 byte length.

Valid bounded examples:

```smile
LET Text = ""
LET Continue = TRUE

WHILE Continue = TRUE
    INPUT Text
    INPUT Continue
END WHILE
```

For static loop analysis, `Text` receives a finite conservative planning bound from INPUT. That internal fact is not a public line-length limit and does not require every target to use the same input capacity or runtime implementation.

Valid:

```smile
LET Text = ""
LET Continue = TRUE

WHILE Continue = TRUE
    SET Text = "Fixed"
    INPUT Continue
END WHILE
```

Invalid in v1.0:

```smile
LET Text = ""
LET Continue = TRUE

WHILE Continue = TRUE
    SET Text = Text + "x"
    INPUT Continue
END WHILE
```

The final example can grow by one byte on every iteration and has no finite portable bound.

It produces `SMILE1612`.

---

# 39. No trip-count proof in WHILE v1.0 String sizing

WHILE v1.0 does not rely on proving a maximum iteration count from the condition.

Therefore, this is also rejected by the portable bounded-String rule:

```smile
LET Text = ""
LET Count = 0

WHILE Count < 5
    SET Text = Text + "x"
    SET Count = Count + 1
END WHILE
```

A human can see that the loop normally runs five times, but v1.0's portable analysis treats `WHILE` as zero or more iterations rather than as a counted loop.

A future specification may add:

- dynamic unbounded String storage;
- a counted `FOR` loop;
- stronger trip-count proof;
- or another safe mechanism.

Relaxing this restriction later is additive and does not invalidate programs accepted by v1.0.

---

# 40. Stable bounded String assignments

A loop-carried String assignment is valid when repeated analysis reaches the same finite upper bound.

Examples that may remain valid:

```smile
SET Text = Text
```

```smile
SET Text = Text + ""
```

```smile
SET Text = OtherBoundedText
```

```smile
INPUT Text
```

The complete body transfer, not one isolated statement spelling, determines whether the maximum is stable.

---

# 41. String equality and output inside loops

All existing exact String semantics remain unchanged.

A loop may:

- compare complete Strings;
- print complete Strings;
- preserve embedded NUL;
- reassign bounded Strings;
- read bounded String input;
- interpolate bounded String values.

C, Objective-C, COBOL, and MASM must continue using exact logical lengths where required.

---

# 42. Source-order runtime errors

If a runtime error occurs during:

- condition evaluation;
- a body expression;
- `INPUT`;
- checked Integer arithmetic;

the program:

1. preserves stdout already produced;
2. writes the canonical runtime error to stderr;
3. terminates with exit code 1;
4. performs no later body statement, iteration, or post-loop statement.

Existing runtime codes remain defined by specifications `004` and `008`.

---

# 43. No BREAK or CONTINUE in v1.0

SMILE v0.8.0 does not add:

```text
BREAK
CONTINUE
EXIT WHILE
```

To stop a loop, update a variable used by the condition.

Example:

```smile
LET Running = TRUE
LET Command = ""

WHILE Running = TRUE
    INPUT Command

    IF Command = "Q" THEN
        SET Running = FALSE
    ELSE
        PRINT Command={Command}
    END IF
END WHILE
```

`BREAK` and `CONTINUE` are not reserved SMILE keywords in v1.0.

---

# 44. No post-test loop in v1.0

SMILE v0.8.0 does not add:

```text
DO
LOOP
REPEAT
UNTIL
```

`WHILE` always tests before its body.

Other loop forms may be specified later.

---

# 45. Target-language mappings

The three active targets must preserve pre-test loop behavior. The retained paused generators must meet the same structural rule when each target is deliberately re-enabled.

Recommended structures:

| Target | Required general structure |
|---|---|
| C# | `while (condition) { ... }` |
| C | `while (condition) { ... }` |
| C++ | `while (condition) { ... }` |
| JavaScript | `while (condition) { ... }` |
| Java | `while (condition) { ... }` |
| Objective-C | `while (condition) { ... }` |
| Swift | `while condition { ... }` |
| Python | `while condition:` |
| COBOL | structured `PERFORM` with a condition re-evaluated before each SMILE body iteration |
| Windows x64 MASM | deterministic condition, body, back-edge, and exit labels |

Target syntax may differ, but ordinary observable loop behavior must match the reference evaluator, subject only to the documented target-native INPUT tradeoffs in specification 008.

---

# 46. Python empty-body lowering

When the WHILE body has no semantic statement, generated Python must include `pass`.

Preserved comments and blank lines may appear before `pass`.

Example shape:

```python
while condition:
    # preserved comment

    pass
```

---

# 47. COBOL condition re-evaluation

COBOL lowering must recompute the complete SMILE condition before every possible body execution.

A condition helper field may be used, but it must not be computed once and reused forever.

Structured `PERFORM`, `EXIT PERFORM`, helper paragraphs, or equivalent GnuCOBOL-compatible lowering may be used as long as:

- the condition is tested before the first body execution;
- the condition is re-evaluated after every iteration;
- runtime errors and short-circuiting remain exact;
- comments and blank lines remain preserved;
- generated warnings remain zero.

---

# 48. MASM deterministic labels

MASM x64 lowering must use deterministic compiler-owned labels equivalent to:

```text
while_1_condition
while_1_body
while_1_end
```

The exact spelling may follow the existing generator convention.

Labels must remain collision-safe and deterministic across repeated generation.

Nested IF and WHILE labels must not collide.

---

# 49. No source-to-target line-number identity requirement

One SMILE WHILE statement may expand into:

- helper calls;
- checked arithmetic;
- condition temporaries;
- labels;
- runtime error branches;
- target boilerplate.

Source and target physical line numbers do not need to match.

Comments, blank lines, and semantic body order must remain represented in the nearest corresponding target region.

---

# 50. Reserved keyword

Beginning with v0.8.0:

```text
WHILE
```

is reserved and cannot be used as a variable name.

Invalid:

```smile
LET WHILE = 1
```

`END` is already reserved by IF.

`WEND`, `LOOP`, `BREAK`, and `CONTINUE` do not become reserved merely because they appear in this specification as unsupported forms.

---

# 51. Invalid examples

Missing condition:

```smile
WHILE
END WHILE
```

Implicit Boolean:

```smile
WHILE Running
END WHILE
```

THEN is not part of WHILE:

```smile
WHILE Count < 5 THEN
END WHILE
```

DO is not part of WHILE:

```smile
WHILE Count < 5 DO
END WHILE
```

Missing terminator:

```smile
WHILE Count < 5
    SET Count = Count + 1
```

Wrong terminator:

```smile
WHILE Count < 5
WEND
```

LET inside loop:

```smile
WHILE Count < 5
    LET Next = Count + 1
END WHILE
```

Unbounded String growth:

```smile
WHILE Continue = TRUE
    SET Text = Text + "x"
END WHILE
```

Function call in condition:

```smile
WHILE CheckReady() = TRUE
END WHILE
```

---

# 52. Diagnostics

| Code | Meaning |
|---|---|
| `SMILE1601` | `WHILE` must be followed by whitespace |
| `SMILE1602` | `WHILE` requires a condition |
| `SMILE1603` | Every atomic WHILE condition must be an explicit comparison |
| `SMILE1604` | The complete WHILE condition must have type Boolean |
| `SMILE1605` | A WHILE condition cannot invoke a function or procedure |
| `SMILE1606` | Unexpected content follows the WHILE condition |
| `SMILE1607` | WHILE requires a matching END WHILE |
| `SMILE1608` | END WHILE must contain two keywords and stand alone |
| `SMILE1609` | END WHILE has no matching WHILE |
| `SMILE1610` | LET is not permitted inside WHILE v1.0 |
| `SMILE1611` | Maximum combined IF/WHILE nesting depth of 128 exceeded at WHILE |
| `SMILE1612` | A WHILE loop produces a String value without a finite compile-time UTF-8 size bound |

Existing ordinary expression diagnostics remain applicable to malformed condition expressions.

`WEND`, `ENDWHILE`, and `LOOP` may continue to receive the ordinary unknown-statement diagnostic rather than a WHILE-specific alias diagnostic.

---

# 53. Normative acceptance program

```smile
REM SMILE v0.8.0 WHILE acceptance program

LET Count = 0
LET Total = 0

PRINT Enter a positive count:
INPUT Count

WHILE Count > 0
    SET Total = Total + Count
    PRINT Count={Count}, Total={Total}
    SET Count = Count - 1
END WHILE

PRINT Done. Total={Total}
```

Scripted input:

```text
3
```

Required stdout:

```text
Enter a positive count:
Count=3, Total=3
Count=2, Total=5
Count=1, Total=6
Done. Total=6
```

Required stderr is empty.

Required exit code:

```text
0
```

All three active targets must produce this result. Paused targets must add equivalent current conformance before re-enablement.

---

# 54. Zero-iteration acceptance program

```smile
LET Count = 0

WHILE Count > 0
    PRINT Never
END WHILE

PRINT Done
```

Required output:

```text
Done
```

The target source must still contain a genuine loop and its body.

---

# 55. Nested acceptance program

```smile
LET Row = 1
LET Column = 1

WHILE Row <= 2
    SET Column = 1

    WHILE Column <= 2
        PRINT {Row},{Column}
        SET Column = Column + 1
    END WHILE

    SET Row = Row + 1
END WHILE
```

Required output:

```text
1,1
1,2
2,1
2,2
```

---

# 56. Infinite-loop conformance

A conformance test may use:

```smile
WHILE TRUE = TRUE
END WHILE
```

only with explicit host cancellation or timeout.

The test must confirm:

- the parser, binder, analyzer, simplifier, and generator terminate;
- generated code contains a genuine loop;
- the evaluator can be cancelled;
- a child process can be terminated;
- the test suite itself does not hang.

---

# 57. Backward compatibility

`WHILE` is additive except that `WHILE` becomes reserved.

Existing valid v0.7.0 programs that do not use `WHILE` retain the same meaning.

The combined nesting limit preserves the existing valid IF range through depth 128.

No existing comment, blank-line, String, arithmetic, IF, or INPUT semantics are weakened.

---

# 58. Future compatibility

Future loop, function, and scope milestones must preserve these rules unless a later official specification explicitly supersedes them:

- WHILE is a pre-test loop;
- the condition is re-evaluated before every body iteration;
- condition leaves remain explicit comparisons;
- condition evaluation remains call-free;
- only reached paths consume input or produce runtime errors;
- no implicit iteration cap exists;
- runtime state persists between iterations;
- genuine control flow is retained in every target;
- comments and blank lines remain preserved;
- future block scopes may permit LET in loop bodies;
- future dynamic String support may remove the bounded-String v1.0 restriction;
- future `BREAK`, `CONTINUE`, `FOR`, `DO`, or `UNTIL` syntax requires separate official specifications.
