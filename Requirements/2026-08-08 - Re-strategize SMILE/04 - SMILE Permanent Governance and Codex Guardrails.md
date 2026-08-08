# 04 - SMILE Permanent Governance and Codex Guardrails

## Purpose

This document defines how SMILE protects its long-term mission and prevents future Codex work from drifting back toward unnecessary complexity.

This is not a temporary development preference.

This is a **permanent governance policy** for the SMILE project.

It applies to:

- C#;
- C;
- MASM x64 / Assembly;
- every currently paused target if re-enabled;
- every future target language;
- compiler architecture;
- language specifications;
- generators;
- runtime behavior;
- tests;
- documentation;
- Desktop behavior;
- CLI behavior;
- build/toolchain work.

The central problem this document solves is simple:

> A strategy written only in a dated requirement file can eventually be forgotten, contradicted, or bypassed.

SMILE therefore needs multiple reinforcing layers:

```text
Repository instructions
        ↓
Canonical principles
        ↓
Architecture
        ↓
Fast automated guardrails
        ↓
Human review
```

No single layer is sufficient by itself.

---

# 1. Governing Principle

Make the following a permanent SMILE governance rule:

> **SMILE is a beginner-first educational programming language. Generated target code is part of the teaching experience and MUST use the normal, idiomatic, beginner-readable way a programmer would ordinarily write the equivalent program in the destination language whenever practical.**

Also permanent:

> **SMILE MUST NOT impose low-level cross-language runtime behavior that forces otherwise-simple target code to use compiler-generated runtime libraries unless that complexity is required by an explicitly approved and educationally meaningful SMILE language rule.**

Also permanent:

> **When a target language already provides a normal native construct for a SMILE concept, use that native construct unless there is a strong documented reason not to.**

Examples:

```text
SMILE concept     Preferred target concept

PRINT             native target output
INPUT             native target input
IF                native conditional
WHILE             native loop
String            normal target String representation
Integer           normal target Integer representation
Interpolation     native interpolation when available
```

---

# 2. Permanent Variable Reference Rule

Make the following a permanent official SMILE rule:

> **Curly braces `{ }` identify interpolation holes in text-oriented syntax such as raw PRINT templates. They are not general variable-reference delimiters.**

When a statement or expression position already requires an identifier or expression, variables are written directly without braces.

Examples:

```smile
LET Name = ""
INPUT Name
SET Name = "Sin"

IF Name = "Sin" THEN
    PRINT Hello {Name}!
END IF
```

Do not introduce or accept general forms such as:

```smile
INPUT {Name}
SET {Name} = "Sin"
IF {Name} = "Sin" THEN
```

unless Sin explicitly changes the language design in the future.

---

# 3. Root AGENTS.md Is the First Enforcement Layer

The root repository:

```text
AGENTS.md
```

must contain a short, prominent, non-ambiguous permanent mission section near the beginning.

Do not bury the core principles hundreds of lines into the file.

Use wording equivalent to:

```markdown
## NON-NEGOTIABLE SMILE MISSION

SMILE is a beginner-first educational programming language.

Generated target code MUST use the normal, idiomatic,
beginner-readable way a programmer would ordinarily write
the equivalent program in that destination language.

Do not introduce compiler-generated runtime machinery when
the destination language already provides a normal native
construct for the feature.

This rule applies to C#, C, MASM x64, every re-enabled target,
and every future target language.

Curly braces { } are interpolation holes in text-oriented syntax.
They are not general variable-reference delimiters.

Before modifying the compiler, generators, language specifications,
runtime behavior, or target tests, read:

docs/SMILE Core Principles.md
```

The exact formatting may differ, but the meaning must not.

---

# 4. Keep AGENTS.md Short Enough to Remain Effective

The current repository has accumulated many detailed rules in `AGENTS.md`.

During this strategic reset, refactor it so that:

```text
AGENTS.md
```

contains:

- permanent project mission;
- permanent beginner-first transpilation rule;
- permanent Variable Reference Rule;
- current active-target policy;
- current Velocity Mode policy;
- source-control safety rules;
- links to canonical deeper documentation.

Move long explanatory material to canonical docs where appropriate.

Do not turn `AGENTS.md` into a duplicate of every historical specification.

The goal is for a future Codex session to understand the governing rules immediately.

---

# 5. Create One Canonical Core Principles Document

Create:

```text
docs/SMILE Core Principles.md
```

This becomes the highest-authority project strategy document beneath explicit direct instructions from Sin.

It must contain the permanent principles in concise form.

At minimum:

```text
1. Beginner First
2. Native and Idiomatic Target Code
3. Simplicity Over Cross-Runtime Perfection
4. Target Code Is Educational Output
5. Native Constructs Before Custom Runtime Helpers
6. Curly Braces Mean Interpolation
7. KISS / The Sin Way
8. Current Active Targets
9. Paused Targets May Return
10. Future Targets Inherit These Principles
11. Tests Must Protect Readability
12. Historical Requirements May Be Superseded
```

Add a statement equivalent to:

> When older requirements, implementation notes, or historical specifications conflict with this document because SMILE strategy has changed, the current Core Principles and current official specifications govern.

Do not let a dated implementation brief override the current project mission merely because it contains more technical detail.

---

# 6. Authority Order

Document an explicit authority order.

Use this order unless Sin later changes it:

```text
1. Direct current instruction from Sin
2. Root AGENTS.md
3. docs/SMILE Core Principles.md
4. Current official SMILE language specifications
5. Current architecture / generation / toolchain standards
6. Current milestone implementation instructions
7. Historical Requirements files
8. Historical commit messages and old implementation notes
```

If two sources conflict, the higher item wins.

Future Codex work must not silently average contradictory requirements together.

If a meaningful conflict remains unresolved, report it before implementation when practical.

---

# 7. Historical Requirement Files Must Be Clearly Classified

SMILE contains many dated files under:

```text
Requirements/
```

Some describe earlier strategies that may no longer be active.

Do not delete useful history merely because strategy changed.

Instead classify old documents.

For any requirement that conflicts with the new Core Principles, add a clear banner equivalent to:

```markdown
> [!IMPORTANT]
> HISTORICAL / SUPERSEDED
>
> This document records an earlier SMILE design.
>
> Where it conflicts with:
>
> `docs/SMILE Core Principles.md`
>
> or current official language specifications, the newer documents are authoritative.
```

Do not rewrite the historical body unless needed to prevent factual confusion.

Preserve history while preventing future Codex sessions from treating old strategy as current law.

---

# 8. Create a Superseded-Requirements Index

Create or update a simple document such as:

```text
docs/Historical Requirements Index.md
```

or another clear existing location.

List important historical requirement files and whether they are:

```text
ACTIVE
PARTIALLY SUPERSEDED
SUPERSEDED
HISTORICAL ONLY
```

For partially superseded files, state which current document governs the changed portion.

Do not create a complex documentation database.

A simple Markdown table is sufficient.

---

# 9. Architecture Must Reinforce the Mission

Documentation is not enough.

The implementation should make the correct behavior the easiest behavior.

When generator architecture offers a choice between:

```text
simple native target construct
```

and:

```text
generalized compiler/runtime subsystem
```

the native path should be the default when it satisfies the approved SMILE semantics.

Examples:

## C#

Prefer direct generator logic producing:

```csharp
Console.WriteLine(...)
Console.ReadLine()
int.Parse(...)
```

rather than routing ordinary features through a generic runtime framework.

## C

Prefer:

```c
printf(...)
scanf(...)
fgets(...)
```

where appropriate.

## MASM

Prefer straightforward CRT / Win64 calls for simple programs.

A future Codex should not encounter an architecture where the easiest existing helper automatically emits hundreds of lines of runtime code for a four-line SMILE program.

---

# 10. Remove Obsolete Complexity When Safe

During the strategic reset, audit runtime and generator machinery that exists primarily because of superseded requirements.

For each major subsystem classify it as:

```text
KEEP
SIMPLIFY
REPLACE WITH NATIVE TARGET CONSTRUCT
REMOVE
```

Examples to review:

- exact UTF-8 redirected-input state machines;
- exact cross-target input-byte limits;
- embedded-NUL console-input machinery;
- cross-target runtime error dispatch;
- custom Integer parsing used where native parsing is now preferred;
- generated helper-name infrastructure used only by removed helpers;
- all-target runtime-support planning;
- target-wide conformance scaffolding no longer needed during the three-target phase.

Do not remove code blindly.

Remove it only after:

- the governing requirement has been changed or superseded;
- active target behavior remains correct;
- focused tests cover the replacement.

---

# 11. Mission Guardrail Tests Are Mandatory

Create a small, fast test group whose purpose is not merely execution correctness.

Its purpose is to enforce the SMILE mission.

Suggested class name:

```text
SMILEMissionGuardrailTests.cs
```

or:

```text
TargetReadabilityGuardrailTests.cs
```

Add a test category such as:

```text
MissionGuardrail
```

These tests should run quickly and should not require all target toolchains merely to inspect generated text.

---

# 12. Golden Output Tests

For canonical small programs, compare the generated learner-facing source against approved expected output or strong structural expectations.

At minimum protect:

```text
LET + PRINT
INPUT
Interpolation
SET
IF
WHILE
Block String
```

for the currently active targets:

```text
C#
C
MASM x64
```

Future active languages must add equivalent readability tests before being considered fully supported.

---

# 13. Canonical INPUT Guardrail

Use a canonical Integer-input fixture equivalent to:

```smile
LET age = 0
PRINT How old are you?
INPUT age
PRINT $"You are {age} years old."
```

Important:

Current `PRINT` newline semantics may differ from the same-line prompt style of the supplied target examples.

Do not invent hidden context-sensitive PRINT behavior.

The fixture is authoritative for:

- variable type;
- INPUT behavior;
- interpolation;
- target-language style;
- complexity expectations.

A future explicit no-newline PRINT feature may separately make prompt behavior exactly identical.

---

# 14. C# Mission Guardrail

A tiny C# program must look like ordinary beginner C#.

Tests should favor recognizable constructs such as:

```csharp
Console.Write(...)
Console.WriteLine(...)
Console.ReadLine()
int.Parse(...)
```

where appropriate.

For simple native INPUT, tests should reject a return to unnecessary machinery such as:

```text
_smile_read_byte
_smile_read_line
_smile_input_stream
UTF8Encoding
System.IO.Stream
SmileRuntime.ReadString
large SMILER dispatch blocks
```

unless the specific test program actually uses an explicitly approved SMILE feature requiring such machinery.

---

# 15. C Mission Guardrail

A tiny C program should resemble normal beginner C.

Tests should favor:

```c
#include <stdio.h>

int main(void)
{
    int age;

    printf(...);
    scanf(...);

    printf(...);

    return 0;
}
```

where semantically appropriate.

For simple Integer INPUT, reject unnecessary generated implementations such as:

```text
_smile_valid_utf8
_smile_read_line
_smile_input_error
custom byte-at-a-time state machines
generic String-length bookkeeping
```

unless required by an explicitly approved feature.

---

# 16. MASM Mission Guardrail

Assembly will naturally be longer than C# or C.

The guardrail is proportionality and clarity, not arbitrary line count.

For a tiny console program, prefer recognizable:

```text
printf
scanf
ExitProcess
```

or similarly normal target facilities.

Reject regressions where a basic INPUT example again embeds:

```text
full UTF-8 validator
generic byte reader
generic physical-line parser
large input error dispatcher
unrelated formatting/runtime procedures
```

inside the learner-facing assembly.

Comments should explain important assembly concepts, not mechanically annotate every obvious instruction.

---

# 17. No Arbitrary Maximum Line Count

Do not enforce readability primarily through:

```text
generated program must be under N lines
```

Different languages naturally require different amounts of syntax.

Use:

- approved golden output;
- structural expectations;
- forbidden unnecessary helper patterns;
- human-reviewable complexity;
- native API expectations.

A 40-line assembly program may be clearer than a 15-line program using opaque macros.

---

# 18. Future Language Admission Gate

No paused or future target may become active until it passes the permanent mission gate.

For each target, Codex must document:

```text
1. What is the normal beginner-level way to express each supported SMILE feature?
2. Which native APIs are used?
3. Which custom helpers are unavoidable?
4. Why are those helpers necessary?
5. Does the generated source teach the destination language?
6. Are readability/golden tests present?
7. Does the target avoid importing obsolete cross-target runtime assumptions?
```

A target is not ready merely because it compiles and produces matching stdout.

---

# 19. Readability Is a Form of Correctness

Add this explicit statement to project standards:

> **For SMILE, generated-source readability is part of correctness.**

A generated program can:

```text
compile correctly
run correctly
match expected output
```

and still be considered incorrect for SMILE if it unnecessarily obscures the destination language behind compiler implementation details.

This is a permanent design distinction.

---

# 20. Require a Human-Readable Diff Review

When a task changes generator output, Codex must include in its completion report a small before/after example for at least one canonical affected program.

For active target changes, include the relevant C#/C/MASM output when practical.

This creates a human checkpoint:

> "Does this still look like the language we are trying to teach?"

Do not rely exclusively on test counts.

---

# 21. Generator Change Checklist

Before completing any task that modifies code generation, Codex must check:

```text
[ ] Does the output use the normal native construct?
[ ] Is the result understandable to a beginner?
[ ] Did I add a helper that could have been avoided?
[ ] Did I preserve destination-language idioms?
[ ] Did I accidentally expose compiler internals?
[ ] Did I preserve interpolation intent?
[ ] Did I preserve the curly-brace rule?
[ ] Did I preserve IF/WHILE structure?
[ ] Did I run the MissionGuardrail tests?
[ ] Did I show a before/after output example?
```

Put this checklist in:

```text
docs/SMILE Core Principles.md
```

or another canonical current standards document.

---

# 22. Mandatory Fast Test Command

Once the MissionGuardrail category exists, document a copyable command in `AGENTS.md`.

Use verified current MSTest syntax.

Conceptually:

```text
dotnet test <SMILE test project> --filter TestCategory=MissionGuardrail
```

Do not hardcode an incorrect command before verifying the actual project path and test-framework filter behavior.

Codex must run this guardrail after changing:

- any active target generator;
- INPUT;
- PRINT;
- LET;
- SET;
- IF;
- WHILE;
- interpolation;
- target expression rendering;
- target runtime-generation policy.

This test belongs in normal Velocity Mode because it should be fast.

---

# 23. Velocity Mode Does Not Disable Mission Guardrails

The project is intentionally reducing unnecessary full-suite validation.

That does not mean the mission tests should be skipped.

MissionGuardrail tests should be:

```text
small
fast
local
deterministic
toolchain-light whenever possible
```

They should become one of the cheapest and most frequently run test groups in SMILE.

Full CI may be paused.

The mission guardrail is not.

---

# 24. Major Milestone Validation

At major milestones, broader functional testing still applies.

However, a major milestone is not complete if:

```text
all tests pass
```

but the generated code has drifted back toward unreadable runtime-heavy output.

Major milestone review must include:

```text
functional correctness
+
MissionGuardrail tests
+
manual generated-source review
```

---

# 25. Codex Must Not Preserve Complexity Solely Because It Exists

Add this permanent rule:

> **Existing implementation complexity is not itself a requirement.**

If Codex finds:

```text
a complex helper
a large runtime subsystem
an elaborate conformance test
an abstraction created for an old strategy
```

it must determine whether the current Core Principles still require it.

Do not say:

> "I preserved this because tests depend on it."

when the tests themselves protect a superseded requirement.

Correct order:

```text
current mission
    ↓
current specification
    ↓
implementation
    ↓
tests
```

not:

```text
old test
    ↓
old implementation
    ↓
permanent accidental language rule
```

---

# 26. Tests Are Guardrails, Not the Product

Add this statement:

> **SMILE's purpose is not to maximize conformance-test sophistication. Tests exist to protect the language and teaching experience.**

When a test conflicts with an intentionally changed official requirement:

- update the test;
- do not preserve obsolete behavior merely to keep the test green.

Never delete a failing test without understanding why it fails.

---

# 27. No Hidden Context-Sensitive Transpiler Tricks

To keep generated code simple, do not make SMILE semantics surprising.

For example, do not invent:

```text
PRINT behaves differently when the next statement is INPUT
```

or:

```text
LET is omitted whenever a later INPUT exists
```

without explicit semantic approval.

Target-specific lowering may combine statements only when it is safe and clear, but the SMILE language itself must remain deterministic and understandable.

If a desired target idiom requires a new SMILE feature, propose/specify that feature explicitly.

---

# 28. Current Three-Target Focus Is Temporary

Governance must distinguish:

```text
permanent mission
```

from:

```text
temporary active targets
```

Permanent:

```text
Beginner-first idiomatic native generation
Variable Reference Rule
KISS
Readability guardrails
```

Temporary:

```text
C#
C
MASM x64
```

When another target is re-enabled, the permanent rules automatically apply.

No future strategy update is needed to extend the mission to that language.

---

# 29. Current Velocity Mode Is Temporary

Likewise:

Permanent:

```text
focused appropriate testing
fast MissionGuardrail checks
honest reporting of validation
```

Temporary:

```text
automatic CI paused
post-push exact-SHA CI gate suspended
full suite reserved for milestones
```

When CI is restored later, the permanent mission guardrails remain.

---

# 30. Documentation Change Checklist

When implementing this governance reset, update:

```text
AGENTS.md
docs/SMILE Core Principles.md
docs/Architecture.md
docs/SMILE Target Code Generation Standard v1.0.md
docs/Roadmap.md
docs/Toolchains.md
README.md
```

as appropriate.

Also review:

```text
Requirements/
```

for files whose old requirements contradict the new strategy.

Do not attempt to rewrite every historical file unnecessarily.

Mark meaningful conflicts clearly.

---

# 31. Suggested Immediate Implementation Tasks

Codex should implement governance in this order:

## Task 1

Shorten/restructure `AGENTS.md` so the permanent mission is prominent.

## Task 2

Create:

```text
docs/SMILE Core Principles.md
```

and establish the authority order.

## Task 3

Identify the most important superseded historical requirements, especially those requiring complex INPUT/runtime equivalence, all-target maintenance, or mandatory CI behavior.

Mark them clearly.

## Task 4

Add a simple historical requirements index.

## Task 5

Create the fast MissionGuardrail test category and canonical golden/structural tests for C#, C, and MASM.

## Task 6

Update generator-development documentation with the mandatory checklist.

## Task 7

Confirm Velocity Mode's focused validation command includes MissionGuardrail tests when generator/language-output behavior changes.

---

# 32. Completion Report Requirements

For any future task that changes generated target code, Codex should report:

```text
Changed SMILE feature:
Affected active targets:
Native constructs used:
Custom runtime/helper added?:
If yes, why unavoidable:
MissionGuardrail tests run:
Focused functional tests run:
Before/after generated example:
Known tradeoffs:
```

Keep the report concise, but do not omit the native-code/readability review.

---

# 33. Governance Failure Examples

The following are governance failures even if the generated program technically works.

## Failure A

A four-line C# program emits:

```text
150 lines of generated input runtime
```

despite `Console.ReadLine()` and normal parsing being acceptable.

## Failure B

C uses a custom UTF-8 byte state machine instead of `scanf` for a beginner Integer INPUT because an old test expects exact redirected-byte behavior.

## Failure C

MASM emits several generic helper procedures for a single `scanf`-style Integer input.

## Failure D

A future Python backend uses a generated SMILE runtime instead of ordinary:

```python
input()
int()
print()
```

without a strong requirement.

## Failure E

A future Codex updates seven paused targets and runs all-ten-target validation for a small C generator fix despite the active three-target strategy.

## Failure F

A future Codex sees a historical requirement file and follows it even though `SMILE Core Principles.md` explicitly supersedes that behavior.

---

# 34. Governance Success Example

Given a beginner program such as:

```smile
LET age = 0
INPUT age
PRINT $"You are {age} years old."
```

the conceptual outputs should remain recognizable:

## C#

```csharp
int age = int.Parse(Console.ReadLine()!);
Console.WriteLine($"You are {age} years old.");
```

## C

```c
int age;
scanf("%d", &age);
printf("You are %d years old.\n", age);
```

## MASM

Use normal readable CRT / Win64 assembly input/output appropriate to the selected toolchain.

A future Python implementation should naturally resemble:

```python
age = int(input())
print(f"You are {age} years old.")
```

A future C++ implementation should naturally resemble ordinary `std::cin` / `std::cout` or `std::getline` code as appropriate.

The exact textual lowering can differ.

The permanent invariant is that the target looks like the target language.

---

# 35. Final Governance Rule

Use this sentence as the final decision rule:

> **When choosing between preserving compiler machinery and preserving SMILE's educational clarity, first ask whether that machinery is still required by the current approved language design. If it is not, remove or simplify it. If it is required, implement it in the most idiomatic and least intrusive way possible.**

And this sentence governs generated source:

> **A SMILE learner should learn C# by looking at generated C#, C by looking at generated C, Assembly by looking at generated Assembly, and any future language by looking at normal code for that language—not by learning SMILE's compiler internals.**

---

# 36. Definition of Done

Permanent governance is successfully established when:

- root `AGENTS.md` prominently states the new mission;
- `docs/SMILE Core Principles.md` exists;
- authority order is documented;
- conflicting historical requirements are clearly marked;
- a historical requirements index exists;
- MissionGuardrail tests exist and are fast;
- golden/structural readability tests protect C#, C, and MASM;
- future targets are explicitly bound by the same permanent rules;
- Velocity Mode still runs mission guardrails when relevant;
- generator completion reports include human-readable output review;
- obsolete tests cannot silently force SMILE back toward superseded complexity;
- a future Codex session can determine the current strategy without reconstructing it from conversation history.
