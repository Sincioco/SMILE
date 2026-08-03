# SMILE - String Literals Official Specification v1.0

This document defines string literal behavior for SMILE v0.4.1.

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

Raw `PRINT` template text does not process backslash escapes. For example:

```basic
PRINT C:\SMILE\n
```

prints the backslash and `n` literally, followed by the normal `PRINT` line ending.

## Diagnostics

| Code | Meaning |
|---|---|
| `SMILE1003` | Unterminated string literal |
| `SMILE1208` | Unknown or invalid string escape sequence |
| `SMILE1209` | Unterminated string escape sequence |

## Target Generation

Target generators must emit destination-language string syntax that preserves the exact SMILE string value. Generators may choose each target language's normal escape spelling as long as the runtime text is identical to the reference evaluator.

Conformance tests must compare exact values or captured bytes for NUL, backspace, form feed, tab, carriage return, and line feed. Tests must not trim control characters. Line-ending normalization is allowed only when a test is specifically comparing platform line endings.

A target may keep a NUL out of a C-family `printf` format string and emit it through a compiler-owned `%c` argument. This is semantically equivalent and prevents the NUL from terminating the generated format string early.
