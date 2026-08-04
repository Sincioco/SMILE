# SMILE - String Literals Official Specification v1.0

This specification was introduced in SMILE v0.4.1 and remains normative for v0.4.2.1 and later unless superseded by a newer official specification.

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

Target generators must emit destination-language string syntax that preserves the complete SMILE String value. Generators may choose each target language's normal escape spelling as long as the runtime text is identical to the reference evaluator. Destination representations that normally terminate at NUL must carry an exact length whenever the value contains `\0`.

Conformance tests must compare exact values or captured bytes for NUL, backspace, form feed, tab, carriage return, and line feed. Tests must not trim control characters. Line-ending normalization is allowed only when a test is specifically comparing platform line endings.

C and Objective-C may use ordinary `%s` output and `strcmp` equality only for values proven to contain no embedded NUL. A NUL-containing value is emitted through compiler-owned UTF-8 byte data plus an exact byte length, and a NUL-sensitive equality is lowered to its already evaluated Boolean result while all expressions remain pure compile-time constants. This keeps bytes after NUL observable without introducing a general String runtime.
