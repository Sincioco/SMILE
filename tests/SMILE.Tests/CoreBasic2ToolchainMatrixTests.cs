using SMILE.Engine;
using SMILE.Toolchains;
using System.IO;
using System.Text.RegularExpressions;

namespace SMILE.Tests;

[TestClass]
[TestCategory("CoreBasic")]
[TestCategory("MilestoneMatrix")]
[DoNotParallelize]
public sealed class CoreBasic2ToolchainMatrixTests
{
    [TestMethod]
    [DataRow("core-basic-2-canonical.smile")]
    [DataRow("core-basic-2-byval-scope.smile")]
    [DataRow("core-basic-2-recursion.smile")]
    [DataRow("core-basic-2-arrays.smile")]
    [DataRow("core-basic-2-select.smile")]
    [DataRow("core-basic-2-parameters.smile")]
    [DataRow("core-basic-2-evaluation-order.smile")]
    [DataRow("core-basic-2-local-arrays.smile")]
    [DataRow("core-basic-2-end-program-routine.smile")]
    public async Task Profile_two_example_builds_runs_and_matches_evaluator_on_all_ten_targets(
        string exampleName)
    {
        string source = await File.ReadAllTextAsync(Path.Combine(FindExamplesDirectory(), exampleName));
        EvaluationResult expected = new SmileEvaluator().Evaluate(source);
        Assert.IsTrue(expected.Success, Join(expected.Diagnostics));

        var transpiler = new SmileTranspiler();
        ToolchainRegistry toolchains = ToolchainRegistry.CreateDefault();

        foreach (TargetLanguage language in ActiveTargetLanguages.All)
        {
            IToolchain toolchain = toolchains.Get(language);
            ToolchainStatus status = await toolchain.DetectAsync(CancellationToken.None);
            Assert.IsTrue(
                status.IsAvailable,
                $"{exampleName} / {language}: required milestone toolchain unavailable — {status.Message}");

            TranspileResult transpile = transpiler.Transpile(source, language);
            Assert.IsTrue(
                transpile.Success,
                $"{exampleName} / {language}:{Environment.NewLine}{Join(transpile.Diagnostics)}");

            BuildRunResult run = await toolchain.BuildAndRunAsync(
                transpile.GeneratedProgram!,
                CancellationToken.None);
            Assert.IsTrue(
                run.Success,
                $"{exampleName} / {language}: {run.Stage}{Environment.NewLine}" +
                run.BuildOutput + Environment.NewLine + run.StandardError);
            Assert.AreEqual(
                expected.Output,
                Normalize(run.StandardOutput),
                $"{exampleName} / {language}");
            Assert.IsFalse(
                HasCompilerWarning(run.BuildOutput),
                $"{exampleName} / {language} emitted a compiler warning.{Environment.NewLine}{run.BuildOutput}");

            Console.WriteLine($"PASS {exampleName} / {language}");
        }
    }

    [TestMethod]
    public async Task Dynamic_array_bounds_fail_with_SMILER1210_on_all_ten_targets()
    {
        const string source = """
Option Explicit
Dim Values[2] As Number
Dim Index As Number
Index = -1
Print Values[Index]
""";

        EvaluationResult expected = new SmileEvaluator().Evaluate(source);
        Assert.IsFalse(expected.Success);
        Assert.AreEqual("SMILER1210", expected.RuntimeError?.Code);

        var transpiler = new SmileTranspiler();
        ToolchainRegistry toolchains = ToolchainRegistry.CreateDefault();

        foreach (TargetLanguage language in ActiveTargetLanguages.All)
        {
            IToolchain toolchain = toolchains.Get(language);
            ToolchainStatus status = await toolchain.DetectAsync(CancellationToken.None);
            Assert.IsTrue(
                status.IsAvailable,
                $"bounds / {language}: required milestone toolchain unavailable — {status.Message}");

            TranspileResult transpile = transpiler.Transpile(source, language);
            Assert.IsTrue(transpile.Success, $"bounds / {language}: {Join(transpile.Diagnostics)}");

            BuildRunResult run = await toolchain.BuildAndRunAsync(
                transpile.GeneratedProgram!,
                CancellationToken.None);
            Assert.IsFalse(run.Success, $"bounds / {language}: execution unexpectedly succeeded.");
            StringAssert.Contains(
                run.StandardOutput + run.StandardError,
                "SMILER1210",
                $"bounds / {language}: runtime diagnostic did not preserve the SMILER1210 identity.");
            Assert.IsFalse(
                HasCompilerWarning(run.BuildOutput),
                $"bounds / {language} emitted a compiler warning.{Environment.NewLine}{run.BuildOutput}");

            Console.WriteLine($"PASS expected bounds failure / {language}");
        }
    }

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static bool HasCompilerWarning(string text) =>
        Regex.IsMatch(text, @"\bwarning(?:\s+[A-Z]+\d+|:)", RegexOptions.IgnoreCase);

    private static string Join(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.ToString()));

    private static string FindExamplesDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SMILE.sln")))
            {
                return Path.Combine(directory.FullName, "examples");
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the examples directory.");
    }
}
