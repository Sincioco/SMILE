using SMILE.Engine;
using SMILE.Toolchains;

namespace SMILE.Tests;

[TestClass]
[TestCategory("ByRefBackport")]
public sealed class ByRefBackportTests
{
    private const string Source = """
Dim SharedValue As Number
Dim CurrentIndex As Number
Dim Values[2, 2] As Number
Dim Captions[2] As Text
Dim Flags[2, 2] As Boolean
Dim Amount As Double
Dim Heading As Text
SharedValue = 1
Call Increment(SharedValue)
Print SharedValue
Call Aliases(SecondValue:=SharedValue, FirstValue:=SharedValue)
Print SharedValue
Call CopyArgument(SharedValue)
Print SharedValue
Print GlobalAlias(SharedValue)
Print SharedValue
CurrentIndex = 0
Call Store(Value:=Values[RowIndex(), ColumnIndex()], NewValue:=ChangeCurrentIndex())
Print Values[0, 1]; ":"; Values[1, 1]
Call Increment(Values[0, 1])
Print Values[0, 1]
Heading = "Sin"
Call Rename(Heading, Heading)
Print Heading
Captions[1] = "array"
Call Rename(Captions[1], Captions[1])
Print Captions[1]
Call InvertFlag(Flags[1, 0])
Print Flags[1, 0]
Amount = 0.5
Call RaiseAmount(Amount)
Print Amount = 1.0
Call Many(1, 2.0, True, "four", Amount, SharedValue)
Print Amount = 3.0; ":"; SharedValue
SharedValue = 3
Call Recurse(SharedValue)
Print SharedValue
Call LocalArrays()
Call CountUsing(SharedValue)
Print SharedValue
Call ReadCount(SharedValue)
Print SharedValue
Sub Increment(ByRef Value As Number)
    Value = Value + 1
End Sub
Sub Aliases(ByRef FirstValue As Number, ByRef SecondValue As Number)
    FirstValue = FirstValue + 1
    Call Increment(SecondValue)
    Print FirstValue
End Sub
Sub CopyArgument(Value As Number)
    Call Increment(Value)
    Print Value
End Sub
Function GlobalAlias(ByRef Value As Number) As Number
    SharedValue = 20
    Value = Value + 1
    Return SharedValue
End Function
Function RowIndex() As Number
    Print "row"
    Return CurrentIndex
End Function
Function ColumnIndex() As Number
    Print "column"
    Return 1
End Function
Function ChangeCurrentIndex() As Number
    Print "later"
    CurrentIndex = 1
    Return 42
End Function
Sub Store(ByRef Value As Number, Optional NewValue As Number = 7)
    Value = NewValue
End Sub
Sub Rename(ByRef FirstValue As Text, ByRef SecondValue As Text)
    FirstValue = FirstValue + "!"
    SecondValue = SecondValue + "?"
End Sub
Sub InvertFlag(ByRef Value As Boolean)
    Value = Not Value
End Sub
Sub RaiseAmount(ByRef Value As Double)
    Value = Value + 0.5
End Sub
Sub Many(FirstValue As Number, SecondValue As Double, ThirdValue As Boolean, FourthValue As Text, ByRef FifthValue As Double, ByRef SixthValue As Number)
    FifthValue = FifthValue + SecondValue
    SixthValue = SixthValue + FirstValue
End Sub
Sub Recurse(ByRef Value As Number)
    If Value > 0 Then
        Value = Value - 1
        Call Recurse(Value)
    End If
End Sub
Sub LocalArrays()
    Dim LocalValues[2] As Number
    Dim LocalText[2, 2] As Text
    Call Store(LocalValues[1])
    LocalText[0, 1] = "local"
    Call Rename(LocalText[0, 1], LocalText[0, 1])
    Print LocalValues[1]; ":"; LocalText[0, 1]
End Sub
Sub CountUsing(ByRef Value As Number)
    For Value = 1 To 2
        Print Value
    End For
End Sub
Sub ReadCount(ByRef ByteCount As Number)
    Dim Bytes[2] As Number
    Load Text File "missing-byref-count.txt" Into Bytes Count ByteCount
End Sub
""";
    private const string Expected = "2\n4\n4\n5\n4\n21\n21\nrow\ncolumn\nlater\n42:0\n43\nSin!?\narray!?\nTrue\nTrue\nTrue:22\n0\n7:local!?\n1\n2\n3\n0\n";

    private const string BoundsSource = """
Dim Values[2, 2] As Number
Call Change(Values[BadIndex(), LaterIndex()])
Function BadIndex() As Number
    Print "first"
    Return 3
End Function
Function LaterIndex() As Number
    Print "incorrect later index"
    Return 0
End Function
Sub Change(ByRef Value As Number)
    Print "incorrect call"
    Value = 1
End Sub
""";

    [TestMethod]
    [TestCategory("MissionGuardrail")]
    public void Writable_aliases_forwarding_and_ordered_array_locations_preserve_meaning()
    {
        EvaluationResult result = new SmileEvaluator().Evaluate(Source);
        Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
        Assert.AreEqual(Expected, result.Output);
        SmileFormatResult formatted = SmileSourceFormatter.Format(Source);
        Assert.IsTrue(formatted.Success, string.Join("\n", formatted.Diagnostics));
        Assert.AreEqual(Expected, new SmileEvaluator().Evaluate(formatted.FormattedSource).Output);
        EvaluationResult bounds = new SmileEvaluator().Evaluate(BoundsSource);
        Assert.IsFalse(bounds.Success);
        Assert.AreEqual("SMILER1210", bounds.RuntimeError?.Code);
        Assert.AreEqual("first\n", bounds.Output);
    }

    [TestMethod]
    [DataRow("Call Change(1)")]
    [DataRow("Call Change(1 + 2)")]
    [DataRow("Const FixedValue = 1\nCall Change(FixedValue)")]
    [DataRow("Dim Value As Double\nCall Change(Value)")]
    [DataRow("Dim Values[2] As Number\nCall Change(Values)")]
    [DataRow("Function GetValue() As Number\nReturn 1\nEnd Function\nCall Change(GetValue())")]
    public void ByRef_requires_an_exact_type_writable_location(string source)
    {
        Assert.IsFalse(new SmileTranspiler().Bind(source + "\nSub Change(ByRef Value As Number)\nEnd Sub").Success);
    }

    [TestMethod]
    public void Ordinary_references_use_native_constructs_without_runtime_helpers()
    {
        const string simple = "Dim Value As Number\nCall Increment(Value)\nSub Increment(ByRef Amount As Number)\nAmount = Amount + 1\nEnd Sub";
        var transpiler = new SmileTranspiler();
        string csharp = transpiler.Transpile(simple, TargetLanguage.CSharp).GeneratedProgram!.Files.Single(file => file.IsPrimary).Content;
        StringAssert.Contains(csharp, "ref long Amount");
        string cpp = transpiler.Transpile(simple, TargetLanguage.Cpp).GeneratedProgram!.Files.Single(file => file.IsPrimary).Content;
        StringAssert.Contains(cpp, "std::int64_t& Amount");
        string swift = transpiler.Transpile(simple, TargetLanguage.Swift).GeneratedProgram!.Files.Single(file => file.IsPrimary).Content;
        StringAssert.Contains(swift, "inout Int64");
        Assert.DoesNotContain("SmileReference", swift);
    }

    [TestMethod]
    [TestCategory("MilestoneMatrix")]
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
    public async Task ByRef_executes_in_every_active_target(TargetLanguage target)
    {
        IToolchain toolchain = ToolchainRegistry.CreateDefault().Get(target);
        foreach ((string source, string expected, bool success) in new[] { (Source, Expected, true), (BoundsSource, "first\n", false) })
        {
            TranspileResult transpile = new SmileTranspiler().Transpile(source, target);
            Assert.IsTrue(transpile.Success, string.Join("\n", transpile.Diagnostics));
            BuildRunResult run = await toolchain.BuildAndRunAsync(transpile.GeneratedProgram!, CancellationToken.None);
            Assert.AreEqual(success, run.Success, $"{target}: {run.BuildOutput}\n{run.StandardError}");
            string output = run.StandardOutput.Replace("\r\n", "\n");
            if (success) Assert.AreEqual(expected, output, target.ToString());
            else
            {
                StringAssert.StartsWith(output, expected, target.ToString());
                Assert.DoesNotContain("incorrect", output + run.StandardError);
                StringAssert.Contains(output + run.StandardError, "SMILER1210");
            }
        }
    }
}
