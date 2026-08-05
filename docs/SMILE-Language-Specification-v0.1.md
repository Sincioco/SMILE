# SMILE Language Specification v0.1

This document is retained as a historical v0.1 note.

SMILE v0.1 supported only:

```basic
PRINT "text"
```

SMILE v0.6.0 implements and hardens the official LET, SET, IF, Friendly PRINT, String literal, and typed expression behavior defined in:

- [SMILE - LET Statement Official Specification v1.0](SMILE%20Language%20Specification/SMILE%20-%20LET%20Statement%20Official%20Specification%20v1.0.md)
- [SMILE - SET Statement Official Specification v1.0](SMILE%20Language%20Specification/SMILE%20-%20SET%20Statement%20Official%20Specification%20v1.0.md)
- [SMILE - IF Statement Official Specification v1.0](SMILE%20Language%20Specification/SMILE%20-%20IF%20Statement%20Official%20Specification%20v1.0.md)
- [SMILE - PRINT Statement Official Specification v1.0](SMILE%20Language%20Specification/SMILE%20-%20PRINT%20Statement%20Official%20Specification%20v1.0.md)
- [SMILE - String Literals Official Specification v1.0](SMILE%20Language%20Specification/SMILE%20-%20String%20Literals%20Official%20Specification%20v1.0.md)
- [SMILE - Core Types and Expressions Official Specification v1.0](SMILE%20Language%20Specification/SMILE%20-%20Core%20Types%20and%20Expressions%20Official%20Specification%20v1.0.md)

Current v0.6.0 behavior includes:

- `LET Name = "Sin"` string variable declarations.
- `LET Age = 49` integer variable declarations.
- `LET Adult = Age >= 18` boolean variable declarations.
- `LET Copy = Name` variable initializers.
- `LET FullName = FirstName + " " + LastName` concatenation initializers.
- `LET Greeting = $"Hello {FullName}!"` interpolated string initializers.
- Mutable runtime values declared by `LET` and changed by fixed-type, case-insensitive `SET` assignment.
- Recursive statement-order and branch-aware known-value analysis for diagnostics, mutation-aware simplification, Integer/String planning, and low-level target generation.
- Block IF with optional same-line ELSE IF clauses, optional final ELSE, mandatory THEN, and mandatory END IF.
- Call-free IF conditions whose complete result is Boolean and whose every atomic leaf contains an explicit comparison and right-hand operand.
- A same-line ELSE IF clause distinct from a nested IF after a standalone ELSE line.
- PRINT, SET, nested IF, blank lines, and SET Block String Literals in branches, with LET rejected until scopes are introduced.
- First-successful-clause evaluator execution and selected-branch-only mutation.
- Recursive Known/Unknown analysis that merges all possible outgoing paths and never leaks branch-specific values.
- Genuine idiomatic control flow across all ten targets without deleting source clauses or bodies.
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
- Runtime-authentic direct and composite Unknown expressions in generated C, Objective-C, COBOL, and MASM, including later LET, SET, PRINT, interpolation, and IF positions, plus full-JDK Java and all-ten-target runtime validation.
- Warning-free C# and Swift identity lowering for valid direct self-assignment, plus strict generated-warning validation for the compiler-backed C#, C, MASM x64, Java, COBOL, Objective-C, Swift, and C++ targets, distinct from the SMILE solution build. JavaScript and Python remain interpreter-only.
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
- Stable `SMILE1401` through `SMILE1415` diagnostics for IF headers, terminators, condition structure, clause placement, and branch restrictions.

The old v0.1 `PRINT "text"` form remains valid.
