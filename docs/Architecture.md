# Architecture

SMILE v0.3.0 uses a small direct compiler pipeline:

```text
Source -> Parser -> Syntax Tree -> Binder -> Bound Program -> Target Generator -> Generated Files
                                                        |
                                                 Optional Toolchain
                                                        |
                                                 Build and Run Result
```

The parser is line-oriented because current SMILE statements are line-oriented. It recognizes `LET`, deterministic `PRINT` forms, raw templates, `$"..."` interpolation, ordinary quoted strings, and string concatenation for shared string expressions.

The binder resolves variable names with an ordinal case-insensitive symbol table. A `LET` variable is deliberately absent while its initializer binds, then becomes visible only after the initializer is successfully evaluated. This keeps declarations-before-use, self-reference, failed declarations, duplicate declarations, and undefined variables target-independent. Target generators consume the bound program and do not reparse SMILE source text.

The bound tree preserves expression intent. C#, JavaScript, and Swift can therefore render interpolation-oriented SMILE expressions as native interpolation, while explicit SMILE concatenation remains target-language concatenation. Java uses concatenation as the interpolation fallback.

Official `LET` v1.0 initializers are all compile-time string constants. The engine evaluates string literals, variable references, concatenation, and interpolated strings once during binding and carries the resulting value on each bound declaration. C, Objective-C, and MASM use that value for declaration storage instead of generating premature runtime string buffers, while high-level targets still preserve the original initializer expression where the destination language has a natural form.

The engine also exposes a tiny target-neutral string flattener for targets that need segment output. It turns bound string expressions such as:

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

C and Objective-C use those segments as target-local input to a safe `printf` format plan, which keeps one natural destination-language output call per SMILE `PRINT` where practical. MASM uses the segments directly with `WriteFile`, and Java uses them only where a clear concatenation fallback is appropriate. The flattener is a target-local lowering helper, not the canonical semantic representation.

The engine includes a small `SmileEvaluator` reference evaluator. It executes the bound program directly, stores string values for `LET`, and appends output for `PRINT`. Tests use it as the semantic oracle for runnable generated targets.

Target generators use a symbol-based target identifier map. Valid SMILE identifiers are preserved when safe, and mapped to readable names such as `_smile_class` when they conflict with destination-language keywords or generator-owned runtime names. The map is built once per target from `BoundProgram.Variables`, so every reference to a variable uses the same generated name as its declaration.

Generated target code should be semantically correct, idiomatic for the destination language, and close to code a competent human developer would naturally write. That rule applies even when a lower-level target must lower SMILE features into equivalent operations.

`SMILE.Engine` has no WPF dependency. That keeps the language front end reusable from the CLI, the desktop app, tests, and a possible future web interface.

`SMILE.Toolchains` owns local compiler/runtime detection, process execution, transpile-only target status, separate build/program timeouts, bounded process output, and optional press-any-key launcher scripts. Process work is asynchronous, cancellable, timed, and isolated in `%TEMP%\SMILE\Runs\<unique-id> - <language>\` so the WPF UI thread stays responsive, learners can identify each generated-code folder, and build artifacts stay out of the repository.

The desktop app tracks source revisions for live preview. Typing schedules a short debounced background transpilation for the visible target languages only. Manual Transpile All runs asynchronously and generates every target. Build & Run asks for the current source revision before invoking a toolchain, which prevents a compiler from running stale generated code.

`SMILE.Desktop` uses AvalonEdit for the editable SMILE source pane and the three generated target panes. Those four code panes show line numbers and lexical syntax highlighting, while the output area stays a plain append-only log for build and program output.

Generated programs are cached by target language and source revision. A target-language switch first tries that cache; live transpilation is scheduled only for visible targets that are missing for the current source revision. This keeps rapid ComboBox changes responsive and avoids unnecessary compiler work on the WPF dispatcher path.

Recoverable desktop failures are contained at the operation boundary. Toolchain detection, build/run, child-process output, command-state refresh, and folder-opening failures report concise output, write a diagnostic log when possible, and keep the IDE open.

KISS keeps the architecture small: one engine project, one toolchain project, one CLI harness, one desktop app, and one test project. KISS v2 keeps the user experience responsive first.
