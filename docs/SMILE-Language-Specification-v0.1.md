# SMILE Language Specification v0.1

This document is retained as a historical v0.1 note.

SMILE v0.1 supported only:

```basic
PRINT "text"
```

SMILE v0.2.0 implements the official Friendly PRINT language behavior defined in:

[SMILE PRINT Statement Specification v1.0](SMILE-PRINT-Statement-Specification-v1.0.md)

Current v0.2.0 behavior includes:

- `LET Name = "Sin"` string variable declarations.
- Blank `PRINT`.
- Ordinary quoted `PRINT`.
- Quote-free raw `PRINT` templates.
- Raw `{Name}` interpolation.
- `$"..."` interpolation.
- String concatenation in quoted `PRINT` expressions.
- Case-insensitive keywords and variable lookup.
- Stable diagnostics for malformed interpolation, duplicate variables, undefined variables, missing `PRINT` whitespace, and second `PRINT` keywords on one line.

The old v0.1 `PRINT "text"` form remains valid.
