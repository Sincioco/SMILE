using SMILE.Engine;
using SMILE.Toolchains;

namespace SMILE.Tests;

[TestClass]
public sealed class TargetIntegerGenerationTests
{
    private readonly SmileTranspiler _transpiler = new();
    private readonly SmileEvaluator _evaluator = new();
    private readonly ToolchainRegistry _toolchains = ToolchainRegistry.CreateDefault();

    [TestMethod]
    [DataRow(TargetLanguage.C)]
    [DataRow(TargetLanguage.ObjectiveC)]
    public void C_family_small_program_matches_the_idiomatic_acceptance_example(
        TargetLanguage language)
    {
        string generated = Generate("""
LET Age = 49
LET Adult = Age >= 18
LET WorkingAge = Adult AND NOT FALSE
""", language).PrimaryFile.Content;

        StringAssert.Contains(generated, "int Age = 49;");
        StringAssert.Contains(generated, "bool Adult = Age >= 18;");
        StringAssert.Contains(generated, "bool WorkingAge = Adult;");
        Assert.IsFalse(generated.Contains("#include <stdint.h>", StringComparison.Ordinal));
        Assert.IsFalse(generated.Contains("INT64_C", StringComparison.Ordinal));
        Assert.IsFalse(generated.Contains("LL", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Small_programs_use_each_targets_natural_integer_representation()
    {
        const string source = """
LET Age = 49
LET Count = 2 + 3 * 4
LET Quotient = -7 / 2

PRINT {Age}
PRINT {Count}
PRINT {Quotient}
""";

        string csharp = Generate(source, TargetLanguage.CSharp).PrimaryFile.Content;
        StringAssert.Contains(csharp, "int Age = 49;");
        StringAssert.Contains(csharp, "int Count = 2 + 3 * 4;");
        Assert.IsFalse(csharp.Contains("49L", StringComparison.Ordinal));

        string java = Generate(source, TargetLanguage.Java).PrimaryFile.Content;
        StringAssert.Contains(java, "int Age = 49;");
        StringAssert.Contains(java, "int Count = 2 + 3 * 4;");
        StringAssert.Contains(java, "Integer.toString(Age)");
        Assert.IsFalse(java.Contains("49L", StringComparison.Ordinal));

        string javascript = Generate(source, TargetLanguage.JavaScript).PrimaryFile.Content;
        StringAssert.Contains(javascript, "let Age = 49;");
        StringAssert.Contains(javascript, "let Count = 2 + 3 * 4;");
        StringAssert.Contains(javascript, "let Quotient = Math.trunc(-7 / 2);");
        Assert.IsFalse(javascript.Contains("49n", StringComparison.Ordinal));

        string swift = Generate(source, TargetLanguage.Swift).PrimaryFile.Content;
        StringAssert.Contains(swift, "let Age: Int = 49");
        StringAssert.Contains(swift, "let Count: Int = 2 + 3 * 4");
        Assert.IsFalse(swift.Contains("Int64", StringComparison.Ordinal));

        foreach (TargetLanguage language in new[] { TargetLanguage.C, TargetLanguage.ObjectiveC })
        {
            string generated = Generate(source, language).PrimaryFile.Content;
            StringAssert.Contains(generated, "int Age = 49;");
            StringAssert.Contains(generated, "int Count = 2 + 3 * 4;");
            StringAssert.Contains(generated, "printf(\"%d\\n\", Age);");
        }

        string python = Generate(source, TargetLanguage.Python).PrimaryFile.Content;
        StringAssert.Contains(python, "Age = 49");
        StringAssert.Contains(python, "Count = 2 + 3 * 4");
        Assert.IsFalse(python.Contains("Int64", StringComparison.Ordinal));

        string cpp = Generate(source, TargetLanguage.Cpp).PrimaryFile.Content;
        StringAssert.Contains(cpp, "int Age = 49;");
        StringAssert.Contains(cpp, "int Count = 2 + 3 * 4;");
        Assert.IsFalse(cpp.Contains("std::int64_t", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Signed_32_boundary_and_intermediate_promote_static_targets_only()
    {
        const string source = """
LET Small = 49
LET Boundary = 2147483648
LET Product = 50000 * 50000
""";

        foreach (TargetLanguage language in new[] { TargetLanguage.C, TargetLanguage.ObjectiveC })
        {
            string generated = Generate(source, language).PrimaryFile.Content;
            StringAssert.Contains(generated, "#include <stdint.h>");
            StringAssert.Contains(generated, "int64_t Small = INT64_C(49);");
            StringAssert.Contains(generated, "int64_t Boundary = INT64_C(2147483648);");
            StringAssert.Contains(generated, "int64_t Product = INT64_C(50000) * INT64_C(50000);");
        }

        string csharp = Generate(source, TargetLanguage.CSharp).PrimaryFile.Content;
        StringAssert.Contains(csharp, "long Small = 49L;");
        StringAssert.Contains(csharp, "long Boundary = 2147483648L;");
        StringAssert.Contains(csharp, "long Product = 50000L * 50000L;");

        string java = Generate(source, TargetLanguage.Java).PrimaryFile.Content;
        StringAssert.Contains(java, "long Small = 49L;");
        StringAssert.Contains(java, "long Boundary = 2147483648L;");
        StringAssert.Contains(java, "long Product = 50000L * 50000L;");

        string swift = Generate(source, TargetLanguage.Swift).PrimaryFile.Content;
        StringAssert.Contains(swift, "let Small: Int64 = 49");
        StringAssert.Contains(swift, "let Boundary: Int64 = 2147483648");

        string javascript = Generate(source, TargetLanguage.JavaScript).PrimaryFile.Content;
        StringAssert.Contains(javascript, "let Boundary = 2147483648;");
        StringAssert.Contains(javascript, "let Product = 50000 * 50000;");
        Assert.AreEqual(-1, javascript.IndexOf("2147483648n", StringComparison.Ordinal));
        Assert.AreEqual(-1, javascript.IndexOf("50000n", StringComparison.Ordinal));

        string cpp = Generate(source, TargetLanguage.Cpp).PrimaryFile.Content;
        StringAssert.Contains(cpp, "#include <cstdint>");
        StringAssert.Contains(cpp, "std::int64_t Boundary = INT64_C(2147483648);");
        StringAssert.Contains(cpp, "std::int64_t Product = INT64_C(50000) * INT64_C(50000);");
    }

    [TestMethod]
    public void JavaScript_promotes_the_complete_program_when_an_intermediate_exceeds_its_safe_range()
    {
        string generated = Generate("""
LET Small = 49
LET Huge = 3100000000 * 3000000
LET Quotient = Huge / 3
""", TargetLanguage.JavaScript).PrimaryFile.Content;

        StringAssert.Contains(generated, "let Small = 49n;");
        StringAssert.Contains(generated, "let Huge = 3100000000n * 3000000n;");
        StringAssert.Contains(generated, "let Quotient = Huge / 3n;");
        Assert.IsFalse(generated.Contains("Math.trunc", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Exact_signed_64_boundaries_use_wide_target_literals()
    {
        const string source = """
LET Min = -9223372036854775808
LET Max = 9223372036854775807
""";

        string c = Generate(source, TargetLanguage.C).PrimaryFile.Content;
        StringAssert.Contains(c, "int64_t Min = INT64_MIN;");
        StringAssert.Contains(c, "int64_t Max = INT64_C(9223372036854775807);");

        string csharp = Generate(source, TargetLanguage.CSharp).PrimaryFile.Content;
        StringAssert.Contains(csharp, "long Min = long.MinValue;");
        StringAssert.Contains(csharp, "long Max = 9223372036854775807L;");

        string java = Generate(source, TargetLanguage.Java).PrimaryFile.Content;
        StringAssert.Contains(java, "long Min = Long.MIN_VALUE;");
        StringAssert.Contains(java, "long Max = 9223372036854775807L;");

        string javascript = Generate(source, TargetLanguage.JavaScript).PrimaryFile.Content;
        StringAssert.Contains(javascript, "let Min = (-9223372036854775808n);");
        StringAssert.Contains(javascript, "let Max = 9223372036854775807n;");

        string swift = Generate(source, TargetLanguage.Swift).PrimaryFile.Content;
        StringAssert.Contains(swift, "let Min: Int64 = Int64.min");
        StringAssert.Contains(swift, "let Max: Int64 = 9223372036854775807");

        string python = Generate(source, TargetLanguage.Python).PrimaryFile.Content;
        StringAssert.Contains(python, "Min = -9223372036854775808");
        StringAssert.Contains(python, "Max = 9223372036854775807");

        string cpp = Generate(source, TargetLanguage.Cpp).PrimaryFile.Content;
        StringAssert.Contains(cpp, "std::int64_t Min = INT64_MIN;");
        StringAssert.Contains(cpp, "std::int64_t Max = INT64_C(9223372036854775807);");
    }

    [TestMethod]
    public void Pure_boolean_identities_are_simplified_once_for_every_target()
    {
        const string source = """
LET X = 1 = 1
LET NotFalse = NOT FALSE
LET NotTrue = NOT TRUE
LET XAndTrue = X AND TRUE
LET TrueAndX = TRUE AND X
LET XAndFalse = X AND FALSE
LET FalseAndX = FALSE AND X
LET XOrFalse = X OR FALSE
LET FalseOrX = FALSE OR X
LET XOrTrue = X OR TRUE
LET TrueOrX = TRUE OR X
""";

        string c = Generate(source, TargetLanguage.C).PrimaryFile.Content;
        StringAssert.Contains(c, "bool NotFalse = true;");
        StringAssert.Contains(c, "bool NotTrue = false;");
        StringAssert.Contains(c, "bool XAndTrue = X;");
        StringAssert.Contains(c, "bool TrueAndX = X;");
        StringAssert.Contains(c, "bool XAndFalse = false;");
        StringAssert.Contains(c, "bool FalseAndX = false;");
        StringAssert.Contains(c, "bool XOrFalse = X;");
        StringAssert.Contains(c, "bool FalseOrX = X;");
        StringAssert.Contains(c, "bool XOrTrue = true;");
        StringAssert.Contains(c, "bool TrueOrX = true;");

        foreach (TargetLanguage language in TargetLanguageInfo.All)
        {
            string first = Generate(source, language).PrimaryFile.Content;
            string second = Generate(source, language).PrimaryFile.Content;
            Assert.AreEqual(first, second, language.ToString());
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
    [DataRow(TargetLanguage.Python)]
    [DataRow(TargetLanguage.Cpp)]
    public async Task Installed_target_matches_evaluator_for_wide_integer_profile(
        TargetLanguage language)
    {
        const string source = """
LET Small = 49
LET Boundary = 2147483648
LET Product = 50000 * 50000
LET Huge = 3100000000 * 3000000
LET Min = -9223372036854775808
LET Max = 9223372036854775807
LET Quotient = -9223372036854775807 / 3

PRINT {Small}
PRINT {Boundary}
PRINT {Product}
PRINT {Huge}
PRINT {Min}
PRINT {Max}
PRINT {Quotient}
""";

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
        Assert.AreEqual(
            NormalizeLineEndings(expected.Output),
            NormalizeLineEndings(actual.StandardOutput));
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
