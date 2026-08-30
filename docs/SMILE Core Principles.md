# SMILE Core Principles

## Status and authority

This document is the canonical current product strategy beneath `AGENTS.md`. The [Core BASIC 1 Official Specification](SMILE%20Language%20Specification/001%20-%20SMILE%20Core%20BASIC%201%20Official%20Specification.md) governs language behavior. Historical requirement files explain past research but cannot restore superseded syntax or workflow.

## 1. Beginner first

SMILE means **Simple Modern Interactive Learning Environment**. The source language and generated code are teaching materials. Prefer the smallest complete design a beginner can read, explain, and change.

## 2. One canonical source language

SMILE 1.0 implements only SMILE Core BASIC 1, frozen from SMILE 2.0. Every public surface—engine, evaluator, CLI, Desktop, highlighting, examples, and tests—uses the same parser and binder.

There is no language selector, compatibility profile, source auto-detection, or fallback. Unsupported source receives a diagnostic. A breaking error is safer and more teachable than silently changing its meaning.

## 3. Native and idiomatic target code

Generated code should express the source using the normal destination-language construct whenever practical:

| Core BASIC idea | Preferred destination form |
|---|---|
| variable or constant | ordinary typed/native storage |
| `Print` | the destination's familiar output call |
| `If` | genuine structured conditional |
| `For` | genuine counted loop or ordinary range loop |
| `Do` | genuine post-test loop, or the clearest equivalent |
| typed `Exit` | native break/label when it preserves lexical meaning |
| `End Program` | normal successful process termination |

Do not add a runtime abstraction when a normal destination feature already explains the idea.

## 4. Proportional output

A tiny SMILE program must produce tiny target source. Emit imports, declarations, helpers, labels, and support data only when the actual program needs them. Assembly and COBOL are naturally more verbose, but their output must still remain traceable to learner statements.

Python is a direct executable script. Learner statements belong at module top level after only required helper definitions. Do not invent a `main()` function or module guard as boilerplate.

## 5. One semantic pipeline

All targets consume the same bound program. Parsing establishes canonical structure; binding establishes names and exact scalar types; evaluation and generation consume that shared meaning. A backend must not reparse source text, infer another dialect, execute learner loops during compilation, or invent target-specific source semantics.

## 6. Helpers require a reason

A helper is acceptable only when the destination lacks a clear direct construct or when the helper is necessary to preserve the language rule. Keep it local, deterministic, plainly named, and absent from programs that do not need it.

Current justified examples are C Text concatenation and Python control transfer for `Exit For` or `Exit Do` across a differently nested loop kind. Ordinary loops without those exits receive ordinary Python only.

## 7. Ten active destinations

C#, C, Windows x64 MASM Assembly, JavaScript, Java, COBOL, Objective-C, Swift, Python, and C++ are active. Keep their generator, toolchain, Desktop/CLI exposure, tests, and documentation available.

Do not add, recommend, prototype, or scaffold another destination until Sin explicitly changes the target strategy.

## 8. Responsive tools

Toolchain detection, compilation, linking, execution, process I/O, and noticeable file work must not block the WPF UI thread. Recoverable failures stay visible and keep Desktop open. Temporary generated/build files belong beneath a verified SMILE temporary root, not in the repository.

## 9. Tests protect meaning and readability

Focused tests must cover the canonical lexer/parser/binder rules, evaluation, obsolete-source rejection, Desktop and highlighting exposure, deterministic generation for all ten targets, and the native construct used by each changed backend.

After language or generator changes, run the `MissionGuardrail` category plus the narrow functional tests and smallest relevant build. Major language migrations additionally justify all-target toolchain and cross-repository parity checks.

Tests may require a helper only when the specification makes it necessary. An old test cannot preserve superseded behavior.

## 10. Parity is reproducible

The frozen profile records its authoritative SMILE 2.0 commit. One unchanged fixture must compile in both repositories and produce the recorded output. Verification must check that SMILE 2.0 is clean and still at the pinned commit before and after; the authority repository is read-only.

## 11. Documentation is living behavior

README, the official specification, architecture, toolchains, target standard, examples, highlighting, Desktop version text, and parity report must describe what exists now. Earlier source forms belong only in explicitly labeled migration or rejection material.

## Generator change checklist

- Does every target consume the same bound meaning?
- Is the generated construct normal for that destination?
- Is output proportional to the source?
- Is every helper unavoidable and emitted only when used?
- Are Boolean, Text, Number, loop, and output semantics preserved?
- Do direct generation, focused conformance, MissionGuardrail, and relevant toolchain checks pass?
- Does the completion report show a small before/after generated example and target-native tradeoffs?

## Final decision rule

When several correct implementations exist, choose the one that makes both SMILE and the generated destination program easiest for a beginner to understand without hiding the concept being taught.
