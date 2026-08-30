using System.Diagnostics;
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
    internal const string LanguageFileName = "language.smile";

    // Live transpilation is intentionally delayed a little. A compiler front
    // end usually works on complete snapshots of source text, so this debounce
    // lets the user finish a burst of typing before the lexer/parser/generator
    // pipeline runs in the background.
    private static readonly TimeSpan LiveTranspileDelay = TimeSpan.FromMilliseconds(250);
    internal const int MaxOutputTextLength = 1_000_000;
    internal const string OutputTruncatedMarker = "[Older SMILE output was truncated.]";

    private readonly SmileTranspiler _transpiler = new();
    private readonly ToolchainRegistry _toolchains;
    private readonly IAppErrorReporter _errorReporter;
    private readonly IFolderOpener _folderOpener;
    private readonly string _languageFilePath;
    private readonly Func<CancellationToken, Task<string>> _languageSourceReader;
    private readonly Func<CancellationToken, Task>? _liveGenerationGate;
    private readonly Func<string, string, string?> _generatedSourcePathSelector;
    private readonly Dictionary<TargetLanguage, GeneratedSnapshot> _generatedPrograms = new();
    private readonly Dictionary<TargetLanguage, ToolchainStatus> _toolchainStatuses = new();
    private CancellationTokenSource? _operationCancellation;
    private CancellationTokenSource? _liveTranspileCancellation;
    private Task? _liveTranspileTask;
    private string _sourceText = string.Empty;
    private string _outputText = string.Empty;
    private string _operationStatus = "Ready";
    private string? _currentFilePath;
    private long _sourceRevision;
    private bool _isBusy;
    private bool _openGeneratedFolderAfterBuild = true;
    private bool _createPauseLauncherAfterBuild = true;
    private bool _outputShowsLiveDiagnostics;
    public MainWindowViewModel()
        : this(
            ToolchainRegistry.CreateDefault(),
            AppErrorReporter.Shared,
            new SystemFolderOpener(),
            languageFilePath: null)
    {
    }

    public MainWindowViewModel(
        ToolchainRegistry toolchains,
        IAppErrorReporter? errorReporter = null,
        IFolderOpener? folderOpener = null,
        string? languageFilePath = null)
        : this(
            toolchains,
            errorReporter,
            folderOpener,
            languageFilePath,
            languageSourceReader: null,
            liveGenerationGate: null,
            generatedSourcePathSelector: null)
    {
    }

    internal MainWindowViewModel(
        ToolchainRegistry toolchains,
        IAppErrorReporter? errorReporter,
        IFolderOpener? folderOpener,
        string? languageFilePath,
        Func<CancellationToken, Task<string>>? languageSourceReader,
        Func<CancellationToken, Task>? liveGenerationGate = null,
        Func<string, string, string?>? generatedSourcePathSelector = null)
    {
        _toolchains = toolchains;
        _errorReporter = errorReporter ?? AppErrorReporter.Shared;
        _folderOpener = folderOpener ?? new SystemFolderOpener();
        _languageFilePath = languageFilePath ?? Path.Combine(AppContext.BaseDirectory, LanguageFileName);
        _languageSourceReader = languageSourceReader ?? (cancellationToken =>
            File.ReadAllTextAsync(_languageFilePath, Encoding.UTF8, cancellationToken));
        _liveGenerationGate = liveGenerationGate;
        _generatedSourcePathSelector = generatedSourcePathSelector ?? SelectGeneratedSourcePath;

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

    private sealed record PaneGenerationState(
        TargetPaneViewModel Pane,
        TargetLanguage Language,
        long UserEditRevision);

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
                if (_sourceText.Length == 0)
                {
                    ResetGeneratedTargetsForEmptySource();
                }
                else
                {
                    ScheduleLiveTranspilation();
                }
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

    public string SessionId => _errorReporter.SessionId;

    public async Task InitializeAsync()
    {
        try
        {
            long revisionBeforeLoad = _sourceRevision;
            string? languageSource = await LoadLanguageSourceAsync(CancellationToken.None).ConfigureAwait(true);
            if (languageSource is not null &&
                revisionBeforeLoad == 0 &&
                revisionBeforeLoad == _sourceRevision &&
                _currentFilePath is null)
            {
                SourceText = languageSource;
            }

            await TranspileVisibleCurrentSourceAsync(CancellationToken.None).ConfigureAwait(true);
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
            RefreshVisiblePaneAfterLanguageChange(pane);
        };
        pane.UserSourceChanged += (_, _) => RaiseCommandStateChanged();
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

        foreach (TargetLanguage language in ActiveTargetLanguages.All)
        {
            ToolchainStatus status;
            try
            {
                status = await _toolchains.Get(language)
                    .DetectAsync(CancellationToken.None)
                    .ConfigureAwait(true);
            }
            catch (Exception ex) when (!DesktopExceptionPolicy.IsFatal(ex))
            {
                string languageName = TargetLanguageInfo.GetDisplayName(language);
                string details = ReportException("Toolchain detection", ex, languageName, "Detection");
                status = new ToolchainStatus(
                    language,
                    IsAvailable: false,
                    languageName,
                    Version: null,
                    Location: null,
                    $"Detection failed: {ex.GetType().Name}: {ex.Message}");
                AppendConciseError("Toolchain detection", ex, details, languageName, "Detection");
            }

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
        CancelLiveTranspilation();

        // New is an editor reset, not a second request for the packaged
        // language reference. Advancing the revision even when the editor was
        // already empty prevents a pending startup read from winning the race
        // and putting language.smile back into the new document.
        _sourceRevision++;
        if (_sourceText.Length != 0)
        {
            _sourceText = string.Empty;
            OnPropertyChanged(nameof(SourceText));
        }

        _currentFilePath = null;
        ResetGeneratedTargetsForEmptySource();
    }

    private async Task<string?> LoadLanguageSourceAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _languageSourceReader(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (!DesktopExceptionPolicy.IsFatal(ex))
        {
            string details = ReportException("Load language reference", ex, stage: LanguageFileName);
            AppendConciseError("Load language reference", ex, details, stage: LanguageFileName);
            return null;
        }
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

        if (SourceText.Length == 0)
        {
            ResetGeneratedTargetsForEmptySource();
            if (isManual)
            {
                OperationStatus = "Completed";
            }

            return;
        }

        string sourceSnapshot = SourceText;
        long revision = _sourceRevision;
        IReadOnlyList<TranspileResult> results = await GenerateAsync(
            sourceSnapshot,
            ActiveTargetLanguages.All,
            cancellationToken).ConfigureAwait(true);

        if (revision == _sourceRevision)
        {
            ApplyTranspileResults(
                results,
                revision,
                isLive: false,
                reportSuccess: isManual,
                preserveUserEdits: false);
        }
    }

    private async Task TranspileVisibleCurrentSourceAsync(CancellationToken cancellationToken)
    {
        CancelLiveTranspilation();

        if (SourceText.Length == 0)
        {
            return;
        }

        string sourceSnapshot = SourceText;
        long revision = _sourceRevision;
        TargetLanguage[] languages = Panes
            .Select(pane => pane.Language)
            .Distinct()
            .ToArray();
        PaneGenerationState[] paneGenerationStates = Panes
            .Select(pane => new PaneGenerationState(
                pane,
                pane.Language,
                pane.UserEditRevision))
            .ToArray();
        IReadOnlyList<TranspileResult> results = await GenerateAsync(
            sourceSnapshot,
            languages,
            cancellationToken).ConfigureAwait(true);

        if (revision != _sourceRevision)
        {
            return;
        }

        ApplyTranspileResults(
            results,
            revision,
            isLive: true,
            reportSuccess: false,
            preserveUserEdits: true,
            paneGenerationStates: paneGenerationStates);
        if (results.All(result => result.Success))
        {
            OperationStatus = "Ready";
        }
    }

    private void ScheduleLiveTranspilation()
    {
        RefreshVisiblePanesAndScheduleLiveTranspilation(clearExistingSyntaxErrors: true);
    }

    private void RefreshVisiblePaneAfterLanguageChange(TargetPaneViewModel changedPane)
    {
        if (SourceText.Length == 0)
        {
            CancelLiveTranspilation();
            UpdateToolchainStatus(changedPane);
            changedPane.ApplyGeneratedCode(string.Empty);
            changedPane.HasValidSource = false;
            changedPane.HasSyntaxError = false;
            changedPane.Status = GetReadyStatus(changedPane);
            OperationStatus = "Ready";
            RaiseCommandStateChanged();
            return;
        }

        RefreshVisiblePanesAndScheduleLiveTranspilation(clearExistingSyntaxErrors: false);
    }

    private void RefreshVisiblePanesAndScheduleLiveTranspilation(
        bool clearExistingSyntaxErrors,
        TargetPaneViewModel? preservedPane = null,
        string? operationStatusAfterCompletion = null)
    {
        if (IsBusy)
        {
            return;
        }

        CancelLiveTranspilation();

        if (SourceText.Length == 0)
        {
            ResetGeneratedTargetsForEmptySource();
            return;
        }

        var missingLanguages = new List<TargetLanguage>();
        var paneGenerationStates = new List<PaneGenerationState>();
        foreach (TargetPaneViewModel pane in Panes)
        {
            if (ReferenceEquals(pane, preservedPane))
            {
                continue;
            }

            if (clearExistingSyntaxErrors)
            {
                pane.HasSyntaxError = false;
            }

            UpdateToolchainStatus(pane);
            if (TryApplyCurrentGeneratedProgram(
                    pane,
                    preserveUserEdits: !clearExistingSyntaxErrors))
            {
                pane.RaiseCommandStateChanged();
                continue;
            }

            if (!pane.HasSyntaxError)
            {
                // A source edit invalidates old generated programs; a language
                // switch may simply reveal a target that was not visible during
                // the last live transpile. In both cases, only missing visible
                // targets need compiler work. Cached targets stay ready, which
                // keeps rapid ComboBox changes from flooding the UI thread.
                pane.MarkGeneratedCodeStale();
                pane.Status = "Updating";
                missingLanguages.Add(pane.Language);
                paneGenerationStates.Add(new PaneGenerationState(
                    pane,
                    pane.Language,
                    pane.UserEditRevision));
            }

            pane.RaiseCommandStateChanged();
        }

        TargetLanguage[] languages = missingLanguages.Distinct().ToArray();
        if (languages.Length == 0)
        {
            OperationStatus = operationStatusAfterCompletion ??
                (Panes.Any(pane => pane.HasSyntaxError) ? "Syntax Error" : "Ready");
            RaiseCommandStateChanged();
            return;
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
            paneGenerationStates.ToArray(),
            cancellation,
            LiveTranspileDelay,
            preserveUserEdits: !clearExistingSyntaxErrors,
            preservedPane,
            operationStatusAfterCompletion);
    }

    private async Task RunLiveTranspilationAsync(
        string sourceSnapshot,
        long revision,
        IReadOnlyList<TargetLanguage> languages,
        IReadOnlyList<PaneGenerationState> paneGenerationStates,
        CancellationTokenSource cancellation,
        TimeSpan delay,
        bool preserveUserEdits,
        TargetPaneViewModel? preservedPane,
        string? operationStatusAfterCompletion)
    {
        try
        {
            await Task.Delay(delay, cancellation.Token).ConfigureAwait(true);

            if (_liveGenerationGate is not null)
            {
                await _liveGenerationGate(cancellation.Token).ConfigureAwait(true);
            }

            IReadOnlyList<TranspileResult> results = await GenerateAsync(
                sourceSnapshot,
                languages,
                cancellation.Token).ConfigureAwait(true);

            if (cancellation.IsCancellationRequested || revision != _sourceRevision)
            {
                return;
            }

            ApplyTranspileResults(
                results,
                revision,
                isLive: true,
                reportSuccess: false,
                preserveUserEdits,
                preservedPane,
                paneGenerationStates);
            if (operationStatusAfterCompletion is not null)
            {
                OperationStatus = operationStatusAfterCompletion;
            }
            else if (results.All(result => result.Success))
            {
                OperationStatus = "Ready";
            }
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
        bool reportSuccess,
        bool preserveUserEdits,
        TargetPaneViewModel? preservedPane = null,
        IReadOnlyList<PaneGenerationState>? paneGenerationStates = null)
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

            // A one-target generation can happen while Build & Run is moving
            // through the visible panes. Refresh only panes whose language was
            // just generated so a later target cannot erase an earlier pane's
            // Completed or Failed status.
            HashSet<TargetLanguage> generatedLanguages = results
                .Select(result => result.Language)
                .ToHashSet();
            foreach (TargetPaneViewModel pane in Panes.Where(pane =>
                         !ReferenceEquals(pane, preservedPane) &&
                         generatedLanguages.Contains(pane.Language)))
            {
                if (!CanApplyGenerationToPane(pane, paneGenerationStates))
                {
                    pane.RaiseCommandStateChanged();
                    continue;
                }

                UpdatePaneForLanguage(pane, preserveUserEdits);
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
            if (ReferenceEquals(pane, preservedPane))
            {
                continue;
            }

            if (!CanApplyGenerationToPane(pane, paneGenerationStates))
            {
                pane.RaiseCommandStateChanged();
                continue;
            }

            if (preserveUserEdits && pane.HasUserEdits)
            {
                pane.RaiseCommandStateChanged();
                continue;
            }

            pane.ApplyGeneratedCode(string.Empty);
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

    private void ResetGeneratedTargetsForEmptySource()
    {
        CancelLiveTranspilation();
        _generatedPrograms.Clear();

        foreach (TargetPaneViewModel pane in Panes)
        {
            pane.ApplyGeneratedCode(string.Empty);
            pane.HasValidSource = false;
            pane.HasSyntaxError = false;
            pane.Status = GetReadyStatus(pane);
            pane.RaiseCommandStateChanged();
        }

        if (_outputShowsLiveDiagnostics)
        {
            OutputText = string.Empty;
            _outputShowsLiveDiagnostics = false;
        }

        OperationStatus = "Ready";
        RaiseCommandStateChanged();
    }

    private static bool CanApplyGenerationToPane(
        TargetPaneViewModel pane,
        IReadOnlyList<PaneGenerationState>? paneGenerationStates)
    {
        if (paneGenerationStates is null)
        {
            return true;
        }

        PaneGenerationState? capturedState = paneGenerationStates.FirstOrDefault(
            state => ReferenceEquals(state.Pane, pane));
        return capturedState is not null &&
            capturedState.Language == pane.Language &&
            capturedState.UserEditRevision == pane.UserEditRevision;
    }

    private async Task BuildRunVisibleAsync()
    {
        var results = new List<BuildRunResult>();

        await RunOperationAsync(
            "Build & Run visible panes",
            async cancellationToken =>
            {
                foreach (TargetPaneViewModel pane in Panes.Where(CanPreparePaneSource))
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

        OperationStatus = results.Any(result => !result.Success && !result.Cancelled)
            ? "Completed with failures"
            : OperationStatus;

        await OpenGeneratedFolderForResultsAsync(results).ConfigureAwait(true);
    }

    private async Task BuildRunPaneAsync(TargetPaneViewModel pane)
    {
        BuildRunResult? result = null;
        bool resumePendingVisibleTranspilation = _liveTranspileTask is not null;

        await RunOperationAsync(
            $"{TargetLanguageInfo.GetDisplayName(pane.Language)} {pane.BuildButtonText}",
            async cancellationToken =>
            {
                result = await BuildRunPaneCoreAsync(pane, cancellationToken).ConfigureAwait(true);
            }).ConfigureAwait(true);

        if (result is not null && !result.Success)
        {
            OperationStatus = result.Cancelled ? "Cancelled" : "Failed";
        }

        await OpenGeneratedFolderForResultsAsync(result is null ? Array.Empty<BuildRunResult>() : new[] { result })
            .ConfigureAwait(true);

        if (resumePendingVisibleTranspilation && SourceText.Length > 0)
        {
            // Building this pane intentionally cancelled the shared debounce.
            // Resume only previews still missing for the same source revision;
            // preserve the learner-edited pane and its build result.
            RefreshVisiblePanesAndScheduleLiveTranspilation(
                clearExistingSyntaxErrors: false,
                preservedPane: pane,
                operationStatusAfterCompletion: OperationStatus);
        }
    }

    private async Task<BuildRunResult?> BuildRunPaneCoreAsync(
        TargetPaneViewModel pane,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        TargetLanguage language = pane.Language;
        string languageName = TargetLanguageInfo.GetDisplayName(language);
        _outputShowsLiveDiagnostics = false;

        try
        {
            AppendOutput($"=== {pane.DisplayTitle} ===");

            if (!_toolchainStatuses.TryGetValue(language, out ToolchainStatus? status) || !status.IsAvailable)
            {
                pane.Status = status?.Message.StartsWith("Detection failed:", StringComparison.Ordinal) == true
                    ? "Detection Failed"
                    : "Toolchain Missing";
                AppendOutput(status?.Message ?? "Toolchain not detected.");
                return null;
            }

            GeneratedProgram? generatedProgram = await GetProgramForPaneAsync(pane, cancellationToken)
                .ConfigureAwait(true);
            if (generatedProgram is null)
            {
                pane.Status = "Syntax Error";
                return null;
            }

            GeneratedProgram runnableProgram = WithCurrentPrimarySource(
                generatedProgram,
                pane.GeneratedCode);

            pane.Status = GetInitialBuildStatus(language);

            BuildRunOptions options = new(CreatePauseLauncher: CreatePauseLauncherAfterBuild);
            BuildRunResult result = await _toolchains.Get(language)
                .BuildAndRunAsync(runnableProgram, cancellationToken, options)
                .ConfigureAwait(true);

            pane.Status = BuildRunStatusText(result);
            AppendBuildRunResult(result);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            pane.Status = "Cancelled";
            BuildRunResult result = CreateDesktopFailureResult(
                language,
                "Cancelled",
                new OperationCanceledException("Build & Run was cancelled by the user."),
                "n/a",
                stopwatch.Elapsed,
                cancelled: true);
            AppendBuildRunResult(result);
            return result;
        }
        catch (Exception ex) when (!DesktopExceptionPolicy.IsFatal(ex))
        {
            string stage = pane.Status is "Ready" or "Updating" ? "Build & Run" : pane.Status;
            string details = ReportException("Build & Run", ex, languageName, stage);
            pane.Status = "Failed";
            BuildRunResult result = CreateDesktopFailureResult(language, stage, ex, details, stopwatch.Elapsed);
            AppendBuildRunResult(result);
            return result;
        }
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

        ApplyTranspileResults(
            results,
            revision,
            isLive: false,
            reportSuccess: false,
            preserveUserEdits: true);
        return _generatedPrograms.TryGetValue(language, out GeneratedSnapshot? currentSnapshot) &&
            currentSnapshot.SourceRevision == revision
            ? currentSnapshot.Program
            : null;
    }

    private async Task<GeneratedProgram?> GetProgramForPaneAsync(
        TargetPaneViewModel pane,
        CancellationToken cancellationToken)
    {
        TargetLanguage language = pane.Language;
        long revision = _sourceRevision;
        string sourceSnapshot = SourceText;

        if (_generatedPrograms.TryGetValue(language, out GeneratedSnapshot? snapshot) &&
            snapshot.SourceRevision == revision)
        {
            if (!pane.HasUserEdits)
            {
                TryApplyCurrentGeneratedProgram(pane);
            }

            return snapshot.Program;
        }

        if (!pane.HasUserEdits)
        {
            GeneratedProgram? generatedProgram = await EnsureCurrentGeneratedProgramAsync(
                language,
                cancellationToken).ConfigureAwait(true);
            return revision == _sourceRevision && pane.Language == language
                ? generatedProgram
                : null;
        }

        // Generate the current SMILE snapshot only to recover target-owned
        // metadata and companion files. The learner's primary target source
        // stays visible and authoritative. A blank or currently invalid SMILE
        // document falls back to the target's minimal empty-program container.
        IReadOnlyList<TranspileResult> results = await GenerateAsync(
            sourceSnapshot,
            new[] { language },
            cancellationToken).ConfigureAwait(true);
        TranspileResult result = results.Single();
        bool representsCurrentSource = result.Success && result.GeneratedProgram is not null;

        if (!representsCurrentSource && sourceSnapshot.Length > 0)
        {
            results = await GenerateAsync(
                string.Empty,
                new[] { language },
                cancellationToken).ConfigureAwait(true);
            result = results.Single();
        }

        if (revision != _sourceRevision || pane.Language != language)
        {
            return null;
        }

        if (!result.Success || result.GeneratedProgram is null)
        {
            return null;
        }

        // An empty fallback is build metadata, not generated source for an
        // invalid nonempty SMILE revision, so never expose it through the live
        // preview cache.
        if (representsCurrentSource)
        {
            _generatedPrograms[language] = new GeneratedSnapshot(
                revision,
                result.GeneratedProgram);
        }

        return result.GeneratedProgram;
    }

    private static GeneratedProgram WithCurrentPrimarySource(
        GeneratedProgram generatedProgram,
        string primarySource) =>
        generatedProgram with
        {
            Files = generatedProgram.Files
                .Select(file => file.IsPrimary
                    ? file with { Content = primarySource }
                    : file)
                .ToArray()
        };

    private async Task RunOperationAsync(string title, Func<CancellationToken, Task> operation)
    {
        if (IsBusy)
        {
            return;
        }

        CancellationTokenSource? cancellation = null;

        try
        {
            cancellation = new CancellationTokenSource();
            _operationCancellation = cancellation;
            CancelLiveTranspilation();
            SafeSetBusy(true);
            OperationStatus = title;

            await operation(cancellation.Token).ConfigureAwait(true);
            OperationStatus = cancellation.IsCancellationRequested ? "Cancelled" : "Completed";
        }
        catch (OperationCanceledException) when (cancellation?.IsCancellationRequested == true)
        {
            RecoverAfterCancellation();
        }
        catch (Exception ex) when (!DesktopExceptionPolicy.IsFatal(ex))
        {
            RecoverAfterOperationFailure(title, ex);
        }
        finally
        {
            if (ReferenceEquals(_operationCancellation, cancellation))
            {
                _operationCancellation = null;
            }

            cancellation?.Dispose();
            SafeSetBusy(false);
            SafeRaiseCommandStateChanged();
        }
    }

    private void Cancel()
    {
        try
        {
            _operationCancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        AppendOutput("Cancellation requested.");
        OperationStatus = "Cancelling...";
    }

    private async Task SaveGeneratedSourceAsync(TargetPaneViewModel pane)
    {
        GeneratedProgram? generatedProgram = await GetProgramForPaneAsync(
            pane,
            CancellationToken.None).ConfigureAwait(true);
        if (generatedProgram is null)
        {
            return;
        }

        string? filePath = _generatedSourcePathSelector(
            generatedProgram.PrimaryFile.RelativePath,
            TargetLanguageInfo.GetDisplayName(pane.Language));
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        await File.WriteAllTextAsync(filePath, pane.GeneratedCode).ConfigureAwait(true);
        OperationStatus = $"Saved {Path.GetFileName(filePath)}";
    }

    private static string? SelectGeneratedSourcePath(string fileName, string languageDisplayName)
    {
        var dialog = new SaveFileDialog
        {
            FileName = fileName,
            Title = $"Save {languageDisplayName} source",
            Filter = "All files (*.*)|*.*"
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private void UpdatePaneForLanguage(
        TargetPaneViewModel pane,
        bool preserveUserEdits = true)
    {
        UpdateToolchainStatus(pane);

        if (SourceText.Length == 0)
        {
            // Toolchain detection can finish after New. Preserve any target
            // source the learner has typed since that reset while still
            // settling untouched blank panes into their ready state.
            if (!pane.HasUserEdits)
            {
                pane.ApplyGeneratedCode(string.Empty);
                pane.HasValidSource = false;
                pane.HasSyntaxError = false;
            }

            pane.Status = GetReadyStatus(pane);
            pane.RaiseCommandStateChanged();
            return;
        }

        if (!TryApplyCurrentGeneratedProgram(pane, preserveUserEdits) && !pane.HasSyntaxError)
        {
            pane.HasValidSource = false;
            pane.Status = "Updating";
        }

        pane.RaiseCommandStateChanged();
    }

    private bool TryApplyCurrentGeneratedProgram(
        TargetPaneViewModel pane,
        bool preserveUserEdits = false)
    {
        if (preserveUserEdits && pane.HasUserEdits)
        {
            return true;
        }

        if (!_generatedPrograms.TryGetValue(pane.Language, out GeneratedSnapshot? snapshot) ||
            snapshot.SourceRevision != _sourceRevision)
        {
            return false;
        }

        pane.ApplyGeneratedCode(snapshot.Program.PrimaryFile.Content);
        pane.HasValidSource = true;
        pane.HasSyntaxError = false;
        pane.Status = GetReadyStatus(pane);
        return true;
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

    private async Task OpenGeneratedFolderForResultsAsync(IReadOnlyList<BuildRunResult> results)
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
        await OpenGeneratedFolderAsync(folderToOpen).ConfigureAwait(true);
    }

    private async Task OpenGeneratedFolderAsync(string folderToOpen)
    {
        try
        {
            AppendOutput($"Generated code folder: {folderToOpen}");
            await _folderOpener.OpenAsync(folderToOpen, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex) when (!DesktopExceptionPolicy.IsFatal(ex))
        {
            string details = ReportException("Open generated folder", ex, stage: "Explorer Launch");
            AppendOutput(
                "Build completed, but the generated folder could not be opened." +
                Environment.NewLine +
                $"{ex.GetType().Name}: {ex.Message}" +
                Environment.NewLine +
                $"Details: {details}");
        }
    }

    private static string GetFolderToOpen(IReadOnlyList<string> folders)
    {
        if (folders.Count == 1)
        {
            return folders[0];
        }

        // A visible-panes build creates one temp workspace per pane build.
        // Opening their shared parent gives the learner one folder view where
        // all generated-code folders from the operation can be inspected.
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
        Panes.Any(pane =>
            pane.HasToolchain &&
            CanPreparePaneSource(pane));

    private bool CanPreparePaneSource(TargetPaneViewModel pane) =>
        !pane.HasSyntaxError &&
        (pane.CanUseSource ||
         (SourceText.Length > 0 && !pane.HasUserEdits));

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

        if (IsUnexpectedDesktopFailure(result))
        {
            builder.AppendLine(result.StandardError.TrimEnd());
            builder.AppendLine($"Total duration: {result.Duration.TotalMilliseconds:0} ms");
            AppendOutput(builder.ToString().TrimEnd());
            return;
        }

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
        text = BoundOutputChunk(text);

        if (string.IsNullOrWhiteSpace(OutputText) || OutputText == "Transpilation completed.")
        {
            OutputText = text;
            return;
        }

        OutputText = TrimOutputHistory(OutputText + Environment.NewLine + Environment.NewLine + text);
    }

    private void HandleCommandException(Exception exception) =>
        HandleUiError("Command", exception);

    private void HandleUiError(string operation, Exception exception)
    {
        OperationStatus = "Failed";
        string details = ReportException(operation, exception);
        AppendConciseError(operation, exception, details);
    }

    public void HandleGlobalException(string operation, Exception exception, string details)
    {
        OperationStatus = "Failed";
        foreach (TargetPaneViewModel pane in Panes.Where(IsActiveBuildStatus))
        {
            pane.Status = "Failed";
        }

        SafeSetBusy(false);
        AppendConciseError(operation, exception, details);
        SafeRaiseCommandStateChanged();
    }

    internal void AppendOutputForTesting(string text) => AppendOutput(text);

    private void RecoverAfterCancellation()
    {
        OperationStatus = "Cancelled";
        foreach (TargetPaneViewModel pane in Panes.Where(IsActiveBuildStatus))
        {
            pane.Status = "Cancelled";
        }
    }

    private void RecoverAfterOperationFailure(string operation, Exception exception)
    {
        OperationStatus = "Failed";
        foreach (TargetPaneViewModel pane in Panes.Where(IsActiveBuildStatus))
        {
            pane.Status = "Failed";
        }

        string details = ReportException(operation, exception);
        AppendConciseError(operation, exception, details);
    }

    private void SafeSetBusy(bool value)
    {
        try
        {
            IsBusy = value;
        }
        catch (Exception ex) when (!DesktopExceptionPolicy.IsFatal(ex))
        {
            _isBusy = value;
            ReportException("Set busy state", ex, stage: value ? "Busy" : "Idle");
        }
    }

    private void SafeRaiseCommandStateChanged()
    {
        try
        {
            RaiseCommandStateChanged();
        }
        catch (Exception ex) when (!DesktopExceptionPolicy.IsFatal(ex))
        {
            ReportException("Raise command state", ex, stage: "Command Notification");
        }
    }

    private string ReportException(
        string operation,
        Exception exception,
        string? target = null,
        string? stage = null) =>
        _errorReporter.Report(operation, exception, target, stage, _sourceRevision);

    private void AppendConciseError(
        string operation,
        Exception exception,
        string details,
        string? target = null,
        string? stage = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine(target is null ? $"=== {operation} Error ===" : $"=== {target} {operation} Error ===");
        if (!string.IsNullOrWhiteSpace(stage))
        {
            builder.AppendLine($"Stage: {stage}");
        }

        builder.AppendLine($"{exception.GetType().Name}: {exception.Message}");
        builder.AppendLine($"Details: {details}");
        builder.Append("SMILE remains open. Correct the issue and try again.");
        AppendOutput(builder.ToString());
    }

    private BuildRunResult CreateDesktopFailureResult(
        TargetLanguage language,
        string stage,
        Exception exception,
        string details,
        TimeSpan duration,
        bool cancelled = false)
    {
        string languageName = TargetLanguageInfo.GetDisplayName(language);
        var status = new ToolchainStatus(
            language,
            IsAvailable: true,
            languageName,
            Version: null,
            Location: null,
            cancelled ? "Cancelled by user." : "Unexpected desktop/toolchain failure.");

        string message = cancelled
            ? "Cancelled by user."
            : $"Unexpected failure during {stage}." +
              Environment.NewLine +
              $"{exception.GetType().Name}: {exception.Message}" +
              Environment.NewLine +
              $"Details: {details}" +
              Environment.NewLine +
              "SMILE remains open.";

        return new BuildRunResult(
            language,
            Success: false,
            status,
            BuildOutput: string.Empty,
            StandardOutput: string.Empty,
            StandardError: message,
            ExitCode: null,
            duration,
            TimedOut: false,
            Cancelled: cancelled,
            WorkingDirectory: null,
            PauseLauncherPath: null,
            Stage: stage);
    }

    private void CancelLiveTranspilation()
    {
        _liveTranspileCancellation?.Cancel();
        _liveTranspileCancellation = null;
        _liveTranspileTask = null;
    }

    private string GetReadyStatus(TargetPaneViewModel pane)
    {
        if (pane.HasToolchain)
        {
            return "Ready";
        }

        return _toolchainStatuses.TryGetValue(pane.Language, out ToolchainStatus? status) &&
            status.Message.StartsWith("Detection failed:", StringComparison.Ordinal)
            ? "Detection Failed"
            : "Toolchain Missing";
    }

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

    private static void Exit() =>
        Application.Current.Shutdown();

    private static bool IsUnexpectedDesktopFailure(BuildRunResult result) =>
        !result.Success &&
        !result.TimedOut &&
        !result.Cancelled &&
        result.ExitCode is null &&
        result.StandardError.StartsWith("Unexpected failure during", StringComparison.Ordinal);

    private static bool IsActiveBuildStatus(TargetPaneViewModel pane) =>
        pane.Status is "Building" or "Assembling" or "Linking" or "Running" or "Cancelling";

    private static string BoundOutputChunk(string text)
    {
        if (text.Length <= MaxOutputTextLength)
        {
            return text;
        }

        return OutputTruncatedMarker +
            Environment.NewLine +
            text[^Math.Min(text.Length, MaxOutputTextLength - OutputTruncatedMarker.Length - Environment.NewLine.Length)..];
    }

    private static string TrimOutputHistory(string text)
    {
        if (text.Length <= MaxOutputTextLength)
        {
            return text;
        }

        int keep = MaxOutputTextLength - OutputTruncatedMarker.Length - Environment.NewLine.Length;
        if (keep <= 0)
        {
            return OutputTruncatedMarker;
        }

        string newest = text[^keep..];
        int sectionBoundary = newest.IndexOf(Environment.NewLine + Environment.NewLine, StringComparison.Ordinal);
        if (sectionBoundary >= 0)
        {
            newest = newest[(sectionBoundary + Environment.NewLine.Length * 2)..];
        }

        return OutputTruncatedMarker + Environment.NewLine + newest;
    }

    private void ShowAbout()
    {
        Assembly assembly = typeof(MainWindowViewModel).Assembly;
        string version =
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
            assembly.GetName().Version?.ToString() ??
            "unknown";
        const string mission = "SMILE is a modern programming language inspired by BASIC, designed to help newcomers learn not only how to write code, but also how programming languages work at a fundamental level. Building on BASIC’s simplicity and accessibility, SMILE allows students to transpile their code into multiple programming languages and compile the resulting programs. This enables learners to see how the same logic and concepts are expressed using different languages and syntaxes. Through this comparative approach, students can recognize an essential principle: despite their surface-level differences, all programming languages share the same core fundamentals. The primary goal is therefore not to memorize the syntax of a particular language, but to develop logical thinking, problem-solving skills, and a strong understanding of programming concepts. By combining simplicity, experimentation, and cross-language learning, SMILE provides a fun and educational environment that teaches students how to think like programmers.";

        MessageBox.Show(
            $"SMILE - Simple Modern Interactive Learning Environment{Environment.NewLine}Version {version}{Environment.NewLine}Session {SessionId}{Environment.NewLine}{Environment.NewLine}{mission}",
            "About SMILE",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}
