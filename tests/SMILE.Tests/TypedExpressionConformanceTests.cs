using SMILE.Engine;

namespace SMILE.Tests;

[TestClass]
public sealed class TypedExpressionConformanceTests
{
    private readonly SmileTranspiler _transpiler = new();
    private readonly SmileEvaluator _evaluator = new();

    [TestMethod]
    public void Evaluator_honors_integer_boolean_precedence_and_display_rules()
    {
        EvaluationResult result = _evaluator.Evaluate("""
LET A = 2 + 3 * 4
LET B = (2 + 3) * 4
LET C = -7 / 2
LET D = NOT FALSE AND TRUE OR FALSE
LET E = A = 14
LET F = A <> B

PRINT {A}
PRINT {B}
PRINT {C}
PRINT {D}
PRINT {E}
PRINT {F}
PRINT Calculation: {A}, {B}, {C}
""");

        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.AreEqual(
            "14\n20\n-3\nTRUE\nTRUE\nTRUE\nCalculation: 14, 20, -3\n",
            result.Output);
    }

    [TestMethod]
    public void Parser_keeps_multiplication_under_addition_in_the_expression_tree()
    {
        BindResult result = _transpiler.Bind("LET Result = 2 + 3 * 4");

        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        var let = (BoundLetStatement)result.Program!.Statements.Single();
        var addition = (BoundBinaryExpression)let.Initializer;
        var multiplication = (BoundBinaryExpression)addition.Right;

        Assert.AreEqual(BoundBinaryOperatorKind.Addition, addition.Operator.Kind);
        Assert.AreEqual(BoundBinaryOperatorKind.Multiplication, multiplication.Operator.Kind);
        BoundProgramExecutionTrace trace = BoundProgramExecutionTrace.Create(result.Program!);
        Assert.AreEqual(14L, trace.Steps.Single().Value.IntegerValue);
    }

    [TestMethod]
    [DataRow("LET Bad = \"Age \" + 49", "SMILE1204")]
    [DataRow("LET Bad = 1 AND TRUE", "SMILE1204")]
    [DataRow("LET Bad = NOT 1", "SMILE1203")]
    [DataRow("LET Bad = \"A\" < \"B\"", "SMILE1204")]
    [DataRow("LET Bad = TRUE = 1", "SMILE1204")]
    public void Binder_rejects_type_mismatched_operators(string source, string expectedCode)
    {
        BindResult result = _transpiler.Bind(source);

        Assert.IsFalse(result.Success);
        Assert.IsTrue(
            result.Diagnostics.Any(diagnostic => diagnostic.Code == expectedCode),
            string.Join(Environment.NewLine, result.Diagnostics));
    }

    [TestMethod]
    public void Evaluator_accepts_signed_int64_boundaries()
    {
        EvaluationResult result = _evaluator.Evaluate("""
LET Min = -9223372036854775808
LET Max = 9223372036854775807
LET NegativeOne = -1

PRINT {Min}
PRINT {Max}
PRINT {NegativeOne}
""");

        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.AreEqual("-9223372036854775808\n9223372036854775807\n-1\n", result.Output);
    }

    [TestMethod]
    [DataRow("LET TooLarge = 9223372036854775808", "SMILE1202")]
    [DataRow("LET TooSmall = -9223372036854775809", "SMILE1202")]
    [DataRow("LET Overflow = 9223372036854775807 + 1", "SMILE1206")]
    [DataRow("LET Underflow = -9223372036854775808 - 1", "SMILE1206")]
    [DataRow("LET Divide = 1 / 0", "SMILE1207")]
    public void Binder_reports_integer_range_overflow_and_division_errors(string source, string expectedCode)
    {
        BindResult result = _transpiler.Bind(source);

        Assert.IsFalse(result.Success);
        Assert.IsTrue(
            result.Diagnostics.Any(diagnostic => diagnostic.Code == expectedCode),
            string.Join(Environment.NewLine, result.Diagnostics));
    }

    [TestMethod]
    public void High_level_generators_emit_typed_expression_code()
    {
        const string source = """
LET Age = 49
LET Adult = Age >= 18
LET Enabled = TRUE
LET Count = 2 + 3 * 4
LET Negative = -12
LET Name = "Sin"
LET Message = $"{Name}: {Age}, {Adult}"

PRINT {Age}
PRINT {Adult}
PRINT {Enabled}
PRINT {Count}
PRINT {Negative}
PRINT {-12}
PRINT {Message}
""";

        string csharp = Generate(source, TargetLanguage.CSharp).PrimaryFile.Content;
        StringAssert.Contains(csharp, "using System.Globalization;");
        StringAssert.Contains(csharp, "int Age = 49;");
        StringAssert.Contains(csharp, "bool Adult = Age >= 18;");
        StringAssert.Contains(csharp, "string Message = $\"{Name}: {Age.ToString(CultureInfo.InvariantCulture)}, {(Adult ? \"TRUE\" : \"FALSE\")}\";");
        StringAssert.Contains(csharp, "Console.WriteLine(Age.ToString(CultureInfo.InvariantCulture));");
        StringAssert.Contains(csharp, "Console.WriteLine((Adult ? \"TRUE\" : \"FALSE\"));");
        StringAssert.Contains(csharp, "Console.WriteLine(Negative.ToString(CultureInfo.InvariantCulture));");
        StringAssert.Contains(csharp, "Console.WriteLine((-12).ToString(CultureInfo.InvariantCulture));");

        string javascript = Generate(source, TargetLanguage.JavaScript).PrimaryFile.Content;
        StringAssert.Contains(javascript, "let Age = 49;");
        StringAssert.Contains(javascript, "let Count = 2 + 3 * 4;");
        StringAssert.Contains(javascript, "console.log((Age).toString());");
        StringAssert.Contains(javascript, "console.log((Adult ? \"TRUE\" : \"FALSE\"));");

        string java = Generate(source, TargetLanguage.Java).PrimaryFile.Content;
        StringAssert.Contains(java, "int Age = 49;");
        StringAssert.Contains(java, "boolean Adult = Age >= 18;");
        StringAssert.Contains(java, "System.out.println(Integer.toString(Age));");

        string swift = Generate(source, TargetLanguage.Swift).PrimaryFile.Content;
        StringAssert.Contains(swift, "let Age: Int = 49");
        StringAssert.Contains(swift, "let Adult: Bool = Age >= 18");
        StringAssert.Contains(swift, "print(String(Age))");
        StringAssert.Contains(swift, "print((Adult ? \"TRUE\" : \"FALSE\"))");
    }

    [TestMethod]
    public void Lower_level_generators_preserve_C_family_typed_intent_and_lower_other_targets()
    {
        const string source = """
LET Age = 49
LET Adult = Age >= 18
LET Message = $"Age={Age}, Adult={Adult}"

PRINT {Age}
PRINT {Adult}
PRINT {Message}
""";

        string c = Generate(source, TargetLanguage.C).PrimaryFile.Content;
        StringAssert.Contains(c, "#include <stdbool.h>");
        StringAssert.Contains(c, "int Age = 49;");
        StringAssert.Contains(c, "bool Adult = Age >= 18;");
        StringAssert.Contains(c, "const char *Message = \"Age=49, Adult=TRUE\";");
        StringAssert.Contains(c, "printf(\"%d\\n\", Age);");
        StringAssert.Contains(c, "printf(\"%s\\n\", Adult ? \"TRUE\" : \"FALSE\");");
        StringAssert.Contains(c, "printf(\"%s\\n\", Message);");

        string objectiveC = Generate(source, TargetLanguage.ObjectiveC).PrimaryFile.Content;
        StringAssert.Contains(objectiveC, "#include <stdbool.h>");
        StringAssert.Contains(objectiveC, "int Age = 49;");
        StringAssert.Contains(objectiveC, "bool Adult = Age >= 18;");
        StringAssert.Contains(objectiveC, "printf(\"%s\\n\", Message);");

        string cobol = Generate(source, TargetLanguage.Cobol).PrimaryFile.Content;
        StringAssert.Contains(cobol, "01 Age PIC X(2) VALUE \"49\".");
        StringAssert.Contains(cobol, "01 Adult PIC X(4) VALUE \"TRUE\".");
        StringAssert.Contains(cobol, "01 SMILE-Message PIC X(18) VALUE \"Age=49, Adult=TRUE\".");
        StringAssert.Contains(cobol, "DISPLAY \"Age=49, Adult=TRUE\".");

        string masm = Generate(source, TargetLanguage.MasmX64).PrimaryFile.Content;
        StringAssert.Contains(masm, "variable0Value BYTE \"49\"");
        StringAssert.Contains(masm, "variable1Value BYTE \"TRUE\"");
        StringAssert.Contains(masm, "mov rdx, QWORD PTR [variable2Ptr]");
        StringAssert.Contains(masm, "mov r8d, DWORD PTR [variable2Length]");
    }

    private GeneratedProgram Generate(string source, TargetLanguage language)
    {
        TranspileResult result = _transpiler.Transpile(source, language);

        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return result.GeneratedProgram!;
    }
}
