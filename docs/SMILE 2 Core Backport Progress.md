# SMILE 2.0 non-graphical back-port inventory

Authority inspected read-only: `D:\SMILE 2.0`, commit
`e97afe296f6866d97ad035c9a9b0c9596b919fe0`. The authority repository was clean
before inspection and after the unchanged fixture compilation/execution.

The overall requested back-port is **not complete**. This file records the
completed text, routine, console-key, Double, text-file, persistence, Enum, Type records/members, With, and ByRef milestones and the remaining source-language gaps.

## Implemented in this milestone

| Feature | SMILE 1.0 result |
|---|---|
| `Text_Length`, `Text_Code_At`, `Text_Slice` | Unicode scalar semantics in evaluation and all ten targets |
| Optional parameters | Exact scalar literal/Const defaults; explicit ByVal supported |
| Named arguments | `Name:=Value`; single evaluation in authored order before parameter placement |
| Multiline routine declarations | Balanced declaration parentheses; explicit parameter and return types |
| ByRef parameters | Exact-type writable scalars, array cells, forwarding and aliases; immediate writes; source-order index checks; all ten targets |
| Unary Number `+` | Identity operation with unary precedence |
| Unicode console output | UTF-8 for C#, Java, Python when source contains non-ASCII Text |
| Expanded console keys | O/F/G/R/P/B/X/Y/Z/E/C, Backtick, Plus and Minus events on all ten targets; Control constant available |
| Double arithmetic | Distinct binary64 literals, scalar/array storage, routines, Optional defaults, same-type operators and exact comparisons |
| Double math and conversions | ToDouble/ToNumber; polymorphic Abs/Min/Max; Clamp, Sqrt, Sin, Cos, Atan2, Floor, Ceiling, Truncate, Round; Text_From_Double/Text_To_Double |
| Load Text File | Expression paths, executable-relative normalization, bounded UTF-8 bytes, BOM removal, zero-fill, safe missing/unreadable results; all ten targets |
| Integer Load/Save | Literal keys, Number variables/constants, eagerly evaluated defaults, signed-64 decimal files, stable source identity; evaluator and all ten targets |
| Data Load/Save | Byte arrays, computed UTF-8 keys, 1 MiB bound, SMD4 checksums, checked recovery/Status and strict failures; evaluator and all ten targets |
| Nominal Enum | Checked signed-64 members, aliases, zero initialization, Const, arrays, ByVal/ByRef, Optional defaults, returns, exact equality and Select; evaluator and all ten targets |
| Type value records | Exact nominal identity, nested fields, fixed-array fields, independent copies/defaults, arrays of records, ByVal/ByRef/returns, writable field references and Data Count/Status fields; evaluator and all ten targets |
| With blocks | Capture writable record locations and checked indexes once; nested leading-dot access, stable aliases, recursion and ordinary return/loop-exit behavior; evaluator and all ten targets |
| Type members | Sub/Function methods, Get/Set properties, borrowed Me, Public/Private access, Optional/named calls and value-first property assignment; evaluator and all ten targets |
| Checked Double failures | Invalid domains, zero divisors, conversion overflow and nonfinite results report their source line before destination mutation |

The UTF-8 output fix addresses failures observed during actual generated-program
execution on Windows. Ordinary ASCII programs retain their minimal output.

## Remaining non-graphical gaps

| Area | Implemented SMILE 2.0 features still absent from SMILE 1.0 |
|---|---|
| Console keys | Standalone Control key events: character-stream APIs do not report modifier-only events; a native console-event boundary is still needed |
| Files and persistence | Project ApplicationId ownership/configuration; current loose programs use the source filename stem |
| Reference objects | Class references/constructors, New, Nothing, Is/Is Not, With on class references, class member visibility and ownership |
| Program organization | Module/Import, qualified names, visibility, multi-file source/project inputs, deterministic source-owned libraries |

Classes and modules require additional binding,
generation and project-system work. They remain outstanding core-language work.

## Outside the console/core back-port

Graphics, Game Window, pointer input, rendering, images, audio and media are
graphical/runtime facilities. `Key_Held` and `Key_Event_Held` require Game Window
in the authority's semantic analyzer, so they are not console primitives.
GUI file pickers and shell-reveal operations should be scoped separately from
ordinary text-file/persistence operations.

Blocking `Input`, While, dynamic arrays, variadic parameters, and rank-three
arrays are not newly implemented SMILE 2.0 features to import. Legacy untyped
routine/array forms are older compatibility forms, not additions to the canonical
explicitly typed SMILE 1.0 subset.

## Ownership and validation

- `Binder.RoutineArguments.cs` validates defaults and binds names to parameter
  slots. The bound tree retains source-order expressions and the parameter map.
- `RoutineArguments.cs` applies the map after evaluation/capture; it is a compiler
  utility, not emitted runtime machinery.
- `Evaluation.RoutineArguments.cs` owns captured writable locations. The focused
  structured ByRef analysis/writer selects native references and the necessary
  array/index or Swift location adapter. MASM uses direct addresses; COBOL uses
  native BY REFERENCE, including Text length. Caller frames own referenced Text.
- `TextFileLoading.cs` owns evaluator file semantics and the injectable stream host.
  `CoreBasicTextFileWriter.cs` owns lowering; managed/native file-support modules
  use normal file APIs. Node.js uses asynchronous file handles.
- `TextIntrinsics.cs` owns evaluator/static-analysis scalar traversal.
- `Persistence.cs` owns evaluator integer storage, with an injectable root.
  Focused persistence parser/binder/writers use standard file APIs. CLI/Desktop
  embed the source filename stem for storage stability across target/run folders.
  SMILE 1.0 stores beneath `SMILE`, separate from the authority's `SMILE 2.0` root.
- `DoubleSemantics.cs` owns evaluator/static-analysis binary64 rules. `Binder.Double.cs`
  binds exact numeric intrinsic signatures. `DoubleProgramFeatures.cs` inventories
  required numeric support without emitting helpers for folded constants.
- The focused structured, MASM, and COBOL Double writers own target lowering;
  `NativeDoubleSupport.cs` owns the C numeric boundary. MASM uses REAL8/SSE and
  native Windows x64 floating argument/return registers. COBOL uses FLOAT-LONG
  storage and C interop where its decimal transfers/arithmetic fail binary64 tests.
- `CoreBasicTextInspectionWriter.cs` owns structured-target text lowering;
  `NativeTextInspection.cs` owns the UTF-8 traversal needed by C-family/MASM/COBOL.
- Slices reuse existing C/Objective-C/MASM root tracking. COBOL keeps its existing
  4096-byte storage and explicit logical lengths. No third-party dependency or
  source-language graphics feature was added.

Actual validation includes the new evaluator/diagnostic tests, text/routine/Double/text-file/ByRef fixtures
built and run across all ten targets, MissionGuardrail, existing core conformance
and terminal coverage, and unchanged fixtures built/run with the authoritative
SMILE 2.0 compiler. Generated binaries and parity scratch files belong in ignored
`out/backport-validation` and `out/backport-parity`. Double regression coverage
includes adjacent binary64 values, subnormals, signed zero, rounding ties,
19-digit Number conversion, named/Optional calls, mixed calls exceeding four
arguments, exact Select Case, print/argument evaluation order, and six runtime
failure programs per target. The COBOL comparison/literal/transfer path was
checked against actual generated GnuCOBOL C output before selecting interop. The text-file fixture runs on all ten targets and
unchanged in SMILE 2.0; it covers BOM/Unicode/NUL bytes, multi-chunk reads, short
files, truncation, zero-fill, path expressions and normalization, invalid/missing
paths, global/local arrays, and asynchronous routine propagation. It also keeps
regressions for a Windows GetPath name collision and C++ literal concatenation.
The ByRef fixture covers shared scalar/array aliases, all four scalar types,
named/Optional calls, mixed calls beyond four parameters, recursion, forwarding,
ByVal isolation, global aliases, local arrays, loop counters and file counts.
An out-of-bounds first dimension stops before the later index or call on all ten
targets. Swift flushes prior output before its native bounds/numeric-error traps;
the existing Double failure matrix now checks prior output as well as diagnostics.
Integer persistence runs unchanged in SMILE 2.0 and on all ten targets, covering
both signed limits, eager defaults, ByRef/routine access, Unicode-key sanitizing,
missing/unreadable files, 63-byte parsing, malformed text and overflow. A reproduced
.NET trailing-NUL parsing difference is fixed and retained as a regression case.
The Data fixture also runs unchanged in SMILE 2.0, with exact file envelopes,
Unicode/empty keys, corrupt checksums/signatures, missing/corrupt primary recovery,
invalid counts/bytes, unavailable directory paths, capacity limits, local arrays,
ByRef counts and post-I/O Count/Status location order. Strict load/save failures
preserve prior output and exit 2 on all ten targets. Native header/API differences
use the widely available BCrypt create/update/finish API. C/Objective-C now widen
direct Number literals correctly at the variadic Print boundary.

The Enum fixture also runs unchanged in SMILE 2.0 and across all ten targets, checking aliases, signed limits, unnamed zero, arrays, ByRef/ByVal, Optional defaults, named calls, returns, Select and target-sensitive member names. Objective-C minimum-Int64 enum literals use the signed-safe native constant after an actual compiler failure. Enum lowering adds native declarations/constants only, with no runtime helper.

Record fixtures run unchanged in SMILE 2.0 and on all ten targets. They cover
nested record/array copies, source-order ByVal snapshots, stable field references
through whole-record replacement, scalar and aggregate returns, allocated Text,
forwarded Text/Double/Boolean/Enum references and Data Count/Status fields. The
record formatter retains structural indentation. Reproduced COBOL field-name
ambiguity and left-operand timing errors have regression coverage. MASM retains
native small-value returns and uses hidden caller buffers for larger aggregates.

The With fixture also runs unchanged in SMILE 2.0 and on all ten targets. It checks
one-time index capture, nested receivers, replacement of the containing record,
ByRef fields, recursive re-entry, ByVal isolation, Return and Exit For through an
intervening Do. Native aliases/addresses or captured COBOL subscripts retain the
location; Swift reuses a location with captured indexes. No runtime helper is added.
The C/Objective-C/MASM allocating-index/early-return fixture reports 41 allocations,
41 frees, zero live allocations and a peak of two. With index temporaries release
their Text roots before entering the body.

Type member fixtures run unchanged in SMILE 2.0 and on all ten targets, covering
borrowed/aliased receivers, recursion, Optional/named calls, mixed six-argument
integer/Double calls, read-only/write-only/private properties, aggregate copies,
ByRef setter Value, and text-file reads inside accessors. Native instance methods
and properties are used where available. C/Objective-C, MASM and COBOL use explicit
receiver procedures; C++/Java use getter/setter methods. Aliased Swift receivers
reuse the existing location adapter through static member methods, and asynchronous
JavaScript accessors become async getter/setter methods. No new member runtime is
introduced. Missing nested terminators preserve later declarations for diagnostics.
Regressions cover Python self/property names, Swift newValue shadowing, COBOL's
31-character program identity limit and a method parameter shadowing an Enum name.
The last case is a recorded parity exception: SMILE 2.0 supports this shadowing
after module linking, but its loose-file Enum lookup incorrectly overrides the
local parameter. SMILE 1.0 consistently retains local variable lookup first;
the separate regression is not claimed as an unchanged SMILE 2.0 fixture.

The member milestone passed 111 focused member/record/ByRef/With/mission/reference/
highlighting tests, the normal 22-test MissionGuardrail, the full living-source
format check, and the cumulative example on all ten installed toolchains. The
Desktop build has zero warnings/errors. Member Text lifetime probes finish with
zero live allocations on C, Objective-C and MASM (2/2 and 1/1 allocations/frees).

No repository file-size/complexity checker or reviewed no-growth baseline was
found in SMILE 1.0. Existing broad writers received focused wiring; new text and
argument algorithms have their own owners. No guardrail limit or exclusion was
changed. Ordinary target-native Number overflow and existing native Text storage
limitations remain; object/module and project-identity parity is pending. Double exponent
spelling and transcendental rounding follow the target's standard library.
