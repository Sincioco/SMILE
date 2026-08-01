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
    private TargetLanguageOption _selectedLanguageOption;
    private string _generatedCode = string.Empty;
    private string _status = "Ready";
    private string _toolchainStatusText = "Toolchain not detected.";
    private bool _hasToolchain;
    private bool _hasValidSource;
    private bool _isBusy;

    public TargetPaneViewModel(string title, TargetLanguage defaultLanguage)
    {
        Title = title;
        LanguageOptions = TargetLanguageInfo.All
            .Select(language => new TargetLanguageOption(language))
            .ToArray();
        _selectedLanguageOption = LanguageOptions.Single(option => option.Language == defaultLanguage);
    }

    public event EventHandler? SelectedLanguageChanged;

    public string Title { get; }

    public IReadOnlyList<TargetLanguageOption> LanguageOptions { get; }

    public TargetLanguageOption SelectedLanguageOption
    {
        get => _selectedLanguageOption;
        set
        {
            if (SetProperty(ref _selectedLanguageOption, value))
            {
                OnPropertyChanged(nameof(Language));
                OnPropertyChanged(nameof(BuildButtonText));
                SelectedLanguageChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public TargetLanguage Language => SelectedLanguageOption.Language;

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

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaiseCommandStateChanged();
            }
        }
    }

    public string BuildButtonText =>
        Language == TargetLanguage.JavaScript ? "Run" : "Build & Run";

    public bool CanUseSource =>
        HasValidSource && !string.IsNullOrWhiteSpace(GeneratedCode) && !IsBusy;

    public bool CanBuild =>
        CanUseSource && HasToolchain;

    public ICommand? CopyCommand { get; set; }

    public ICommand? SaveSourceCommand { get; set; }

    public ICommand? BuildRunCommand { get; set; }

    public void RaiseCommandStateChanged()
    {
        OnPropertyChanged(nameof(CanUseSource));
        OnPropertyChanged(nameof(CanBuild));

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
