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
PRINT "Hello from SMILE!"
PRINT "Different syntax, same idea."
""";

    private readonly SmileTranspiler _transpiler = new();
    private readonly ToolchainRegistry _toolchains = ToolchainRegistry.CreateDefault();
    private readonly Dictionary<TargetLanguage, GeneratedProgram> _generatedPrograms = new();
    private readonly Dictionary<TargetLanguage, ToolchainStatus> _toolchainStatuses = new();
    private CancellationTokenSource? _operationCancellation;
    private string _sourceText = SampleSource;
    private string _outputText = string.Empty;
    private string _operationStatus = "Ready";
    private string? _currentFilePath;
    private bool _isBusy;
    private bool _openGeneratedFolderAfterBuild = true;
    private bool _createPauseLauncherAfterBuild = true;

    public MainWindowViewModel()
    {
        Pane1 = CreatePane("Generated target 1", TargetLanguage.CSharp);
        Pane2 = CreatePane("Generated target 2", TargetLanguage.MasmX64);
        Pane3 = CreatePane("Generated target 3", TargetLanguage.C);

        NewCommand = new RelayCommand(NewDocument, CanStartWork);
        OpenCommand = new AsyncRelayCommand(OpenAsync, CanStartWork);
        SaveCommand = new AsyncRelayCommand(SaveAsync, CanStartWork);
        SaveAsCommand = new AsyncRelayCommand(SaveAsAsync, CanStartWork);
        TranspileAllCommand = new RelayCommand(TranspileAll, CanStartWork);
        BuildRunVisibleCommand = new AsyncRelayCommand(BuildRunVisibleAsync, CanBuildVisible);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
        ExitCommand = new RelayCommand(Exit, CanStartWork);
        AboutCommand = new RelayCommand(ShowAbout);
    }

    public TargetPaneViewModel Pane1 { get; }

    public TargetPaneViewModel Pane2 { get; }

    public TargetPaneViewModel Pane3 { get; }

    public IReadOnlyList<TargetPaneViewModel> Panes => new[] { Pane1, Pane2, Pane3 };

    public RelayCommand NewCommand { get; }

    public AsyncRelayCommand OpenCommand { get; }

    public AsyncRelayCommand SaveCommand { get; }

    public AsyncRelayCommand SaveAsCommand { get; }

    public RelayCommand TranspileAllCommand { get; }

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
                // v0.1 parsing is intentionally tiny, so live transpilation
                // keeps the panes honest without creating noticeable UI work.
                TranspileAll();
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
        TranspileAll();
        await DetectToolchainsAsync().ConfigureAwait(true);
    }

    private TargetPaneViewModel CreatePane(string title, TargetLanguage defaultLanguage)
    {
        var pane = new TargetPaneViewModel(title, defaultLanguage);
        pane.SelectedLanguageChanged += (_, _) =>
        {
            UpdatePaneForLanguage(pane);
            RaiseCommandStateChanged();
        };
        pane.CopyCommand = new RelayCommand(
            () => Clipboard.SetText(pane.GeneratedCode),
            () => pane.CanUseSource);
        pane.SaveSourceCommand = new AsyncRelayCommand(
            () => SaveGeneratedSourceAsync(pane),
            () => pane.CanUseSource);
        pane.BuildRunCommand = new AsyncRelayCommand(
            () => BuildRunPaneAsync(pane),
            () => pane.CanBuild);
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
        }

        OperationStatus = "Ready";
        RaiseCommandStateChanged();
    }

    private void NewDocument()
    {
        SourceText = SampleSource;
        _currentFilePath = null;
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

    private void TranspileAll()
    {
        IReadOnlyList<TranspileResult> results = _transpiler.TranspileMany(SourceText, TargetLanguageInfo.All);
        _generatedPrograms.Clear();

        foreach (TranspileResult result in results)
        {
            if (result.GeneratedProgram is not null)
            {
                _generatedPrograms[result.Language] = result.GeneratedProgram;
            }
        }

        IReadOnlyList<Diagnostic> diagnostics = results
            .SelectMany(result => result.Diagnostics)
            .Distinct()
            .ToArray();

        bool success = results.All(result => result.Success);

        OutputText = diagnostics.Count > 0
            ? string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.ToString()))
            : "Transpilation completed.";

        foreach (TargetPaneViewModel pane in Panes)
        {
            pane.HasValidSource = success;
            UpdatePaneForLanguage(pane);
        }

        OperationStatus = success ? "Transpiled" : "Syntax error";
        RaiseCommandStateChanged();
    }

    private async Task BuildRunVisibleAsync()
    {
        var results = new List<BuildRunResult>();

        await RunOperationAsync(
            "Build & Run visible languages",
            async cancellationToken =>
            {
                foreach (TargetPaneViewModel pane in Panes.GroupBy(pane => pane.Language).Select(group => group.First()))
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
        if (!_generatedPrograms.TryGetValue(pane.Language, out GeneratedProgram? generatedProgram))
        {
            TranspileAll();
            if (!_generatedPrograms.TryGetValue(pane.Language, out generatedProgram))
            {
                return null;
            }
        }

        pane.Status = "Running";
        AppendOutput($"=== {TargetLanguageInfo.GetDisplayName(pane.Language)} ===");

        var options = new BuildRunOptions(CreatePauseLauncher: CreatePauseLauncherAfterBuild);
        BuildRunResult result = await _toolchains.Get(pane.Language)
            .BuildAndRunAsync(generatedProgram, cancellationToken, options)
            .ConfigureAwait(true);

        pane.Status = result.Success ? "Completed" : result.Stage;
        AppendBuildRunResult(result);
        return result;
    }

    private async Task RunOperationAsync(string title, Func<CancellationToken, Task> operation)
    {
        if (IsBusy)
        {
            return;
        }

        _operationCancellation = new CancellationTokenSource();
        IsBusy = true;
        OperationStatus = title;

        try
        {
            await operation(_operationCancellation.Token).ConfigureAwait(true);
            OperationStatus = _operationCancellation.IsCancellationRequested ? "Cancelled" : "Ready";
        }
        catch (OperationCanceledException)
        {
            OperationStatus = "Cancelled";
        }
        catch (Exception ex)
        {
            OperationStatus = "Failed";
            AppendOutput(ex.Message);
        }
        finally
        {
            _operationCancellation.Dispose();
            _operationCancellation = null;
            IsBusy = false;

            foreach (TargetPaneViewModel pane in Panes)
            {
                if (pane.Status == "Running")
                {
                    pane.Status = "Ready";
                }
            }
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
        if (!_generatedPrograms.TryGetValue(pane.Language, out GeneratedProgram? generatedProgram))
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
        if (_generatedPrograms.TryGetValue(pane.Language, out GeneratedProgram? generatedProgram))
        {
            pane.GeneratedCode = generatedProgram.PrimaryFile.Content;
            pane.Status = "Ready";
        }
        else
        {
            pane.GeneratedCode = string.Empty;
            pane.Status = "No source";
        }

        UpdateToolchainStatus(pane);
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
        AppendOutput($"Generated code folder: {folderToOpen}");
        FolderOpener.OpenOrActivate(folderToOpen);
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
        !IsBusy && Panes.Any(pane => pane.CanBuild);

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
        builder.AppendLine($"Duration: {result.Duration.TotalMilliseconds:0} ms");

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
