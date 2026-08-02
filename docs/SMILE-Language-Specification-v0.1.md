# SMILE Language Specification v0.1

SMILE v0.1 supports one statement: `PRINT "text"`.

The current v0.1.2 toolchain can generate C#, C, Windows x64 MASM, JavaScript, Java, Objective-C, and Swift from that same one-statement syntax.

## Grammar

```text
program         -> line* end-of-file
line            -> whitespace* statement? whitespace* newline
statement       -> print-statement
print-statement -> PRINT whitespace+ string-literal
```

## Valid Source

```basic
PRINT "Hello World"
Print "Keywords are case-insensitive"
print "Smart quote delimiters are accepted when typed as smart quotes"

PRINT "Multiple statements are supported"
PRINT "Every PRINT ends with a newline"
```

Rules:

- `PRINT` is case-insensitive.
- Blank lines are allowed.
- Multiple `PRINT` statements are allowed.
- A final newline is optional.
- Straight double quotes are accepted.
- Smart opening and closing double quotes are accepted.
- `PRINT` appends a newline.
- User-visible line and column values are one-based.

## Diagnostics

Expected source errors return diagnostics instead of exceptions.

| Code | Meaning |
|---|---|
| `SMILE1001` | Unknown statement or keyword |
| `SMILE1002` | `PRINT` requires a quoted string |
| `SMILE1003` | Unterminated string literal |
| `SMILE1004` | Unexpected text after statement |
| `SMILE1005` | Invalid or unexpected character |

Examples:

```basic
PRINT
PRINT Hello
PRINT "Unclosed
PRONT "Typo"
PRINT "Hello" extra
```

## Not In v0.1

Variables, expressions, comments, `INPUT`, conditions, loops, labels, `GOTO`, functions, and classes are not implemented.
