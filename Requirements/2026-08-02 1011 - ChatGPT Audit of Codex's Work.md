# SMILE v0.1.3 — Responsive Live Transpilation and Hardening

## Codex implementation instructions

Use this document as the implementation brief for the next SMILE hardening release.

**Highest priority:** eliminate the typing delay in the WPF SMILE editor immediately.

Do not stop after writing another plan. Inspect the current repository, preserve all intentional work, implement the fixes, update tests and documentation, validate the application on the Windows VM, and provide a detailed completion report.

This prompt explicitly authorizes Codex to:

- Create a feature branch.
- Modify the repository.
- Run all builds, tests, compiler checks, and WPF validation locally on Sin's Windows VM.
- Update the living documentation.
- Commit the completed work using the required `Sin and Codex:` prefix.
- Push the completed feature branch after all local validation is green.

Do not add GitHub Actions, cloud CI, hosted runners, self-hosted Actions runners, or any other remote build/test automation. Sin is currently the only developer and wants all development and validation performed locally on the VM.

Do not merge the branch into `main` automatically.

---

# 1. Repository and reviewed baseline

- Repository: `https://github.com/Sincioco/SMILE.git`
- Expected local folder: `C:\SMILE`
- Reviewed public commit: `97c9a5a0b1ef8161309b88675ee219fd9f4b5b92`
- Reviewed version: `0.1.2 PRINT Everywhere`
- Primary environment: Windows VM
- Primary IDE: Visual Studio 2026 Enterprise
- Framework: .NET 10 and WPF
- License: `AGPL-3.0-only`

The reviewed release successfully added:

- C#, C, Windows x64 MASM, JavaScript, and Java generation.
- Objective-C and Swift transpile-only targets.
- A four-quadrant WPF desktop interface.
- Local build/run toolchains.
- A press-any-key launcher.
- Generated-workspace inspection.
- Expanded MASM educational comments.
- Updated README documentation and a current screenshot.

The earlier screenshot/source mismatch is resolved. The current public XAML contains the File, Options, and Help menus, Open Generated Folder option, Press Any Key Launcher option, and v0.1.2 title shown by the latest work.

Do not remove these working features.

---

# 2. Why the live editor is slow

The current `SourceText` setter calls `TranspileAll()` synchronously for every character typed.

`TranspileAll()` currently:

1. Parses the entire source.
2. Generates all targets in `TargetLanguageInfo.All`.
3. `TargetLanguageInfo.All` now contains seven languages.
4. Rebuilds the generated-program dictionary.
5. Updates all three visible generated-code text boxes.
6. Rewrites the diagnostics/output text.
7. Triggers output auto-scroll.
8. Recalculates command state.

The source binding uses:

```xml
UpdateSourceTrigger=PropertyChanged
```

Therefore, all of this work runs once per keystroke on the WPF UI thread.

This is no longer theoretical. Sin has already observed visible input delay in the Windows VM with only three `PRINT` statements.

The old source comment saying that live transpilation does not create noticeable UI work is now demonstrably false and must be removed or rewritten.

---

# 3. Governing principles

KISS and KISS v2, “The Sin Way,” continue to govern this work.

## KISS

Use the smallest complete fix.

Do not add:

- Reactive Extensions.
- ReactiveUI.
- A third-party debounce package.
- A third-party MVVM package.
- A background-worker framework.
- A new service framework.
- A new project solely for live transpilation.
- A plugin architecture.
- A message bus.
- An event aggregator.
- Unnecessary interfaces, factories, or layers.

A `CancellationTokenSource`, `Task.Delay`, `Task.Run`, a source revision number, and a few focused methods are enough.

## KISS v2

User-experience performance is the first priority.

Typing must feel immediate even when:

- The VM is slow.
- The source has many `PRINT` lines.
- MASM is one of the visible targets.
- The user types quickly.
- The user pastes a larger program.
- Objective-C and Swift are available in the target list.

The UI thread must never parse or generate code synchronously as a side effect of a keystroke.

---

# 4. Git workflow

Begin by inspecting the actual working tree:

```bat
cmd /c cd /d C:\SMILE && git status --short --branch
cmd /c cd /d C:\SMILE && git remote -v
cmd /c cd /d C:\SMILE && git log --oneline --decorate -10
```

Do not discard, reset, clean, overwrite, or revert user work.

When the working tree is safe:

```bat
cmd /c cd /d C:\SMILE && git fetch origin
cmd /c cd /d C:\SMILE && git switch main
cmd /c cd /d C:\SMILE && git pull --ff-only origin main
cmd /c cd /d C:\SMILE && git switch -c feature/v0.1.3-responsive-live-transpilation
```

If that branch already exists, inspect and continue it instead of creating a conflicting branch.

---

# 5. Release goal

Create:

> **SMILE v0.1.3 — Responsive PRINT Everywhere**

This is a hardening release.

Do not add `LET`, variables, expressions, `INPUT`, conditions, loops, functions, or other new SMILE language features in this change.

---

# 6. P0 — Replace synchronous per-keystroke transpilation

This is the first task and must be completed before the lower-priority fixes.

## 6.1 Required behavior

Automatic live transpilation must be:

- Debounced.
- Asynchronous.
- Performed away from the WPF UI thread.
- Cancellable.
- Latest-source-wins.
- Limited to the languages currently visible in the three generated panes.
- Safe against stale results.
- Safe when a target selector changes.
- Safe when the user immediately clicks Build & Run.
- Free of unobserved task exceptions.

Use a debounce delay of approximately:

```csharp
TimeSpan.FromMilliseconds(250)
```

A value from 200–300 milliseconds is acceptable after testing on the VM. Document the final value in code as one named constant.

## 6.2 The `SourceText` setter must become cheap

The setter may:

- Store the new text.
- Increment a source revision.
- Mark generated output as pending/stale.
- Cancel the previously scheduled live transpilation.
- Schedule a new live transpilation.

It must not:

- Parse.
- Generate any target language.
- Rewrite all generated panes.
- Rewrite the build log.
- Enumerate toolchains.
- Perform disk I/O.
- Block.
- Wait synchronously.

Conceptually:

```csharp
public string SourceText
{
    get => _sourceText;
    set
    {
        if (!SetProperty(ref _sourceText, value))
        {
            return;
        }

        _sourceRevision++;
        MarkVisibleOutputPending();
        ScheduleLiveTranspilation();
    }
}
```

Do not copy this blindly; integrate it cleanly with the existing view model.

## 6.3 Minimal state

A simple implementation may use fields similar to:

```csharp
private static readonly TimeSpan LiveTranspileDelay =
    TimeSpan.FromMilliseconds(250);

private CancellationTokenSource? _liveTranspileCancellation;
private Task? _liveTranspileTask;
private long _sourceRevision;
private bool _outputShowsLiveDiagnostics;
```

Track the source revision associated with every generated program.

A small nested record is acceptable:

```csharp
private sealed record GeneratedSnapshot(
    long SourceRevision,
    GeneratedProgram Program);
```

Then cache:

```csharp
Dictionary<TargetLanguage, GeneratedSnapshot>
```

Do not use generated code whose revision does not equal the current source revision.

## 6.4 Debounce and background generation

Use this general flow:

1. Capture the source text.
2. Capture its revision.
3. Capture the distinct languages visible in the three target panes.
4. Cancel the previous pending live operation.
5. Await the debounce delay.
6. Run synchronous parser/generator CPU work with `Task.Run`.
7. Return to the UI context.
8. Check cancellation and source revision again.
9. Apply results only when they are still current.

Illustrative structure:

```csharp
private async Task RunLiveTranspilationAsync(
    string sourceSnapshot,
    long sourceRevision,
    IReadOnlyList<TargetLanguage> languages,
    CancellationToken cancellationToken)
{
    try
    {
        await Task.Delay(LiveTranspileDelay, cancellationToken);

        IReadOnlyList<TranspileResult> results = await Task.Run(
            () => _transpiler.TranspileMany(sourceSnapshot, languages),
            cancellationToken);

        if (cancellationToken.IsCancellationRequested ||
            sourceRevision != _sourceRevision)
        {
            return;
        }

        ApplyTranspileResults(sourceRevision, results, isLive: true);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        // A newer source revision replaced this request.
    }
    catch (Exception ex)
    {
        HandleUiError("Live transpilation", ex);
    }
}
```

The exact implementation may differ, but the behavior must not.

Do not use `.Wait()`, `.Result`, `Thread.Sleep`, or synchronous dispatcher waits.

## 6.5 Generate only visible targets during live typing

Automatic live preview should generate only:

```csharp
Panes
    .Select(pane => pane.Language)
    .Distinct()
```

There are only three visible output panes.

Do not generate all seven targets after every typing pause.

The manual **Transpile All** command must still generate every target in:

```csharp
TargetLanguageInfo.All
```

This preserves the meaning of the button while making live typing much faster.

## 6.6 Make Transpile All asynchronous

Convert the current synchronous `RelayCommand` for Transpile All into an asynchronous command.

Manual Transpile All must:

- Cancel a pending live transpilation.
- Capture the current source/revision.
- Generate all targets with CPU work off the UI thread.
- Apply only results for the captured current revision.
- Show a clear status.
- Support cancellation where practical.
- Never freeze the window.

## 6.7 Target-language selector changes

When a user switches a generated pane to a language that has not been generated for the current source revision:

- Show `Updating...`.
- Generate the newly visible distinct target set immediately or with a very short debounce.
- Perform generation off the UI thread.
- Apply only current-revision results.
- Do not generate unrelated hidden targets.

When a valid current-revision cached result already exists, display it immediately.

## 6.8 Latest result must always win

This sequence must never display stale code:

1. User types `PRINT "A"`.
2. Live generation starts.
3. User quickly changes it to `PRINT "B"`.
4. The older `"A"` task finishes after the newer task.

The displayed generated code must remain `"B"`.

Use both cancellation and source-revision comparison. Cancellation alone is insufficient because synchronous generator work may finish after cancellation was requested.

## 6.9 Build & Run must never use stale generated code

Before building a pane, confirm that its generated program belongs to the current source revision.

If not, asynchronously generate that target from a captured current source snapshot before starting its toolchain.

Replace the current synchronous fallback:

```csharp
TranspileAll();
```

inside `BuildRunPaneCoreAsync`.

Build & Run must never:

- Compile source from an older revision.
- Trigger synchronous all-target generation on the UI thread.
- Build while current source has syntax errors.

## 6.10 Reduce automatic output churn

Automatic successful live transpilation must not repeatedly replace the build/program output with:

```text
Transpilation completed.
```

That causes unnecessary text rendering and `ScrollToEnd()` activity and can erase useful build logs.

Use this simple behavior:

- On successful automatic live transpilation, update pane code and status only.
- Do not touch `OutputText` unless it currently contains diagnostics owned by live transpilation.
- On a live syntax error, display the current diagnostics and mark that output as live diagnostics.
- When that syntax error is fixed, clear only the live-diagnostics output.
- Never erase existing build/run history merely because the user typed another character.
- Manual Transpile All may explicitly report completion.

A single Boolean such as `_outputShowsLiveDiagnostics` is enough. Do not build a logging framework.

## 6.11 Pending visual state

While waiting for the debounce or generating:

- Keep the old generated source visible to avoid flicker.
- Mark panes `Updating...` or `Pending...`.
- Disable Build & Run for stale output.
- Disable Copy/Save only when the visible source is stale.
- Do not clear and rewrite large generated text boxes on every keystroke.

When results arrive:

- Replace code once.
- Set `Ready` on success.
- Set `Syntax error` on failure.
- Re-enable valid commands.

## 6.12 Stable UI during Build & Run

Prevent state-changing conflicts while a build/run operation is active:

- Bind the SMILE source text box `IsReadOnly` to `IsBusy`.
- Keep the source box selectable and scrollable.
- Disable target-language selectors while `IsBusy`.
- Keep generated text boxes readable and scrollable.
- The user must still be able to move, resize, and scroll the window and click Cancel.

Add a simple pane property such as:

```csharp
public bool CanChangeLanguage => !IsBusy;
```

Bind the ComboBox to it.

The current code allows a pane language to be changed during an active build, which can cause the result status for one language to be displayed on a pane that has changed to another language.

---

# 7. Live-transpilation acceptance criteria

The fix is not complete until all of these are manually verified on the same Windows VM where the lag was observed.

## Rapid typing

Type quickly:

```basic
PRINT "Line 1"
PRINT "Line 2"
PRINT "Line 3"
PRINT "Line 4"
PRINT "Line 5"
```

Expected:

- Characters appear immediately.
- The caret follows typing without visible hesitation.
- There is no pause on each key.
- Generation begins only after typing pauses.
- Generated panes update together once the current revision completes.

## Larger paste

Paste at least 100 valid `PRINT` lines.

Expected:

- The paste appears immediately.
- The window remains movable and resizable.
- The generated output appears after background work finishes.
- No stale result replaces the latest source.

## Latest-wins race

Rapidly change:

```basic
PRINT "A"
```

to:

```basic
PRINT "B"
```

Expected:

- No pane ends with `"A"` after `"B"` is current.

## Syntax recovery

Type an incomplete statement:

```basic
PRINT "Unclosed
```

Pause, then fix it.

Expected:

- Diagnostics appear after the debounce.
- Diagnostics clear when fixed.
- Existing build/run output is not destroyed.
- The editor remains responsive.

## Target switch

Switch a pane to Objective-C or Swift.

Expected:

- Only the newly required visible output is generated.
- The selector does not freeze the window.
- The target pane updates for the current source revision.

## Build-current-source

Edit a line and immediately click Build & Run.

Expected:

- The compiled program uses the edited current source.
- No stale generated source is built.

---

# 8. P1 — Prevent command and initialization exceptions from closing WPF

The current `AsyncRelayCommand.Execute` has a `finally` block but no `catch`. Exceptions from Open, Save, Save As, Save Generated Source, or another async command can escape an `async void` command boundary and close the application.

Fix this without adding a framework.

## 8.1 Async command error handler

Give `AsyncRelayCommand` an optional error handler, for example:

```csharp
private readonly Func<Exception, Task>? _onError;
```

Catch expected command exceptions:

```csharp
try
{
    await _execute();
}
catch (OperationCanceledException)
{
    // Treat expected cancellation separately.
}
catch (Exception ex)
{
    if (_onError is not null)
    {
        await _onError(ex);
    }
}
finally
{
    _isRunning = false;
    RaiseCanExecuteChanged();
}
```

Ensure an exception in the error handler itself does not create another unhandled `async void` exception.

## 8.2 Cover all UI operations

Friendly error handling is required for:

- Open.
- Save.
- Save As.
- Save Generated Source.
- Clipboard Copy.
- Folder opening/Explorer activation.
- Toolchain initialization.
- Live transpilation.
- Manual Transpile All.

Show:

- The failed operation.
- A short useful message.
- A stable final UI state.

Do not show raw stack traces to ordinary users. Preserve details for debugging where appropriate.

## 8.3 Loaded initialization

The current async `Loaded` event must not allow `InitializeAsync()` exceptions to escape.

Handle initialization failure and leave SMILE usable where possible.

## 8.4 Tests

Add focused tests where practical:

- An async command that throws invokes the error handler.
- The command returns to an executable state.
- Cancellation is not reported as an unexpected crash.

Do not create a new test framework.

---

# 9. P1 — Separate build timeout from program timeout

The current code uses the 10-second `ProgramTimeout` for compilation, assembly, linking, and execution.

That is too short for a slower VM or a first .NET/JDK build.

Use:

```csharp
public static readonly TimeSpan DetectionTimeout =
    TimeSpan.FromSeconds(5);

public static readonly TimeSpan BuildTimeout =
    TimeSpan.FromSeconds(120);

public static readonly TimeSpan ProgramTimeout =
    TimeSpan.FromSeconds(10);
```

Use `BuildTimeout` for:

- `dotnet build`
- `cl.exe`
- `ml64`
- `link.exe`
- `javac`

Use `ProgramTimeout` for:

- The generated C# program.
- The generated C program.
- The generated MASM program.
- Node.js execution.
- Java execution.

Keep cancellation available throughout.

For C#, after a successful build, prefer running the generated executable directly:

```text
bin\Debug\net10.0\GeneratedProgram.exe
```

This matches the generated launcher and avoids the additional `dotnet run --no-build` orchestration overhead.

---

# 10. P1 — Move old-workspace cleanup away from the UI thread

`WriteGeneratedProgramAsync()` currently calls synchronous recursive cleanup before its first asynchronous file write.

Move workspace enumeration/deletion off the UI thread.

Keep it simple:

- Run cleanup with `Task.Run`.
- Run it at most once per SMILE process/session.
- Check cancellation between directories where practical.
- Preserve the one-day retention rule.
- Preserve the strict SMILE-owned-root safety check.
- Swallow only expected file-in-use and access exceptions.
- Do not delete outside `%TEMP%\SMILE\Runs`.

Do not scan the entire temp drive.

Add or update tests for safe-root behavior when practical.

---

# 11. P1 — Fix Build & Run Visible Languages with transpile-only targets

Objective-C and Swift are now visible targets but have no Windows Build & Run toolchain.

The current visible-languages loop calls `BuildRunPaneCoreAsync` for every distinct visible language, even when the pane is transpile-only.

Required behavior:

- Build each distinct visible language only once.
- Build only targets with an available toolchain.
- Skip Objective-C and Swift on Windows.
- Report a concise message such as:

```text
Skipped Objective-C: transpile-only on Windows.
```

- Do not report this as an unexpected failure.
- Continue building the remaining supported visible languages.
- Keep the main button disabled when no visible target can build.

Compute the buildable target list before setting all panes busy, or expose a capability property independent of the transient `IsBusy` flag.

Do not call `pane.CanBuild` after `IsBusy` has already made it false.

Also update the per-pane button text:

- JavaScript: `Run`
- Objective-C: `Transpile Only`
- Swift: `Transpile Only`
- Other compiled targets: `Build & Run`

The transpile-only buttons remain disabled for local execution.

---

# 12. P1 — Correct final statuses and duration reporting

The current final pane status can remain `Building`, `Assembling`, or `Linking` after failure, and a timed-out run can be reset to `Ready`.

Use clear final states:

```text
Ready
Updating
Building
Assembling
Linking
Running
Completed
Failed
Cancelled
Timed Out
Transpile Only
Toolchain Missing
Syntax Error
```

Final state mapping must prioritize:

1. Cancelled
2. Timed Out
3. Success
4. Failure with stage

For example:

```text
Failed — Building
Failed — Linking
```

Do not reset a timed-out or failed result to `Ready` in `finally`.

## Duration

The successful `Duration` currently represents only the final run process.

Change it to represent total build-and-run duration:

- C#: build + run
- C: compile + run
- MASM: assemble + link + run
- Java: compile + run
- JavaScript: run only

Label it:

```text
Total duration:
```

Detection time may remain separate/excluded.

Update tests and documentation.

---

# 13. P1 — Make target escaping truly target-specific

The public methods now have target-specific names, but C#, C, Objective-C, JavaScript, and Java still call the same internal escape routine.

That routine emits:

```text
\a
\v
```

Those are not valid Java string escapes. JavaScript also does not preserve every C-style escape with the same semantics.

Required changes:

## Java

Use only legal Java escapes.

At minimum:

- `\\`
- `\"`
- `\b`
- `\t`
- `\n`
- `\f`
- `\r`
- Valid fixed octal escapes for NUL, bell, vertical tab, and other low control characters where needed.

Do not emit Java `\a` or `\v`.

Compile Java with explicit UTF-8 source encoding:

```bat
cmd /c javac -encoding UTF-8 Program.java
```

## JavaScript

Use legal JavaScript escapes.

For control characters without a standard short escape, use fixed Unicode escapes such as:

```text
\u0007
\u000b
\u0000
```

Do not rely on a backslash followed by an unrecognized character.

## C and Objective-C

C-style escapes are valid, but prevent ambiguous escape continuation, such as a NUL escape immediately followed by a digit.

Fixed-width octal escapes are acceptable.

## C#

Use legal C# escapes.

## Swift

The existing `\u{...}` approach may remain if tests confirm it.

## Tests

Add exact generator tests for:

- Backslash.
- Tab.
- NUL.
- Bell.
- Vertical tab.
- A Unicode character.
- A control character followed by a digit.
- Deterministic output.

Because a JDK is now installed on the development VM, compile the generated Java control-character test program and prove that `javac` accepts it.

---

# 14. P2 — Make parser behavior match the documented whitespace grammar

The documented grammar says:

```text
print-statement -> PRINT whitespace+ string-literal
```

The lexer currently skips whitespace, so this is accepted even though the specification says it should not be:

```basic
PRINT"Hello"
```

For beginner readability, enforce at least one space or tab.

This can be done simply by comparing spans:

```text
string token start > PRINT keyword end
```

No whitespace-token hierarchy is required.

Add a stable diagnostic, preferably:

```text
SMILE1006
PRINT requires a space or tab before its quoted string.
```

Report it at the position immediately after `PRINT`.

Add tests for:

```basic
PRINT"Hello"
PRINT "Hello"
PRINT     "Hello"
PRINT	"Hello"
```

Update:

- README
- Language specification
- Diagnostic documentation

---

# 15. P2 — Make Objective-C PRINT produce plain stdout

The current Objective-C target uses `NSLog`.

`NSLog` is a logging API and normally adds metadata rather than producing the same plain console output as the other PRINT targets.

Generate normal stdout instead while still showing real Objective-C syntax.

Recommended KISS output:

```objc
#import <Foundation/Foundation.h>
#include <stdio.h>

int main(void)
{
    @autoreleasepool
    {
        puts([@"Hello from SMILE!" UTF8String]);
        puts([@"Different syntax, same idea." UTF8String]);
    }

    return 0;
}
```

This:

- Uses an Objective-C string literal.
- Uses an Objective-C message send.
- Writes plain text to stdout.
- Appends one newline.
- Avoids logging timestamps and process metadata.
- Remains simple for students.

Update golden tests and README generated examples.

Objective-C remains transpile-only on Windows.

---

# 16. P2 — Keep all validation local to Sin's Windows VM

SMILE currently has one developer. All development, builds, tests, target compiler checks, performance checks, and WPF validation must be performed locally on Sin's Windows VM.

For this release:

- Do not add a `.github/workflows` directory.
- Do not add GitHub Actions.
- Do not use GitHub-hosted runners.
- Do not configure a self-hosted Actions runner.
- Do not add another CI/CD service.
- Do not make a cloud CI result part of the definition of done.
- Do not upload build artifacts merely for automated validation.

Use the local validation commands in this document.

Before committing and pushing:

1. Run Debug restore, build, and tests locally.
2. Run Release build and tests locally.
3. Run every installed target compiler/runtime locally.
4. Run the WPF responsiveness tests locally on the same VM where the typing lag was observed.
5. Record the actual local results in the commit message and final Codex report.
6. Do not claim a target or user-interface behavior was validated unless it was actually exercised locally.

This local-only policy is intentional, not a missing task.

---

# 17. Small user-experience corrections

Apply these only with simple changes.

## Copy and Save Source during build

Reading/copying already generated read-only source does not conflict with a build.

Prefer:

```csharp
CanUseSource =>
    HasValidSource &&
    !string.IsNullOrWhiteSpace(GeneratedCode);
```

Then apply `!IsBusy` only to Build & Run and target selection.

Do not disable harmless reading/copying merely because a compiler is running.

## Explorer opening

Explorer activation currently performs COM enumeration synchronously.

At minimum:

- Catch failures so they cannot close SMILE.
- Do not let Explorer activation block completion-state cleanup.
- Keep the application usable if Explorer cannot be opened or focused.

Do not create a complex Explorer-management subsystem.

## Pane title

If easy, make each pane heading reflect its selected target instead of only:

```text
Generated target 1
Generated target 2
Generated target 3
```

For example:

```text
Generated target 1 — C#
```

This is optional and must not delay the responsiveness fix.

---

# 18. Tests

Run and preserve all existing tests.

Add focused coverage for the defects fixed in this release.

## Required automated tests

- Java no longer emits illegal `\a` or `\v`.
- Java control-character output compiles with installed `javac`.
- JavaScript control characters are preserved with valid escapes.
- `PRINT"Hello"` reports the whitespace diagnostic.
- `PRINT "Hello"` and tab-separated PRINT remain valid.
- Build timeout and program timeout use the correct values.
- Async command exceptions are handled and command state resets.
- Build & Run Visible skips transpile-only targets.
- Final status mapping covers success, failure, cancellation, and timeout.
- Total duration includes build stages.
- Objective-C uses plain stdout generation.
- Existing seven-target deterministic output tests remain green.
- Press-any-key launchers remain green.

## Live-transpilation tests

Do not add a new test project or framework solely for this.

When the debounce/latest-wins logic is extracted into a small pure helper that can be tested from the existing test project without distorting the architecture, add tests for:

- Multiple rapid schedules execute/apply only the latest.
- A stale result is rejected.
- Cancellation is expected and silent.
- Visible target selection is distinct.

If doing that would require a disproportionate project restructure, keep the implementation small and document the complete manual VM validation instead.

Do not add timing-sensitive automated assertions such as “must finish in 10 ms.” Those are brittle.

---

# 19. WPF manual validation

Codex must manually run the desktop application and validate:

- Rapid typing has no visible lag.
- Pasting 100 lines does not freeze the window.
- Window move works during live generation.
- Window resize works during live generation.
- Pane splitters work during live generation.
- Scrolling works during live generation.
- Only current source results are displayed.
- Manual Transpile All generates all seven targets.
- Target switching generates the selected current target.
- Build & Run compiles current source.
- Source editing and target selectors are protected during build.
- Cancel remains responsive.
- Timeout leaves `Timed Out`, not `Ready`.
- Failure leaves `Failed — <stage>`.
- Objective-C and Swift are skipped by Build & Run Visible.
- Build logs are not erased by successful live typing.
- File/clipboard/Explorer failures do not close the application.

Use the same Windows VM where Sin observed the lag.

---

# 20. Documentation and permanent project rules

## README

Update `README.md` in the same commit.

Document:

- Version `0.1.3`.
- Debounced background live transpilation.
- Visible-target-only live preview.
- Manual Transpile All still generates all seven targets.
- Latest-source-wins protection.
- Current build and run timeouts.
- Objective-C plain stdout output.
- Transpile-only target skip behavior.
- Local Windows VM validation and the exact commands used.
- Any changed UI status behavior.
- Actual tested behavior only.

Do not say the UI is responsive unless it was manually validated on the VM.

## AGENTS.md

Add permanent rules equivalent to:

```markdown
- WPF property setters and text-change handlers must never synchronously parse or generate target code.
- Automatic live transpilation must be debounced, cancellable, performed away from the UI thread, and protected by latest-source-wins revision checks.
- Automatic live preview should process only currently visible or otherwise required targets; explicit Transpile All may generate every target.
- Build & Run must never consume generated output from an older source revision.
- Successful automatic live transpilation must not erase build/run output.
- All SMILE development and validation currently runs locally on Sin's Windows VM.
- Do not add GitHub Actions, hosted runners, self-hosted Actions runners, or another remote CI service unless Sin explicitly requests it in the future.
```

Preserve existing KISS, KISS v2, living documentation, cleanup, commit, and public-repository rules.

## Other docs

Update as affected:

- `docs/Architecture.md`
- `docs/Toolchains.md`
- `docs/SMILE-Language-Specification-v0.1.md`
- `docs/Roadmap.md`
- Daily requirements/progress notes

Add this brief or a concise equivalent to the appropriate `Requirements` record.

## Screenshot

Because the window title/version changes to v0.1.3, capture a new screenshot after all work is complete.

Replace or add the README screenshot only after the final UI is running and verified.

---

# 21. Version update

Update all aligned references to:

```text
0.1.3
0.1.3.0
0.1.3 PRINT Everywhere
SMILE v0.1.3 - PRINT Everywhere
```

Keep these synchronized:

- WPF window title.
- Desktop project version.
- Assembly version.
- File version.
- Informational version.
- About dialog.
- README.
- Screenshot.
- Progress notes.

Do not change the SMILE language specification version from v0.1 merely because the desktop application patch version becomes 0.1.3.

---

# 22. Validation commands

Run from `C:\SMILE`:

```bat
cmd /c cd /d C:\SMILE && dotnet restore SMILE.sln
cmd /c cd /d C:\SMILE && dotnet build SMILE.sln -c Debug
cmd /c cd /d C:\SMILE && dotnet test SMILE.sln -c Debug --no-build
cmd /c cd /d C:\SMILE && dotnet build SMILE.sln -c Release
cmd /c cd /d C:\SMILE && dotnet test SMILE.sln -c Release --no-build
```

Exercise all generated targets:

```bat
cmd /c cd /d C:\SMILE && dotnet run --project src\SMILE.Cli -- examples\PrintEverywhere.smile --target all
cmd /c cd /d C:\SMILE && dotnet run --project src\SMILE.Cli -- examples\PrintEverywhere.smile --target csharp --run
cmd /c cd /d C:\SMILE && dotnet run --project src\SMILE.Cli -- examples\PrintEverywhere.smile --target c --run
cmd /c cd /d C:\SMILE && dotnet run --project src\SMILE.Cli -- examples\PrintEverywhere.smile --target masm-x64 --run
cmd /c cd /d C:\SMILE && dotnet run --project src\SMILE.Cli -- examples\PrintEverywhere.smile --target javascript --run
cmd /c cd /d C:\SMILE && dotnet run --project src\SMILE.Cli -- examples\PrintEverywhere.smile --target java --run
```

Objective-C and Swift should generate successfully but report transpile-only when `--run` is requested.

Run the WPF application:

```bat
cmd /c cd /d C:\SMILE && dotnet run --project src\SMILE.Desktop
```

Inspect repository cleanliness:

```bat
cmd /c cd /d C:\SMILE && git status --short
```

Do not commit:

- `bin`
- `obj`
- `.vs`
- Temp workspaces
- Generated executables
- Generated compiler output
- Unrelated local files

---

# 23. Suggested commits

Use logical commits such as:

```text
Sin and Codex: Make live transpilation responsive
Sin and Codex: Harden desktop commands and toolchain execution
Sin and Codex: Correct target output and parser behavior
Sin and Codex: Finalize v0.1.3 local validation and documentation
```

A smaller number of commits is acceptable when each remains coherent.

Every commit must:

- Build.
- Pass its relevant tests.
- Update README when behavior changes.
- Include a detailed public commit message.

Push:

```bat
cmd /c cd /d C:\SMILE && git push -u origin feature/v0.1.3-responsive-live-transpilation
```

No pull request is required for this one-developer local workflow unless Sin explicitly requests one.

Do not merge automatically.

---

# 24. Definition of done

## Live responsiveness

- [ ] `SourceText` never parses or generates synchronously.
- [ ] Live transpilation is debounced.
- [ ] Parser/generator CPU work runs away from the WPF UI thread.
- [ ] Pending live work is cancelled when source changes.
- [ ] Stale results are rejected by revision.
- [ ] Live preview generates only visible distinct targets.
- [ ] Manual Transpile All generates all seven targets.
- [ ] Target switching generates the newly visible current target.
- [ ] Build & Run never uses stale output.
- [ ] Automatic success does not erase build logs.
- [ ] Rapid typing is smooth on Sin’s Windows VM.
- [ ] A 100-line paste does not freeze the UI.

## WPF safety

- [ ] Async command exceptions do not close SMILE.
- [ ] Initialization exceptions do not close SMILE.
- [ ] Clipboard failures are handled.
- [ ] Explorer failures are handled.
- [ ] Source and target selectors cannot change during an active build.
- [ ] Generated source remains readable and scrollable.
- [ ] Cancel remains responsive.

## Toolchains

- [ ] Build timeout is separate from program timeout.
- [ ] Build timeout is long enough for the VM.
- [ ] Workspace cleanup runs off the UI thread.
- [ ] Workspace cleanup runs at most once per process.
- [ ] Visible-language build skips transpile-only targets.
- [ ] Final statuses are accurate.
- [ ] Total duration includes build stages.
- [ ] Java uses `-encoding UTF-8`.

## Language correctness

- [ ] Java never emits illegal `\a` or `\v`.
- [ ] JavaScript uses valid control-character escapes.
- [ ] Target-specific escaping tests pass.
- [ ] `PRINT"Hello"` follows the documented whitespace rule.
- [ ] Objective-C prints plain stdout instead of log metadata.
- [ ] Existing generated output remains deterministic.

## Repository quality

- [ ] No GitHub Actions or other remote CI workflow was added.
- [ ] All required validation was completed locally on Sin's Windows VM.
- [ ] The final report identifies the exact local commands and results.
- [ ] README matches the implemented application.
- [ ] AGENTS contains permanent live-transpilation rules.
- [ ] Docs are updated.
- [ ] Version is 0.1.3 everywhere.
- [ ] Latest screenshot is updated.
- [ ] Debug build passes.
- [ ] Debug tests pass.
- [ ] Release build passes.
- [ ] Release tests pass.
- [ ] Feature branch is pushed after local validation is green.
- [ ] No pull request was created unless Sin explicitly requested one.

---

# 25. Final Codex report

When complete, report:

1. Branch name.
2. Commit hashes and subjects.
3. Exact live-transpilation implementation.
4. Debounce value selected.
5. How stale results are prevented.
6. Which targets are generated during live preview.
7. How Build & Run ensures current-revision source.
8. Before/after manual typing observations on the VM.
9. 100-line paste validation.
10. Build and test totals.
11. Toolchains detected and actually executed.
12. Java control-character compile result.
13. Objective-C generated example.
14. Exact local VM restore/build/test commands and results.
15. WPF cancellation/timeout/status validation.
16. Documentation and screenshot updates.
17. Confirmation that no GitHub Actions or remote CI was added.
18. Any item not completed and the exact reason.

Do not claim the typing delay was fixed without manually testing rapid input in the actual WPF application on the Windows VM.

---

# 26. Out of scope

Do not add in this hardening release:

- `LET`
- Variables
- Expressions
- `INPUT`
- Conditions
- Loops
- Functions
- Classes
- Syntax highlighting
- IntelliSense
- A new editor framework
- Cloud compilation
- User accounts
- A server
- A web UI
- Additional target languages
- A plugin system
- Automatic update infrastructure

The immediate goal is simple:

> **Typing SMILE code must feel instant, and every current PRINT Everywhere feature must remain correct, stable, and easy to understand.**
