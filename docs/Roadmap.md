# Roadmap

## Implemented In v0.2.0

- Parse `LET Name = "Sin"` string variable declarations.
- Resolve variables case-insensitively with declarations before use.
- Detect duplicate variable declarations case-insensitively.
- Parse `PRINT` blank-line, ordinary quoted, raw template, `$"..."`, and quoted concatenation forms.
- Keep `PRINT Name` literal and `PRINT {Name}` evaluated.
- Support `{{` and `}}` literal braces in raw templates and `$"..."`.
- Reject missing `PRINT` whitespace, malformed interpolation, semicolon statement separators, and a second standalone `PRINT` keyword on one line.
- Generate C#, C, Windows x64 MASM, JavaScript, Java, COBOL, Objective-C, and Swift directly from the bound SMILE program.
- Build and run installed local target toolchains.
- Provide a CLI developer harness.
- Provide a responsive WPF desktop app with three generated panes.
- Debounce live desktop previews so typing stays responsive.
- Keep Build & Run tied to the latest source revision.
- Use separate 120 second build and 10 second program execution timeouts.

## Implemented In v0.2.1

- Add AvalonEdit-backed SMILE and generated-code panes with line numbers.
- Add lexical syntax highlighting for SMILE, C#, C, MASM x64, JavaScript, Java, COBOL, Objective-C, and Swift.
- Keep target-language highlighting in sync when a generated pane changes language.
- Contain recoverable desktop Build & Run, process, command, folder-opening, and toolchain-detection failures.
- Write desktop diagnostic logs under `%LOCALAPPDATA%\SMILE\Logs`, with a `%TEMP%\SMILE\Logs` fallback.
- Bound captured child-process streams and desktop output history to protect the IDE from runaway generated-program output.

## Implemented In v0.2.2

- Reuse cached generated code when a target pane changes language.
- Regenerate only visible target languages that are missing for the current source revision.
- Keep rapid C, Swift, Java, and other target selector changes from turning into repeated visible-target transpilation bursts.

## Implemented In v0.3.0

- Complete official `LET` v1.0 string initializer support.
- Accept previously declared variables, concatenation, and interpolated quoted strings in `LET`.
- Evaluate official string-only `LET` initializers as compile-time constants for low-level targets.
- Add the SMILE reference evaluator for semantic conformance tests.
- Enforce portable ASCII identifiers and reject `LET` or `PRINT` as variable names.
- Add deterministic target identifier mapping for destination-language keywords and generator-owned runtime names.

## Implemented In v0.3.1

- Harden `LET` and `PRINT` v1.0 target conformance without adding new language syntax.
- Preserve empty string variables exactly, including MASM zero logical length for empty storage.
- Map Java and Swift single `_` identifiers to usable generated variable names.
- Map C and Objective-C implementation-reserved identifier prefixes such as `__internal` and `_Upper`.
- Expand target identifier mapping coverage for keywords, contextual/restricted identifiers, generator-owned names, and collision cases.
- Add `SMILE1116` for missing `LET` initializer expressions.
- Add evaluator-versus-toolchain tests for empty strings and adversarial valid SMILE identifiers.

## Implemented In v0.3.2

- Add local Objective-C Build & Run through MSYS2 MinGW64 Clang.
- Add local Swift Build & Run through Swift.Toolchain for Windows and Visual Studio C++ linker tools.
- Switch Objective-C generation to a Foundation-free Windows console profile that compiles reliably as `.m` locally.
- Enable Objective-C and Swift in desktop Build & Run when their toolchains are detected.
- Extend evaluator-versus-toolchain tests to Objective-C and Swift when those local toolchains are installed.

## Implemented In v0.3.3

- Add COBOL as a generated target with stable ID `cobol` and primary file `Program.cob`.
- Generate GnuCOBOL free-format source with readable divisions, fixed-length `WORKING-STORAGE` values, and `DISPLAY` output.
- Preserve empty SMILE strings in COBOL by skipping zero-length display operands and using an exact newline operation for blank `PRINT`.
- Add local COBOL Build & Run through MSYS2 GnuCOBOL.
- Add COBOL syntax highlighting and evaluator-versus-toolchain test coverage.

## Future Ideas

These are not implemented in v0.3.3:

1. Numeric and boolean expressions
2. `INPUT`
3. `IF / THEN / ELSE`
4. Loops
5. Functions
6. Type checking beyond string-only expressions
7. Debugging and source mapping
8. Reusable web interface
9. Evolution toward a full SMILE language
