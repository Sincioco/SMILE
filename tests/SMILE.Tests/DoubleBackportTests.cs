using SMILE.Engine;
using SMILE.Toolchains;

namespace SMILE.Tests;

[TestClass]
[TestCategory("DoubleBackport")]
public sealed class DoubleBackportTests
{
    private const string Source = """
Option Explicit
Const Half = 0.5
Const NegativeZero = -0.0
Dim Samples[2, 2] As Double
Dim Value As Double
Dim Shared As Double
Dim Double As Number
Dim math As Number
Dim smileCheckDouble As Number
Double = 6
math = 7
smileCheckDouble = 8
Print Double + math + smileCheckDouble
Value = ToDouble(3) / 2.0
Samples[1, 1] = Value + Half
Print Samples[1, 1]
Print ToNumber(-3.9)
Print Abs(-2.5); ":"; Min(-0.0, 0.0); ":"; Max(-0.0, 0.0)
Print Clamp(9.0, 1.0, 4.0); ":"; Sqrt(9.0)
Print Sin(0.0); ":"; Cos(0.0); ":"; Atan2(-0.0, 0.0)
Print Floor(-1.5); ":"; Ceiling(-1.5); ":"; Truncate(-1.5); ":"; Round(2.5); ":"; Round(3.5)
Print Text_From_Double(-0.0); ":"; Text_To_Double(" -1.25e1 ")
Print Add(Value:=Samples[1, 1]); ":"; Add()
Select Case Value
    Case 1.5
        Print "selected"
End Select
Print Text_From_Double(NegativeZero) = "-0.0"
Print 0.1 + 0.2 = 0.3
Print 1.0000000000000002 > 1.0
Print 5e-324 <> 0.0
Print Text_From_Double(-5e-324 * 0.5) = "-0.0"
Print Text_From_Double(Ceiling(-0.25)) = "-0.0"
Print Text_From_Double(Round(-0.5)) = "-0.0"
Print ToDouble(9007199254740993) = 9007199254740992.0
Shared = 1.0
Print Shared + Change()
Shared = 1.0
Print Shared; ":"; Min(9.0, Change())
Print Many(1.0, "mixed", 2, 3.0, 4.0)
Print ToNumber(-9223372036854775808.0)
Value = 1.0000000000000002
Select Case Value
    Case 1.0
        Print "wrong case"
    Case 1.0000000000000002
        Print "exact case"
End Select
Function Add(Optional Value As Double = Half) As Double
    Return Value + 0.25
End Function
Function Change() As Double
    Shared = 7.0
    Return 2.0
End Function
Function Many(FirstValue As Double, Caption As Text, CountValue As Number, FourthValue As Double, FifthValue As Double) As Double
    Print Caption
    Return FirstValue + ToDouble(CountValue) + FourthValue + FifthValue
End Function
""";

    private const string Expected = "21\n2.0\n-3\n2.5:-0.0:-0.0\n4.0:3.0\n0.0:1.0:-0.0\n-2.0:-1.0:-1.0:2.0:4.0\n-0.0:-12.5\n2.25:0.75\nselected\nTrue\nFalse\nTrue\nTrue\nTrue\nTrue\nTrue\nTrue\n3.0\n1.0:2.0\nmixed\n10.0\n-9223372036854775808\nexact case\n";

    [TestMethod]
    [TestCategory("MissionGuardrail")]
    public void Double_evaluator_matches_exact_type_and_signed_zero_contract()
    {
        EvaluationResult result = new SmileEvaluator().Evaluate(Source);
        Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
        Assert.AreEqual(Expected, result.Output);
        SmileFormatResult formatted = SmileSourceFormatter.Format(Source);
        Assert.IsTrue(formatted.Success, string.Join("\n", formatted.Diagnostics));
        Assert.AreEqual(Expected, new SmileEvaluator().Evaluate(formatted.FormattedSource).Output);
    }

    [TestMethod]
    [DataRow("Print .5")]
    [DataRow("Print 1.")]
    [DataRow("Print 1e+")]
    [DataRow("Print 1e309")]
    [DataRow("Print 1.0 + 1")]
    [DataRow("Print 1.0 Mod 1.0")]
    [DataRow("Dim Index As Double\nFor Index = 0.0 To 2.0\nNext")]
    [DataRow("Dim Value As Double\nValue = 1")]
    [DataRow("Const Bad = Sqrt(-1.0)")]
    [DataRow("Const Bad = 1.0 / 0.0")]
    [DataRow("Const Bad = Text_To_Double(\"NaN\")")]
    [DataRow("Const Bad = ToNumber(9223372036854775808.0)")]
    [DataRow("Const Bad = Clamp(1.0, 4.0, 2.0)")]
    public void Double_invalid_programs_are_rejected(string source) =>
        Assert.IsFalse(new SmileTranspiler().Bind(source).Success);

    [TestMethod]
    [DataRow(TargetLanguage.Cobol)]
    [DataRow(TargetLanguage.MasmX64)]
    [DataRow(TargetLanguage.CSharp)]
    [DataRow(TargetLanguage.C)]
    [DataRow(TargetLanguage.JavaScript)]
    [DataRow(TargetLanguage.Java)]
    [DataRow(TargetLanguage.ObjectiveC)]
    [DataRow(TargetLanguage.Swift)]
    [DataRow(TargetLanguage.Python)]
    [DataRow(TargetLanguage.Cpp)]
    public async Task Double_program_executes_with_native_target_arithmetic(TargetLanguage target)
    {
        TranspileResult transpile = new SmileTranspiler().Transpile(Source, target);
        Assert.IsTrue(transpile.Success, string.Join("\n", transpile.Diagnostics));
        BuildRunResult run = await ToolchainRegistry.CreateDefault().Get(target).BuildAndRunAsync(transpile.GeneratedProgram!, CancellationToken.None);
        Assert.IsTrue(run.Success, $"{target}: {run.BuildOutput}\n{run.StandardError}");
        Assert.AreEqual(Expected, run.StandardOutput.Replace("\r\n", "\n"), target.ToString());
        Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(run.BuildOutput, @"(?im)\bwarning(?:\s+[A-Z]+\d+)?\s*:"), run.BuildOutput);
    }

    [TestMethod]
    [DataRow(TargetLanguage.CSharp)]
    [DataRow(TargetLanguage.C)]
    [DataRow(TargetLanguage.Cpp)]
    [DataRow(TargetLanguage.ObjectiveC)]
    [DataRow(TargetLanguage.JavaScript)]
    [DataRow(TargetLanguage.Java)]
    [DataRow(TargetLanguage.MasmX64)]
    [DataRow(TargetLanguage.Cobol)]
    [DataRow(TargetLanguage.Swift)]
    [DataRow(TargetLanguage.Python)]
    public async Task Double_runtime_failures_report_the_source_line(TargetLanguage target)
    {
        foreach ((string argument, string inputType, string resultType, string expression) in new[]
        {
            ("0.0", "Double", "Double", "1.0 / Value"),
            ("1e308", "Double", "Double", "Value * 2.0"),
            ("9223372036854775808.0", "Double", "Number", "ToNumber(Value)"),
            ("\"1.0junk\"", "Text", "Double", "Text_To_Double(Value)"),
            ("-1.0", "Double", "Double", "Sqrt(Value)"),
            ("1.0", "Double", "Double", "Clamp(Value, 4.0, 2.0)")
        })
        {
            string source = $"Print \"before\"\nPrint Crash({argument})\nFunction Crash(Value As {inputType}) As {resultType}\n    Return {expression}\nEnd Function";
            EvaluationResult evaluated = new SmileEvaluator().Evaluate(source);
            Assert.IsFalse(evaluated.Success);
            Assert.AreEqual("before\n", evaluated.Output);
            TranspileResult transpile = new SmileTranspiler().Transpile(source, target);
            Assert.IsTrue(transpile.Success, string.Join("\n", transpile.Diagnostics));
            BuildRunResult run = await ToolchainRegistry.CreateDefault().Get(target).BuildAndRunAsync(transpile.GeneratedProgram!, CancellationToken.None);
            Assert.IsFalse(run.Success, $"{target} accepted {expression}.");
            Assert.AreEqual("before\n", run.StandardOutput.Replace("\r\n", "\n"), $"{target}/{expression}");
            StringAssert.Contains(run.StandardError, "SMILER3902", $"{target}/{expression}: {run.BuildOutput}\n{run.StandardError}");
            StringAssert.Contains(run.StandardError, "line 4", $"{target}/{expression}: {run.StandardError}");
        }
    }

    [TestMethod]
    [TestCategory("MissionGuardrail")]
    public void Folded_Double_constants_do_not_emit_runtime_support_and_names_remain_contextual()
    {
        TranspileResult division = new SmileTranspiler().Transpile("Print 1.0 / 2.0", TargetLanguage.Python);
        Assert.IsTrue(division.Success);
        Assert.IsFalse(division.GeneratedProgram!.Files.Any(file => file.Content.Contains("def _smile_div", StringComparison.Ordinal)));
        const string source = "Const Caption = Text_From_Double(1.25)\nPrint Caption\nPrint Double(3)\nFunction Double(Value As Number) As Number\n    Return Value + 1\nEnd Function";
        Assert.AreEqual("1.25\n4\n", new SmileEvaluator().Evaluate(source).Output);
        foreach (TargetLanguage target in ActiveTargetLanguages.All)
        {
            TranspileResult result = new SmileTranspiler().Transpile(source, target);
            Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
            Assert.IsFalse(result.GeneratedProgram!.Files.Any(file => file.Content.Contains("smile_text_allocations", StringComparison.Ordinal) || file.Content.Contains("check_double", StringComparison.Ordinal)));
        }
    }
}
