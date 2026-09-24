using SMILE.Engine;
using SMILE.Toolchains;

namespace SMILE.Tests;

[TestClass]
[TestCategory("RoutineArgumentBackport")]
public sealed class RoutineArgumentBackportTests
{
    private const string Source = """
Const DefaultCaption = "ready"
Dim SharedValue As Number
SharedValue = +4
Call Present()
Call Present(Caption:="named", Value:=+7)
Call Present(Caption:=CaptionValue(), Value:=NumberValue())
Print Difference(RightValue:=SharedValue, LeftValue:=ChangeShared())
Print SharedValue
SharedValue = 4
Print Difference(LeftValue:=SharedValue, RightValue:=ChangeShared())
Print False And Never(RightValue:=2, LeftValue:=1)
Sub Present(
    Optional Value As Number = -2,
    Optional ByVal Caption As Text = DefaultCaption
)
    Print Caption; ":"; Value
End Sub
Function CaptionValue() As Text
    Print "caption first"
    Return "ordered"
End Function
Function NumberValue() As Number
    Print "number second"
    Return 8
End Function
Function ChangeShared() As Number
    SharedValue = 9
    Return 10
End Function
Function Difference(LeftValue As Number, RightValue As Number) As Number
    Return LeftValue - RightValue
End Function
Function Never(LeftValue As Number, RightValue As Number) As Boolean
    Print "incorrect"
    Return True
End Function
""";
    private const string Expected = "ready:-2\nnamed:7\ncaption first\nnumber second\nordered:8\n6\n9\n-6\nFalse\n";

    [TestMethod]
    [TestCategory("MissionGuardrail")]
    public void Defaults_named_captures_multiline_declarations_and_unary_plus_preserve_meaning()
    {
        EvaluationResult result = new SmileEvaluator().Evaluate(Source);
        Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
        Assert.AreEqual(Expected, result.Output);
        SmileFormatResult formatted = SmileSourceFormatter.Format(Source);
        Assert.IsTrue(formatted.Success, string.Join("\n", formatted.Diagnostics));
        StringAssert.Contains(formatted.FormattedSource, "\n)\n");
        Assert.AreEqual(Expected, new SmileEvaluator().Evaluate(formatted.FormattedSource).Output);
    }

    [TestMethod]
    [DataRow("Sub Work(Optional Value As Number = 1, Required As Number)\nEnd Sub")]
    [DataRow("Sub Work(Optional Value As Number = 1 + 2)\nEnd Sub")]
    [DataRow("Sub Work(Optional Value As Number = True)\nEnd Sub")]
    [DataRow("Sub Work(Value As Number)\nEnd Sub\nCall Work(Value:=1, 2)")]
    [DataRow("Sub Work(Value As Number)\nEnd Sub\nCall Work(Unknown:=1)")]
    [DataRow("Sub Work(Value As Number)\nEnd Sub\nCall Work(1, Value:=2)")]
    [DataRow("Sub Work(Value As Number)\nEnd Sub\nCall Work()")]
    [DataRow("Print Text_Length(text:=\"a\")")]
    public void Invalid_argument_contracts_are_diagnosed(string source)
    {
        Assert.IsFalse(new SmileTranspiler().Bind(source).Success);
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
    public async Task Routine_arguments_execute_in_every_active_target(TargetLanguage target)
    {
        TranspileResult transpile = new SmileTranspiler().Transpile(Source, target);
        Assert.IsTrue(transpile.Success, string.Join("\n", transpile.Diagnostics));
        IToolchain toolchain = ToolchainRegistry.CreateDefault().Get(target);
        ToolchainStatus status = await toolchain.DetectAsync(CancellationToken.None);
        Assert.IsTrue(status.IsAvailable, status.Message);
        BuildRunResult run = await toolchain.BuildAndRunAsync(transpile.GeneratedProgram!, CancellationToken.None);
        Assert.IsTrue(run.Success, $"{target}: {run.BuildOutput}\n{run.StandardError}");
        Assert.AreEqual(Expected, run.StandardOutput.Replace("\r\n", "\n"), target.ToString());
    }
}
