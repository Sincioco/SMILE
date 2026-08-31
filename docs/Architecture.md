# Architecture

## Current strategy

SMILE 1.0 is a single-language, ten-target transpiler. Its only source language is SMILE Core BASIC 2.1 — Text-Game Foundation, selected from the read-only SMILE 2.0 authority. The architecture intentionally has no compatibility layer: all public entry points construct the same parser and binder.

## Compiler pipeline

```text
SMILE source
    -> canonical lexer/parser
    -> ordered syntax tree and diagnostics
    -> multipass binder, global/routine scopes, and typed symbols
    -> bound program + feature inventory
       -> evaluator + injected text-game host + statement budget
       -> registered target writer (one of ten)
    -> generated files
    -> optional toolchain build/run
```

`SmileTranspiler` is the small public facade. `Parser` owns lexical and grammatical structure. `Binder` inventories declarations and routine signatures before binding bodies, then owns names, scopes, exact types, constants, calls, return paths, Select cases, arrays, writable counters, and typed-exit validity. Generators never select or detect a source language.

## Front end

The canonical lexer is nested with the parser so there is one reachable source-tokenization path. It recognizes case-insensitive Core BASIC keywords, Unicode identifiers, signed-64 decimal literal text, doubled-quote Text, apostrophe comments, line endings, operators, commas in array dimensions/indexes, and the text-game statement words.

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
- Select values are exact-type compile-time constants and rank-one/rank-two arrays have positive compile-time dimensions with bounded total storage;
- conditions require Boolean, loop bounds/counters/indexes require Number, and typed exits require a matching enclosing loop in the same routine.

The evaluator keeps globals outside a stack of reentrant call frames. Each frame owns copied ByVal parameters, locals, and local arrays. It preserves left-to-right evaluation, short circuiting, selector-once Select behavior, checked one- and two-dimensional indexes, routine Return, typed exits, recursion, and whole-program `End Program` propagation.

`ISmileEvaluationHost` isolates terminal and nondeterministic effects: one-event key polling, frame boundaries, virtual Wait, monotonic time, and inclusive Random. The default host is safe for ordinary callers; scripted tests use a deterministic host. A configurable statement budget stops runaway game loops with `SMILER1222` without changing normal source semantics.

## Generation registry

`CodeGeneratorRegistry` contains exactly one registered `ICodeGenerator` for each active target. Each entry delegates the bound Core BASIC program to the canonical target renderer for that language. No target reparses source or switches language behavior. `CoreBasicProgramFeatureSet` inventories used operations once; the structured writer owns common statement/expression lowering, while focused runtime and COBOL/MASM writers emit only required target support.

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

Structured destinations receive normal native routines, local variables, fixed storage, calls, conditionals, selection chains, and control flow. Rank two uses rectangular or nested native arrays; JavaScript constructs independent rows and MASM uses a checked flat offset. `For` bounds and Select selectors are evaluated once. `Do` stays post-tested. Typed exits use native `break` when possible and a normal target label when crossing another loop kind requires one.

Python uses module-level statements and its normal `for`, `while True`, and `if`. An exception class is generated only for a loop actually targeted by a typed exit that Python cannot express with an ordinary nearest-loop `break`.

C uses direct scalar storage, `printf`/`fputs`, `for`, `do`, and `if`. A small Text concatenation helper is emitted only when Text `+` occurs. Objective-C deliberately uses portable C-compatible constructs for the same dependency-light teaching path.

MASM uses ABI-correct `PROC` frames, register/stack arguments, global `.data`, local stack arrays, direct CRT/Win64 calls, and readable compare/branch labels. COBOL uses separate recursive program units, explicit shared global state, `LOCAL-STORAGE`, linkage parameters, `EVALUATE`, nested `OCCURS`, `DISPLAY`, and structured `PERFORM`. Because ordinary `PIC X` fields are fixed-width and do not remember a SMILE Text value's logical end, each mutable COBOL Text field or array cell has a parallel numeric length. Calls pass that length with Text parameters and returns; exact reference modification preserves leading, embedded, trailing, and all-space values without trimming. A feature-gated C companion supplies the few Windows console calls GnuCOBOL does not expose directly.

Main-first ordering is structural policy: C#, C, MASM, Java, COBOL, Objective-C, and C++ put the main/primary body before user routines and compiler helpers. JavaScript (Node.js) uses `async function main()` only when key/Wait lifecycle requires it, propagates async through called routines, restores raw stdin in `finally`, and keeps helpers last. Python remains a direct script; Swift keeps ordinary top-level execution.

Text-game operations map to normal facilities: attached-console key polling and screen control, non-busy waits, monotonic clocks, and one process-level random source. Redirected key input returns `KEY_NONE`, redirected clear is a no-op, and runtime imports/helpers are feature-gated.

`End Program` maps to normal successful target termination. C# receives a minimal companion project because local `dotnet` compilation requires it.

## Desktop and CLI

The CLI requires a source path and target ID, with optional `--run`. `all` requests all ten targets. There is no language-related option.

Desktop creates one `SmileTranspiler`, loads the packaged `language.smile` after first paint, and asynchronously regenerates the visible active target. The UI has no profile selector. Highlighting includes the current Core BASIC 2.1 keywords and key constants, doubled-quote Text, numbers, operators, and apostrophe comments. Every source/generated `SmileCodeEditor` installs AvalonEdit's native Find panel on Ctrl+F and a validated Go to Line command on Ctrl+G. The Edit menu remembers the last focused editor so both actions target the pane the learner was using.

Process work is cancellation-aware and off the WPF UI thread. Generated programs build in unique `%TEMP%\SMILE\Runs` workspaces. Recoverable diagnostics and toolchain failures remain visible.

## Validation architecture

The test suite is organized around current behavior:

- `CoreBasicConformanceTests` — language, binding, evaluation, and explicit obsolete-source rejection;
- `CoreBasicGenerationTests` — deterministic all-target output and native construct markers;
- Desktop and highlighting focused tests;
- `CoreBasicToolchainSmokeTests` — installed all-target build/run comparison to the evaluator;
- `CoreBasicParityTests` and `CoreBasic2ParityTests` — unchanged fixture execution in both repositories and read-only authority verification;
- `CoreBasic2ToolchainMatrixTests` — nine Profile 2 programs built and run by all ten required toolchains plus a ten-target expected bounds-failure matrix;
- `TextGameFoundationTests` — syntax/binding/evaluator/order/idiom checks and deterministic scripted games;
- `TextGameToolchainMatrixTests` — one complete deterministic 2D/console/intrinsic fixture on every toolchain;
- `TextGameInteractiveMatrixTests` — real Windows ConPTY keys, redraw, cleanup, and all three games on every target;
- `MissionGuardrail` — the fast mandatory semantic and all-target guardrail.

Profile 1 fixtures remain in `tests/CoreBasicParity`; Profile 2 source/stdout pairs and their hash manifest live in `tests/CoreBasic2Parity`. `scripts/Test-CoreBasicParity.ps1` runs both reproducible cross-repository gates.

## Architectural decision rule

Keep one canonical semantic path. Add complexity only at the target boundary where a destination genuinely requires it, and keep that complexity absent from programs that do not use the feature.
