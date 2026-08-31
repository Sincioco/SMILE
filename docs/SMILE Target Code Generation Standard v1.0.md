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

Calls use normal destination routines and native call frames. Destinations without a guaranteed left-to-right native argument order capture source arguments in readable temporaries first. Parameters remain independent ByVal copies, including when assigned by the callee.

## Storage

Implicit assignment, `Dim`, constants, parameters, local/global variables, arrays, and loop counters become clear target declarations and assignments. Declarations may be hoisted where the destination requires it, but learner reads and writes stay visible. Constants should use a destination constant when its declaration model permits; otherwise use the smallest faithful immutable representation.

Fixed rank-one and rank-two arrays use native fixed storage where practical. Every dynamic index is checked against its SMILE zero-based dimension before access, even when the target would otherwise allow negative indexing, sparse extension, one-based subscripts, or unchecked memory. Index expressions run left to right exactly once; every assignment index is checked before its right-hand value. Text cells start as empty Text. Routine-local arrays are new/defaulted per call.

Do not generate unused declarations. Preserve case-insensitive SMILE identity while mapping names deterministically away from destination reserved words and collisions.

## Print

Values print in order with no inserted separator. Number uses invariant decimal text, Boolean uses `True`/`False`, and Text writes its value. A normal Print ends with one newline; a trailing source semicolon suppresses it; blank Print writes only a newline.

Use familiar output:

- `Console.Write`/`Console.WriteLine` in C#;
- `printf`, `fputs`, or `putchar` in C and Objective-C;
- `process.stdout.write` in JavaScript;
- `System.out.print` in Java;
- `DISPLAY` in COBOL;
- `print(..., terminator:)` in Swift;
- `print(..., end=...)` in Python;
- `std::cout` in C++;
- direct CRT output calls in MASM.

## Control flow

`If` maps to genuine conditionals. `For` maps to a genuine counted/range loop and evaluates source bounds once. `Do` maps to a genuine post-test loop where available; Python's clearest equivalent is `while True` followed by a condition and `break`.

`Exit For` and `Exit Do` target the nearest lexically enclosing loop of that kind, not merely the innermost loop. Use ordinary `break` when those are the same loop. Java and JavaScript may use labeled `break`; C-family and Swift may use a clear target label where required. Python may use a tiny generated exception scoped to the targeted loop because Python has no labeled break. Generate that exception only when such a cross-kind exit exists.

`Select Case` evaluates one selector then uses the destination's native selection when it directly supports the exact types/rules, or a readable first-match conditional chain. `End Program` uses the destination's normal successful termination path and propagates out of calls.

## Target direction

| Target | Required recognizable direction |
|---|---|
| C# | main-first minimal console program, rectangular arrays, `Console` polling/clear, `Thread.Sleep`, monotonic clock |
| C | main-first `int main(void)`, fixed arrays, Win32/CRT console primitives, explicit ordered temporaries |
| MASM x64 | main-first ABI-correct `PROC`, flattened checked 2D offsets, direct CRT/Win64 primitives |
| JavaScript (Node.js) | dependency-free `.js`, independent nested arrays, feature-driven async main, Promise Wait, raw queue/finally cleanup |
| Java | main-first small `Program`, primitive arrays, standard JDK 21 FFM for Windows CRT key polling |
| COBOL | primary-first recursive program units, nested `OCCURS`, and a feature-gated C console companion |
| Objective-C | dependency-light C-compatible console source in `.m` |
| Swift | top-level script statements, nested arrays, WinSDK/CRT console interop only when used |
| Python | direct module-level script, list comprehensions, `msvcrt`, and no synthetic main |
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
