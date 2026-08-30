# Roadmap

## Current baseline: SMILE 1.0

SMILE 1.0 is the breaking Core BASIC alignment milestone. It replaces the research-era source language with one canonical profile frozen from SMILE 2.0.

Implemented in this baseline:

- one case-insensitive Core BASIC lexer/parser/binder path;
- Number, Boolean, and Text values; direct assignment; `Dim`; and `Const`;
- canonical expressions and expression-list `Print`;
- `If`, ascending/descending `For`, post-tested `Do`, typed exits, and `End Program`;
- explicit rejection of superseded source forms and excluded SMILE 2.0 features;
- evaluator support for the complete profile;
- deterministic generation and Build & Run integration for all ten active targets;
- canonical CLI, Desktop, packaged example, and syntax highlighting with no language selector;
- a pinned unchanged parity fixture compiled by both repositories;
- current specification, migration, architecture, toolchain, target, and parity documentation.

## Permanent direction

- Keep SMILE beginner-first and generated code educational.
- Keep exactly one source-language path unless Sin explicitly changes the language strategy.
- Keep all ten current destination generators, toolchains, tests, and product surfaces active.
- Prefer native target constructs and proportional output.
- Preserve the shared parser/binder/bound-tree pipeline.
- Keep Desktop responsive and failures recoverable.
- Keep SMILE 2.0 authoritative and read-only for profile parity work.

## Current development mode

Velocity Mode remains active. Routine work uses focused validation plus `MissionGuardrail` for affected language/generator behavior. Broad all-target and parity runs belong to major milestones, releases, or explicit requests.

`SMILE CI` remains manually runnable. Automatic triggers and the exact-SHA post-push gate remain paused.

## Destination-language freeze

The active set is C#, C, Windows x64 MASM Assembly, JavaScript, Java, COBOL, Objective-C, Swift, Python, and C++. Do not add or scaffold another destination until Sin explicitly changes this strategy.

## Next work

Future work should be driven by observed defects, clearer generated teaching output, toolchain reliability, diagnostics, Desktop usability, and explicit new product direction. Do not reintroduce compatibility modes or present unimplemented SMILE 2.0 superset features as a roadmap promise.

Historical milestone narratives remain under `Requirements/` for research context and do not define current source behavior.
