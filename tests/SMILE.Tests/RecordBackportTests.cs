using SMILE.Engine;
using SMILE.Toolchains;

namespace SMILE.Tests;

[TestClass]
[TestCategory("RecordBackport")]
public sealed class RecordBackportTests
{
    private const string Source = """
Option Explicit
Type Point
    X As Number
    Caption As Text
End Type
Type Bag
    Position As Point
    Points[2] As Point
    Labels[2] As Text
    Values[2, 2] As Number
    Amount As Double
    Active As Boolean
End Type
Dim First As Bag
Dim Second As Bag
Dim Bags[2, 2] As Bag
Dim Payload[1] As Number
Print First.Position.X; ":"; First.Position.Caption; ":"; First.Active
First.Position.X = 7
First.Position.Caption = "first"
First.Points[1].Caption = "point"
First.Labels[0] = "label"
First.Values[1, 1] = 9
First.Amount = 0.5
Second = First
First.Position.X = 20
First.Points[1].Caption = "changed"
First.Labels[0] = "new"
First.Values[1, 1] = 42
Print Second.Position.X; ":"; Second.Points[1].Caption; ":"; Second.Labels[0]; ":"; Second.Values[1, 1]
Bags[1, 1] = Second
Second.Position.Caption = "second"
Print Bags[1, 1].Position.Caption; ":"; Bags[0, 0].Position.Caption
Call Observe(Second, Mutate(Second))
Print Second.Position.X
Call UpdateField(Second.Position.X, ReplaceRecord(Second))
Print Second.Position.X; ":"; Second.Points[1].Caption
Call UpdateField(Second.Values[1, 1], ReplaceRecord(Second))
Print Second.Values[1, 1]
Call UpdatePoint(Bags[0, 1].Position)
Print Bags[0, 1].Position.X
Print MakePoint().Caption
Second.Position.X = 10
Print Second.Position.X + Mutate(Second)
Print Second.Position.X
Save Data Payload Count -1 To "record-unused" Status Second.Position.X
Print Second.Position.X
Load Data "backport-record-absent-0dfef6f137d74763ac8ef76d0b87bbde" Into Payload Count Second.Position.X Status Second.Values[0, 0]
Print Second.Position.X; ":"; Second.Values[0, 0]
Call CopyStatus(Second)
Print Second.Position.X
Call LocalRecords()
Sub Observe(Value As Bag, Ignored As Number)
    Print Value.Position.X
    Value.Position.X = 100
End Sub
Function Mutate(ByRef Value As Bag) As Number
    Value.Position.X = 30
    Return 0
End Function
Function ReplaceRecord(ByRef Value As Bag) As Number
    Value = First
    Return 5
End Function
Sub UpdateField(ByRef Value As Number, Ignored As Number)
    Value = Value + 1
End Sub
Sub UpdatePoint(ByRef Value As Point)
    Value = MakePoint()
End Sub
Function MakePoint() As Point
    Dim Result As Point
    Result.X = 81
    Result.Caption = "returned"
    Return Result
End Function
Sub LocalRecords()
    Dim Local[2] As Bag
    Local[0] = First
    Local[0].Position.X = 99
    Print Local[0].Position.X; ":"; Local[1].Position.X
End Sub
Sub CopyStatus(Value As Bag)
    Save Data Payload Count -1 To "record-unused" Status Value.Position.X
    Print Value.Position.X
End Sub
""";

    private const string Expected = "0::False\n7:point:label:9\nfirst:\n7\n30\n21:changed\n43\n81\nreturned\n10\n30\n3\n0:1\n3\n0\n99:0\n";

    private const string TextSource = """
Option Explicit
Enum Mode
    Active = 4
End Enum
Type Word
    Content As Text
End Type
Type Bundle
    Words[2, 2] As Word
    Previous As Word
    Modes[2] As Mode
    Amount As Double
    Enabled As Boolean
End Type
Dim Original As Bundle
Dim Saved As Bundle
Dim Index As Number
Dim InitialMode As Mode
For Index = 1 To 20
    Original = Build("left", "right", 1, 2, 3, 0.5)
    Saved = Original
    Original.Words[1, 1].Content = "changed" + "!"
End For
Print Saved.Words[1, 1].Content; ":"; Saved.Previous.Content; ":"; Saved.Amount; ":"; Saved.Enabled
Print Saved.Modes[1] = InitialMode
Call EditText(Saved.Words[1, 1].Content, ReplaceWord(Saved.Words[1, 1]))
Print Saved.Words[1, 1].Content
Call ChangeFlags(Saved.Amount, Saved.Enabled, Saved.Modes[1])
Print Saved.Amount; ":"; Saved.Enabled; ":"; Saved.Modes[1] = Mode.Active
Print MakeWord("return").Content; ":"; MakeWord("again").Content
Function Build(PartOne As Text, PartTwo As Text, Third As Number, Fourth As Number, Fifth As Number, Sixth As Double) As Bundle
    Dim Value As Bundle
    Value.Words[1, 1] = MakeWord(PartOne + PartTwo)
    Value.Previous = MakeWord("previous")
    Value.Amount = Sixth
    Return Value
End Function
Function MakeWord(Value As Text) As Word
    Dim Result As Word
    Result.Content = Text_Slice("!" + Value, 1, Text_Length(Value))
    Return Result
End Function
Function ReplaceWord(ByRef Value As Word) As Number
    Value = MakeWord("replacement")
    Return 0
End Function
Sub EditText(ByRef Value As Text, Ignored As Number)
    Call AppendText(Value)
End Sub
Sub AppendText(ByRef Value As Text)
    Value = Value + "!"
End Sub
Sub ChangeFlags(ByRef Amount As Double, ByRef Enabled As Boolean, ByRef Selected As Mode)
    Amount = Amount + 0.5
    Enabled = True
    Selected = Mode.Active
End Sub
""";

    private const string TextExpected = "leftright:previous:0.5:False\nTrue\nreplacement!\n1.0:True:True\nreturn:again\n";

    private const string BoundsSource = """
Type Grid
    Values[1, 1] As Number
End Type
Dim Board As Grid
Dim BadIndex As Number
BadIndex = 2
Print "before"
Call Store(Board.Values[BadIndex, Later()])
Function Later() As Number
    Print "later"
    Return 0
End Function
Sub Store(ByRef Value As Number)
    Value = 1
End Sub
""";

    [TestMethod]
    [TestCategory("MissionGuardrail")]
    public void Record_values_copy_into_stable_locations_and_capture_ByVal_arguments_in_order()
    {
        EvaluationResult result = new SmileEvaluator().Evaluate(Source);
        Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
        Assert.AreEqual(Expected, result.Output);
        result = new SmileEvaluator().Evaluate(TextSource);
        Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
        Assert.AreEqual(TextExpected, result.Output);
        SmileFormatResult formatted = SmileSourceFormatter.Format(Source);
        Assert.IsTrue(formatted.Success, string.Join("\n", formatted.Diagnostics));
        StringAssert.Contains(formatted.FormattedSource, "Type Point\n    X As Number\n    Caption As Text\nEnd Type\n");
        Assert.AreEqual(Expected, new SmileEvaluator().Evaluate(formatted.FormattedSource).Output);
        result = new SmileEvaluator().Evaluate(BoundsSource);
        Assert.IsFalse(result.Success);
        Assert.AreEqual("before\n", result.Output);
        Assert.AreEqual("SMILER1210", result.RuntimeError?.Code);
    }

    [TestMethod]
    [DataRow("Type Empty\nEnd Type", "SMILE3402")]
    [DataRow("Type Item\nValue As Number\nvalue As Text\nEnd Type", "SMILE3402")]
    [DataRow("Type Item\nValue As Item\nEnd Type", "SMILE3404")]
    [DataRow("Type First\nValue As Second\nEnd Type\nType Second\nValue As First\nEnd Type", "SMILE3404")]
    [DataRow("Type Item\nValue[268435456] As Number\nEnd Type", "SMILE3411")]
    [DataRow("Type Item\nValue As Number\nEnd Type\nDim Items[268435456] As Item", "SMILE3411")]
    [DataRow("Type Item\nValue As Number\nEnd Type\nSub Test()\nDim Items[268435456] As Item\nEnd Sub", "SMILE3411")]
    [DataRow("Type Item\nValue As Number\nEnd Type\nDim Value As Item\nPrint Value", "SMILE3424")]
    [DataRow("Type Item\nValue As Number\nEnd Type\nDim Value As Item\nValue = 1", "SMILE2106")]
    [DataRow("Type Item\nValue As Number\nEnd Type\nDim Value As Item\nValue.Value = True", "SMILE2106")]
    [DataRow("Type Item\nValue[2] As Number\nEnd Type\nDim Value As Item\nPrint Value.Value", "SMILE3407")]
    [DataRow("Type Item\nValue As Number\nEnd Type\nDim Value As Item\nPrint Value.Value[0]", "SMILE3407")]
    [DataRow("Type Item\nValue As Number\nEnd Type\nDim Value As Item\nPrint Value.Absent", "SMILE3406")]
    [DataRow("If True Then\nType Item\nValue As Number\nEnd Type\nEnd If", "SMILE3400")]
    [DataRow("Type Item\nValue As Number\nEnd Type\nCall Store(Make().Value)\nSub Store(ByRef Value As Number)\nEnd Sub\nFunction Make() As Item\nDim Value As Item\nReturn Value\nEnd Function", "SMILE2165")]
    public void Record_errors_are_diagnosed(string source, string code)
    {
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
    public async Task Record_program_executes_on_every_target(TargetLanguage target)
    {
        TranspileResult transpile = new SmileTranspiler().Transpile(Source, target);
        Assert.IsTrue(transpile.Success, string.Join("\n", transpile.Diagnostics));
        BuildRunResult run = await ToolchainRegistry.CreateDefault().Get(target).BuildAndRunAsync(transpile.GeneratedProgram!, CancellationToken.None);
        Assert.IsTrue(run.Success, $"{target}: {run.BuildOutput}\n{run.StandardError}");
        Assert.AreEqual(Expected, run.StandardOutput.Replace("\r\n", "\n"), target.ToString());
        transpile = new SmileTranspiler().Transpile(TextSource, target);
        Assert.IsTrue(transpile.Success, string.Join("\n", transpile.Diagnostics));
        run = await ToolchainRegistry.CreateDefault().Get(target).BuildAndRunAsync(transpile.GeneratedProgram!, CancellationToken.None);
        Assert.IsTrue(run.Success, $"{target}: {run.BuildOutput}\n{run.StandardError}");
        Assert.AreEqual(TextExpected, run.StandardOutput.Replace("\r\n", "\n"), target.ToString());
        transpile = new SmileTranspiler().Transpile(BoundsSource, target);
        Assert.IsTrue(transpile.Success, string.Join("\n", transpile.Diagnostics));
        run = await ToolchainRegistry.CreateDefault().Get(target).BuildAndRunAsync(transpile.GeneratedProgram!, CancellationToken.None);
        Assert.IsFalse(run.Success, target.ToString());
        StringAssert.Contains(run.StandardError + run.StandardOutput, "SMILER1210");
        StringAssert.StartsWith(run.StandardOutput.Replace("\r\n", "\n"), "before\n");
        Assert.IsFalse(run.StandardOutput.Contains("later", StringComparison.Ordinal), target.ToString());
    }
}
