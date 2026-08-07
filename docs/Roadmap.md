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
- Record the then-current pause after Python; v0.4.3 supersedes that pause with one final C++ target and a permanent destination-language freeze.

## Implemented In v0.4.2.1

- Preserve bytes after embedded NUL in C and Objective-C `PRINT` with compiler-owned UTF-8 byte arrays, exact byte lengths, and `fwrite`.
- Preserve complete ordinal String equality when either C-family operand contains NUL by lowering the pure comparison to its exact evaluated Boolean.
- Keep ordinary NUL-free C and Objective-C output on readable `%s` and ordinary equality on `strcmp`.
- Make shared Boolean simplification use previously declared bound constants in `LET`, direct `PRINT`, raw-template holes, interpolated String holes, and nested expressions.
- Decide short-circuit reachability before simplifying the right operand so unreachable target-invalid arithmetic is never emitted.
- Compare exact output bytes across all nine installed targets without trimming NUL or other official control characters.
- Clarify which v1.0 language specifications remain normative for v0.4.2.1 and later.

## Implemented In v0.4.3

- Add C++ as the tenth and final planned destination with stable ID `cpp` and primary file `Program.cpp`.
- Generate modern C++20 directly from `BoundProgram` with owned `std::string`, `std::cout`, native length-aware String equality, RAII ownership, precedence-aware expressions, and canonical Boolean text.
- Keep literal-plus-literal concatenation valid by beginning the generated chain with `std::string` when needed.
- Preserve embedded NUL through UTF-8 byte-counted `std::string` construction and stream complete String values through `std::cout`.
- Use the shared per-program Integer profile with ordinary `int` and exact `std::int64_t`/`INT64_C(...)` wide storage.
- Add complete C++20 keyword, runtime-name, collision, and implementation-reserved identifier mapping.
- Add local MSVC C++20 Build & Run, press-any-key launchers, AvalonEdit C++ highlighting, desktop selectors, CLI support, and ten-target evaluator conformance.
- Freeze destination-language expansion after C++ so future milestones deepen the SMILE language and its teaching tools.

## Implemented In v0.4.3.1

- Protect the complete fixed-width Integer and limit macro family in C, Objective-C, and C++ target identifier maps.
- Map C++ implementation-reserved double underscores anywhere in a name and ensure the final mapped spelling contains no double underscore.
- Drive C++ header emission from generated facilities, allowing directly streamed templates and literals to omit `<string>`.
- Add deterministic structural coverage and evaluator-versus-toolchain runs for the new adversarial cases across all ten targets.

## Implemented In v0.5.0

- Add case-insensitive `SET` as SMILE's only assignment statement, with declaration-before-assignment and exact fixed-type checking.
- Move current runtime state out of `BoundLetStatement` and into the evaluator's mutable symbol environment.
- Add shared statement-order execution analysis for mutation-aware simplification, short-circuit reachability, Integer profiling, exact String planning, and deterministic generation.
- Add the SET Block String Literal — The SMILE Way as a complete SET-only value with exact structural indentation removal, logical `\n` normalization, official escapes, quotes, tabs, trailing whitespace, and embedded NUL preservation.
- Emit real assignments across all ten targets, including Swift `var` analysis, C/Objective-C pointer-plus-length updates, COBOL `MOVE`, and MASM runtime pointer/length updates.
- Add the committed cumulative `examples/language.smile`, package it for Desktop deployment, load it only after the first window paint, and preserve visible-target-only background transpilation. Keep SET/block syntax highlighting, official specifications, diagnostics, CLI examples, and About version synchronized.
- Preserve asynchronous debounced live transpilation, exact-byte evaluator conformance, deterministic generation, and the ten-target destination-language freeze.

## Implemented In v0.5.1

- Complete Java SET runtime validation with a full JDK containing both `javac` and `java`, including ordinary assignment, String reassignment, Block String, embedded NUL, wide Integer, runtime-authenticity, and cumulative `language.smile` programs.
- Make direct C and Objective-C String variable PRINT read the current target pointer and logical length instead of an independent compiler-time output copy.
- Make applicable C and Objective-C String equality read current target storage, using `strcmp` for ordinary NUL-free values and exact length plus `memcmp` when embedded NUL is possible.
- Make COBOL direct variable PRINT read current `WORKING-STORAGE` and current logical length, including exact empty, Block String, control-byte, UTF-8, and trailing-whitespace output.
- Preserve MASM pointer-and-length direct reads and natural high-level target assignments with structural runtime-authenticity regression tests across all ten targets.
- Add no SMILE syntax. Preserve the cumulative deployable `examples/language.smile`, first-paint Desktop startup, asynchronous visible-target transpilation, deterministic generation, and the destination-language freeze.

## Implemented In v0.5.1.1

- Lower valid direct C# self-assignment to the smallest type-preserving identity assignment, eliminating `CS1717` while retaining an explicit target update for String, Integer, and Boolean values.
- Add a generated C# compiler-warning gate controlled by `SMILE_REQUIRE_ZERO_TARGET_WARNINGS`, distinct from the SMILE solution's own warning count.
- Build and run generated C# for the cumulative `examples/language.smile`, require zero C# compiler warnings, and compare output with `SmileEvaluator`.
- Run direct self-assignment through all ten targets, preserve Swift identity lowering, and compare each installed target's runtime output with `SmileEvaluator`.
- Add no SMILE syntax. Preserve the cumulative deployable `examples/language.smile`, first-paint Desktop startup, asynchronous visible-target transpilation, deterministic generation, and the destination-language freeze.

## Implemented In v0.6.0

- Add block `IF / ELSE IF / ELSE / END IF` with case-insensitive `IF`, `THEN`, `ELSE`, and `END` keywords.
- Require every atomic condition to be an explicit comparison and keep IF conditions free of function or procedure invocation.
- Treat same-line `ELSE IF` as one clause while preserving an IF after a standalone ELSE line as a nested statement with its own END IF.
- Permit PRINT, SET, nested IF, blank lines, and SET Block String Literals in branches; reject LET until scopes are formally introduced.
- Add canonical recursive syntax and bound IF representations, first-successful-clause evaluator behavior, and branch-aware Known/Unknown path merging.
- Preserve every source branch across all ten generators with idiomatic high-level control flow, Python `elif`, COBOL `END-IF`, and deterministic MASM compare/jump labels.
- Extend the cumulative deployable `examples/language.smile`, lexical highlighting, official documentation, exact evaluator conformance, deterministic generation, and strict generated-warning validation.
- Preserve first-paint Desktop startup, asynchronous visible-target transpilation, cancellation, failure containment, and the destination-language freeze.

## Implemented In v0.6.0.1

- Harden the existing IF implementation without adding or changing SMILE syntax or valid-program behavior.
- Add the `SMILE CI` Windows GitHub Actions workflow for main pushes, pull requests targeting main, and manual dispatch, with independent Debug and Release solution validation on .NET SDK 10.0.302.
- Keep strict local release validation separate from hosted CI so Java, all ten destination toolchains, exact evaluator conformance, and zero generated compiler warnings remain mandatory before publication.
- Move `Binder` from `Parser.cs` into focused `Binder.cs` while preserving the syntax-tree boundary, binding behavior, diagnostics, and source spans.
- Keep `Generation.cs` as the small public facade and split shared helpers plus all ten destination generators into focused files without changing registry order, APIs, deterministic labels, generated files, or output.
- Limit supported IF nesting to 128 levels and report `SMILE1416` at depth 129, using bounded recovery so pathological input cannot recurse into an over-limit body or destabilize the Desktop editor.
- Add direct invalid-source regressions for function-shaped IF and ELSE IF conditions while preserving the permanent call-free rule and introducing no function-call grammar.

## Implemented In v0.6.1

- Add equivalent first-non-whitespace `REM`, `//`, `#`, and `--` full-line comments, with ordinal case-insensitive boundary-aware contextual REM and no inline-comment syntax.
- Retain comments and every blank physical source line as ordered non-semantic syntax and bound items while keeping evaluation, execution trace, analysis, diagnostics, mutation, and runtime output unchanged.
- Preserve comments once in each target's primary user-code region with native `//`, `#`, `*>`, or `;` syntax, deterministic target-safe payload rendering, conservative COBOL wrapping, and explicit source blank-line boundaries.
- Keep SET Block String content authoritative over comment/layout classification and make IF parsing plus 128-level recovery ignore comment payloads safely.
- Preserve layout through simplification and nested branches, including leading, trailing, consecutive, and layout-only bodies with required Python or COBOL no-op placeholders.
- Extend the cumulative packaged `examples/language.smile`, AvalonEdit Comment highlighting, numbered official specifications, evaluator conformance, and strict all-ten-target warning-free runtime validation.
- Preserve first-paint Desktop startup, asynchronous visible-target transpilation, cancellation, failure containment, and the destination-language freeze.

## Implemented In v0.7.0

- Add case-insensitive `INPUT variable` for an existing fixed-type String, Integer, or Boolean variable at top level and inside every IF-related body.
- Reserve INPUT globally, add one canonical syntax and bound statement, and publish the numbered official INPUT specification with exact `SMILE1501` through `SMILE1505` compile diagnostics.
- Separate statically Known, runtime-Unknown, and Invalid evaluation facts so no LET or SET value can be propagated past INPUT and every possible IF path remains conservative.
- Read one strict UTF-8 logical line per executed INPUT, preserve complete String data, parse invariant signed 64-bit Integer and exact TRUE/FALSE text, and enforce the shared 4096-byte limit.
- Add injectable evaluator input plus canonical runtime stdout, stderr, exit-code, and `SMILER1206`, `SMILER1207`, and `SMILER1501` through `SMILER1506` error behavior.
- Preserve checked signed 64-bit runtime arithmetic, left-to-right short circuiting, selected-branch-only input consumption, full-range Integer planning, and 0-to-4096-byte NUL-capable String planning.
- Generate dependency-free native INPUT support for all ten frozen destinations, provide scripted stdin conformance with exact stdout/stderr/exit comparisons and zero generated warnings, and keep no-INPUT output minimal.
- Make CLI runs inherit interactive standard input and stream prompts live. Launch Desktop INPUT programs through one visible interactive console path while keeping live transpilation and the WPF UI responsive.
- Extend and package the cumulative `examples/language.smile`, add the focused `examples/input.smile`, highlight INPUT lexically in AvalonEdit, and align public documentation and About SMILE at `0.7.0 INPUT`.

## Implemented In v0.7.0.1

- Treat every visible target pane as an independent editable build document and process duplicate target-language selections sequentially in Pane1, Pane2, Pane3 order.
- Build each pane from its own current primary source while retaining the current generated project file, INPUT metadata, non-primary companions, immutable preview cache, and unique toolchain workspace.
- Identify build output by pane and language, and rename the global command to `Build & Run Visible Panes`.
- Add a monotonically increasing learner-edit revision to each pane and capture immutable pane revision/language state for startup and debounced live generation.
- Prevent an older generation result from replacing a still-newer target edit while allowing untouched and same-language siblings to update; retain later-SMILE, same-pane-switch, Transpile All, and New authority.
- Append `*` when a target pane differs from generated SMILE output, preserve it across Save Source, Build & Run, toolchain refresh, unrelated pane work, and Maximize/Restore, and clear it only through authoritative replacement or reset.

## Implemented In v0.8.0

- Add canonical case-insensitive, block-only, pre-test `WHILE condition` / `END WHILE` with the same explicit-comparison and call-free condition rules as IF, zero-or-more execution, and no THEN, DO, WEND, BREAK, or CONTINUE aliases.
- Permit PRINT, SET, INPUT, IF, nested WHILE, comments, blank lines, and SET Block String Literals in loop bodies while rejecting LET recursively until scopes are introduced.
- Share one 128-level IF/WHILE control-flow nesting limit, add opener-specific depth diagnostics and bounded mixed-block recovery, and keep comments plus Block Strings authoritative over structural-looking text.
- Add one canonical syntax and bound WHILE node, cancellation-aware evaluator execution, deterministic loop ordinals, and structural execution-trace handling that never runs a loop during compilation.
- Solve zero-or-more loop facts with a terminating two-phase fixed point, conservative post-loop merging, deterministic Integer widening, current-storage condition facts, and portable finite String-bound validation through `SMILE1612`.
- Emit genuine warning-free pre-test loops for all ten destinations, including Python empty-body `pass`, structured COBOL `PERFORM`, deterministic MASM labels/back-edges, checked runtime arithmetic, exact INPUT, and preserved comments and blank lines.
- Extend and package the cumulative `examples/language.smile`, add focused `examples/while.smile`, highlight WHILE and both END/WHILE terminator keywords, and align public documentation and About SMILE at `0.8.0 WHILE Loops`.
- Preserve every v0.7.0.1 target-editor ownership rule, first-paint responsiveness, process-tree cancellation, strict all-ten-target evaluator conformance, generated-warning gates, and exact-SHA post-push CI completion.

## Final Destination-Language Freeze

C++ is SMILE's tenth and final planned destination language. Target-language expansion is frozen so development can deepen loops, functions, scopes, debugging, and teaching tools. Do not add another destination language unless Sin explicitly reopens target expansion. Rust, Zig, and Go remain intentionally deferred and are not active targets.

## Active Language-Depth Milestones

These are not implemented in v0.8.0:

1. v0.9.0 - Functions and scopes

Later teaching-depth ideas include floating-point and decimal numeric types, debugging and source mapping, semantic highlighting, autocomplete, diagnostic squiggles, and a reusable web interface.
