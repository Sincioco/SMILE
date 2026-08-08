# SMILE Velocity Mode — Focused Testing and Temporary CI Pause

## Purpose

SMILE development is entering a velocity-focused phase.

The current project has accumulated a large validation burden:

- Debug build;
- Release build;
- full Debug tests;
- full Release tests;
- strict test variants;
- multi-toolchain execution;
- generated-warning checks;
- all-target conformance;
- post-push CI completion gates.

That level of validation is useful at release/milestone boundaries.

It is too expensive as the mandatory price of every small edit during active language design.

Until Sin explicitly changes this policy, use **Velocity Mode**.

---

# 1. Core Velocity Rule

> **Run the smallest set of tests that gives reasonable confidence in the code being changed. Do not run the full suite by default.**

Normal development should optimize for:

```text
edit -> focused validation -> commit -> push -> continue
```

not:

```text
edit -> every target -> every configuration -> every integration test -> CI gate
```

---

# 2. Pause automatic CI/CD

The current `.github/workflows/smile-ci.yml` automatically runs on pushes and pull requests.

Temporarily stop automatic CI execution.

Preferred reversible implementation:

- keep the workflow file and commands;
- remove `push` and `pull_request` triggers;
- retain only `workflow_dispatch` so full CI can still be manually invoked for a milestone.

Equivalent:

```yaml
on:
  workflow_dispatch:
```

Do not delete the workflow logic.

Do not destroy the ability to restore automatic CI later.

Add a clear comment in the workflow explaining that automatic triggers are intentionally paused for Velocity Mode.

---

# 3. Remove the mandatory post-push CI completion gate

`AGENTS.md` currently says every pushed commit is incomplete until its exact-SHA `SMILE CI` run completes successfully.

That is incompatible with Velocity Mode.

Replace it with:

> **During Velocity Mode, normal pushes do not require a GitHub Actions run. The exact-SHA CI completion gate is suspended. Run manual/full CI only for major milestones, release candidates, target re-enablement, broad architectural changes, or when Sin explicitly asks.**

Keep the old policy in history; do not preserve it as an active mandatory instruction.

---

# 4. Routine validation tiers

Use three levels.

## Tier 1 — Tiny/local change

Examples:

- documentation;
- formatting;
- one generator output adjustment;
- isolated syntax-highlighting change;
- one focused bug.

Run:

```text
the narrowest directly related tests
```

and, when code changed:

```text
one Debug build or the smallest project build needed to prove compilation
```

Do not automatically run Release or the full suite.

## Tier 2 — Normal feature/fix

Examples:

- parser/binder change;
- C# generator feature;
- C generator fix;
- MASM lowering change;
- Desktop behavior change;
- toolchain change.

Run:

- focused unit tests for the changed subsystem;
- focused active-target generation tests;
- build affected projects or one normal Debug solution build;
- one small end-to-end smoke test when appropriate.

Do not automatically test paused targets.

Do not automatically run both Debug and Release.

## Tier 3 — Major milestone

Examples:

- adding a new SMILE language feature;
- changing core type semantics;
- changing parser/binder architecture;
- changing INPUT/PRINT semantics;
- major release/version milestone;
- re-enabling a target;
- before a release candidate;
- when Sin explicitly requests "full tests."

Run the comprehensive suite appropriate to the current active target set.

This is when broader Debug/Release and integration checks belong.

---

# 5. Major milestone does not automatically mean all paused targets

While the seven targets are paused, a normal major SMILE milestone should fully validate:

```text
shared compiler/evaluator
C#
C
MASM x64
Desktop/CLI behavior relevant to the feature
```

Do not require the seven paused backends.

When re-enabling a paused language, run its catch-up/full conformance tests as part of that re-enablement milestone.

---

# 6. Create focused test categories if useful

The current test suite contains many test classes spanning targets and integration levels.

Codex may add simple MSTest categories such as:

```text
Core
CSharp
C
Masm
Desktop
Toolchain
Full
PausedTarget
Slow
```

only if this makes test selection substantially simpler.

Do not spend days categorizing every historical test before productive work can continue.

KISS: categorize the tests needed for the immediate workflow first.

---

# 7. Provide simple documented commands

Update contributor/Codex documentation with a few copyable commands.

Examples should use the actual current test project and verified MSTest filter syntax.

Document:

```text
Focused test command
Active-target test command
Full milestone test command
Manual GitHub Actions milestone command/workflow
```

Do not create an elaborate build orchestration framework merely to choose test subsets.

A small PowerShell helper is acceptable if it materially simplifies repeated commands.

---

# 8. Do not run strict/all-target validation automatically

Suspend routine requirements such as:

```text
SMILE_REQUIRE_ALL_TARGETS
SMILE_REQUIRE_JAVA
SMILE_REQUIRE_ZERO_TARGET_WARNINGS across every target
all-ten-target runtime execution
Debug + Release duplicated full suites
```

unless they are relevant to a major milestone.

For current active work, generated warning checks should focus on:

```text
C#
C
MASM
```

where applicable.

---

# 9. Keep essential correctness tests

Velocity Mode is not "no tests."

Always run the tests that are likely to catch a regression caused by the change.

Examples:

- parser change -> parser/binder tests;
- INPUT change -> INPUT tests for active targets;
- C generator change -> C generator + C build/run smoke;
- MASM change -> MASM generation + assembly/link/run smoke;
- Desktop edit behavior -> focused Desktop tests;
- common expression change -> shared expression tests plus active targets.

The developer must be able to explain why the selected tests are sufficient for the current change.

---

# 10. Avoid duplicate validation

Do not routinely do all of the following for the same tiny change:

```text
Debug full suite
Release full suite
strict Debug full suite
strict Release full suite
all-target local suite
GitHub CI repeating the same suite
manual smoke of every language
```

Choose the narrowest meaningful validation.

Broaden only when risk justifies it.

---

# 11. Commit/push behavior

When Sin explicitly asks Codex to commit and push:

1. run the focused validation appropriate to the change;
2. report what was run;
3. commit with the required `Sin and Codex:` subject;
4. push to `main`;
5. do not wait for or require automatic CI during Velocity Mode because automatic CI is paused.

If the change is a major milestone, run the major-milestone validation before commit/push or manually invoke the retained workflow as directed.

---

# 12. Re-enabling CI later

Make restoration easy.

Document the original automatic triggers:

```yaml
push:
  branches:
    - main
pull_request:
  branches:
    - main
workflow_dispatch:
```

When Sin ends Velocity Mode, automatic CI can be restored in one small workflow edit and the post-push gate can be reactivated in `AGENTS.md`.

Do not require reconstructing the workflow from git history.

---

# 13. Velocity guardrail

Velocity does not justify:

- knowingly broken builds;
- skipping tests directly related to changed code;
- committing compiler errors;
- hiding failures;
- deleting useful tests merely because they are slow;
- weakening language semantics accidentally;
- bypassing source control safety.

The goal is to eliminate **unnecessary repetition**, not correctness.

---

# 14. Permanent readability tests should be fast

The new beginner-readability/golden tests for C#, C, and MASM should be cheap and belong in normal focused validation.

They should not require all target toolchains just to inspect generated source.

This provides a fast guardrail against SMILE losing its way again.

---

# 15. Definition of done

Velocity Mode is active when:

- pushes no longer auto-trigger SMILE CI;
- `AGENTS.md` no longer requires an exact-SHA CI pass for every push;
- normal development uses focused tests;
- paused targets are excluded from routine validation;
- full suites are documented as milestone validation;
- full CI remains manually runnable;
- no tests are deleted solely to make the test count smaller;
- Codex reports the focused checks it actually ran rather than claiming exhaustive validation.
