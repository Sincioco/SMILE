using System.Text.RegularExpressions;
using SMILE.Engine;
using SMILE.Toolchains;

namespace SMILE.Tests;

[TestClass]
[TestCategory("ActiveTarget")]
[TestCategory("Toolchain")]
public sealed class ActiveTargetNativeSmokeTests
{
    private const string Source = """
LET age = 0
PRINT How old are you?
INPUT age
PRINT $"You are {age} years old."
""";

    private readonly SmileTranspiler _transpiler = new();
    private readonly ToolchainRegistry _toolchains = ToolchainRegistry.CreateDefault();

    [TestMethod]
    [DoNotParallelize]
    [DataRow(TargetLanguage.CSharp)]
    [DataRow(TargetLanguage.C)]
    [DataRow(TargetLanguage.MasmX64)]
    public async Task Canonical_native_INPUT_builds_and_runs_when_the_active_toolchain_is_installed(
        TargetLanguage language)
    {
        BuildRunResult run = await BuildAndRunAsync(language, Source, "49\n");

        Assert.AreEqual(
            "How old are you?\nYou are 49 years old.\n",
            NormalizeLineEndings(run.StandardOutput));
    }

    [TestMethod]
    [DoNotParallelize]
    [DataRow(TargetLanguage.CSharp)]
    [DataRow(TargetLanguage.C)]
    [DataRow(TargetLanguage.MasmX64)]
    public async Task Native_String_and_Boolean_INPUT_build_and_run_when_the_active_toolchain_is_installed(
        TargetLanguage language)
    {
        const string source = """
LET Name = ""
LET Ready = FALSE
LET Finished = TRUE
INPUT Name
INPUT Ready
INPUT Finished
PRINT {Name}
PRINT {Ready}
PRINT {Finished}
""";

        BuildRunResult run = await BuildAndRunAsync(language, source, "Sin\nTRUE\nFALSE\n");

        Assert.AreEqual("Sin\nTRUE\nFALSE\n", NormalizeLineEndings(run.StandardOutput));
    }

    [TestMethod]
    [DoNotParallelize]
    [DataRow(TargetLanguage.CSharp)]
    [DataRow(TargetLanguage.C)]
    [DataRow(TargetLanguage.MasmX64)]
    public async Task Native_mixed_Integer_then_String_INPUT_consumes_the_expected_lines(
        TargetLanguage language)
    {
        const string source = """
LET Age = 0
LET Name = ""
INPUT Age
INPUT Name
PRINT $"{Name}:{Age}"
""";

        BuildRunResult run = await BuildAndRunAsync(language, source, "49\nSin\n");

        Assert.AreEqual("Sin:49\n", NormalizeLineEndings(run.StandardOutput));
    }

    [TestMethod]
    [DoNotParallelize]
    [DataRow(TargetLanguage.CSharp)]
    [DataRow(TargetLanguage.C)]
    [DataRow(TargetLanguage.MasmX64)]
    public async Task Native_loop_String_copy_keeps_value_semantics_across_later_INPUT(
        TargetLanguage language)
    {
        const string source = """
LET Name = ""
LET Saved = ""
LET Count = 0
WHILE Count < 2
    INPUT Name
    IF Count = 0 THEN
        SET Saved = Name
    END IF
    SET Count = Count + 1
END WHILE
PRINT {Saved}
PRINT {Name}
""";

        BuildRunResult run = await BuildAndRunAsync(language, source, "First\nSecond\n");

        Assert.AreEqual("First\nSecond\n", NormalizeLineEndings(run.StandardOutput));
    }

    [TestMethod]
    [DoNotParallelize]
    [DataRow(TargetLanguage.CSharp)]
    [DataRow(TargetLanguage.C)]
    [DataRow(TargetLanguage.MasmX64)]
    public async Task Native_invalid_Boolean_INPUT_fails_before_the_next_statement(
        TargetLanguage language)
    {
        const string source = """
LET Ready = FALSE
INPUT Ready
PRINT After
""";

        BuildRunResult run = await BuildAndRunCoreAsync(language, source, "YES\n");

        Assert.AreEqual("Running", run.Stage, run.BuildOutput + Environment.NewLine + run.StandardError);
        Assert.IsFalse(run.Success, $"{language} accepted invalid Boolean INPUT.");
        Assert.AreNotEqual(0, run.ExitCode, $"{language} returned success for invalid Boolean INPUT.");
        Assert.IsFalse(
            run.StandardOutput.Contains("After", StringComparison.Ordinal),
            $"{language} executed the statement after failed INPUT.");
    }

    private async Task<BuildRunResult> BuildAndRunAsync(
        TargetLanguage language,
        string source,
        string standardInput)
    {
        BuildRunResult run = await BuildAndRunCoreAsync(language, source, standardInput);

        Assert.IsTrue(
            run.Success,
            $"{language}: {run.Stage}{Environment.NewLine}{run.BuildOutput}{Environment.NewLine}{run.StandardError}");
        return run;
    }

    private async Task<BuildRunResult> BuildAndRunCoreAsync(
        TargetLanguage language,
        string source,
        string standardInput)
    {
        IToolchain toolchain = _toolchains.Get(language);
        ToolchainStatus status = await toolchain.DetectAsync(CancellationToken.None);
        if (!status.IsAvailable)
        {
            Assert.Inconclusive(status.Message);
        }

        TranspileResult transpile = _transpiler.Transpile(source, language);
        Assert.IsTrue(
            transpile.Success,
            string.Join(Environment.NewLine, transpile.Diagnostics.Select(item => item.Message)));

        BuildRunResult run = await toolchain.BuildAndRunAsync(
            transpile.GeneratedProgram!,
            CancellationToken.None,
            BuildRunOptions.Scripted(standardInput));

        Assert.IsFalse(
            Regex.IsMatch(run.BuildOutput, @"\bwarning\s+[A-Z]+\d+\b", RegexOptions.IgnoreCase),
            $"{language} emitted a generated-target compiler/linker warning.{Environment.NewLine}{run.BuildOutput}");
        return run;
    }

    private static string NormalizeLineEndings(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
}
