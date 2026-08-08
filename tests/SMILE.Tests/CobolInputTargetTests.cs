using System.IO;
using SMILE.Engine;
using SMILE.Toolchains;

namespace SMILE.Tests;

[TestClass]
public sealed class CobolInputTargetTests
{
    private const string AcceptanceSource = """
REM SMILE v0.7.0 INPUT acceptance program

LET Name = ""
LET Age = 0
LET Ready = FALSE

PRINT Enter your name:
INPUT Name

PRINT Enter your age:
INPUT Age

PRINT Enter TRUE or FALSE:
INPUT Ready

PRINT Name=[{Name}]

IF Age >= 18 THEN
    PRINT Age group=Adult
ELSE
    PRINT Age group=Minor
END IF

IF Ready = TRUE THEN
    PRINT Ready=TRUE
ELSE
    PRINT Ready=FALSE
END IF
""";

    private readonly SmileTranspiler _transpiler = new();
    private readonly SmileEvaluator _evaluator = new();
    private readonly IToolchain _toolchain =
        ToolchainRegistry.CreateDefault().Get(TargetLanguage.Cobol);

    [TestMethod]
    public void COBOL_INPUT_uses_one_deterministic_C_companion_and_preserves_layout()
    {
        GeneratedProgram generated = Generate(AcceptanceSource);

        Assert.IsTrue(generated.RequiresStandardInput);
        Assert.HasCount(2, generated.Files);
        Assert.AreEqual("Program.cob", generated.PrimaryFile.RelativePath);
        GeneratedFile companion = generated.Files.Single(file => !file.IsPrimary);
        Assert.AreEqual("SmileRuntime.c", companion.RelativePath);
        StringAssert.Contains(generated.PrimaryFile.Content, "01 Name PIC X(4096)");
        StringAssert.Contains(generated.PrimaryFile.Content, "CALL \"smile_input_0\" USING");
        StringAssert.Contains(generated.PrimaryFile.Content, "CALL \"smile_input_1\" USING");
        StringAssert.Contains(generated.PrimaryFile.Content, "CALL \"smile_input_2\" USING");
        Assert.IsFalse(generated.PrimaryFile.Content.Contains("ACCEPT ", StringComparison.Ordinal));
        StringAssert.Contains(generated.PrimaryFile.Content, "*> SMILE v0.7.0 INPUT acceptance program");
        StringAssert.Contains(companion.Content, "#define SMILE_MAX_INPUT_BYTES 4096");
        StringAssert.Contains(companion.Content, "static int smile_valid_utf8");

        GeneratedProgram repeated = Generate(AcceptanceSource);
        CollectionAssert.AreEqual(
            generated.Files.Select(file => file.Content).ToArray(),
            repeated.Files.Select(file => file.Content).ToArray());
    }

    [TestMethod]
    public void COBOL_without_INPUT_has_no_companion_or_input_helpers()
    {
        GeneratedProgram generated = Generate("LET Name = \"Sin\"\nPRINT {Name}");

        Assert.IsFalse(generated.RequiresStandardInput);
        Assert.HasCount(1, generated.Files);
        Assert.IsFalse(generated.PrimaryFile.Content.Contains("smile_input_", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Installed_COBOL_INPUT_matches_the_normative_evaluator_run()
    {
        const string input = "  Sin  \n49\nTrUe\n";
        await AssertCobolMatchesEvaluatorAsync(AcceptanceSource, input);
    }

    [TestMethod]
    public async Task Installed_COBOL_preserves_String_bytes_line_forms_and_repeated_INPUT()
    {
        const string source = """
LET Value = "Before"
INPUT Value
PRINT [{Value}]
INPUT Value
PRINT [{Value}]
INPUT Value
PRINT [{Value}]
""";
        const string input = " \t雪\0尾 \r\n\rLast";

        await AssertCobolMatchesEvaluatorAsync(source, input);
    }

    [TestMethod]
    [TestCategory("HistoricalExactInput")]
    public async Task Installed_COBOL_enforces_the_exact_String_byte_limit()
    {
        const string source = """
LET Value = ""
INPUT Value
PRINT {Value}
""";

        await AssertCobolMatchesEvaluatorAsync(source, new string('a', 4096) + "\n");
        await AssertCobolMatchesEvaluatorAsync(source, new string('a', 4097) + "\n");
    }

    [TestMethod]
    public async Task Installed_COBOL_rejects_malformed_redirected_UTF8()
    {
        await RequireCobolAsync();
        const string source = """
LET Value = ""
INPUT Value
PRINT {Value}
""";
        byte[] invalidUtf8 = [0xC3, 0x28, 0x0A];
        EvaluationResult expected = _evaluator.Evaluate(source, new MemoryStream(invalidUtf8));
        Assert.AreEqual("SMILER1506", expected.RuntimeError?.Code);

        BuildRunResult built = await _toolchain.BuildAndRunAsync(
            Generate(source),
            CancellationToken.None,
            BuildRunOptions.Scripted("valid\n"));
        Assert.IsTrue(built.Success, Failure(built));
        Assert.IsNotNull(built.WorkingDirectory);
        string inputPath = Path.Combine(built.WorkingDirectory, "invalid-input.bin");
        await File.WriteAllBytesAsync(inputPath, invalidUtf8);

        var runner = new ProcessRunner();
        ProcessResult actual = await runner.RunAsync(
            ProcessCommand.ForCmd("run-cobol.cmd < invalid-input.bin", built.WorkingDirectory),
            TimeSpan.FromSeconds(30),
            CancellationToken.None);

        Assert.AreEqual(expected.ExitCode, actual.ExitCode);
        Assert.AreEqual(Normalize(expected.Output), Normalize(actual.StandardOutput));
        Assert.AreEqual(Normalize(expected.StandardError), Normalize(actual.StandardError));
    }

    [TestMethod]
    public async Task Installed_COBOL_distinguishes_INPUT_conversion_and_EOF_errors()
    {
        const string integerSource = """
LET Age = 0
PRINT Before
INPUT Age
PRINT After
""";
        const string booleanSource = """
LET Ready = FALSE
INPUT Ready
""";
        const string stringSource = """
LET Name = ""
INPUT Name
""";

        await AssertCobolMatchesEvaluatorAsync(integerSource, "hello\n");
        await AssertCobolMatchesEvaluatorAsync(integerSource, "9223372036854775808\n");
        await AssertCobolMatchesEvaluatorAsync(booleanSource, "YES\n");
        await AssertCobolMatchesEvaluatorAsync(stringSource, string.Empty);
    }

    [TestMethod]
    public async Task Installed_COBOL_consumes_INPUT_only_in_the_selected_IF_branch()
    {
        const string source = """
LET Choose = FALSE
LET First = ""
LET Second = ""
INPUT Choose
IF Choose = TRUE THEN
    INPUT First
ELSE
    SET First = "Skipped"
END IF
INPUT Second
PRINT {First}
PRINT {Second}
""";

        await AssertCobolMatchesEvaluatorAsync(source, "FALSE\nBeta\n");
        await AssertCobolMatchesEvaluatorAsync(source, "TRUE\nAlpha\nBeta\n");
    }

    [TestMethod]
    public async Task Installed_COBOL_uses_full_Int64_INPUT_and_checked_arithmetic()
    {
        const string source = """
LET Left = 0
LET Right = 0
INPUT Left
INPUT Right
LET Sum = Left + Right
LET Difference = Left - Right
LET Product = Left * Right
LET Quotient = Left / Right
LET Negative = -Left
PRINT {Sum}
PRINT {Difference}
PRINT {Product}
PRINT {Quotient}
PRINT {Negative}
""";

        GeneratedProgram generated = Generate(source);
        StringAssert.Contains(generated.PrimaryFile.Content, "PIC S9(18) COMP-5");
        StringAssert.Contains(generated.PrimaryFile.Content, "CALL \"smile_checked_add\" USING");
        StringAssert.Contains(generated.PrimaryFile.Content, "CALL \"smile_checked_divide\" USING");
        StringAssert.Contains(
            generated.Files.Single(file => file.RelativePath == "SmileRuntime.c").Content,
            "__builtin_mul_overflow");
        await AssertCobolMatchesEvaluatorAsync(source, "7\n-3\n");

        const string boundarySource = """
LET Value = 0
INPUT Value
LET Result = Value + 0
PRINT {Result}
""";
        await AssertCobolMatchesEvaluatorAsync(boundarySource, "9223372036854775807\n");
        await AssertCobolMatchesEvaluatorAsync(boundarySource, "-9223372036854775808\n");
    }

    [TestMethod]
    public async Task Installed_COBOL_reports_reached_arithmetic_errors_only()
    {
        const string overflow = """
LET Value = 0
INPUT Value
LET Result = Value + 1
PRINT {Result}
""";
        const string divide = """
LET Divisor = 1
INPUT Divisor
LET Result = -9223372036854775808 / Divisor
PRINT {Result}
""";
        const string conditional = """
LET Check = FALSE
INPUT Check
LET Result = Check = TRUE AND (1 / 0 = 0)
PRINT {Result}
""";

        await AssertCobolMatchesEvaluatorAsync(overflow, "9223372036854775807\n");
        await AssertCobolMatchesEvaluatorAsync(divide, "0\n");
        await AssertCobolMatchesEvaluatorAsync(divide, "-1\n");
        await AssertCobolMatchesEvaluatorAsync(conditional, "FALSE\n");
        await AssertCobolMatchesEvaluatorAsync(conditional, "TRUE\n");
    }

    [TestMethod]
    public async Task Installed_COBOL_checks_each_overflowing_Integer_operation()
    {
        const string subtract = """
LET Value = 0
INPUT Value
LET Result = Value - 1
PRINT {Result}
""";
        const string multiply = """
LET Value = 0
INPUT Value
LET Result = Value * 2
PRINT {Result}
""";
        const string negate = """
LET Value = 0
INPUT Value
LET Result = -Value
PRINT {Result}
""";

        await AssertCobolMatchesEvaluatorAsync(subtract, "-9223372036854775808\n");
        await AssertCobolMatchesEvaluatorAsync(multiply, "9223372036854775807\n");
        await AssertCobolMatchesEvaluatorAsync(negate, "-9223372036854775808\n");
    }

    private GeneratedProgram Generate(string source)
    {
        TranspileResult result = _transpiler.Transpile(source, TargetLanguage.Cobol);
        Assert.IsTrue(
            result.Success,
            string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        return result.GeneratedProgram!;
    }

    private async Task RequireCobolAsync()
    {
        ToolchainStatus status = await _toolchain.DetectAsync(CancellationToken.None);
        if (!status.IsAvailable)
        {
            Assert.Inconclusive(status.Message);
        }
    }

    private async Task AssertCobolMatchesEvaluatorAsync(string source, string input)
    {
        await RequireCobolAsync();
        EvaluationResult expected = _evaluator.Evaluate(source, input);
        Assert.HasCount(0, expected.Diagnostics);

        BuildRunResult actual = await _toolchain.BuildAndRunAsync(
            Generate(source),
            CancellationToken.None,
            BuildRunOptions.Scripted(input));

        Assert.AreEqual("Running", actual.Stage, Failure(actual));
        Assert.AreEqual(expected.ExitCode, actual.ExitCode, Failure(actual));
        Assert.AreEqual(expected.Success, actual.Success, Failure(actual));
        Assert.AreEqual(Normalize(expected.Output), Normalize(actual.StandardOutput));
        Assert.AreEqual(Normalize(expected.StandardError), Normalize(actual.StandardError));
        Assert.IsFalse(GeneratedTargetWarningDetector.ContainsCompilerWarning(
            TargetLanguage.Cobol,
            actual.BuildOutput), actual.BuildOutput);
    }

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string Failure(BuildRunResult result) =>
        result.BuildOutput + Environment.NewLine + result.StandardError;
}
