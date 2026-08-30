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
