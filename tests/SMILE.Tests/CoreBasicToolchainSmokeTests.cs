using System.Text.RegularExpressions;
using SMILE.Engine;
using SMILE.Toolchains;

namespace SMILE.Tests;

[TestClass]
[TestCategory("CoreBasic")]
[TestCategory("Toolchain")]
public sealed class CoreBasicToolchainSmokeTests
{
    private const string Source = """
Const Prefix = "Core="
Total = 0
For I = 1 To 3
    Total = Total + I
End For
Print "Final I="; I
For Reverse = 2 Down To 1
End For
Print "Final Reverse="; Reverse
Do
    Total = Total - 1
Loop Until Total = 0
If Total = 0 Then
    Print Prefix; Total
End If
For Outer = 1 To 2
    Do
        Print Outer;
        If Outer = 1 Then
            Exit For
        End If
    Loop Until Outer = 2
End For
Do
    For Inner = 2 Down To 1
        Print Inner;
        If Inner = 2 Then
            Exit Do
        End If
    End For
Loop Until Inner = 1
Print
End Program
Print "unreachable marker"
""";

    [TestMethod]
    [DoNotParallelize]
    public async Task Installed_active_targets_build_run_and_match_the_evaluator()
    {
        EvaluationResult expected = new SmileEvaluator().Evaluate(Source);
        Assert.IsTrue(expected.Success);
        var transpiler = new SmileTranspiler();
        ToolchainRegistry toolchains = ToolchainRegistry.CreateDefault();

        foreach (TargetLanguage language in ActiveTargetLanguages.All)
        {
            IToolchain toolchain = toolchains.Get(language);
            ToolchainStatus status = await toolchain.DetectAsync(CancellationToken.None);
            Console.WriteLine($"{language}: {(status.IsAvailable ? "available" : "unavailable")} — {status.Message}");
            if (!status.IsAvailable)
            {
                continue;
            }

            TranspileResult transpile = transpiler.Transpile(Source, language);
            Assert.IsTrue(transpile.Success, language + ": " + Join(transpile.Diagnostics));
            BuildRunResult run = await toolchain.BuildAndRunAsync(
                transpile.GeneratedProgram!,
                CancellationToken.None);

            Assert.IsTrue(
                run.Success,
                $"{language}: {run.Stage}{Environment.NewLine}{run.BuildOutput}{Environment.NewLine}{run.StandardError}");
            Assert.AreEqual(expected.Output, Normalize(run.StandardOutput), language.ToString());
            Assert.IsFalse(
                Regex.IsMatch(run.BuildOutput, @"\bwarning\s+[A-Z]+\d+\b", RegexOptions.IgnoreCase),
                $"{language} emitted a compiler warning.{Environment.NewLine}{run.BuildOutput}");
        }
    }

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static string Join(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.ToString()));
}
