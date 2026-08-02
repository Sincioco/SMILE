# Roadmap

## Implemented In v0.1.3

- Parse `PRINT "text"` statements.
- Accept multiple `PRINT` statements and blank lines.
- Accept straight and smart double-quote delimiters.
- Generate C#, C, Windows x64 MASM, JavaScript, Java, Objective-C, and Swift directly from the SMILE syntax tree.
- Build and run installed local target toolchains.
- Report Objective-C and Swift as transpile-only targets on Windows.
- Provide a CLI developer harness.
- Provide a responsive WPF desktop app with three generated panes.
- Debounce live desktop previews so typing stays responsive.
- Keep Build & Run tied to the latest source revision.
- Use separate 120 second build and 10 second program execution timeouts.

## Future Ideas

These are not implemented in v0.1.3:

1. `LET` and variables
2. Printing variables and expressions
3. `INPUT`
4. Numeric and string expressions
5. `IF / THEN / ELSE`
6. Loops
7. Functions
8. Type checking
9. Debugging and source mapping
10. Reusable web interface
11. Evolution toward a full SMILE language
