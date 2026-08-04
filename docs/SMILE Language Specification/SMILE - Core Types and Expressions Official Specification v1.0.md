# SMILE - Core Types and Expressions Official Specification v1.0

This specification was introduced in SMILE v0.4.1 and remains normative for v0.4.2.1 and later unless superseded by a newer official specification.

## Core Types

The SMILE v1.0 expression core has three value types:

| Type | Meaning | Display Text |
|---|---|---|
| `String` | Text | The string itself |
| `Integer` | Signed 64-bit integer | Decimal digits using invariant culture |
| `Boolean` | Truth value | `TRUE` or `FALSE` |

`Integer` is a signed 64-bit SMILE semantic type regardless of target-language storage. A target generator MAY use a narrower natural destination type for a complete program only when every bound Integer literal, value, operand, and intermediate result is proven to fit that type. This target-local storage choice MUST NOT change the valid SMILE range, checked overflow behavior, division semantics, or evaluator output.

All current `LET` initializers are compile-time evaluable because SMILE v0.4.2.1 has no runtime input, reassignment, or side effects.

## Lexical Tokens

The lexer recognizes:

- identifiers;
- string literals;
- integer literals;
- `LET`, `PRINT`, `TRUE`, `FALSE`, `NOT`, `AND`, and `OR`;
- `+`, `-`, `*`, `/`;
- `=`, `<>`, `<`, `<=`, `>`, `>=`;
- `(` and `)`;
- line endings and end of file.

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

## Canonical Expression Representation

Each expression concept has one syntax representation and one bound representation. Every binary operator, including String `+`, is represented by:

```text
BinaryExpressionSyntax
BoundBinaryExpression
```

The bound operator distinguishes Integer addition from `StringConcatenation`. Implementations must not maintain a second concatenation syntax or bound node in parallel with the typed binary-expression path.

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

After binding succeeds, the shared simplifier may use previously declared constant Boolean values to make the same reachability decision in every expression position. It must decide whether the right operand is reachable before simplifying that operand. Binding still resolves and type-checks both sides first. This constant-aware rule is valid while SMILE has no input, reassignment, functions, or side effects; future runtime features must preserve left-to-right evaluation and may fold only expressions proven safe.

The same failure remains an error when the right operand is reachable:

```basic
LET Result = TRUE AND (1 / 0 = 0)
```

This produces `SMILE1207`. Likewise, reachable signed 64-bit overflow produces `SMILE1206`.

These rules remain normative when future SMILE versions add runtime expressions, functions, or other operations with observable evaluation behavior.

## Interpolation

`$"..."` strings and raw `PRINT` templates may contain `{expression}` holes. The expression inside a hole is parsed and type-checked with the normal expression grammar. The inserted text is the expression value's display text.

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

Integers are signed 64-bit values. `-9223372036854775808` through `9223372036854775807` are valid. Arithmetic overflow and division by zero are compile-time errors.

Division uses integer division with truncation toward zero.

The valid signed boundaries are `-9223372036854775808` and `9223372036854775807`. Overflow includes, but is not limited to:

- `-9223372036854775808 / -1`;
- `-(-9223372036854775808)`;
- `9223372036854775807 + 1`;
- `-9223372036854775808 - 1`;
- `3037000500 * 3037000500`.

Each produces `SMILE1206` when evaluated.

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

## Target Generation Rule

Every target generator must consume the shared bound tree produced by the lexer, parser, binder, and evaluator. A target generator must not invent its own expression semantics or reparse SMILE source text.

C and Objective-C preserve native Integer and Boolean expression intent where the destination language has a direct equivalent. String-producing declarations may continue to use evaluated constants while SMILE has no runtime string library. C-family NUL-free String equality uses value comparison such as `strcmp`, not pointer equality; NUL-sensitive equality must account for the complete length and bytes or use the exact evaluated Boolean. Compiler-owned `printf` format strings use `%d`, `%lld`, `%s`, and safe literal-percent escaping as appropriate. A NUL-containing String uses length-aware byte output so `%s` cannot truncate the value.

Deterministic generated-expression conformance tests use a fixed seed, evaluate one larger valid SMILE program with `SmileEvaluator`, build every locally available target, normalize line endings only where explicitly allowed, and compare all remaining stdout bytes exactly without trimming control characters.
