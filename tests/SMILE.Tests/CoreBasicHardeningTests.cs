using SMILE.Engine;
using SMILE.Toolchains;
using System.Text.RegularExpressions;

namespace SMILE.Tests;

[TestClass]
[TestCategory("CoreBasic")]
[TestCategory("CoreBasicHardening")]
[DoNotParallelize]
public sealed class CoreBasicHardeningTests
{
    private const string ControlFlowFixture = """
Option Explicit

Dim Calls As Number
Dim Total As Number
Dim Index As Number
Dim Roll As Number

Clear Screen
Wait -1 Milliseconds
Random Roll From 10 To 2
Print Roll

Select Case NextValue()
    Case Else
        Print "Fallback"
End Select

Select Case NextValue()
End Select

For Index = 1 To 3
    Do
        Select Case Index
            Case 1
                Total = Total + 1
                Exit For
        End Select

        Exit Do
    Loop
End For

Print Total

Total = 0

Do
    For Index = 1 To 3
        Select Case Index
            Case 2
                Total = Total + Index
                Exit Do
            Case Else
                Total = Total + 10
        End Select
    End For
Loop

Print Total
Print Calls

Function NextValue() As Number
    Calls = Calls + 1
    Return Calls
End Function
""";

    private const string ExpectedOutput = "10\nFallback\n1\n12\n2\n";

    [TestMethod]
    [TestCategory("MissionGuardrail")]
    public void Select_fallback_empty_selector_and_cross_kind_exits_have_exact_evaluator_semantics()
    {
        EvaluationResult result = new SmileEvaluator().Evaluate(ControlFlowFixture);

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        Assert.AreEqual(ExpectedOutput, result.Output);
    }

    [TestMethod]
    public void Every_generator_handles_only_fallback_empty_select_and_cross_kind_exit_through_select()
    {
        var transpiler = new SmileTranspiler();

        foreach (TranspileResult result in transpiler.TranspileMany(ControlFlowFixture, ActiveTargetLanguages.All))
        {
            Assert.IsTrue(result.Success, $"{result.Language}: {Join(result.Diagnostics)}");
            Assert.IsNotNull(result.GeneratedProgram);
            Assert.EndsWith("\n", result.GeneratedProgram.PrimaryFile.Content);
        }

        string python = transpiler.Transpile(ControlFlowFixture, TargetLanguage.Python)
            .GeneratedProgram!.PrimaryFile.Content;
        StringAssert.Contains(python, "class _SmileExitLoop1(Exception):");
        StringAssert.Contains(python, "class _SmileExitLoop3(Exception):");
        StringAssert.Contains(python, "raise _SmileExitLoop1()");
        StringAssert.Contains(python, "raise _SmileExitLoop3()");
    }

    [TestMethod]
    [TestCategory("MissionGuardrail")]
    public void Runtime_authority_homes_the_cursor_returns_reversed_lower_bound_and_clamps_wait_once()
    {
        var host = new ScriptedSmileEvaluationHost(randomValues: new long[] { 99 });
        const string source = """
Option Explicit
Dim Roll As Number
Print "Frame"
Clear Screen
Wait -1 Milliseconds
Wait 9223372036854775807 Milliseconds
Random Roll From 10 To 2
Print Roll
""";

        EvaluationResult result = new SmileEvaluator().Evaluate(
            source,
            new SmileEvaluationOptions(host, StatementBudget: 100));

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        Assert.AreEqual("Frame\n10\n", result.Output);
        CollectionAssert.AreEqual(
            new long[] { 0, SmileRuntimeRules.MaximumWaitMilliseconds },
            host.Waits.ToArray());
        CollectionAssert.AreEqual(new[] { "Frame\n" }, host.ScreenFrames.ToArray());
        Assert.AreEqual(1, host.RemainingRandomCount, "Reversed bounds must not consume randomness.");
    }

    [TestMethod]
    public void Every_target_structurally_uses_cursor_home_reversed_lower_and_uint32_wait_limit()
    {
        const string source = """
Option Explicit
Dim Roll As Number
Clear Screen
Wait 9223372036854775807 Milliseconds
Random Roll From 10 To 2
Print Roll
""";
        var transpiler = new SmileTranspiler();

        foreach (TranspileResult result in transpiler.TranspileMany(source, ActiveTargetLanguages.All))
        {
            Assert.IsTrue(result.Success, $"{result.Language}: {Join(result.Diagnostics)}");
            string generated = string.Join("\n", result.GeneratedProgram!.Files.Select(file => file.Content));
            Assert.IsFalse(generated.Contains("SMILER1221", StringComparison.Ordinal));
            Assert.IsFalse(generated.Contains("[2J", StringComparison.Ordinal));
            Assert.IsFalse(generated.Contains("Console.Clear", StringComparison.Ordinal));
            Assert.IsFalse(generated.Contains("FillConsoleOutput", StringComparison.Ordinal));
        }
    }

    [TestMethod]
    [TestCategory("MilestoneMatrix")]
    public async Task Control_flow_hardening_fixture_builds_and_runs_on_all_ten_targets()
    {
        var transpiler = new SmileTranspiler();
        ToolchainRegistry toolchains = ToolchainRegistry.CreateDefault();

        foreach (TargetLanguage language in ActiveTargetLanguages.All)
        {
            IToolchain toolchain = toolchains.Get(language);
            ToolchainStatus status = await toolchain.DetectAsync(CancellationToken.None);
            Assert.IsTrue(status.IsAvailable, $"{language}: required toolchain unavailable — {status.Message}");

            TranspileResult transpile = transpiler.Transpile(ControlFlowFixture, language);
            Assert.IsTrue(transpile.Success, $"{language}: {Join(transpile.Diagnostics)}");

            BuildRunResult run = await toolchain.BuildAndRunAsync(
                transpile.GeneratedProgram!,
                CancellationToken.None);
            Assert.IsTrue(
                run.Success,
                $"{language}: {run.Stage}{Environment.NewLine}{run.BuildOutput}{Environment.NewLine}{run.StandardError}");
            Assert.AreEqual(ExpectedOutput, Normalize(run.StandardOutput), language.ToString());
            Assert.IsFalse(
                Regex.IsMatch(run.BuildOutput, @"\bwarning(?:\s+[A-Z]+\d+|:)", RegexOptions.IgnoreCase),
                $"{language} emitted a compiler warning.{Environment.NewLine}{run.BuildOutput}");

            Console.WriteLine($"PASS control-flow hardening / {language}");
        }
    }

    [TestMethod]
    [TestCategory("GeneratedFormatting")]
    public void Every_generated_file_obeys_the_shared_layout_invariants()
    {
        const string source = """
Option Explicit
Dim Value As Number
Value = 1
Print "Value="; Value
Select Case Value
    Case 1
        Print "one"
    Case Else
        Print "other"
End Select
Call Present(Value)
Sub Present(ByVal Item As Number)
Print Item
End Sub
""";

        foreach (TranspileResult result in new SmileTranspiler().TranspileMany(source, ActiveTargetLanguages.All))
        {
            Assert.IsTrue(result.Success, $"{result.Language}: {Join(result.Diagnostics)}");
            foreach (GeneratedFile file in result.GeneratedProgram!.Files)
            {
                string content = file.Content;
                Assert.AreNotEqual('\n', content[0], $"{result.Language}/{file.RelativePath}: leading blank line");
                Assert.EndsWith("\n", content, $"{result.Language}/{file.RelativePath}: final newline");
                Assert.IsFalse(content.EndsWith("\n\n", StringComparison.Ordinal),
                    $"{result.Language}/{file.RelativePath}: trailing blank line");
                Assert.AreEqual(-1, content.IndexOf('\r'), $"{result.Language}/{file.RelativePath}: non-LF line ending");
                Assert.IsFalse(Regex.IsMatch(content, @"(?m)[ \t]+$"),
                    $"{result.Language}/{file.RelativePath}: trailing whitespace");
                string excessiveBlanks = result.Language is TargetLanguage.Python ? "\n\n\n\n" : "\n\n\n";
                Assert.IsFalse(content.Contains(excessiveBlanks, StringComparison.Ordinal),
                    $"{result.Language}/{file.RelativePath}: excessive blank lines");
            }
        }
    }

    [TestMethod]
    [TestCategory("GeneratedIdiomaticity")]
    public void Eligible_selects_use_native_target_constructs_without_changing_selector_rules()
    {
        const string source = """
Option Explicit
Dim NumberChoice As Number
Dim TextChoice As Text
NumberChoice = 2
TextChoice = "B"
Select Case NumberChoice
    Case 1
        Print "one"
    Case 2
        Print "two"
    Case Else
        Print "other"
End Select
Select Case TextChoice
    Case "A"
        Print "A"
    Case "B"
        Print "B"
    Case Else
        Print "other"
End Select
""";
        var transpiler = new SmileTranspiler();

        StringAssert.Contains(Generate(transpiler, source, TargetLanguage.CSharp), "switch (_smileSelect1)");
        StringAssert.Contains(Generate(transpiler, source, TargetLanguage.JavaScript), "switch (_smileSelect1)");
        StringAssert.Contains(Generate(transpiler, source, TargetLanguage.C), "switch (_smileSelect1)");
        StringAssert.Contains(Generate(transpiler, source, TargetLanguage.ObjectiveC), "switch (_smileSelect1)");
        StringAssert.Contains(Generate(transpiler, source, TargetLanguage.Cpp), "switch (_smileSelect1)");
        StringAssert.Contains(Generate(transpiler, source, TargetLanguage.Swift), "switch _smileSelect1 {");
        StringAssert.Contains(Generate(transpiler, source, TargetLanguage.Python), "match _smileSelect1:");
        StringAssert.Contains(Generate(transpiler, source, TargetLanguage.Java), "switch (_smileSelect2)");
        StringAssert.Contains(Generate(transpiler, source, TargetLanguage.Cobol), "EVALUATE SMILE-TEMP-");
        StringAssert.Contains(Generate(transpiler, source, TargetLanguage.MasmX64), "cmp rax, 1");
    }

    [TestMethod]
    [TestCategory("MilestoneMatrix")]
    [TestCategory("GeneratedIdiomaticity")]
    public async Task Idiomatic_print_preserves_exactly_once_order_on_all_ten_targets()
    {
        const string source = """
Option Explicit
Dim Calls As Number
Print
Print NextValue(); True; "["; "X"; "]"
Print "A"; NextValue();
Print "!"
Print Calls
Function NextValue() As Number
Calls = Calls + 1
Return Calls
End Function
""";
        const string expected = "\n1True[X]\nA2!\n2\n";
        var transpiler = new SmileTranspiler();
        ToolchainRegistry toolchains = ToolchainRegistry.CreateDefault();

        foreach (TargetLanguage language in ActiveTargetLanguages.All)
        {
            TranspileResult transpile = transpiler.Transpile(source, language);
            Assert.IsTrue(transpile.Success, $"{language}: {Join(transpile.Diagnostics)}");
            BuildRunResult run = await toolchains.Get(language).BuildAndRunAsync(
                transpile.GeneratedProgram!,
                CancellationToken.None);
            Assert.IsTrue(run.Success,
                $"{language}: {run.BuildOutput}{Environment.NewLine}{run.StandardError}");
            Assert.AreEqual(expected, Normalize(run.StandardOutput), language.ToString());
            Assert.IsFalse(
                Regex.IsMatch(run.BuildOutput, @"\bwarning(?:\s+[A-Z]+\d+|:)", RegexOptions.IgnoreCase),
                $"{language} emitted a compiler warning.{Environment.NewLine}{run.BuildOutput}");
        }
    }

    [TestMethod]
    [TestCategory("TextLifetime")]
    public async Task C_family_owned_text_is_bounded_and_fully_released()
    {
        const string source = """
Option Explicit
Dim Index As Number
Dim Message As Text
Dim Values[4] As Text
Message = ""
For Index = 1 To 50000
    Message = "Frame " + "Value"
    Values[Index Mod 4] = "[" + Message + "]"
End For
Print Message
Print ("A" + "B") + ("C" + "D")
Print MakeText("X")
Call StopNow()
Print "unreachable"
Function MakeText(ByVal Value As Text) As Text
Dim Position As Number
Dim LocalValues[2] As Text
For Position = 0 To 1
    LocalValues[Position] = "[" + Value + "]"
    Exit For
End For
Select Case Value
    Case "X"
        Return LocalValues[0]
    Case Else
        Return "other"
End Select
End Function
Sub StopNow()
Dim LastMessage As Text
LastMessage = "Done" + "!"
End Program
End Sub
""";
        const string expected = "Frame Value\nABCD\n[X]\n";
        var transpiler = new SmileTranspiler();
        ToolchainRegistry toolchains = ToolchainRegistry.CreateDefault();
        string? priorReport = Environment.GetEnvironmentVariable("SMILE_TEXT_LIFETIME_REPORT");
        Environment.SetEnvironmentVariable("SMILE_TEXT_LIFETIME_REPORT", "1");
        try
        {
            foreach (TargetLanguage language in new[]
            {
                TargetLanguage.C,
                TargetLanguage.ObjectiveC,
                TargetLanguage.MasmX64
            })
            {
                TranspileResult transpile = transpiler.Transpile(source, language);
                Assert.IsTrue(transpile.Success, $"{language}: {Join(transpile.Diagnostics)}");
                BuildRunResult run = await toolchains.Get(language).BuildAndRunAsync(
                    transpile.GeneratedProgram!,
                    CancellationToken.None);
                Assert.IsTrue(run.Success,
                    $"{language}: stage={run.Stage}, exit={run.ExitCode}, timedOut={run.TimedOut}" +
                    $"{Environment.NewLine}{run.BuildOutput}{Environment.NewLine}{run.StandardOutput}" +
                    $"{Environment.NewLine}{run.StandardError}");
                Assert.AreEqual(expected, Normalize(run.StandardOutput), language.ToString());

                Match report = Regex.Match(
                    run.StandardError,
                    @"SMILE Text lifetime: allocations=(\d+) frees=(\d+) live=(\d+) peak=(\d+)");
                Assert.IsTrue(report.Success, $"{language}: missing lifetime report.{Environment.NewLine}{run.StandardError}");
                Assert.AreEqual(report.Groups[1].Value, report.Groups[2].Value, $"{language}: allocations != frees");
                Assert.AreEqual("0", report.Groups[3].Value, $"{language}: live allocations remain");
                Assert.IsLessThan(32L, long.Parse(report.Groups[4].Value), $"{language}: peak grew unexpectedly");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("SMILE_TEXT_LIFETIME_REPORT", priorReport);
        }
    }

    [TestMethod]
    [TestCategory("GeneratedFormatting")]
    public void Nontrivial_runtime_helpers_are_expanded_and_feature_gated()
    {
        const string source = """
Option Explicit
Dim KeyCode As Number
Dim Roll As Number
Get Key KeyCode
Clear Screen
Wait 0 Milliseconds
Random Roll From 1 To 2
Print Timer(); Abs(-1); Min(1, 2); Max(1, 2); Roll
""";
        var transpiler = new SmileTranspiler();
        string csharp = Generate(transpiler, source, TargetLanguage.CSharp);
        string c = Generate(transpiler, source, TargetLanguage.C);
        string javascript = Generate(transpiler, source, TargetLanguage.JavaScript);
        string java = Generate(transpiler, source, TargetLanguage.Java);
        string swift = Generate(transpiler, source, TargetLanguage.Swift);

        Assert.IsFalse(csharp.Contains("if (!Console.KeyAvailable) return", StringComparison.Ordinal));
        Assert.IsFalse(c.Contains("switch (key) {", StringComparison.Ordinal));
        Assert.IsFalse(javascript.Contains("smileInputStarted = true; process", StringComparison.Ordinal));
        Assert.IsFalse(javascript.Contains("function smileGetKey() { smileStartInput();", StringComparison.Ordinal));
        Assert.IsFalse(java.Contains("return switch (key) {", StringComparison.Ordinal));
        Assert.IsFalse(swift.Contains("if key == 0 || key == 224 { if", StringComparison.Ordinal));

        foreach (TargetLanguage language in ActiveTargetLanguages.All)
        {
            string hello = Generate(transpiler, "Print \"Hello\"", language);
            Assert.IsFalse(hello.Contains("smileRandom", StringComparison.OrdinalIgnoreCase), language.ToString());
            Assert.IsFalse(hello.Contains("smile_get_key", StringComparison.OrdinalIgnoreCase), language.ToString());
        }
    }

    private static string Generate(SmileTranspiler transpiler, string source, TargetLanguage language)
    {
        TranspileResult result = transpiler.Transpile(source, language);
        Assert.IsTrue(result.Success, $"{language}: {Join(result.Diagnostics)}");
        return result.GeneratedProgram!.PrimaryFile.Content;
    }

    private static string Join(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.ToString()));

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
}
