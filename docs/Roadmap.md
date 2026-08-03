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

## Implemented In v0.3.4

- Keep Objective-C language switching responsive by using AvalonEdit's built-in C/C++ highlighter for SMILE's current C-compatible Objective-C console profile.
- Add regression coverage that tokenizes generated Objective-C source through AvalonEdit's highlighting engine.

## Implemented In v0.4.0

- Add a real lexer for identifiers, keywords, string literals, integer literals, typed operators, parentheses, line endings, and end-of-file.
- Add official string escapes: `\\`, `\"`, `\n`, `\r`, `\t`, `\0`, `\b`, and `\f`.
- Add `String`, `Integer`, and `Boolean` core value types.
- Add signed 64-bit integer literals, unary arithmetic, binary arithmetic, integer comparison, string equality, boolean equality, `NOT`, `AND`, `OR`, and parentheses.
- Add typed `SmileValue` constants and evaluator display rules for integers and booleans.
- Add expression diagnostics `SMILE1201` through `SMILE1209`.
- Update high-level target generators to emit idiomatic typed expressions in C#, JavaScript, Java, and Swift.
- Update C, COBOL, Objective-C, and MASM to lower current compile-time values through the shared evaluator.
- Add official string literal and core expression specification documents.
- Expand conformance tests for lexer behavior, typed binding/evaluation, target generation, and evaluator-versus-toolchain runtime output.

## Implemented In v0.4.1

- Make `AND` and `OR` evaluation explicitly left-to-right and short-circuiting while continuing to bind and type-check both operands.
- Remove obsolete concatenation syntax and bound-node paths so typed binary `+` is the one canonical string-concatenation representation.
- Harden signed 64-bit boundaries, checked overflow, truncating integer division, associativity, precedence, ordinal string equality, and exact official escape behavior.
- Preserve native integer and boolean expressions in generated C and Objective-C declarations.
- Generate safe typed C and Objective-C `printf` calls with `%lld`, `%s`, canonical boolean display, literal-percent protection, embedded-NUL support, and ordinal `strcmp` string equality.
- Add a deterministic fixed-seed typed-expression corpus plus all-eight-target runtime comparisons against the reference evaluator for the corpus, shipped example, and exact control-character output.
- Synchronize the public specifications, architecture, target-code standard, README, project history, and desktop version at `0.4.1 Typed Expression Conformance Hardening`.

## Implemented In v0.4.2

- Add Python as the ninth first-class target with stable ID `python` and primary file `Program.py`.
- Generate conventional dependency-free Python 3.10+ source directly from `BoundProgram`, including `main()` and the standard main guard.
- Preserve truncation-toward-zero Integer division with an on-demand `_smile_div` helper and canonical Integer/Boolean display with an on-demand `_smile_text` helper.
- Preserve Python f-string interpolation, literal braces, official string escapes, short-circuit `and`/`or`, case-sensitive string equality, and precedence-aware bound-tree rendering.
- Add collision-safe Python identifier mapping for keywords, soft keywords, built-ins, and generated helper names.
- Add local Python Run support, press-any-key launchers, Python highlighting, desktop selectors, CLI support, and nine-target evaluator conformance.
- Keep compiler-rejected unreachable short-circuit divisions valid by lowering affected Boolean initializers to evaluated constants, and harden complex C/Objective-C String equality operands.
- Add one shared pure bound-expression simplifier for Boolean identities, including the acceptance lowering from `Adult AND NOT FALSE` to `Adult`.
- Keep SMILE Integer semantics signed 64-bit while profiling the complete bound program for idiomatic target storage: C/Objective-C `int` or `int64_t`, C#/Java `int` or `long`, JavaScript `Number` or `BigInt`, Swift `Int` or `Int64`, and Python `int`.
- Add exact small, boundary, wide, intermediate-result, and evaluator-versus-toolchain Integer-profile coverage for all nine targets.
- Publish the expanded learner-first mission statement and link the project introduction video from the README.
- Pause destination-language expansion after Python so SMILE can deepen its runtime language model.

## Deferred Destination Languages

Rust, Zig, and Go are intentionally not part of the active SMILE roadmap at this stage. After Python, target-language expansion is paused while SMILE focuses on runtime variables, assignment, input, conditions, loops, functions, and scopes. These targets may be reconsidered later when the runtime language model is mature.

## Future Ideas

These are not implemented in v0.4.2:

1. Runtime variables and assignment
2. `INPUT`
3. `IF / THEN / ELSE`
4. Loops
5. Functions and scopes
6. Floating-point and decimal numeric types
7. Debugging and source mapping
8. Semantic highlighting, autocomplete, and diagnostic squiggles
9. Reusable web interface
10. Evolution toward a full SMILE language
