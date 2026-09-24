using SMILE.Engine;
using SMILE.Toolchains;

namespace SMILE.Tests;

[TestClass]
[TestCategory("EnumBackport")]
public sealed class EnumBackportTests
{
    private const string Source = """
Option Explicit
Enum State
    None
    Ready = BASE_VALUE
    Alias = 7
    Maximum = 9223372036854775807
    Minimum = -9223372036854775807 - 1
End Enum
Enum Direction
    Left = -1
    Right = -1
    Up = Abs(-10)
    Down
End Enum
Enum NativeNames
    value
    mro
    rawValue
    __proto__
    _missing_
    _
    Text
    Key
End Enum
Const BASE_VALUE = 7
Const DEFAULT_STATE = State.Alias
Dim Selected As State
Dim States[2, 2] As State
Dim Heading As Direction
Dim Headings[2] As Direction
Print Selected = State.None; ":"; States[1, 1] = State.None
Print Heading = Direction.Left; ":"; Heading = Headings[1]
Print NativeNames.value <> NativeNames.mro; ":"; NativeNames.rawValue <> NativeNames.__proto__
Print NativeNames._missing_ <> NativeNames._; ":"; NativeNames.Text <> NativeNames.Key
Selected = DEFAULT_STATE
Print Selected = State.Ready; ":"; Direction.Left = Direction.Right
Call Store(States[1, 0])
Print States[1, 0] = State.Ready
Call Aliases(SecondValue:=Selected, FirstValue:=Selected)
Print Selected = State.Maximum
Call CopyValue(Selected)
Print Selected = State.Maximum
Print ReadValue() = State.Minimum
Select Case States[1, 0]
Case State.Alias
    Print "selected"
Case Else
    Print "wrong"
End Select
Call LocalValues()
Sub Store(ByRef Value As State, Optional NewValue As State = State.Ready)
    Value = NewValue
End Sub
Sub Aliases(ByRef FirstValue As State, ByRef SecondValue As State)
    FirstValue = State.Minimum
    Print SecondValue = State.Minimum
    Call Store(SecondValue, State.Maximum)
End Sub
Sub CopyValue(Value As State)
    Call Store(Value)
    Print Value = State.Ready
End Sub
Function ReadValue(Optional Value As State = State.Minimum) As State
    Return Value
End Function
Sub LocalValues()
    Dim LocalStates[2] As State
    Dim LocalHeadings[2, 2] As Direction
    Dim LocalHeading As Direction
    Print LocalStates[0] = State.None; ":"; LocalHeadings[1, 0] = LocalHeading
    Call Store(LocalStates[1], ReadValue())
    Print LocalStates[1] = State.Minimum
End Sub
""";

    private const string Expected = "True:True\nFalse:True\nTrue:True\nTrue:True\nTrue:True\nTrue\nTrue\nTrue\nTrue\nTrue\nTrue\nselected\nTrue:True\nTrue\n";

    [TestMethod]
    [TestCategory("MissionGuardrail")]
    public void Enums_preserve_nominal_identity_aliases_zero_and_routine_locations()
    {
        EvaluationResult result = new SmileEvaluator().Evaluate(Source);
        Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
        Assert.AreEqual(Expected, result.Output);
        SmileFormatResult formatted = SmileSourceFormatter.Format(Source);
        Assert.IsTrue(formatted.Success, string.Join("\n", formatted.Diagnostics));
        StringAssert.Contains(formatted.FormattedSource, "Enum State\n    None\n");
        Assert.AreEqual(Expected, new SmileEvaluator().Evaluate(formatted.FormattedSource).Output);
    }

    [TestMethod]
    [DataRow("Enum Empty\nEnd Enum", "SMILE3421")]
    [DataRow("Enum Test\nOne\none\nEnd Enum", "SMILE3421")]
    [DataRow("Enum Test\nMaximum = 9223372036854775807\nNextValue\nEnd Enum", "SMILE3422")]
    [DataRow("Enum Test\nValue = TOO_LARGE\nEnd Enum\nConst TOO_LARGE = 9223372036854775807 + 1", "SMILE3422")]
    [DataRow("Enum Test\nValue = +1\nEnd Enum", "SMILE3422")]
    [DataRow("Enum Test\nValue = Test.Other\nOther\nEnd Enum", "SMILE3422")]
    [DataRow("Enum Test\nValue = CYCLIC\nEnd Enum\nConst CYCLIC = CYCLIC", "SMILE3422")]
    [DataRow("Enum Test\nValue\nEnd Enum\nDim Item As Test\nItem = 0", "SMILE2106")]
    [DataRow("Enum First\nValue\nEnd Enum\nEnum Second\nValue\nEnd Enum\nPrint First.Value = Second.Value", "SMILE2114")]
    [DataRow("Enum Test\nValue\nEnd Enum\nPrint Test.Value + Test.Value", "SMILE2114")]
    [DataRow("Enum Test\nValue\nEnd Enum\nPrint Test.Value < Test.Value", "SMILE2114")]
    [DataRow("Enum Test\nValue\nEnd Enum\nPrint Test.Value", "SMILE3424")]
    [DataRow("Enum Test\nValue\nAlias = 0\nEnd Enum\nSelect Case Test.Value\nCase Test.Value\nCase Test.Alias\nEnd Select", "SMILE2141")]
    [DataRow("Enum Test\nValue\nEnd Enum\nPrint Test.Missing = Test.Value", "SMILE3423")]
    [DataRow("If True Then\nEnum Test\nValue\nEnd Enum\nEnd If", "SMILE3420")]
    public void Enum_errors_are_diagnosed(string source, string code)
    {
        BindResult result = new SmileTranspiler().Bind(source);
        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Diagnostics.Any(item => item.Code == code), string.Join("\n", result.Diagnostics));
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
    public async Task Enum_program_executes_on_every_target(TargetLanguage target)
    {
        TranspileResult transpile = new SmileTranspiler().Transpile(Source, target);
        Assert.IsTrue(transpile.Success, string.Join("\n", transpile.Diagnostics));
        BuildRunResult run = await ToolchainRegistry.CreateDefault().Get(target).BuildAndRunAsync(transpile.GeneratedProgram!, CancellationToken.None);
        Assert.IsTrue(run.Success, $"{target}: {run.BuildOutput}\n{run.StandardError}");
        Assert.AreEqual(Expected, run.StandardOutput.Replace("\r\n", "\n"), target.ToString());
    }
}
