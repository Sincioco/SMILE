using System.IO;
using System.Security.Cryptography;
using System.Text;
using SMILE.Engine;
using SMILE.Toolchains;

namespace SMILE.Tests;

[TestClass]
public sealed class TypedExpressionGeneratedCorpusTests
{
    private const string ExpectedCorpusSha256 = "0059D7D86089AA8DC78BA29B98EC1D4A3202E5437D298B827E874DF14921E247";
    private readonly SmileTranspiler _transpiler = new();
    private readonly SmileEvaluator _evaluator = new();
    private readonly ToolchainRegistry _toolchains = ToolchainRegistry.CreateDefault();

    [TestMethod]
    public void Fixed_seed_corpus_and_generated_targets_are_byte_for_byte_deterministic()
    {
        string first = TypedExpressionCorpus.Create();
        string second = TypedExpressionCorpus.Create();

        Assert.AreEqual(first, second);
        string sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(first)));
        Assert.AreEqual(ExpectedCorpusSha256, sha256, $"Actual corpus SHA-256: {sha256}");

        EvaluationResult firstEvaluation = _evaluator.Evaluate(first);
        EvaluationResult secondEvaluation = _evaluator.Evaluate(second);
        Assert.IsTrue(firstEvaluation.Success, string.Join(Environment.NewLine, firstEvaluation.Diagnostics));
        Assert.IsTrue(secondEvaluation.Success, string.Join(Environment.NewLine, secondEvaluation.Diagnostics));
        Assert.AreEqual(firstEvaluation.Output, secondEvaluation.Output);

        foreach (TargetLanguage language in TargetLanguageInfo.All)
        {
            GeneratedProgram firstProgram = Generate(first, language);
            GeneratedProgram secondProgram = Generate(second, language);
            CollectionAssert.AreEqual(
                firstProgram.Files.Select(file => file.Content).ToArray(),
                secondProgram.Files.Select(file => file.Content).ToArray());
        }
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
    public async Task Installed_target_matches_reference_evaluator_for_fixed_seed_corpus(
        TargetLanguage language)
    {
        IToolchain toolchain = _toolchains.Get(language);
        ToolchainStatus status = await toolchain.DetectAsync(CancellationToken.None);
        if (!status.IsAvailable)
        {
            Assert.Inconclusive(status.Message);
        }

        string source = TypedExpressionCorpus.Create();
        EvaluationResult evaluation = _evaluator.Evaluate(source);
        Assert.IsTrue(evaluation.Success, string.Join(Environment.NewLine, evaluation.Diagnostics));

        BuildRunResult result = await toolchain.BuildAndRunAsync(
            Generate(source, language),
            CancellationToken.None);

        Assert.IsTrue(result.Success, result.BuildOutput + Environment.NewLine + result.StandardError);
        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual(NormalizeLineEndings(evaluation.Output), NormalizeLineEndings(result.StandardOutput));
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
    public async Task Installed_target_matches_reference_evaluator_for_shipped_typed_expression_example(
        TargetLanguage language)
    {
        IToolchain toolchain = _toolchains.Get(language);
        ToolchainStatus status = await toolchain.DetectAsync(CancellationToken.None);
        if (!status.IsAvailable)
        {
            Assert.Inconclusive(status.Message);
        }

        string source = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "examples", "TypedExpressionCore.smile"));
        EvaluationResult evaluation = _evaluator.Evaluate(source);
        Assert.IsTrue(evaluation.Success, string.Join(Environment.NewLine, evaluation.Diagnostics));

        BuildRunResult result = await toolchain.BuildAndRunAsync(
            Generate(source, language),
            CancellationToken.None);

        Assert.IsTrue(result.Success, result.BuildOutput + Environment.NewLine + result.StandardError);
        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual(NormalizeLineEndings(evaluation.Output), NormalizeLineEndings(result.StandardOutput));
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
    public async Task Installed_target_preserves_official_escape_bytes(TargetLanguage language)
    {
        IToolchain toolchain = _toolchains.Get(language);
        ToolchainStatus status = await toolchain.DetectAsync(CancellationToken.None);
        if (!status.IsAvailable)
        {
            Assert.Inconclusive(status.Message);
        }

        const string source = """
PRINT "\\"
PRINT "\""
PRINT "A\nB"
PRINT "A\rB"
PRINT "A\tB"
PRINT "A\0B"
PRINT "A\bB"
PRINT "A\fB"
""";
        EvaluationResult evaluation = _evaluator.Evaluate(source);
        Assert.IsTrue(evaluation.Success, string.Join(Environment.NewLine, evaluation.Diagnostics));

        BuildRunResult result = await toolchain.BuildAndRunAsync(
            Generate(source, language),
            CancellationToken.None);

        Assert.IsTrue(result.Success, result.BuildOutput + Environment.NewLine + result.StandardError);
        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual(NormalizeLineEndings(evaluation.Output), NormalizeLineEndings(result.StandardOutput));
        StringAssert.Contains(result.StandardOutput, "\0", "Captured output did not preserve the embedded NUL byte.");
        StringAssert.Contains(result.StandardOutput, "\b", "Captured output did not preserve the embedded backspace byte.");
        StringAssert.Contains(result.StandardOutput, "\f", "Captured output did not preserve the embedded form-feed byte.");
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

internal static class TypedExpressionCorpus
{
    public const int Seed = 20260401;

    public static string Create()
    {
        var random = new Random(Seed);
        var source = new StringBuilder();
        int caseNumber = 1;

        void Add(string expression)
        {
            string name = $"Case{caseNumber:000}";
            source.Append("LET ").Append(name).Append(" = ").AppendLine(expression);
            source.Append("PRINT {").Append(name).AppendLine("}");
            caseNumber++;
        }

        foreach (string expression in new[]
        {
            "0",
            "+7",
            "-12",
            "2 + 3 * 4",
            "(2 + 3) * 4",
            "10 - (3 - 1)",
            "100 / (10 / 2)",
            "7 / 2",
            "-7 / 2",
            "7 / -2",
            "-7 / -2",
            "2 + 3 = 5",
            "2 + 3 <> 6",
            "4 < 5",
            "4 <= 4",
            "5 > 4",
            "5 >= 5",
            "TRUE",
            "FALSE",
            "NOT FALSE",
            "TRUE AND NOT FALSE",
            "FALSE OR TRUE",
            "TRUE OR FALSE AND FALSE",
            "TRUE = (FALSE = FALSE)",
            "\"Sin\"",
            "\"A\" + \"B\" + \"C\"",
            "\"Sin\" = \"Sin\"",
            "\"Sin\" <> \"sin\"",
            "$\"Age={40 + 9}, Adult={49 >= 18}\""
        })
        {
            Add(expression);
        }

        string[] comparisons = { "=", "<>", "<", "<=", ">", ">=" };
        for (int index = 0; index < 15; index++)
        {
            int a = random.Next(-20, 21);
            int b = random.Next(-20, 21);
            int c = random.Next(1, 10);
            int divisor = random.Next(1, 10);
            if (random.Next(2) == 0)
            {
                divisor = -divisor;
            }

            Add($"({a} + {b}) * {c}");
            Add($"({a} - {b}) / {divisor}");
            Add($"({a} + {c}) {comparisons[random.Next(comparisons.Length)]} ({b} - {c})");
            Add(index % 2 == 0
                ? $"NOT FALSE AND ({a} <= {b} OR TRUE)"
                : $"FALSE OR ({a} <> {b} AND TRUE)");
            Add($"\"S{random.Next(100):00}\" + \"-\" + \"T{random.Next(100):00}\"");
            Add($"$\"I={{{a} + {b}}}, B={{{a} < {b}}}\"");
        }

        return source.ToString();
    }
}
