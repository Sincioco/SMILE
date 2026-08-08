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
    private bool _hasUserEdits;
    private bool _hasToolchain;
    private bool _hasValidSource;
    private bool _hasSyntaxError;
    private bool _isBusy;
    private bool _isMaximized;
    private bool _isApplyingGeneratedCode;
    private long _userEditRevision;

    public TargetPaneViewModel(string title, TargetLanguage defaultLanguage)
    {
        _baseTitle = title;
        LanguageOptions = ActiveTargetLanguages.All
            .Select(language => new TargetLanguageOption(language))
            .ToArray();
        _selectedLanguageOption = LanguageOptions.Single(option => option.Language == defaultLanguage);
    }

    public event EventHandler? SelectedLanguageChanged;

    public event EventHandler? UserSourceChanged;

    public string DisplayTitle => $"{_baseTitle} - {SelectedLanguageOption.DisplayName}";

    public string Title => HasUserEdits ? $"{DisplayTitle} *" : DisplayTitle;

    public IReadOnlyList<TargetLanguageOption> LanguageOptions { get; }

    public TargetLanguageOption SelectedLanguageOption
    {
        get => _selectedLanguageOption;
        set
        {
            if (SetProperty(ref _selectedLanguageOption, value))
            {
                // A language switch changes the meaning of every character in
                // this pane. The selected target's generated snapshot will
                // replace the old text through the normal refresh path.
                MarkGeneratedCodeStale();
                OnPropertyChanged(nameof(Language));
                OnPropertyChanged(nameof(DisplayTitle));
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
        set
        {
            if (!SetProperty(ref _generatedCode, value ?? string.Empty))
            {
                return;
            }

            bool isUserEdit = !_isApplyingGeneratedCode;
            if (isUserEdit)
            {
                // AvalonEdit's two-way binding reaches this setter only for a
                // learner edit. Generated snapshots use ApplyGeneratedCode so
                // Build & Run can distinguish the editable primary file from
                // the compiler-owned cache and companion files.
                _userEditRevision++;
                OnPropertyChanged(nameof(UserEditRevision));
                SetHasUserEdits(true);
                HasValidSource = !string.IsNullOrWhiteSpace(_generatedCode);
                // The syntax flag describes the SMILE snapshot that failed to
                // generate this pane, not source the learner writes directly
                // in the destination language.
                HasSyntaxError = false;
                Status = "Edited";
            }

            RaiseCommandStateChanged();
            if (isUserEdit)
            {
                UserSourceChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool HasUserEdits => _hasUserEdits;

    public long UserEditRevision => _userEditRevision;

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

    public bool IsMaximized
    {
        get => _isMaximized;
        set
        {
            if (SetProperty(ref _isMaximized, value))
            {
                OnPropertyChanged(nameof(MaximizeButtonText));
            }
        }
    }

    public string MaximizeButtonText => IsMaximized ? "Restore" : "Maximize";

    public string BuildButtonText =>
        Language switch
        {
            TargetLanguage.JavaScript or TargetLanguage.Python => "Run",
            _ => "Build & Run"
        };

    public bool CanUseSource =>
        HasValidSource && !string.IsNullOrWhiteSpace(GeneratedCode);

    public bool CanBuild =>
        HasToolchain && CanUseSource && !HasSyntaxError && !IsBusy;

    public bool CanChangeLanguage => !IsBusy;

    public ICommand? CopyCommand { get; set; }

    public ICommand? SaveSourceCommand { get; set; }

    public ICommand? BuildRunCommand { get; set; }

    public void ApplyGeneratedCode(string code)
    {
        _isApplyingGeneratedCode = true;
        try
        {
            GeneratedCode = code;
        }
        finally
        {
            _isApplyingGeneratedCode = false;
        }

        SetHasUserEdits(false);
        RaiseCommandStateChanged();
    }

    public void MarkGeneratedCodeStale()
    {
        SetHasUserEdits(false);
        HasValidSource = false;
        RaiseCommandStateChanged();
    }

    private void SetHasUserEdits(bool value)
    {
        if (SetProperty(ref _hasUserEdits, value, nameof(HasUserEdits)))
        {
            OnPropertyChanged(nameof(Title));
        }
    }

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

}
