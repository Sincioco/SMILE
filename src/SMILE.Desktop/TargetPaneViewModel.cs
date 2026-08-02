using System.Windows.Input;
using SMILE.Engine;

namespace SMILE.Desktop;

public sealed class TargetLanguageOption
{
    public TargetLanguageOption(TargetLanguage language)
    {
        Language = language;
        DisplayName = TargetLanguageInfo.GetDisplayName(language);
    }

    public TargetLanguage Language { get; }

    public string DisplayName { get; }

    public override string ToString() => DisplayName;
}

public sealed class TargetPaneViewModel : ViewModelBase
{
    private readonly string _baseTitle;
    private TargetLanguageOption _selectedLanguageOption;
    private string _generatedCode = string.Empty;
    private string _status = "Ready";
    private string _toolchainStatusText = "Toolchain not detected.";
    private bool _hasToolchain;
    private bool _hasValidSource;
    private bool _hasSyntaxError;
    private bool _isBusy;

    public TargetPaneViewModel(string title, TargetLanguage defaultLanguage)
    {
        _baseTitle = title;
        LanguageOptions = TargetLanguageInfo.All
            .Select(language => new TargetLanguageOption(language))
            .ToArray();
        _selectedLanguageOption = LanguageOptions.Single(option => option.Language == defaultLanguage);
    }

    public event EventHandler? SelectedLanguageChanged;

    public string Title => $"{_baseTitle} - {SelectedLanguageOption.DisplayName}";

    public IReadOnlyList<TargetLanguageOption> LanguageOptions { get; }

    public TargetLanguageOption SelectedLanguageOption
    {
        get => _selectedLanguageOption;
        set
        {
            if (SetProperty(ref _selectedLanguageOption, value))
            {
                OnPropertyChanged(nameof(Language));
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(BuildButtonText));
                OnPropertyChanged(nameof(HighlightingId));
                SelectedLanguageChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public TargetLanguage Language => SelectedLanguageOption.Language;

    public string HighlightingId => TargetLanguageInfo.GetStableId(Language);

    public string GeneratedCode
    {
        get => _generatedCode;
        set => SetProperty(ref _generatedCode, value);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public string ToolchainStatusText
    {
        get => _toolchainStatusText;
        set => SetProperty(ref _toolchainStatusText, value);
    }

    public bool HasToolchain
    {
        get => _hasToolchain;
        set
        {
            if (SetProperty(ref _hasToolchain, value))
            {
                RaiseCommandStateChanged();
            }
        }
    }

    public bool HasValidSource
    {
        get => _hasValidSource;
        set
        {
            if (SetProperty(ref _hasValidSource, value))
            {
                RaiseCommandStateChanged();
            }
        }
    }

    public bool HasSyntaxError
    {
        get => _hasSyntaxError;
        set
        {
            if (SetProperty(ref _hasSyntaxError, value))
            {
                RaiseCommandStateChanged();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanChangeLanguage));
                RaiseCommandStateChanged();
            }
        }
    }

    public string BuildButtonText =>
        Language switch
        {
            TargetLanguage.JavaScript => "Run",
            TargetLanguage.ObjectiveC => "Transpile Only",
            TargetLanguage.Swift => "Transpile Only",
            _ => "Build & Run"
        };

    public bool CanUseSource =>
        HasValidSource && !string.IsNullOrWhiteSpace(GeneratedCode);

    public bool CanBuild =>
        (HasToolchain || IsTranspileOnlyLanguage(Language)) && !HasSyntaxError && !IsBusy;

    public bool CanChangeLanguage => !IsBusy;

    public ICommand? CopyCommand { get; set; }

    public ICommand? SaveSourceCommand { get; set; }

    public ICommand? BuildRunCommand { get; set; }

    public void RaiseCommandStateChanged()
    {
        OnPropertyChanged(nameof(CanUseSource));
        OnPropertyChanged(nameof(CanBuild));
        OnPropertyChanged(nameof(CanChangeLanguage));

        if (CopyCommand is RelayCommand copy)
        {
            copy.RaiseCanExecuteChanged();
        }

        if (SaveSourceCommand is AsyncRelayCommand save)
        {
            save.RaiseCanExecuteChanged();
        }

        if (BuildRunCommand is AsyncRelayCommand build)
        {
            build.RaiseCanExecuteChanged();
        }
    }

    private static bool IsTranspileOnlyLanguage(TargetLanguage language) =>
        language is TargetLanguage.ObjectiveC or TargetLanguage.Swift;
}
