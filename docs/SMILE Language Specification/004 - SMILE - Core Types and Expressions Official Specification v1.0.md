# SMILE - Core Types and Expressions Official Specification v1.0

This specification was introduced in SMILE v0.4.1 and remains normative for SMILE v0.8.0 and later unless superseded by a newer official specification. IF- and WHILE-specific condition structure is additionally defined by the official [006 - IF Statement specification](006%20-%20SMILE%20-%20IF%20Statement%20Official%20Specification%20v1.0.md) and [009 - WHILE Statement specification](009%20-%20SMILE%20-%20WHILE%20Statement%20Official%20Specification%20v1.0.md), while runtime-unknown value introduction is defined by the [008 - INPUT Statement specification](008%20-%20SMILE%20-%20INPUT%20Statement%20Official%20Specification%20v1.0.md).

## Core Types

The SMILE v1.0 expression core has three value types:

| Type | Meaning | Display Text |
|---|---|---|
| `String` | Text | The string itself |
| `Integer` | Signed 64-bit integer | Ordinary decimal digits |
| `Boolean` | Truth value | `TRUE` or `FALSE` |

`Integer` is a signed 64-bit SMILE semantic type for source literals, binding, analysis, and the reference evaluator. A target generator may use a narrower natural destination type when the program's source-known literals, values, operands, and intermediates fit it. Runtime INPUT uses the conventional target conversion selected under specification `008`; a host-native out-of-range failure need not be simulated through a shared generated parser. Source-known overflow, division semantics, and evaluator behavior remain defined here.

SMILE v0.8.0 has mutable variables through `SET`, runtime values through `INPUT`, conditional branches through `IF`, and pre-test loops through `WHILE`, but it still has no function. Static evaluation MUST distinguish Known, runtime-Unknown, and Invalid. Analysis MUST be statement-order, mutation aware, branch aware, and loop-fixed-point aware. An earlier value MUST NOT be propagated past a later SET or INPUT, a branch-specific value MUST NOT be propagated after IF unless every possible outgoing path proves the same value, and a pre-loop value MUST NOT be reused at a WHILE head after a possible body mutation. WHILE is analyzed structurally as zero or more iterations; the compiler MUST NOT execute or unroll learner loops to obtain expression facts.

After INPUT, the variable's type remains known and its value becomes runtime-Unknown. Unknown is valid and MUST NOT be reported as a compile diagnostic or replaced by the earlier LET/SET value. An analyzer may retain conservative capacity or NUL facts for internal planning, but those facts do not define a universal INPUT byte limit or require generated targets to reproduce identical host-input edge behavior. The current INPUT specification governs target-native conversion and runtime differences.

## Lexical Tokens

The lexer recognizes:

- identifiers;
- string literals;
- integer literals;
- `LET`, `SET`, `INPUT`, `PRINT`, `IF`, `WHILE`, `THEN`, `ELSE`, `END`, `TRUE`, `FALSE`, `NOT`, `AND`, and `OR`;
- the dedicated lexical representation for a Block String Literal used as a complete LET or SET value;
- `+`, `-`, `*`, `/`;
- `=`, `<>`, `<`, `<=`, `>`, `>=`;
- `(` and `)`;
- line endings and end of file.

The public full-source lexer also retains v0.6.1 full-line comments for tooling. Comment recognition is contextual to physical source-line position and is not part of the bounded expression lexer, so marker-looking inline expression or String text is never deleted. Ordered blank-line items are parser layout rather than expression tokens. See the [007 - Full-Line Comments and Source Layout Preservation specification](007%20-%20SMILE%20-%20Full-Line%20Comments%20and%20Source%20Layout%20Preservation%20Official%20Specification%20v1.0.md).

Keywords are case-insensitive. Identifiers are ASCII-only, begin with a letter or `_`, and then contain letters, digits, or `_`.

## Expression Grammar

```text
expression     -> unary (binary-operator unary)*
unary          -> ('+' | '-' | NOT) unary | primary
primary        -> string-literal
                | integer-literal
                | TRUE
                | FALSE
                | identifier
                | '(' expression ')'
                | interpolated-string
```

Operator precedence, from strongest to weakest:

| Precedence | Operators |
|---|---|
| 7 | unary `+`, unary `-`, `NOT` |
| 6 | `*`, `/` |
| 5 | `+`, `-` |
| 4 | `<`, `<=`, `>`, `>=` |
| 3 | `=`, `<>` |
| 2 | `AND` |
| 1 | `OR` |

Binary operators are left-associative.

The grammar above defines ordinary expressions. `SET` is an expression-assignment statement and `INPUT` is a runtime-input statement; neither is an expression or adds an assignment operator. A Block String Literal is normalized before binding and is valid only as the complete value of `LET` or `SET`; it is not a general expression primary.

Curly braces `{ }` identify interpolation holes in text-oriented syntax. They are not part of the ordinary expression grammar and are not general variable-reference delimiters. An ordinary expression reads a variable by its identifier directly, as in `Name = "Sin"`; braces appear only around an expression embedded in a raw `PRINT` template or `$"..."` interpolated String. Forms such as `INPUT {Name}`, `SET {Name} = ...`, and `IF {Name} = ...` are invalid.

## Canonical Expression Representation

Each expression concept has one syntax representation and one bound representation. Every binary operator, including String `+`, is represented by:

```text
BinaryExpressionSyntax
BoundBinaryExpression
```

The bound operator distinguishes Integer addition from `StringConcatenation`. Implementations must not maintain a second concatenation syntax or bound node in parallel with the typed binary-expression path.

A variable-reference expression reads the current value associated with its `VariableSymbol` in the evaluator environment at that statement position. `LET` establishes the initial value, `SET` replaces it only after its complete right side evaluates successfully, and `INPUT` replaces it only after one complete line is read, validated, and converted successfully. Current runtime state MUST NOT be stored permanently on `BoundLetStatement`, and a pre-input value MUST NOT stand in for the runtime result.

## Operator Types

| Operator | Operand Types | Result Type |
|---|---|---|
| unary `+`, unary `-` | `Integer` | `Integer` |
| `NOT` | `Boolean` | `Boolean` |
| `+`, `-`, `*`, `/` | `Integer`, `Integer` | `Integer` |
| `+` | `String`, `String` | `String` |
| `=`, `<>` | matching `String`, `Integer`, or `Boolean` operands | `Boolean` |
| `<`, `<=`, `>`, `>=` | `Integer`, `Integer` | `Boolean` |
| `AND`, `OR` | `Boolean`, `Boolean` | `Boolean` |

SMILE does not perform implicit conversions in v1.0. For example, `"Age " + 49` is invalid because one operand is `String` and the other is `Integer`.

## Boolean Evaluation And Short-Circuiting

`AND` and `OR` evaluate operands from left to right and use short-circuit evaluation.

| Expression shape | Right operand evaluated? | Result rule |
|---|---|---|
| `FALSE AND right` | No | `FALSE` |
| `TRUE AND right` | Yes | value of `right` |
| `TRUE OR right` | No | `TRUE` |
| `FALSE OR right` | Yes | value of `right` |

Short-circuiting affects evaluation only. Parsing, name resolution, and type checking still examine both operands. Therefore these are invalid even though the right operand would be unreachable during evaluation:

```basic
LET Result = FALSE AND MissingName
LET Other = TRUE OR 42
```

The first has an undefined variable and the second has an invalid Boolean operand type.

Evaluation-time failures in an unreachable operand are not produced:

```basic
LET Result = FALSE AND (1 / 0 = 0)
PRINT {Result}
```

Output:

```text
FALSE
```

After binding succeeds, the shared simplifier may use the current known Boolean values at each statement position to make the same reachability decision in every expression position. It must decide whether the right operand is reachable before simplifying that operand. Binding still resolves and type-checks both sides first. For `SET`, the right side is simplified and evaluated using the old environment, and the known value changes only after the complete assignment succeeds. For `IF`, each branch begins from the same incoming environment and outgoing paths merge before a later statement can use a known value. For `WHILE`, the condition and body may use only stable loop-head facts valid for every possible iteration. Simplification MUST NOT delete an IF clause/body or a WHILE condition/body, execute or unroll a loop, duplicate INPUT, hoist SET, or replace a current loop-carried read with its stale pre-loop value.

The same source-known failure remains a compile error when the right operand is definitely reachable:

```basic
LET Result = TRUE AND (1 / 0 = 0)
```

This produces `SMILE1207`. Likewise, definitely reached source-known signed 64-bit overflow produces `SMILE1206`.

When reachability or an operand depends on INPUT or loop-carried storage, the compiler validates names and types but does not invent a value or report a conditional evaluation failure. If execution reaches a failing runtime operation, it produces `SMILER1206` or `SMILER1207`. If short-circuiting, branch selection, or a false WHILE condition makes that operation unreachable, no runtime error occurs.

These rules remain normative when future SMILE versions add runtime expressions, functions, or other operations with observable evaluation behavior.

## IF And WHILE Condition Context

The official ordinary expression grammar is reused inside IF, ELSE IF, and WHILE headers. IF and WHILE add the same permanent structural restrictions:

- the complete condition result MUST have type Boolean;
- every atomic Boolean leaf MUST be an explicit comparison using `=`, `<>`, `<`, `<=`, `>`, or `>=` and a right-hand operand;
- a standalone Boolean variable or literal, including one wrapped in parentheses or NOT, is not an IF or WHILE condition;
- a compound AND/OR/NOT condition is valid only when every leaf is an explicit comparison;
- an IF or WHILE condition MUST NOT invoke a function or procedure.

These restrictions are validated on the unsimplified bound expression so simplification cannot erase an explicit `= TRUE` comparison. Binding and type checking still examine both sides of a short-circuit operator and every source branch or loop body. The complete statement syntax, diagnostics, and future call-prohibition contracts are defined by [006 - SMILE - IF Statement Official Specification v1.0](006%20-%20SMILE%20-%20IF%20Statement%20Official%20Specification%20v1.0.md) and [009 - SMILE - WHILE Statement Official Specification v1.0](009%20-%20SMILE%20-%20WHILE%20Statement%20Official%20Specification%20v1.0.md).

## Interpolation

`$"..."` strings and raw `PRINT` templates may contain `{expression}` holes. The expression inside a hole is parsed and type-checked with the normal expression grammar. The inserted text is the expression value's display text.

Block String Literals do not interpolate. Their normalized value is one ordinary bound String literal.

```basic
LET Age = 49
LET Adult = Age >= 18
PRINT Age={Age}, Adult={Adult}
```

Output:

```text
Age=49, Adult=TRUE
```

## Integer Semantics

Integers are signed 64-bit values. `-9223372036854775808` through `9223372036854775807` are valid. Arithmetic overflow and division by zero are compile-time errors only when source-known evaluation proves the failing operation is definitely reached. Runtime-dependent operations are checked when executed.

Division uses integer division with truncation toward zero.

The valid signed boundaries are `-9223372036854775808` and `9223372036854775807`. Overflow includes, but is not limited to:

- `-9223372036854775808 / -1`;
- `-(-9223372036854775808)`;
- `9223372036854775807 + 1`;
- `-9223372036854775808 - 1`;
- `3037000500 * 3037000500`.

Each produces `SMILE1206` when definitely evaluated from source-known values. The equivalent reached runtime-dependent operation produces `SMILER1206`. Reached runtime division by zero produces `SMILER1207`.

## Equality Semantics

String equality and inequality compare the complete String value case-sensitively using ordinal value semantics. Identifier lookup remains case-insensitive; that identifier rule does not change String data.

```basic
LET Same = "Sin" = "Sin"
LET DifferentCase = "Sin" = "sin"
```

`Same` is `TRUE`; `DifferentCase` is `FALSE`. Equality applies equally to literals, variables, concatenation results, and interpolation-produced strings.

## Diagnostics

| Code | Meaning |
|---|---|
| `SMILE1201` | Invalid or unexpected token in expression |
| `SMILE1202` | Integer literal is outside the signed 64-bit range |
| `SMILE1203` | Unary operator is not defined for the operand type |
| `SMILE1204` | Binary operator is not defined for the operand types |
| `SMILE1205` | Missing closing parenthesis |
| `SMILE1206` | Integer arithmetic overflow |
| `SMILE1207` | Division by zero |
| `SMILE1208` | Unknown or invalid string escape sequence |
| `SMILE1209` | Unterminated string escape sequence |

Runtime arithmetic errors are separate from compile diagnostics:

| Code | Meaning |
|---|---|
| `SMILER1206` | Reached runtime Integer arithmetic overflow |
| `SMILER1207` | Reached runtime division by zero |

A runtime error preserves stdout already produced, writes exactly one canonical stderr line plus its line ending, stops later statements, and exits with code 1.

## Target Generation Rule

Every target generator must consume the shared bound tree and recursive branch/loop-aware analysis produced by the lexer, parser, binder, and evaluator. A target generator must not invent its own expression semantics, reparse SMILE source text, interpret Block String delimiters, substitute a pre-input or pre-loop value, delete an INPUT or IF clause because current values make another branch predictable, or delete/unroll a WHILE because its incoming condition is known.

C and other low-level targets preserve native Integer and Boolean expression intent where the destination language has a direct equivalent. A low-level target may use an evaluated value only when the corresponding stable branch/loop-aware fact is `Known` on every possible incoming path; an `Unknown` expression must read current runtime storage. Every `SET` and `INPUT` must still emit a real storage update at its source position, and every WHILE condition must be re-evaluated before each possible body execution. Target-native INPUT may use the destination's conventional parsing, storage, and failure behavior as defined by specification `008`; an INPUT alone does not justify a generated exact-byte runtime. C-family String equality uses value comparison rather than pointer equality whenever ordinary native String values make that comparison meaningful.

Focused generated-expression tests protect the changed active targets and compare ordinary successful behavior with `SmileEvaluator` where that comparison is meaningful. Exact malformed-input bytes, identical host-library diagnostics, all-target execution, and paused-target parity are not routine language requirements. Broader active-target or restored-target conformance belongs to milestones, releases, target re-enablement, or explicit requests.
