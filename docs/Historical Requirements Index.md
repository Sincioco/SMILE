# Historical Requirements Index

## Purpose

SMILE preserves dated requirement files as project history. They explain earlier milestones but do not automatically remain current instructions.

Current authority is defined by `AGENTS.md` and `docs/SMILE Core Principles.md`. When a historical file conflicts with current governance or an official specification, the current higher-authority document wins.

## Status Meanings

| Status | Meaning |
|---|---|
| `ACTIVE` | Current milestone instructions that remain in force beneath AGENTS, Core Principles, and current official specifications |
| `PARTIALLY SUPERSEDED` | Useful history and some still-valid decisions, but identified portions no longer govern |
| `SUPERSEDED` | Replaced as an active design or workflow requirement |
| `HISTORICAL ONLY` | Completed milestone, progress record, audit, or implementation note retained for context |

Unless a pre-Strategic-Reset file is specifically identified as current by a higher-authority source, treat it as `HISTORICAL ONLY` rather than active law.

## Current Strategic Reset

| Path | Status | Current use |
|---|---|---|
| `Requirements/2026-08-08 - Re-strategize SMILE/00 - SMILE Strategic Reset - Master Instructions.md` | `ACTIVE` | Umbrella reset and permanent/temporary distinction |
| `Requirements/2026-08-08 - Re-strategize SMILE/01 - SMILE Beginner-First Idiomatic Transpilation and Permanent Guardrails.md` | `ACTIVE` | Native beginner-first generation direction |
| `Requirements/2026-08-08 - Re-strategize SMILE/02 - SMILE Temporary Three-Target Focus - CSharp C MASM.md` | `SUPERSEDED` | The temporary freeze ended on 2026-08-10 when all ten implemented targets were reactivated through AGENTS, Core Principles, and the central target policy |
| `Requirements/2026-08-08 - Re-strategize SMILE/03 - SMILE Velocity Mode - Focused Testing and CI Pause.md` | `ACTIVE` | Temporary focused-validation and manual-CI policy |
| `Requirements/2026-08-08 - Re-strategize SMILE/04 - SMILE Permanent Governance and Codex Guardrails.md` | `ACTIVE` | Governance implementation instructions; permanent results now live in AGENTS and Core Principles |
| `Requirements/SMILE Coding Standards/2026-08-08 0901 - Standard 1/` | `ACTIVE` | Current canonical style fixtures for C#, C, MASM, and the paired SMILE example; PRINT keeps its separately specified newline semantics |

## Priority Partially Superseded Briefs

| Historical path | Status | Current governing replacement |
|---|---|---|
| `Requirements/Archive/Pre-Strategic-Reset/2026-08-06 2139 - SMILE Codex Instructions v0.7.0 INPUT.md` | `PARTIALLY SUPERSEDED` | Current official INPUT specification and Core Principles; keep syntax, fixed typing, runtime-unknown analysis, and responsive interaction, not strict UTF-8/4096/NUL/all-target runtime machinery |
| `Requirements/Archive/Pre-Strategic-Reset/2026-08-06 1747 - SMILE Codex Instructions Post-Push CI Completion Gate and Workflow Hardening.md` | `PARTIALLY SUPERSEDED` | AGENTS and Velocity Mode suspend automatic triggers and the exact-SHA gate; manual workflow and security history remain |
| `Requirements/Archive/Pre-Strategic-Reset/2026-08-07 1929 - SMILE - Codex Instructions v0.8.0 WHILE Loops.md` | `PARTIALLY SUPERSEDED` | Current WHILE and INPUT specifications, active-target policy, and Velocity Mode |
| `Requirements/Archive/Pre-Strategic-Reset/2026-08-07 2352 - SMILE Codex Instructions - LET Block Strings and Idiomatic Multiline Strings v0.8.0 FULL.md` | `PARTIALLY SUPERSEDED` | Current String/LET/SET specifications and native beginner-first generation policy |
| `Requirements/Archive/Pre-Strategic-Reset/2026-08-05 1855 - SMILE Codex Instructions v0.5.1.1 Generated Warning Hygiene.md` | `PARTIALLY SUPERSEDED` | Warning hygiene applies to changed targets and milestones; routine strict ten-toolchain gates remain paused under Velocity Mode |
| `Requirements/Archive/Pre-Strategic-Reset/2026-08-04 1819 - SMILE Codex Instructions v0.4.2.1 Exact String and Target Safe Expression Hardening.md` | `PARTIALLY SUPERSEDED` | Short-circuit correctness remains; exact edge-case machinery must be justified by a current official rule and Core Principles |

## Other Completed Implementation Briefs

The following groups are `HISTORICAL ONLY` as implementation instructions. Current official specifications preserve any language rules that remain active.

| Paths or group | Status | Note |
|---|---|---|
| `Requirements/Archive/Pre-Strategic-Reset/2026-08-01*` and `Requirements/Archive/Pre-Strategic-Reset/2026-08-02*` | `HISTORICAL ONLY` | Initial PRINT, Desktop, audit, idiomatic-output, highlighting, and stability milestones |
| `Requirements/Archive/Pre-Strategic-Reset/2026-08-03*` | `HISTORICAL ONLY` | LET, core expressions, Python-target, and Desktop zoom milestones |
| `Requirements/Archive/Pre-Strategic-Reset/2026-08-04 1820 - SMILE Codex Instructions v0.4.3 Add CPP Final Target.md` | `HISTORICAL ONLY` | C++ is the active tenth implementation; the current no-new-target rule independently remains in Core Principles |
| `Requirements/Archive/Pre-Strategic-Reset/2026-08-04 1945 - SMILE Codex Instructions v0.4.3.1 Final Target Identifier and Header Hygiene.md` | `HISTORICAL ONLY` | Completed target-hardening milestone; all target identifier data is active again |
| `Requirements/Archive/Pre-Strategic-Reset/2026-08-05 0456 - SMILE Codex Instructions v0.5.0 Runtime Variables SET and Block String FULL.md` | `HISTORICAL ONLY` | Completed SET milestone; current SET/String specifications govern |
| `Requirements/Archive/Pre-Strategic-Reset/2026-08-05 1711 - SMILE Codex Instructions v0.5.1 Runtime Storage Readiness.md` | `HISTORICAL ONLY` | Completed runtime-storage milestone; old all-target exactness is not current strategy |
| `Requirements/Archive/Pre-Strategic-Reset/2026-08-05 2024 - SMILE Codex Instructions v0.6.0 IF ELSE IF ELSE.md` | `HISTORICAL ONLY` | Completed IF milestone; current IF specification governs |
| `Requirements/Archive/Pre-Strategic-Reset/2026-08-06 1306 - SMILE Codex Instructions v0.6.0.1 IF Hardening.md` | `HISTORICAL ONLY` | Completed hardening milestone; its automatic-CI/all-target workflow is superseded |
| `Requirements/Archive/Pre-Strategic-Reset/2026-08-06 1917 - SMILE Codex Instructions v0.6.1 Full-Line Comments and Source Layout Preservation.md` | `HISTORICAL ONLY` | Completed layout milestone; current comment/layout specification governs |
| `Requirements/Archive/Pre-Strategic-Reset/2026-08-07 1648 - SMILE Codex Instructions v0.7.0.1 Target-Editor Hardening.md` | `HISTORICAL ONLY` | Completed Desktop milestone; exact-SHA/all-target completion steps are superseded |

## Daily Records And Progress Artifacts

The archived Day 1 through Day 7 requirement records, audit transcripts, completion logs, commit templates, validation reports, and screenshots under `Requirements/Archive/Pre-Strategic-Reset/Progress/` are `HISTORICAL ONLY` unless Sin directly reactivates a specific item. The current Day 8 reset record remains outside that archive and is governed by the active Strategic Reset documents above.

Do not rewrite historical bodies merely to make them sound current. Add or maintain a clear banner when a detailed old brief is likely to be mistaken for active strategy.
