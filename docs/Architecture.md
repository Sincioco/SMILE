# Architecture

## Current strategy

SMILE 1.0 is a single-language, ten-target transpiler. Its only source language is the frozen SMILE Core BASIC 2 profile. The architecture intentionally has no compatibility layer: all public entry points construct the same parser and binder.

## Compiler pipeline

```text
Core BASIC source
    -> canonical lexer/parser
    -> ordered syntax tree and diagnostics
    -> multipass binder, global/routine scopes, and typed symbols
    -> bound program
       -> evaluator
       -> registered target backend (one of ten)
    -> generated files
    -> optional toolchain build/run
```

`SmileTranspiler` is the small public facade. `Parser` owns lexical and grammatical structure. `Binder` inventories declarations and routine signatures before binding bodies, then owns names, scopes, exact types, constants, calls, return paths, Select cases, arrays, writable counters, and typed-exit validity. Generators never select or detect a source language.

## Front end

The canonical lexer is nested with the parser so there is one reachable source-tokenization path. It recognizes case-insensitive Core BASIC keywords, Unicode identifiers, signed-64 decimal literal text, doubled-quote Text, apostrophe comments, line endings, and operators.

All SMILE 2.0 reserved words remain reserved. A reserved feature outside the frozen profile receives a diagnostic rather than becoming an identifier. Earlier SMILE 1.0-only spellings are ordinary invalid input; there is no retry through another lexer.

The parser preserves ordered comments and blank lines alongside statements, builds routines, calls, `If`, `Select`, `For`, `Do`, and array structure, and records physical spans for diagnostics. Newlines continue an expression only inside parentheses or an open Text literal.

## Binding and evaluation

Binding is case-insensitive with a shared program namespace and per-routine scopes:

- `Dim` creates fixed typed storage with a scalar default;
- first direct assignment creates an implicit fixed-type variable;
- `Const` resolves a compile-time scalar, including forward constant references, while rejecting cycles;
- later assignments preserve exact type and cannot target constants;
- expressions require declared or already assigned names;
- signatures permit forward and mutually recursive calls with exact positional scalar arguments;
- parameters and locals shadow globals, are fresh per invocation, and never leak;
- Select values are exact-type compile-time constants and arrays have positive compile-time dimensions;
- conditions require Boolean, loop bounds/counters/indexes require Number, and typed exits require a matching enclosing loop in the same routine.

The evaluator keeps globals outside a stack of reentrant call frames. Each frame owns copied ByVal parameters, locals, and local arrays. It preserves left-to-right evaluation, short circuiting, selector-once Select behavior, checked indexes, routine Return, typed exits, recursion, and whole-program `End Program` propagation.

## Generation registry

`CodeGeneratorRegistry` contains exactly one registered `ICodeGenerator` for each active target. Each entry delegates the bound Core BASIC program to the canonical target renderer for that language. The renderer has explicit per-target writers; no target reparses source or switches language behavior.

The active policy is centralized in `TargetLanguageInfo.All` and `ActiveTargetLanguages.All`, in this order:

1. C#
2. C
3. Windows x64 MASM Assembly
4. JavaScript (Node.js)
5. Java
6. COBOL
7. Objective-C
8. Swift
9. Python
10. C++

CLI, Desktop panes, generation tests, and toolchain registration consume the same set.

## Native target lowering

Structured destinations receive normal native routines, local variables, fixed storage, calls, conditionals, selection chains, and control flow. `For` bounds and Select selectors are evaluated once. `Do` stays post-tested. Typed exits use native `break` when possible and a normal target label when crossing another loop kind requires one.

Python uses module-level statements and its normal `for`, `while True`, and `if`. An exception class is generated only for a loop actually targeted by a typed exit that Python cannot express with an ordinary nearest-loop `break`.

C uses direct scalar storage, `printf`/`fputs`, `for`, `do`, and `if`. A small Text concatenation helper is emitted only when Text `+` occurs. Objective-C deliberately uses portable C-compatible constructs for the same dependency-light teaching path.

MASM uses ABI-correct `PROC` frames, register/stack arguments, global `.data`, local stack arrays, direct CRT/Win64 calls, and readable compare/branch labels. COBOL uses separate recursive program units, explicit shared global state, `LOCAL-STORAGE`, linkage parameters, `EVALUATE`, `OCCURS`, `DISPLAY`, and structured `PERFORM`. Their additional declarations exist only because those targets require them.

`End Program` maps to normal successful target termination. C# receives a minimal companion project because local `dotnet` compilation requires it.

## Desktop and CLI

The CLI requires a source path and target ID, with optional `--run`. `all` requests all ten targets. There is no language-related option.

Desktop creates one `SmileTranspiler`, loads the packaged `language.smile` after first paint, and asynchronously regenerates the visible active target. The UI has no profile selector. Highlighting contains only Core BASIC keywords, doubled-quote Text, numbers, operators, and apostrophe comments.

Process work is cancellation-aware and off the WPF UI thread. Generated programs build in unique `%TEMP%\SMILE\Runs` workspaces. Recoverable diagnostics and toolchain failures remain visible.

## Validation architecture

The test suite is organized around current behavior:

- `CoreBasicConformanceTests` — language, binding, evaluation, and explicit obsolete-source rejection;
- `CoreBasicGenerationTests` — deterministic all-target output and native construct markers;
- Desktop and highlighting focused tests;
- `CoreBasicToolchainSmokeTests` — installed all-target build/run comparison to the evaluator;
- `CoreBasicParityTests` and `CoreBasic2ParityTests` — unchanged fixture execution in both repositories and read-only authority verification;
- `CoreBasic2ToolchainMatrixTests` — nine Profile 2 programs built and run by all ten required toolchains plus a ten-target expected bounds-failure matrix;
- `MissionGuardrail` — the fast mandatory semantic and all-target guardrail.

Profile 1 fixtures remain in `tests/CoreBasicParity`; Profile 2 source/stdout pairs and their hash manifest live in `tests/CoreBasic2Parity`. `scripts/Test-CoreBasicParity.ps1` runs both reproducible cross-repository gates.

## Architectural decision rule

Keep one canonical semantic path. Add complexity only at the target boundary where a destination genuinely requires it, and keep that complexity absent from programs that do not use the feature.
