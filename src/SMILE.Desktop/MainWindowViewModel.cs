using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using SMILE.Engine;
using SMILE.Toolchains;

namespace SMILE.Desktop;

public sealed class MainWindowViewModel : ViewModelBase
{
    private const string SampleSource = """
LET Name = "Sin"

PRINT
PRINT "Hello World!"
PRINT Hello World!
PRINT Hello {Name}!
PRINT $"Hello {Name}!"
PRINT "Hello " + Name + "!"
""";

    // Live transpilation is intentionally delayed a little. A compiler front
    // end usually works on complete snapshots of source text, so this debounce
    // lets the user finish a burst of typing before the lexer/parser/generator
    // pipeline runs in the background.
    private static readonly TimeSpan LiveTranspileDelay = TimeSpan.FromMilliseconds(250);

    private readonly SmileTranspiler _transpiler = new();
    private readonly ToolchainRegistry _toolchains = ToolchainRegistry.CreateDefault();
    private readonly Dictionary<TargetLanguage, GeneratedSnapshot> _generatedPrograms = new();
    private readonly Dictionary<TargetLanguage, ToolchainStatus> _toolchainStatuses = new();
    private CancellationTokenSource? _operationCancellation;
    private CancellationTokenSource? _liveTranspileCancellation;
    private Task? _liveTranspileTask;
    private string _sourceText = SampleSource;
    private string _outputText = string.Empty;
    private string _operationStatus = "Ready";
    private string? _currentFilePath;
    private long _sourceRevision;
    private bool _isBusy;
    private bool _openGeneratedFolderAfterBuild = true;
    private bool _createPauseLauncherAfterBuild = true;
    private bool _outputShowsLiveDiagnostics;

    public MainWindowViewModel()
    {
        Pane1 = CreatePane("Generated target 1", TargetLanguage.CSharp);
        Pane2 = CreatePane("Generated target 2", TargetLanguage.MasmX64);
        Pane3 = CreatePane("Generated target 3", TargetLanguage.C);

        NewCommand = new RelayCommand(NewDocument, CanStartWork, HandleCommandException);
        OpenCommand = new AsyncRelayCommand(OpenAsync, CanStartWork, HandleCommandException);
        SaveCommand = new AsyncRelayCommand(SaveAsync, CanStartWork, HandleCommandException);
        SaveAsCommand = new AsyncRelayCommand(SaveAsAsync, CanStartWork, HandleCommandException);
        TranspileAllCommand = new AsyncRelayCommand(TranspileAllAsync, CanStartWork, HandleCommandException);
        BuildRunVisibleCommand = new AsyncRelayCommand(BuildRunVisibleAsync, CanBuildVisible, HandleCommandException);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy, HandleCommandException);
        ExitCommand = new RelayCommand(Exit, CanStartWork, HandleCommandException);
        AboutCommand = new RelayCommand(ShowAbout, onError: HandleCommandException);
    }

    private sealed record GeneratedSnapshot(long SourceRevision, GeneratedProgram Program);

    public TargetPaneViewModel Pane1 { get; }

    public TargetPaneViewModel Pane2 { get; }

    public TargetPaneViewModel Pane3 { get; }

    public IReadOnlyList<TargetPaneViewModel> Panes => new[] { Pane1, Pane2, Pane3 };

    public RelayCommand NewCommand { get; }

    public AsyncRelayCommand OpenCommand { get; }

    public AsyncRelayCommand SaveCommand { get; }

    public AsyncRelayCommand SaveAsCommand { get; }

    public AsyncRelayCommand TranspileAllCommand { get; }

    public AsyncRelayCommand BuildRunVisibleCommand { get; }

    public RelayCommand CancelCommand { get; }

    public RelayCommand ExitCommand { get; }

    public RelayCommand AboutCommand { get; }

    public string SourceText
    {
        get => _sourceText;
        set
        {
            if (SetProperty(ref _sourceText, value) && Pane1 is not null)
            {
                _sourceRevision++;
                ScheduleLiveTranspilation();
            }
        }
    }

    public string OutputText
    {
        get => _outputText;
        set => SetProperty(ref _outputText, value);
    }

    public string OperationStatus
    {
        get => _operationStatus;
        set => SetProperty(ref _operationStatus, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                foreach (TargetPaneViewModel pane in Panes)
                {
                    pane.IsBusy = value;
                }

                RaiseCommandStateChanged();
            }
        }
    }

    public bool OpenGeneratedFolderAfterBuild
    {
        get => _openGeneratedFolderAfterBuild;
        set => SetProperty(ref _openGeneratedFolderAfterBuild, value);
    }

    public bool CreatePauseLauncherAfterBuild
    {
        get => _createPauseLauncherAfterBuild;
        set => SetProperty(ref _createPauseLauncherAfterBuild, value);
    }

    public async Task InitializeAsync()
    {
        try
        {
            await TranspileAllCurrentSourceAsync(isManual: false, CancellationToken.None).ConfigureAwait(true);
            await DetectToolchainsAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            HandleUiError("Initialize SMILE", ex);
        }
    }

    public void HandleInitializationException(Exception ex) =>
        HandleUiError("Initialize SMILE", ex);

    private TargetPaneViewModel CreatePane(string title, TargetLanguage defaultLanguage)
    {
        var pane = new TargetPaneViewModel(title, defaultLanguage);
        pane.SelectedLanguageChanged += (_, _) =>
        {
            UpdatePaneForLanguage(pane);
            ScheduleLiveTranspilation();
            RaiseCommandStateChanged();
        };
        pane.CopyCommand = new RelayCommand(
            () => Clipboard.SetText(pane.GeneratedCode),
            () => pane.CanUseSource,
            HandleCommandException);
        pane.SaveSourceCommand = new AsyncRelayCommand(
            () => SaveGeneratedSourceAsync(pane),
            () => pane.CanUseSource,
            HandleCommandException);
        pane.BuildRunCommand = new AsyncRelayCommand(
            () => BuildRunPaneAsync(pane),
            () => pane.CanBuild,
            HandleCommandException);
        return pane;
    }

    private async Task DetectToolchainsAsync()
    {
        OperationStatus = "Detecting toolchains...";

        foreach (TargetLanguage language in TargetLanguageInfo.All)
        {
            ToolchainStatus status = await _toolchains.Get(language)
                .DetectAsync(CancellationToken.None)
                .ConfigureAwait(true);

            _toolchainStatuses[language] = status;
        }

        foreach (TargetPaneViewModel pane in Panes)
        {
            UpdateToolchainStatus(pane);
            UpdatePaneForLanguage(pane);
        }

        OperationStatus = "Ready";
        RaiseCommandStateChanged();
    }

    private void NewDocument()
    {
        SourceText = SampleSource;
        _currentFilePath = null;
        OperationStatus = "New file";
    }

    private async Task OpenAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "SMILE source (*.smile)|*.smile|All files (*.*)|*.*",
            Title = "Open SMILE source"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        SourceText = await File.ReadAllTextAsync(dialog.FileName).ConfigureAwait(true);
        _currentFilePath = dialog.FileName;
        OperationStatus = $"Opened {Path.GetFileName(_currentFilePath)}";
    }

    private async Task SaveAsync()
    {
        if (_currentFilePath is null)
        {
            await SaveAsAsync().ConfigureAwait(true);
            return;
        }

        await File.WriteAllTextAsync(_currentFilePath, SourceText).ConfigureAwait(true);
        OperationStatus = $"Saved {Path.GetFileName(_currentFilePath)}";
    }

    private async Task SaveAsAsync()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "SMILE source (*.smile)|*.smile|All files (*.*)|*.*",
            FileName = "PrintEverywhere.smile",
            Title = "Save SMILE source"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _currentFilePath = dialog.FileName;
        await SaveAsync().ConfigureAwait(true);
    }

    private async Task TranspileAllAsync()
    {
        await RunOperationAsync(
            "Transpile all targets",
            cancellationToken => TranspileAllCurrentSourceAsync(isManual: true, cancellationToken))
            .ConfigureAwait(true);
    }

    private async Task TranspileAllCurrentSourceAsync(bool isManual, CancellationToken cancellationToken)
    {
        CancelLiveTranspilation();

        string sourceSnapshot = SourceText;
        long revision = _sourceRevision;
        IReadOnlyList<TranspileResult> results = await GenerateAsync(
            sourceSnapshot,
            TargetLanguageInfo.All,
            cancellationToken).ConfigureAwait(true);

        if (revision == _sourceRevision)
        {
            ApplyTranspileResults(results, revision, isLive: false, reportSuccess: isManual);
        }
    }

    private void ScheduleLiveTranspilation()
    {
        if (IsBusy)
        {
            return;
        }

        CancelLiveTranspilation();

        TargetLanguage[] languages = GetVisibleLanguages();
        if (languages.Length == 0)
        {
            return;
        }

        foreach (TargetPaneViewModel pane in Panes)
        {
            pane.HasValidSource = false;
            pane.HasSyntaxError = false;
            pane.Status = "Updating";
        }

        OperationStatus = "Updating";
        RaiseCommandStateChanged();

        var cancellation = new CancellationTokenSource();
        _liveTranspileCancellation = cancellation;
        string sourceSnapshot = SourceText;
        long revision = _sourceRevision;

        _liveTranspileTask = RunLiveTranspilationAsync(
            sourceSnapshot,
            revision,
            languages,
            cancellation,
            LiveTranspileDelay);
    }

    private async Task RunLiveTranspilationAsync(
        string sourceSnapshot,
        long revision,
        IReadOnlyList<TargetLanguage> languages,
        CancellationTokenSource cancellation,
        TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay, cancellation.Token).ConfigureAwait(true);

            IReadOnlyList<TranspileResult> results = await GenerateAsync(
                sourceSnapshot,
                languages,
                cancellation.Token).ConfigureAwait(true);

            if (cancellation.IsCancellationRequested || revision != _sourceRevision)
            {
                return;
            }

            ApplyTranspileResults(results, revision, isLive: true, reportSuccess: false);
            OperationStatus = "Ready";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // A newer source snapshot superseded this one. That is expected:
            // the UI only wants compiler output for the most recent text.
        }
        catch (Exception ex)
        {
            HandleUiError("Live transpilation", ex);
        }
        finally
        {
            if (ReferenceEquals(_liveTranspileCancellation, cancellation))
            {
                _liveTranspileCancellation = null;
                _liveTranspileTask = null;
            }

            cancellation.Dispose();
        }
    }

    private async Task<IReadOnlyList<TranspileResult>> GenerateAsync(
        string sourceSnapshot,
        IReadOnlyList<TargetLanguage> languages,
        CancellationToken cancellationToken)
    {
        // The lexer/parser/generator pipeline is fast today, but keeping it
        // off the WPF dispatcher protects the editor as SMILE grows.
        return await Task.Run(
            () => _transpiler.TranspileMany(sourceSnapshot, languages),
            cancellationToken).ConfigureAwait(true);
    }

    private void ApplyTranspileResults(
        IReadOnlyList<TranspileResult> results,
        long revision,
        bool isLive,
        bool reportSuccess)
    {
        IReadOnlyList<Diagnostic> diagnostics = results
            .SelectMany(result => result.Diagnostics)
            .Distinct()
            .ToArray();

        bool success = results.All(result => result.Success);
        if (success)
        {
            foreach (TranspileResult result in results)
            {
                _generatedPrograms[result.Language] = new GeneratedSnapshot(
                    revision,
                    result.GeneratedProgram!);
            }

            foreach (TargetPaneViewModel pane in Panes)
            {
                UpdatePaneForLanguage(pane);
            }

            if (reportSuccess)
            {
                OutputText = "Transpilation completed.";
                _outputShowsLiveDiagnostics = false;
                OperationStatus = "Completed";
            }
            else if (isLive && _outputShowsLiveDiagnostics)
            {
                OutputText = string.Empty;
                _outputShowsLiveDiagnostics = false;
            }

            return;
        }

        string diagnosticText = string.Join(
            Environment.NewLine,
            diagnostics.Select(diagnostic => diagnostic.ToString()));

        foreach (TargetPaneViewModel pane in Panes)
        {
            pane.GeneratedCode = string.Empty;
            pane.HasValidSource = false;
            pane.HasSyntaxError = true;
            pane.Status = "Syntax Error";
            pane.RaiseCommandStateChanged();
        }

        OutputText = diagnosticText;
        _outputShowsLiveDiagnostics = isLive;
        OperationStatus = "Syntax Error";
        RaiseCommandStateChanged();
    }

    private async Task BuildRunVisibleAsync()
    {
        var results = new List<BuildRunResult>();

        await RunOperationAsync(
            "Build & Run visible languages",
            async cancellationToken =>
            {
                foreach (TargetPaneViewModel pane in Panes
                    .GroupBy(pane => pane.Language)
                    .Select(group => group.First()))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    BuildRunResult? result = await BuildRunPaneCoreAsync(pane, cancellationToken).ConfigureAwait(true);
                    if (result is not null)
                    {
                        results.Add(result);
                    }
                }
            }).ConfigureAwait(true);

        OpenGeneratedFolderForResults(results);
    }

    private async Task BuildRunPaneAsync(TargetPaneViewModel pane)
    {
        BuildRunResult? result = null;

        await RunOperationAsync(
            $"{TargetLanguageInfo.GetDisplayName(pane.Language)} {pane.BuildButtonText}",
            async cancellationToken =>
            {
                result = await BuildRunPaneCoreAsync(pane, cancellationToken).ConfigureAwait(true);
            }).ConfigureAwait(true);

        OpenGeneratedFolderForResults(result is null ? Array.Empty<BuildRunResult>() : new[] { result });
    }

    private async Task<BuildRunResult?> BuildRunPaneCoreAsync(
        TargetPaneViewModel pane,
        CancellationToken cancellationToken)
    {
        TargetLanguage language = pane.Language;
        string languageName = TargetLanguageInfo.GetDisplayName(language);

        if (IsTranspileOnlyLanguage(language))
        {
            pane.Status = "Transpile Only";
            await EnsureCurrentGeneratedProgramAsync(language, cancellationToken).ConfigureAwait(true);
            AppendOutput($"=== {languageName} ==={Environment.NewLine}Skipped: this target is transpile-only on Windows for now.");
            return null;
        }

        if (!_toolchainStatuses.TryGetValue(language, out ToolchainStatus? status) || !status.IsAvailable)
        {
            pane.Status = "Toolchain Missing";
            AppendOutput($"=== {languageName} ==={Environment.NewLine}{status?.Message ?? "Toolchain not detected."}");
            return null;
        }

        GeneratedProgram? generatedProgram = await EnsureCurrentGeneratedProgramAsync(language, cancellationToken)
            .ConfigureAwait(true);
        if (generatedProgram is null)
        {
            pane.Status = "Syntax Error";
            return null;
        }

        pane.Status = GetInitialBuildStatus(language);
        AppendOutput($"=== {languageName} ===");

        var options = new BuildRunOptions(CreatePauseLauncher: CreatePauseLauncherAfterBuild);
        BuildRunResult result = await _toolchains.Get(language)
            .BuildAndRunAsync(generatedProgram, cancellationToken, options)
            .ConfigureAwait(true);

        pane.Status = BuildRunStatusText(result);
        AppendBuildRunResult(result);
        return result;
    }

    private async Task<GeneratedProgram?> EnsureCurrentGeneratedProgramAsync(
        TargetLanguage language,
        CancellationToken cancellationToken)
    {
        if (_generatedPrograms.TryGetValue(language, out GeneratedSnapshot? snapshot) &&
            snapshot.SourceRevision == _sourceRevision)
        {
            return snapshot.Program;
        }

        string sourceSnapshot = SourceText;
        long revision = _sourceRevision;
        IReadOnlyList<TranspileResult> results = await GenerateAsync(
            sourceSnapshot,
            new[] { language },
            cancellationToken).ConfigureAwait(true);

        if (revision != _sourceRevision)
        {
            return null;
        }

        ApplyTranspileResults(results, revision, isLive: false, reportSuccess: false);
        return _generatedPrograms.TryGetValue(language, out GeneratedSnapshot? currentSnapshot) &&
            currentSnapshot.SourceRevision == revision
            ? currentSnapshot.Program
            : null;
    }

    private async Task RunOperationAsync(string title, Func<CancellationToken, Task> operation)
    {
        if (IsBusy)
        {
            return;
        }

        CancelLiveTranspilation();
        _operationCancellation = new CancellationTokenSource();
        IsBusy = true;
        OperationStatus = title;

        try
        {
            await operation(_operationCancellation.Token).ConfigureAwait(true);
            OperationStatus = _operationCancellation.IsCancellationRequested ? "Cancelled" : "Completed";
        }
        catch (OperationCanceledException)
        {
            OperationStatus = "Cancelled";

            foreach (TargetPaneViewModel pane in Panes.Where(pane => pane.Status is "Building" or "Assembling" or "Linking" or "Running"))
            {
                pane.Status = "Cancelled";
            }
        }
        catch (Exception ex)
        {
            OperationStatus = "Failed";

            foreach (TargetPaneViewModel pane in Panes.Where(pane => pane.Status is "Building" or "Assembling" or "Linking" or "Running"))
            {
                pane.Status = "Failed";
            }

            HandleUiError(title, ex);
        }
        finally
        {
            _operationCancellation.Dispose();
            _operationCancellation = null;
            IsBusy = false;
        }
    }

    private void Cancel()
    {
        _operationCancellation?.Cancel();
        AppendOutput("Cancellation requested.");
        OperationStatus = "Cancelling...";
    }

    private async Task SaveGeneratedSourceAsync(TargetPaneViewModel pane)
    {
        GeneratedProgram? generatedProgram = await EnsureCurrentGeneratedProgramAsync(
            pane.Language,
            CancellationToken.None).ConfigureAwait(true);
        if (generatedProgram is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            FileName = generatedProgram.PrimaryFile.RelativePath,
            Title = $"Save {TargetLanguageInfo.GetDisplayName(pane.Language)} source",
            Filter = "All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await File.WriteAllTextAsync(dialog.FileName, generatedProgram.PrimaryFile.Content).ConfigureAwait(true);
        OperationStatus = $"Saved {Path.GetFileName(dialog.FileName)}";
    }

    private void UpdatePaneForLanguage(TargetPaneViewModel pane)
    {
        UpdateToolchainStatus(pane);

        if (_generatedPrograms.TryGetValue(pane.Language, out GeneratedSnapshot? snapshot) &&
            snapshot.SourceRevision == _sourceRevision)
        {
            pane.GeneratedCode = snapshot.Program.PrimaryFile.Content;
            pane.HasValidSource = true;
            pane.HasSyntaxError = false;
            pane.Status = GetReadyStatus(pane);
        }
        else if (!pane.HasSyntaxError)
        {
            pane.HasValidSource = false;
            pane.Status = "Updating";
        }

        pane.RaiseCommandStateChanged();
    }

    private void UpdateToolchainStatus(TargetPaneViewModel pane)
    {
        if (_toolchainStatuses.TryGetValue(pane.Language, out ToolchainStatus? status))
        {
            pane.HasToolchain = status.IsAvailable;
            pane.ToolchainStatusText = status.Message;
        }
        else
        {
            pane.HasToolchain = false;
            pane.ToolchainStatusText = "Toolchain not detected.";
        }
    }

    private void OpenGeneratedFolderForResults(IReadOnlyList<BuildRunResult> results)
    {
        if (!OpenGeneratedFolderAfterBuild)
        {
            return;
        }

        string[] folders = results
            .Select(result => result.WorkingDirectory)
            .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            .Select(path => Path.GetFullPath(path!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (folders.Length == 0)
        {
            return;
        }

        string folderToOpen = GetFolderToOpen(folders);
        _ = OpenGeneratedFolderAsync(folderToOpen);
    }

    private async Task OpenGeneratedFolderAsync(string folderToOpen)
    {
        AppendOutput($"Generated code folder: {folderToOpen}");

        try
        {
            await Task.Run(() => FolderOpener.OpenOrActivate(folderToOpen)).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            HandleUiError("Open generated folder", ex);
        }
    }

    private static string GetFolderToOpen(IReadOnlyList<string> folders)
    {
        if (folders.Count == 1)
        {
            return folders[0];
        }

        // A visible-languages build creates one temp workspace per language.
        // Opening their shared parent gives the learner one Explorer window
        // where all generated-code folders from the operation can be inspected.
        string? parent = Path.GetDirectoryName(folders[0]);
        if (!string.IsNullOrWhiteSpace(parent) &&
            folders.All(folder => string.Equals(
                Path.GetDirectoryName(folder),
                parent,
                StringComparison.OrdinalIgnoreCase)))
        {
            return parent;
        }

        return folders[^1];
    }

    private bool CanStartWork() => !IsBusy;

    private bool CanBuildVisible() =>
        !IsBusy &&
        Panes.Any(pane => !pane.HasSyntaxError && (pane.HasToolchain || IsTranspileOnlyLanguage(pane.Language)));

    private void RaiseCommandStateChanged()
    {
        NewCommand.RaiseCanExecuteChanged();
        OpenCommand.RaiseCanExecuteChanged();
        SaveCommand.RaiseCanExecuteChanged();
        SaveAsCommand.RaiseCanExecuteChanged();
        TranspileAllCommand.RaiseCanExecuteChanged();
        BuildRunVisibleCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
        ExitCommand.RaiseCanExecuteChanged();

        foreach (TargetPaneViewModel pane in Panes)
        {
            pane.RaiseCommandStateChanged();
        }
    }

    private void AppendBuildRunResult(BuildRunResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine(result.ToolchainStatus.Message);

        if (!string.IsNullOrWhiteSpace(result.BuildOutput))
        {
            builder.AppendLine("Build output:");
            builder.AppendLine(result.BuildOutput.TrimEnd());
        }

        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            builder.AppendLine("Program output:");
            builder.Append(result.StandardOutput);
            if (!result.StandardOutput.EndsWith(Environment.NewLine, StringComparison.Ordinal))
            {
                builder.AppendLine();
            }
        }

        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            builder.AppendLine("Program error:");
            builder.AppendLine(result.StandardError.TrimEnd());
        }

        builder.AppendLine($"Exit code: {(result.ExitCode.HasValue ? result.ExitCode.Value.ToString() : "n/a")}");
        builder.AppendLine($"Total duration: {result.Duration.TotalMilliseconds:0} ms");

        if (result.WorkingDirectory is not null)
        {
            builder.AppendLine($"Workspace: {result.WorkingDirectory}");
        }

        if (result.PauseLauncherPath is not null)
        {
            builder.AppendLine($"Press-any-key launcher: {result.PauseLauncherPath}");
        }

        if (result.TimedOut)
        {
            builder.AppendLine("Timed out.");
        }

        if (result.Cancelled)
        {
            builder.AppendLine("Cancelled.");
        }

        AppendOutput(builder.ToString().TrimEnd());
    }

    private void AppendOutput(string text)
    {
        if (string.IsNullOrWhiteSpace(OutputText) || OutputText == "Transpilation completed.")
        {
            OutputText = text;
            return;
        }

        OutputText += Environment.NewLine + Environment.NewLine + text;
    }

    private void HandleCommandException(Exception exception) =>
        HandleUiError("Command", exception);

    private void HandleUiError(string operation, Exception exception)
    {
        OperationStatus = "Failed";
        AppendOutput($"{operation} failed: {exception.Message}");
    }

    private void CancelLiveTranspilation()
    {
        _liveTranspileCancellation?.Cancel();
        _liveTranspileCancellation = null;
        _liveTranspileTask = null;
    }

    private TargetLanguage[] GetVisibleLanguages() =>
        Panes
            .Select(pane => pane.Language)
            .Distinct()
            .ToArray();

    private static string GetReadyStatus(TargetPaneViewModel pane) =>
        IsTranspileOnlyLanguage(pane.Language)
            ? "Transpile Only"
            : pane.HasToolchain ? "Ready" : "Toolchain Missing";

    private static string GetInitialBuildStatus(TargetLanguage language) =>
        language switch
        {
            TargetLanguage.MasmX64 => "Assembling",
            TargetLanguage.JavaScript => "Running",
            _ => "Building"
        };

    public static string BuildRunStatusText(BuildRunResult result)
    {
        if (result.Cancelled)
        {
            return "Cancelled";
        }

        if (result.TimedOut)
        {
            return "Timed Out";
        }

        if (result.Stage.Equals("Transpile Only", StringComparison.OrdinalIgnoreCase))
        {
            return "Transpile Only";
        }

        if (result.Stage.Equals("Toolchain Missing", StringComparison.OrdinalIgnoreCase))
        {
            return "Toolchain Missing";
        }

        return result.Success ? "Completed" : "Failed";
    }

    private static bool IsTranspileOnlyLanguage(TargetLanguage language) =>
        language is TargetLanguage.ObjectiveC or TargetLanguage.Swift;

    private static void Exit() =>
        Application.Current.Shutdown();

    private static void ShowAbout()
    {
        Assembly assembly = typeof(MainWindowViewModel).Assembly;
        string version =
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
            assembly.GetName().Version?.ToString() ??
            "unknown";

        MessageBox.Show(
            $"SMILE{Environment.NewLine}Version {version}{Environment.NewLine}{Environment.NewLine}Educational BASIC-inspired multi-target transpiler.",
            "About SMILE",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}
