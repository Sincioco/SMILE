using System.IO;
using SMILE.Desktop;
using SMILE.Engine;
using SMILE.Toolchains;

namespace SMILE.Tests;

[TestClass]
[TestCategory("CoreBasic")]
public sealed class CoreBasicDesktopTests
{
    [TestMethod]
    public async Task Desktop_loads_and_transpiles_the_canonical_language_reference()
    {
        string source = await File.ReadAllTextAsync(FindRepositoryFile("examples", "language.smile"));
        var viewModel = new MainWindowViewModel(
            CreateRegistry(),
            languageFilePath: null,
            errorReporter: null,
            folderOpener: null,
            languageSourceReader: _ => Task.FromResult(source));

        await viewModel.InitializeAsync();

        Assert.AreEqual(source, viewModel.SourceText);
        Assert.IsTrue(viewModel.Panes.All(pane => pane.HasValidSource));
        Assert.IsTrue(viewModel.Panes.All(pane => !pane.HasSyntaxError));
    }

    [TestMethod]
    public void Desktop_exposes_no_language_profile_selector()
    {
        string xaml = File.ReadAllText(FindRepositoryFile("src", "SMILE.Desktop", "MainWindow.xaml"));

        Assert.IsFalse(xaml.Contains("Dialect", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(xaml.Contains("Legacy", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Desktop_exposes_find_and_go_to_line_for_the_active_editor()
    {
        string xaml = File.ReadAllText(FindRepositoryFile("src", "SMILE.Desktop", "MainWindow.xaml"));

        StringAssert.Contains(xaml, "Header=\"_Find...\"");
        StringAssert.Contains(xaml, "InputGestureText=\"Ctrl+F\"");
        StringAssert.Contains(xaml, "Header=\"_Go to Line...\"");
        StringAssert.Contains(xaml, "InputGestureText=\"Ctrl+G\"");
        Assert.HasCount(2, xaml.Split("GotKeyboardFocus=\"CodeEditor_GotKeyboardFocus\"").Skip(1));
    }

    [TestMethod]
    public void Find_panel_uses_visible_text_labels_instead_of_ambiguous_bitmap_buttons()
    {
        string xaml = File.ReadAllText(FindRepositoryFile("src", "SMILE.Desktop", "App.xaml"));

        StringAssert.Contains(xaml, "Content=\"Previous\"");
        StringAssert.Contains(xaml, "Content=\"Next\"");
        StringAssert.Contains(xaml, "Content=\"Close\"");
        StringAssert.Contains(xaml, "Content=\"Match case\"");
        StringAssert.Contains(xaml, "Content=\"Whole words\"");
        StringAssert.Contains(xaml, "Content=\"Regex\"");
        Assert.IsFalse(xaml.Contains("prev.png", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(xaml.Contains("next.png", StringComparison.OrdinalIgnoreCase));
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(new[] { current.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }

    private static ToolchainRegistry CreateRegistry() => new(
        ActiveTargetLanguages.All.Select(language => new RecordingToolchain(language)));

    private sealed class RecordingToolchain(TargetLanguage language) : IToolchain
    {
        public TargetLanguage Language { get; } = language;

        public Task<ToolchainStatus> DetectAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ToolchainStatus(
                Language,
                IsAvailable: true,
                TargetLanguageInfo.GetDisplayName(Language),
                "test",
                "test",
                "Toolchain detected."));

        public Task<BuildRunResult> BuildAndRunAsync(
            GeneratedProgram generatedProgram,
            CancellationToken cancellationToken,
            BuildRunOptions? options = null) =>
            throw new NotSupportedException();
    }
}
