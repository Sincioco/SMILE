# Codex Implementation Instructions — Build & Run Crash Containment and Desktop Stability

## Repository

- Repository: `Sincioco/SMILE`
- Work from the latest `main`.
- Re-read `AGENTS.md` before changing code.
- Do not discard or overwrite unrelated user work.
- Do not commit or push until Sin explicitly instructs you to do so.
- Keep KISS and KISS v2.
- Do not add a third-party logging, process, MVVM, or resilience framework.

---

# 1. Objective

Harden the SMILE WPF desktop application so that a recoverable failure during:

- transpilation;
- toolchain detection;
- generated-file creation;
- compilation;
- assembly;
- linking;
- program launch;
- program execution;
- cancellation;
- timeout handling;
- output capture;
- generated-folder opening;

does **not** terminate the SMILE desktop process.

The required user-facing rule is:

> A Build & Run failure must fail the build/run operation, not the SMILE desktop application.

When possible, SMILE must show:

- which target failed;
- which stage failed;
- a concise error message;
- compiler/linker/program output;
- whether it timed out or was cancelled;
- where detailed diagnostic information was logged.

The UI must return to a usable state after a recoverable failure.

---

# 2. Important diagnostic conclusion

The current code already catches many command and operation exceptions, but it does not yet provide complete failure containment.

The reviewed code has these important gaps:

1. `App.xaml.cs` contains no application-level WPF exception safety net.
2. `AsyncRelayCommand.Execute` performs some work outside its protected `try` block.
3. `RunOperationAsync` performs lifecycle state changes before and after the main protected operation.
4. one unexpected target exception can abort the remaining targets in **Build & Run Visible Languages**;
5. `ProcessRunner` catches only a subset of process lifecycle exceptions;
6. stdout and stderr are captured with unlimited `ReadToEndAsync`;
7. the desktop output history grows without a size limit;
8. generated-folder opening is started as a discarded fire-and-forget task;
9. the current UI error display usually shows only `Exception.Message`, so the actual crash source is lost;
10. there is no durable per-session diagnostic log.

Do not claim one exact crash source without reproducing it or obtaining a Windows crash record. Implement all of the containment boundaries below so the next failure is both survivable and diagnosable.

---

# 3. Stability guarantee

Add this permanent project rule to `AGENTS.md` and relevant architecture/toolchain documentation:

> Recoverable compiler, toolchain, process, file-system, cancellation, timeout, and generated-program failures MUST NOT terminate the SMILE desktop application. The failure must be converted into a stable operation result, displayed to the user where practical, logged with diagnostic detail, and followed by restoration of a usable UI state.

Also add:

> No asynchronous operation may be intentionally abandoned without observing and reporting its exceptions.

---

# 4. Add a minimal error-reporting facility

Create a small first-party component in `SMILE.Desktop`, for example:

```text
AppErrorReporter.cs
```

Do not add a logging package.

A simple interface is acceptable for testing:

```csharp
public interface IAppErrorReporter
{
    string Report(
        string operation,
        Exception exception,
        string? target = null,
        string? stage = null);
}
```

The returned string may be the full log-file path.

## 4.1 Log location

Use a per-user writable location:

```text
%LOCALAPPDATA%\SMILE\Logs
```

For example:

```text
%LOCALAPPDATA%\SMILE\Logs\SMILE-2026-08-02.log
```

Create directories safely.

If the preferred location cannot be used, fall back to:

```text
%TEMP%\SMILE\Logs
```

Logging failure must never cause a second application failure.

## 4.2 Log content

Include:

- UTC timestamp;
- local timestamp;
- unique session ID;
- application version;
- operation;
- target language when known;
- stage when known;
- exception type;
- exception message;
- complete `exception.ToString()`;
- inner exceptions;
- operating-system version;
- process architecture;
- current source revision if available;
- current thread ID;
- whether the thread is the WPF dispatcher thread.

Do not put full source code in the diagnostic log by default.

## 4.3 User-facing error text

Ordinary UI output should be concise:

```text
=== C Build & Run Error ===
Stage: Building
UnauthorizedAccessException: Access to the build workspace was denied.
Details: C:\Users\...\AppData\Local\SMILE\Logs\SMILE-2026-08-02.log
SMILE remains open. Correct the issue and try again.
```

Do not dump a full stack trace into the normal output pane.

---

# 5. Application-level WPF exception safety net

`App.xaml.cs` is currently empty. Add application-level handlers during startup.

Handle:

```csharp
DispatcherUnhandledException
AppDomain.CurrentDomain.UnhandledException
TaskScheduler.UnobservedTaskException
```

## 5.1 `DispatcherUnhandledException`

For a recoverable managed UI-thread exception:

1. log it;
2. display a concise error in the desktop output area when possible;
3. restore command/busy state when possible;
4. set:

```csharp
e.Handled = true;
```

Do **not** indiscriminately continue after genuinely fatal runtime conditions.

Use a conservative helper such as:

```csharp
private static bool IsFatal(Exception exception)
```

At minimum, do not attempt normal recovery for:

- `OutOfMemoryException`;
- `AccessViolationException`;
- other exceptions that clearly indicate corrupted process state.

`StackOverflowException` generally cannot be recovered through this handler.

## 5.2 `AppDomain.CurrentDomain.UnhandledException`

This handler is primarily diagnostic because the process may already be terminating.

Log as much as possible.

Do not claim that setting a flag here prevents termination.

## 5.3 `TaskScheduler.UnobservedTaskException`

Log the exception and call:

```csharp
e.SetObserved();
```

The main fix is still to observe every task directly; this event is only the final safety net.

## 5.4 Avoid recursive error handling

Protect the global handlers themselves with a final `try/catch`.

An exception while reporting an exception must not trigger an infinite error loop.

---

# 6. Harden `AsyncRelayCommand` completely

The current `AsyncRelayCommand.Execute` catches exceptions from `_execute`, but these calls occur outside or at the edge of the protected region:

```csharp
CanExecute(parameter)
RaiseCanExecuteChanged()
```

The final `RaiseCanExecuteChanged()` can also throw from inside `finally`.

Refactor so the entire `async void` boundary is contained.

A recommended shape is:

```csharp
public async void Execute(object? parameter)
{
    try
    {
        await ExecuteCoreAsync(parameter).ConfigureAwait(true);
    }
    catch (OperationCanceledException)
    {
        // Expected cancellation.
    }
    catch (Exception ex)
    {
        SafeReportError(ex);
    }
}
```

Then put all state changes and all `CanExecuteChanged` notifications inside `ExecuteCoreAsync`.

Requirements:

- exceptions from `CanExecute` must not terminate WPF;
- exceptions from `RaiseCanExecuteChanged` must not terminate WPF;
- exceptions from `_execute` must be reported;
- exceptions from the error callback must be swallowed after best-effort logging;
- `_isRunning` must always return to `false`;
- the command must become executable again after a recoverable failure;
- double-click/re-entrancy must remain blocked.

Apply equivalent defensive notification handling to `RelayCommand`.

Do not use `.Wait()`, `.Result`, or synchronous dispatcher waits.

---

# 7. Make `RunOperationAsync` a complete containment boundary

Refactor `MainWindowViewModel.RunOperationAsync`.

The current method changes busy/cancellation state before entering its primary `try`, then disposes and resets state in `finally`.

Use a local operation token source and place the whole operation lifecycle inside a protected boundary.

Conceptually:

```csharp
private async Task RunOperationAsync(
    string title,
    Func<CancellationToken, Task> operation)
{
    if (IsBusy)
    {
        return;
    }

    using var cancellation = new CancellationTokenSource();
    _operationCancellation = cancellation;

    try
    {
        CancelLiveTranspilation();
        IsBusy = true;
        OperationStatus = title;

        await operation(cancellation.Token).ConfigureAwait(true);

        OperationStatus =
            cancellation.IsCancellationRequested
                ? "Cancelled"
                : "Completed";
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
    {
        RecoverAfterCancellation();
    }
    catch (Exception ex)
    {
        RecoverAfterOperationFailure(title, ex);
    }
    finally
    {
        if (ReferenceEquals(_operationCancellation, cancellation))
        {
            _operationCancellation = null;
        }

        SafeSetBusy(false);
        SafeRaiseCommandStateChanged();
    }
}
```

Exact names may differ.

Requirements:

- use a local CTS rather than repeatedly dereferencing a mutable field;
- cancellation must be distinguished from failure;
- UI cleanup must happen even when setup or notification fails;
- error reporting must not throw;
- all panes left in `Building`, `Assembling`, `Linking`, or `Running` must move to a final status;
- `IsBusy` must become `false`;
- source and language selectors must become usable again;
- Cancel must not target a disposed token source.

---

# 8. Isolate each visible target

`BuildRunVisibleAsync` currently runs multiple targets under one broad operation. An unexpected exception in one target can stop the remaining targets.

Wrap each distinct target independently.

Required behavior:

```text
C# succeeds
C unexpectedly throws
MASM still gets a chance to build unless cancellation was requested
```

For each target:

1. set its pane status;
2. run detection/build/run in a target-specific `try`;
3. convert an exception into a displayed and logged target failure;
4. continue to the next target unless cancelled;
5. preserve completed results from previous targets.

Suggested output:

```text
=== C# ===
Completed.

=== C ===
Failed during Building.
IOException: The build workspace could not be written.
Details: ...

=== MASM x64 ===
Completed.
```

A normal compiler error is not an exceptional desktop failure. It should continue using `BuildRunResult`.

---

# 9. Add a safe toolchain boundary

Expected environment failures should become structured `BuildRunResult` values rather than escaping as exceptions.

Add a focused wrapper, either:

- in `MainWindowViewModel`; or
- in a small toolchain execution helper.

Do not add a large service framework.

Contain failures from:

- toolchain re-detection;
- temp directory creation;
- cleanup;
- generated source writes;
- command-script writes;
- compiler launch;
- linker launch;
- generated-program launch;
- pause-launcher creation.

Populate:

- target;
- failed stage;
- error type/message;
- workspace path when it exists;
- build output captured before failure;
- duration;
- cancellation/timeout flags.

Unexpected exceptions should still be logged in full.

---

# 10. Harden `ProcessRunner`

The process runner is the most important technical boundary.

It should return a failed `ProcessResult` for expected process and environment problems.

## 10.1 Validate inputs

Before launch, validate:

- command is not null;
- filename is not blank;
- timeout is positive;
- working directory is not blank;
- working directory exists.

Return a useful failure result rather than throwing for an expected invalid environment.

## 10.2 Broaden safe process-start handling

Current start handling catches `Win32Exception` only.

Safely handle expected launch exceptions such as:

- `Win32Exception`;
- `InvalidOperationException`;
- `IOException`;
- `UnauthorizedAccessException`;
- `ArgumentException`;
- `NotSupportedException`;
- `ObjectDisposedException`.

Do not catch fatal runtime exceptions as normal failures.

## 10.3 Harden cancellation and timeout killing

`TryKillProcessTree` currently catches only `InvalidOperationException`.

Also safely handle expected kill failures such as:

- `Win32Exception`;
- `NotSupportedException`;
- `InvalidOperationException`;
- `ObjectDisposedException`.

After requesting termination, do not wait forever.

Use a small bounded kill grace period, for example:

```csharp
TimeSpan.FromSeconds(5)
```

If the process still does not exit:

- return a timed-out/cancelled result;
- include a warning that process-tree termination could not be confirmed;
- do not hang the desktop operation indefinitely.

## 10.4 Safely read exit status

Access to:

```csharp
process.HasExited
process.ExitCode
```

can fail when process startup or teardown is abnormal.

Read them through safe helper methods.

## 10.5 Safely complete stream readers

The current stream helper only catches `ObjectDisposedException`.

Safely account for:

- `IOException`;
- `InvalidOperationException`;
- `ObjectDisposedException`.

Include stream-read failure details in `StandardError`.

---

# 11. Bound stdout and stderr capture

The current runner uses unlimited:

```csharp
ReadToEndAsync()
```

A child program that produces very large or endless output can consume enough memory to destabilize or terminate the desktop application.

Replace this with bounded asynchronous draining.

## 11.1 Required behavior

- continue draining the child stream so the child does not deadlock;
- retain only a configured maximum amount for display;
- count omitted characters or bytes;
- append a truncation marker;
- keep stdout and stderr separate;
- do not block the UI thread.

A reasonable initial limit is:

```csharp
1_000_000 characters per stream
```

or another documented value between 512 KB and 4 MB.

Example marker:

```text
[SMILE truncated 4,218,772 additional stdout characters.]
```

The exact number must be deterministic and testable.

## 11.2 Future-proofing

This is required even though the current official language is small. Future loops or user-written generated programs must not be able to exhaust the IDE process merely by printing continuously.

---

# 12. Bound the desktop output history

`AppendOutput` currently keeps concatenating strings without a limit.

Add a maximum desktop history size, for example:

```csharp
1_000_000 characters
```

When exceeded:

- retain the newest useful output;
- remove complete older sections where practical;
- add one marker:

```text
[Older SMILE output was truncated.]
```

Do not repeatedly duplicate the marker.

This prevents long development sessions and repeated compiler logs from growing without bound.

---

# 13. Remove discarded fire-and-forget tasks

The current generated-folder flow uses:

```csharp
_ = OpenGeneratedFolderAsync(folderToOpen);
```

Do not intentionally discard this task.

Preferred fix:

- make `OpenGeneratedFolderForResultsAsync` return `Task`;
- await it from `BuildRunVisibleAsync` and `BuildRunPaneAsync`;
- keep its errors inside the same command/operation reporting path.

If a truly fire-and-forget task remains necessary, use one small helper that:

- observes completion;
- catches the exception;
- reports it safely.

No unobserved task is acceptable.

Also move all of `OpenGeneratedFolderAsync`, including the initial `AppendOutput`, inside its `try`.

---

# 14. Simplify or harden Explorer integration

`FolderOpener` uses dynamic `Shell.Application` COM automation to find and activate Explorer windows.

This is more fragile than simply asking Windows to open a folder.

Stability takes priority.

Choose one of these:

## Preferred KISS option

Remove the COM activation/reuse behavior and use:

```csharp
Process.Start(new ProcessStartInfo
{
    FileName = "explorer.exe",
    Arguments = quotedFolder,
    UseShellExecute = true
});
```

Catch and report expected launch failures.

## Alternative

Retain activation only if it is moved behind a fully tested, isolated, correctly apartment-threaded implementation with a reliable fallback to `explorer.exe`.

Do not run fragile COM automation as an unobserved background task.

Folder-opening failure must never change a successful build into an application crash.

It may be reported as a secondary warning:

```text
Build completed, but the generated folder could not be opened.
```

---

# 15. Make toolchain detection independent per target

During initialization and later re-detection:

- catch and report failure for each language separately;
- mark that language `Toolchain Missing` or `Detection Failed`;
- continue detecting the remaining languages;
- leave the editor/transpiler usable.

One broken Java or Visual Studio installation must not prevent C# or JavaScript from being used.

---

# 16. Make old-workspace cleanup non-critical

Workspace cleanup is housekeeping. It must not prevent a new build.

Safely handle:

- root enumeration failure;
- directory disappearance during enumeration;
- path-too-long conditions;
- file-in-use conditions;
- unauthorized access;
- transient I/O errors.

Log cleanup warnings and continue creating a new workspace when safe.

Preserve the strict rule that deletion may occur only inside the SMILE-owned temp root.

---

# 17. User-visible Build & Run error format

For a normal compiler failure:

```text
=== C ===
MSVC x64 tools detected.
Build failed.

Build output:
Program.c(12): error C2143: ...

Exit code: 2
Workspace: ...
```

For an unexpected internal/toolchain exception:

```text
=== C ===
Unexpected failure during Building.

IOException: The generated source file could not be written.
Details: C:\Users\...\AppData\Local\SMILE\Logs\SMILE-2026-08-02.log
SMILE remains open.
```

For a generated program failure:

```text
=== JavaScript ===
Program exited with an error.

Program error:
...

Exit code: 1
```

For timeout:

```text
=== JavaScript ===
Timed out after 10 seconds.
The generated process was terminated.
```

For cancellation:

```text
=== MASM x64 ===
Cancelled by user.
```

Do not label a compiler diagnostic as a desktop crash.

---

# 18. Optional session indicator

Add a session ID to the About dialog or diagnostics output only if it remains simple.

Example:

```text
Session: 20260802-214510-a1b2c3d4
```

This makes a user's screenshot easy to match to a log entry.

Do not clutter the main UI permanently.

---

# 19. Required automated tests

Use MSTest already in the solution.

## 19.1 Command containment

Test that `AsyncRelayCommand` survives exceptions from:

- the asynchronous execute delegate;
- the error callback;
- `CanExecute`;
- a `CanExecuteChanged` subscriber.

Verify:

- no exception escapes the command boundary;
- `_isRunning` resets;
- `CanExecute` returns to the expected state.

## 19.2 View-model operation recovery

Inject fake toolchains/process runners.

Add a test-friendly constructor or small dependency seam for `MainWindowViewModel`.

Test a toolchain that throws during:

- detection;
- generated-file preparation;
- building;
- running.

Verify:

- `IsBusy == false`;
- the pane has a final `Failed` status;
- `OperationStatus == "Failed"` or an equally clear final state;
- output contains operation, target, stage, and concise message;
- the next command can execute.

## 19.3 Visible-target isolation

Use three fake targets:

- first succeeds;
- second throws;
- third succeeds.

Verify that the third still runs when cancellation was not requested.

## 19.4 Process start failures

Test:

- missing executable;
- missing working directory;
- invalid filename;
- process start denied where practical through a fake runner seam.

Verify a failed `ProcessResult`, not an escaped exception.

## 19.5 Timeout and cancellation teardown

Test:

- timeout;
- user cancellation;
- child process spawning another child;
- kill failure through a fake process abstraction or injectable termination helper.

Verify no indefinite wait.

## 19.6 Output truncation

Run a process that emits more than the capture limit to both stdout and stderr.

Verify:

- operation completes;
- retained output is within the configured limit plus marker;
- truncation marker appears;
- no pipe deadlock occurs.

## 19.7 Desktop history truncation

Append output beyond the UI limit.

Verify:

- newest output remains;
- older output is removed;
- one truncation marker is present;
- the property does not grow indefinitely.

## 19.8 Folder opening

Inject a folder opener that throws.

Verify:

- build result remains displayed;
- SMILE returns to non-busy state;
- error is reported;
- no unobserved task is created.

## 19.9 Global reporting

Test the reporter directly:

- creates the preferred directory;
- falls back safely when writing fails;
- includes operation, exception type, and stack;
- never throws back to the caller.

---

# 20. Manual Windows validation

Perform validation on the Windows VM where Visual Studio 2026 Enterprise and the toolchains are installed.

## 20.1 Repeated use

Click per-pane Build & Run at least 20 times for:

- C#;
- C;
- MASM x64;
- JavaScript;
- Java.

The desktop must remain open.

## 20.2 Visible build

Run **Build & Run Visible Languages** repeatedly with different combinations.

One target failure must not terminate the app or prevent unrelated targets from reporting.

## 20.3 Cancellation timing

Press Cancel during:

- .NET build;
- C compilation;
- MASM assembly;
- MASM link;
- Java compilation;
- generated-program execution.

The app must return to a stable state every time.

## 20.4 Toolchain disruption

While SMILE is open, test expected failures such as:

- temporarily rename or make a test compiler path unavailable;
- use an invalid temp/workspace path through a controlled test setting;
- lock a generated file;
- deny access to a controlled test directory;
- remove Node or Java from the test process PATH.

SMILE must show an error and remain open.

Do not alter the real machine destructively.

## 20.5 Generated program errors

Run a controlled generated target that:

- exits nonzero;
- writes stderr;
- times out;
- emits output beyond the cap.

The desktop must remain responsive.

## 20.6 Folder opening

Test with **Open Generated Folder After Build**:

- enabled;
- disabled;
- Explorer already open;
- Explorer closed;
- invalid folder through a controlled fake/test path.

Folder opening failure must be a warning, not an app crash.

## 20.7 Window usability

After every failure confirm:

- source editor can be edited;
- selectors are enabled;
- Build & Run can be tried again;
- Cancel is disabled when no operation is active;
- output can be copied/selected;
- window can move, resize, minimize, and restore.

---

# 21. Capture the actual crash if it still occurs

After hardening, if the desktop still exits unexpectedly, collect:

1. the newest SMILE log under `%LOCALAPPDATA%\SMILE\Logs`;
2. Windows Event Viewer:
   - Windows Logs;
   - Application;
   - `.NET Runtime`;
   - `Application Error`;
3. Windows Reliability Monitor entry;
4. exact target language;
5. whether **Open Generated Folder After Build** was enabled;
6. whether Cancel was pressed;
7. source program;
8. last text visible in the output pane.

Add the application version and session ID to the report.

Do not guess at a remaining native/fatal crash without this evidence.

---

# 22. Documentation updates

Update:

- `AGENTS.md`;
- `README.md`;
- `docs/Architecture.md`;
- `docs/Toolchains.md`;
- relevant requirements/history notes.

Document:

- Build & Run failure containment;
- per-target isolation;
- error-log location;
- output limits;
- cancellation and timeout behavior;
- generated-folder warning behavior;
- distinction between compiler errors, program errors, and internal desktop errors.

Do not claim that all fatal runtime failures are recoverable.

---

# 23. Acceptance criteria

The task is complete only when all of these are true:

1. A recoverable Build & Run exception does not close SMILE.
2. Application-level managed exception handlers are installed.
3. All command async-void boundaries contain their exceptions.
4. `RunOperationAsync` restores a stable state after setup, operation, reporting, or cleanup failure.
5. one visible target failure does not prevent unrelated targets from continuing.
6. toolchain exceptions become target-specific failure output.
7. process-start, wait, kill, exit-code, and stream-read failures are handled safely.
8. cancellation and timeout teardown cannot wait forever.
9. stdout and stderr capture are bounded while still fully drained.
10. desktop output history is bounded.
11. no task is intentionally discarded without exception observation.
12. folder-opening failure is a secondary warning and cannot crash the app.
13. toolchain detection is isolated per language.
14. cleanup failure does not block a new build.
15. concise errors appear in the output pane.
16. detailed errors are written to a per-user log.
17. the error reporter itself never throws.
18. all pane and command states recover after a failure.
19. the approved C# generator output remains unchanged.
20. no SMILE language semantics are changed.
21. Debug and Release tests pass.
22. repeated manual Windows build/run and cancellation testing passes.
23. README and technical documentation match the implemented behavior.
24. no build artifacts or unrelated changes are committed.

---

# 24. Validation commands

Run from the repository root:

```bat
cmd /c dotnet restore SMILE.sln
```

```bat
cmd /c dotnet build SMILE.sln -c Debug
```

```bat
cmd /c dotnet test SMILE.sln -c Debug --no-build
```

```bat
cmd /c dotnet build SMILE.sln -c Release
```

```bat
cmd /c dotnet test SMILE.sln -c Release --no-build
```

Run CLI generation for all targets.

Run all currently supported local toolchains.

Before any commit:

```bat
cmd /c git diff --check
```

---

# 25. Commit guidance

Use a focused commit subject such as:

```text
Sin and Codex: Contain Build and Run failures without crashing SMILE
```

The detailed commit message should include:

- reproduced crash or failure mode, if identified;
- application-level exception safety;
- command and operation containment;
- per-target isolation;
- process runner hardening;
- bounded output;
- logging location;
- folder opener changes;
- automated test counts;
- repeated Windows smoke-test results;
- any failure that could not be reproduced.

Do not state that the exact original crash was fixed unless it was reproduced or its exception was captured. State accurately that the Build & Run path was hardened against the tested failure classes.
