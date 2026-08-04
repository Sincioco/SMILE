using System.Text;
using SMILE.Engine;
using SMILE.Toolchains;

namespace SMILE.Tests;

[TestClass]
public sealed class ExactStringAndTargetSafeExpressionTests
{
    private readonly SmileTranspiler _transpiler = new();
    private readonly SmileEvaluator _evaluator = new();
    private readonly ToolchainRegistry _toolchains = ToolchainRegistry.CreateDefault();

    [TestMethod]
    [DataRow(TargetLanguage.C)]
    [DataRow(TargetLanguage.ObjectiveC)]
    public void C_family_uses_exact_length_output_only_for_NUL_sensitive_strings(
        TargetLanguage language)
    {
        string generated = Generate("""
LET Exact = "A\0B"
LET Ordinary = "Sin"

PRINT {Exact}
PRINT {Ordinary}
""", language).PrimaryFile.Content;

        StringAssert.Contains(
            generated,
            "static const unsigned char smilePrintBytes[] = { 65, 0, 66 };");
        StringAssert.Contains(generated, "fwrite(smilePrintBytes, 1, 3, stdout);");
        StringAssert.Contains(generated, "fputc('\\n', stdout);");
        StringAssert.Contains(generated, "printf(\"%s\\n\", Ordinary);");
    }

    [TestMethod]
    [DataRow(TargetLanguage.C)]
    [DataRow(TargetLanguage.ObjectiveC)]
    public void C_family_lowers_only_NUL_sensitive_equality(
        TargetLanguage language)
    {
        string generated = Generate("""
LET NulLeft = "A\0B"
LET NulRight = "A\0C"
LET Exact = NulLeft = NulRight

LET Left = "A"
LET Right = "B"
LET Ordinary = Left = Right
""", language).PrimaryFile.Content;

        StringAssert.Contains(generated, "bool Exact = false;");
        StringAssert.Contains(generated, "bool Ordinary = strcmp(Left, Right) == 0;");
    }

    [TestMethod]
    [DataRow(TargetLanguage.CSharp)]
    [DataRow(TargetLanguage.C)]
    [DataRow(TargetLanguage.MasmX64)]
    [DataRow(TargetLanguage.JavaScript)]
    [DataRow(TargetLanguage.Java)]
    [DataRow(TargetLanguage.Cobol)]
    [DataRow(TargetLanguage.ObjectiveC)]
    [DataRow(TargetLanguage.Swift)]
    [DataRow(TargetLanguage.Python)]
    [DataRow(TargetLanguage.Cpp)]
    public async Task Installed_target_preserves_complete_NUL_values_and_equality(
        TargetLanguage language)
    {
        const string source = """
LET Literal = "A\0B"
LET Original = "A\0B"
LET Copy = Original
LET Left = "A\0"
LET Concatenated = Left + "B"
LET Middle = "\0"
LET Interpolated = $"A{Middle}B"

LET A = "A\0B"
LET B = "A\0B"
LET C = "A\0C"
LET Same = A = B
LET Different = A <> C
LET NotSame = A = C

PRINT {Literal}
PRINT {Copy}
PRINT {Concatenated}
PRINT {Interpolated}
PRINT {Same}
PRINT {Different}
PRINT {NotSame}
PRINT {A = C}
""";

        await AssertInstalledTargetMatchesEvaluatorExactly(source, language);
    }

    [TestMethod]
    [DataRow(TargetLanguage.CSharp)]
    [DataRow(TargetLanguage.C)]
    [DataRow(TargetLanguage.MasmX64)]
    [DataRow(TargetLanguage.JavaScript)]
    [DataRow(TargetLanguage.Java)]
    [DataRow(TargetLanguage.Cobol)]
    [DataRow(TargetLanguage.ObjectiveC)]
    [DataRow(TargetLanguage.Swift)]
    [DataRow(TargetLanguage.Python)]
    [DataRow(TargetLanguage.Cpp)]
    public async Task Installed_target_short_circuits_known_values_in_every_expression_position(
        TargetLanguage language)
    {
        const string source = """
LET FalseFlag = FALSE
LET TrueFlag = TRUE

LET A = FalseFlag AND (1 / 0 = 0)
LET B = TrueFlag OR (1 / 0 = 0)

PRINT {A}
PRINT {B}
PRINT {FalseFlag AND (1 / 0 = 0)}
PRINT {TrueFlag OR (1 / 0 = 0)}
PRINT RawA={FalseFlag AND (1 / 0 = 0)}
PRINT RawB={TrueFlag OR (1 / 0 = 0)}

LET Message = $"A={FalseFlag AND (1 / 0 = 0)}, B={TrueFlag OR (1 / 0 = 0)}"
PRINT {Message}

LET Nested = TRUE OR (FalseFlag AND (1 / 0 = 0))
PRINT {Nested}
""";

        GeneratedProgram generated = Generate(source, language);
        Assert.IsFalse(
            generated.PrimaryFile.Content.Contains("1 / 0", StringComparison.Ordinal),
            generated.PrimaryFile.Content);

        await AssertInstalledTargetMatchesEvaluatorExactly(source, language);
    }

    [TestMethod]
    [DataRow("LET Flag = FALSE\nLET Result = Flag AND MissingName", "SMILE1106")]
    [DataRow("LET Flag = TRUE\nLET Result = Flag OR 42", "SMILE1204")]
    public void Binding_still_validates_unreachable_short_circuit_operands(
        string source,
        string expectedCode)
    {
        TranspileResult result = _transpiler.Transpile(source, TargetLanguage.C);

        Assert.IsFalse(result.Success);
        Assert.IsTrue(
            result.Diagnostics.Any(diagnostic => diagnostic.Code == expectedCode),
            string.Join(Environment.NewLine, result.Diagnostics));
    }

    private async Task AssertInstalledTargetMatchesEvaluatorExactly(
        string source,
        TargetLanguage language)
    {
        IToolchain toolchain = _toolchains.Get(language);
        ToolchainStatus status = await toolchain.DetectAsync(CancellationToken.None);
        if (!status.IsAvailable)
        {
            Assert.Inconclusive(status.Message);
        }

        EvaluationResult expected = _evaluator.Evaluate(source);
        Assert.IsTrue(expected.Success, string.Join(Environment.NewLine, expected.Diagnostics));

        BuildRunResult actual = await toolchain.BuildAndRunAsync(
            Generate(source, language),
            CancellationToken.None);

        Assert.IsTrue(actual.Success, actual.BuildOutput + Environment.NewLine + actual.StandardError);
        Assert.AreEqual(0, actual.ExitCode);

        byte[] expectedBytes = Encoding.UTF8.GetBytes(NormalizeLineEndings(expected.Output));
        byte[] actualBytes = Encoding.UTF8.GetBytes(NormalizeLineEndings(actual.StandardOutput));
        Assert.AreEqual(
            Convert.ToHexString(expectedBytes),
            Convert.ToHexString(actualBytes),
            $"Exact stdout differed for {language}.");
    }

    private GeneratedProgram Generate(string source, TargetLanguage language)
    {
        TranspileResult result = _transpiler.Transpile(source, language);

        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return result.GeneratedProgram!;
    }

    private static string NormalizeLineEndings(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal);
}
