# Architecture

SMILE v0.1 uses a small direct transpiler pipeline:

```text
Source -> Lexer -> Parser -> Syntax Tree -> Target Generator -> Generated Files
                                             |
                                      Optional Toolchain
                                             |
                                      Build and Run Result
```

The lexer turns characters into tokens. The parser turns tokens into a small language-neutral syntax tree. Target generators consume that same syntax tree directly.

SMILE deliberately does not generate C first and then derive other targets from C. Each target generator owns its own native output so students can compare the same idea in several languages.

`SMILE.Engine` has no WPF dependency. That keeps the language front end reusable from the CLI, the desktop app, tests, and a possible future web interface.

`SMILE.Toolchains` owns local compiler/runtime detection and process execution. Process work is asynchronous, cancellable, timed, and isolated in `%TEMP%\SMILE\Runs\<unique-id>\` so the WPF UI thread stays responsive and build artifacts stay out of the repository.

KISS keeps the architecture small: one engine project, one toolchain project, one CLI harness, one desktop app, and one test project. KISS v2 keeps the user experience responsive first, then optimizes functional work such as parsing once for multiple target generators.
