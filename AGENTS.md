# SMILE Repository Instructions

## NON-NEGOTIABLE SMILE MISSION

SMILE means **Simple Modern and Intuitive Language for Everyone**. It is a beginner-first educational programming language.

Generated target code is part of the teaching experience. It **MUST** use the normal, idiomatic, beginner-readable way a programmer would ordinarily express the same idea in the destination language whenever practical.

Do not introduce compiler-generated runtime machinery when the destination language already provides a normal native construct for the feature. Existing implementation complexity is not itself a requirement, and an old test does not make a superseded behavior permanent.

SMILE 1.0 accepts one canonical Core BASIC 2 grammar. Write variables directly in statement and expression positions, including `Name = "Sin"`, `Print Name`, and `If Name = "Sin" Then`. Apostrophes start comments; historical raw Print templates, interpolation, `LET`, `SET`, `INPUT`, and `WHILE` forms are not active language syntax.

Before changing the compiler, generators, language specifications, runtime behavior, or target tests, read [docs/SMILE Core Principles.md](docs/SMILE%20Core%20Principles.md) and the relevant current official specification.

## Authority Order

When sources conflict, the higher source governs:

1. Direct current instruction from Sin.
2. This root `AGENTS.md`.
3. The current SMILE 2.0 source language implementation for shared syntax and semantics; inspect it read-only when behavior differs.
4. `docs/SMILE Core Principles.md`.
5. Current official specifications under `docs/SMILE Language Specification/`.
6. Current architecture, generation, and toolchain standards.
7. Current milestone implementation instructions.
8. Historical files under `Requirements/`.
9. Historical commit messages and old implementation notes.

Do not silently average conflicting requirements. Historical requirements may explain how SMILE reached its current state, but they cannot override current governance or specifications.

## Current Development Mode

The permanent mission applies to every target now and in the future.

- Active targets: C#, C, Windows x64 MASM Assembly, JavaScript (Node.js), Java, COBOL, Objective-C, Swift, Python, and C++.
- Keep all ten generators, toolchains, tests, highlighting, Desktop/CLI exposure, and history available.
- Routine work remains focused on the targets it changes; activating all ten does not require a full ten-toolchain matrix for every unrelated edit.
- Do not add, recommend, prototype, or scaffold another destination language unless Sin explicitly changes the strategy.
- Use a single-agent workflow by default. Do not delegate or spawn sub-agents unless Sin explicitly requests it.

SMILE is also in temporary **Velocity Mode**:

- Run the smallest focused validation that gives reasonable confidence in the changed code.
- Do not run duplicated Debug/Release or strict ten-toolchain matrices by default.
- Full all-target validation belongs at major milestones, releases, broad architecture changes, or when Sin explicitly requests it.
- Automatic GitHub Actions triggers and the exact-SHA post-push completion gate are suspended. `SMILE CI` remains manually runnable with `workflow_dispatch`.
- Velocity Mode never permits knowingly broken builds, skipped directly relevant tests, hidden failures, or weakened semantics by accident.

After changing an active generator, assignment, `Print`, `If`, `For`, `Do`, routines, `Select Case`, arrays, target expression rendering, or runtime-generation policy, run the fast mission guardrail:

```powershell
dotnet test tests/SMILE.Tests/SMILE.Tests.csproj -c Debug --filter TestCategory=MissionGuardrail -nologo
```

Also run the narrow functional tests and smallest build appropriate to the change. Report only the checks actually run.

## Implementation Rules

- KISS and KISS v2, "The Sin Way," govern architecture, UI, runtime behavior, documentation, tests, and generated code.
- Prefer the simplest complete native target construct. Add a helper only when it is genuinely necessary and does not hide the concept being taught.
- Keep generated source minimal, readable, deterministic, educational, dependency-light, and proportional to the SMILE program.
- Python target output is a direct executable script. Emit learner statements at module top level after only the imports and helpers actually required. Do not generate a `main()` function or `if __name__ == "__main__":` guard solely as boilerplate. Preserve normal indentation only for real Python suites such as routines, `If`, `For`, and `Do` bodies.
- Preserve clear SMILE language semantics, expression intent, genuine `If`/`For`/`Do` structure, current runtime storage, and the shared lexer/parser/binder/bound-tree pipeline.
- Keep `Parser.cs` focused on parsing, `Binder.cs` focused on binding, `Generation.cs` as the small public facade, shared helpers under `src/SMILE.Engine/Generation`, and each destination generator focused.
- The WPF UI thread must never be blocked by toolchain detection, compilation, linking, execution, process output, long file operations, or other noticeable work.
- Recoverable Desktop failures must report a concise visible message, record diagnostics when possible, and keep the IDE open.
- Comments should teach compiler concepts, design reasons, target choices, process behavior, cancellation, and UI responsiveness rather than restating obvious code.

When generator output changes, the completion report must include a small before/after generated example and state:

- affected SMILE feature and active targets;
- native constructs used;
- whether any custom helper was added and why it was unavoidable;
- MissionGuardrail and focused functional tests run;
- known target-native tradeoffs.

## Source Control

- SMILE is public; write detailed public-reader-friendly commit messages.
- When Codex creates a commit, prefix the subject with `Sin and Codex:`.
- Do not commit or push unless Sin explicitly asks.
- Work directly on `main`; do not create or recommend a branch unless Sin changes this rule.
- "Commit all files" means stage all current non-ignored unstaged and untracked repository changes, while never force-adding ignored build output.
- Never force-push, rewrite published history, discard user work, or commit unrelated local changes.
- Never commit generated build/output folders or files.

## Living Documentation

- `docs/SMILE Core Principles.md` is the canonical current strategy beneath this file.
- Current official language behavior lives under `docs/SMILE Language Specification/`.
- `README.md` is the living public product overview for implemented features, active targets, setup, UI behavior, limitations, and roadmap.
- `docs/Architecture.md`, `docs/Toolchains.md`, and `docs/SMILE Target Code Generation Standard v1.0.md` must describe the implementation that actually exists.
- `docs/Historical Requirements Index.md` classifies important dated requirement files. Preserve useful history, but clearly mark superseded instructions.
- `examples/language.smile` is the cumulative valid Desktop language reference. Extend it; do not replace or shrink earlier valid teaching coverage.
- Package `language.smile` beside the Desktop executable and preserve first-paint-before-load plus asynchronous visible-active-target transpilation.
- Update README and other affected current documentation in the same commit as a feature, target, toolchain, UI, architecture, limitation, or generated-output change.
- Never present a roadmap item as implemented.

## Build Artifacts And Versioning

- SMILE-owned artifacts older than one day may be deleted only from verified repository `bin`, `obj`, or `out` folders and `%TEMP%\SMILE\Runs`.
- Resolve and verify cleanup paths before recursive deletion; never delete outside the repository or SMILE temporary root.
- A new keyword or important milestone may increment the version. Keep project, README, and About SMILE version text aligned.
