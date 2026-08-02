# Architecture

SMILE v0.2.0 uses a small direct compiler pipeline:

```text
Source -> Parser -> Syntax Tree -> Binder -> Bound Program -> Target Generator -> Generated Files
                                                        |
                                                 Optional Toolchain
                                                        |
                                                 Build and Run Result
```

The parser is line-oriented because current SMILE statements are line-oriented. It recognizes `LET`, deterministic `PRINT` forms, raw templates, `$"..."` interpolation, ordinary quoted strings, and string concatenation for `PRINT`.

The binder resolves variable names with an ordinal case-insensitive symbol table. This keeps declarations-before-use, duplicate declarations, and undefined variables target-independent. Target generators consume the bound program and do not reparse SMILE source text.

The engine also exposes a tiny target-neutral string flattener. It turns bound string expressions such as:

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

C, Objective-C, and MASM use those segments directly so v0.2.0 does not need a runtime string-concatenation library.

`SMILE.Engine` has no WPF dependency. That keeps the language front end reusable from the CLI, the desktop app, tests, and a possible future web interface.

`SMILE.Toolchains` owns local compiler/runtime detection, process execution, transpile-only target status, separate build/program timeouts, and optional press-any-key launcher scripts. Process work is asynchronous, cancellable, timed, and isolated in `%TEMP%\SMILE\Runs\<unique-id> - <language>\` so the WPF UI thread stays responsive, learners can identify each generated-code folder, and build artifacts stay out of the repository.

The desktop app tracks source revisions for live preview. Typing schedules a short debounced background transpilation for the visible target languages only. Manual Transpile All runs asynchronously and generates every target. Build & Run asks for the current source revision before invoking a toolchain, which prevents a compiler from running stale generated code.

KISS keeps the architecture small: one engine project, one toolchain project, one CLI harness, one desktop app, and one test project. KISS v2 keeps the user experience responsive first.
