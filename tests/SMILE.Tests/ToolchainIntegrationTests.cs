using SMILE.Engine;
using SMILE.Toolchains;

namespace SMILE.Tests;

[TestClass]
public sealed class ToolchainIntegrationTests
{
    private const string SampleSource = """
PRINT "Hello from SMILE!"
PRINT "Different syntax, same idea."
""";

    private const string ExpectedOutput = """
Hello from SMILE!
Different syntax, same idea.
""";

    private readonly SmileTranspiler _transpiler = new();
    private readonly ToolchainRegistry _toolchains = ToolchainRegistry.CreateDefault();

    [TestMethod]
    [DataRow(TargetLanguage.CSharp)]
    [DataRow(TargetLanguage.C)]
    [DataRow(TargetLanguage.MasmX64)]
    [DataRow(TargetLanguage.JavaScript)]
    [DataRow(TargetLanguage.Java)]
    [DataRow(TargetLanguage.ObjectiveC)]
    [DataRow(TargetLanguage.Swift)]
    public async Task Installed_target_builds_or_runs_and_matches_expected_output(TargetLanguage language)
    {
        IToolchain toolchain = _toolchains.Get(language);
        ToolchainStatus status = await toolchain.DetectAsync(CancellationToken.None);

        if (!status.IsAvailable)
        {
            Assert.Inconclusive(status.Message);
        }

        GeneratedProgram program = _transpiler.Transpile(SampleSource, language).GeneratedProgram!;
        BuildRunResult result = await toolchain.BuildAndRunAsync(program, CancellationToken.None);

        Assert.IsTrue(result.Success, result.BuildOutput + Environment.NewLine + result.StandardError);
        Assert.AreEqual(Normalize(ExpectedOutput), Normalize(result.StandardOutput));
    }

    [TestMethod]
    [DataRow(TargetLanguage.CSharp)]
    [DataRow(TargetLanguage.C)]
    [DataRow(TargetLanguage.MasmX64)]
    [DataRow(TargetLanguage.JavaScript)]
    [DataRow(TargetLanguage.Java)]
    [DataRow(TargetLanguage.ObjectiveC)]
    [DataRow(TargetLanguage.Swift)]
    public async Task Installed_target_writes_press_any_key_launcher_when_requested(TargetLanguage language)
    {
        IToolchain toolchain = _toolchains.Get(language);
        ToolchainStatus status = await toolchain.DetectAsync(CancellationToken.None);

        if (!status.IsAvailable)
        {
            Assert.Inconclusive(status.Message);
        }

        GeneratedProgram program = _transpiler.Transpile(SampleSource, language).GeneratedProgram!;
        BuildRunResult result = await toolchain.BuildAndRunAsync(
            program,
            CancellationToken.None,
            new BuildRunOptions(CreatePauseLauncher: true));

        Assert.IsTrue(result.Success, result.BuildOutput + Environment.NewLine + result.StandardError);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.PauseLauncherPath));
        string launcherPath = result.PauseLauncherPath!;
        Assert.IsTrue(File.Exists(launcherPath), launcherPath);

        string launcher = await File.ReadAllTextAsync(launcherPath);
        StringAssert.Contains(launcher, ExpectedPauseLauncherCommand(language));
        StringAssert.Contains(launcher, "Press any key to exit...");
    }

    [TestMethod]
    [DataRow(TargetLanguage.ObjectiveC)]
    [DataRow(TargetLanguage.Swift)]
    public async Task Transpile_only_targets_report_unavailable_local_build_run(TargetLanguage language)
    {
        IToolchain toolchain = _toolchains.Get(language);
        ToolchainStatus status = await toolchain.DetectAsync(CancellationToken.None);

        Assert.IsFalse(status.IsAvailable);
        StringAssert.Contains(status.Message, "transpilation is available");

        GeneratedProgram program = _transpiler.Transpile(SampleSource, language).GeneratedProgram!;
        BuildRunResult result = await toolchain.BuildAndRunAsync(program, CancellationToken.None);

        Assert.IsFalse(result.Success);
        Assert.AreEqual("Transpile only", result.Stage);
        Assert.IsNull(result.WorkingDirectory);
        Assert.IsNull(result.PauseLauncherPath);
    }

    [TestMethod]
    public async Task Installed_targets_produce_identical_normalized_output()
    {
        var outputs = new Dictionary<TargetLanguage, string>();

        foreach (TargetLanguage language in TargetLanguageInfo.All)
        {
            IToolchain toolchain = _toolchains.Get(language);
            ToolchainStatus status = await toolchain.DetectAsync(CancellationToken.None);

            if (!status.IsAvailable)
            {
                continue;
            }

            GeneratedProgram program = _transpiler.Transpile(SampleSource, language).GeneratedProgram!;
            BuildRunResult result = await toolchain.BuildAndRunAsync(program, CancellationToken.None);

            Assert.IsTrue(result.Success, result.BuildOutput + Environment.NewLine + result.StandardError);
            outputs[language] = Normalize(result.StandardOutput);
        }

        if (outputs.Count < 2)
        {
            Assert.Inconclusive("Fewer than two target toolchains are installed.");
        }

        string expected = outputs.Values.First();
        Assert.IsTrue(outputs.Values.All(output => output == expected));
    }

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');

    private static string ExpectedPauseLauncherCommand(TargetLanguage language) =>
        language switch
        {
            TargetLanguage.CSharp => "\"bin\\Debug\\net10.0\\GeneratedProgram.exe\"",
            TargetLanguage.C => "\"Program.exe\"",
            TargetLanguage.MasmX64 => "\"Program.exe\"",
            TargetLanguage.JavaScript => "node Program.js",
            TargetLanguage.Java => "java Program",
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, null)
        };
}
