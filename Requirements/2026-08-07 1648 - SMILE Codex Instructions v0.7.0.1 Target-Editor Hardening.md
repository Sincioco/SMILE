# Codex Implementation Instructions — SMILE v0.7.0.1 Target Editor Hardening

## Repository and workflow

- Repository: `Sincioco/SMILE`
- Work directly on `main`.
- Sin is the only developer.
- Do not create or suggest a feature branch.
- Do not open a pull request.
- Re-read `AGENTS.md` before changing code.
- Inspect the newest `main` commit, working tree, current Desktop architecture, current tests, current `SMILE CI` workflow, and current version metadata before editing.
- Do not discard, reset, overwrite, or commit unrelated work.
- Do not force-push or rewrite published history.
- Follow KISS and KISS v2, “The Sin Way.”
- Preserve the permanent ten-target destination-language freeze.
- Commit all intended changes and push only after required validation is green.
- After pushing, verify that the `SMILE CI` run for the exact final `main` SHA completes successfully before reporting completion.

The reviewed baseline when this brief was prepared was:

```text
ac9fbe19ada91c8d2eeb52f60283e4a50f4684aa
Sin and Codex: Make target editors interactive
```

Do not assume that SHA is still current. Always begin from the newest `main`.

---

# 1. Milestone

Create:

> **SMILE v0.7.0.1 — Target Editor Hardening**

This is a Desktop/editor hardening release.

It introduces **no new SMILE language syntax** and therefore requires **no new official language specification**.

Do not add or modify language behavior unless a correction is strictly required to preserve the already-published v0.7.0 specifications.

The goals are:

1. make `Build & Run Visible` operate on every visible pane independently, even when two or three panes select the same target language;
2. prevent delayed live transpilation from overwriting a target-pane edit that happened after the SMILE transpilation request began;
3. add a simple visible marker showing when a target pane differs from its generated SMILE source;
4. add regression coverage for these state-ordering cases;
5. preserve all existing v0.7.0 INPUT, target-editing, highlighting, Maximize/Restore, New, source-layout, and all-ten-target behavior.

---

# 2. Why this hardening is required

The current Desktop intentionally allows each target pane to be edited independently.

That changes two assumptions that were safe when target panes were read-only:

## 2.1 Duplicate target languages are no longer equivalent panes

Two panes may both select C# but contain different learner edits:

```text
Pane 1 — C#
    edited program A

Pane 2 — C#
    edited program B
```

The current global visible-build path groups panes by target language and builds only the first pane in each language group.

That is no longer correct.

Each visible pane is now an independent editable document and must be treated as such.

## 2.2 Asynchronous generation can finish after a newer target edit

Current live generation is debounced and asynchronous.

This sequence is possible:

```text
1. learner edits SMILE source
2. live transpilation request starts
3. learner edits a target pane
4. older SMILE transpilation finishes
5. generated result replaces the newer target edit
```

The latest user action should win.

A target edit made after a generation request begins must survive that older generation result.

A later SMILE edit must still intentionally regain authority and replace temporary target edits.

---

# 3. Non-goals

Do not add:

- new SMILE keywords;
- WHILE or other loops;
- functions;
- scopes;
- arrays;
- floating-point types;
- another target language;
- a new project system;
- an autosave system;
- a multi-document target editor;
- a diff/merge editor;
- source-control integration;
- branch protection;
- parallel target-pane builds;
- a target-pane stdin mode selector in this milestone;
- automatic parsing of hand-edited target source to infer whether it needs stdin;
- target-specific language services or IntelliSense.

Do not change the official INPUT specification.

Do not change the semantics of:

```text
LET
SET
PRINT
IF
INPUT
comments
blank-line preservation
String literals
Integer arithmetic
Boolean expressions
```

---

# 4. Preserve the current target-editor contract

Keep these existing behaviors:

- target panes are editable when the app is not busy;
- each pane may independently select any of the ten target languages;
- Copy uses the pane's current visible source;
- Save Source uses the pane's current visible source;
- Build & Run uses the pane's current visible primary source;
- generated project metadata and companion files remain compiler-owned;
- a current valid SMILE snapshot supplies target metadata and companions;
- blank or invalid SMILE may use the existing minimal target-only fallback container;
- a direct target edit does not enter the SMILE-generated preview cache;
- an unrelated pane language switch does not erase another pane's edit;
- toolchain detection does not erase target edits;
- explicit Transpile All intentionally replaces target edits;
- a later SMILE source edit intentionally replaces earlier target edits;
- New clears SMILE and all target panes;
- same-pane language switching intentionally replaces that pane's previous edit;
- INPUT interactive-console selection continues to come from valid generated SMILE metadata;
- Maximize/Restore preserves the same AvalonEdit instance;
- the WPF UI remains responsive.

---

# 5. Task 1 — Build every visible pane independently

The current global visible-build code contains behavior equivalent to:

```csharp
Panes
    .Where(CanPreparePaneSource)
    .GroupBy(pane => pane.Language)
    .Select(group => group.First())
```

Remove the language-level grouping.

Iterate visible panes in deterministic pane order:

```text
Pane1
Pane2
Pane3
```

Every pane that can be prepared and built must receive its own build/run attempt.

## 5.1 Duplicate-language example

Given:

```text
Pane 1 = C#
Pane 2 = C#
Pane 3 = Python
```

all three must be processed.

If Pane 1 and Pane 2 contain different C# text, each build must receive exactly its own current primary source.

Do not silently choose one.

## 5.2 Do not parallelize pane builds

Keep the global visible-build operation sequential.

Reasons:

- simpler output ordering;
- predictable CPU/toolchain load;
- simpler cancellation;
- lower temporary-directory collision risk;
- better learner visibility.

KISS applies here.

## 5.3 Isolated build inputs

Each pane build must receive its own `GeneratedProgram` instance with:

- that pane's current primary source;
- the correct current target language;
- the appropriate generated project file;
- the appropriate companion files;
- the appropriate `RequiresStandardInput` metadata;
- no mutation of the cached generated preview.

If existing toolchains already create unique per-invocation working directories, preserve that design.

If any toolchain reuses a shared mutable working directory in a way that causes one same-language pane to overwrite another pane's build during the same global operation, fix only that collision using the smallest deterministic per-build workspace identity.

Do not redesign the entire toolchain layer.

---

# 6. Task 2 — Rename the global command to describe pane semantics

Because duplicate target languages can now be built independently, update the user-facing command text from:

```text
Build & Run Visible Languages
```

to:

```text
Build & Run Visible Panes
```

Use equivalent casing consistent with the current UI.

Update:

- toolbar button text;
- operation-status text where appropriate;
- README/Desktop documentation;
- tests.

Internal method names may be renamed when that makes the code clearer, for example:

```text
BuildRunVisibleAsync
```

may remain if it is already concise, or become:

```text
BuildRunVisiblePanesAsync
```

Do not churn names unnecessarily.

---

# 7. Task 3 — Make build output identify the pane

When multiple panes use the same target language, output headers must distinguish them.

Use a clear form such as:

```text
=== Generated target 1 — C# ===
=== Generated target 2 — C# ===
=== Generated target 3 — Python ===
```

Use the pane's existing base identity plus target display name.

Do not rely only on:

```text
=== C# ===
```

because two C# results become ambiguous.

For a single-pane Build & Run button, the existing concise language-oriented output may remain if appropriate, but consistent pane identification is preferred.

---

# 8. Task 4 — Add a per-pane user-edit revision

Add a monotonically increasing revision to `TargetPaneViewModel`, for example:

```csharp
private long _userEditRevision;

public long UserEditRevision => _userEditRevision;
```

Increment it **only when the learner changes the target editor text**.

Do not increment it when generated code is applied programmatically.

The existing `_isApplyingGeneratedCode` guard is the natural place to distinguish those cases.

Conceptually:

```csharp
if (isUserEdit)
{
    _userEditRevision++;
    ...
}
```

Use `long`.

No global static counter is needed.

---

# 9. Task 5 — Capture target-edit revisions when live generation is scheduled

When a live SMILE transpilation request is created, capture the edit revision of each pane that may receive that request's result.

A small immutable snapshot is sufficient.

Example concept:

```csharp
private sealed record PaneGenerationState(
    TargetPaneViewModel Pane,
    TargetLanguage Language,
    long UserEditRevision);
```

The exact type is up to the implementation.

Do not capture mutable references and then read their revision only at completion; record the numeric revision at request creation.

Also continue to capture:

- SMILE source snapshot;
- SMILE source revision;
- target language(s);
- cancellation token.

---

# 10. Task 6 — Apply generated code only if it is still newer than the pane state

When a live transpilation result returns, existing source-revision checks remain mandatory.

In addition, before replacing a target pane:

```text
captured user-edit revision
must equal
current user-edit revision
```

If the pane's user-edit revision changed after the generation request began:

- do not replace its current target source;
- do not clear `HasUserEdits`;
- do not overwrite its `Edited` state;
- do not make its build commands unavailable;
- still allow the generated program to enter the normal language/source-revision cache when valid;
- still update sibling panes whose edit revisions did not change;
- still report SMILE diagnostics normally.

This is a presentation-ownership check, not a compiler-generation rejection.

---

# 11. Required ordering semantics

Implement and test these rules explicitly.

## 11.1 Target edit after SMILE request begins wins

Sequence:

```text
SMILE edit A
generation A begins
target edit T
generation A completes
```

Final target pane:

```text
T
```

The generation result may exist in cache, but it does not overwrite the pane.

## 11.2 Later SMILE edit wins

Sequence:

```text
target edit T
SMILE edit B
generation B begins
generation B completes
```

Final target pane:

```text
generated result B
```

The SMILE edit intentionally reasserts generated-source authority.

This remains existing product behavior.

## 11.3 Multiple target edits during one pending generation

Sequence:

```text
SMILE edit A
generation begins
target edit T1
target edit T2
generation completes
```

Final target pane:

```text
T2
```

## 11.4 Sibling panes remain independent

Sequence:

```text
SMILE edit A
generation begins for C#, MASM, C
Pane1 target edit
generation completes
```

Expected:

```text
Pane1 = learner edit
Pane2 = newly generated MASM
Pane3 = newly generated C
```

Do not discard the entire generation batch merely because one pane changed.

## 11.5 Same target language in two panes remains independent

If Pane1 and Pane2 both use C#:

```text
SMILE generation begins
Pane2 edited
generation completes
```

Expected:

```text
Pane1 = fresh generated C#
Pane2 = learner edit
```

The cache may contain one generated C# snapshot for the current SMILE source, but pane presentation ownership is independent.

---

# 12. Task 7 — Keep SMILE edits authoritative

Do not weaken the existing rule that a **newer SMILE source edit** replaces earlier target edits.

When `SourceText` changes:

- increment the SMILE source revision as today;
- clear target-pane edit ownership as required by the current generated-source-authoritative design;
- schedule new live generation;
- allow that new generation to replace the prior target edits if no still-newer target edit occurs afterward.

The edit-revision mechanism must model ordering, not make target edits permanent.

---

# 13. Task 8 — Same-pane language switch remains authoritative

When the learner changes a pane from:

```text
C#
```

to:

```text
Python
```

the old C# target edit no longer represents the selected language.

Continue to:

- mark old visible source stale;
- clear the pane's edited/divergent state;
- load or generate Python;
- apply Python when ready.

If the learner begins editing the Python pane after the switch but before delayed Python generation finishes, that newer Python edit must survive the older pending generation using the same edit-revision rule.

---

# 14. Task 9 — Explicit Transpile All remains authoritative

Manual:

```text
Transpile All
```

is an explicit request to regenerate from SMILE.

It may replace existing target edits.

Because the application is busy during the operation and target panes are read-only, no user-edit race should normally occur.

Preserve that behavior.

Do not change Transpile All into a merge operation.

---

# 15. Task 10 — Add a visible target-edit marker

When a pane differs from its generated SMILE source, append:

```text
*
```

to the pane title.

Example:

```text
Generated target 1 - C# *
```

When not edited:

```text
Generated target 1 - C#
```

Use the current title punctuation/style already in the app; only add the marker.

## 15.1 Marker meaning

The `*` means:

> This pane has learner edits that differ from the current generated target source.

It does **not** mean that the pane has or has not been saved to a separate file.

Therefore:

- editing target source -> `*` appears;
- Save Source -> `*` remains, because the pane still differs from generated SMILE output;
- fresh generated code applied -> `*` disappears;
- same-pane language switch -> `*` disappears;
- New -> `*` disappears;
- later authoritative SMILE generation -> `*` disappears;
- unrelated pane operations -> `*` remains.

Keep the existing `Edited` status.

Do not build a separate saved/unsaved file-state model in this milestone.

---

# 16. Task 11 — Preserve Build & Run metadata semantics

Do not change the current INPUT metadata policy in this hardening release.

For an edited pane associated with current valid SMILE source:

```text
RequiresStandardInput
```

continues to come from that generated SMILE snapshot.

For a target-only edit after blank/invalid SMILE fallback, the current fallback remains noninteractive unless existing code already determines otherwise.

Do not add:

```text
Auto / Captured / Interactive
```

run-mode UI in v0.7.0.1.

That can be designed separately if target-only interactive experiments become a priority.

This keeps the hardening milestone focused.

---

# 17. Task 12 — Do not lose INPUT companion files

Regression coverage must continue proving that hand-edited primary source keeps the correct compiler-owned companion files.

At minimum verify this for:

- C# project file;
- COBOL ancillary helper when used;
- any other target whose INPUT lowering uses multiple generated files.

The new duplicate-pane builds must preserve the correct companions independently for each pane.

---

# 18. Task 13 — Regression tests for duplicate-language panes

Add focused Desktop tests.

Required cases:

## 18.1 Two C# panes with two different edits

Configure:

```text
Pane1 = C#
Pane2 = C#
```

Give them distinct source strings.

Execute global:

```text
Build & Run Visible Panes
```

Assert:

- C# toolchain BuildAndRun is called twice;
- first call receives Pane1 source;
- second call receives Pane2 source;
- calls occur in pane order;
- companion/project files are present;
- neither edit is replaced.

The fake toolchain may need to record a list of generated programs rather than only the last one.

## 18.2 Three identical target languages

Set all three panes to one language.

Require three build attempts when all are buildable.

## 18.3 Mixed duplicate languages

Example:

```text
Pane1 = C#
Pane2 = C#
Pane3 = Python
```

Require:

```text
2 C# runs
1 Python run
```

## 18.4 One duplicate pane unavailable

If two C# panes exist but only one is buildable because of its pane state, build only the valid pane.

Do not let one pane incorrectly suppress or authorize its sibling.

## 18.5 Cancellation

If cancellation occurs after Pane1:

- stop before later panes as current cancellation semantics require;
- do not lose their editor contents.

---

# 19. Task 14 — Regression tests for edit-during-debounce ordering

Add tests with controlled delays rather than relying only on `Task.Delay`.

Prefer an injected generation gate or test seam that allows the test to pause generation deterministically.

Required cases:

## 19.1 Target edit after generation starts

```text
SMILE edit
generation blocked
target edit
release generation
```

Assert target edit survives.

## 19.2 Sibling panes update

Under the same blocked generation:

- edit only Pane1;
- release;
- Pane1 keeps edit;
- Pane2 and Pane3 receive fresh generation.

## 19.3 Same-language sibling independence

Use Pane1 and Pane2 both C#.

Edit one while generation is blocked.

Assert only that pane preserves its edit.

## 19.4 Later SMILE edit reasserts authority

```text
target edit
SMILE edit
generation completes
```

Assert generated result replaces the older target edit.

## 19.5 Target edit after second SMILE edit

```text
target edit T1
SMILE edit B
generation B blocked
target edit T2
release B
```

Assert T2 survives.

This is the most important ordering regression.

## 19.6 Multiple target edits

Increment the per-pane edit revision more than once and prove only the latest text remains.

---

# 20. Task 15 — Regression tests for the edited marker

Test:

- untouched generated pane has no `*`;
- learner edit adds `*`;
- Save Source does not remove `*`;
- unrelated toolchain refresh does not remove `*`;
- unrelated sibling language switch does not remove `*`;
- successful build does not remove `*`;
- fresh authoritative generation removes `*`;
- same-pane language switch removes `*`;
- New removes `*`.

Also confirm the title remains correct after Maximize/Restore.

---

# 21. Task 16 — Preserve existing New behavior

Do not alter the recently hardened New command.

New must continue to:

- be synchronous and I/O-free;
- cancel pending live work;
- advance source revision even if already blank;
- clear the SMILE editor;
- clear all target editors;
- clear target edit markers;
- clear generated snapshots;
- clear file association;
- prevent delayed startup loading from repopulating the new document;
- leave target-only editing available afterward.

Existing New tests must remain green.

---

# 22. Task 17 — Preserve Maximize/Restore behavior

Do not recreate editors.

Maximize/Restore must continue to preserve:

- current target edit;
- edit marker;
- caret;
- selection;
- undo history;
- scroll position;
- zoom;
- selected language.

Add no hard dependency between maximize state and build state.

---

# 23. Task 18 — Preserve highlighting

No palette redesign is required.

Keep the current teaching palette:

```text
comments                green
keywords/instructions   blue
learner identifiers     black
strings                 red
numbers                 dark blue
```

Do not reintroduce purple-family colors.

Only adjust highlighting tests if the title/edit-state changes require UI test updates.

---

# 24. Task 19 — Documentation

Update only applicable documentation:

```text
README.md
docs/Architecture.md
docs/Roadmap.md
AGENTS.md
requirements/progress history when repository convention requires it
```

Document:

- every visible target pane is independently buildable;
- duplicate target languages are allowed and independently built;
- the global command acts on visible panes, not unique languages;
- a target-pane `*` means its source differs from current generated SMILE output;
- delayed older generation cannot overwrite a newer target edit;
- a later SMILE edit still intentionally replaces earlier target edits.

Do not modify official language specification `008` except to fix a genuinely incorrect link, typo, or factual statement unrelated to syntax.

This release adds no language semantics.

---

# 25. Task 20 — Version identity

Update the Desktop/release identity to:

```text
0.7.0.1 Target Editor Hardening
```

Update the applicable assembly/project metadata and About dialog.

Add a roadmap entry under:

```text
Implemented In v0.7.0.1
```

Do not change:

```text
v0.8.0 — Loops
v0.9.0 — Functions and scopes
```

as the next language-depth milestones.

---

# 26. Required normal validation

Run from the actual repository root.

Examples use `D:\SMILE`.

```bat
cmd /c "cd /d D:\SMILE && dotnet restore SMILE.sln"
```

```bat
cmd /c "cd /d D:\SMILE && dotnet build SMILE.sln -c Debug --no-restore -nologo"
```

```bat
cmd /c "cd /d D:\SMILE && dotnet test SMILE.sln -c Debug --no-build --no-restore -nologo"
```

```bat
cmd /c "cd /d D:\SMILE && dotnet build SMILE.sln -c Release --no-restore -nologo"
```

```bat
cmd /c "cd /d D:\SMILE && dotnet test SMILE.sln -c Release --no-build --no-restore -nologo"
```

Required:

- zero build warnings;
- zero build errors;
- zero test failures;
- zero unexpected skips.

---

# 27. Required focused Desktop validation

Run focused tests covering:

- `TargetPaneViewModel`;
- duplicate-language visible builds;
- per-pane edit revisions;
- edit-during-debounce races;
- target edit preservation;
- edited-marker title;
- New;
- Maximize/Restore;
- target-only build;
- INPUT metadata;
- build failure preservation;
- startup/toolchain races;
- syntax highlighting.

Report the exact focused test count.

---

# 28. Required strict all-ten-target validation

Even though this is Desktop hardening, Build & Run metadata and companion-file behavior are target-facing.

Run:

```bat
cmd /c "cd /d D:\SMILE && set SMILE_REQUIRE_JAVA=1 && set SMILE_REQUIRE_ALL_TARGETS=1 && set SMILE_REQUIRE_ZERO_TARGET_WARNINGS=1 && dotnet test SMILE.sln -c Debug --no-build --no-restore -nologo"
```

```bat
cmd /c "cd /d D:\SMILE && set SMILE_REQUIRE_JAVA=1 && set SMILE_REQUIRE_ALL_TARGETS=1 && set SMILE_REQUIRE_ZERO_TARGET_WARNINGS=1 && dotnet test SMILE.sln -c Release --no-build --no-restore -nologo"
```

Require:

- Java;
- all ten target toolchains;
- zero generated compiler warnings;
- exact evaluator conformance;
- existing INPUT scripted-stdin tests;
- no unexpected skips.

---

# 29. Manual Desktop smoke test

Launch the real WPF Desktop application.

Verify:

1. first paint remains responsive;
2. cumulative language source loads normally;
3. target panes remain editable;
4. edit Pane1 and confirm `*` appears;
5. Save Source and confirm `*` remains;
6. Build Pane1 and confirm its edited code is built;
7. select C# in Pane1 and Pane2;
8. make different edits in both;
9. execute `Build & Run Visible Panes`;
10. confirm both C# programs are independently processed;
11. edit SMILE, then quickly edit one target pane before generation completes;
12. confirm the newer target edit is not overwritten;
13. edit SMILE again and confirm that newer SMILE edit can intentionally replace the earlier target edit;
14. confirm sibling panes continue updating;
15. switch one edited pane to another language and confirm its old edit/marker is cleared;
16. confirm unrelated pane language switching preserves other edits;
17. confirm Maximize/Restore preserves edited source and `*`;
18. confirm New clears all four editors and all `*` markers;
19. confirm target-only editing after New still builds;
20. confirm an INPUT-generated program still launches the visible interactive console;
21. confirm About displays:

```text
0.7.0.1 Target Editor Hardening
```

---

# 30. Acceptance criteria

This task is complete only when all of the following are true.

## Duplicate-language builds

- global visible build no longer groups by target language;
- each visible buildable pane is processed independently;
- two C# panes with different edits both build;
- three identical-language panes can all build;
- output identifies pane plus language;
- build order is deterministic;
- cancellation remains correct.

## Edit ordering

- each target pane has a monotonically increasing user-edit revision;
- programmatic generated-code application does not increment it;
- a live generation request captures pane edit revisions;
- a target edit after request creation prevents that older result from replacing the pane;
- sibling panes still update;
- same-language sibling panes remain independent;
- a later SMILE edit still reasserts generated-source authority;
- a still-later target edit again wins over that pending generation.

## Edit marker

- `*` appears when a pane differs from generated SMILE source;
- it is not described as a saved/unsaved file marker;
- Save Source and Build & Run do not remove it;
- generated replacement, language switch, and New remove it.

## Existing behavior

- current INPUT semantics remain unchanged;
- current comments and blank-line preservation remain unchanged;
- current target companions remain intact;
- New remains race-safe;
- Maximize/Restore remains state-preserving;
- highlighting remains correct;
- direct target-only build after New remains supported;
- exact current-SHA CI completion gate remains mandatory.

## Release quality

- Debug build: zero warnings and zero errors;
- Release build: zero warnings and zero errors;
- normal Debug tests: zero failures;
- normal Release tests: zero failures;
- strict Debug: zero failures and zero unexpected skips;
- strict Release: zero failures and zero unexpected skips;
- all ten target toolchains required and executed;
- zero generated compiler warnings;
- About displays `0.7.0.1 Target Editor Hardening`;
- changes are committed and pushed;
- exact final `main` SHA has a successful `SMILE CI` run.

---

# 31. Commit message

Use a detailed public commit message similar to:

```text
Sin and Codex: Harden editable target panes

Release SMILE v0.7.0.1 Target Editor Hardening.

Treat each visible target pane as an independent editable build unit so duplicate target-language selections build their own current primary source rather than being collapsed by language. Identify build output by pane and language while retaining generated companion files and INPUT metadata.

Add per-pane learner-edit revisions to enforce latest-edit-wins behavior across debounced live transpilation. Prevent an older in-flight SMILE generation from replacing a target edit made after that request began, while preserving the rule that a later SMILE edit intentionally reasserts generated-source authority.

Mark panes that differ from generated SMILE source with an asterisk, preserve that state across save, build, toolchain refresh, unrelated pane changes, and Maximize/Restore, and clear it on authoritative regeneration, same-pane language switching, or New.

Validation: <insert exact focused, normal, strict, all-target, warning, and Desktop results>. Post-push SMILE CI: <insert exact final run ID and successful conclusion>.
```

Replace placeholders with actual results.

Commit all intended changes and push to `main`.

Do not create a Git tag or GitHub Release unless Sin explicitly asks or the repository later establishes that convention.

---

# 32. Mandatory post-push completion gate

After pushing:

1. obtain the exact final `main` SHA;
2. locate the `SMILE CI` run for that exact SHA;
3. wait for completion;
4. require conclusion `success`;
5. verify Restore, Debug Build/Test, and Release Build/Test all succeeded.

Do not use an older green run.

If CI fails:

- inspect the failed step and logs;
- fix the root cause;
- rerun applicable local validation;
- create a normal follow-up commit;
- push it;
- verify the replacement exact-SHA run.

Do not force-push or hide failed history.

---

# 33. Completion report to Sin

Report:

- final commit SHA;
- whether it was pushed;
- version identity;
- files changed;
- whether the global command was renamed;
- duplicate-language build behavior;
- exact per-pane edit-revision implementation;
- edit-during-debounce behavior;
- edited-marker behavior;
- companion-file preservation;
- INPUT metadata preservation;
- exact focused test count;
- exact Debug build result;
- exact Debug test count;
- exact Release build result;
- exact Release test count;
- exact strict Debug result;
- exact strict Release result;
- generated-warning result;
- manual Desktop smoke result;
- GitHub Actions run ID;
- GitHub Actions conclusion;
- whether a corrective follow-up commit was required;
- remaining known limitations.

Highlight these as ready for testing:

- **Independent duplicate-language pane builds**
- **Latest target edit wins over older live generation**
- **Later SMILE edits remain authoritative**
- **Target-pane edited `*` marker**
- **SMILE v0.7.0.1 Target Editor Hardening**

After this milestone is accepted, the next planned language-design task is:

> **SMILE v0.8.0 — WHILE loops**

with a new official language specification expected as:

```text
009 - SMILE - WHILE Statement Official Specification v1.0.md
```
