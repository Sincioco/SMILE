# SMILE Target Code Generation Standard v1.0

## Status

This standard governs generation for SMILE Core BASIC 2.1 across the ten active destinations. `AGENTS.md`, [Core Principles](SMILE%20Core%20Principles.md), and the [Text-Game Foundation Official Specification](SMILE%20Language%20Specification/003%20-%20SMILE%20Core%20BASIC%202.1%20Text-Game%20Foundation%20Official%20Specification.md) have higher authority.

## Governing rule

Generated target code is part of the lesson. Use the normal, idiomatic, beginner-readable destination construct whenever practical. Preserve source meaning, but do not build a private runtime when the destination already expresses that meaning clearly.

## Shared contract

Every backend receives the same bound Core BASIC program. A generator must not:

- read source text to infer syntax;
- select another profile or compatibility behavior;
- execute or unroll learner loops during generation;
- replace runtime storage with stale compile-time values;
- change short-circuiting, post-test behavior, bound evaluation, or typed-exit destination;
- change left-to-right argument/operand evaluation, ByVal isolation, recursive frame behavior, selector-once Select, or checked array indexing;
- add support for syntax outside the official profile.

Generated files are deterministic. Imports, helpers, declarations, labels, and companion files appear only when required by the actual source or destination toolchain.

Every generated text file has no leading/trailing blank line or trailing whitespace, uses LF, ends with exactly one newline, and has no more than one consecutive blank line except Python's conventional two between top-level definitions. Semantic sections separate imports, constants/state, prototypes, entry code, learner routines, generated support, and the footer. Source-authored blanks are soft body boundaries; semantic control/output phases may add a hard boundary after lowering relocates declarations.

## Canonical source fixture

```smile
Const Greeting = "Hello"
Total = 0

For I = 1 To 3
    Total = Total + I
End For

Do
    Total = Total - 1
Loop Until Total = 0

If Total = 0 Then
    Print Greeting; "!"
End If
```

A target should make the variable, counted loop, post-test loop, conditional, and output recognizable without compiler-internal ceremony.

## Values and expressions

- Map Number to the destination's ordinary signed 64-bit integer type where one exists.
- Map Boolean to its ordinary Boolean type or the clearest conventional representation on lower-level targets.
- Map Text to the ordinary dependency-light text representation.
- Preserve truncating division and signed `Mod` semantics. A helper is acceptable only where the destination operator differs.
- Preserve short-circuit `And` and `Or` with native operators or explicit low-level branches.
- Preserve case-sensitive Text equality/inequality and Text concatenation. Text ordering is outside Profile 2.0. Emit C-family support only for programs that use it.
- Escape target literals from the bound Text value; never reinterpret source delimiters.

Target-native integer overflow behavior can differ at extreme values because this beginner-first profile does not require a generated arbitrary precision or checked-arithmetic runtime in every destination. Ordinary signed-64 inputs and the pinned parity corpus remain cross-target conformance requirements.

Calls use normal destination routines and native call frames. Destinations without a guaranteed left-to-right native argument order capture source arguments in readable temporaries first. ByVal parameters remain independent copies, including when assigned by the callee. ByRef parameters retain exact-type writable locations and immediately observable aliases; copy-in/copy-out cannot replace shared storage.

Named calls capture explicit values in source order, then pass ordinary arguments in declaration order. Optional defaults are emitted as explicit literal arguments when omitted. This keeps one native routine per learner routine across all ten targets. No argument-dispatch runtime is permitted.

ByRef uses native C# `ref`, C/Objective-C pointers, C++ references, MASM addresses,
and COBOL BY REFERENCE data items (plus the Text logical length). For Java,
JavaScript, and Python, use ordinary arrays and captured indices; one-element
arrays are needed only for addressed scalars. Swift uses native `inout` when the
call graph proves exclusive access; a focused getter/setter location adapter
preserves shared aliases that Swift's exclusivity rules otherwise prohibit.
Check each ByRef array dimension before evaluating the next index or argument.
No target may replace references with deferred write-back or a general dispatch
runtime. Native failure diagnostics may differ in formatting, but prior learner
output must survive a checked failure.

Unicode inspection uses native scalar iterators, streams, or string indexing where available. Small boundary helpers implement SMILE's `-1`/empty-Text results instead of target exceptions. UTF-8 targets require shared scalar traversal; slice allocation reuses the native Text lifetime owner. Programs containing non-ASCII Text configure UTF-8 output on C#, Java, and Python so redirected output does not depend on the Windows code page.

## Storage

Implicit assignment, `Dim`, constants, parameters, local/global variables, arrays, and loop counters become clear target declarations and assignments. Declarations may be hoisted where the destination requires it, but learner reads and writes stay visible. Constants should use a destination constant when its declaration model permits; otherwise use the smallest faithful immutable representation.

Fixed rank-one and rank-two arrays use native fixed storage where practical. Every dynamic index is checked against its SMILE zero-based dimension before access, even when the target would otherwise allow negative indexing, sparse extension, one-based subscripts, or unchecked memory. Index expressions run left to right exactly once; every assignment index is checked before its right-hand value. Text cells start as empty Text. Routine-local arrays are new/defaulted per call.

COBOL's native fixed `PIC X(4096)` fields require a parallel logical length for mutable Text scalars, array cells, parameters, returns, and expression temporaries. Propagate that length through normal COBOL call linkage and use guarded reference modification for exact output, equality, selection, and concatenation. Never infer SMILE Text length with trailing trim: an all-space value is data, not padding.

Do not generate unused declarations. Preserve case-insensitive SMILE identity while mapping names deterministically away from destination reserved words and collisions.

Integer Load/Save uses normal bounded file reads and decimal writes, with small
helpers for the shared storage path and recoverable failures. Preserve complete
signed-64 parsing of the first 63 bytes, the exact ASCII whitespace policy, and
unconditional evaluation of Load's default. Do not replace source-level storage
with a compiler-time cache. Node.js uses asynchronous file APIs; MASM/COBOL share
their existing feature-gated C file companion. Program identity is embedded from
the source filename, independently of target filenames or temporary run folders.

Data persistence preserves the SMD4 version-1 envelope and SHA-256 checksums using
native cryptographic/file APIs. Checked loads never partially overwrite an array;
strict loads zero it before I/O. Evaluate save Count before Key, and evaluate
output locations only after the operation, Count before Status. Emit only used
operations (save also needs read validation). Java's standard FFM and Python's
standard ctypes expose Windows atomic replacement; Swift uses WinSDK, and native
targets use BCrypt/Win32 directly. Node uses asynchronous built-in APIs and two
atomic renames to publish a verified backup followed by the new primary.

## Print

Values print in order with no inserted separator. Number uses invariant decimal text, Boolean uses `True`/`False`, and Text writes its value. A normal Print ends with one newline; a trailing source semicolon suppresses it; blank Print writes only a newline.

Use familiar output:

- `Console.Write`/`Console.WriteLine` in C#;
- one combined `printf` where practical in C and Objective-C;
- `process.stdout.write` in JavaScript;
- `System.out.print` in Java;
- exact reference-modified `DISPLAY` in COBOL;
- `print(..., terminator:)` in Swift;
- `print(..., end=...)` in Python;
- `std::cout` in C++;
- direct CRT output calls in MASM.

## Control flow

`If` maps to genuine conditionals. `For` maps to a genuine counted/range loop and evaluates source bounds once. `Do` maps to a genuine post-test loop where available; Python's clearest equivalent is `while True` followed by a condition and `break`.

`Exit For` and `Exit Do` target the nearest lexically enclosing loop of that kind, not merely the innermost loop. Use ordinary `break` when those are the same loop. Java and JavaScript may use labeled `break`; C-family and Swift may use a clear target label where required. Python may use a tiny generated exception scoped to the targeted loop because Python has no labeled break. Generate that exception only when such a cross-kind exit exists.

`Select Case` evaluates one selector then uses the destination's native selection when it directly supports the exact types/rules, or a readable first-match conditional chain. `End Program` uses the destination's normal successful termination path and propagates out of calls.

Only-fallback Select emits its body unconditionally after the one selector capture. Empty Select captures once and emits no branch. Native selection is withheld when a typed loop exit inside the selection would bind to the destination's switch break rather than the SMILE loop.

## Native Text lifetime

C and Objective-C programs that concatenate Text emit an immutable allocation registry plus explicit roots for globals, parameters, locals, arrays, selectors, returned values, and expression temporaries. Collection occurs at safe source-statement boundaries; routine cleanup unregisters frame roots on fallthrough and Return; controlled process exit frees all owned allocations. MASM emits equivalent root operations and a feature-gated `SmileTextRuntime.c` companion because expressing the registry in C keeps the learner-facing assembly proportional. `SMILE_TEXT_LIFETIME_REPORT=1` exposes allocation, free, live, and peak counters for tests. Programs without Text `+` receive none of this machinery.

## Target direction

| Target | Required recognizable direction |
|---|---|
| C# | main-first minimal console program, rectangular arrays, Win32 event polling and `Console` screen/color operations, `Thread.Sleep`, monotonic clock |
| C | main-first `int main(void)`, fixed arrays, Win32/CRT console and color primitives, explicit ordered temporaries |
| MASM x64 | main-first ABI-correct `PROC`, flattened checked 2D offsets, direct CRT/Win64 screen/color primitives |
| JavaScript (Node.js) | npm-free `.js`, independent nested arrays, feature-driven async main, Promise Wait, feature-gated native console addon |
| Java | main-first small `Program`, primitive arrays, standard JDK 21 FFM for Win32 console events |
| COBOL | primary-first recursive program units, nested `OCCURS`, exact logical-length Text, and a feature-gated C console companion |
| Objective-C | dependency-light C-compatible console source in `.m` |
| Swift | top-level script statements, nested arrays, WinSDK/CRT console interop only when used |
| Python | direct module-level script, list comprehensions, standard `ctypes` Win32 polling, and no synthetic main |
| C++ | main-first small program, nested `std::array`, standard chrono/thread/random |

## Comments and layout

Preserving source comments is useful when the target has a clear comment marker. Blank lines may be retained for readability. Formatting must be deterministic and must not distort generated syntax merely to reproduce every source column.

## Functional validation

For a changed generator:

1. transpile a focused Core BASIC fixture;
2. assert the native construct and absence of unnecessary machinery;
3. build/run the smallest installed toolchain set directly relevant to the change;
4. run `MissionGuardrail` after changes to canonical statements, expressions, loops, output, or generation policy;
5. use all-target toolchain and pinned parity coverage for broad language milestones.

## Enum generation

For nominal Enum, preserve exact type identity in the bound program and use
native enum declarations wherever practical: C# `enum : long`, C++ `enum class`,
Objective-C fixed-underlying enums, Java/Swift/Python enums. JavaScript uses a
frozen object of BigInt members; MASM uses EQU and COBOL level-78 constants.
C17 uses ordinary enums for int-sized values and int64 typedef/named macros
for wider values. Java and Swift emit aliases of canonical members; native enum
models that cannot represent unnamed zero receive one internal default case.
No enum runtime helper or generic dispatch machinery is needed.

## Completion report

Generator work reports:

- the affected Core BASIC features and active targets;
- a small before/after generated example;
- the native constructs used;
- every helper added and why it was unavoidable;
- MissionGuardrail and focused functional tests actually run;
- known target-native tradeoffs.

## Final decision rule

Prefer the target program a competent teacher would write on a whiteboard for the same behavior, provided it faithfully implements the bound Core BASIC program.

## Double generation

Use the target's native binary64 type, operators, and standard math APIs. Emit
only the used finite/domain/conversion checks needed to preserve the source
contract; do not add integer helpers to Double-only programs or numeric support
for folded constants. Formatting preserves signed zero and numeric round trips.
MASM uses REAL8/SSE with Windows x64 floating calls. COBOL uses FLOAT-LONG storage
and a focused C interoperability boundary where its decimal-based arithmetic,
literal conversion, or comparison would lose binary64 distinctions. These
adapters contain individual native operations, not a numeric opcode interpreter.

## Text-file generation

Use native file streams/handles and typed array storage. The feature-selected
helper owns relative-path normalization, BOM removal, bounded copying, zero-fill,
and recoverable I/O only. Node.js must await native asynchronous file operations;
propagate await through callers without blocking its event loop. Python remains
a direct script, and Swift passes its array with ordinary inout. MASM and COBOL
may use their normal C interoperability for these operating-system services.

## Type record generation

Use nominal native structs in C#, C, Objective-C, C++ and Swift; MASM STRUCT
layouts and Windows x64 aggregate calling conventions; COBOL groups, OCCURS and
group MOVE. Preserve source-order ByVal snapshots and value returns. C++ strings
and Swift arrays keep their ordinary value semantics. C initializers remain
ordinary aggregate literals unless Text-bearing arrays require bounded loops.

Java and JavaScript use small classes; Python uses standard-library dataclasses
and deepcopy. These reference-based destinations need copies for SMILE value
records. C# needs explicit copies only when a record contains an array. Whole
assignment copies into existing fields/arrays so previously captured ByRef
locations remain valid. Emit only the corresponding per-type copy methods.
Java field-addressed ByRef parameters use focused getter/setter adapters, with
forwarding analyzed through the call graph; ordinary scalar ByRef retains its
existing array/index form. Record fields use the existing Text ownership support
in C/Objective-C/MASM; no new record runtime or type registry is permitted.

With captures a location once: C# uses a ref local, C/Objective-C a pointer, C++ a
reference, Java/JavaScript/Python an object alias, MASM a saved address, and COBOL
the original group with captured subscripts. Swift uses the original value
location with captured indexes. Leading-dot expressions then select ordinary
native fields; no With runtime or record snapshot is generated.

Type methods are native instance methods in C#, C++, Java, JavaScript, Python and
Swift. C and Objective-C retain value structs with receiver-pointer functions;
MASM and COBOL pass the receiver address through their existing procedure ABI.
Me becomes this/self or that receiver parameter. Hidden receiver and setter state
do not become authored SMILE parameters. Preserve receiver-first method calls and
value-first property assignment before placing native arguments.

C# and Python use native properties, JavaScript uses get/set, and Swift uses
computed properties with mutating getters. C++/Java and procedural targets use
named getter/setter methods. A write-only Swift property uses a setter method,
and asynchronous JavaScript accessors use async methods because those languages
cannot express the corresponding operation as a native property. When Swift
requires overlapping receiver access, emit a static member with the existing
SmileReference adapter; retain ordinary mutating methods for exclusive receivers.
Apply native privacy where available and enforce all visibility during binding.
No member registry, dynamic dispatch framework or new runtime helper is needed.

Class declarations use native classes and nullable references in C#, Java,
JavaScript, Python and Swift. C++ uses classes and std::shared_ptr. Constructors
use native constructors where possible. A C++ constructor that exposes Me uses a
small factory so shared ownership exists before authored initialization; an async
JavaScript constructor uses an async factory because native constructors cannot
await. Identity uses native reference/pointer comparison.

C/Objective-C use heap structs and receiver-pointer routines; MASM uses heap
payload addresses; COBOL uses POINTER values and LINKAGE field views. These
destinations require generated allocation/root support because their native
pointer storage does not keep borrowed fields alive. NativeClassSupport owns that
support, with ordinary native pointers and explicit roots, no object IDs or
interpreter. It collects unreferenced allocations at statement/frame boundaries,
finalizes owned Text roots, and frees remaining objects on program shutdown.
Class fields cannot contain references, so recursive graph tracing is unnecessary.
No class allocation support is emitted for programs without classes.

Module imports are resolved during binding. Current targets statically combine
the selected sources, using readable module prefixes for ordinary declarations,
native classes/records/enums and routine calls. Privacy and provider identity are
checked before generation; no runtime import loader, dispatch table, module
interpreter or new support helper is emitted. Initializers retain dependency and
source order. Python remains a direct top-level script and Node remains plain
JavaScript with no npm dependency. Get Key alone adds the native console addon. Packages contain source and verified API metadata,
never target binaries. Runtime project assets are separate from GeneratedFile
source text and are copied only after a successful native build.
