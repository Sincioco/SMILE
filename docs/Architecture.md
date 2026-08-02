# Architecture

SMILE v0.2.1 uses a small direct compiler pipeline:

```text
Source -> Parser -> Syntax Tree -> Binder -> Bound Program -> Target Generator -> Generated Files
                                                        |
                                                 Optional Toolchain
                                                        |
                                                 Build and Run Result
```

The parser is line-oriented because current SMILE statements are line-oriented. It recognizes `LET`, deterministic `PRINT` forms, raw templates, `$"..."` interpolation, ordinary quoted strings, and string concatenation for `PRINT`.

The binder resolves variable names with an ordinal case-insensitive symbol table. This keeps declarations-before-use, duplicate declarations, and undefined variables target-independent. Target generators consume the bound program and do not reparse SMILE source text.

The bound tree preserves expression intent. C#, JavaScript, and Swift can therefore render interpolation-oriented SMILE expressions as native interpolation, while explicit SMILE concatenation remains target-language concatenation.

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

Generated target code should be semantically correct, idiomatic for the destination language, and close to code a competent human developer would naturally write. That rule applies even when a lower-level target must lower SMILE features into equivalent operations.

`SMILE.Engine` has no WPF dependency. That keeps the language front end reusable from the CLI, the desktop app, tests, and a possible future web interface.

`SMILE.Toolchains` owns local compiler/runtime detection, process execution, transpile-only target status, separate build/program timeouts, bounded process output, and optional press-any-key launcher scripts. Process work is asynchronous, cancellable, timed, and isolated in `%TEMP%\SMILE\Runs\<unique-id> - <language>\` so the WPF UI thread stays responsive, learners can identify each generated-code folder, and build artifacts stay out of the repository.

The desktop app tracks source revisions for live preview. Typing schedules a short debounced background transpilation for the visible target languages only. Manual Transpile All runs asynchronously and generates every target. Build & Run asks for the current source revision before invoking a toolchain, which prevents a compiler from running stale generated code.

`SMILE.Desktop` uses AvalonEdit for the editable SMILE source pane and the three generated target panes. Those four code panes show line numbers and lexical syntax highlighting, while the output area stays a plain append-only log for build and program output.

Recoverable desktop failures are contained at the operation boundary. Toolchain detection, build/run, child-process output, command-state refresh, and folder-opening failures report concise output, write a diagnostic log when possible, and keep the IDE open.

KISS keeps the architecture small: one engine project, one toolchain project, one CLI harness, one desktop app, and one test project. KISS v2 keeps the user experience responsive first.
