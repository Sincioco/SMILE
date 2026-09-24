using SMILE.Engine;
using SMILE.Toolchains;

namespace SMILE.Tests;

[TestClass]
[TestCategory("WithBackport")]
public sealed class WithBackportTests
{
    private const string Source = """
Option Explicit
Type Point
    X As Number
    Name As Text
    Amount As Double
    Active As Boolean
End Type
Type Bag
    Position As Point
    Points[2] As Point
End Type
Dim Bags[2, 2] As Bag
Dim Other As Point
Dim Calls As Number
Dim Index As Number
Dim Counter As Number
Dim Message As Text
Index = 1
With Bags[Choose(), Index]
    Index = 0
    .Position.Name = "outer"
    With .Points[Choose()]
        .X = 7
        .Name = "inner"
        Call Change(.X, Replace(Bags[1, 1]))
        .Amount = 1.25
        .Active = True
        Print .X; ":"; .Name; ":"; .Amount; ":"; .Active
    End With
    Print .Position.Name; ":"; Calls
End With
Call Recurse(Bags[1, 1].Points[1], 2)
Print Bags[1, 1].Points[1].X; ":"; Other.X
Print Snapshot(Bags[1, 1]); ":"; Bags[1, 1].Points[1].X
With Bags[1, 1].Points[1]
    .Name = Rename(Bags[1, 1].Points[1])
    Print .Name; ":"; .X
End With
With Bags[1, 1]
    For Counter = 1 To 3
        Do
            With .Position
                .Name = "loop"
                Exit For
            End With
        Loop
    End For
End With
Print Bags[1, 1].Position.Name
Call Local()
For Counter = 1 To 40
    Message = ReadName(Bags[1, 1])
End For
Print Message
With Bags[0, 0]
    ' An empty With block still evaluates its target.
End With
Function Choose() As Number
    Calls = Calls + 1
    Return 1
End Function
Function Replace(ByRef Value As Bag) As Number
    Dim Fresh As Bag
    Fresh.Position.Name = "new"
    Fresh.Points[1].X = 20
    Fresh.Points[1].Name = "replaced"
    Value = Fresh
    Return 2
End Function
Sub Change(ByRef Value As Number, ByVal Delta As Number)
    Value = Value + Delta
End Sub
Sub Recurse(ByRef Value As Point, ByVal Depth As Number)
    With Value
        .X = .X + 1
        If Depth > 0 Then
            Call Recurse(Other, Depth - 1)
        End If
        .X = .X + 10
    End With
End Sub
Function Snapshot(ByVal Value As Bag) As Number
    With Value.Points[1]
        .X = 99
        Return .X
    End With
    Print "unreachable"
End Function
Function ReadName(ByRef Value As Bag) As Text
    Select Case 1
    Case 1
        With Value.Points[Text_Length("x" + "")]
            Return .Name
        End With
    Case Else
        Return "unused"
    End Select
End Function
Function Rename(ByRef Value As Point) As Text
    Dim Fresh As Point
    Fresh.X = 4
    Value = Fresh
    Return "assign" + "ed"
End Function
Sub Local()
    Dim LocalValue As Point
    With LocalValue
        Dim Message As Text
        Message = "local"
        .Name = Message
        Print .Name
    End With
End Sub
""";

    private const string Expected = "22:replaced:1.25:True\nnew:2\n33:22\n99:33\nassigned:4\nloop\nlocal\nassigned\n";

    [TestMethod]
    [TestCategory("MissionGuardrail")]
    public void With_captures_locations_once_and_restores_recursive_receivers()
    {
        EvaluationResult result = new SmileEvaluator().Evaluate(Source);
        Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
        Assert.AreEqual(Expected, result.Output);
        SmileFormatResult formatted = SmileSourceFormatter.Format(Source);
        Assert.IsTrue(formatted.Success, string.Join("\n", formatted.Diagnostics));
        StringAssert.Contains(formatted.FormattedSource, "With Bags[Choose(), Index]\n    Index = 0\n    .Position.Name = \"outer\"");
        StringAssert.Contains(formatted.FormattedSource, "    With .Points[Choose()]\n        .X = 7");
        Assert.AreEqual(Expected, new SmileEvaluator().Evaluate(formatted.FormattedSource).Output);
    }

    [TestMethod]
    [DataRow("Print .Missing", "SMILE3413")]
    [DataRow(".Missing = 1", "SMILE3413")]
    [DataRow("With 1\nPrint .Missing\nEnd With", "SMILE3415")]
    [DataRow("With Make()\nEnd With", "SMILE3412")]
    [DataRow("With Value\nWith 1\n.X = 1\nEnd With\nEnd With", "SMILE3413")]
    public void Invalid_With_receivers_are_diagnosed(string body, string code)
    {
        string source = "Type Point\nX As Number\nEnd Type\nDim Value As Point\n" + body +
            "\nFunction Make() As Point\nDim Result As Point\nReturn Result\nEnd Function";
        BindResult result = new SmileTranspiler().Bind(source);
        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Diagnostics.Any(item => item.Code == code), string.Join("\n", result.Diagnostics));
    }

    [TestMethod]
    [DataRow(TargetLanguage.CSharp)]
    [DataRow(TargetLanguage.Cobol)]
    [DataRow(TargetLanguage.MasmX64)]
    [DataRow(TargetLanguage.C)]
    [DataRow(TargetLanguage.ObjectiveC)]
    [DataRow(TargetLanguage.Java)]
    [DataRow(TargetLanguage.JavaScript)]
    [DataRow(TargetLanguage.Python)]
    [DataRow(TargetLanguage.Swift)]
    [DataRow(TargetLanguage.Cpp)]
    public async Task With_program_executes_on_every_target(TargetLanguage target)
    {
        TranspileResult transpile = new SmileTranspiler().Transpile(Source, target);
        Assert.IsTrue(transpile.Success, string.Join("\n", transpile.Diagnostics));
        BuildRunResult run = await ToolchainRegistry.CreateDefault().Get(target).BuildAndRunAsync(transpile.GeneratedProgram!, CancellationToken.None);
        Assert.IsTrue(run.Success, $"{target}: {run.BuildOutput}\n{run.StandardError}");
        Assert.AreEqual(Expected, run.StandardOutput.Replace("\r\n", "\n"), target.ToString());
    }
}
