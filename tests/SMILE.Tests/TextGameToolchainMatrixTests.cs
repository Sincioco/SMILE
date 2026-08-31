using SMILE.Engine;
using SMILE.Toolchains;
using System.Text.RegularExpressions;

namespace SMILE.Tests;

[TestClass]
[TestCategory("CoreBasic")]
[TestCategory("TextGameFoundation")]
[TestCategory("MilestoneMatrix")]
[DoNotParallelize]
public sealed class TextGameToolchainMatrixTests
{
    private const string Source = """
Option Explicit
Dim Board[2, 3] As Number
Dim Flags[2, 3] As Boolean
Dim Words[2, 3] As Text
Dim KeyCode As Number
Dim FixedRoll As Number
Dim AcrossZero As Number
Dim StartedAt As Number
Dim Elapsed As Number

Board[1, 2] = 7
Flags[1, 2] = True
Words[1, 2] = "ready"
Clear Screen
Get Key KeyCode
StartedAt = Timer()
Wait 45 Milliseconds
Elapsed = Timer() - StartedAt
Random FixedRoll From -2 To -2
Random AcrossZero From -3 To 3
Print Board[1, 2]; Flags[1, 2]; Words[1, 2]
Print KeyCode
Print Abs(-5); Min(9, 4); Max(-8, -2); FixedRoll
Print AcrossZero >= -3 And AcrossZero <= 3
Print Elapsed >= 20
""";

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
    public async Task Deterministic_text_game_fixture_builds_and_runs_warning_free(TargetLanguage language)
    {
        var transpiler = new SmileTranspiler();
        TranspileResult transpile = transpiler.Transpile(Source, language);
        Assert.IsTrue(transpile.Success, $"{language}: {Join(transpile.Diagnostics)}");

        IToolchain toolchain = ToolchainRegistry.CreateDefault().Get(language);
        ToolchainStatus status = await toolchain.DetectAsync(CancellationToken.None);
        Assert.IsTrue(status.IsAvailable, $"{language}: required toolchain unavailable — {status.Message}");

        BuildRunResult run = await toolchain.BuildAndRunAsync(
            transpile.GeneratedProgram!,
            CancellationToken.None);

        Assert.IsTrue(
            run.Success,
            $"{language}: {run.Stage}{Environment.NewLine}{run.BuildOutput}{Environment.NewLine}{run.StandardError}");
        Assert.AreEqual("7Trueready\n0\n54-2-2\nTrue\nTrue\n", Normalize(run.StandardOutput), language.ToString());
        Assert.IsFalse(HasCompilerWarning(run.BuildOutput),
            $"{language} emitted a compiler warning.{Environment.NewLine}{run.BuildOutput}");
        Console.WriteLine($"PASS text-game deterministic matrix / {language} / {run.Duration.TotalMilliseconds:F0} ms");
    }

    [TestMethod]
    [TestCategory("CobolFocused")]
    public async Task Cobol_dynamic_Text_preserves_spaces_through_variables_arrays_routines_and_concatenation()
    {
        const string source = """
Option Explicit
Dim Cell As Text
Dim Empty As Text
Dim Cells[1] As Text
Dim Grid[1, 1] As Text
Cell = " "
Cells[0] = " A "
Grid[0, 0] = " G "
Print "["; Cell; "]"
Print "["; Empty; "]"
Print "["; Cells[0]; "]"
Print "["; Grid[0, 0]; "]"
Print "["; Echo(" x "); "]"
Print "["; "a" + " " + "b"; "]"
If Cell = " " And Cell <> "" Then
    Print "[equal]"
End If
Select Case Cell
    Case ""
        Print "[wrong]"
    Case " "
        Print "[selected]"
End Select
Function Echo(Value As Text) As Text
    Return Value
End Function
""";
        var transpiler = new SmileTranspiler();
        TranspileResult transpile = transpiler.Transpile(source, TargetLanguage.Cobol);
        Assert.IsTrue(transpile.Success, Join(transpile.Diagnostics));

        IToolchain toolchain = ToolchainRegistry.CreateDefault().Get(TargetLanguage.Cobol);
        ToolchainStatus status = await toolchain.DetectAsync(CancellationToken.None);
        Assert.IsTrue(status.IsAvailable, $"COBOL: required toolchain unavailable — {status.Message}");

        BuildRunResult run = await toolchain.BuildAndRunAsync(
            transpile.GeneratedProgram!,
            CancellationToken.None);

        Assert.IsTrue(
            run.Success,
            $"COBOL: {run.Stage}{Environment.NewLine}{run.BuildOutput}{Environment.NewLine}{run.StandardError}");
        Assert.AreEqual(
            "[ ]\n[]\n[ A ]\n[ G ]\n[ x ]\n[a b]\n[equal]\n[selected]\n",
            Normalize(run.StandardOutput));
        Assert.IsFalse(HasCompilerWarning(run.BuildOutput),
            $"COBOL emitted a compiler warning.{Environment.NewLine}{run.BuildOutput}");
    }

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static bool HasCompilerWarning(string text) =>
        Regex.IsMatch(text, @"\bwarning(?:\s+[A-Z]+\d+|:)", RegexOptions.IgnoreCase);

    private static string Join(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.ToString()));
}
