using SMILE.Engine;
using SMILE.Toolchains;

namespace SMILE.Tests;

[TestClass]
[TestCategory("TextInspection")]
public sealed class TextInspectionTests
{
    private const string Source = """
Option Explicit
Dim Caption As Text
Dim Saved[2] As Text
Dim smileTextSlice As Number
Dim ord As Number
smileTextSlice = 2
ord = 1
Print smileTextSlice + ord
Caption = "A😀é尾 "
Print Text_Length(Caption)
Print Text_Code_At(Caption, 1)
Print Text_Code_At(Caption, 3)
Print "["; Text_Slice(Caption, 1, 3); "]"
Print "["; Text_Slice(Caption, 4, 9223372036854775807); "]"
Print Text_Code_At(Caption, -1); ":"; Text_Code_At(Caption, 9223372036854775807)
Print "["; Text_Slice(Caption, -1, 2); Text_Slice(Caption, 0, 0); Text_Slice(Caption, 99, 1); "]"
Saved[0] = Text_Slice(Caption, 1, 1)
Saved[1] = Echo(Text_Slice(Caption, 4, 2))
Print "["; Saved[0]; Saved[1]; "]"
Print Text_Length(""); ":"; Text_Code_At("", 0)
Print "["; Text_Slice("", 0, 1); "]"
Caption = Text_Slice(SourceText(), StartIndex(), SliceCount())
Print "["; Caption; "]"
Function Echo(Value As Text) As Text
    Return Value
End Function
Function SourceText() As Text
    Print "text"
    Return "x😀y"
End Function
Function StartIndex() As Number
    Print "start"
    Return 1
End Function
Function SliceCount() As Number
    Print "count"
    Return 1
End Function
""";

    private const string Expected = "3\n6\n128512\n769\n[😀é]\n[尾 ]\n-1:-1\n[]\n[😀尾 ]\n0:-1\n[]\ntext\nstart\ncount\n[😀]\n";

    [TestMethod]
    [TestCategory("MissionGuardrail")]
    public void Unicode_scalars_bounds_and_argument_order_match_the_authority()
    {
        EvaluationResult result = new SmileEvaluator().Evaluate(Source);
        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        Assert.AreEqual(Expected, result.Output);
        foreach (TargetLanguage target in ActiveTargetLanguages.All)
        {
            TranspileResult generated = new SmileTranspiler().Transpile(Source, target);
            Assert.IsTrue(generated.Success, $"{target}: {Join(generated.Diagnostics)}");
        }
    }

    [TestMethod]
    public void Text_inspection_uses_native_length_without_a_helper()
    {
        string python = new SmileTranspiler().Transpile("Print Text_Length(\"😀\")", TargetLanguage.Python).GeneratedProgram!.PrimaryFile.Content;
        StringAssert.Contains(python, "len(");
        Assert.IsFalse(python.Contains("def ", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("Print Text_Length(1)")]
    [DataRow("Print Text_Code_At(\"a\", True)")]
    [DataRow("Const Bad = Text_Slice(\"a\")")]
    [DataRow("Const Bad = Text_Length(\"a\")")]
    [DataRow("Print Text_Length(\"a\", 1)")]
    [DataRow("Print Text_Slice(\"a\", 0, \"1\")")]
    public void Invalid_calls_return_diagnostics(string source)
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
    public async Task Text_functions_build_and_execute_in_each_active_target(TargetLanguage target)
    {
        TranspileResult transpile = new SmileTranspiler().Transpile(Source, target);
        Assert.IsTrue(transpile.Success, Join(transpile.Diagnostics));
        IToolchain toolchain = ToolchainRegistry.CreateDefault().Get(target);
        ToolchainStatus status = await toolchain.DetectAsync(CancellationToken.None);
        Assert.IsTrue(status.IsAvailable, status.Message);
        BuildRunResult run = await toolchain.BuildAndRunAsync(transpile.GeneratedProgram!, CancellationToken.None);
        Assert.IsTrue(run.Success, $"{target}: {run.BuildOutput}\n{run.StandardError}");
        Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(run.BuildOutput, @"(?im)\bwarning(?:\s+[A-Z]+\d+)?\s*:"), run.BuildOutput);
        Assert.AreEqual(Expected, run.StandardOutput.Replace("\r\n", "\n"), target.ToString());
    }

    [TestMethod]
    [TestCategory("MilestoneMatrix")]
    [DataRow(TargetLanguage.MasmX64)]
    [DataRow(TargetLanguage.C)]
    [DataRow(TargetLanguage.ObjectiveC)]
    [DataRow(TargetLanguage.Cpp)]
    [DataRow(TargetLanguage.Cobol)]
    public async Task Native_scalar_inspection_without_slice_needs_no_allocation_runtime(TargetLanguage target)
    {
        TranspileResult transpile = new SmileTranspiler().Transpile("Print Text_Length(\"A😀B\"); Text_Code_At(\"A😀B\", 1)", target);
        Assert.IsTrue(transpile.Success, Join(transpile.Diagnostics));
        Assert.IsFalse(transpile.GeneratedProgram!.Files.Any(file => file.Content.Contains("smile_text_allocations", StringComparison.Ordinal)));
        BuildRunResult run = await ToolchainRegistry.CreateDefault().Get(target).BuildAndRunAsync(transpile.GeneratedProgram!, CancellationToken.None);
        Assert.IsTrue(run.Success, $"{target}: {run.BuildOutput}\n{run.StandardError}");
        Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(run.BuildOutput, @"(?im)\bwarning(?:\s+[A-Z]+\d+)?\s*:"), run.BuildOutput);
        Assert.AreEqual("3128512\n", run.StandardOutput.Replace("\r\n", "\n"));
    }

    private static string Join(IEnumerable<Diagnostic> diagnostics) => string.Join("\n", diagnostics);
}
