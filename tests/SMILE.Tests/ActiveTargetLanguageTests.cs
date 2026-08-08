using SMILE.Desktop;
using SMILE.Engine;
using SMILE.Toolchains;

namespace SMILE.Tests;

[TestClass]
public sealed class ActiveTargetLanguageTests
{
    [TestMethod]
    public void Active_target_policy_contains_only_CSharp_C_and_MASM()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                TargetLanguage.CSharp,
                TargetLanguage.C,
                TargetLanguage.MasmX64
            },
            ActiveTargetLanguages.All.ToArray());
        Assert.IsTrue(ActiveTargetLanguages.All.All(ActiveTargetLanguages.IsActive));
        Assert.HasCount(ActiveTargetLanguages.All.Count, ActiveTargetLanguages.All.Distinct());
    }

    [TestMethod]
    public void Complete_catalog_keeps_paused_targets_parseable_but_inactive()
    {
        Assert.HasCount(10, TargetLanguageInfo.All);
        Assert.IsTrue(TargetLanguageInfo.TryParse("python", out TargetLanguage language));
        Assert.AreEqual(TargetLanguage.Python, language);
        Assert.IsFalse(ActiveTargetLanguages.IsActive(language));
        Assert.IsTrue(ActiveTargetLanguages.All.All(TargetLanguageInfo.All.Contains));
    }

    [TestMethod]
    public void Desktop_target_selectors_expose_only_active_targets()
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
    public async Task Desktop_initialization_detects_only_active_toolchains()
    {
        RecordingToolchain[] toolchains = TargetLanguageInfo.All
            .Select(language => new RecordingToolchain(language))
            .ToArray();
        var viewModel = new MainWindowViewModel(
            new ToolchainRegistry(toolchains),
            errorReporter: null,
            folderOpener: null,
            languageFilePath: null,
            languageSourceReader: _ => Task.FromResult("PRINT Hello"));

        await viewModel.InitializeAsync();

        foreach (RecordingToolchain toolchain in toolchains)
        {
            Assert.AreEqual(
                ActiveTargetLanguages.IsActive(toolchain.Language) ? 1 : 0,
                toolchain.DetectionCount,
                toolchain.Language.ToString());
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
