# SMILE - Core Types and Expressions Official Specification v1.0

This document defines the SMILE v0.4.0 lexical and typed expression core.

## Core Types

SMILE v0.4.0 has three value types:

| Type | Meaning | Display Text |
|---|---|---|
| `String` | Text | The string itself |
| `Integer` | Signed 64-bit integer | Decimal digits using invariant culture |
| `Boolean` | Truth value | `TRUE` or `FALSE` |

All current `LET` initializers are compile-time evaluable because SMILE v0.4.0 has no runtime input, reassignment, or side effects.

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
