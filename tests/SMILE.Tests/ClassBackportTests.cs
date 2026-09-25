using SMILE.Engine;
using SMILE.Toolchains;

namespace SMILE.Tests;

[TestClass]
[TestCategory("ClassBackport")]
public sealed class ClassBackportTests
{
    private const string Source = """
Option Explicit
Type Point
    X As Number
    Caption As Text
End Type
Class Counter
    Public Current As Number
    Private Label As Text
    Public Samples[2, 3] As Number
    Public Position As Point
    Public Sub New(Optional Start As Number = 1, Optional Name As Text = "Counter")
        Me.Current = Start
        Me.Label = Name
        Me.Position.Caption = Name + "!"
    End Sub
    Public Sub Add(Optional Delta As Number = 1)
        Me.Current = Me.Current + Delta
        Me.Samples[1, 2] = Me.Current
    End Sub
    Public Function Alias() As Counter
        Return Me
    End Function
    Public Property Score As Number
        Get
            Return Me.Current
        End Get
        Set
            Me.Current = Value
        End Set
    End Property
    Function Caption() As Text
        Return Me.Label
    End Function
End Class
Class Empty
End Class
Dim First As New Counter(Name := "First", Start := 2)
Dim Second As Counter
Dim Saved As Counter
Dim EmptyValue As Empty
Dim Copied As Point
Print Second Is Nothing
Second = First
Call Second.Add(Delta := 3)
Print First Is Second; ":"; First.Score; ":"; First.Caption()
With First
    First = New Counter(10)
    .Score = .Score + 1
    Print .Score; ":"; First.Score
End With
Saved = Second.Alias()
Call Replace(Second.Current)
Print Saved.Current; ":"; Second Is Nothing
Copied = Saved.Position
Copied.Caption = "copy"
Print Saved.Position.Caption; ":"; Copied.Caption
Call RebindValue(Saved)
Print Saved.Current
Call RebindReference(Second)
Print Second.Score; ":"; Saved Is Not Second
EmptyValue = New Empty()
Print EmptyValue Is Not Nothing
Call New Counter(4).Add()
Print New Counter(7).Score
Sub Replace(ByRef Value As Number)
    Second = Nothing
    Value = Value + 2
End Sub
Sub RebindValue(ByVal Item As Counter)
    Item = New Counter(99)
End Sub
Sub RebindReference(ByRef Item As Counter)
    Item = New Counter(12)
End Sub
""";

    private const string Expected = "True\nTrue:5:First\n6:10\n8:True\nFirst!:copy\n8\n12:True\nTrue\n7\n";

    private const string ConstructorSource = """
Option Explicit
Class Token
    Public Value As Number
    Public Name As Text
    Sub New(ByRef Sequence As Number, Label As Text)
        Sequence = Sequence + 1
        Me.Value = Sequence
        Me.Name = Label
        Escaped = Me
    End Sub
End Class
Class Reader
    Public Total As Number
    Sub New()
        Dim Bytes[1] As Number
        Dim ByteCount As Number
        Load Text File "missing-class-backport-file" Into Bytes Count ByteCount
        Me.Total = ByteCount
    End Sub
End Class
Dim Sequence As Number
Dim Escaped As Token
Dim Current As New Token(Sequence := Sequence, Label := Mark("first"))
Print Sequence; ":"; Current Is Escaped; ":"; Current.Name
Call Increment(New Token(Sequence, "second").Value)
Print Escaped.Value; ":"; Sequence
Dim InputFile As Reader
InputFile = MakeReader()
Print InputFile.Total
Function MakeReader() As Reader
    Return New Reader()
End Function
Function Mark(Value As Text) As Text
    Print Value
    Return Value
End Function
Sub Increment(ByRef Value As Number)
    Value = Value + 1
End Sub
""";

    private const string ConstructorExpected = "first\n1:True:first\n3:2\n0\n";

    [TestMethod]
    [DataRow(TargetLanguage.CSharp)]
    [DataRow(TargetLanguage.Java)]
    [DataRow(TargetLanguage.JavaScript)]
    [DataRow(TargetLanguage.Python)]
    [DataRow(TargetLanguage.Swift)]
    [DataRow(TargetLanguage.Cpp)]
    [DataRow(TargetLanguage.C)]
    [DataRow(TargetLanguage.ObjectiveC)]
    [DataRow(TargetLanguage.MasmX64)]
    [DataRow(TargetLanguage.Cobol)]
    public async Task Classes_execute_with_native_references(TargetLanguage target)
    {
        TranspileResult transpile = new SmileTranspiler().Transpile(Source, target);
        Assert.IsTrue(transpile.Success, string.Join("\n", transpile.Diagnostics));
        BuildRunResult run = await ToolchainRegistry.CreateDefault().Get(target).BuildAndRunAsync(transpile.GeneratedProgram!, CancellationToken.None);
        Assert.IsTrue(run.Success, $"{target}: {run.BuildOutput}\n{run.StandardError}");
        if (target is TargetLanguage.CSharp)
            Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(run.BuildOutput, @"warning CS86\d+"), run.BuildOutput);
        Assert.AreEqual(Expected, run.StandardOutput.Replace("\r\n", "\n"), target.ToString());
        transpile = new SmileTranspiler().Transpile(ConstructorSource, target);
        Assert.IsTrue(transpile.Success, string.Join("\n", transpile.Diagnostics));
        run = await ToolchainRegistry.CreateDefault().Get(target).BuildAndRunAsync(transpile.GeneratedProgram!, CancellationToken.None);
        Assert.IsTrue(run.Success, $"{target}: {run.BuildOutput}\n{run.StandardError}");
        Assert.AreEqual(ConstructorExpected, run.StandardOutput.Replace("\r\n", "\n"), target.ToString());
    }

    [TestMethod]
    [TestCategory("MissionGuardrail")]
    public void Classes_evaluate_identity_borrowed_fields_and_reference_copies()
    {
        EvaluationResult result = new SmileEvaluator().Evaluate(Source);
        Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
        Assert.AreEqual(Expected, result.Output);
        result = new SmileEvaluator().Evaluate(ConstructorSource);
        Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
        Assert.AreEqual(ConstructorExpected, result.Output);
    }

    [TestMethod]
    [DataRow("Class Item\nPrivate Sub New()\nEnd Sub\nEnd Class", "SMILE3451")]
    [DataRow("Class Item\nFunction New() As Number\nReturn 1\nEnd Function\nEnd Class", "SMILE3451")]
    [DataRow("Class Item\nOther As Item\nEnd Class", "SMILE3452")]
    [DataRow("Class Item\nEnd Class\nType Holder\nOther As Item\nEnd Type", "SMILE3452")]
    [DataRow("Dim Value As Number\nValue = New Number()", "SMILE3453")]
    [DataRow("Value = Nothing", "SMILE3454")]
    [DataRow("Class Item\nEnd Class\nDim Values[2] As Item", "SMILE3452")]
    [DataRow("Class Item\nPrivate Secret As Number\nEnd Class\nDim Current As New Item()\nPrint Current.Secret", "SMILE3446")]
    [DataRow("Class Item\nValue As Number\nEnd Class\nDim Current As New Item()\nPrint Current.Value", "SMILE3446")]
    [DataRow("Print Nothing Is Nothing", "SMILE3455")]
    public void Invalid_classes_are_diagnosed(string source, string code)
    {
        BindResult result = new SmileTranspiler().Bind(source);
        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Diagnostics.Any(item => item.Code == code), string.Join("\n", result.Diagnostics));
    }

    [TestMethod]
    public void Dereferencing_Nothing_preserves_prior_output_and_reports_an_error()
    {
        EvaluationResult result = new SmileEvaluator().Evaluate("Class Item\nPublic Value As Number\nEnd Class\nDim Current As Item\nPrint \"before\"\nPrint Current.Value");
        Assert.IsFalse(result.Success);
        Assert.AreEqual("before\n", result.Output);
        StringAssert.Contains(result.RuntimeError!.Message, "Nothing");
    }
}
