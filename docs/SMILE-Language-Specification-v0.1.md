# SMILE Language Specification v0.1

This document is retained as a historical v0.1 note.

SMILE v0.1 supported only:

```basic
PRINT "text"
```

SMILE v0.3.0 implements the official Friendly PRINT and LET language behavior defined in:

- [SMILE - PRINT Statement Official Specification v1.0](SMILE%20Language%20Specification/SMILE%20-%20PRINT%20Statement%20Official%20Specification%20v1.0.md)
- [SMILE - LET Statement Official Specification v1.0](SMILE%20Language%20Specification/SMILE%20-%20LET%20Statement%20Official%20Specification%20v1.0.md)

Current v0.3.0 behavior includes:

- `LET Name = "Sin"` string variable declarations.
- `LET Copy = Name` variable initializers.
- `LET FullName = FirstName + " " + LastName` concatenation initializers.
- `LET Greeting = $"Hello {FullName}!"` interpolated string initializers.
- Compile-time string constant evaluation for official `LET` v1.0 values.
- Blank `PRINT`.
- Ordinary quoted `PRINT`.
- Quote-free raw `PRINT` templates.
- Raw `{Name}` interpolation.
- `$"..."` interpolation.
- String concatenation in quoted `PRINT` expressions.
- Case-insensitive keywords and variable lookup.
- Stable diagnostics for malformed interpolation, duplicate variables, undefined variables, missing `PRINT` whitespace, and second `PRINT` keywords on one line.

The old v0.1 `PRINT "text"` form remains valid.
