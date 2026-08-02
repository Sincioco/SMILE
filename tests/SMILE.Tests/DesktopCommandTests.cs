using SMILE.Desktop;
using SMILE.Engine;
using SMILE.Toolchains;

namespace SMILE.Tests;

[TestClass]
public sealed class DesktopCommandTests
{
    [TestMethod]
    public async Task Async_command_reports_exceptions_and_reenables_itself()
    {
        var reported = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = new AsyncRelayCommand(
            async () =>
            {
                await Task.Delay(10);
                throw new InvalidOperationException("boom");
            },
            onError: exception => reported.TrySetResult(exception));

        Assert.IsTrue(command.CanExecute(null));
        command.Execute(null);
        Assert.IsFalse(command.CanExecute(null));

        Exception exception = await reported.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => command.CanExecute(null));

        Assert.IsInstanceOfType(exception, typeof(InvalidOperationException));
    }

    [TestMethod]
    public async Task Async_command_treats_cancellation_as_normal_control_flow()
    {
        bool reported = false;
        var command = new AsyncRelayCommand(
            async () =>
            {
                await Task.Delay(10);
                throw new OperationCanceledException();
            },
            onError: _ => reported = true);

        command.Execute(null);
        await WaitUntilAsync(() => command.CanExecute(null));

        Assert.IsFalse(reported);
    }

    [TestMethod]
    public void Relay_command_error_handler_cannot_crash_command_execution()
    {
        bool sawOriginalFailure = false;
        var command = new RelayCommand(
            () => throw new InvalidOperationException("original"),
            onError: _ =>
            {
                sawOriginalFailure = true;
                throw new InvalidOperationException("handler");
            });

        command.Execute(null);

        Assert.IsTrue(sawOriginalFailure);
    }

    [TestMethod]
    public void Target_pane_button_text_and_language_lock_match_target_capability()
    {
        var pane = new TargetPaneViewModel("Pane", TargetLanguage.JavaScript);

        Assert.AreEqual("Pane - JavaScript", pane.Title);
        Assert.AreEqual("Run", pane.BuildButtonText);

        pane.SelectedLanguageOption = pane.LanguageOptions.Single(option => option.Language == TargetLanguage.ObjectiveC);

        Assert.AreEqual("Pane - Objective-C", pane.Title);
        Assert.AreEqual("Transpile Only", pane.BuildButtonText);
        Assert.IsTrue(pane.CanBuild);

        pane.IsBusy = true;

        Assert.IsFalse(pane.CanBuild);
        Assert.IsFalse(pane.CanChangeLanguage);
    }

    [TestMethod]
    public void Build_run_status_text_uses_final_user_facing_statuses()
    {
        Assert.AreEqual("Completed", MainWindowViewModel.BuildRunStatusText(Result(success: true, stage: "Running")));
        Assert.AreEqual("Failed", MainWindowViewModel.BuildRunStatusText(Result(success: false, stage: "Building")));
        Assert.AreEqual("Timed Out", MainWindowViewModel.BuildRunStatusText(Result(timedOut: true)));
        Assert.AreEqual("Cancelled", MainWindowViewModel.BuildRunStatusText(Result(cancelled: true)));
        Assert.AreEqual("Toolchain Missing", MainWindowViewModel.BuildRunStatusText(Result(stage: "Toolchain Missing")));
        Assert.AreEqual("Transpile Only", MainWindowViewModel.BuildRunStatusText(Result(stage: "Transpile Only")));
    }

    [TestMethod]
    public async Task Visible_build_run_skips_transpile_only_targets_without_failure()
    {
        var viewModel = new MainWindowViewModel
        {
            OpenGeneratedFolderAfterBuild = false
        };

        viewModel.Pane1.SelectedLanguageOption = viewModel.Pane1.LanguageOptions.Single(option => option.Language == TargetLanguage.ObjectiveC);
        viewModel.Pane2.SelectedLanguageOption = viewModel.Pane2.LanguageOptions.Single(option => option.Language == TargetLanguage.Swift);
        viewModel.Pane3.SelectedLanguageOption = viewModel.Pane3.LanguageOptions.Single(option => option.Language == TargetLanguage.ObjectiveC);

        viewModel.BuildRunVisibleCommand.Execute(null);

        await WaitUntilAsync(() => !viewModel.IsBusy && viewModel.OperationStatus == "Completed");

        Assert.AreEqual("Transpile Only", viewModel.Pane1.Status);
        Assert.AreEqual("Transpile Only", viewModel.Pane2.Status);
        StringAssert.Contains(viewModel.OutputText, "Skipped: this target is transpile-only on Windows for now.");
    }

    private static BuildRunResult Result(
        bool success = false,
        bool timedOut = false,
        bool cancelled = false,
        string stage = "Running") =>
        new(
            TargetLanguage.CSharp,
            success,
            new ToolchainStatus(TargetLanguage.CSharp, true, "C#", "test", "test", "Toolchain detected."),
            string.Empty,
            string.Empty,
            string.Empty,
            success ? 0 : 1,
            TimeSpan.FromMilliseconds(1),
            timedOut,
            cancelled,
            null,
            null,
            stage);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        while (!condition())
        {
            if (timeout.IsCancellationRequested)
            {
                Assert.Fail("Condition was not met before the test timeout.");
            }

            await Task.Delay(10);
        }
    }
}
