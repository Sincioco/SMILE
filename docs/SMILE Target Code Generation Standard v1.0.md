# SMILE Target Code Generation Standard v1.0

## Status And Authority

This is the current public standard for learner-facing generated target source.

It implements [SMILE Core Principles](SMILE%20Core%20Principles.md). Direct current instructions from Sin and root `AGENTS.md` remain higher authority; current official language specifications define SMILE syntax and semantics.

Historical generation briefs and tests cannot require runtime-heavy output when their underlying requirement has been superseded.

## Governing Rule

SMILE is a beginner-first educational programming language. Generated target source is part of the teaching experience and **MUST** use the normal, idiomatic, beginner-readable way a programmer would ordinarily express the equivalent program in that destination language whenever practical.

For SMILE, generated-source readability is part of correctness.

A program can compile, run, and match expected output yet still be incorrect for SMILE if it unnecessarily hides the destination language behind compiler internals.

## Priority Order

1. Preserve current approved SMILE language meaning for ordinary programs.
2. Teach the destination language through recognizable native constructs.
3. Preserve source expression intent and genuine control-flow structure.
4. Keep output simple, proportional, deterministic, readable, and dependency-light.
5. Use target-local lowering only when the destination lacks a reasonable native equivalent.
6. Accept documented target-native edge behavior instead of generating a cross-language runtime solely for obscure parity.

Exact cross-runtime byte behavior is not automatically more correct than clear native target code.

## Current Active Targets

Routine generation work, product exposure, tests, and toolchain support currently focus on:

1. C#
2. C
3. Windows x64 MASM Assembly

JavaScript, Java, COBOL, Objective-C, Swift, Python, and C++ generators remain retained but paused. Their historical output is not the current style standard, and new language work does not update them by default.

The central `ActiveTargetLanguages` source of truth drives normal Desktop choices, CLI enumeration, Transpile All, toolchain detection, and routine target tests.

## Native Constructs Before Helpers

For every feature:

1. identify the ordinary beginner-level construct in the destination;
2. use it directly when it clearly expresses the SMILE concept;
3. add only the minimum surrounding code that destination normally requires;
4. avoid a compiler-owned runtime API when the target already provides the concept;
5. document a meaningful native limitation rather than silently building a general simulator.

A helper is acceptable only when:

- a current approved SMILE rule genuinely requires it;
- the destination lacks a reasonable direct construct;
- it is small and target-local;
- it does not hide the concept being taught;
- it is clearer than the direct alternative.

Do not replace one obsolete abstraction layer with a new abstraction framework.

## Canonical Style Fixture

The current strategic style fixture is:

```smile
LET age = 0
PRINT How old are you?
INPUT age
PRINT $"You are {age} years old."
```

It establishes the approved direction:

- C# uses ordinary console APIs and conventional parsing;
- C uses ordinary `printf`, `scanf`, or `fgets` as appropriate;
- MASM uses recognizable CRT/Win64 calls and clear data/control flow;
- the generated source stays proportional to four SMILE statements.

The supplied target examples use a same-line prompt for style illustration, but current SMILE `PRINT` appends a newline. Generators must preserve that language meaning. Do not invent a context-sensitive rule where PRINT suppresses its newline before INPUT. A no-newline form requires a separate official language feature.

## Shared Bound Representation

Every target consumes the shared bound tree. A generator must not:

- reparse SMILE source;
- reinterpret Block String delimiters;
- invent target-specific expression semantics;
- use source-text replacement for identifiers;
- execute or unroll WHILE during generation;
- select and delete IF branches based on current values;
- propagate an old LET/SET value past INPUT;
- replace a current runtime storage read with an unrelated compiler-time literal.

Shared analysis remains statement-order, mutation, branch, and loop aware. Target-local lowering uses only facts valid at that source position.

## Curly Braces And Expression Intent

Curly braces `{ }` are interpolation holes in text-oriented syntax. They are not general variable-reference delimiters.

```smile
INPUT Name
SET Name = "Sin"
IF Name = "Sin" THEN
    PRINT Hello {Name}!
END IF
```

Preserve intent:

- bare PRINT text remains literal template text;
- `{expression}` in raw PRINT remains interpolation;
- `$"..."` remains explicit interpolation;
- explicit `+` String concatenation remains concatenation when natural;
- ordinary quoted Strings do not interpolate;
- target-native interpolation is preferred where available.

Lower-level targets may represent interpolation with a conventional formatted-output call when that is clearer.

## PRINT

Each SMILE PRINT writes its value followed by the current specified newline.

Use ordinary destination output. For example:

- C#: `Console.WriteLine` for normal PRINT;
- C: a compiler-owned safe `printf` format string and normal arguments;
- MASM: a direct understandable CRT/Win64 output call.

Never use learner data as a C format string. Escape compiler-generated literal percent signs correctly.

Do not add a general output runtime merely to normalize host details that are not current SMILE requirements.

## INPUT

INPUT operates on one existing fixed-type variable and leaves its value runtime-unknown to static propagation.

Current generated-source direction:

- C#: `Console.ReadLine()` plus conventional target-native conversion;
- C: `scanf` for simple numeric input and `fgets` for line-oriented String input when appropriate;
- MASM: direct recognizable `scanf`-style CRT/Win64 input with clear storage.

The following are not universal generated-target requirements:

- a shared strict UTF-8 byte reader;
- a 4096-byte line limit;
- embedded-NUL console-input preservation;
- identical CR/LF/final-EOF parsing;
- identical parsing internals;
- identical stderr text, runtime codes, or exit values for obscure invalid input;
- byte-for-byte evaluator parity across unrelated runtimes.

For ordinary valid input, targets preserve the conceptual behavior: read a value according to the existing variable type, update current storage, and let later statements read that value.

Invalid input should use a sensible normal destination mechanism or the smallest clear check. Do not emit a generic error-dispatch runtime solely to imitate the evaluator.

## LET, SET, And Runtime Storage

LET declares and initializes a variable. SET updates an existing variable from a SMILE expression. INPUT updates an existing variable from runtime input. SET and INPUT do not change the type established by LET.

Prefer ordinary native declarations and assignments.

A direct self-assignment remains a visible assignment because it is a valid SMILE SET. A destination-specific identity expression is acceptable only when the destination rejects or warns on plain self-assignment and the alternative is the smallest type-preserving form.

Target optimization may combine a declaration with a later first assignment only when ordinary program behavior is preserved and the result is materially clearer. This is lowering, not a hidden change to SMILE syntax.

## Strings And Block Strings

The front end scans and normalizes LET/SET Block Strings into ordinary bound String values. Generators never inspect source delimiters or indentation margins.

Use the clearest native String or multiline representation practical for the actual value. Preserve source interpolation versus concatenation intent.

When a native representation has an obscure limitation, choose the simplest documented target tradeoff unless a current official language rule explicitly justifies additional machinery. Exact-length buffers, pointer/length pairs, byte arrays, and generalized String runtimes are not the default merely because an old all-target test used them.

Source comments and blank lines inside a Block String remain String data and are not emitted separately as layout.

## Integers And Arithmetic

The current core-expression specification remains the authority for SMILE Integer expressions. Generated storage should use the ordinary idiomatic target type appropriate to the program and approved active-target behavior.

Do not force every small INPUT program into a wide type plus generated overflow helpers solely because every possible host input was once modeled as a full signed-64 value.

Source-known invalid arithmetic remains a compiler concern under the core specification. Runtime-dependent target behavior should use normal destination constructs and only the smallest checks that current approved semantics require.

## IF And WHILE

Every active generator preserves genuine source control flow.

- IF maps to the destination's normal conditional structure.
- ELSE IF preserves clause order.
- WHILE maps to genuine pre-test loop control flow.
- Conditions re-read current runtime storage.
- Unselected branches and zero-iteration bodies do not execute INPUT or runtime operations.
- Generators do not delete a source branch or loop merely because an incoming value is currently known.

C# and C use ordinary `if`/`else if`/`else` and `while`. MASM uses clear deterministic comparison, branch, loop, back-edge, and exit labels.

## Source Comments And Blank Lines

Comments and blank physical source lines are ordered non-semantic bound items. Emit them once in the corresponding learner-code region using native syntax:

| Active destination | Generated comment marker |
|---|---|
| C# | `//` |
| C | `//` |
| Windows x64 MASM | `;` |

Payload rendering must remain target-safe and deterministic, but should not grow into a target-neutral runtime concern. A target-required no-op for an otherwise empty semantic body is allowed.

Preserved comments become generated source, so never place passwords, private keys, tokens, or other secrets in SMILE comments.

## Identifier Spelling

Use the shared symbol-based target identifier map.

Preserve the learner's spelling when safe. Map it only when it conflicts with:

- destination keywords or contextual/restricted words;
- destination identifier syntax or reserved patterns;
- generator-owned entry points, APIs, labels, or helper names;
- active headers/macros or library facilities;
- another mapped target name.

Every reference to one SMILE symbol must use the same mapped target spelling. Never implement mapping through source-text replacement.

Paused-target identifier data remains retained for future catch-up work.

## Active Target Standards

### C#

Generated C# should resemble a beginner C# tutorial:

- ordinary `using` directives only when needed;
- a small conventional program entry point;
- normal local variables;
- `Console.WriteLine`, `Console.ReadLine`, and conventional conversion;
- native interpolation;
- normal `if` and `while`;
- no generated `SmileRuntime` when the platform already has the concept.

Reject regressions where an ordinary INPUT program emits raw standard-input streams, a UTF-8 state machine, a common byte limit, or a large runtime-error dispatcher.

### C

Generated C should resemble a beginner C tutorial:

- only required standard headers;
- a normal `int main(void)` shape;
- ordinary variables and arrays/strings;
- safe `printf` calls;
- `scanf` or `fgets` according to the input concept;
- normal `if` and `while`;
- a direct return value;
- no generalized generated runtime for a simple program.

When C has a real native limitation, document the simplest tradeoff instead of automatically simulating a richer universal runtime.

### Windows x64 MASM

Generated MASM should be understandable assembly, not merely correct machine-oriented output:

- clear `.data` values and source-variable storage;
- straightforward `main PROC`;
- correct Windows x64 calling convention, shadow space, and alignment;
- direct recognizable CRT/Win64 calls such as `printf`, `scanf`, and `ExitProcess` where suitable;
- clear labels for IF and WHILE;
- comments explaining important assembly concepts without annotating every obvious instruction;
- no generic byte reader, UTF-8 validator, physical-line parser, or unrelated runtime procedures for a tiny program.

Assembly will naturally be longer than C# or C. Judge proportionality, native APIs, and clarity rather than enforcing an arbitrary line-count ceiling.

## Paused And Future Targets

Paused backend source remains in the repository but is not current generated-output authority.

Before re-enablement, a target must document:

1. the normal beginner-level form for each supported SMILE concept;
2. native APIs and constructs used;
3. unavoidable helpers and why they are necessary;
4. how output avoids obsolete cross-runtime assumptions;
5. readability/golden tests equivalent to active-target guardrails;
6. focused compiler/runtime/toolchain validation.

A target is not ready merely because its historical generator still compiles or matches stdout.

Do not add another destination language unless Sin explicitly reopens target expansion.

## Readability Guardrails

Fast `MissionGuardrail` tests inspect learner-facing generated text without requiring every target toolchain.

At minimum, active-target golden or structural coverage protects:

- LET plus PRINT;
- INPUT;
- interpolation;
- SET;
- IF;
- WHILE;
- Block Strings.

Tests should positively require native constructs and reject unnecessary machinery. For example, a simple C# INPUT test should reject byte readers and stream state machines; C should reject custom UTF-8/input dispatch for simple numeric input; MASM should require the approved direct-call direction and reject a generic input subsystem.

Use exact expected source for small stable fixtures when useful. Otherwise use strong structural expectations and forbidden helper patterns. Do not substitute an arbitrary maximum line count for human readability review.

## Functional Validation

Readability tests complement, not replace, focused behavior tests.

For ordinary valid programs, compare intended evaluator behavior and active-target execution where the toolchain is available. Do not make malformed bytes, NUL console input, a shared byte boundary, or identical host error messages part of normal cross-target conformance.

Velocity Mode uses:

- focused parser/binder/evaluator tests for changed semantics;
- active-target source tests for changed generation;
- one appropriate active-target build/run smoke when practical;
- MissionGuardrail after generator or output-policy changes.

Broader active-target Debug/Release validation belongs to milestones. Paused-target suites run only for explicit maintenance or re-enablement.

The hosted `SMILE CI` workflow remains manually dispatchable. It is not automatically triggered during Velocity Mode and does not impose an exact-SHA gate on normal pushes.

## Generator Change Checklist

Before completing generator work:

- [ ] Does the output use the normal native construct?
- [ ] Is it understandable to a beginner?
- [ ] Did I add a helper that could have been avoided?
- [ ] Did I preserve destination-language idioms?
- [ ] Did I expose compiler internals?
- [ ] Did I preserve interpolation and concatenation intent?
- [ ] Did I preserve the curly-brace rule?
- [ ] Did I preserve genuine IF/WHILE structure and current storage reads?
- [ ] Did I run MissionGuardrail?
- [ ] Did I run focused functional tests?
- [ ] Did I inspect and report a before/after generated example?

## Completion Report

For a generated-output change, report:

```text
Changed SMILE feature:
Affected active targets:
Native constructs used:
Custom runtime/helper added?:
If yes, why unavoidable:
MissionGuardrail tests run:
Focused functional tests run:
Before/after generated example:
Known target-native tradeoffs:
```

## Final Decision Rule

When choosing between preserving compiler machinery and preserving SMILE's educational clarity, first determine whether the machinery is still required by current approved language design. If it is not, remove or simplify it. If it is required, implement it in the most idiomatic and least intrusive target-local way practical.
