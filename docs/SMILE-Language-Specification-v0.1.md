# SMILE Language Specification v0.1

This document is retained as a historical v0.1 note.

SMILE v0.1 supported only:

```basic
PRINT "text"
```

SMILE v0.5.1.1 implements and hardens the official LET, SET, Friendly PRINT, String literal, and typed expression behavior defined in:

- [SMILE - LET Statement Official Specification v1.0](SMILE%20Language%20Specification/SMILE%20-%20LET%20Statement%20Official%20Specification%20v1.0.md)
- [SMILE - SET Statement Official Specification v1.0](SMILE%20Language%20Specification/SMILE%20-%20SET%20Statement%20Official%20Specification%20v1.0.md)
- [SMILE - PRINT Statement Official Specification v1.0](SMILE%20Language%20Specification/SMILE%20-%20PRINT%20Statement%20Official%20Specification%20v1.0.md)
- [SMILE - String Literals Official Specification v1.0](SMILE%20Language%20Specification/SMILE%20-%20String%20Literals%20Official%20Specification%20v1.0.md)
- [SMILE - Core Types and Expressions Official Specification v1.0](SMILE%20Language%20Specification/SMILE%20-%20Core%20Types%20and%20Expressions%20Official%20Specification%20v1.0.md)

Current v0.5.1.1 behavior includes:

- `LET Name = "Sin"` string variable declarations.
- `LET Age = 49` integer variable declarations.
- `LET Adult = Age >= 18` boolean variable declarations.
- `LET Copy = Name` variable initializers.
- `LET FullName = FirstName + " " + LastName` concatenation initializers.
- `LET Greeting = $"Hello {FullName}!"` interpolated string initializers.
- Mutable runtime values declared by `LET` and changed by fixed-type, case-insensitive `SET` assignment.
- Statement-order known-value analysis for diagnostics, mutation-aware simplification, Integer profiling, and low-level target generation.
- The SET Block String Literal — The SMILE Way, with exact logical newlines, structural indentation removal, quotes, escapes, trailing whitespace, and embedded NUL.
- Empty string `LET` values preserved exactly across the evaluator and generated targets.
- Official string escapes for quote, backslash, control characters, and tab/newline text.
- Signed 64-bit integer arithmetic, comparison, and grouping with parentheses.
- Boolean literals, comparison results, and `NOT`/`AND`/`OR`.
- Left-to-right short-circuit evaluation for `AND` and `OR`, with both operands still bound and type-checked.
- One canonical syntax and bound-tree representation for every typed expression feature.
- Native integer and boolean expression generation plus safe typed `printf` and ordinal string equality in C and Objective-C.
- Deterministic all-target runtime conformance tests against the SMILE reference evaluator.
- Target identifier mapping for destination keywords, generator-owned names, Java/Swift `_`, COBOL data names, and C-family reserved identifier patterns.
- Runtime-authentic direct variable reads in generated C, Objective-C, COBOL, and MASM, plus full-JDK Java and all-ten-target runtime validation.
- Warning-free C# and Swift identity lowering for valid direct self-assignment, plus a strict generated C# compiler-warning gate distinct from the SMILE solution build.
- Local Build & Run support for COBOL, Objective-C, and Swift when their Windows toolchains are installed.
- Blank `PRINT`.
- Ordinary quoted `PRINT`.
- Quote-free raw `PRINT` templates.
- Raw `{Name}` interpolation.
- `$"..."` interpolation.
- String concatenation in quoted `PRINT` expressions.
- Case-insensitive keywords and variable lookup.
- Stable diagnostics for malformed interpolation, duplicate variables, undefined variables, missing `PRINT` whitespace, and second `PRINT` keywords on one line.
- Stable diagnostics for malformed SET statements, undefined targets, type mismatches, and invalid block placement or delimiters.

The old v0.1 `PRINT "text"` form remains valid.
