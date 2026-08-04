using System.Reflection;
using SMILE.Engine;
using SMILE.Toolchains;

namespace SMILE.Tests;

[TestClass]
public sealed class TypedExpressionHardeningTests
{
    private readonly SmileTranspiler _transpiler = new();
    private readonly SmileEvaluator _evaluator = new();
    private readonly ToolchainRegistry _toolchains = ToolchainRegistry.CreateDefault();

    [TestMethod]
    [DataRow("LET Result = FALSE AND (1 / 0 = 0)\nPRINT {Result}", "FALSE\n")]
    [DataRow("LET Result = TRUE OR (1 / 0 = 0)\nPRINT {Result}", "TRUE\n")]
    [DataRow("LET Result = FALSE AND (9223372036854775807 + 1 = 0)\nPRINT {Result}", "FALSE\n")]
    [DataRow("LET Result = TRUE OR (9223372036854775807 + 1 = 0)\nPRINT {Result}", "TRUE\n")]
    public void Logical_operators_do_not_evaluate_unreachable_right_operands(
        string source,
        string expectedOutput)
    {
        EvaluationResult result = _evaluator.Evaluate(source);

        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.AreEqual(expectedOutput, result.Output);
        Assert.IsFalse(result.Diagnostics.Any(diagnostic => diagnostic.Code is "SMILE1206" or "SMILE1207"));
    }

    [TestMethod]
    public void Strict_destination_compilers_lower_short_circuited_initializers_to_constants()
    {
        const string source = """
LET A = FALSE AND (1 / 0 = 0)
LET B = TRUE OR (1 / 0 = 0)

PRINT {A}
PRINT {B}
""";

        string csharp = Generate(source, TargetLanguage.CSharp).PrimaryFile.Content;
        StringAssert.Contains(csharp, "bool A = false;");
        StringAssert.Contains(csharp, "bool B = true;");

        string java = Generate(source, TargetLanguage.Java).PrimaryFile.Content;
        StringAssert.Contains(java, "boolean A = false;");
        StringAssert.Contains(java, "boolean B = true;");

        string swift = Generate(source, TargetLanguage.Swift).PrimaryFile.Content;
        StringAssert.Contains(swift, "let A: Bool = false");
        StringAssert.Contains(swift, "let B: Bool = true");
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
    public async Task Installed_target_runs_short_circuit_division_program(TargetLanguage language)
    {
        const string source = """
LET A = FALSE AND (1 / 0 = 0)
LET B = TRUE OR (1 / 0 = 0)

PRINT {A}
PRINT {B}
""";
        IToolchain toolchain = _toolchains.Get(language);
        ToolchainStatus status = await toolchain.DetectAsync(CancellationToken.None);
        if (!status.IsAvailable)
        {
            Assert.Inconclusive(status.Message);
        }

        BuildRunResult result = await toolchain.BuildAndRunAsync(
            Generate(source, language),
            CancellationToken.None);

        Assert.IsTrue(result.Success, result.BuildOutput + Environment.NewLine + result.StandardError);
        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual("FALSE\nTRUE\n", NormalizeLineEndings(result.StandardOutput));
    }

    [TestMethod]
    [DataRow(TargetLanguage.C)]
    [DataRow(TargetLanguage.ObjectiveC)]
    public void C_family_string_equality_lowers_complex_operands_only(TargetLanguage language)
    {
        const string source = """
LET Suffix = "B"
LET A = ("A" + "B") = "AB"
LET B = $"A{Suffix}" = "AB"
LET C = ("A" + Suffix) <> "AC"
LET D = Suffix = "B"

PRINT {A}
PRINT {B}
PRINT {C}
PRINT {D}
""";

        string generated = Generate(source, language).PrimaryFile.Content;
        StringAssert.Contains(generated, "bool A = strcmp(\"AB\", \"AB\") == 0;");
        StringAssert.Contains(generated, "bool B = strcmp(\"AB\", \"AB\") == 0;");
        StringAssert.Contains(generated, "bool C = strcmp(\"AB\", \"AC\") != 0;");
        StringAssert.Contains(generated, "bool D = strcmp(Suffix, \"B\") == 0;");
    }

    [TestMethod]
    [DataRow(TargetLanguage.C)]
    [DataRow(TargetLanguage.ObjectiveC)]
    public async Task Installed_C_family_target_runs_complex_string_equality(TargetLanguage language)
    {
        const string source = """
LET Suffix = "B"
LET A = ("A" + "B") = "AB"
LET B = $"A{Suffix}" = "AB"
LET C = ("A" + Suffix) <> "AC"

PRINT {A}
PRINT {B}
PRINT {C}
""";
        IToolchain toolchain = _toolchains.Get(language);
        ToolchainStatus status = await toolchain.DetectAsync(CancellationToken.None);
        if (!status.IsAvailable)
        {
            Assert.Inconclusive(status.Message);
        }

        EvaluationResult evaluation = _evaluator.Evaluate(source);
        Assert.IsTrue(evaluation.Success, string.Join(Environment.NewLine, evaluation.Diagnostics));
        BuildRunResult result = await toolchain.BuildAndRunAsync(
            Generate(source, language),
            CancellationToken.None);

        Assert.IsTrue(result.Success, result.BuildOutput + Environment.NewLine + result.StandardError);
        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual(
            NormalizeLineEndings(evaluation.Output),
            NormalizeLineEndings(result.StandardOutput));
    }

    [TestMethod]
    [DataRow("LET Result = TRUE AND (1 / 0 = 0)", "SMILE1207")]
    [DataRow("LET Result = FALSE OR (9223372036854775807 + 1 = 0)", "SMILE1206")]
    public void Logical_operators_evaluate_reachable_right_operands(string source, string expectedCode)
    {
        AssertDiagnostic(source, expectedCode);
    }

    [TestMethod]
    [DataRow("LET Result = FALSE AND MissingName", "SMILE1106")]
    [DataRow("LET Result = TRUE OR 42", "SMILE1204")]
    public void Logical_operators_bind_and_type_check_unreachable_right_operands(
        string source,
        string expectedCode)
    {
        AssertDiagnostic(source, expectedCode);
    }

    [TestMethod]
    public void String_concatenation_has_one_syntax_and_bound_representation()
    {
        Assembly engine = typeof(SmileProgramSyntax).Assembly;
        Assert.IsNull(engine.GetType("SMILE.Engine.ConcatenationExpressionSyntax"));
        Assert.IsNull(engine.GetType("SMILE.Engine.BoundConcatenationExpression"));

        BindResult result = _transpiler.Bind("LET FullName = \"Sin\" + \" Cioco\"");

        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        var let = (BoundLetStatement)result.Program!.Statements.Single();
        var binary = (BoundBinaryExpression)let.Initializer;
        Assert.AreEqual(BoundBinaryOperatorKind.StringConcatenation, binary.Operator.Kind);
    }

    [TestMethod]
    public void Signed_integer_boundaries_division_and_associativity_are_exact()
    {
        EvaluationResult result = _evaluator.Evaluate("""
LET Min = -9223372036854775808
LET Max = 9223372036854775807
LET A = 7 / 2
LET B = -7 / 2
LET C = 7 / -2
LET D = -7 / -2
LET E = 10 - 3 - 1
LET F = 10 - (3 - 1)
LET G = 100 / 10 / 2
LET H = 100 / (10 / 2)

PRINT {Min}
PRINT {Max}
PRINT {A}
PRINT {B}
PRINT {C}
PRINT {D}
PRINT {E}
PRINT {F}
PRINT {G}
PRINT {H}
""");

        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.AreEqual(
            "-9223372036854775808\n9223372036854775807\n3\n-3\n-3\n3\n6\n8\n5\n20\n",
            result.Output);
    }

    [TestMethod]
    [DataRow("LET Invalid = -9223372036854775808 / -1")]
    [DataRow("LET Invalid = -(-9223372036854775808)")]
    [DataRow("LET Invalid = 9223372036854775807 + 1")]
    [DataRow("LET Invalid = -9223372036854775808 - 1")]
    [DataRow("LET Invalid = 3037000500 * 3037000500")]
    public void Signed_integer_overflow_cases_report_SMILE1206(string source)
    {
        AssertDiagnostic(source, "SMILE1206");
    }

    [TestMethod]
    public void String_equality_is_ordinal_and_handles_variables_and_interpolation()
    {
        EvaluationResult result = _evaluator.Evaluate("""
LET A = "Sin" = "Sin"
LET B = "Sin" = "sin"
LET C = "Sin" <> "sin"
LET D = "" = ""
LET E = "A\nB" = "A\nB"
LET Name = "Sin"
LET Copy = Name
LET Greeting = $"Hello {Name}"
LET SameName = Name = Copy
LET SameGreeting = Greeting = "Hello Sin"

PRINT {A}
PRINT {B}
PRINT {C}
PRINT {D}
PRINT {E}
PRINT {SameName}
PRINT {SameGreeting}
""");

        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.AreEqual("TRUE\nFALSE\nTRUE\nTRUE\nTRUE\nTRUE\nTRUE\n", result.Output);
    }

    [TestMethod]
    public void Official_string_escapes_bind_to_exact_values()
    {
        BindResult result = _transpiler.Bind("""
LET Backslash = "\\"
LET Quote = "\""
LET Newline = "A\nB"
LET CarriageReturn = "A\rB"
LET Tab = "A\tB"
LET Nul = "A\0B"
LET Backspace = "A\bB"
LET FormFeed = "A\fB"
""");

        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        string[] values = result.Program!.Statements
            .OfType<BoundLetStatement>()
            .Select(let => let.ConstantValue.StringValue)
            .ToArray();
        CollectionAssert.AreEqual(
            new[] { "\\", "\"", "A\nB", "A\rB", "A\tB", "A\0B", "A\bB", "A\fB" },
            values);
    }

    [TestMethod]
    [DataRow("LET Invalid = \"\\q\"", "SMILE1208")]
    [DataRow("LET Invalid = \"\\x\"", "SMILE1208")]
    [DataRow("LET Invalid = \"Ends with \\", "SMILE1209")]
    public void Invalid_string_escapes_keep_dedicated_diagnostics(string source, string expectedCode)
    {
        AssertDiagnostic(source, expectedCode);
    }

    [TestMethod]
    public void Raw_print_templates_keep_backslashes_literal()
    {
        EvaluationResult result = _evaluator.Evaluate("PRINT C:\\SMILE\\n");

        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.AreEqual("C:\\SMILE\\n\n", result.Output);
    }

    [TestMethod]
    [DataRow(TargetLanguage.C)]
    [DataRow(TargetLanguage.ObjectiveC)]
    public void C_family_targets_preserve_typed_expressions_and_safe_printf(TargetLanguage language)
    {
        const string source = """
LET Name = "Sin"
LET Copy = "Sin"
LET Age = 49
LET Adult = Age >= 18
LET WorkingAge = Adult AND NOT FALSE
LET SameName = Name = Copy

PRINT {Age}
PRINT {Adult}
PRINT Result: {Age + 1}
PRINT Progress: 100%, Same={SameName}
""";

        string generated = Generate(source, language).PrimaryFile.Content;

        StringAssert.Contains(generated, "#include <string.h>");
        StringAssert.Contains(generated, "int Age = 49;");
        StringAssert.Contains(generated, "bool Adult = Age >= 18;");
        StringAssert.Contains(generated, "bool WorkingAge = Adult;");
        StringAssert.Contains(generated, "bool SameName = strcmp(Name, Copy) == 0;");
        StringAssert.Contains(generated, "printf(\"%d\\n\", Age);");
        StringAssert.Contains(generated, "printf(\"%s\\n\", Adult ? \"TRUE\" : \"FALSE\");");
        StringAssert.Contains(generated, "printf(\"Result: %d\\n\", Age + 1);");
        StringAssert.Contains(generated, "printf(\"Progress: 100%%, Same=%s\\n\", SameName ? \"TRUE\" : \"FALSE\");");
    }

    [TestMethod]
    public void Java_and_C_family_targets_use_value_based_string_equality()
    {
        const string source = """
LET Left = "Sin"
LET Right = "Sin"
LET Same = Left = Right
LET Different = Left <> "sin"
""";

        string java = Generate(source, TargetLanguage.Java).PrimaryFile.Content;
        StringAssert.Contains(java, "boolean Same = Left.equals(Right);");
        StringAssert.Contains(java, "boolean Different = !Left.equals(\"sin\");");

        foreach (TargetLanguage language in new[] { TargetLanguage.C, TargetLanguage.ObjectiveC })
        {
            string generated = Generate(source, language).PrimaryFile.Content;
            StringAssert.Contains(generated, "bool Same = strcmp(Left, Right) == 0;");
            StringAssert.Contains(generated, "bool Different = strcmp(Left, \"sin\") != 0;");
        }
    }

    [TestMethod]
    [DataRow(TargetLanguage.C)]
    [DataRow(TargetLanguage.ObjectiveC)]
    public void C_family_targets_include_string_header_only_for_emitted_comparisons(TargetLanguage language)
    {
        string withoutComparison = Generate(
            "LET Name = \"Sin\"\nPRINT {Name}",
            language).PrimaryFile.Content;
        Assert.IsFalse(withoutComparison.Contains("#include <string.h>", StringComparison.Ordinal));

        string loweredStringLet = Generate(
            "LET Left = \"Sin\"\nLET Right = \"Sin\"\nLET Message = $\"{Left = Right}\"\nPRINT {Message}",
            language).PrimaryFile.Content;
        Assert.IsFalse(loweredStringLet.Contains("#include <string.h>", StringComparison.Ordinal));

        string withComparison = Generate(
            "LET Same = \"Sin\" = \"Sin\"\nPRINT {Same}",
            language).PrimaryFile.Content;
        StringAssert.Contains(withComparison, "#include <string.h>");
    }

    [TestMethod]
    public void Cobol_maps_typed_acceptance_identifiers_that_are_reserved_words()
    {
        string generated = Generate("""
LET Quote = "Text"
LET Negative = -12

PRINT {Quote}
PRINT {Negative}
""", TargetLanguage.Cobol).PrimaryFile.Content;

        StringAssert.Contains(generated, "01 SMILE-Quote PIC X(4) VALUE \"Text\".");
        StringAssert.Contains(generated, "01 SMILE-Negative PIC X(3) VALUE \"-12\".");
    }

    [TestMethod]
    public void High_level_targets_preserve_precedence_and_nested_right_operands()
    {
        const string source = """
LET A = 2 + 3 * 4
LET B = (2 + 3) * 4
LET C = 10 - (3 - 1)
LET D = 100 / (10 / 2)
LET E = NOT TRUE OR TRUE
LET F = TRUE OR FALSE AND FALSE
LET G = 1 = (2 = 2)
""";

        BindResult invalid = _transpiler.Bind(source);
        Assert.IsFalse(invalid.Success, "Mixed Integer/Boolean equality must remain a type error.");

        const string validSource = """
LET A = 2 + 3 * 4
LET B = (2 + 3) * 4
LET C = 10 - (3 - 1)
LET D = 100 / (10 / 2)
LET E = NOT TRUE OR TRUE
LET F = TRUE OR FALSE AND FALSE
LET G = TRUE = (FALSE = FALSE)
LET H = 1 < (2 + 1)
""";

        foreach (TargetLanguage language in new[]
        {
            TargetLanguage.CSharp,
            TargetLanguage.C,
            TargetLanguage.JavaScript,
            TargetLanguage.Java,
            TargetLanguage.ObjectiveC,
            TargetLanguage.Swift,
            TargetLanguage.Python,
            TargetLanguage.Cpp
        })
        {
            string generated = Generate(validSource, language).PrimaryFile.Content;
            (string subtraction, string division, string equality) = language switch
            {
                TargetLanguage.CSharp =>
                    ("int C = 10 - (3 - 1);", "int D = 100 / (10 / 2);", "bool G = true == (false == false);"),
                TargetLanguage.JavaScript =>
                    ("let C = 10 - (3 - 1);", "let D = Math.trunc(100 / Math.trunc(10 / 2));", "let G = true === (false === false);"),
                TargetLanguage.Java =>
                    ("int C = 10 - (3 - 1);", "int D = 100 / (10 / 2);", "boolean G = true == (false == false);"),
                TargetLanguage.Swift =>
                    ("let C: Int = 10 - (3 - 1)", "let D: Int = 100 / (10 / 2)", "let G: Bool = true == (false == false)"),
                TargetLanguage.Python =>
                    ("C = 10 - (3 - 1)", "D = _smile_div(100, _smile_div(10, 2))", "G = True == (False == False)"),
                _ =>
                    ("int C = 10 - (3 - 1);", "int D = 100 / (10 / 2);", "bool G = true == (false == false);")
            };

            StringAssert.Contains(generated, subtraction);
            StringAssert.Contains(generated, division);
            StringAssert.Contains(generated, equality);
            Assert.IsFalse(generated.Contains("10 - 3 - 1", StringComparison.Ordinal), generated);
        }

        EvaluationResult evaluation = _evaluator.Evaluate(validSource + "\nPRINT {A}\nPRINT {B}\nPRINT {C}\nPRINT {D}\nPRINT {E}\nPRINT {F}\nPRINT {G}\nPRINT {H}");
        Assert.IsTrue(evaluation.Success, string.Join(Environment.NewLine, evaluation.Diagnostics));
        Assert.AreEqual("14\n20\n8\n20\nTRUE\nTRUE\nTRUE\nTRUE\n", evaluation.Output);
    }

    [TestMethod]
    public void JavaScript_omits_unsupported_unary_plus_for_BigInt()
    {
        string generated = Generate(
            "LET Wide = 9007199254740992\nLET Positive = +7\nPRINT {Positive}",
            TargetLanguage.JavaScript)
            .PrimaryFile
            .Content;

        StringAssert.Contains(generated, "let Positive = 7n;");
        Assert.IsFalse(generated.Contains("+7n", StringComparison.Ordinal), generated);
    }

    private GeneratedProgram Generate(string source, TargetLanguage language)
    {
        TranspileResult result = _transpiler.Transpile(source, language);

        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return result.GeneratedProgram!;
    }

    private void AssertDiagnostic(string source, string expectedCode)
    {
        BindResult result = _transpiler.Bind(source);

        Assert.IsFalse(result.Success);
        Assert.IsTrue(
            result.Diagnostics.Any(diagnostic => diagnostic.Code == expectedCode),
            string.Join(Environment.NewLine, result.Diagnostics));
    }

    private static string NormalizeLineEndings(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal);
}
