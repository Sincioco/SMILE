# Codex Implementation Instructions — SMILE Post-Push CI Completion Gate and Workflow Hardening

## Repository and workflow

- Repository: `Sincioco/SMILE`
- Work directly on `main`.
- Sin is the only developer.
- Do not create or suggest a feature branch.
- Do not open a pull request.
- Do not enable GitHub branch protection or required pull requests as part of this task.
- Re-read `AGENTS.md` before changing anything.
- Inspect the current `main` branch, latest commit, working tree, and current `SMILE CI` workflow before editing.
- Do not discard, reset, overwrite, or commit unrelated work.
- Do not force-push or rewrite published history.
- Follow KISS and KISS v2, “The Sin Way.”
- When the work is complete, commit all intended changes and push to `main`.

The reviewed baseline when this brief was prepared had:

```text
.github/workflows/smile-ci.yml
```

with a Windows `SMILE CI` workflow that restores, builds, and tests Debug and Release.

Do not assume the reviewed SHA is still current. Always begin from the newest `main`.

---

# 1. Objective

Make two small but permanent improvements to SMILE’s CI process:

1. **Post-push CI completion gate**

   SMILE intentionally allows Codex to work directly on `main`. Because `main` is not protected, CI is reactive: a pushed commit reaches the public branch before GitHub Actions validates it.

   Preserve the direct-to-`main` workflow, but make the Codex task-completion rule explicit:

   > A task that pushes to `main` is not complete until the `SMILE CI` run for the newest pushed commit finishes successfully.

2. **CI security and reliability polish**

   Add:

   ```yaml
   permissions:
     contents: read
   ```

   and a job timeout:

   ```yaml
   timeout-minutes: 30
   ```

This is a repository-process hardening task. It must not change the SMILE language, compiler, generated code, evaluator, Desktop behavior, target toolchains, or release version.

---

# 2. Non-goals

Do not:

- enable branch protection;
- require pull requests;
- create a feature branch;
- change the direct-to-`main` workflow;
- add a merge queue;
- add approval requirements;
- add another CI provider;
- add a new package or dependency;
- add a new script unless it is genuinely necessary;
- change the .NET SDK version;
- change the Windows runner;
- alter the existing Debug or Release build/test commands;
- add all ten destination-language toolchains to hosted CI;
- change SMILE source code;
- change language specifications;
- bump the SMILE version;
- create a Git tag or GitHub Release.

---

# 3. Task 1 — Add the permanent post-push CI completion rule

Update `AGENTS.md` with a permanent rule for every Codex task that pushes to `main`.

Use wording equivalent to:

> **Post-push CI verification:** After pushing to `main`, Codex must identify the newest pushed commit SHA and inspect the `SMILE CI` GitHub Actions run for that exact SHA. The task is not complete until that run finishes with a successful conclusion. If the run fails, Codex must inspect the failed step and logs, fix the root cause, rerun all applicable local validation, create a normal follow-up commit, push it, and confirm that the replacement `SMILE CI` run is successful. Never force-push or rewrite published `main` history merely to remove a failed run.

Also add these rules:

- The CI run being checked must match the exact current `main` commit SHA.
- Do not treat an older green run as validation for a newer commit.
- Do not declare a task complete while CI is queued or in progress.
- Do not declare success when the workflow was skipped, cancelled, timed out, or concluded neutral.
- Do not hide or delete a failed run.
- When fixing a CI-only portability problem, correct the underlying test or workflow problem rather than weakening production behavior.
- After a CI failure, use a normal follow-up commit. Do not amend or force-push a commit that is already public.
- The completion report must include:
  - final commit SHA;
  - CI workflow name;
  - CI run conclusion;
  - whether the first run passed or required a follow-up fix;
  - final run URL or run ID when available.

## 3.1 Recommended verification method

Prefer the connected GitHub tools when they expose the workflow run and job result for the exact SHA.

When using GitHub CLI, commands may follow this pattern:

```bat
cmd /c "cd /d D:\SMILE && git rev-parse HEAD"
```

```bat
cmd /c "cd /d D:\SMILE && gh run list --workflow \"SMILE CI\" --branch main --limit 10"
```

Then inspect the run whose `headSha` exactly matches `git rev-parse HEAD`.

To follow that run to completion:

```bat
cmd /c "cd /d D:\SMILE && gh run watch <RUN_ID> --exit-status"
```

If `gh` is unavailable, use the connected GitHub application or GitHub web/API tooling available in the Codex environment.

Do not add a repository dependency merely to monitor CI.

## 3.2 Failure handling

When CI fails:

1. Identify the failed job and step.
2. Read the relevant log output.
3. Determine whether the problem is:
   - a production defect;
   - a test defect;
   - an environment-portability defect;
   - a workflow defect;
   - a transient GitHub infrastructure problem.
4. Fix the actual root cause.
5. Run the applicable local build and tests.
6. Commit the fix as a normal follow-up commit.
7. Push to `main`.
8. Confirm that the new commit’s own `SMILE CI` run succeeds.

A successful rerun of an old failed commit is useful diagnostically, but the completion gate should normally be satisfied by a green run attached to the current `main` SHA.

If GitHub Actions is experiencing a confirmed service outage, report that accurately and do not claim the task has passed the CI completion gate.

---

# 4. Task 2 — Add explicit minimum workflow permissions

Edit:

```text
.github/workflows/smile-ci.yml
```

Add the following at the workflow level:

```yaml
permissions:
  contents: read
```

Recommended placement:

```yaml
name: SMILE CI

permissions:
  contents: read

on:
  ...
```

This workflow only needs to check out and read repository contents. It should not receive unnecessary write permissions.

Do not add:

```yaml
contents: write
```

Do not add write permission for:

- actions;
- checks;
- pull requests;
- issues;
- packages;
- deployments;
- security events.

If a future workflow genuinely needs a write permission, that permission must be added deliberately in that future task and scoped as narrowly as possible.

---

# 5. Task 3 — Add a job timeout

In the existing `build-and-test` job, add:

```yaml
timeout-minutes: 30
```

Recommended form:

```yaml
jobs:
  build-and-test:
    name: Build and test
    runs-on: windows-latest
    timeout-minutes: 30
```

The timeout belongs at the job level.

Do not add separate arbitrary timeouts to each step.

Do not shorten the timeout below 30 minutes without evidence that the full Debug and Release workflow consistently completes with a comfortable margin.

The existing workflow should continue to run:

1. checkout;
2. .NET setup;
3. restore;
4. Debug build;
5. Debug tests;
6. Release build;
7. Release tests.

Do not change their order.

---

# 6. Task 4 — Document hosted CI versus local release validation

Update the appropriate project documentation, preferably `README.md` and/or `docs/Toolchains.md`, with a short clarification:

- `SMILE CI` is the mandatory hosted verification after every push to `main`.
- A pushed task is not complete until the current commit’s hosted CI run is green.
- Hosted CI validates the Windows .NET solution and the tests available on the hosted runner.
- Hosted CI does not replace strict local release validation requiring:
  - Java;
  - all ten destination toolchains;
  - zero generated compiler warnings;
  - evaluator-versus-target conformance.

Keep this documentation concise. Do not duplicate the entire `AGENTS.md` procedure in multiple files.

---

# 7. Workflow shape after the change

The relevant workflow structure should be equivalent to:

```yaml
name: SMILE CI

permissions:
  contents: read

on:
  push:
    branches:
      - main
  pull_request:
    branches:
      - main
  workflow_dispatch:

jobs:
  build-and-test:
    name: Build and test
    runs-on: windows-latest
    timeout-minutes: 30

    steps:
      - name: Check out repository
        uses: actions/checkout@v6

      - name: Set up .NET SDK
        uses: actions/setup-dotnet@v5
        with:
          dotnet-version: 10.0.302

      - name: Restore
        run: dotnet restore SMILE.sln

      - name: Build Debug
        run: dotnet build SMILE.sln -c Debug --no-restore -nologo

      - name: Test Debug
        run: dotnet test SMILE.sln -c Debug --no-build --no-restore -nologo

      - name: Build Release
        run: dotnet build SMILE.sln -c Release --no-restore -nologo

      - name: Test Release
        run: dotnet test SMILE.sln -c Release --no-build --no-restore -nologo
```

Preserve the actual stable action versions and SDK version already approved in the current repository unless there is a separately documented reason to update them.

Do not change unrelated YAML formatting merely for style.

---

# 8. Validation before commit

Because this task changes process documentation and CI YAML rather than compiler behavior, do not invent new SMILE language tests.

Still run the normal local solution validation before committing.

From the actual repository root:

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

Required result:

- Debug build: zero errors;
- Debug tests: zero failures;
- Release build: zero errors;
- Release tests: zero failures.

Preserve the repository’s existing warning standards.

A strict all-ten-target run is not required solely because two YAML keys and process documentation changed, unless another modified file or repository rule requires it.

---

# 9. Commit and push

Review the complete diff before committing.

The expected changed files should be limited to items such as:

```text
.github/workflows/smile-ci.yml
AGENTS.md
README.md
docs/Toolchains.md
Requirements/progress history, when the repository convention requires it
```

No compiler or language source file should change.

Use a detailed commit message such as:

```text
Sin and Codex: Require green post-push CI

Make successful SMILE CI validation part of the permanent completion rule for every task pushed directly to main. Require Codex to verify the workflow run for the exact current commit SHA, fix failures with normal follow-up commits, and report the final run result without force-pushing published history.

Restrict the hosted workflow token to read-only repository contents and add a 30-minute build-and-test job timeout. Preserve the existing Windows runner, .NET SDK, Debug and Release commands, direct-to-main workflow, and separate strict local all-ten-target release validation.

Validation: <insert exact local Debug and Release build/test results>. Post-push SMILE CI: <insert exact final run ID and successful conclusion>.
```

Replace all placeholders with the exact results.

Push the commit to `main`.

---

# 10. Mandatory post-push verification for this task

This task must be the first task completed under the new rule.

After pushing:

1. Read the exact final commit SHA:

   ```bat
   cmd /c "cd /d D:\SMILE && git rev-parse HEAD"
   ```

2. Find the `SMILE CI` run for that exact SHA.
3. Confirm:
   - workflow name is `SMILE CI`;
   - event is the expected push event;
   - head branch is `main`;
   - head SHA matches exactly;
   - status is `completed`;
   - conclusion is `success`;
   - restore, Debug build/test, and Release build/test all succeeded.

If that run fails, follow the failure procedure in this brief and push a normal corrective commit. The task remains incomplete until the newest `main` commit has its own successful run.

---

# 11. Acceptance criteria

This task is complete only when all of the following are true:

## Process rule

- `AGENTS.md` says a push-to-main task is not complete until the exact current commit’s `SMILE CI` run succeeds.
- It prohibits using an older run as evidence.
- It requires normal follow-up commits after failure.
- It prohibits force-pushing or rewriting public `main` history to hide a failure.
- It requires the final CI result in the completion report.

## Workflow security

- The workflow contains:

  ```yaml
  permissions:
    contents: read
  ```

- No unnecessary write permission was added.

## Workflow reliability

- The `build-and-test` job contains:

  ```yaml
  timeout-minutes: 30
  ```

- Existing triggers, runner, SDK, steps, and commands remain intact.

## Documentation

- Hosted post-push CI and strict local release validation are clearly distinguished.
- No SMILE language or version documentation was changed unnecessarily.

## Validation

- Local Debug build and tests pass.
- Local Release build and tests pass.
- Changes are committed and pushed to `main`.
- The final commit SHA has a completed, successful `SMILE CI` run.

---

# 12. Completion report to Sin

Report:

- final commit SHA;
- files changed;
- exact `permissions` setting;
- exact timeout setting;
- local Debug build result;
- local Debug test count;
- local Release build result;
- local Release test count;
- GitHub Actions run ID;
- GitHub Actions conclusion;
- whether the first run passed or required a corrective follow-up commit;
- confirmation that the validated workflow SHA matches the final `main` SHA.

Highlight these as ready for testing:

- **Post-push CI completion gate**
- **Read-only GitHub Actions permissions**
- **30-minute CI job timeout**
- **Green CI verification for the final `main` commit**
