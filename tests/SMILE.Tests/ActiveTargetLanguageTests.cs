using SMILE.Desktop;
using SMILE.Engine;
using SMILE.Toolchains;

namespace SMILE.Tests;

[TestClass]
public sealed class ActiveTargetLanguageTests
{
    [TestMethod]
    public void Active_target_policy_contains_all_ten_implemented_languages()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                TargetLanguage.CSharp,
                TargetLanguage.C,
                TargetLanguage.MasmX64,
                TargetLanguage.JavaScript,
                TargetLanguage.Java,
                TargetLanguage.Cobol,
                TargetLanguage.ObjectiveC,
                TargetLanguage.Swift,
                TargetLanguage.Python,
                TargetLanguage.Cpp
            },
            ActiveTargetLanguages.All.ToArray());
        Assert.IsTrue(ActiveTargetLanguages.All.All(ActiveTargetLanguages.IsActive));
        Assert.HasCount(ActiveTargetLanguages.All.Count, ActiveTargetLanguages.All.Distinct());
    }

    [TestMethod]
    public void Complete_catalog_keeps_every_target_parseable_and_active()
    {
        Assert.HasCount(10, TargetLanguageInfo.All);
        Assert.IsTrue(TargetLanguageInfo.TryParse("python", out TargetLanguage language));
        Assert.AreEqual(TargetLanguage.Python, language);
        Assert.IsTrue(ActiveTargetLanguages.IsActive(language));
        CollectionAssert.AreEqual(
            TargetLanguageInfo.All.ToArray(),
            ActiveTargetLanguages.All.ToArray());
    }

    [TestMethod]
    public void Desktop_target_selectors_expose_all_active_targets()
    {
        var pane = new TargetPaneViewModel("Pane", TargetLanguage.CSharp);

        CollectionAssert.AreEqual(
            ActiveTargetLanguages.All.ToArray(),
            pane.LanguageOptions.Select(option => option.Language).ToArray());
    }

    [TestMethod]
    public void Desktop_keeps_the_CSharp_MASM_C_default_pane_order()
    {
        var viewModel = new MainWindowViewModel();

        CollectionAssert.AreEqual(
            new[]
            {
                TargetLanguage.CSharp,
                TargetLanguage.MasmX64,
                TargetLanguage.C
            },
            viewModel.Panes.Select(pane => pane.Language).ToArray());
    }

    [TestMethod]
    public async Task Desktop_initialization_detects_all_active_toolchains()
    {
        RecordingToolchain[] toolchains = TargetLanguageInfo.All
            .Select(language => new RecordingToolchain(language))
            .ToArray();
        var viewModel = new MainWindowViewModel(
            new ToolchainRegistry(toolchains),
            errorReporter: null,
            folderOpener: null,
            languageFilePath: null,
            languageSourceReader: _ => Task.FromResult("Print \"Hello\""));

        await viewModel.InitializeAsync();

        foreach (RecordingToolchain toolchain in toolchains)
        {
            Assert.AreEqual(1, toolchain.DetectionCount, toolchain.Language.ToString());
        }
    }

    private sealed class RecordingToolchain(TargetLanguage language) : IToolchain
    {
        public TargetLanguage Language { get; } = language;

        public int DetectionCount { get; private set; }

        public Task<ToolchainStatus> DetectAsync(CancellationToken cancellationToken)
        {
            DetectionCount++;
            string name = TargetLanguageInfo.GetDisplayName(Language);
            return Task.FromResult(new ToolchainStatus(
                Language,
                IsAvailable: true,
                name,
                Version: "test",
                Location: "test",
                $"{name} detected."));
        }

        public Task<BuildRunResult> BuildAndRunAsync(
            GeneratedProgram generatedProgram,
            CancellationToken cancellationToken,
            BuildRunOptions? options = null) =>
            throw new NotSupportedException("Build & Run is not used by active-target policy tests.");
    }
}
