# Historical Requirements Index

## Purpose

Files under `Requirements/` preserve the SMILE 1.0 research history. They are not current language specifications or implementation instructions.

The authority order is:

1. Sin's current direct instruction;
2. root `AGENTS.md`;
3. the read-only SMILE 2.0 source implementation for shared language behavior;
4. [SMILE Core Principles](SMILE%20Core%20Principles.md);
5. the single [SMILE Core BASIC 2 Official Specification](SMILE%20Language%20Specification/002%20-%20SMILE%20Core%20BASIC%202%20Official%20Specification.md);
6. current architecture, toolchain, target, and milestone documents;
7. historical requirements.

## Classification

| Historical group | Status | Current use |
|---|---|---|
| Pre-1.0 language milestones | `HISTORICAL ONLY` | Research provenance; any superseded source examples are unsupported and rejected by the current compiler |
| Strategic Reset native-generation briefs | `PARTIALLY SUPERSEDED` | The beginner-first native-output principle remains current; their former source-language assumptions do not |
| Velocity Mode brief | `ACTIVE WORKFLOW POLICY` | Focused validation and manual CI remain current beneath `AGENTS.md` |
| Progress logs, audits, screenshots, and completion reports | `HISTORICAL ONLY` | Evidence of past work, not product behavior |
| Old exact cross-runtime/toolchain gates | `HISTORICAL ONLY` | Do not restore without current explicit authority |
| Core BASIC Profile 1 specification/report/migration set | `HISTORICAL VALID SUBSET` | Archived under `Requirements/Archive/Core-BASIC-1`; Profile 1 programs remain valid under Profile 2, but those files are not the current specification |

No historical document can reactivate a compatibility parser, alternate profile, hidden syntax fallback, or obsolete test expectation. If historical prose conflicts with Core BASIC 2, the current compiler must follow the current official specification and pinned SMILE 2.0 behavior.

Do not edit archived historical bodies merely to make them sound current.
