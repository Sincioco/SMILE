using SMILE.Engine;

namespace SMILE.Tests;

[TestClass]
[TestCategory("CoreBasic")]
public sealed class CoreBasicConformanceTests
{
    private readonly SmileTranspiler _transpiler = new();
    private readonly SmileEvaluator _evaluator = new();

    [TestMethod]
    public void Public_engine_surface_has_no_language_profile_selector()
    {
        Assert.IsTrue(typeof(SmileTranspiler).GetConstructors().All(constructor => constructor.GetParameters().Length == 0));
        Assert.IsTrue(typeof(SmileEvaluator).GetConstructors().All(constructor => constructor.GetParameters().Length == 0));
        Assert.IsFalse(typeof(SmileTranspiler).Assembly.GetExportedTypes().Any(type =>
            type.Name.Contains("Dialect", StringComparison.OrdinalIgnoreCase) ||
            type.Name.Contains("Legacy", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    [TestCategory("MissionGuardrail")]
    public void Canonical_fixture_evaluates_with_SMILE_2_BASIC_core_behavior()
    {
        const string source = """
' Core BASIC statements and doubled-quote Text escaping.
Const Greeting = "She said ""Hello""."
Dim Total As Number
Total = 0
For I = 1 To 3
    Total = Total + I
End For
Print Greeting; " Total="; Total
Do
    Total = Total - 1
Loop Until Total = 0
If Not False And Total = 0 Then
    Print "done";
Else
    Print "wrong"
End If
Print "!"
End Program
Print "unreachable"
""";

        EvaluationResult result = _evaluator.Evaluate(source);

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        Assert.AreEqual("She said \"Hello\". Total=6\ndone!\n", result.Output);
    }

    [TestMethod]
    public void Names_keywords_and_comments_are_case_insensitive_and_Unicode_aware()
    {
        const string source = """
變數 = 2 ' inline comment
cAfÉ = 3
pRiNt 變數 + Café
""";

        EvaluationResult result = _evaluator.Evaluate(source);

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        Assert.AreEqual("5\n", result.Output);
    }

    [TestMethod]
    public void Expressions_follow_SMILE_2_precedence_short_circuit_and_signed_remainder()
    {
        const string source = """
Print 2 + 3 * 4
Print (2 + 3) * 4
Print -7 / 3
Print -7 Mod 3
Print False And (1 / 0 = 0)
Print True Or (1 / 0 = 0)
Print "A" + "B" = "AB"
""";

        EvaluationResult result = _evaluator.Evaluate(source);

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        Assert.AreEqual("14\n20\n-2\n-1\nFALSE\nTRUE\nTRUE\n", result.Output);
    }

    [TestMethod]
    public void Number_boundaries_overflow_and_divide_by_zero_match_the_profile()
    {
        EvaluationResult boundaries = _evaluator.Evaluate("""
Print 9223372036854775807
Print -9223372036854775807 - 1
""");
        Assert.IsTrue(boundaries.Success, Join(boundaries.Diagnostics));
        Assert.AreEqual("9223372036854775807\n-9223372036854775808\n", boundaries.Output);

        BindResult outOfRange = _transpiler.Bind("Print 9223372036854775808");
        Assert.IsFalse(outOfRange.Success);
        StringAssert.Contains(Join(outOfRange.Diagnostics), "outside the signed 64-bit range");

        EvaluationResult overflow = _evaluator.Evaluate(
            "Value = 9223372036854775807\nPrint Value + 1");
        Assert.IsFalse(overflow.Success);
        Assert.AreEqual("SMILER1206", overflow.RuntimeError?.Code);

        EvaluationResult divideByZero = _evaluator.Evaluate(
            "Divisor = 0\nPrint 1 / Divisor");
        Assert.IsFalse(divideByZero.Success);
        Assert.AreEqual("SMILER1207", divideByZero.RuntimeError?.Code);
    }

    [TestMethod]
    public void Text_equality_is_case_sensitive()
    {
        EvaluationResult result = _evaluator.Evaluate("""
Print "A" = "A"
Print "A" <> "a"
""");

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        Assert.AreEqual("TRUE\nTRUE\n", result.Output);
        AssertRejected("Print \"A\" < \"B\"", "not defined for Text and Text");
    }

    [TestMethod]
    public void Dim_direct_assignment_and_Const_enforce_exact_scalar_types()
    {
        const string valid = """
Const Later = Base + 2
Const Base = 3
Dim Tally As Number
Dim Ready As Boolean
Dim Name As Text
Print Tally; Ready; "["; Name; "]"; Later
Tally = 5
Ready = True
Name = "Sin"
Print Tally; Ready; Name
""";

        EvaluationResult result = _evaluator.Evaluate(valid);

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        Assert.AreEqual("0FALSE[]5\n5TRUESin\n", result.Output);
        AssertRejected("Value = 1\nValue = \"one\"", "Cannot assign Text");
        AssertRejected("Const Limit = 3\nLimit = 4", "cannot be assigned");
        AssertRejected("Const Alpha = Beta\nConst Beta = Alpha", "circular");
        AssertRejected("Dim Value", "require 'As Number'");
    }

    [TestMethod]
    public void Print_supports_blank_lists_and_trailing_semicolon_suppression()
    {
        const string source = """
Name = "Sin"
Print
Print "Hello "; Name;
Print "!"
""";

        EvaluationResult result = _evaluator.Evaluate(source);

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        Assert.AreEqual("\nHello Sin!\n", result.Output);
    }

    [TestMethod]
    public void If_For_Do_and_typed_Exit_preserve_nested_control_flow()
    {
        const string source = """
For Outer = 1 To 3
    Do
        Print Outer;
        Exit For
    Loop
End For
Print "A"
Do
    For Inner = 3 Down To 1
        Print Inner;
        Exit Do
    End For
Loop
If True Then
    Print "B"
Else If True Then
    Print "wrong"
Else
    Print "wrong"
End If
""";

        EvaluationResult result = _evaluator.Evaluate(source);

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        Assert.AreEqual("1A\n3B\n", result.Output);
    }

    [TestMethod]
    public void For_bounds_are_evaluated_once_and_Do_is_post_tested()
    {
        const string source = """
Limit = 3
For I = 1 To Limit
    Print I;
    Limit = 0
End For
Print
Tally = 0
Do
    Tally = Tally + 1
Loop Until Tally >= 1
Print Tally
""";

        EvaluationResult result = _evaluator.Evaluate(source);

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        Assert.AreEqual("123\n1\n", result.Output);
    }

    [TestMethod]
    public void For_counter_final_values_and_zero_iteration_behavior_match_SMILE_2()
    {
        const string source = """
For Ascending = 1 To 2
End For
Print Ascending
For Descending = 2 Down To 1
End For
Print Descending
For EmptyAscending = 2 To 1
End For
Print EmptyAscending
For EmptyDescending = 1 Down To 2
End For
Print EmptyDescending
""";

        EvaluationResult result = _evaluator.Evaluate(source);

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        Assert.AreEqual("3\n0\n2\n1\n", result.Output);
    }

    [TestMethod]
    public void End_Program_terminates_from_a_nested_block()
    {
        EvaluationResult result = _evaluator.Evaluate("""
For I = 1 To 2
    If I = 1 Then
        Print "stop"
        End Program
    End If
End For
Print "unreachable"
""");

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        Assert.AreEqual("stop\n", result.Output);
    }

    [TestMethod]
    public void Malformed_blocks_conditions_and_declarations_are_rejected()
    {
        AssertRejected("If 1 Then\nEnd If", "must have type Boolean");
        AssertRejected("Do\nLoop Until 1", "must have type Boolean");
        AssertRejected("Dim Value As Number\nDim Value As Number", "already declared");
        AssertRejected("If True Then\nPrint \"missing end\"", "Expected 'End If'");
        AssertRejected("For I = 1 To 2\nPrint I", "Expected 'End For'");
        AssertRejected("Do\nPrint \"missing loop\"", "Expected 'Loop'");
        AssertRejected("Print \"unterminated", "Unterminated Text literal");
    }

    [TestMethod]
    [TestCategory("MissionGuardrail")]
    public void Historical_SMILE_1_only_forms_are_rejected()
    {
        string[] obsoleteSources =
        {
            "LET Name = \"Sin\"",
            "SET Count = Count + 1",
            "INPUT Name",
            "WHILE True\nEND WHILE",
            "PRINT Hello {Name}",
            "PRINT {Name}",
            "$\"Interpolated {Name}\"",
            "REM old comment",
            "// old comment",
            "# old comment",
            "-- old comment",
            "Print \"backslash \\\"escaped\\\"\""
        };

        foreach (string source in obsoleteSources)
        {
            BindResult result = _transpiler.Bind(source);
            Assert.IsFalse(result.Success, $"Obsolete source was accepted: {source}");
        }
    }

    [TestMethod]
    public void Excluded_SMILE_2_superset_forms_are_rejected()
    {
        string[] excluded =
        {
            "Option Explicit",
            "Dim Values(3) As Number",
            "Select Case 1\nEnd Select",
            "Sub Work\nEnd Sub",
            "Call Work",
            "Module Demo",
            "Game Window 800 By 600"
        };

        foreach (string source in excluded)
        {
            Assert.IsFalse(_transpiler.Bind(source).Success, source);
        }
    }

    [TestMethod]
    public void Source_spans_retain_physical_line_and_column_information()
    {
        BindResult result = _transpiler.Bind("Value = 1\n    Print Missing\n");

        Assert.IsFalse(result.Success);
        Diagnostic diagnostic = result.Diagnostics.Single(item => item.Message.Contains("Missing", StringComparison.Ordinal));
        Assert.AreEqual(2, diagnostic.Span.Line);
        Assert.AreEqual(11, diagnostic.Span.Column);
    }

    private void AssertRejected(string source, string expectedMessage)
    {
        BindResult result = _transpiler.Bind(source);
        Assert.IsFalse(result.Success);
        StringAssert.Contains(Join(result.Diagnostics), expectedMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static string Join(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.ToString()));
}
