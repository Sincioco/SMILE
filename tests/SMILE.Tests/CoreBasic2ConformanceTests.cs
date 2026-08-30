using SMILE.Engine;
using System.IO;

namespace SMILE.Tests;

[TestClass]
[TestCategory("CoreBasic")]
[TestCategory("CoreBasic2")]
public sealed class CoreBasic2ConformanceTests
{
    private readonly SmileTranspiler _transpiler = new();
    private readonly SmileEvaluator _evaluator = new();

    [TestMethod]
    [TestCategory("MissionGuardrail")]
    public void Checked_in_Profile_2_fixtures_match_their_authoritative_output()
    {
        AssertExample("core-basic-2-canonical.smile", "Alyssa: 75\nBorin: 93\nCyra: 92\nBest player: Borin\nAverage score: 86\nSelected Borin\n");
        AssertExample("core-basic-2-byval-scope.smile", "15\n30\n10\n");
        AssertExample("core-basic-2-recursion.smile", "720\nTrue\nTrue\n");
    }

    [TestMethod]
    public void Option_Explicit_controls_implicit_variables_and_has_one_valid_position()
    {
        Assert.IsTrue(_evaluator.Evaluate("Value = 3\nPrint Value").Success);
        AssertRejected("Option Explicit\nValue = 3", "must be declared");
        AssertRejected("Print \"before\"\nOption Explicit", "first nonblank");
        AssertRejected("Option Explicit\nOption Explicit", "at most once");

        EvaluationResult declared = _evaluator.Evaluate("Option Explicit\nDim Value As Number\nValue = 3\nPrint Value");
        Assert.IsTrue(declared.Success, Join(declared.Diagnostics));
        Assert.AreEqual("3\n", declared.Output);
    }

    [TestMethod]
    public void Calls_bind_forward_signatures_and_arguments_run_once_left_to_right()
    {
        const string source = """
Option Explicit
Dim Trace As Text

Call Take(Mark("A"), Mark("B"), Mark("C"))
Print Trace

Sub Take(First As Text, Second As Text, Third As Text)
End Sub

Function Mark(Value As Text) As Text
    Trace = Trace + Value
    Return Value
End Function
""";

        EvaluationResult result = _evaluator.Evaluate(source);

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        Assert.AreEqual("ABC\n", result.Output);
    }

    [TestMethod]
    public void Parameter_counts_cover_zero_one_four_five_eight_and_sixteen()
    {
        const string source = """
Option Explicit
Call Zero()
Call One(1)
Call Four(1, 2, 3, 4)
Call Five(1, 2, 3, 4, 5)
Call Eight(1, 2, 3, 4, 5, 6, 7, 8)
Call Sixteen(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16)

Sub Zero()
    Print 0
End Sub
Sub One(P01 As Number)
    Print P01
End Sub
Sub Four(P01 As Number, P02 As Number, P03 As Number, P04 As Number)
    Print P01 + P04
End Sub
Sub Five(P01 As Number, P02 As Number, P03 As Number, P04 As Number, P05 As Number)
    Print P01 + P05
End Sub
Sub Eight(P01 As Number, P02 As Number, P03 As Number, P04 As Number, P05 As Number, P06 As Number, P07 As Number, P08 As Number)
    Print P01 + P08
End Sub
Sub Sixteen(P01 As Number, P02 As Number, P03 As Number, P04 As Number, P05 As Number, P06 As Number, P07 As Number, P08 As Number, P09 As Number, P10 As Number, P11 As Number, P12 As Number, P13 As Number, P14 As Number, P15 As Number, P16 As Number)
    Print P01 + P16
End Sub
""";

        EvaluationResult result = _evaluator.Evaluate(source);

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        Assert.AreEqual("0\n1\n5\n6\n9\n17\n", result.Output);
        Assert.IsTrue(_transpiler.TranspileMany(source, ActiveTargetLanguages.All).All(item => item.Success));
    }

    [TestMethod]
    public void ByVal_local_shadowing_global_access_and_fresh_frames_are_distinct()
    {
        const string source = """
Option Explicit
Dim Score As Number
Score = 10
Call ChangeCopy(Score)
Call BumpGlobal()
Print Score
Print Fresh()
Print Fresh()

Sub ChangeCopy(Score As Number)
    Score = Score + 100
End Sub

Sub BumpGlobal()
    Score = Score + 1
End Sub

Function Fresh() As Number
    Dim Tally As Number
    Tally = Tally + 1
    Return Tally
End Function
""";

        EvaluationResult result = _evaluator.Evaluate(source);

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        Assert.AreEqual("11\n1\n1\n", result.Output);
    }

    [TestMethod]
    public void Direct_and_mutual_recursion_receive_independent_frames()
    {
        string source = File.ReadAllText(Path.Combine(FindExamplesDirectory(), "core-basic-2-recursion.smile"));
        EvaluationResult result = _evaluator.Evaluate(source);

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        Assert.AreEqual("720\nTrue\nTrue\n", result.Output);
    }

    [TestMethod]
    public void Select_Case_supports_all_scalars_evaluates_selector_once_and_uses_first_match()
    {
        const string source = """
Option Explicit
Dim Calls As Number

Select Case NextValue()
Case 1
    Print "Number"
Case Else
    Print "wrong"
End Select

Select Case True
Case False
    Print "wrong"
Case True
    Print "Boolean"
End Select

Select Case "Sin"
Case "sin"
    Print "wrong"
Case "Sin"
    Print "Text"
Case Else
    Print "wrong"
End Select

Print Calls

Function NextValue() As Number
    Calls = Calls + 1
    Return Calls
End Function
""";

        EvaluationResult result = _evaluator.Evaluate(source);

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        Assert.AreEqual("Number\nBoolean\nText\n1\n", result.Output);
    }

    [TestMethod]
    public void Arrays_are_zero_based_typed_defaulted_and_local_arrays_reset_per_call()
    {
        const string source = """
Option Explicit
Const ArrayLength = 2
Dim Numbers[ArrayLength] As Number
Dim Flags[ArrayLength] As Boolean
Dim Words[ArrayLength] As Text
Print Numbers[0]; Flags[0]; "["; Words[0]; "]"
Numbers[1] = 9
Flags[1] = True
Words[1] = "ready"
Print Numbers[1]; Flags[1]; Words[1]
Print FreshArray()
Print FreshArray()

Function FreshArray() As Text
    Dim Values[2] As Text
    Values[0] = Values[0] + "X"
    Return Values[0]
End Function
""";

        EvaluationResult result = _evaluator.Evaluate(source);

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        Assert.AreEqual("0False[]\n9Trueready\nX\nX\n", result.Output);
    }

    [TestMethod]
    public void Dynamic_array_bounds_fail_before_access_and_constant_bounds_fail_at_compile_time()
    {
        EvaluationResult runtime = _evaluator.Evaluate("""
Option Explicit
Dim Values[2] As Number
Dim Index As Number
Index = 2
Print Values[Index]
""");

        Assert.IsFalse(runtime.Success);
        Assert.AreEqual("SMILER1210", runtime.RuntimeError?.Code);
        AssertRejected("Dim Values[2] As Number\nPrint Values[2]", "outside the valid range");
        AssertRejected("Dim Values[2] As Number\nPrint Values[-1]", "outside the valid range");
    }

    [TestMethod]
    public void End_Program_propagates_through_active_routines()
    {
        EvaluationResult result = _evaluator.Evaluate("""
Call StopNow()
Print "unreachable"
Sub StopNow()
    Print "stop"
    End Program
End Sub
""");

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        Assert.AreEqual("stop\n", result.Output);
    }

    [TestMethod]
    public void Function_definite_return_covers_If_and_Select_but_never_assumes_a_loop_runs()
    {
        Assert.IsTrue(_transpiler.Bind("""
Function Choose(Value As Boolean) As Number
    If Value Then
        Return 1
    Else
        Return 2
    End If
End Function
""").Success);

        Assert.IsTrue(_transpiler.Bind("""
Function Choose(Value As Number) As Text
    Select Case Value
    Case 1
        Return "one"
    Case Else
        Return "other"
    End Select
End Function
""").Success);

        AssertRejected("""
Function Missing(Value As Boolean) As Number
    If Value Then
        Return 1
    End If
End Function
""", "does not return");
        AssertRejected("""
Function LoopOnly() As Number
    Do
        Return 1
    Loop
End Function
""", "does not return");
    }

    [TestMethod]
    public void Local_Dim_visibility_starts_at_its_declaration_and_does_not_bind_an_earlier_global()
    {
        AssertRejected("""
Dim Value As Number
Sub Demonstrate()
    Print Value
    Dim Value As Number
End Sub
""", "used before its Dim");
    }

    [TestMethod]
    [DataRow("Sub Outer()\nSub Inner()\nEnd Sub\nEnd Sub", "cannot be nested")]
    [DataRow("Sub Work()\nEnd Sub\nSub work()\nEnd Sub", "already declared")]
    [DataRow("Dim Work As Number\nSub Work()\nEnd Sub", "already declared")]
    [DataRow("Sub Work(Value As Number, value As Number)\nEnd Sub", "Parameter")]
    [DataRow("Sub Work(Value As Number)\nDim value As Number\nEnd Sub", "Local")]
    [DataRow("Sub Work(Value)\nEnd Sub", "Typed parameters")]
    [DataRow("Sub Work(ByRef Value As Number)\nEnd Sub", "ByRef")]
    [DataRow("Sub Work(Optional Value As Number)\nEnd Sub", "Optional")]
    [DataRow("Sub Work(Value As Number)\nEnd Sub\nCall Work(Value:=1)", "named arguments")]
    [DataRow("Sub Work(Value As Number)\nEnd Sub\nCall Work()", "expects 1")]
    [DataRow("Sub Work(Value As Number)\nEnd Sub\nCall Work(\"one\")", "Argument 1")]
    [DataRow("Function Work() As Number\nReturn 1\nEnd Function\nCall Work()", "must be used as an expression")]
    [DataRow("Sub Work()\nEnd Sub\nPrint Work()", "cannot be used as an expression")]
    [DataRow("Return", "only inside")]
    [DataRow("Sub Work()\nReturn 1\nEnd Sub", "cannot return a value")]
    [DataRow("Function Work() As Number\nReturn\nEnd Function", "must return a value")]
    [DataRow("Function Work() As Number\nReturn \"one\"\nEnd Function", "must return Number")]
    [DataRow("Dim Choice As Number\nSelect Case Choice\nCase Choice\nEnd Select", "Constant expression")]
    [DataRow("Select Case 1\nCase \"one\"\nEnd Select", "selector's scalar type")]
    [DataRow("Select Case 1\nCase 1\nCase 1\nEnd Select", "Duplicate Case")]
    [DataRow("Select Case 1\nCase Else\nCase 1\nEnd Select", "must be the last")]
    [DataRow("Dim Values[0] As Number", "must be positive")]
    [DataRow("Dim Values[-1] As Number", "must be positive")]
    [DataRow("Dim ArrayLength As Number\nDim Values[ArrayLength] As Number", "Constant expression")]
    [DataRow("Dim Values[2, 2] As Number", "one-dimensional")]
    [DataRow("Dim Values[2]", "require 'As")]
    [DataRow("Dim Value As Number\nPrint Value[0]", "cannot be indexed")]
    [DataRow("Dim Values[2] As Number\nPrint Values", "requires an index")]
    [DataRow("Dim Values[2] As Number\nPrint Values[True]", "must have type Number")]
    public void Invalid_Profile_2_programs_receive_focused_diagnostics(string source, string expected)
    {
        AssertRejected(source, expected);
    }

    private void AssertExample(string fileName, string expected)
    {
        string source = File.ReadAllText(Path.Combine(FindExamplesDirectory(), fileName));
        EvaluationResult result = _evaluator.Evaluate(source);
        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        Assert.AreEqual(expected, result.Output);
    }

    private void AssertRejected(string source, string expectedMessage)
    {
        BindResult result = _transpiler.Bind(source);
        Assert.IsFalse(result.Success, source);
        StringAssert.Contains(Join(result.Diagnostics), expectedMessage, StringComparison.OrdinalIgnoreCase);
    }

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
