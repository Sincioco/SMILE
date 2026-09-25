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

`SmileType` is an immutable type symbol. The five existing scalar/error symbols
are shared singletons; exact assignment, argument and return checks compare
symbol identity. `SmileTypeKind` describes their storage/operation category.
Each Enum owns its symbol and checked member values. Scalar type-pattern matching uses
Kind, while semantic equality stays exact. The bound program, symbols and values
carry the type directly; no process-wide registry or numeric type-ID allocation
is involved. `TypeNameSyntax` keeps unresolved type spelling/location in the
syntax tree. `Parser.Enums` owns declarations/member-access syntax;
`Binder.Enums` resolves named types and checked Enum initializers before ordinary
constants and routine signatures. `Enums.cs` holds the focused syntax/symbol/bound
model; values retain nominal identity through evaluation, aliases and ByRef.
The structured enum writer and native enum writers emit declarations/constants;
`TargetIdentifierMap.Enums` owns safe type/member spellings. No enum runtime
helper, type registry or numeric type ID is added.

`Parser.Records`, `Binder.Records` and `Records.cs` own Type declarations, nominal
field symbols, layout-cycle/storage validation and bound field locations.
`RecordValues` owns evaluator storage and copies into existing field cells;
`Evaluation.Records` captures checked field locations. ByVal arguments copy before
later arguments run. `TargetIdentifierMap.Records` owns safe field/type spellings.
Focused record declaration/location/storage writers use native structs and
aggregate copies in C#, C, Objective-C, C++ and Swift, MASM structures and native
ABI aggregate passing, and COBOL groups/OCCURS/MOVE. C# array-bearing records,
Java/JavaScript classes and Python dataclasses need explicit copies; assignment
copies into existing storage so previously captured field aliases remain valid.
Java adapts only field-addressed ByRef parameters and their forwarding paths with
read/write closures. C/Objective-C register owned Text fields through the existing
collector, retaining record result roots through final callee collection. MASM
caller-owned aggregate buffers are rooted before calls. No new type registry,
record interpreter or dependency is added. Type methods and Class references use
the shared instance-member symbols described below.

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

The evaluator keeps globals outside a stack of reentrant call frames. Each frame owns copied ByVal parameters, locals, and local arrays. ByRef parameters retain captured caller locations; writes are immediate and aliases remain shared. It preserves left-to-right evaluation, short circuiting, selector-once Select behavior, checked one- and two-dimensional indexes, routine Return, typed exits, recursion, and whole-program `End Program` propagation.

`ISmileEvaluationHost` isolates terminal and nondeterministic effects: one-event key polling, clear/top-left frame boundaries, cursor moves, named color changes, virtual Wait, monotonic time, and inclusive Random. The default host is safe for ordinary callers; scripted tests use a deterministic host. Wait clamps once to the unsigned 32-bit millisecond maximum, and a reversed Random range returns its evaluated lower bound without consuming randomness. A configurable statement budget stops runaway game loops with `SMILER1222` without changing normal source semantics.

`TextFileLoading` owns executable-relative path normalization and bounded byte
loading in the evaluator. `ISmileFileHost` supplies a disposable stream and
`SmileDirectoryFileHost` is the default directory-backed implementation; tests
inject memory streams. `SmileEvaluationOptions.Files` selects the host. The
source is evaluated before the destination is cleared and Count is assigned only
after the read. Binding remains in `Binder.TextFile`, while the parser and
formatter own syntax and expression traversal.

## Generation registry

`Parser.Data`/`Binder.Data` own Data byte-array operations. Output locations remain
bound expressions, so arrays and ByRef outputs retain their existing storage
ownership. `Evaluation.Data` calls `SmilePersistentStorage`'s separate Data module,
then evaluates Count and Status locations in that order. Strict failures stop
before output locations are evaluated. `CoreBasicDataSyntax` holds these nodes and
their focused traversal facts. No persistence logic lives in CLI/Desktop.

The structured, MASM and COBOL Data writers lower operations through standard file
and SHA-256 APIs. Target templates are split by destination; the native C support
is shared by C/C++, Objective-C, MASM and COBOL. Only Data programs need that support
or Windows BCrypt linkage. Save uses flushed temporary files and atomic replacement;
Node's standard API publishes backup and primary through two separate renames.
Unrelated programs acquire no storage dependencies. Hashes preserve full UTF-8
key/program identity, rather than integer-storage filename sanitization.

`Parser.Persistence` and `Binder.Persistence` own integer Load/Save syntax and
exact Number/key validation. `SmilePersistentStorage` owns evaluator storage;
`SmileEvaluationOptions.Storage` permits an isolated program/root. The structured,
MASM, and COBOL lowering lives in `CoreBasicNumberPersistenceWriter`; managed and
native support modules use standard file APIs and emit only required operations.
Node.js propagates asynchronous storage through the existing call analysis.
CLI/Desktop pass the source filename stem through the transpiler into immutable
`BoundProgram.ProgramName` (direct API default: `Program`). This keeps storage
stable across generated filenames and fresh run workspaces. Desktop regenerates
after Save As changes that identity, even when source text is unchanged.

`Binder.RoutineArguments` owns Optional-default validation and named-argument binding. Bound calls retain source-order expressions and a parameter-order index list; the evaluator and each writer apply that list only after capturing arguments. `RoutineArguments` provides this small compiler-side ordering operation, without introducing a generated calling framework. The parser continues to own all syntax, including multiline parameter lists and named labels.

`Evaluation.RoutineArguments` owns evaluator location capture. ByRef array indices
are checked one dimension at a time before later argument effects. The structured
ByRef writer lowers native references/pointers and Java/JavaScript/Python
array-and-index parameters. Only scalar storage actually passed ByRef is boxed
in those three targets. The separate ByRef analysis tracks addressed storage and
Swift call-graph overlap: safe routines keep native `inout`; potentially shared
locations use a small getter/setter closure. MASM passes captured addresses in
integer ABI slots, including for Double, and dereferences parameter storage.
COBOL passes the original data item and, for Text, its logical-length item using
native BY REFERENCE. Caller Text roots remain owned by their declaring frame;
ByRef callees do not register or release those roots a second time.

`TextIntrinsics` owns scalar-based text evaluation. `CoreBasicTextInspectionWriter` uses each structured target's normal Unicode/string APIs. `NativeTextInspection` emits only the used UTF-8 operations for C-family, MASM, and COBOL, sharing traversal semantics across those backends. Text slices reuse the existing native Text allocation/root owner; C/Objective-C/MASM do not add a second lifetime mechanism. Default expressions and named values are included in formatter traversal.

`DoubleSemantics` owns exact-type binary64 constant/evaluator rules; `Binder.Double` validates numeric intrinsic signatures. `DoubleProgramFeatures` inventories the operations that require support, excluding folded constant initializers. Focused Double writers lower native arithmetic, math, conversions, and formatting for structured, MASM, and COBOL targets. `NativeDoubleSupport` owns the small C boundary shared by the native numeric writers. Double Text conversion reuses existing Text allocation ownership. Ordered argument/print capture uses ordinary local temporaries when a later call could change an earlier value.

MASM Double values use REAL8 storage and SSE operations; learner calls use Windows x64 XMM argument and return registers. COBOL uses native FLOAT-LONG storage. Its literal conversion, COMPUTE, NUMVAL-F, and decimal comparison paths failed binary64 edge tests, so exact operations use standard C interoperability while same-type storage copies remain MOVE. No bit-cast API or general numeric runtime is exposed to learners.

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

`CoreBasicTextFileWriter` lowers the file-read statement. `ManagedTextFileSupport`
and `NativeTextFileSupport` provide feature-selected standard file/stream APIs,
path normalization, BOM removal, and zero-fill. Node.js uses asynchronous reads;
the existing await propagation now includes routines containing a text-file read.
MASM delegates file mechanics to `SmileFileRuntime.c`, and COBOL reuses its
ordinary C-interoperability companion. No game logic or generic file framework
is emitted.

## Native target lowering

Structured destinations receive normal native routines, local variables, fixed storage, calls, conditionals, native selection where exact, readable selector-once chains otherwise, and control flow. Rank two uses rectangular or nested native arrays; JavaScript constructs independent rows and MASM uses a checked flat offset. `For` bounds and Select selectors are evaluated once. `Do` stays post-tested. Typed exits use native `break` when possible and a normal target label when crossing another loop kind requires one. One recursive bound-tree walker is the authority for nested loop/Select exit discovery.

Python uses module-level statements and its normal `for`, `while True`, and `if`. An exception class is generated only for a loop actually targeted by a typed exit that Python cannot express with an ordinary nearest-loop `break`.

C uses direct scalar storage, combined `printf`, `for`, `do`, and native numeric/Boolean `switch`. A small immutable Text allocation registry is emitted only when Text `+` occurs. Generated global, parameter, local, array-element, selector, and expression roots make assignments and returns ordinary pointer writes while statement-boundary collection bounds temporary lifetime. Controlled shutdown exposes allocation/free/live/peak counters for stress verification. Objective-C deliberately uses the same portable C-compatible teaching path.

MASM uses ABI-correct `PROC` frames, register/stack arguments, global `.data`, local stack arrays, direct CRT/Win64 calls, and readable compare/branch labels. When Text concatenation is used, a generated `SmileTextRuntime.c` companion provides only the same explicit-root collector and counters; the assembly still contains all learner control and data flow. COBOL uses separate recursive program units, explicit shared global state, `LOCAL-STORAGE`, linkage parameters, `EVALUATE`, nested `OCCURS`, `DISPLAY`, and structured `PERFORM`. Because ordinary `PIC X` fields are fixed-width and do not remember a SMILE Text value's logical end, each mutable COBOL Text field or array cell has a parallel numeric length. Calls pass that length with Text parameters and returns; exact reference modification preserves leading, embedded, trailing, and all-space values without trimming. A feature-gated C companion supplies the few Windows console calls GnuCOBOL does not expose directly.

Main-first ordering is structural policy: C#, C, MASM, Java, COBOL, Objective-C, and C++ put the main/primary body before user routines and compiler helpers. JavaScript (Node.js) uses `async function main()` only for Wait or asynchronous file/persistence operations and propagates async through called routines. Key polling requires no async wrapper or raw-stdin lifecycle. Python remains a direct script; Swift keeps ordinary top-level execution.

ConsoleKeyMap owns compile-time key metadata and shared C event-reader rendering.
CoreBasicConsoleInputWriter and CoreBasicMasmConsoleInput emit the native bindings
and switches; the Windows input buffer owns pending events. No process-global
keyboard polling is used. NodeConsoleSupport supplies a minimal feature-gated
Node-API addon because Node has no built-in Win32 FFI. NodeToolchain owns its local
MSVC compilation. No new package or external build dependency is introduced.

Text-game operations map to normal facilities: attached-console key polling, clearing, cursor positioning, named foreground/background colors, non-busy waits, monotonic clocks, and one process-level random source. Redirected key input returns `KEY_NONE`; redirected screen/color operations are no-ops; runtime imports/helpers are feature-gated.

`End Program` maps to normal successful target termination. C# receives a minimal companion project because local `dotnet` compilation requires it.

## Formatting pipeline

`SmileSourceFormatter` parses and binds valid source before formatting. Syntax spans drive indentation, comment attachment, routine/control boundaries, legal Select spacing, and balanced-parenthesis call wrapping. It verifies the formatted program again and compares protected Text/comment payloads; failure returns the original source unchanged. Output is LF, idempotent, and ends with one newline.

`GeneratedSourceLayout` is separate from source formatting. Target writers emit semantic blank boundaries while the final policy removes leading/trailing/excess blank lines and trailing whitespace, preserving Python's conventional two blank lines between top-level definitions and COBOL/MASM leading columns.

## Desktop and CLI

The CLI requires a source path and target ID, with optional `--run`. `all` requests all ten targets. Explicit `--format` and `--check` modes use the Engine formatter without requiring a target; successful writes replace the file atomically. `scripts/Format-Smile.ps1` is the repository batch wrapper. There is no language-related option.

Desktop creates one `SmileTranspiler`, loads the packaged `language.smile` after first paint, and asynchronously regenerates the visible active target. The UI has no profile selector. Highlighting includes the current Core BASIC 2.1 keywords and key constants, doubled-quote Text, numbers, operators, and apostrophe comments. Every source/generated `SmileCodeEditor` installs AvalonEdit's Find behavior on Ctrl+F with a SMILE-owned template whose Previous, Next, Close, and option controls use visible text labels. Ctrl+G opens a validated Go to Line dialog. The Edit menu remembers the last focused editor so both navigation actions target that pane. `Format SMILE` and `Ctrl+K, Ctrl+D` explicitly format only the source document with one AvalonEdit replacement/undo record; ordinary live generation never writes back.

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
- `SourceFormattingTests` — formatter safety/idempotence, living-source check, CLI integration, and one-step Desktop undo;
- `CoreBasicHardeningTests` — recursive Select/Exit correctness, native Print/Select, generated layout, all-ten compilation, and 50,000-iteration C/Objective-C/MASM Text lifetime counters.

Profile 1 fixtures remain in `tests/CoreBasicParity`; Profile 2 source/stdout pairs and their hash manifest live in `tests/CoreBasic2Parity`. `scripts/Test-CoreBasicParity.ps1` runs both reproducible cross-repository gates.

## Architectural decision rule

Record member syntax/binding has focused Parser.RecordMembers and Binder.RecordMembers
owners. Types own method/property symbols; routines own borrowed receivers and
setter values separately from user parameters. Bound calls retain source evaluation
order and map hidden state to execution parameters, reusing existing argument,
ByRef, copy and lifetime logic. CoreBasicRecordMembers owns native declarations and
call forms; shared routine bodies remain the single statement-generation path.

With binding owns a lexical stack of receiver symbols, while evaluation captures
and restores writable locations across recursive calls. Existing record-location
writers own target aliases and index capture. Shared control-flow scans traverse
With bodies so return analysis, loop exits, feature gating and mutation analysis
see the same statements; the block introduces no new runtime service.

Keep one canonical semantic path. Add complexity only at the target boundary where a destination genuinely requires it, and keep that complexity absent from programs that do not use the feature.

Class parsing/binding and evaluation have focused partials: Parser.Classes,
Binder.Classes and Evaluation.Classes. Instances.cs owns shared Type/Class field
and member symbols; each nominal type owns its fields, methods and properties.
ClassValues.cs owns evaluator heap instances. A Class reference is separate from a
copied record value; captured locations keep their owning instance alive.

CoreBasicClasses owns native class declarations, constructors and identity.
C, MASM and COBOL class writers own their storage/ABI details. NativeClassSupport
owns the small allocator and root list required for those procedural targets,
including Text-field finalization. It is emitted only for class programs. C++
uses standard shared ownership; the other structured targets use their native
object lifetime rules. Existing record/member writers remain the shared owners
of field access and member calls for both kinds of instance.

## Multi-file compilation and project ownership

Parser.Modules creates source-preserving Module/Import/visibility syntax.
ModuleCompilation owns provider inventory, source-local options/imports, dependency
ordering, public API checks and syntax lowering into the existing Binder. Focused
expression/statement partials handle rewriting; no second type checker or runtime
module registry is introduced. BoundProgram retains source/module ownership for
diagnostics, target naming and package metadata. Each target emits ordinary
declarations and routines with deterministic readable module prefixes.

Projects/SmileProject owns XML metadata. SmileProjectLoader owns an individual
load's provider graph and produces an immutable SmileCompilationInput snapshot.
It validates libraries using their own declared dependency closures, excluding
consumer globals and unrelated sibling providers. SmileLibraryPackage owns the
bounded archive envelope and atomic output publication; SmileLibraryApi derives
format-7 public metadata from bound symbols. Package source stays in memory.
SmileProjectAssets resolves portable project-relative files; ToolchainAssets
copies them after compilation into each fresh runtime directory, never before
the compiler could accidentally treat an asset as generated source.

DesktopProjectSession keeps the project path and selected startup path. Its
background snapshots replace only the editor-owned text and reload support files.
MainWindowViewModel.Sources owns source opening/saving/format orchestration; the
main view model delegates generation to the session. Open/format use the existing
busy/progress/cancellation boundary and reject stale results after an edit.
CLI options and formatting have separate focused owners. No new dependency is used.
