using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using SMILE.Engine;
using SMILE.Toolchains;

namespace SMILE.Tests;

[TestClass]
public sealed class GeneratedWarningHygieneTests
{
    private const string RequireAllTargetsEnvironmentVariable = "SMILE_REQUIRE_ALL_TARGETS";
    private const string RequireZeroTargetWarningsEnvironmentVariable =
        "SMILE_REQUIRE_ZERO_TARGET_WARNINGS";

    private const string DirectSelfAssignmentSource = """
LET Name = "Sin"
LET Count = 49
LET Ready = TRUE

SET Name = Name
SET Count = Count
SET Ready = Ready

PRINT {Name}
PRINT {Count}
PRINT {Ready}
""";

    private readonly SmileTranspiler _transpiler = new();
    private readonly SmileEvaluator _evaluator = new();
    private readonly ToolchainRegistry _toolchains = ToolchainRegistry.CreateDefault();

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(TargetLanguage.CSharp, "Program.cs(10,9): warning CS1717: Assignment made to same variable")]
    [DataRow(TargetLanguage.CSharp, "PROGRAM.CS(10,9): WARNING cs1717: Assignment made to same variable")]
    [DataRow(TargetLanguage.C, "Program.c(8): warning C4101: 'value': unreferenced local variable")]
    [DataRow(TargetLanguage.Cpp, "LINK : warning LNK4099: PDB was not found")]
    [DataRow(TargetLanguage.MasmX64, "Program.asm(8) : warning A4013: instructions changed by optimizer")]
    [DataRow(TargetLanguage.MasmX64, "LINK : warning LNK4210: .CRT section exists")]
    [DataRow(TargetLanguage.Java, "Program.java:10: warning: redundant cast")]
    [DataRow(TargetLanguage.Cobol, "Program.cob:10: warning: overlapping MOVE may occur")]
    [DataRow(TargetLanguage.ObjectiveC, "Program.m:10:9: warning: explicitly assigning value to itself")]
    [DataRow(TargetLanguage.Swift, "Program.swift:10:6: warning: variable was never mutated")]
    public void Compiler_warning_detector_recognizes_target_diagnostics(
        TargetLanguage language,
        string output)
    {
        Assert.IsTrue(
            GeneratedTargetWarningDetector.ContainsCompilerWarning(language, output),
            output);
    }

    [TestMethod]
    [DataRow("Build succeeded.")]
    [DataRow("0 Warning(s)")]
    [DataRow("Warnings: 0")]
    [DataRow("No warnings were produced.")]
    [DataRow("Program.cs(10,9): error CS1002: ; expected")]
    public void CSharp_warning_detector_ignores_zero_warning_prose_and_errors(string output)
    {
        Assert.IsFalse(
            GeneratedTargetWarningDetector.ContainsCompilerWarning(TargetLanguage.CSharp, output),
            output);
    }

    [TestMethod]
    [DataRow(TargetLanguage.JavaScript)]
    [DataRow(TargetLanguage.Python)]
    public void Interpreted_targets_do_not_report_compile_diagnostics(TargetLanguage language)
    {
        Assert.IsFalse(
            GeneratedTargetWarningDetector.ContainsCompilerWarning(
                language,
                "Program: warning: this text did not come from a compile stage"));
    }

    [TestMethod]
    public async Task Generated_CSharp_self_assignment_builds_runs_and_has_zero_compiler_warnings()
    {
        GeneratedProgram program = Generate(DirectSelfAssignmentSource, TargetLanguage.CSharp);
        string csharp = program.PrimaryFile.Content;

        StringAssert.Contains(csharp, "Name = Name + \"\";");
        StringAssert.Contains(csharp, "Count = Count + 0;");
        StringAssert.Contains(csharp, "Ready = Ready || false;");
        Assert.IsFalse(csharp.Contains("Name = Name;", StringComparison.Ordinal));
        Assert.IsFalse(csharp.Contains("Count = Count;", StringComparison.Ordinal));
        Assert.IsFalse(csharp.Contains("Ready = Ready;", StringComparison.Ordinal));

        await AssertCSharpMatchesEvaluatorWithoutWarnings(
            "direct self-assignment acceptance program",
            DirectSelfAssignmentSource,
            program);
    }

    [TestMethod]
    public async Task Generated_CSharp_wide_and_mapped_self_assignments_have_zero_compiler_warnings()
    {
        const string source = """
LET Count = 5000000000
LET class = "Sin"

SET Count = Count
SET CLASS = class

PRINT {Count}
PRINT {Class}
""";

        GeneratedProgram program = Generate(source, TargetLanguage.CSharp);
        string csharp = program.PrimaryFile.Content;

        StringAssert.Contains(csharp, "long Count = 5000000000L;");
        StringAssert.Contains(csharp, "Count = Count + 0;");
        StringAssert.Contains(csharp, "string _smile_class = \"Sin\";");
        StringAssert.Contains(csharp, "_smile_class = _smile_class + \"\";");

        await AssertCSharpMatchesEvaluatorWithoutWarnings(
            "wide and mapped self-assignment program",
            source,
            program);
    }

    [TestMethod]
    public async Task Generated_CSharp_language_reference_builds_runs_and_has_zero_compiler_warnings()
    {
        string languagePath = Path.Combine(AppContext.BaseDirectory, "language.smile");
        Assert.IsTrue(File.Exists(languagePath), languagePath);
        string source = await File.ReadAllTextAsync(languagePath, Encoding.UTF8);

        StringAssert.Contains(source, "SET LastName = LastName");
        GeneratedProgram program = Generate(source, TargetLanguage.CSharp);
        Assert.IsFalse(
            program.PrimaryFile.Content.Contains("LastName = LastName;", StringComparison.Ordinal));

        await AssertCSharpMatchesEvaluatorWithoutWarnings(
            "language.smile",
            source,
            program,
            InputTestData.CanonicalScriptedInput);
    }

    [TestMethod]
    public async Task Available_targets_run_direct_self_assignment_without_compiler_warnings()
    {
        EvaluationResult reference = _evaluator.Evaluate(DirectSelfAssignmentSource);
        Assert.IsTrue(reference.Success, JoinDiagnostics(reference.Diagnostics));

        bool requireAllTargets = EnvironmentFlagIsEnabled(RequireAllTargetsEnvironmentVariable);
        bool requireZeroWarnings = EnvironmentFlagIsEnabled(
            RequireZeroTargetWarningsEnvironmentVariable);
        var failures = new List<string>();
        int executed = 0;

        TestContext.WriteLine(
            $"{RequireZeroTargetWarningsEnvironmentVariable}={(requireZeroWarnings ? "1" : "0")}");

        foreach (TargetLanguage language in TargetLanguageInfo.All)
        {
            IToolchain toolchain = _toolchains.Get(language);
            ToolchainStatus status = await toolchain.DetectAsync(CancellationToken.None);
            if (!status.IsAvailable)
            {
                TestContext.WriteLine($"{language}: unavailable - {status.Message}");
                if (requireAllTargets ||
                    (requireZeroWarnings && language is TargetLanguage.CSharp))
                {
                    failures.Add($"{language}: required toolchain unavailable - {status.Message}");
                }

                continue;
            }

            GeneratedProgram program = Generate(DirectSelfAssignmentSource, language);
            BuildRunResult result = await toolchain.BuildAndRunAsync(
                program,
                CancellationToken.None);

            int failureCountBeforeTarget = failures.Count;
            string compilerOutput = FormatBuildAndErrorOutput(result);
            if (GeneratedTargetWarningDetector.ContainsCompilerWarning(language, compilerOutput))
            {
                failures.Add(
                    $"{language}: generated target emitted a compiler warning.{Environment.NewLine}" +
                    compilerOutput);
            }

            if (language is TargetLanguage.JavaScript or TargetLanguage.Python &&
                !string.IsNullOrWhiteSpace(result.BuildOutput))
            {
                failures.Add(
                    $"{language}: interpreted target unexpectedly reported compile-stage output." +
                    Environment.NewLine + result.BuildOutput);
            }

            if (!result.Success || result.ExitCode != 0)
            {
                failures.Add(
                    $"{language}: build/run failed.{Environment.NewLine}" +
                    compilerOutput);
            }
            else if (!string.Equals(
                    NormalizePhysicalNewlines(reference.Output),
                    NormalizePhysicalNewlines(result.StandardOutput),
                    StringComparison.Ordinal))
            {
                failures.Add($"{language}: stdout differed from SmileEvaluator.");
            }

            if (failures.Count != failureCountBeforeTarget)
            {
                continue;
            }

            TestContext.WriteLine(
                language is TargetLanguage.JavaScript or TargetLanguage.Python
                    ? $"{language}: no compile stage; runtime matched SmileEvaluator"
                    : $"{language}: compiler emitted zero detected warnings; runtime matched SmileEvaluator");
            executed++;
        }

        if (failures.Count > 0)
        {
            Assert.Fail(string.Join(Environment.NewLine + Environment.NewLine, failures));
        }

        if (requireAllTargets)
        {
            Assert.AreEqual(
                TargetLanguageInfo.All.Count,
                executed,
                $"{RequireAllTargetsEnvironmentVariable}=1 requires every target to execute.");
        }

        if (executed == 0)
        {
            Assert.Inconclusive("No target toolchains are installed.");
        }
    }

    private async Task AssertCSharpMatchesEvaluatorWithoutWarnings(
        string programName,
        string source,
        GeneratedProgram program,
        string? scriptedInput = null)
    {
        IToolchain toolchain = _toolchains.Get(TargetLanguage.CSharp);
        ToolchainStatus status = await toolchain.DetectAsync(CancellationToken.None);
        if (!status.IsAvailable)
        {
            if (EnvironmentFlagIsEnabled(RequireZeroTargetWarningsEnvironmentVariable))
            {
                Assert.Fail(
                    $"{RequireZeroTargetWarningsEnvironmentVariable}=1 requires C#: {status.Message}");
            }

            Assert.Inconclusive(status.Message);
        }

        EvaluationResult reference = scriptedInput is null
            ? _evaluator.Evaluate(source)
            : _evaluator.Evaluate(source, scriptedInput);
        Assert.IsTrue(reference.Success, JoinDiagnostics(reference.Diagnostics));

        BuildRunResult result = await toolchain.BuildAndRunAsync(
            program,
            CancellationToken.None,
            scriptedInput is null ? null : BuildRunOptions.Scripted(scriptedInput));
        Assert.IsTrue(result.Success, FormatBuildAndErrorOutput(result));
        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual(
            NormalizePhysicalNewlines(reference.Output),
            NormalizePhysicalNewlines(result.StandardOutput),
            $"{programName} stdout differed from SmileEvaluator.");

        string compilerOutput = FormatBuildAndErrorOutput(result);
        Assert.IsFalse(
            GeneratedTargetWarningDetector.ContainsCompilerWarning(
                TargetLanguage.CSharp,
                compilerOutput),
            $"{programName} emitted a C# compiler warning.{Environment.NewLine}{compilerOutput}");

        TestContext.WriteLine(
            $"C#: {programName} built and ran with zero detected compiler warnings.");
    }

    private GeneratedProgram Generate(string source, TargetLanguage language)
    {
        TranspileResult result = _transpiler.Transpile(source, language);
        Assert.IsTrue(result.Success, JoinDiagnostics(result.Diagnostics));
        return result.GeneratedProgram!;
    }

    private static bool EnvironmentFlagIsEnabled(string name) =>
        string.Equals(
            Environment.GetEnvironmentVariable(name),
            "1",
            StringComparison.Ordinal);

    private static string FormatBuildAndErrorOutput(BuildRunResult result) =>
        string.Join(
            Environment.NewLine,
            new[] { result.BuildOutput, result.StandardError }
                .Where(output => !string.IsNullOrWhiteSpace(output)));

    private static string NormalizePhysicalNewlines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string JoinDiagnostics(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics);
}

internal static class GeneratedTargetWarningDetector
{
    private static readonly Regex CSharpCompilerWarning = CreateRegex(
        @"\bwarning\s+CS\d{4}\b");

    private static readonly Regex MsvcCompilerOrLinkerWarning = CreateRegex(
        @"\bwarning\s+(?:C|LNK)\d{4}\b");

    private static readonly Regex MasmAssemblerOrLinkerWarning = CreateRegex(
        @"\bwarning\s+(?:A|LNK)\d{4}\b");

    private static readonly Regex ColonStyleCompilerWarning = CreateRegex(
        @"\bwarning\s*:");

    public static bool ContainsCompilerWarning(TargetLanguage language, string? output)
    {
        if (string.IsNullOrEmpty(output))
        {
            return false;
        }

        return language switch
        {
            TargetLanguage.CSharp => CSharpCompilerWarning.IsMatch(output),
            TargetLanguage.C or TargetLanguage.Cpp =>
                MsvcCompilerOrLinkerWarning.IsMatch(output),
            TargetLanguage.MasmX64 => MasmAssemblerOrLinkerWarning.IsMatch(output),
            TargetLanguage.Java or
            TargetLanguage.Cobol or
            TargetLanguage.ObjectiveC or
            TargetLanguage.Swift => ColonStyleCompilerWarning.IsMatch(output),
            TargetLanguage.JavaScript or TargetLanguage.Python => false,
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, null)
        };
    }

    private static Regex CreateRegex(string pattern) =>
        new(
            pattern,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
}
