# SMILE - String Literals Official Specification v1.0

This specification was introduced in SMILE v0.4.1 and remains normative for SMILE v0.5.0 and later unless superseded by a newer official specification.

## Purpose

Ordinary String literals are the one-line source form for fixed text values. They are used by `LET`, ordinary `SET` expressions, `PRINT`, and interpolation text.

SMILE v0.5.0 also defines one deliberately separate multiline source form: the [SET Block String Literal — The SMILE Way](001%20-%20SMILE%20-%20SET%20Statement%20Official%20Specification%20v1.0.md). It is valid only as the complete value of `SET`. It does not make ordinary String expressions multiline-capable.

## Source Form

An ordinary String literal begins with `"` and ends with the next unescaped `"` on the same physical source line.

```basic
"Hello"
"She said \"Hello\"."
"C:\\SMILE"
```

SMILE also accepts legacy left and right smart quote characters as double quotes for beginner-friendly recovery, but generated examples should use ordinary ASCII quotes.

A quote that ends a physical SET line may instead begin a SET Block String Literal. The SET specification exclusively defines its opening and closing delimiters, structural indentation removal, logical `\n` normalization, complete-value placement, and diagnostics. The front end normalizes that form to one ordinary String value before binding.

SMILE v0.6.1 full-line comment markers inside ordinary or interpolated Strings remain String data. Comment and blank-line recognition is suspended inside a SET Block String Literal, so `REM`, `//`, `#`, `--`, and blank physical content lines retain their exact String meaning. See the [007 - Full-Line Comments and Source Layout Preservation specification](007%20-%20SMILE%20-%20Full-Line%20Comments%20and%20Source%20Layout%20Preservation%20Official%20Specification%20v1.0.md).

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

SET Block String content decodes this same escape table. Quotes embedded in ordinary block content lines remain literal; the closing delimiter is recognized structurally as defined by the SET specification.

Raw `PRINT` template text does not process backslash escapes. For example:

```basic
PRINT C:\SMILE\n
```

prints the backslash and `n` literally, followed by the normal `PRINT` line ending.

## Diagnostics

| Code | Meaning |
|---|---|
| `SMILE1003` | Unterminated ordinary String literal or SET Block String Literal |
| `SMILE1208` | Unknown or invalid string escape sequence |
| `SMILE1209` | Unterminated string escape sequence |

SET-only placement and delimiter diagnostics `SMILE1306` through `SMILE1308` are defined by the official SET specification.

## Target Generation

Target generators must emit destination-language string syntax that preserves the complete SMILE String value. Generators may choose each target language's normal escape spelling as long as the runtime text is identical to the reference evaluator. Destination representations that normally terminate at NUL must carry an exact length whenever the value contains `\0`. Generators receive only the normalized value and must never inspect block delimiters, source indentation, or physical line endings.

Conformance tests must compare exact values or captured bytes for NUL, backspace, form feed, tab, carriage return, and line feed. Tests must not trim control characters. Line-ending normalization is allowed only when a test is specifically comparing platform line endings.

C and Objective-C may use ordinary `%s` output and `strcmp` equality only for values proven to contain no embedded NUL. A NUL-containing value is emitted through compiler-owned UTF-8 byte data plus an exact byte length. A NUL-sensitive equality may be lowered to a static Boolean result only when branch-aware analysis proves that result on every possible incoming path; otherwise the target must compare current runtime lengths and bytes. This keeps bytes after NUL observable and remains correct after `SET` or an IF merge changes the current value.
