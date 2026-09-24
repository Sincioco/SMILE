using SMILE.Engine;
using SMILE.Toolchains;

namespace SMILE.Tests;

[TestClass]
[TestCategory("ClassBackport")]
public sealed class ClassOwnershipBackportTests
{
    private const string Source = """
Option Explicit
Type Payload
    Caption As Text
    Values[2] As Number
End Type
Class Owner
    Public Data As Payload
    Notes[2] As Text
    Sub New(Title As Text)
        Me.Data.Caption = Title + ""
        Me.Notes[1] = Title + "!"
    End Sub
    Property Linked As Owner
        Get
            Return Slot
        End Get
        Set
            Slot = Value
        End Set
    End Property
End Class
Dim Current As Owner
Dim Slot As Owner
Dim Message As Text
Dim Index As Number
For Index = 1 To 20
    Current = New Owner("x")
    With Current.Data
        Current = Nothing
        .Caption = .Caption + "y"
        Message = .Caption
        Do
            With New Owner("temporary")
                If Index = 20 Then
                    Exit For
                End If
            End With
            Exit Do
        Loop
    End With
End For
Print Message
For Index = 1 To 20
    Current = New Owner("last")
    Message = Release(Current)
End For
Print Message; ":"; Current Is Nothing
Current = New Owner("linked")
Current.Linked = Current
Print Current.Linked Is Current
Current.Linked = Nothing
Print Current.Linked Is Nothing
Current = Nothing
Function Release(ByRef Item As Owner) As Text
    With Item.Data
        Item = Nothing
        Return .Caption + "!"
    End With
End Function
""";

    private const string Expected = "xy\nlast!:True\nTrue\nTrue\n";
    private const string NullSource = """
Class Item
    Sub Work(Value As Number)
    End Sub
End Class
Dim Current As Item
Print "before"
Call Current.Work(Mark())
Function Mark() As Number
    Print "argument"
    Return 1
End Function
""";

    [TestMethod]
    public void Class_locations_survive_reassignment_early_returns_and_cross_kind_exits()
    {
        EvaluationResult result = new SmileEvaluator().Evaluate(Source);
        Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics) + result.RuntimeError);
        Assert.AreEqual(Expected, result.Output);
        SmileFormatResult formatted = SmileSourceFormatter.Format(Source);
        Assert.IsTrue(formatted.Success, string.Join("\n", formatted.Diagnostics));
        Assert.AreEqual(Expected, new SmileEvaluator().Evaluate(formatted.FormattedSource).Output);
        Assert.AreEqual(formatted.FormattedSource, SmileSourceFormatter.Format(formatted.FormattedSource).FormattedSource);
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
    public async Task Class_ownership_and_null_receiver_order_execute_on_every_target(TargetLanguage target)
    {
        TranspileResult transpile = new SmileTranspiler().Transpile(Source, target);
        Assert.IsTrue(transpile.Success, string.Join("\n", transpile.Diagnostics));
        BuildRunResult run = await ToolchainRegistry.CreateDefault().Get(target).BuildAndRunAsync(transpile.GeneratedProgram!, CancellationToken.None);
        Assert.IsTrue(run.Success, $"{target}: {run.BuildOutput}\n{run.StandardError}");
        Assert.AreEqual(Expected, run.StandardOutput.Replace("\r\n", "\n"), target.ToString());
        transpile = new SmileTranspiler().Transpile(NullSource, target);
        Assert.IsTrue(transpile.Success, string.Join("\n", transpile.Diagnostics));
        run = await ToolchainRegistry.CreateDefault().Get(target).BuildAndRunAsync(transpile.GeneratedProgram!, CancellationToken.None);
        Assert.IsFalse(run.Success, target.ToString());
        Assert.AreEqual("before\n", run.StandardOutput.Replace("\r\n", "\n"), $"{target}: {run.BuildOutput}\n{run.StandardError}");
    }
}
