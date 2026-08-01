using System.IO;
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

                    await BuildRunPaneCoreAsync(pane, cancellationToken).ConfigureAwait(true);
                }
            }).ConfigureAwait(true);
    }

    private async Task BuildRunPaneAsync(TargetPaneViewModel pane)
    {
        await RunOperationAsync(
            $"{TargetLanguageInfo.GetDisplayName(pane.Language)} {pane.BuildButtonText}",
            cancellationToken => BuildRunPaneCoreAsync(pane, cancellationToken)).ConfigureAwait(true);
    }

    private async Task BuildRunPaneCoreAsync(TargetPaneViewModel pane, CancellationToken cancellationToken)
    {
        if (!_generatedPrograms.TryGetValue(pane.Language, out GeneratedProgram? generatedProgram))
        {
            TranspileAll();
            if (!_generatedPrograms.TryGetValue(pane.Language, out generatedProgram))
            {
                return;
            }
        }

        pane.Status = "Running";
        AppendOutput($"=== {TargetLanguageInfo.GetDisplayName(pane.Language)} ===");

        BuildRunResult result = await _toolchains.Get(pane.Language)
            .BuildAndRunAsync(generatedProgram, cancellationToken)
            .ConfigureAwait(true);

        pane.Status = result.Success ? "Completed" : result.Stage;
        AppendBuildRunResult(result);
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
}
