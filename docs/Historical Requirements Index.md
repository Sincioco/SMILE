# Historical Requirements Index

## Purpose

Files under `Requirements/` preserve the SMILE 1.0 research history. They are not current language specifications or implementation instructions.

The authority order is:

1. Sin's current direct instruction;
2. root `AGENTS.md`;
3. [SMILE Core Principles](SMILE%20Core%20Principles.md);
4. the single [SMILE Core BASIC 1 Official Specification](SMILE%20Language%20Specification/001%20-%20SMILE%20Core%20BASIC%201%20Official%20Specification.md);
5. current architecture, toolchain, target, and milestone documents;
6. historical requirements.

## Classification

| Historical group | Status | Current use |
|---|---|---|
| Pre-1.0 language milestones | `HISTORICAL ONLY` | Research provenance; any superseded source examples are unsupported and rejected by the current compiler |
| Strategic Reset native-generation briefs | `PARTIALLY SUPERSEDED` | The beginner-first native-output principle remains current; their former source-language assumptions do not |
| Velocity Mode brief | `ACTIVE WORKFLOW POLICY` | Focused validation and manual CI remain current beneath `AGENTS.md` |
| Progress logs, audits, screenshots, and completion reports | `HISTORICAL ONLY` | Evidence of past work, not product behavior |
| Old exact cross-runtime/toolchain gates | `HISTORICAL ONLY` | Do not restore without current explicit authority |

No historical document can reactivate a compatibility parser, alternate profile, hidden syntax fallback, or obsolete test expectation. If historical prose conflicts with Core BASIC 1, the current compiler must reject the historical source.

For explicit unsupported-to-canonical rewrites, use [Migrating to Core BASIC 1](Migrating%20to%20Core%20BASIC%201.md). Do not edit historical bodies merely to make them sound current.
