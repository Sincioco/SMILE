# SMILE Core Principles

## Status And Authority

This is SMILE's canonical current strategy document beneath direct current instructions from Sin and the root `AGENTS.md`.

When an older requirement, implementation note, test, or historical specification conflicts with this document because SMILE strategy has changed, these Core Principles and the current official language specifications govern. Technical detail in a dated brief does not give it higher authority.

Authority order:

1. Direct current instruction from Sin.
2. Root `AGENTS.md`.
3. This document.
4. Current official SMILE language specifications.
5. Current architecture, target-generation, and toolchain standards.
6. Current milestone implementation instructions.
7. Historical `Requirements/` files.
8. Historical commit messages and old implementation notes.

If two sources conflict, follow the higher one. Do not silently blend contradictory rules. Report a meaningful unresolved conflict before implementation when practical.

## 1. Beginner First

SMILE is a beginner-first educational programming language. Its purpose is to teach programming concepts and help learners recognize how the same idea is normally expressed in another language.

A feature is not complete merely because generated programs compile or reproduce evaluator bytes. The learner-facing result must remain understandable.

## 2. Native And Idiomatic Target Code

Generated code **MUST** use the normal, idiomatic, beginner-readable destination-language construct whenever practical.

Examples of the default direction:

| SMILE concept | Preferred target concept |
|---|---|
| `PRINT` | Native output such as `Console.WriteLine`, `printf`, or a direct CRT call |
| `INPUT` | Native input such as `Console.ReadLine`, `scanf`, `fgets`, or a direct CRT call |
| `IF` | Native conditional control flow |
| `WHILE` | Native pre-test loop control flow |
| String | Normal target String representation |
| Integer | Normal target Integer representation appropriate to the program |
| Interpolation | Native interpolation where the language provides it |

A backend must look like its own language. Do not mechanically imitate another backend or route ordinary language concepts through a generic `SmileRuntime` API.

## 3. Simplicity Over Cross-Runtime Perfection

SMILE preserves ordinary conceptual behavior. It does not require unrelated runtimes to share the same byte reader, parser, buffer limit, error text, or obscure host edge cases.

Do not add a generated runtime library, byte-state machine, UTF-8 validator, exact cross-target line limit, generic error dispatcher, or String-length subsystem merely to make a small beginner program behave identically on malformed or unusual host input.

Target-native differences are acceptable for obscure runtime edges unless an explicit current SMILE rule is both educationally meaningful and important enough to justify the added learner-facing complexity.

## 4. Target Code Is Educational Output

For SMILE, generated-source readability is part of correctness.

A generated program may compile, run, and match expected output yet still be wrong for SMILE if it unnecessarily exposes compiler internals or obscures normal destination-language code.

Generated output should be:

- minimal and proportional to the source program;
- recognizable to a learner of that destination language;
- deterministic and dependency-light;
- clear about native variables, expressions, control flow, input, and output;
- free of avoidable compiler-owned state and helper machinery.

## 5. Native Constructs Before Helpers

Use a custom helper only when:

- the destination genuinely lacks a reasonable native construct;
- the approved current SMILE semantics require it;
- the helper is as small and local as practical;
- the helper does not hide the concept being taught;
- direct native code would be materially more confusing.

Existing implementation complexity is not itself a requirement. Classify old machinery as `KEEP`, `SIMPLIFY`, `REPLACE WITH NATIVE TARGET CONSTRUCT`, or `REMOVE` based on current approved semantics.

## 6. Curly Braces Mean Interpolation

Curly braces `{ }` identify interpolation holes in text-oriented syntax such as raw `PRINT` templates and `$"..."` strings. They are not general variable-reference delimiters.

Variables are written directly wherever the grammar already requires an identifier or expression:

```smile
LET Name = ""
INPUT Name
SET Name = "Sin"

IF Name = "Sin" THEN
    PRINT Hello {Name}!
END IF
```

Do not introduce or accept forms such as:

```smile
INPUT {Name}
SET {Name} = "Sin"
IF {Name} = "Sin" THEN
```

Bare `PRINT Name` remains literal template text. `PRINT {Name}` interpolates the variable because `PRINT` is a text-oriented context.

## 7. KISS And The Sin Way

Choose the simplest complete solution. Avoid speculative abstractions, frameworks, dependencies, files, types, state, and indirection.

User-experience performance comes first. Compiler, toolchain, process, and file work must not block the WPF UI thread. Recoverable Desktop failures must be contained and reported without closing the IDE.

Do not add new abstraction layers merely to remove old abstraction layers.

## 8. Current Active Targets

All ten implemented targets are active:

1. C#
2. C
3. Windows x64 MASM Assembly
4. JavaScript
5. Java
6. COBOL
7. Objective-C
8. Swift
9. Python
10. C++

They are available through the central active-target policy, Desktop and CLI selectors, normal toolchain detection, Transpile All, and Build & Run when the matching local toolchain is installed.

The permanent beginner-first native-code principles apply equally to all ten. Routine work remains focused on the targets it changes; the active set does not force an exhaustive ten-toolchain matrix for every unrelated edit.

## 9. Destination-Language Set

C++ remains the tenth implemented destination. Keep all ten generators, toolchains, tests, identifiers, highlighting, and documentation history intact and available.

No eleventh destination language may be added, recommended, prototyped, or scaffolded unless Sin explicitly reopens target expansion.

## 10. Future Targets Inherit These Principles

A future target is not ready merely because it compiles or matches stdout. Before activation, document:

1. the normal beginner-level expression of every supported SMILE feature;
2. the native APIs and constructs used;
3. any unavoidable custom helper and why it is necessary;
4. how the generated source teaches the destination language;
5. the readability/golden tests that prevent runtime-heavy regression.

## 11. Tests Protect Readability And Behavior

Tests exist to protect the language and teaching experience. They are guardrails, not the product.

When a test protects an intentionally superseded requirement, update the test. Never preserve obsolete behavior merely to keep an old test green, and never delete a failing test without understanding what requirement it represents.

Fast `MissionGuardrail` tests inspect generated source without requiring every target toolchain. The reset-reference C#, C, and MASM checks plus focused target tests protect at least:

- LET plus PRINT;
- INPUT;
- interpolation;
- SET;
- IF;
- WHILE;
- Block Strings.

Routine work uses focused validation. Broader all-target Debug/Release and integration validation belongs to major milestones, releases, broad architecture changes, or explicit requests.

## 12. Historical Requirements May Be Superseded

`Requirements/` preserves valuable project history, including detailed implementation briefs written for earlier strategies. It is not a flat collection of equally current law.

Meaningfully conflicting documents must carry a historical/superseded banner and appear in `docs/Historical Requirements Index.md`. Preserve their historical body unless a small clarification is necessary to prevent active misuse.

## Generator Change Checklist

Before completing a task that changes generated code, check:

- [ ] Does the output use the normal native construct?
- [ ] Is the result understandable to a beginner?
- [ ] Did I add a helper that could have been avoided?
- [ ] Did I preserve destination-language idioms?
- [ ] Did I accidentally expose compiler internals?
- [ ] Did I preserve interpolation intent?
- [ ] Did I preserve the curly-brace rule?
- [ ] Did I preserve genuine IF and WHILE structure?
- [ ] Did I run the `MissionGuardrail` tests?
- [ ] Did I run focused functional tests for the changed behavior?
- [ ] Did I review and report a before/after generated example?

## Completion Reporting For Generator Work

Report concisely:

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

When choosing between preserving compiler machinery and preserving SMILE's educational clarity, first ask whether that machinery is still required by the current approved language design. If it is not, remove or simplify it. If it is required, implement it in the most idiomatic and least intrusive way practical.

A learner should learn C# by looking at normal C#, C by looking at normal C, Assembly by looking at understandable Assembly, and every other destination by looking at normal code for that language—not by learning SMILE compiler internals.
