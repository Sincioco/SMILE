# SMILE Strategic Reset — Master Instructions for Codex

## Status

This document is the umbrella instruction set for a deliberate SMILE course correction.

Repository:

```text
Sincioco/SMILE
```

Reviewed baseline when this document was prepared:

```text
999c4d4ed45608e75303245d88f3b8466850b618
Sin and Codex: Generalize Block Strings for LET and all targets
```

Always begin from the newest `main`. Do not assume the reviewed SHA is still current.

Read and implement these companion documents together:

1. `01 - SMILE Beginner-First Idiomatic Transpilation and Permanent Guardrails.md`
2. `02 - SMILE Temporary Three-Target Focus - CSharp C MASM.md`
3. `03 - SMILE Velocity Mode - Focused Testing and CI Pause.md`

---

# 1. Why this reset exists

SMILE has drifted away from its primary educational purpose.

Recent work has optimized heavily for:

- byte-for-byte runtime equivalence across ten targets;
- exact low-level I/O behavior;
- broad edge-case handling;
- large generated runtime helpers;
- exhaustive cross-target validation;
- warning and toolchain matrices;
- simultaneous maintenance of ten backends.

Those goals produced rigorous compiler behavior, but they also caused tiny beginner programs to transpile into target source that is much more complicated than code a beginner would normally write in the destination language.

That is not the desired direction.

SMILE must once again optimize first for:

> **Simple, recognizable, idiomatic code that teaches a beginner how the same programming idea is normally expressed in another language.**

---

# 2. Permanent versus temporary decisions

## Permanent

The following is permanent and applies to every target language SMILE supports now or in the future:

> **SMILE should transpile to the normal, idiomatic way a beginner would write the equivalent program in the target language. SMILE should not impose low-level cross-language runtime behavior that forces otherwise-simple target code to use compiler-generated runtime libraries.**

Also permanent:

> **Generated target source is part of the SMILE teaching experience. Human readability, recognizable native constructs, and beginner comprehension are first-class correctness criteria.**

Also permanent:

> **Curly braces are interpolation holes in text-oriented syntax. They are not general variable-reference delimiters.**

## Temporary

Until Sin explicitly changes the policy:

- active target development/transpilation is limited to C#, C, and MASM x64 / Assembly;
- the other seven existing backends are paused but retained in the repository;
- automatic CI on commit/push is paused;
- routine development uses focused tests;
- full-suite/all-target validation is reserved for major milestones or explicit requests.

---

# 3. Current authoritative style fixture

The current authoritative SMILE fixture supplied for this reset is:

```smile
LET age = 0
PRINT How old are you?
INPUT age
PRINT $"You are {age} years old."
```

The supplied C#, C, and Assembly files define the desired **style, APIs, readability, and complexity level**.

They show the intended direction:

## C#

Prefer ordinary C# such as:

```csharp
Console.Write(...)
Console.ReadLine()
int.Parse(...)
Console.WriteLine(...)
```

not a generated custom SMILE input runtime.

## C

Prefer ordinary C such as:

```c
printf(...)
scanf(...)
```

for simple Integer console input/output, not a generated UTF-8 state machine and custom parser.

## MASM x64

Prefer recognizable CRT/Win64 assembly using facilities such as:

```text
printf
scanf
ExitProcess
```

for a tiny console program, not hundreds of lines of generic generated runtime code.

---

# 4. Important PRINT newline note

Current SMILE `PRINT` semantics append a newline, while the supplied target prompt examples use same-line prompt output.

Do **not** silently invent a context-sensitive rule such as:

```text
PRINT immediately before INPUT suppresses newline
```

That would make the language surprising.

For this reset:

- treat the supplied targets as authoritative for style and complexity;
- preserve existing PRINT meaning unless Sin separately approves a no-newline PRINT feature;
- do not block the broader simplification work on this formatting mismatch.

If a future no-newline form is added, specify it explicitly as a SMILE language feature.

---

# 5. High-level implementation goals

Codex must:

1. Re-center target generation around native, beginner-readable destination code.
2. Remove or simplify requirements that exist mainly to force low-level equivalence across unrelated runtimes.
3. Prefer target-native input/output, parsing, strings, control flow, and interpolation.
4. Keep SMILE syntax/semantics clear and explicit.
5. Focus active engineering on C#, C, and MASM x64.
6. Pause the other seven backends without deleting them.
7. Stop automatic CI on every push.
8. Replace routine exhaustive test runs with focused development tests.
9. Keep a clearly documented path to re-enable paused targets and full CI later.
10. Add permanent guardrails so a future Codex does not reintroduce runtime-heavy target output.

---

# 6. KISS reset

KISS and "The Sin Way" remain governing principles.

When a feature can be represented as either:

```text
A. a normal native target-language statement
```

or:

```text
B. a custom generated runtime abstraction covering obscure edge cases
```

prefer A unless B is required by an explicitly approved, educationally meaningful SMILE behavior.

Do not preserve complexity merely because it already exists.

Do not add new abstraction layers to remove old abstraction layers.

Delete or simplify obsolete compiler/runtime machinery when it no longer serves the revised mission and can be removed safely.

---

# 7. Required documentation changes

Update at minimum:

```text
AGENTS.md
README.md
docs/Architecture.md
docs/Roadmap.md
docs/Toolchains.md
docs/SMILE Target Code Generation Standard v1.0.md
```

Update official language specifications when this reset changes a normative rule, especially INPUT/runtime portability requirements and the curly-brace rule.

Keep documentation synchronized with actual code.

---

# 8. Do not commit or push unless instructed

Continue to follow repository ownership rules:

- work directly on `main` unless Sin says otherwise;
- never force-push;
- never discard unrelated user work;
- do not commit/push unless Sin explicitly asks;
- use `Sin and Codex:` for commit subjects when Codex is asked to commit.

The old mandatory post-push CI-success gate must be updated as part of Velocity Mode because automatic CI is being paused.

---

# 9. Definition of success

The reset is successful when a beginner can compare SMILE, C#, C, and Assembly and immediately recognize the same programming concepts without having to understand SMILE compiler internals.

A tiny SMILE program must produce tiny, normal-looking target programs.

Future language support must inherit the same rule.
