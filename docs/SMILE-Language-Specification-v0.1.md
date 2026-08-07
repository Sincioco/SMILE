# SMILE Language Specification v0.1

This document is retained as a historical v0.1 note.

SMILE v0.1 supported only:

```basic
PRINT "text"
```

SMILE v0.8.0 implements the official LET, SET, INPUT, IF, WHILE, Friendly PRINT, String literal, typed expression, full-line comment, and source-layout behavior defined in:

- [001 - SMILE - SET Statement Official Specification v1.0](SMILE%20Language%20Specification/001%20-%20SMILE%20-%20SET%20Statement%20Official%20Specification%20v1.0.md)
- [002 - SMILE - PRINT Statement Official Specification v1.0](SMILE%20Language%20Specification/002%20-%20SMILE%20-%20PRINT%20Statement%20Official%20Specification%20v1.0.md)
- [003 - SMILE - String Literals Official Specification v1.0](SMILE%20Language%20Specification/003%20-%20SMILE%20-%20String%20Literals%20Official%20Specification%20v1.0.md)
- [004 - SMILE - Core Types and Expressions Official Specification v1.0](SMILE%20Language%20Specification/004%20-%20SMILE%20-%20Core%20Types%20and%20Expressions%20Official%20Specification%20v1.0.md)
- [005 - SMILE - LET Statement Official Specification v1.0](SMILE%20Language%20Specification/005%20-%20SMILE%20-%20LET%20Statement%20Official%20Specification%20v1.0.md)
- [006 - SMILE - IF Statement Official Specification v1.0](SMILE%20Language%20Specification/006%20-%20SMILE%20-%20IF%20Statement%20Official%20Specification%20v1.0.md)
- [007 - SMILE - Full-Line Comments and Source Layout Preservation Official Specification v1.0](SMILE%20Language%20Specification/007%20-%20SMILE%20-%20Full-Line%20Comments%20and%20Source%20Layout%20Preservation%20Official%20Specification%20v1.0.md)
- [008 - SMILE - INPUT Statement Official Specification v1.0](SMILE%20Language%20Specification/008%20-%20SMILE%20-%20INPUT%20Statement%20Official%20Specification%20v1.0.md)
- [009 - SMILE - WHILE Statement Official Specification v1.0](SMILE%20Language%20Specification/009%20-%20SMILE%20-%20WHILE%20Statement%20Official%20Specification%20v1.0.md)

Current v0.8.0 behavior includes:

- `LET Name = "Sin"` string variable declarations.
- `LET Age = 49` integer variable declarations.
- `LET Adult = Age >= 18` boolean variable declarations.
- `LET Copy = Name` variable initializers.
- `LET FullName = FirstName + " " + LastName` concatenation initializers.
- `LET Greeting = $"Hello {FullName}!"` interpolated string initializers.
- Mutable runtime values declared by `LET` and changed by fixed-type, case-insensitive `SET` assignment.
- Runtime input through case-insensitive `INPUT variable`, targeting one already-declared String, Integer, or Boolean variable at top level or inside any IF- or WHILE-related body.
- One strict UTF-8 logical input line per executed INPUT, with a 4096-byte maximum, complete String preservation, invariant signed-decimal Int64 conversion, and exact TRUE/FALSE Boolean conversion.
- Injectable scripted evaluator input plus canonical runtime stdout, stderr, exit code, and `SMILER1501` through `SMILER1506` input errors.
- Runtime-unknown analysis after INPUT, including full signed-64 Integer range, 0-to-4096-byte NUL-capable String planning, both Boolean values, and conservative branch merging.
- Checked runtime signed-64 arithmetic with `SMILER1206` overflow and `SMILER1207` division-by-zero errors only when a runtime-dependent operation is reached.
- Recursive statement-order, branch-aware, and loop-fixed-point known-value analysis for diagnostics, mutation-aware simplification, Integer/String planning, and low-level target generation.
- Block IF with optional same-line ELSE IF clauses, optional final ELSE, mandatory THEN, and mandatory END IF.
- Call-free IF conditions whose complete result is Boolean and whose every atomic leaf contains an explicit comparison and right-hand operand.
- A same-line ELSE IF clause distinct from a nested IF after a standalone ELSE line.
- PRINT, SET, INPUT, nested IF, WHILE, blank lines, and SET Block String Literals in branches, with LET rejected until scopes are introduced.
- First-successful-clause evaluator execution and selected-branch-only mutation.
- Recursive Known/Unknown analysis that merges all possible outgoing paths and never leaks branch-specific values.
- Genuine idiomatic control flow across all ten targets without deleting source clauses or bodies.
- Block-only pre-test WHILE with mandatory END WHILE, zero-or-more iterations, current-storage condition re-evaluation, and no THEN, DO, WEND, BREAK, or CONTINUE form.
- Explicit-comparison, call-free WHILE conditions shared with IF and loop bodies containing PRINT, SET, INPUT, IF, nested WHILE, comments, blank lines, and SET Block Strings while LET remains prohibited.
- One combined IF/WHILE nesting depth of 128, with opener-specific `SMILE1416` or `SMILE1611` and bounded mixed-block parser recovery at depth 129.
- Cancellation-aware reference loop execution with no implicit iteration limit and safe generated-process timeout/tree termination.
- Two-phase zero-or-more fixed-point analysis with deterministic loop ordinals, conservative post-loop merging, Integer range widening, and facts recorded once per source statement.
- Portable bounded-String loop validation that retains finite stable assignments and reports `SMILE1612` for recurrence without a finite compile-time UTF-8 maximum.
- Genuine pre-test control flow across all ten targets, including Python empty-body `pass`, structured COBOL `PERFORM`, deterministic MASM labels, exact INPUT, checked arithmetic, comments, and blank lines.
- Contextual ordinal case-insensitive `REM` plus `//`, `#`, and `--` full-line comments at the first space/tab-trimmed position, with REM remaining valid as an identifier elsewhere.
- No inline or trailing comment syntax; PRINT raw templates and marker-looking ordinary, interpolated, and Block String content remain data.
- Ordered non-semantic comment and blank-line source items preserved through parsing, binding, simplification, and all ten primary generated source files while semantic analysis and evaluation ignore them.
- Native target comment markers, deterministic safe payload rendering, conservative COBOL wrapping, source blank-line boundaries, and required no-op placeholders in semantically empty target bodies.
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
- A focused `Binder.cs`, a small `Generation.cs` facade, shared generation helpers, and one source file per destination generator without changing compiler behavior or emitted output.
- The hosted Windows `SMILE CI` workflow for independent Debug and Release solution validation, while strict local release validation continues to require all ten target toolchains.
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
- Stable `SMILE1401` through `SMILE1416` diagnostics for IF headers, terminators, condition structure, clause placement, branch restrictions, and the nesting safety limit.
- Stable `SMILE1501` through `SMILE1505` diagnostics for malformed or undefined INPUT targets.
- Stable `SMILE1601` through `SMILE1612` diagnostics for WHILE headers, conditions, terminators, body restrictions, combined nesting safety, and unbounded String recurrence.
- Scripted stdin conformance for all ten targets and live interactive CLI/Desktop INPUT execution without blocking the WPF UI thread.

The old v0.1 `PRINT "text"` form remains valid.
