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
    public void Desktop_assembly_reports_the_v080_WHILE_Loops_release()
    {
        string? version = typeof(MainWindowViewModel).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        Assert.IsNotNull(version);
        Assert.AreEqual("0.8.0 WHILE Loops", version);
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

        Assert.IsFalse(pane.CanBuild);

        pane.GeneratedCode = "console.log('learner edit');";

        Assert.IsTrue(pane.CanBuild);
        Assert.IsTrue(pane.HasUserEdits);

        pane.SelectedLanguageOption = pane.LanguageOptions.Single(option => option.Language == TargetLanguage.Cpp);

        Assert.AreEqual("Pane - C++", pane.Title);
        Assert.AreEqual("Build & Run", pane.BuildButtonText);

        pane.IsBusy = true;

        Assert.IsFalse(pane.CanBuild);
        Assert.IsFalse(pane.CanChangeLanguage);
    }

    [TestMethod]
    public void Target_pane_tracks_user_edit_revisions_and_marks_generated_divergence()
    {
        var pane = new TargetPaneViewModel("Pane", TargetLanguage.CSharp);
        var changedProperties = new List<string?>();
        pane.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        pane.ApplyGeneratedCode("// generated");

        Assert.AreEqual(0L, pane.UserEditRevision);
        Assert.IsFalse(pane.HasUserEdits);
        Assert.AreEqual("Pane - C#", pane.DisplayTitle);
        Assert.AreEqual("Pane - C#", pane.Title);

        pane.GeneratedCode = "// learner edit 1";

        Assert.AreEqual(1L, pane.UserEditRevision);
        Assert.IsTrue(pane.HasUserEdits);
        Assert.AreEqual("Pane - C#", pane.DisplayTitle);
        Assert.AreEqual("Pane - C# *", pane.Title);
        CollectionAssert.Contains(changedProperties, nameof(TargetPaneViewModel.UserEditRevision));
        CollectionAssert.Contains(changedProperties, nameof(TargetPaneViewModel.Title));

        pane.GeneratedCode = "// learner edit 2";
        pane.IsMaximized = true;
        pane.IsMaximized = false;

        Assert.AreEqual(2L, pane.UserEditRevision);
        Assert.AreEqual("Pane - C# *", pane.Title);

        pane.ApplyGeneratedCode("// generated replacement");

        Assert.AreEqual(2L, pane.UserEditRevision);
        Assert.IsFalse(pane.HasUserEdits);
        Assert.AreEqual("Pane - C#", pane.Title);

        pane.GeneratedCode = "// temporary Swift edit";
        pane.SelectedLanguageOption = pane.LanguageOptions.Single(
            option => option.Language == TargetLanguage.Swift);

        Assert.AreEqual(3L, pane.UserEditRevision);
        Assert.IsFalse(pane.HasUserEdits);
        Assert.AreEqual("Pane - Swift", pane.DisplayTitle);
        Assert.AreEqual("Pane - Swift", pane.Title);
    }

    [TestMethod]
    public void Target_pane_reports_Maximize_and_Restore_state()
    {
        var pane = new TargetPaneViewModel("Pane", TargetLanguage.CSharp);

        Assert.IsFalse(pane.IsMaximized);
        Assert.AreEqual("Maximize", pane.MaximizeButtonText);

        pane.IsMaximized = true;

        Assert.AreEqual("Restore", pane.MaximizeButtonText);

        pane.IsMaximized = false;

        Assert.AreEqual("Maximize", pane.MaximizeButtonText);
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
        string runtimeInputExamplePath = Path.Combine(AppContext.BaseDirectory, "input.smile");
        Assert.IsTrue(File.Exists(runtimeInputExamplePath), runtimeInputExamplePath);
        EvaluationResult inputExample = new SmileEvaluator().Evaluate(
            await File.ReadAllTextAsync(runtimeInputExamplePath),
            InputTestData.CanonicalScriptedInput);
        Assert.IsTrue(
            inputExample.Success,
            string.Join(Environment.NewLine, inputExample.Diagnostics) + inputExample.StandardError);
        string runtimeWhileExamplePath = Path.Combine(AppContext.BaseDirectory, "while.smile");
        Assert.IsTrue(File.Exists(runtimeWhileExamplePath), runtimeWhileExamplePath);
        EvaluationResult whileExample = new SmileEvaluator().Evaluate(
            await File.ReadAllTextAsync(runtimeWhileExamplePath),
            "3\n");
        Assert.IsTrue(
            whileExample.Success,
            string.Join(Environment.NewLine, whileExample.Diagnostics) + whileExample.StandardError);

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
        StringAssert.Contains(viewModel.SourceText, "REM Full-line comments and source layout examples:");
        StringAssert.Contains(viewModel.SourceText, "rEm This mixed-case marker is also a comment.");
        StringAssert.Contains(viewModel.SourceText, "// C-family-style full-line comment.");
        StringAssert.Contains(viewModel.SourceText, "# Script-language-style full-line comment.");
        StringAssert.Contains(viewModel.SourceText, "-- SQL, Ada, and Haskell-style full-line comment.");
        StringAssert.Contains(viewModel.SourceText, "LET REM = \"REM remains a valid variable name.\"");
        StringAssert.Contains(viewModel.SourceText, "Rem Nested REM comments are valid.");
        StringAssert.Contains(viewModel.SourceText, "PRINT // This text is printed");
        StringAssert.Contains(viewModel.SourceText, "PRINT https://example.com");
        StringAssert.Contains(viewModel.SourceText, "PRINT \"INPUT statement examples in language.smile:\"");
        StringAssert.Contains(viewModel.SourceText, "INPUT InputName");
        StringAssert.Contains(viewModel.SourceText, "INPUT InputAge");
        StringAssert.Contains(viewModel.SourceText, "INPUT InputReady");
        StringAssert.Contains(viewModel.SourceText, "IF InputAge >= 18 THEN");
        StringAssert.Contains(viewModel.SourceText, "PRINT \"WHILE statement examples in language.smile:\"");
        StringAssert.Contains(viewModel.SourceText, "WHILE LoopCount <= 3");
        StringAssert.Contains(viewModel.SourceText, "SET LoopCount = LoopCount + 1");
        StringAssert.Contains(viewModel.SourceText, "END WHILE");
        StringAssert.Contains(
            normalizedSource,
            "LET CommentLayoutMessage = \"\"\n\n\n\nIF REM = \"REM remains a valid variable name.\" THEN");
        StringAssert.Contains(
            normalizedSource,
            "SET CommentLayoutMessage =\"\nREM Block String data\n\n// Block String data\n# Block String data\n-- Block String data\n\"");

        EvaluationResult evaluation = new SmileEvaluator().Evaluate(
            viewModel.SourceText,
            InputTestData.CanonicalScriptedInput);
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
    public async Task New_command_clears_the_SMILE_and_target_editors_and_keeps_them_blank()
    {
        var viewModel = new MainWindowViewModel(
            CreateRegistry(),
            new FakeErrorReporter(),
            new FakeFolderOpener());
        await viewModel.InitializeAsync();
        viewModel.SourceText = "PRINT temporary edit";
        await WaitUntilAsync(() => viewModel.Panes.All(pane => pane.GeneratedCode.Contains("temporary edit", StringComparison.Ordinal)));
        foreach (TargetPaneViewModel pane in viewModel.Panes)
        {
            pane.GeneratedCode += Environment.NewLine + "// learner target edit";
        }

        viewModel.NewCommand.Execute(null);

        Assert.AreEqual(string.Empty, viewModel.SourceText);
        Assert.IsTrue(viewModel.Panes.All(pane => string.IsNullOrEmpty(pane.GeneratedCode)));
        Assert.IsTrue(viewModel.Panes.All(pane => !pane.HasValidSource && !pane.HasSyntaxError));
        Assert.IsTrue(viewModel.Panes.All(pane => !pane.HasUserEdits));
        Assert.IsTrue(viewModel.Panes.All(pane => !pane.Title.EndsWith(" *", StringComparison.Ordinal)));
        Assert.IsTrue(viewModel.Panes.All(pane => pane.CopyCommand?.CanExecute(null) == false));
        Assert.IsTrue(viewModel.Panes.All(pane => pane.SaveSourceCommand?.CanExecute(null) == false));
        Assert.IsTrue(viewModel.Panes.All(pane => pane.BuildRunCommand?.CanExecute(null) == false));
        Assert.IsFalse(viewModel.BuildRunVisibleCommand.CanExecute(null));
        Assert.AreEqual("Ready", viewModel.OperationStatus);

        await Task.Delay(400);
        viewModel.Pane3.SelectedLanguageOption = viewModel.Pane3.LanguageOptions.Single(
            option => option.Language == TargetLanguage.Python);
        await Task.Delay(50);

        Assert.AreEqual(string.Empty, viewModel.SourceText);
        Assert.IsTrue(viewModel.Panes.All(pane => string.IsNullOrEmpty(pane.GeneratedCode)));
    }

    [TestMethod]
    public async Task New_cancels_a_pending_live_transpilation_and_keeps_every_editor_blank()
    {
        var viewModel = new MainWindowViewModel(
            CreateRegistry(),
            new FakeErrorReporter(),
            new FakeFolderOpener());
        await viewModel.InitializeAsync();

        viewModel.SourceText = "PRINT this debounce must not win";
        viewModel.NewCommand.Execute(null);

        await Task.Delay(400);

        Assert.AreEqual(string.Empty, viewModel.SourceText);
        Assert.IsTrue(viewModel.Panes.All(pane => string.IsNullOrEmpty(pane.GeneratedCode)));
        Assert.IsTrue(viewModel.Panes.All(pane => !pane.HasUserEdits && !pane.HasValidSource));
        Assert.IsTrue(viewModel.Panes.All(pane => pane.Status == "Ready"));
        Assert.IsFalse(viewModel.BuildRunVisibleCommand.CanExecute(null));
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
    public void New_command_does_not_read_the_packaged_language_reference()
    {
        int reads = 0;
        var viewModel = new MainWindowViewModel(
            CreateRegistry(),
            new FakeErrorReporter(),
            new FakeFolderOpener(),
            languageFilePath: null,
            _ =>
            {
                reads++;
                throw new IOException("New must not read language.smile.");
            })
        {
            SourceText = "PRINT Clear this work"
        };

        viewModel.NewCommand.Execute(null);

        Assert.AreEqual(0, reads);
        Assert.AreEqual(string.Empty, viewModel.SourceText);
        Assert.IsTrue(viewModel.Panes.All(pane => string.IsNullOrEmpty(pane.GeneratedCode)));
        Assert.AreEqual("Ready", viewModel.OperationStatus);
    }

    [TestMethod]
    public async Task New_wins_when_the_startup_language_reference_read_is_still_pending()
    {
        var delayedRead = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = new MainWindowViewModel(
            CreateRegistry(),
            new FakeErrorReporter(),
            new FakeFolderOpener(),
            languageFilePath: null,
            _ => delayedRead.Task);

        Task initialization = viewModel.InitializeAsync();
        viewModel.NewCommand.Execute(null);
        delayedRead.SetResult("PRINT Packaged reference");

        await initialization;
        await Task.Delay(400);

        Assert.AreEqual(string.Empty, viewModel.SourceText);
        Assert.IsTrue(viewModel.Panes.All(pane => string.IsNullOrEmpty(pane.GeneratedCode)));
        Assert.IsTrue(viewModel.Panes.All(pane => !pane.HasValidSource && !pane.HasSyntaxError));
        Assert.IsTrue(viewModel.Panes.All(pane => pane.Status == "Ready"));
        Assert.AreEqual("Ready", viewModel.OperationStatus);
    }

    [TestMethod]
    public async Task New_wins_even_when_it_runs_before_startup_initialization_begins()
    {
        var viewModel = new MainWindowViewModel(
            CreateRegistry(),
            new FakeErrorReporter(),
            new FakeFolderOpener(),
            languageFilePath: null,
            _ => Task.FromResult("PRINT Packaged reference"));

        viewModel.NewCommand.Execute(null);
        await viewModel.InitializeAsync();

        Assert.AreEqual(string.Empty, viewModel.SourceText);
        Assert.IsTrue(viewModel.Panes.All(pane => string.IsNullOrEmpty(pane.GeneratedCode)));
        Assert.IsTrue(viewModel.Panes.All(pane => !pane.HasUserEdits && !pane.HasValidSource));
        Assert.IsTrue(viewModel.Panes.All(pane => pane.Status == "Ready"));
        Assert.AreEqual("Ready", viewModel.OperationStatus);
    }

    [TestMethod]
    public async Task Target_edit_after_New_survives_pending_startup_detection()
    {
        var delayedRead = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = new MainWindowViewModel(
            CreateRegistry(),
            new FakeErrorReporter(),
            new FakeFolderOpener(),
            languageFilePath: null,
            _ => delayedRead.Task);

        Task initialization = viewModel.InitializeAsync();
        viewModel.NewCommand.Execute(null);
        viewModel.Pane1.GeneratedCode = "Console.WriteLine(\"learner target edit\");";
        delayedRead.SetResult("PRINT Packaged reference");

        await initialization;

        Assert.AreEqual(string.Empty, viewModel.SourceText);
        Assert.AreEqual("Console.WriteLine(\"learner target edit\");", viewModel.Pane1.GeneratedCode);
        Assert.IsTrue(viewModel.Pane1.HasUserEdits);
        Assert.IsTrue(viewModel.Pane1.HasValidSource);
        Assert.IsTrue(viewModel.Pane1.CanBuild);
        Assert.IsTrue(viewModel.Panes.Skip(1).All(pane => string.IsNullOrEmpty(pane.GeneratedCode)));
        Assert.IsTrue(viewModel.Panes.All(pane => pane.Status == "Ready"));
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
    public async Task Target_build_uses_the_edited_primary_source_and_preserves_generated_companions()
    {
        var csharp = new FakeToolchain(TargetLanguage.CSharp);
        var viewModel = new MainWindowViewModel(
            CreateRegistry(csharp),
            new FakeErrorReporter(),
            new FakeFolderOpener())
        {
            OpenGeneratedFolderAfterBuild = false
        };
        await viewModel.InitializeAsync();
        viewModel.SourceText = "LET Name = \"\"\nINPUT Name\nPRINT {Name}";
        await WaitUntilAsync(() =>
            viewModel.Pane1.HasValidSource &&
            viewModel.Pane1.GeneratedCode.Contains("SMILER1501", StringComparison.Ordinal));

        GeneratedProgram generated = new SmileTranspiler()
            .Transpile(viewModel.SourceText, TargetLanguage.CSharp)
            .GeneratedProgram!;
        string editedSource = viewModel.Pane1.GeneratedCode + "// learner target edit\n";
        viewModel.Pane1.GeneratedCode = editedSource;

        Assert.IsTrue(viewModel.Pane1.HasUserEdits);
        Assert.IsTrue(viewModel.Pane1.CanBuild);
        Assert.AreEqual("Generated target 1 - C# *", viewModel.Pane1.Title);

        viewModel.Pane1.BuildRunCommand!.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsBusy && viewModel.OperationStatus == "Completed");

        Assert.IsNotNull(csharp.LastGeneratedProgram);
        Assert.AreEqual(editedSource, csharp.LastGeneratedProgram.PrimaryFile.Content);
        Assert.IsTrue(csharp.LastGeneratedProgram.RequiresStandardInput);
        Assert.IsTrue(viewModel.Pane1.HasUserEdits);
        Assert.AreEqual("Generated target 1 - C# *", viewModel.Pane1.Title);

        GeneratedFile[] expectedCompanions = generated.Files.Where(file => !file.IsPrimary).ToArray();
        GeneratedFile[] actualCompanions = csharp.LastGeneratedProgram.Files.Where(file => !file.IsPrimary).ToArray();
        CollectionAssert.AreEqual(expectedCompanions, actualCompanions);
    }

    [TestMethod]
    public async Task Save_Source_preserves_the_target_edit_marker()
    {
        string workspace = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "SMILE-Test-" + Guid.NewGuid())).FullName;
        string savePath = Path.Combine(workspace, "Program.cs");

        try
        {
            var viewModel = new MainWindowViewModel(
                CreateRegistry(),
                new FakeErrorReporter(),
                new FakeFolderOpener(),
                languageFilePath: null,
                languageSourceReader: null,
                generatedSourcePathSelector: (_, _) => savePath);
            await viewModel.InitializeAsync();

            string editedSource = viewModel.Pane1.GeneratedCode + "// saved learner target edit\n";
            viewModel.Pane1.GeneratedCode = editedSource;

            Assert.IsTrue(viewModel.Pane1.HasUserEdits);
            Assert.AreEqual("Generated target 1 - C# *", viewModel.Pane1.Title);

            viewModel.Pane1.SaveSourceCommand!.Execute(null);
            await WaitUntilAsync(() => viewModel.OperationStatus == "Saved Program.cs");

            Assert.AreEqual(editedSource, await File.ReadAllTextAsync(savePath));
            Assert.IsTrue(viewModel.Pane1.HasUserEdits);
            Assert.AreEqual("Generated target 1 - C# *", viewModel.Pane1.Title);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [TestMethod]
    public async Task Target_build_before_live_debounce_uses_the_current_SMILE_metadata()
    {
        var csharp = new FakeToolchain(TargetLanguage.CSharp);
        var viewModel = new MainWindowViewModel(
            CreateRegistry(csharp),
            new FakeErrorReporter(),
            new FakeFolderOpener())
        {
            OpenGeneratedFolderAfterBuild = false
        };
        await viewModel.InitializeAsync();

        viewModel.SourceText = "LET Name = \"\"\nINPUT Name\nPRINT {Name}";
        const string editedSource = "Console.WriteLine(\"edited before debounce\");";
        viewModel.Pane1.GeneratedCode = editedSource;

        viewModel.Pane1.BuildRunCommand!.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsBusy && csharp.BuildRuns == 1);

        Assert.IsNotNull(csharp.LastGeneratedProgram);
        Assert.AreEqual(editedSource, csharp.LastGeneratedProgram.PrimaryFile.Content);
        Assert.IsTrue(csharp.LastGeneratedProgram.RequiresStandardInput);
        Assert.IsTrue(csharp.LastGeneratedProgram.Files.Any(file => file.RelativePath == "GeneratedProgram.csproj"));

        TranspileResult expectedMasm = new SmileTranspiler().Transpile(
            viewModel.SourceText,
            TargetLanguage.MasmX64);
        TranspileResult expectedC = new SmileTranspiler().Transpile(
            viewModel.SourceText,
            TargetLanguage.C);
        await WaitUntilAsync(() =>
            viewModel.Pane2.Status == "Ready" &&
            viewModel.Pane3.Status == "Ready");

        Assert.AreEqual(expectedMasm.GeneratedProgram!.PrimaryFile.Content, viewModel.Pane2.GeneratedCode);
        Assert.AreEqual(expectedC.GeneratedProgram!.PrimaryFile.Content, viewModel.Pane3.GeneratedCode);
        Assert.AreEqual(editedSource, viewModel.Pane1.GeneratedCode);
        Assert.AreEqual("Completed", viewModel.Pane1.Status);
        Assert.AreEqual("Completed", viewModel.OperationStatus);
    }

    [TestMethod]
    public async Task Resumed_preview_preserves_an_unedited_panes_failed_build_status()
    {
        var csharp = new FakeToolchain(
            TargetLanguage.CSharp,
            buildRunException: new InvalidOperationException("injected build failure"));
        var viewModel = new MainWindowViewModel(
            CreateRegistry(csharp),
            new FakeErrorReporter(),
            new FakeFolderOpener())
        {
            OpenGeneratedFolderAfterBuild = false
        };
        await viewModel.InitializeAsync();

        viewModel.Pane3.SelectedLanguageOption = viewModel.Pane3.LanguageOptions.Single(
            option => option.Language == TargetLanguage.Python);
        viewModel.Pane1.BuildRunCommand!.Execute(null);

        await WaitUntilAsync(() =>
            viewModel.Pane3.Status == "Ready" &&
            viewModel.Pane3.GeneratedCode.Contains("def main() -> None:", StringComparison.Ordinal));

        Assert.AreEqual(1, csharp.BuildRuns);
        Assert.IsFalse(viewModel.Pane1.HasUserEdits);
        Assert.AreEqual("Failed", viewModel.Pane1.Status);
        Assert.AreEqual("Failed", viewModel.OperationStatus);
    }

    [TestMethod]
    public async Task Manual_target_edit_survives_an_unrelated_pane_language_switch()
    {
        var viewModel = new MainWindowViewModel(
            CreateRegistry(),
            new FakeErrorReporter(),
            new FakeFolderOpener());
        await viewModel.InitializeAsync();

        string editedSource = viewModel.Pane1.GeneratedCode + "// keep this learner edit\n";
        viewModel.Pane1.GeneratedCode = editedSource;
        viewModel.Pane3.SelectedLanguageOption = viewModel.Pane3.LanguageOptions.Single(
            option => option.Language == TargetLanguage.Python);

        await WaitUntilAsync(() =>
            viewModel.Pane3.Status == "Ready" &&
            viewModel.Pane3.GeneratedCode.Contains("def main() -> None:", StringComparison.Ordinal));

        Assert.AreEqual(editedSource, viewModel.Pane1.GeneratedCode);
        Assert.IsTrue(viewModel.Pane1.HasUserEdits);
        Assert.AreEqual("Edited", viewModel.Pane1.Status);
        Assert.AreEqual("Generated target 1 - C# *", viewModel.Pane1.Title);
    }

    [TestMethod]
    public async Task Manual_target_edit_survives_delayed_startup_toolchain_detection()
    {
        var detectionGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var csharp = new FakeToolchain(TargetLanguage.CSharp, detectGate: detectionGate.Task);
        var viewModel = new MainWindowViewModel(
            CreateRegistry(csharp),
            new FakeErrorReporter(),
            new FakeFolderOpener());

        Task initialization = viewModel.InitializeAsync();
        await WaitUntilAsync(() => viewModel.Pane1.HasValidSource);
        string editedSource = viewModel.Pane1.GeneratedCode + "// keep this startup edit\n";
        viewModel.Pane1.GeneratedCode = editedSource;
        detectionGate.SetResult(true);

        await initialization;

        Assert.AreEqual(editedSource, viewModel.Pane1.GeneratedCode);
        Assert.IsTrue(viewModel.Pane1.HasUserEdits);
        Assert.AreEqual("Edited", viewModel.Pane1.Status);
        Assert.IsTrue(viewModel.Pane1.CanBuild);
        Assert.AreEqual("Generated target 1 - C# *", viewModel.Pane1.Title);
    }

    [TestMethod]
    public async Task Later_SMILE_edits_replace_manual_target_edits_through_live_transpilation()
    {
        var viewModel = new MainWindowViewModel(
            CreateRegistry(),
            new FakeErrorReporter(),
            new FakeFolderOpener());
        await viewModel.InitializeAsync();

        viewModel.Pane1.GeneratedCode = "// temporary learner target edit";
        Assert.IsTrue(viewModel.Pane1.HasUserEdits);

        viewModel.SourceText = "PRINT SMILE replacement wins";
        await WaitUntilAsync(() =>
            viewModel.Pane1.HasValidSource &&
            viewModel.Pane1.GeneratedCode.Contains("SMILE replacement wins", StringComparison.Ordinal));

        Assert.IsFalse(viewModel.Pane1.HasUserEdits);
        Assert.AreEqual("Generated target 1 - C#", viewModel.Pane1.Title);
        Assert.IsFalse(
            viewModel.Pane1.GeneratedCode.Contains("temporary learner target edit", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Explicit_Transpile_All_clears_target_edit_ownership_and_markers()
    {
        var viewModel = new MainWindowViewModel(
            CreateRegistry(),
            new FakeErrorReporter(),
            new FakeFolderOpener());
        await viewModel.InitializeAsync();

        viewModel.Pane1.GeneratedCode = "// temporary target edit";
        Assert.AreEqual("Generated target 1 - C# *", viewModel.Pane1.Title);

        viewModel.TranspileAllCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsBusy && viewModel.OperationStatus == "Completed");

        Assert.IsTrue(viewModel.Panes.All(pane => !pane.HasUserEdits));
        Assert.IsTrue(viewModel.Panes.All(pane => !pane.Title.EndsWith(" *", StringComparison.Ordinal)));
        Assert.IsFalse(
            viewModel.Pane1.GeneratedCode.Contains("temporary target edit", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Newer_target_edits_survive_an_older_live_generation_while_siblings_update()
    {
        var generationGate = new SingleUseLiveGenerationGate();
        var viewModel = new MainWindowViewModel(
            CreateRegistry(),
            new FakeErrorReporter(),
            new FakeFolderOpener(),
            languageFilePath: null,
            languageSourceReader: null,
            liveGenerationGate: generationGate.WaitAsync);
        await viewModel.InitializeAsync();

        generationGate.Arm();
        viewModel.SourceText = "PRINT generation A";
        await generationGate.Entered.WaitAsync(TimeSpan.FromSeconds(2));

        viewModel.Pane1.GeneratedCode = "// learner edit 1";
        viewModel.Pane1.GeneratedCode = "// learner edit 2";
        long learnerRevision = viewModel.Pane1.UserEditRevision;
        generationGate.Release();

        await WaitUntilAsync(() =>
            viewModel.Pane2.GeneratedCode.Contains("generation A", StringComparison.Ordinal) &&
            viewModel.Pane3.GeneratedCode.Contains("generation A", StringComparison.Ordinal));

        Assert.AreEqual("// learner edit 2", viewModel.Pane1.GeneratedCode);
        Assert.AreEqual(learnerRevision, viewModel.Pane1.UserEditRevision);
        Assert.IsTrue(viewModel.Pane1.HasUserEdits);
        Assert.AreEqual("Edited", viewModel.Pane1.Status);
        Assert.AreEqual("Generated target 1 - C# *", viewModel.Pane1.Title);
        Assert.IsFalse(viewModel.Pane2.HasUserEdits);
        Assert.IsFalse(viewModel.Pane3.HasUserEdits);
    }

    [TestMethod]
    public async Task Newer_target_edit_survives_an_older_WHILE_live_generation_while_siblings_update()
    {
        var generationGate = new SingleUseLiveGenerationGate();
        var viewModel = new MainWindowViewModel(
            CreateRegistry(),
            new FakeErrorReporter(),
            new FakeFolderOpener(),
            languageFilePath: null,
            languageSourceReader: null,
            liveGenerationGate: generationGate.WaitAsync);
        await viewModel.InitializeAsync();

        const string whileSource = """
LET Count = 0
WHILE Count < 2
    PRINT WHILE live generation Count={Count}
    SET Count = Count + 1
END WHILE
""";
        generationGate.Arm();
        viewModel.SourceText = whileSource;
        await generationGate.Entered.WaitAsync(TimeSpan.FromSeconds(2));

        viewModel.Pane1.GeneratedCode = "// learner edit after WHILE generation began";
        long learnerRevision = viewModel.Pane1.UserEditRevision;
        generationGate.Release();

        await WaitUntilAsync(() =>
            viewModel.Pane2.GeneratedCode.Contains("WHILE live generation", StringComparison.Ordinal) &&
            viewModel.Pane3.GeneratedCode.Contains("WHILE live generation", StringComparison.Ordinal));

        Assert.AreEqual("// learner edit after WHILE generation began", viewModel.Pane1.GeneratedCode);
        Assert.AreEqual(learnerRevision, viewModel.Pane1.UserEditRevision);
        Assert.IsTrue(viewModel.Pane1.HasUserEdits);
        Assert.IsFalse(viewModel.Pane2.HasUserEdits);
        Assert.IsFalse(viewModel.Pane3.HasUserEdits);
    }

    [TestMethod]
    public async Task Same_language_sibling_panes_keep_independent_live_generation_ownership()
    {
        var generationGate = new SingleUseLiveGenerationGate();
        var viewModel = new MainWindowViewModel(
            CreateRegistry(),
            new FakeErrorReporter(),
            new FakeFolderOpener(),
            languageFilePath: null,
            languageSourceReader: null,
            liveGenerationGate: generationGate.WaitAsync);
        await viewModel.InitializeAsync();
        SelectLanguage(viewModel.Pane2, TargetLanguage.CSharp);
        await WaitUntilAsync(() => viewModel.Pane2.Status == "Ready");

        generationGate.Arm();
        viewModel.SourceText = "PRINT same-language generation";
        await generationGate.Entered.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.Pane2.GeneratedCode = "// only pane 2 changed";
        generationGate.Release();

        await WaitUntilAsync(() =>
            viewModel.Pane1.GeneratedCode.Contains("same-language generation", StringComparison.Ordinal) &&
            viewModel.Pane3.GeneratedCode.Contains("same-language generation", StringComparison.Ordinal));

        StringAssert.Contains(viewModel.Pane1.GeneratedCode, "same-language generation");
        Assert.AreEqual("// only pane 2 changed", viewModel.Pane2.GeneratedCode);
        Assert.IsFalse(viewModel.Pane1.HasUserEdits);
        Assert.IsTrue(viewModel.Pane2.HasUserEdits);
        Assert.AreEqual("Generated target 2 - C# *", viewModel.Pane2.Title);
    }

    [TestMethod]
    public async Task Target_edit_after_a_same_pane_language_switch_survives_older_generation()
    {
        var generationGate = new SingleUseLiveGenerationGate();
        var viewModel = new MainWindowViewModel(
            CreateRegistry(),
            new FakeErrorReporter(),
            new FakeFolderOpener(),
            languageFilePath: null,
            languageSourceReader: null,
            liveGenerationGate: generationGate.WaitAsync);
        await viewModel.InitializeAsync();

        viewModel.Pane3.GeneratedCode = "// old C edit";
        Assert.AreEqual("Generated target 3 - C *", viewModel.Pane3.Title);

        generationGate.Arm();
        SelectLanguage(viewModel.Pane3, TargetLanguage.Java);
        Assert.IsFalse(viewModel.Pane3.HasUserEdits);
        Assert.AreEqual("Generated target 3 - Java", viewModel.Pane3.Title);
        await generationGate.Entered.WaitAsync(TimeSpan.FromSeconds(2));

        viewModel.Pane3.GeneratedCode = "// newer Java edit";
        generationGate.Release();
        await WaitUntilAsync(() => viewModel.OperationStatus == "Ready");

        Assert.AreEqual(TargetLanguage.Java, viewModel.Pane3.Language);
        Assert.AreEqual("// newer Java edit", viewModel.Pane3.GeneratedCode);
        Assert.IsTrue(viewModel.Pane3.HasUserEdits);
        Assert.AreEqual("Edited", viewModel.Pane3.Status);
        Assert.AreEqual("Generated target 3 - Java *", viewModel.Pane3.Title);
    }

    [TestMethod]
    public async Task Target_edit_after_a_second_SMILE_edit_wins_over_that_pending_generation()
    {
        var generationGate = new SingleUseLiveGenerationGate();
        var viewModel = new MainWindowViewModel(
            CreateRegistry(),
            new FakeErrorReporter(),
            new FakeFolderOpener(),
            languageFilePath: null,
            languageSourceReader: null,
            liveGenerationGate: generationGate.WaitAsync);
        await viewModel.InitializeAsync();

        viewModel.Pane1.GeneratedCode = "// target edit T1";
        long firstTargetRevision = viewModel.Pane1.UserEditRevision;
        generationGate.Arm();
        viewModel.SourceText = "PRINT SMILE edit B";
        await generationGate.Entered.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.IsFalse(viewModel.Pane1.HasUserEdits);
        viewModel.Pane1.GeneratedCode = "// target edit T2";
        generationGate.Release();

        await WaitUntilAsync(() =>
            viewModel.Pane2.GeneratedCode.Contains("SMILE edit B", StringComparison.Ordinal) &&
            viewModel.Pane3.GeneratedCode.Contains("SMILE edit B", StringComparison.Ordinal));

        Assert.AreEqual(firstTargetRevision + 1, viewModel.Pane1.UserEditRevision);
        Assert.AreEqual("// target edit T2", viewModel.Pane1.GeneratedCode);
        Assert.IsTrue(viewModel.Pane1.HasUserEdits);
        Assert.AreEqual("Edited", viewModel.Pane1.Status);
    }

    [TestMethod]
    public async Task Live_syntax_diagnostics_do_not_erase_a_newer_target_edit()
    {
        var generationGate = new SingleUseLiveGenerationGate();
        var viewModel = new MainWindowViewModel(
            CreateRegistry(),
            new FakeErrorReporter(),
            new FakeFolderOpener(),
            languageFilePath: null,
            languageSourceReader: null,
            liveGenerationGate: generationGate.WaitAsync);
        await viewModel.InitializeAsync();

        generationGate.Arm();
        viewModel.SourceText = "LET MissingValue =";
        await generationGate.Entered.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.Pane1.GeneratedCode = "// valid learner-owned target source";
        generationGate.Release();

        await WaitUntilAsync(() =>
            viewModel.Panes.Skip(1).All(pane => pane.HasSyntaxError) &&
            viewModel.OperationStatus == "Syntax Error");

        Assert.AreEqual("// valid learner-owned target source", viewModel.Pane1.GeneratedCode);
        Assert.IsTrue(viewModel.Pane1.HasUserEdits);
        Assert.IsFalse(viewModel.Pane1.HasSyntaxError);
        Assert.AreEqual("Edited", viewModel.Pane1.Status);
        StringAssert.Contains(viewModel.OutputText, "SMILE");
    }

    [TestMethod]
    public async Task Blank_New_document_can_build_code_written_directly_in_a_target_editor()
    {
        var csharp = new FakeToolchain(TargetLanguage.CSharp);
        var viewModel = new MainWindowViewModel(
            CreateRegistry(csharp),
            new FakeErrorReporter(),
            new FakeFolderOpener())
        {
            OpenGeneratedFolderAfterBuild = false
        };
        await viewModel.InitializeAsync();
        viewModel.NewCommand.Execute(null);

        const string editedSource = "using System;\nConsole.WriteLine(\"target-only edit\");\n";
        int globalCommandChanges = 0;
        viewModel.BuildRunVisibleCommand.CanExecuteChanged += (_, _) => globalCommandChanges++;
        viewModel.Pane1.GeneratedCode = editedSource;

        Assert.IsTrue(viewModel.Pane1.CanBuild);
        Assert.AreEqual("Generated target 1 - C# *", viewModel.Pane1.Title);
        Assert.IsTrue(viewModel.BuildRunVisibleCommand.CanExecute(null));
        Assert.IsGreaterThan(0, globalCommandChanges);
        viewModel.Pane1.BuildRunCommand!.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsBusy && viewModel.OperationStatus == "Completed");

        Assert.IsNotNull(csharp.LastGeneratedProgram);
        Assert.AreEqual(editedSource, csharp.LastGeneratedProgram.PrimaryFile.Content);
        Assert.IsTrue(csharp.LastGeneratedProgram.Files.Any(file => file.RelativePath == "GeneratedProgram.csproj"));
        Assert.IsFalse(csharp.LastGeneratedProgram.RequiresStandardInput);
        Assert.AreEqual(string.Empty, viewModel.SourceText);
    }

    [TestMethod]
    public async Task Direct_target_edit_can_build_after_the_SMILE_source_has_a_syntax_error()
    {
        var csharp = new FakeToolchain(TargetLanguage.CSharp);
        var viewModel = new MainWindowViewModel(
            CreateRegistry(csharp),
            new FakeErrorReporter(),
            new FakeFolderOpener())
        {
            OpenGeneratedFolderAfterBuild = false
        };
        await viewModel.InitializeAsync();
        viewModel.SourceText = "LET MissingValue =";
        await WaitUntilAsync(() => viewModel.Panes.All(pane => pane.HasSyntaxError));

        const string editedSource = "Console.WriteLine(\"target source is independent\");";
        viewModel.Pane1.GeneratedCode = editedSource;

        Assert.IsFalse(viewModel.Pane1.HasSyntaxError);
        Assert.AreEqual("Edited", viewModel.Pane1.Status);
        Assert.IsTrue(viewModel.Pane1.CanBuild);
        Assert.IsTrue(viewModel.BuildRunVisibleCommand.CanExecute(null));

        viewModel.BuildRunVisibleCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsBusy && viewModel.OperationStatus == "Completed");

        Assert.AreEqual(1, csharp.BuildRuns);
        Assert.IsNotNull(csharp.LastGeneratedProgram);
        Assert.AreEqual(editedSource, csharp.LastGeneratedProgram.PrimaryFile.Content);
        Assert.AreEqual(editedSource, viewModel.Pane1.GeneratedCode);
        Assert.IsTrue(viewModel.Panes.Skip(1).All(pane => pane.HasSyntaxError));
        StringAssert.Contains(viewModel.OutputText, "Program output.");

        viewModel.SourceText = "PRINT SMILE is valid again";
        await WaitUntilAsync(() =>
            viewModel.Pane1.HasValidSource &&
            viewModel.Pane1.GeneratedCode.Contains("SMILE is valid again", StringComparison.Ordinal));

        StringAssert.Contains(viewModel.OutputText, "Program output.");
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
    public async Task Visible_build_runs_duplicate_CSharp_panes_with_their_own_edits_and_INPUT_metadata()
    {
        var csharp = new FakeToolchain(TargetLanguage.CSharp);
        var viewModel = new MainWindowViewModel(
            CreateRegistry(csharp),
            new FakeErrorReporter(),
            new FakeFolderOpener())
        {
            OpenGeneratedFolderAfterBuild = false,
            SourceText = "LET Name = \"\"\nINPUT Name\nPRINT {Name}"
        };
        await viewModel.InitializeAsync();
        await WaitUntilAsync(() => viewModel.Pane1.HasValidSource);
        SelectLanguage(viewModel.Pane2, TargetLanguage.CSharp);
        await WaitUntilAsync(() => viewModel.Pane2.HasValidSource);

        const string pane1Source = "Console.WriteLine(\"pane 1\");";
        const string pane2Source = "Console.WriteLine(\"pane 2\");";
        viewModel.Pane1.GeneratedCode = pane1Source;
        viewModel.Pane2.GeneratedCode = pane2Source;
        viewModel.Pane3.GeneratedCode = string.Empty;

        viewModel.BuildRunVisibleCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsBusy && csharp.BuildRuns == 2);

        Assert.HasCount(2, csharp.GeneratedPrograms);
        Assert.AreEqual(pane1Source, csharp.GeneratedPrograms[0].PrimaryFile.Content);
        Assert.AreEqual(pane2Source, csharp.GeneratedPrograms[1].PrimaryFile.Content);
        Assert.AreNotSame(csharp.GeneratedPrograms[0], csharp.GeneratedPrograms[1]);
        Assert.AreNotSame(csharp.GeneratedPrograms[0].Files, csharp.GeneratedPrograms[1].Files);
        Assert.IsTrue(csharp.GeneratedPrograms.All(program => program.RequiresStandardInput));
        Assert.IsTrue(csharp.GeneratedPrograms.All(program =>
            program.Files.Any(file => file.RelativePath == "GeneratedProgram.csproj")));
        Assert.HasCount(2, csharp.BuildRunOptionsHistory);
        Assert.IsTrue(csharp.BuildRunOptionsHistory.All(options =>
            options.ProgramStandardInput.Mode == ProcessInputMode.InteractiveInherited &&
            options.LaunchVisibleConsole));
        Assert.AreEqual(pane1Source, viewModel.Pane1.GeneratedCode);
        Assert.AreEqual(pane2Source, viewModel.Pane2.GeneratedCode);
        Assert.IsTrue(viewModel.Pane1.HasUserEdits);
        Assert.IsTrue(viewModel.Pane2.HasUserEdits);

        int pane1Header = viewModel.OutputText.IndexOf(
            "=== Generated target 1 - C# ===",
            StringComparison.Ordinal);
        int pane2Header = viewModel.OutputText.IndexOf(
            "=== Generated target 2 - C# ===",
            StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, pane1Header);
        Assert.IsGreaterThan(pane1Header, pane2Header);
    }

    [TestMethod]
    public async Task Duplicate_visible_build_before_debounce_refreshes_an_unedited_sibling_before_compiling()
    {
        var generationGate = new SingleUseLiveGenerationGate();
        var csharp = new FakeToolchain(TargetLanguage.CSharp);
        var viewModel = new MainWindowViewModel(
            CreateRegistry(csharp),
            new FakeErrorReporter(),
            new FakeFolderOpener(),
            languageFilePath: null,
            languageSourceReader: null,
            liveGenerationGate: generationGate.WaitAsync)
        {
            OpenGeneratedFolderAfterBuild = false
        };
        await viewModel.InitializeAsync();
        SelectLanguage(viewModel.Pane2, TargetLanguage.CSharp);
        await WaitUntilAsync(() => viewModel.Pane2.HasValidSource);

        const string smileSource = "PRINT current SMILE source B";
        const string pane1Edit = "Console.WriteLine(\"pane 1 target edit T\");";
        generationGate.Arm();
        viewModel.SourceText = smileSource;
        await generationGate.Entered.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.Pane1.GeneratedCode = pane1Edit;
        viewModel.Pane3.GeneratedCode = string.Empty;

        viewModel.BuildRunVisibleCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsBusy && csharp.BuildRuns == 2);

        Assert.AreEqual(pane1Edit, csharp.GeneratedPrograms[0].PrimaryFile.Content);
        StringAssert.Contains(csharp.GeneratedPrograms[1].PrimaryFile.Content, "current SMILE source B");
        Assert.AreEqual(
            csharp.GeneratedPrograms[1].PrimaryFile.Content,
            viewModel.Pane2.GeneratedCode);
        Assert.IsFalse(viewModel.Pane2.HasUserEdits);
        Assert.AreEqual("Generated target 2 - C#", viewModel.Pane2.Title);
    }

    [TestMethod]
    public async Task Visible_build_runs_three_COBOL_panes_and_preserves_each_INPUT_companion()
    {
        var cobol = new FakeToolchain(TargetLanguage.Cobol);
        var viewModel = new MainWindowViewModel(
            CreateRegistry(cobol),
            new FakeErrorReporter(),
            new FakeFolderOpener())
        {
            OpenGeneratedFolderAfterBuild = false,
            SourceText = "LET Name = \"\"\nINPUT Name\nPRINT {Name}"
        };
        await viewModel.InitializeAsync();
        foreach (TargetPaneViewModel pane in viewModel.Panes)
        {
            SelectLanguage(pane, TargetLanguage.Cobol);
        }

        await WaitUntilAsync(() => viewModel.Panes.All(pane => pane.HasValidSource));
        for (int index = 0; index < viewModel.Panes.Count; index++)
        {
            viewModel.Panes[index].GeneratedCode = $"*> learner COBOL pane {index + 1}";
        }

        viewModel.BuildRunVisibleCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsBusy && cobol.BuildRuns == 3);

        Assert.HasCount(3, cobol.GeneratedPrograms);
        for (int index = 0; index < cobol.GeneratedPrograms.Count; index++)
        {
            GeneratedProgram program = cobol.GeneratedPrograms[index];
            Assert.AreEqual($"*> learner COBOL pane {index + 1}", program.PrimaryFile.Content);
            Assert.IsTrue(program.RequiresStandardInput);
            Assert.IsTrue(program.Files.Any(file => file.RelativePath == "SmileRuntime.c"));
        }
    }

    [TestMethod]
    public async Task Visible_build_runs_mixed_duplicate_languages_in_pane_order()
    {
        var buildOrder = new List<string>();
        var csharp = new FakeToolchain(TargetLanguage.CSharp)
        {
            BuildStarted = () => buildOrder.Add("C#")
        };
        var python = new FakeToolchain(TargetLanguage.Python)
        {
            BuildStarted = () => buildOrder.Add("Python")
        };
        var viewModel = new MainWindowViewModel(
            CreateRegistry(csharp, python),
            new FakeErrorReporter(),
            new FakeFolderOpener())
        {
            OpenGeneratedFolderAfterBuild = false
        };
        await viewModel.InitializeAsync();
        SelectLanguage(viewModel.Pane2, TargetLanguage.CSharp);
        SelectLanguage(viewModel.Pane3, TargetLanguage.Python);
        await WaitUntilAsync(() => viewModel.Panes.All(pane => pane.HasValidSource));

        viewModel.Pane1.GeneratedCode = "// C# pane 1";
        viewModel.Pane2.GeneratedCode = "// C# pane 2";
        viewModel.Pane3.GeneratedCode = "# Python pane 3";

        viewModel.BuildRunVisibleCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsBusy && csharp.BuildRuns == 2 && python.BuildRuns == 1);

        Assert.AreEqual(2, csharp.BuildRuns);
        Assert.AreEqual(1, python.BuildRuns);
        CollectionAssert.AreEqual(new[] { "C#", "C#", "Python" }, buildOrder);
        Assert.AreEqual("// C# pane 1", csharp.GeneratedPrograms[0].PrimaryFile.Content);
        Assert.AreEqual("// C# pane 2", csharp.GeneratedPrograms[1].PrimaryFile.Content);
        Assert.AreEqual("# Python pane 3", python.GeneratedPrograms[0].PrimaryFile.Content);
    }

    [TestMethod]
    public async Task An_unbuildable_duplicate_pane_does_not_suppress_its_valid_sibling()
    {
        var csharp = new FakeToolchain(TargetLanguage.CSharp);
        var viewModel = new MainWindowViewModel(
            CreateRegistry(csharp),
            new FakeErrorReporter(),
            new FakeFolderOpener())
        {
            OpenGeneratedFolderAfterBuild = false
        };
        await viewModel.InitializeAsync();
        SelectLanguage(viewModel.Pane2, TargetLanguage.CSharp);
        await WaitUntilAsync(() => viewModel.Pane2.HasValidSource);

        viewModel.Pane1.GeneratedCode = "// buildable C# pane";
        viewModel.Pane2.GeneratedCode = string.Empty;
        viewModel.Pane3.GeneratedCode = string.Empty;

        viewModel.BuildRunVisibleCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsBusy && csharp.BuildRuns == 1);

        Assert.AreEqual(1, csharp.BuildRuns);
        Assert.AreEqual("// buildable C# pane", csharp.GeneratedPrograms[0].PrimaryFile.Content);
        Assert.AreEqual(string.Empty, viewModel.Pane2.GeneratedCode);
        Assert.IsTrue(viewModel.Pane2.HasUserEdits);
    }

    [TestMethod]
    public async Task Cancelling_a_visible_pane_build_stops_before_later_duplicate_panes()
    {
        var csharp = new FakeToolchain(TargetLanguage.CSharp);
        var viewModel = new MainWindowViewModel(
            CreateRegistry(csharp),
            new FakeErrorReporter(),
            new FakeFolderOpener())
        {
            OpenGeneratedFolderAfterBuild = false
        };
        await viewModel.InitializeAsync();
        SelectLanguage(viewModel.Pane2, TargetLanguage.CSharp);
        SelectLanguage(viewModel.Pane3, TargetLanguage.CSharp);
        await WaitUntilAsync(() => viewModel.Panes.All(pane => pane.HasValidSource));

        string[] editorSources = { "// pane 1", "// pane 2", "// pane 3" };
        for (int index = 0; index < viewModel.Panes.Count; index++)
        {
            viewModel.Panes[index].GeneratedCode = editorSources[index];
        }

        csharp.BuildStarted = () =>
        {
            csharp.BuildStarted = null;
            viewModel.CancelCommand.Execute(null);
        };
        viewModel.BuildRunVisibleCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsBusy && viewModel.OperationStatus == "Cancelled");

        Assert.AreEqual(1, csharp.BuildRuns);
        CollectionAssert.AreEqual(editorSources, viewModel.Panes.Select(pane => pane.GeneratedCode).ToArray());
        Assert.IsTrue(viewModel.Panes.All(pane => pane.HasUserEdits));
    }

    [TestMethod]
    public async Task INPUT_build_runs_exactly_once_in_one_visible_interactive_console()
    {
        var csharp = new FakeToolchain(
            TargetLanguage.CSharp,
            workingDirectory: Environment.CurrentDirectory);
        var viewModel = new MainWindowViewModel(
            CreateRegistry(csharp),
            new FakeErrorReporter(),
            new FakeFolderOpener())
        {
            OpenGeneratedFolderAfterBuild = false,
            SourceText = "LET Name = \"\"\nPRINT Name?\nINPUT Name\nPRINT {Name}"
        };
        await viewModel.InitializeAsync();
        await WaitUntilAsync(() =>
            viewModel.Pane1.HasValidSource &&
            viewModel.Pane1.GeneratedCode.Contains("SMILER1501", StringComparison.Ordinal));

        viewModel.Pane1.BuildRunCommand!.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsBusy && viewModel.OperationStatus == "Completed");

        Assert.AreEqual(1, csharp.BuildRuns);
        Assert.IsNotNull(csharp.LastGeneratedProgram);
        Assert.IsTrue(csharp.LastGeneratedProgram.RequiresStandardInput);
        Assert.IsNotNull(csharp.LastBuildRunOptions);
        Assert.AreEqual(
            ProcessInputMode.InteractiveInherited,
            csharp.LastBuildRunOptions.ProgramStandardInput.Mode);
        Assert.IsTrue(csharp.LastBuildRunOptions.LaunchVisibleConsole);
        Assert.IsTrue(csharp.LastBuildRunOptions.CreatePauseLauncher);
        StringAssert.Contains(viewModel.OutputText, "interactive console launched");
    }

    [TestMethod]
    public async Task INPUT_launch_failure_does_not_claim_that_a_console_launched()
    {
        var csharp = new FakeToolchain(
            TargetLanguage.CSharp,
            workingDirectory: Environment.CurrentDirectory,
            simulateLaunchFailure: true);
        var viewModel = new MainWindowViewModel(
            CreateRegistry(csharp),
            new FakeErrorReporter(),
            new FakeFolderOpener())
        {
            OpenGeneratedFolderAfterBuild = false,
            SourceText = "LET Name = \"\"\nINPUT Name"
        };
        await viewModel.InitializeAsync();
        await WaitUntilAsync(() => viewModel.Pane1.HasValidSource);

        viewModel.Pane1.BuildRunCommand!.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsBusy && viewModel.OperationStatus == "Failed");

        StringAssert.Contains(viewModel.OutputText, "Process launch failed");
        Assert.IsFalse(
            viewModel.OutputText.Contains("interactive console launched", StringComparison.Ordinal));
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

    private static void SelectLanguage(TargetPaneViewModel pane, TargetLanguage language) =>
        pane.SelectedLanguageOption = pane.LanguageOptions.Single(option => option.Language == language);

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
        private readonly bool _simulateLaunchFailure;
        private readonly Task? _detectGate;
        private readonly List<GeneratedProgram> _generatedPrograms = new();
        private readonly List<BuildRunOptions> _buildRunOptions = new();

        public FakeToolchain(
            TargetLanguage language,
            Exception? detectException = null,
            Exception? buildRunException = null,
            string? workingDirectory = null,
            bool simulateLaunchFailure = false,
            Task? detectGate = null)
        {
            Language = language;
            _detectException = detectException;
            _buildRunException = buildRunException;
            _workingDirectory = workingDirectory;
            _simulateLaunchFailure = simulateLaunchFailure;
            _detectGate = detectGate;
        }

        public TargetLanguage Language { get; }

        public int BuildRuns { get; private set; }

        public IReadOnlyList<GeneratedProgram> GeneratedPrograms => _generatedPrograms;

        public IReadOnlyList<BuildRunOptions> BuildRunOptionsHistory => _buildRunOptions;

        public GeneratedProgram? LastGeneratedProgram => _generatedPrograms.LastOrDefault();

        public BuildRunOptions? LastBuildRunOptions => _buildRunOptions.LastOrDefault();

        public Action? BuildStarted { get; set; }

        public async Task<ToolchainStatus> DetectAsync(CancellationToken cancellationToken)
        {
            if (_detectException is not null)
            {
                throw _detectException;
            }

            if (_detectGate is not null)
            {
                await _detectGate.WaitAsync(cancellationToken);
            }

            string name = TargetLanguageInfo.GetDisplayName(Language);
            return new ToolchainStatus(Language, true, name, "test", "test", $"{name} detected.");
        }

        public async Task<BuildRunResult> BuildAndRunAsync(
            GeneratedProgram generatedProgram,
            CancellationToken cancellationToken,
            BuildRunOptions? options = null)
        {
            BuildRuns++;
            _generatedPrograms.Add(generatedProgram);
            _buildRunOptions.Add(options ?? new BuildRunOptions());
            BuildStarted?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            if (_buildRunException is not null)
            {
                throw _buildRunException;
            }

            ToolchainStatus status = await DetectAsync(cancellationToken);
            if (_simulateLaunchFailure)
            {
                return new BuildRunResult(
                    Language,
                    Success: false,
                    status,
                    "Build completed.",
                    string.Empty,
                    "Process launch failed: Win32Exception: injected failure",
                    null,
                    TimeSpan.FromMilliseconds(1),
                    TimedOut: false,
                    Cancelled: false,
                    _workingDirectory,
                    null,
                    "Running");
            }

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

    private sealed class SingleUseLiveGenerationGate
    {
        private TaskCompletionSource<bool> _entered = CompletedSource();
        private TaskCompletionSource<bool> _release = CompletedSource();
        private int _armed;

        public Task Entered => _entered.Task;

        public void Arm()
        {
            _entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref _armed, 1);
        }

        public async Task WaitAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _armed, 0) == 0)
            {
                return;
            }

            _entered.TrySetResult(true);
            await _release.Task.WaitAsync(cancellationToken);
        }

        public void Release() => _release.TrySetResult(true);

        private static TaskCompletionSource<bool> CompletedSource()
        {
            var source = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            source.SetResult(true);
            return source;
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
