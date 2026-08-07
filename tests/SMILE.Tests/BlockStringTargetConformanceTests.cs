using SMILE.Engine;
using SMILE.Toolchains;

namespace SMILE.Tests;

[TestClass]
public sealed class BlockStringTargetConformanceTests
{
    private const string RequireAllTargetsEnvironmentVariable = "SMILE_REQUIRE_ALL_TARGETS";
    private const string RequireJavaEnvironmentVariable = "SMILE_REQUIRE_JAVA";
    private const string RequireZeroWarningsEnvironmentVariable =
        "SMILE_REQUIRE_ZERO_TARGET_WARNINGS";

    private const string CanonicalRuntimeSource = """
LET Intro = "
    Hello World!
    This is SMILE!
        How are you?
"
LET Exact = "
A\0B
C\tD
"
LET Message = ""
LET Count = 0
LET Ready = TRUE

PRINT {Intro}
PRINT {Exact}

WHILE Count < 2
    IF Ready = TRUE THEN
        SET Message = "
Hello
World
"
    END IF

    PRINT {Message}
    SET Count = Count + 1
END WHILE
""";

    // Build the edge block explicitly so trailing spaces, tabs, blank lines,
    // and source escapes stay reviewable and cannot be rewritten by an
    // editor's raw-literal indentation or line-ending behavior.
    private static readonly string ExactRuntimeSource =
        CanonicalRuntimeSource +
        "\nLET EdgeWhitespace = \"\n" +
        "\n" +
        "  Leading spaces\n" +
        "\tLeading tab\n" +
        " \tMixed leading whitespace\n" +
        "\n" +
        "Trailing spaces  \n" +
        "Trailing tab\t\n" +
        "   \n" +
        "\t\n" +
        "\n" +
        "\n" +
        "\"\n" +
        "LET EdgeDelimiters = \"\n" +
        "Quotes: \" \"\" \"\"\" \"\"\"\"\n" +
        "JavaScript: ` and ${value}\n" +
        "Swift: \\\\(value) and \\\\#(value)\n" +
        "C++: )SMILE\"\n" +
        "Python: \"\"\" and '''\n" +
        "\"\n" +
        "LET EdgeControls = \"\n" +
        "Controls: CR=\\r|BS=\\b|FF=\\f|NUL=\\0|TAB=\\t|Unicode=\u53f0\u7063\ud83d\ude42\n" +
        "Text after NUL: A\\0B\n" +
        "\"\n" +
        "PRINT {EdgeWhitespace}\n" +
        "PRINT {EdgeDelimiters}\n" +
        "PRINT {EdgeControls}";

    private static readonly string ExpectedWhitespaceValue =
        "\n" +
        "  Leading spaces\n" +
        "\tLeading tab\n" +
        " \tMixed leading whitespace\n" +
        "\n" +
        "Trailing spaces  \n" +
        "Trailing tab\t\n" +
        "   \n" +
        "\t\n" +
        "\n";

    private static readonly string ExpectedDelimiterValue =
        "Quotes: \" \"\" \"\"\" \"\"\"\"\n" +
        "JavaScript: ` and ${value}\n" +
        "Swift: \\(value) and \\#(value)\n" +
        "C++: )SMILE\"\n" +
        "Python: \"\"\" and '''";

    private static readonly string ExpectedControlValue =
        "Controls: CR=\r|BS=\b|FF=\f|NUL=\0|TAB=\t|Unicode=\u53f0\u7063\ud83d\ude42\n" +
        "Text after NUL: A\0B";

    private readonly SmileTranspiler _transpiler = new();
    private readonly SmileEvaluator _evaluator = new();
    private readonly ToolchainRegistry _toolchains = ToolchainRegistry.CreateDefault();

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task Every_available_target_matches_the_evaluator_for_Block_edges_and_nested_SET()
    {
        EvaluationResult expected = _evaluator.Evaluate(ExactRuntimeSource);
        Assert.IsTrue(expected.Success, JoinDiagnostics(expected.Diagnostics));
        string expectedEdgeOutput =
            ExpectedWhitespaceValue + "\n" +
            ExpectedDelimiterValue + "\n" +
            ExpectedControlValue + "\n";
        Assert.IsTrue(
            expected.StandardOutput.EndsWith(expectedEdgeOutput, StringComparison.Ordinal),
            $"Evaluator edge corpus differed: {Visible(expected.StandardOutput)}");

        bool requireAll = EnvironmentFlagIsEnabled(RequireAllTargetsEnvironmentVariable);
        bool requireJava = EnvironmentFlagIsEnabled(RequireJavaEnvironmentVariable);
        bool requireZeroWarnings = EnvironmentFlagIsEnabled(RequireZeroWarningsEnvironmentVariable);
        var failures = new List<string>();
        int executed = 0;

        foreach (TargetLanguage language in TargetLanguageInfo.All)
        {
            IToolchain toolchain = _toolchains.Get(language);
            ToolchainStatus status = await toolchain.DetectAsync(CancellationToken.None);
            if (!status.IsAvailable)
            {
                TestContext.WriteLine($"{language}: unavailable - {status.Message}");
                if (requireAll || (requireJava && language is TargetLanguage.Java))
                {
                    failures.Add($"{language}: required toolchain unavailable - {status.Message}");
                }

                continue;
            }

            GeneratedProgram first = Generate(language);
            GeneratedProgram second = Generate(language);
            AssertGeneratedProgramsEqual(first, second, language);

            BuildRunResult actual = await toolchain.BuildAndRunAsync(
                first,
                CancellationToken.None);
            string expectedOutput = NormalizeTargetPhysicalNewLines(expected.StandardOutput);
            string actualOutput = NormalizeTargetPhysicalNewLines(actual.StandardOutput);
            string expectedError = NormalizeTargetPhysicalNewLines(expected.StandardError);
            string actualError = NormalizeTargetPhysicalNewLines(actual.StandardError);

            if (!string.Equals(actual.Stage, "Running", StringComparison.Ordinal) ||
                actual.Success != expected.Success ||
                actual.ExitCode != expected.ExitCode)
            {
                failures.Add(
                    $"{language}: expected runtime success={expected.Success}, exit={expected.ExitCode}; " +
                    $"actual stage={actual.Stage}, success={actual.Success}, exit={actual.ExitCode}." +
                    Environment.NewLine + actual.BuildOutput + Environment.NewLine + actual.StandardError);
            }

            if (!string.Equals(expectedOutput, actualOutput, StringComparison.Ordinal))
            {
                failures.Add(
                    $"{language}: stdout differed after only CRLF-to-LF physical normalization." +
                    Environment.NewLine + $"Expected: {Visible(expectedOutput)}" +
                    Environment.NewLine + $"Actual:   {Visible(actualOutput)}");
            }

            if (!string.Equals(expectedError, actualError, StringComparison.Ordinal))
            {
                failures.Add(
                    $"{language}: stderr differed after only CRLF-to-LF physical normalization." +
                    Environment.NewLine + $"Expected: {Visible(expectedError)}" +
                    Environment.NewLine + $"Actual:   {Visible(actualError)}");
            }

            if (GeneratedTargetWarningDetector.ContainsCompilerWarning(language, actual.BuildOutput))
            {
                failures.Add(
                    $"{language}: generated Block String target emitted a compiler warning." +
                    Environment.NewLine + actual.BuildOutput);
            }

            if (language is TargetLanguage.JavaScript or TargetLanguage.Python &&
                !string.IsNullOrWhiteSpace(actual.BuildOutput))
            {
                failures.Add(
                    $"{language}: interpreted target unexpectedly emitted compile-stage output." +
                    Environment.NewLine + actual.BuildOutput);
            }

            TestContext.WriteLine(
                language is TargetLanguage.JavaScript or TargetLanguage.Python
                    ? $"{language}: exact runtime matched; no compile stage"
                    : $"{language}: exact runtime matched; zero compiler warnings");
            executed++;
        }

        if (failures.Count > 0)
        {
            Assert.Fail(string.Join(Environment.NewLine + Environment.NewLine, failures));
        }

        if (requireAll)
        {
            Assert.AreEqual(
                TargetLanguageInfo.All.Count,
                executed,
                $"{RequireAllTargetsEnvironmentVariable}=1 requires all ten targets to execute.");
        }

        if (requireZeroWarnings && executed == 0)
        {
            Assert.Fail($"{RequireZeroWarningsEnvironmentVariable}=1 requires generated target validation.");
        }

        if (executed == 0)
        {
            Assert.Inconclusive("No target toolchain is installed.");
        }
    }

    private GeneratedProgram Generate(TargetLanguage language)
    {
        TranspileResult result = _transpiler.Transpile(ExactRuntimeSource, language);
        Assert.IsTrue(result.Success, JoinDiagnostics(result.Diagnostics));
        return result.GeneratedProgram!;
    }

    private static void AssertGeneratedProgramsEqual(
        GeneratedProgram expected,
        GeneratedProgram actual,
        TargetLanguage language)
    {
        Assert.HasCount(expected.Files.Count, actual.Files, language.ToString());
        for (int index = 0; index < expected.Files.Count; index++)
        {
            Assert.AreEqual(expected.Files[index].RelativePath, actual.Files[index].RelativePath);
            Assert.AreEqual(
                expected.Files[index].Content,
                actual.Files[index].Content,
                $"{language} Block String generation was not deterministic.");
        }
    }

    private static bool EnvironmentFlagIsEnabled(string name) =>
        string.Equals(Environment.GetEnvironmentVariable(name), "1", StringComparison.Ordinal);

    private static string NormalizeTargetPhysicalNewLines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string Visible(string text) =>
        "\"" + text
            .Replace("\0", "<NUL>", StringComparison.Ordinal)
            .Replace("\t", "<TAB>", StringComparison.Ordinal)
            .Replace("\b", "<BS>", StringComparison.Ordinal)
            .Replace("\f", "<FF>", StringComparison.Ordinal)
            .Replace("\r", "<CR>", StringComparison.Ordinal)
            .Replace("\n", "<LF>", StringComparison.Ordinal) + "\"";

    private static string JoinDiagnostics(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics);
}
