# Project Instructions

- SMILE is a public repo, so write detailed commit messages that help people follow its progression.
- Whenever Codex creates a commit for this project, prefix the commit subject with `Sin and Codex:`.
- When there is no unresolved work left in the queue and all relevant tests are green, Codex should commit and push the intentional changes to the current branch.
- Do not wait for a separate push request in that situation. The project is currently single-developer, and a bad final push can be reverted with a later commit.
- Never force-push, never discard user work, and do not commit unrelated local changes.

## Guiding Principles

- KISS and KISS v2, "The Sin Way," govern the entire SMILE project, including architecture, UI, runtime behavior, documentation, tests, and generated target-language code.
- Choose the simplest complete solution. Avoid unnecessary complexity, abstractions, frameworks, dependencies, code, files, folders, classes, methods, variables, features, and bells and whistles.
- User-experience performance is the first performance priority. Functional performance is second.
- The WPF UI thread must never be blocked by toolchain detection, compilation, linking, execution, process output, long file operations, or other noticeable work.
- Generated code must be minimal, idiomatic, readable, deterministic, educational, dependency-light, and fast without sacrificing clarity.

## Educational Code Comments

- SMILE is also a learning project for compilers, interpreters, transpilers, and local toolchains.
- Be generous with inline comments where they explain compiler concepts, parsing decisions, syntax-tree boundaries, target generation choices, process execution, cancellation, and UI responsiveness.
- Comments should teach the "why" and the flow of the system. Avoid comments that merely repeat an obvious line of code.

## Build Artifact Cleanup

- Codex has permission to permanently delete SMILE-owned build and output artifacts older than 2 days when they are discovered.
- This permission applies only to known generated artifact locations such as `bin`, `obj`, `out`, and `%TEMP%\SMILE\Runs`.
- Before deleting recursively, verify resolved absolute paths are inside the SMILE repository or the SMILE-owned temporary root. Never delete outside those roots.

## Living Documentation

- `README.md` is the living source of truth for SMILE's current mission, principles, features, supported syntax, target languages, toolchain requirements, setup steps, UI behavior, limitations, and roadmap.
- Every feature, command, target language, toolchain, UI behavior, build/run behavior, prerequisite, architecture change, renamed path, changed limitation, or changed generated output must update `README.md` in the same commit.
- Documentation must describe the code that actually exists after the change. Never present a roadmap item as implemented.
- When a change genuinely requires no README update, explain why in the commit or pull-request summary.
