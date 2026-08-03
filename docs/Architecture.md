# Architecture

SMILE v0.4.0 uses a small direct compiler pipeline:

```text
Source -> Lexer -> Tokens -> Parser -> Syntax Tree -> Binder -> Bound Program -> Target Generator -> Generated Files
                                                                          |
                                                                   Optional Toolchain
                                                                          |
                                                                   Build and Run Result
```

The lexer turns source text into tokens for identifiers, literals, keywords, operators, parentheses, line endings, and end-of-file. That gives SMILE one official place for recognizing integer literals, string escapes, boolean keywords, and typed expression operators before any target generator is involved.

The parser is line-oriented because current SMILE statements are line-oriented. It recognizes `LET`, deterministic `PRINT` forms, raw templates, `$"..."` interpolation, ordinary quoted strings, and typed expressions with precedence.

The binder resolves variable names with an ordinal case-insensitive symbol table and assigns each expression a SMILE type: `String`, `Integer`, or `Boolean`. A `LET` variable is deliberately absent while its initializer binds, then becomes visible only after the initializer is successfully evaluated. This keeps declarations-before-use, self-reference, failed declarations, duplicate declarations, undefined variables, type mismatches, overflow, and division-by-zero target-independent. Target generators consume the bound program and do not reparse SMILE source text.

The bound tree preserves expression intent. C#, JavaScript, Java, and Swift can therefore render arithmetic, boolean logic, interpolation, and explicit string concatenation in a natural destination-language form. Lower-level targets may lower current compile-time expressions to canonical text when that is simpler and more reliable than introducing runtime buffers or a SMILE runtime library.

Current `LET` initializers are all compile-time constants because SMILE v0.4.0 has no runtime input or reassignment. The engine evaluates strings, integers, booleans, arithmetic, comparisons, boolean logic, variable references, and interpolated strings once during binding and carries the resulting `SmileValue` on each bound declaration. C, COBOL, Objective-C, and MASM can use that value directly for declaration storage, while high-level targets still preserve the original initializer expression where the destination language has a natural form.

The engine still exposes a tiny string flattener for compatibility with targets that need segment output. It turns bound string expressions such as:

```text
"Hello " + Name + "!"
Hello {Name}!
$"Hello {Name}!"
```

into ordered printable segments:

```text
literal "Hello "
variable Name
literal "!"
```

C and Objective-C now prefer canonical compile-time text for current v0.4.0 `PRINT` values, emitted through safe compiler-owned `printf` format strings. COBOL emits canonical text with `DISPLAY`, which avoids fixed-length padding leaks. MASM emits canonical UTF-8 byte labels and writes them with `WriteFile`. The flattener remains a target-local helper, not the canonical semantic representation.

The engine includes a small `SmileEvaluator` reference evaluator. It executes the bound program directly, stores typed `SmileValue` constants for `LET`, and appends display text for `PRINT`. Tests use it as the semantic oracle for runnable generated targets.

Target generators use a symbol-based target identifier map. Valid SMILE identifiers are preserved when safe, and mapped to readable names such as `_smile_class` or `SMILE-class` when they conflict with destination-language keywords, contextual/restricted identifiers, generator-owned runtime names, or target-specific reserved identifier patterns. C and Objective-C map names such as `__internal` and `_Upper` because those prefixes are reserved for implementations in ordinary C-family usage. COBOL maps underscores and reserved words to hyphenated COBOL data names. Java and Swift map a single `_` because it is not a usable ordinary local variable in those targets. The map is built once per target from `BoundProgram.Variables`, so every reference to a variable uses the same generated name as its declaration.

MASM stores string bytes in static labels and writes them with `WriteFile`. For an empty string, the data label keeps a one-byte placeholder so the symbol has an address, but the generated logical length is `0`. This keeps `PRINT {Empty}` from emitting an invisible NUL byte before the normal newline.

Generated target code should be semantically correct, idiomatic for the destination language, and close to code a competent human developer would naturally write. That rule applies even when a lower-level target must lower SMILE features into equivalent operations.

`SMILE.Engine` has no WPF dependency. That keeps the language front end reusable from the CLI, the desktop app, tests, and a possible future web interface.

`SMILE.Toolchains` owns local compiler/runtime detection, process execution, target availability status, separate build/program timeouts, bounded process output, and optional press-any-key launcher scripts. COBOL uses MSYS2 GnuCOBOL, Objective-C uses MSYS2 Clang for SMILE's Foundation-free console profile, and Swift uses Swift.Toolchain with the Visual Studio C++ linker environment. Process work is asynchronous, cancellable, timed, and isolated in `%TEMP%\SMILE\Runs\<unique-id> - <language>\` so the WPF UI thread stays responsive, learners can identify each generated-code folder, and build artifacts stay out of the repository.

The desktop app tracks source revisions for live preview. Typing schedules a short debounced background transpilation for the visible target languages only. Manual Transpile All runs asynchronously and generates every target. Build & Run asks for the current source revision before invoking a toolchain, which prevents a compiler from running stale generated code.

`SMILE.Desktop` uses AvalonEdit for the editable SMILE source pane and the three generated target panes. Those four code panes show line numbers and lexical syntax highlighting, while the output area stays a plain append-only log for build and program output. Objective-C is intentionally highlighted with AvalonEdit's built-in C/C++ definition because the current generated Objective-C profile is C-compatible and this keeps ComboBox language switching responsive.

Generated programs are cached by target language and source revision. A target-language switch first tries that cache; live transpilation is scheduled only for visible targets that are missing for the current source revision. This keeps rapid ComboBox changes responsive and avoids unnecessary compiler work on the WPF dispatcher path.

Recoverable desktop failures are contained at the operation boundary. Toolchain detection, build/run, child-process output, command-state refresh, and folder-opening failures report concise output, write a diagnostic log when possible, and keep the IDE open.

KISS keeps the architecture small: one engine project, one toolchain project, one CLI harness, one desktop app, and one test project. KISS v2 keeps the user experience responsive first.
