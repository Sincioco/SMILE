using System.IO;
using System.Reflection;
using SMILE.Desktop;
using SMILE.Engine;
using SMILE.Toolchains;

namespace SMILE.Tests;

[TestClass]
public sealed class DesktopCommandTests
{
    [TestMethod]
    public void Desktop_assembly_reports_the_v0601_IF_hardening_release()
    {
        string? version = typeof(MainWindowViewModel).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        Assert.IsNotNull(version);
        StringAssert.StartsWith(version, "0.6.0.1 IF Hardening");
    }

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
    public void Relay_command_contains_can_execute_and_notification_failures()
    {
        Exception? reported = null;
        var command = new RelayCommand(
            () => Assert.Fail("Execute should not run when CanExecute fails."),
            canExecute: () => throw new InvalidOperationException("can execute failed"),
            onError: exception => reported = exception);

        Assert.IsFalse(command.CanExecute(null));
        command.Execute(null);

        Assert.IsInstanceOfType(reported, typeof(InvalidOperationException));

        var notifyCommand = new RelayCommand(() => { }, onError: exception => reported = exception);
        notifyCommand.CanExecuteChanged += (_, _) => throw new InvalidOperationException("subscriber failed");

        notifyCommand.RaiseCanExecuteChanged();

        Assert.AreEqual("subscriber failed", reported?.Message);
    }

    [TestMethod]
    public async Task Async_command_contains_error_callback_and_notification_failures()
    {
        var command = new AsyncRelayCommand(
            () => throw new InvalidOperationException("execute failed"),
            onError: _ => throw new InvalidOperationException("report failed"));

        command.Execute(null);
        await WaitUntilAsync(() => command.CanExecute(null));

        Exception? reported = null;
        var notifyCommand = new AsyncRelayCommand(
            () => Task.CompletedTask,
            onError: exception => reported = exception);
        notifyCommand.CanExecuteChanged += (_, _) => throw new InvalidOperationException("async subscriber failed");

        notifyCommand.Execute(null);
        await WaitUntilAsync(() => notifyCommand.CanExecute(null));

        Assert.AreEqual("async subscriber failed", reported?.Message);
    }

    [TestMethod]
    public void Target_pane_button_text_and_language_lock_match_target_capability()
    {
        var pane = new TargetPaneViewModel("Pane", TargetLanguage.JavaScript);

        Assert.AreEqual("Pane - JavaScript", pane.Title);
        Assert.AreEqual("Run", pane.BuildButtonText);

        pane.SelectedLanguageOption = pane.LanguageOptions.Single(option => option.Language == TargetLanguage.Python);

        Assert.AreEqual("Pane - Python", pane.Title);
        Assert.AreEqual("Run", pane.BuildButtonText);

        pane.SelectedLanguageOption = pane.LanguageOptions.Single(option => option.Language == TargetLanguage.ObjectiveC);

        Assert.AreEqual("Pane - Objective-C", pane.Title);
        Assert.AreEqual("Build & Run", pane.BuildButtonText);
        Assert.IsFalse(pane.CanBuild);

        pane.HasToolchain = true;

        Assert.IsTrue(pane.CanBuild);

        pane.SelectedLanguageOption = pane.LanguageOptions.Single(option => option.Language == TargetLanguage.Cpp);

        Assert.AreEqual("Pane - C++", pane.Title);
        Assert.AreEqual("Build & Run", pane.BuildButtonText);

        pane.IsBusy = true;

        Assert.IsFalse(pane.CanBuild);
        Assert.IsFalse(pane.CanChangeLanguage);
    }

    [TestMethod]
    public void Target_pane_reports_highlighting_id_and_change_notification()
    {
        var pane = new TargetPaneViewModel("Pane", TargetLanguage.CSharp);
        var changedProperties = new List<string?>();
        pane.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        Assert.AreEqual("csharp", pane.HighlightingId);

        pane.SelectedLanguageOption = pane.LanguageOptions.Single(option => option.Language == TargetLanguage.Swift);

        Assert.AreEqual("swift", pane.HighlightingId);
        CollectionAssert.Contains(changedProperties, nameof(TargetPaneViewModel.HighlightingId));
    }

    [TestMethod]
    public async Task Initialization_loads_the_packaged_cumulative_language_reference_and_generates_visible_targets()
    {
        string runtimeLanguagePath = Path.Combine(
            AppContext.BaseDirectory,
            MainWindowViewModel.LanguageFileName);
        Assert.IsTrue(File.Exists(runtimeLanguagePath), runtimeLanguagePath);

        var viewModel = new MainWindowViewModel(
            CreateRegistry(),
            new FakeErrorReporter(),
            new FakeFolderOpener());

        // Window construction must stay presentation-only. The post-render
        // initialization step is responsible for language-reference I/O and generation.
        Assert.AreEqual(string.Empty, viewModel.SourceText);

        await viewModel.InitializeAsync();

        const string legacyLetPrintReference = """
LET FirstName = "Sin"
LET LastName = "Cioco"
LET FullName = FirstName + " " + LastName
LET Greeting = $"Hello {FullName}!"
LET Quote = "She said \"Hello\"."
LET Path = "C:\\SMILE"
LET Age = 49
LET Negative = -12
LET Total = 2 + 3 * 4
LET Grouped = (2 + 3) * 4
LET Quotient = -7 / 2
LET Enabled = TRUE
LET Adult = Age >= 18
LET WorkingAge = Adult AND NOT FALSE
LET SameName = FullName = "Sin Cioco"
LET MixedMessage = $"{FullName}: Age={Age}, Adult={Adult}"

PRINT
PRINT "Quoted literal"
PRINT Raw template keeps C:\SMILE literally.
PRINT Literal braces: {{Name}}
PRINT $"Interpolated greeting: {Greeting}"
PRINT "Concat: " + FirstName + " " + LastName
PRINT {FullName}
PRINT {Age}
PRINT {Negative}
PRINT {Total}
PRINT {Grouped}
PRINT {Quotient}
PRINT {Enabled}
PRINT {WorkingAge}
PRINT {SameName}
PRINT {MixedMessage}
PRINT 2 + 3 = {2 + 3}
PRINT Adult check: {Age >= 18}
PRINT Quote: {Quote}
PRINT Path: {Path}
PRINT {Greeting}

PRINT ""
PRINT FirstName
PRINT {FirstName}
PRINT "FirstName remains literal here: {FirstName}"
PRINT $"Literal braces remain literal: {{FirstName}}"
PRINT A; B; C
""";
        string normalizedSource = NormalizeLineEndings(viewModel.SourceText);
        string expectedLegacyPrefix = NormalizeLineEndings(legacyLetPrintReference) +
            "\n\nPRINT\nPRINT SET statement examples in language.smile:";
        Assert.IsTrue(
            normalizedSource.StartsWith(expectedLegacyPrefix, StringComparison.Ordinal),
            "language.smile must preserve the complete cumulative LET/PRINT reference before SET.");

        StringAssert.Contains(viewModel.SourceText, "SET FirstName = \"Louiery\"");
        StringAssert.Contains(viewModel.SourceText, "set lastname=\"Cioco\"");
        StringAssert.Contains(viewModel.SourceText, "SET MixedMessage =\"");
        StringAssert.Contains(viewModel.SourceText, "SET Quote = \"");
        StringAssert.Contains(viewModel.SourceText, "LET Bonus = Score / 3");
        StringAssert.Contains(viewModel.SourceText, "PRINT Toggled passed={Passed}.");
        StringAssert.Contains(viewModel.SourceText, "IF IfScore >= 80 THEN");
        StringAssert.Contains(viewModel.SourceText, "ELSE IF IfScore >= 80 AND IfReady = TRUE THEN");
        StringAssert.Contains(viewModel.SourceText, "IF NOT (IfReady = FALSE) THEN");
        StringAssert.Contains(viewModel.SourceText, "SET IfMessage =\"");
        StringAssert.Contains(viewModel.SourceText, "PRINT Grade={IfGrade}");

        EvaluationResult evaluation = new SmileEvaluator().Evaluate(viewModel.SourceText);
        Assert.IsTrue(
            evaluation.Success,
            string.Join(Environment.NewLine, evaluation.Diagnostics));

        var transpiler = new SmileTranspiler();
        foreach (TargetLanguage language in TargetLanguageInfo.All)
        {
            TranspileResult result = transpiler.Transpile(viewModel.SourceText, language);
            Assert.IsTrue(
                result.Success,
                $"{language}{Environment.NewLine}{string.Join(Environment.NewLine, result.Diagnostics)}");
        }

        foreach (TargetPaneViewModel pane in viewModel.Panes)
        {
            Assert.AreEqual("Ready", pane.Status);
            Assert.IsTrue(pane.HasValidSource);
            Assert.IsFalse(string.IsNullOrWhiteSpace(pane.GeneratedCode));
        }
    }

    [TestMethod]
    public async Task Live_transpilation_reports_1000_level_if_source_without_crashing_the_desktop_path()
    {
        var viewModel = new MainWindowViewModel(
            CreateRegistry(),
            new FakeErrorReporter(),
            new FakeFolderOpener());
        await viewModel.InitializeAsync();

        viewModel.SourceText = CreateNestedIfSource(1_000);

        await WaitUntilAsync(() =>
            viewModel.Panes.All(pane => pane.HasSyntaxError) &&
            viewModel.OutputText.Contains("SMILE1416", StringComparison.Ordinal));

        Assert.IsTrue(viewModel.Panes.All(pane => pane.Status == "Syntax Error"));
        Assert.IsTrue(viewModel.Panes.All(pane => string.IsNullOrEmpty(pane.GeneratedCode)));
    }

    [TestMethod]
    public async Task Initialization_never_overwrites_source_changed_while_the_language_reference_is_loading()
    {
        var delayedRead = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = new MainWindowViewModel(
            CreateRegistry(),
            new FakeErrorReporter(),
            new FakeFolderOpener(),
            languageFilePath: null,
            _ => delayedRead.Task);

        Task initialization = viewModel.InitializeAsync();
        viewModel.SourceText = "PRINT Learner work wins.";
        delayedRead.SetResult("PRINT Packaged reference");

        await initialization;

        Assert.AreEqual("PRINT Learner work wins.", viewModel.SourceText);
        foreach (TargetPaneViewModel pane in viewModel.Panes)
        {
            Assert.AreEqual("Ready", pane.Status);
            Assert.IsTrue(pane.HasValidSource);
            StringAssert.Contains(pane.GeneratedCode, "Learner work wins.");
        }
    }

    [TestMethod]
    public async Task New_command_reloads_the_packaged_language_reference_as_an_unassociated_document()
    {
        var viewModel = new MainWindowViewModel(
            CreateRegistry(),
            new FakeErrorReporter(),
            new FakeFolderOpener());
        await viewModel.InitializeAsync();
        viewModel.SourceText = "PRINT temporary edit";

        viewModel.NewCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.NewCommand.CanExecute(null));

        StringAssert.Contains(viewModel.SourceText, "LET FirstName = \"Sin\"");
        StringAssert.Contains(viewModel.SourceText, "PRINT \"SET, PRINT, and LET together:\"");
        StringAssert.Contains(viewModel.SourceText, "PRINT \"IF, ELSE IF, and ELSE statement examples:\"");
        Assert.AreEqual("Language reference loaded", viewModel.OperationStatus);
    }

    [TestMethod]
    public async Task Missing_language_reference_is_reported_without_closing_or_blocking_the_editor()
    {
        string missingLanguagePath = Path.Combine(
            Path.GetTempPath(),
            "SMILE-Missing-" + Guid.NewGuid().ToString("N") + ".smile");
        var viewModel = new MainWindowViewModel(
            CreateRegistry(),
            new FakeErrorReporter(),
            new FakeFolderOpener(),
            missingLanguagePath);

        await viewModel.InitializeAsync();

        Assert.AreEqual(string.Empty, viewModel.SourceText);
        StringAssert.Contains(viewModel.OutputText, "Load language reference Error");
        StringAssert.Contains(viewModel.OutputText, "SMILE remains open");
        Assert.AreEqual("Ready", viewModel.OperationStatus);
    }

    [TestMethod]
    public async Task New_preserves_the_current_document_when_the_language_reference_cannot_be_loaded()
    {
        string missingLanguagePath = Path.Combine(
            Path.GetTempPath(),
            "SMILE-Missing-" + Guid.NewGuid().ToString("N") + ".smile");
        var viewModel = new MainWindowViewModel(
            CreateRegistry(),
            new FakeErrorReporter(),
            new FakeFolderOpener(),
            missingLanguagePath)
        {
            SourceText = "PRINT Keep this work"
        };

        viewModel.NewCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.NewCommand.CanExecute(null));

        Assert.AreEqual("PRINT Keep this work", viewModel.SourceText);
        Assert.AreEqual("Failed", viewModel.OperationStatus);
        StringAssert.Contains(viewModel.OutputText, "Load language reference Error");
        StringAssert.Contains(viewModel.OutputText, "SMILE remains open");
    }

    [TestMethod]
    public async Task Rapid_language_switching_only_generates_the_latest_missing_visible_target()
    {
        var viewModel = new MainWindowViewModel(
            CreateRegistry(),
            new FakeErrorReporter(),
            new FakeFolderOpener());
        await viewModel.InitializeAsync();

        TargetPaneViewModel pane = viewModel.Pane3;
        pane.SelectedLanguageOption = pane.LanguageOptions.Single(option => option.Language == TargetLanguage.Cpp);
        pane.SelectedLanguageOption = pane.LanguageOptions.Single(option => option.Language == TargetLanguage.Python);
        pane.SelectedLanguageOption = pane.LanguageOptions.Single(option => option.Language == TargetLanguage.Cpp);

        Assert.AreEqual("cpp", pane.HighlightingId);
        Assert.AreEqual("Updating", pane.Status);

        await WaitUntilAsync(() =>
            pane.Status == "Ready" &&
            pane.GeneratedCode.Contains("std::cout", StringComparison.Ordinal));

        pane.SelectedLanguageOption = pane.LanguageOptions.Single(option => option.Language == TargetLanguage.Swift);
        pane.SelectedLanguageOption = pane.LanguageOptions.Single(option => option.Language == TargetLanguage.Python);

        await WaitUntilAsync(() =>
            pane.Status == "Ready" &&
            pane.GeneratedCode.Contains("def main() -> None:", StringComparison.Ordinal));

        Assert.IsTrue(pane.HasValidSource);
        Assert.AreEqual("Ready", viewModel.OperationStatus);
        Assert.AreEqual("Ready", pane.Status);
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
    public async Task Visible_build_run_executes_objective_c_swift_and_cpp_when_available()
    {
        var objectiveC = new FakeToolchain(TargetLanguage.ObjectiveC);
        var swift = new FakeToolchain(TargetLanguage.Swift);
        var cpp = new FakeToolchain(TargetLanguage.Cpp);
        var viewModel = new MainWindowViewModel(
            CreateRegistry(objectiveC, swift, cpp),
            new FakeErrorReporter(),
            new FakeFolderOpener())
        {
            OpenGeneratedFolderAfterBuild = false
        };
        await viewModel.InitializeAsync();

        viewModel.Pane1.SelectedLanguageOption = viewModel.Pane1.LanguageOptions.Single(option => option.Language == TargetLanguage.ObjectiveC);
        viewModel.Pane2.SelectedLanguageOption = viewModel.Pane2.LanguageOptions.Single(option => option.Language == TargetLanguage.Swift);
        viewModel.Pane3.SelectedLanguageOption = viewModel.Pane3.LanguageOptions.Single(option => option.Language == TargetLanguage.Cpp);

        viewModel.BuildRunVisibleCommand.Execute(null);

        await WaitUntilAsync(() => !viewModel.IsBusy && viewModel.OperationStatus == "Completed");

        Assert.AreEqual(1, objectiveC.BuildRuns);
        Assert.AreEqual(1, swift.BuildRuns);
        Assert.AreEqual(1, cpp.BuildRuns);
        Assert.AreEqual("Completed", viewModel.Pane1.Status);
        Assert.AreEqual("Completed", viewModel.Pane2.Status);
        StringAssert.Contains(viewModel.OutputText, "Objective-C detected.");
        StringAssert.Contains(viewModel.OutputText, "Swift detected.");
        StringAssert.Contains(viewModel.OutputText, "C++ detected.");
    }

    [TestMethod]
    public async Task Visible_build_run_continues_after_one_target_throws()
    {
        var csharp = new FakeToolchain(TargetLanguage.CSharp);
        var masm = new FakeToolchain(
            TargetLanguage.MasmX64,
            buildRunException: new IOException("workspace write failed"));
        var c = new FakeToolchain(TargetLanguage.C);
        var viewModel = new MainWindowViewModel(
            CreateRegistry(csharp, masm, c),
            new FakeErrorReporter(),
            new FakeFolderOpener())
        {
            OpenGeneratedFolderAfterBuild = false
        };
        await viewModel.InitializeAsync();

        viewModel.BuildRunVisibleCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsBusy && viewModel.OperationStatus == "Completed with failures");

        Assert.AreEqual(1, csharp.BuildRuns);
        Assert.AreEqual(1, masm.BuildRuns);
        Assert.AreEqual(1, c.BuildRuns);
        Assert.AreEqual("Completed", viewModel.Pane1.Status);
        Assert.AreEqual("Failed", viewModel.Pane2.Status);
        Assert.AreEqual("Completed", viewModel.Pane3.Status);
        StringAssert.Contains(viewModel.OutputText, "Unexpected failure during Assembling.");
        StringAssert.Contains(viewModel.OutputText, "workspace write failed");
    }

    [TestMethod]
    public async Task Build_run_operation_recovers_after_toolchain_exception()
    {
        var failing = new FakeToolchain(
            TargetLanguage.CSharp,
            buildRunException: new InvalidOperationException("compiler launch failed"));
        var viewModel = new MainWindowViewModel(
            CreateRegistry(failing),
            new FakeErrorReporter(),
            new FakeFolderOpener())
        {
            OpenGeneratedFolderAfterBuild = false
        };
        await viewModel.InitializeAsync();

        viewModel.Pane1.BuildRunCommand!.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsBusy && viewModel.OperationStatus == "Failed");

        Assert.AreEqual("Failed", viewModel.Pane1.Status);
        StringAssert.Contains(viewModel.OutputText, "C#");
        StringAssert.Contains(viewModel.OutputText, "Unexpected failure during Building.");
        Assert.IsTrue(viewModel.BuildRunVisibleCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task Toolchain_detection_failure_is_target_specific()
    {
        var failing = new FakeToolchain(
            TargetLanguage.CSharp,
            detectException: new InvalidOperationException("broken SDK"));
        var viewModel = new MainWindowViewModel(
            CreateRegistry(failing),
            new FakeErrorReporter(),
            new FakeFolderOpener());

        await viewModel.InitializeAsync();

        Assert.AreEqual("Detection Failed", viewModel.Pane1.Status);
        StringAssert.Contains(viewModel.Pane1.ToolchainStatusText, "Detection failed");
        StringAssert.Contains(viewModel.OutputText, "broken SDK");
        Assert.IsFalse(viewModel.IsBusy);
    }

    [TestMethod]
    public async Task Folder_opening_failure_is_reported_as_warning()
    {
        string workspace = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "SMILE-Test-" + Guid.NewGuid())).FullName;
        try
        {
            var viewModel = new MainWindowViewModel(
                CreateRegistry(new FakeToolchain(TargetLanguage.CSharp, workingDirectory: workspace)),
                new FakeErrorReporter(),
                new FakeFolderOpener(new InvalidOperationException("explorer failed")));
            await viewModel.InitializeAsync();

            viewModel.Pane1.BuildRunCommand!.Execute(null);
            await WaitUntilAsync(() => !viewModel.IsBusy && viewModel.OperationStatus == "Completed");

            StringAssert.Contains(viewModel.OutputText, "Build completed, but the generated folder could not be opened.");
            StringAssert.Contains(viewModel.OutputText, "explorer failed");
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [TestMethod]
    public void Desktop_output_history_is_bounded()
    {
        var viewModel = new MainWindowViewModel(
            CreateRegistry(),
            new FakeErrorReporter(),
            new FakeFolderOpener());

        viewModel.AppendOutputForTesting(new string('a', MainWindowViewModel.MaxOutputTextLength));
        viewModel.AppendOutputForTesting("newest output");

        Assert.IsLessThanOrEqualTo(MainWindowViewModel.MaxOutputTextLength, viewModel.OutputText.Length);
        StringAssert.Contains(viewModel.OutputText, MainWindowViewModel.OutputTruncatedMarker);
        StringAssert.Contains(viewModel.OutputText, "newest output");
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

    private static string NormalizeLineEndings(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static string CreateNestedIfSource(int depth) =>
        string.Concat(Enumerable.Repeat("IF TRUE = TRUE THEN\n", depth)) +
        "PRINT Reached\n" +
        string.Concat(Enumerable.Repeat("END IF\n", depth));

    private static ToolchainRegistry CreateRegistry(params FakeToolchain[] overrides)
    {
        var byLanguage = overrides.ToDictionary(toolchain => toolchain.Language);
        var toolchains = TargetLanguageInfo.All.Select(language =>
            byLanguage.TryGetValue(language, out FakeToolchain? toolchain)
                ? toolchain
                : new FakeToolchain(language));

        return new ToolchainRegistry(toolchains);
    }

    private sealed class FakeToolchain : IToolchain
    {
        private readonly Exception? _detectException;
        private readonly Exception? _buildRunException;
        private readonly string? _workingDirectory;

        public FakeToolchain(
            TargetLanguage language,
            Exception? detectException = null,
            Exception? buildRunException = null,
            string? workingDirectory = null)
        {
            Language = language;
            _detectException = detectException;
            _buildRunException = buildRunException;
            _workingDirectory = workingDirectory;
        }

        public TargetLanguage Language { get; }

        public int BuildRuns { get; private set; }

        public Task<ToolchainStatus> DetectAsync(CancellationToken cancellationToken)
        {
            if (_detectException is not null)
            {
                throw _detectException;
            }

            string name = TargetLanguageInfo.GetDisplayName(Language);
            return Task.FromResult(new ToolchainStatus(Language, true, name, "test", "test", $"{name} detected."));
        }

        public async Task<BuildRunResult> BuildAndRunAsync(
            GeneratedProgram generatedProgram,
            CancellationToken cancellationToken,
            BuildRunOptions? options = null)
        {
            BuildRuns++;
            if (_buildRunException is not null)
            {
                throw _buildRunException;
            }

            ToolchainStatus status = await DetectAsync(cancellationToken);
            return new BuildRunResult(
                Language,
                Success: true,
                status,
                "Build completed.",
                "Program output.",
                string.Empty,
                0,
                TimeSpan.FromMilliseconds(1),
                TimedOut: false,
                Cancelled: false,
                _workingDirectory,
                null,
                "Running");
        }
    }

    private sealed class FakeFolderOpener : IFolderOpener
    {
        private readonly Exception? _exception;

        public FakeFolderOpener(Exception? exception = null)
        {
            _exception = exception;
        }

        public Task OpenAsync(string folderPath, CancellationToken cancellationToken)
        {
            if (_exception is not null)
            {
                throw _exception;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakeErrorReporter : IAppErrorReporter
    {
        public string SessionId => "test-session";

        public string Report(
            string operation,
            Exception exception,
            string? target = null,
            string? stage = null,
            long? sourceRevision = null) =>
            @"C:\SMILE\Test.log";
    }
}
