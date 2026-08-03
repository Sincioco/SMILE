# SMILE - String Literals Official Specification v1.0

This document defines string literal behavior for SMILE v0.4.0.

## Purpose

String literals are the source form for fixed text values. They are used by `LET`, `PRINT`, and interpolation text.

## Source Form

A string literal begins with `"` and ends with the next unescaped `"`.

```basic
"Hello"
"She said \"Hello\"."
"C:\\SMILE"
```

SMILE also accepts legacy left and right smart quote characters as double quotes for beginner-friendly recovery, but generated examples should use ordinary ASCII quotes.

## Official Escape Sequences

| Escape | Meaning |
|---|---|
| `\\` | Backslash |
| `\"` | Double quote |
| `\n` | Line feed |
| `\r` | Carriage return |
| `\t` | Horizontal tab |
| `\0` | NUL character |
| `\b` | Backspace |
| `\f` | Form feed |

No other escape sequence is valid in v1.0. For example, `\q`, `\a`, and `\v` are errors in SMILE source even if some destination language has such escapes.

## Diagnostics

| Code | Meaning |
|---|---|
| `SMILE1003` | Unterminated string literal |
| `SMILE1208` | Unknown or invalid string escape sequence |
| `SMILE1209` | Unterminated string escape sequence |

## Target Generation

Target generators must emit destination-language string syntax that preserves the exact SMILE string value. Generators may choose each target language's normal escape spelling as long as the runtime text is identical to the reference evaluator.
