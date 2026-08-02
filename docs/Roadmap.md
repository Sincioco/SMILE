# Roadmap

## Implemented In v0.2.0

- Parse `LET Name = "Sin"` string variable declarations.
- Resolve variables case-insensitively with declarations before use.
- Detect duplicate variable declarations case-insensitively.
- Parse `PRINT` blank-line, ordinary quoted, raw template, `$"..."`, and quoted concatenation forms.
- Keep `PRINT Name` literal and `PRINT {Name}` evaluated.
- Support `{{` and `}}` literal braces in raw templates and `$"..."`.
- Reject missing `PRINT` whitespace, malformed interpolation, semicolon statement separators, and a second standalone `PRINT` keyword on one line.
- Generate C#, C, Windows x64 MASM, JavaScript, Java, Objective-C, and Swift directly from the bound SMILE program.
- Build and run installed local target toolchains.
- Report Objective-C and Swift as transpile-only targets on Windows.
- Provide a CLI developer harness.
- Provide a responsive WPF desktop app with three generated panes.
- Debounce live desktop previews so typing stays responsive.
- Keep Build & Run tied to the latest source revision.
- Use separate 120 second build and 10 second program execution timeouts.

## Future Ideas

These are not implemented in v0.2.0:

1. Non-literal `LET` initializers
2. Numeric and boolean expressions
3. `INPUT`
4. `IF / THEN / ELSE`
5. Loops
6. Functions
7. Type checking beyond string-only expressions
8. Debugging and source mapping
9. Reusable web interface
10. Evolution toward a full SMILE language
