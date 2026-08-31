using SMILE.Engine;
using SMILE.Toolchains;
using System.IO;
using System.Text.RegularExpressions;

namespace SMILE.Tests;

[TestClass]
[TestCategory("CoreBasic")]
[TestCategory("TextGameFoundation")]
[TestCategory("InteractiveMatrix")]
[DoNotParallelize]
public sealed class TextGameInteractiveMatrixTests
{
    private const string InteractiveContractSource = """
Option Explicit
Dim KeyCode As Number
Dim StartedAt As Number
Dim ProvedNoInput As Boolean

Print "SMILE INTERACTIVE STALE"
Clear Screen
Print "SMILE INTERACTIVE READY"
StartedAt = Timer()
ProvedNoInput = False

Do
    Get Key KeyCode
    If KeyCode = KEY_NONE And Not ProvedNoInput And Timer() - StartedAt >= 40 Then
        Print "KEY_NONE"
        ProvedNoInput = True
    Else If KeyCode <> KEY_NONE Then
        Select Case KeyCode
            Case KEY_W
                Print "KEY_W"
            Case KEY_A
                Print "KEY_A"
            Case KEY_S
                Print "KEY_S"
            Case KEY_D
                Print "KEY_D"
            Case KEY_UP
                Print "KEY_UP"
            Case KEY_DOWN
                Print "KEY_DOWN"
            Case KEY_LEFT
                Print "KEY_LEFT"
            Case KEY_RIGHT
                Print "KEY_RIGHT"
            Case KEY_ENTER
                Print "KEY_ENTER"
            Case KEY_SPACE
                Print "KEY_SPACE"
            Case KEY_1
                Print "KEY_1"
            Case KEY_2
                Print "KEY_2"
            Case KEY_3
                Print "KEY_3"
            Case KEY_4
                Print "KEY_4"
            Case KEY_TAB
                Print "KEY_TAB"
            Case KEY_ESCAPE
                Print "KEY_ESCAPE"
            Case Else
                Print "KEY_OTHER"
        End Select
    End If
    Wait 10 Milliseconds
Loop Until KeyCode = KEY_ESCAPE

Clear Screen
Print "SMILE INTERACTIVE CLEAN"
""";

    private static readonly IReadOnlyList<PseudoConsoleInput> ContractInput =
        new[]
        {
            PseudoConsoleInput.Text(250, "W"),
            PseudoConsoleInput.Text(35, "A"),
            PseudoConsoleInput.Text(35, "S"),
            PseudoConsoleInput.Text(35, "D"),
            PseudoConsoleInput.BytesAfter(35, 0x1B, 0x5B, 0x41),
            PseudoConsoleInput.BytesAfter(35, 0x1B, 0x5B, 0x42),
            PseudoConsoleInput.BytesAfter(35, 0x1B, 0x5B, 0x44),
            PseudoConsoleInput.BytesAfter(35, 0x1B, 0x5B, 0x43),
            PseudoConsoleInput.BytesAfter(35, 0x0D),
            PseudoConsoleInput.Text(35, " "),
            PseudoConsoleInput.Text(35, "1"),
            PseudoConsoleInput.Text(35, "2"),
            PseudoConsoleInput.Text(35, "3"),
            PseudoConsoleInput.Text(35, "4"),
            PseudoConsoleInput.BytesAfter(35, 0x09),
            PseudoConsoleInput.Text(35, "q"),
            PseudoConsoleInput.BytesAfter(35, 0x1B),
            // The build-only launcher pauses after the generated program exits.
            // A queued final key proves that the target restored terminal input.
            PseudoConsoleInput.Text(350, "x")
        };

    [TestMethod]
    public async Task Pseudo_console_harness_transports_input_and_output()
    {
        DirectoryInfo temp = Directory.CreateTempSubdirectory("SMILE-ConPTY-");
        try
        {
            string launcher = Path.Combine(temp.FullName, "conpty-smoke.cmd");
            await File.WriteAllTextAsync(
                launcher,
                "@echo off\r\nset /p SMILE_KEY=\r\necho CONPTY_KEY_OK:%SMILE_KEY%\r\n");

            PseudoConsoleResult run = await WindowsPseudoConsole.RunBatchFileAsync(
                launcher,
                new[] { PseudoConsoleInput.Text(500, "W\r") },
                TimeSpan.FromSeconds(5),
                CancellationToken.None);

            Assert.IsFalse(run.TimedOut, run.Output);
            Assert.AreEqual(0, run.ExitCode, run.Output);
            StringAssert.Contains(run.Output, "CONPTY_KEY_OK:W");
        }
        finally
        {
            temp.Delete(recursive: true);
        }
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
    public async Task Interactive_contract_uses_real_raw_keys_and_cleans_up_on_all_ten_targets(
        TargetLanguage language)
    {
        var transpiler = new SmileTranspiler();
        ToolchainRegistry toolchains = ToolchainRegistry.CreateDefault();

        TranspileResult transpile = transpiler.Transpile(InteractiveContractSource, language);
        Assert.IsTrue(transpile.Success, $"{language}: {Join(transpile.Diagnostics)}");

        BuildRunResult build = await BuildForPseudoConsoleAsync(
            toolchains.Get(language),
            transpile.GeneratedProgram!);
        Assert.IsTrue(
            build.Success,
            $"{language}: {build.Stage}{Environment.NewLine}{build.BuildOutput}{Environment.NewLine}{build.StandardError}");
        Assert.IsFalse(
            HasCompilerWarning(build.BuildOutput),
            $"{language} emitted a compiler warning.{Environment.NewLine}{build.BuildOutput}");

        PseudoConsoleResult run = await WindowsPseudoConsole.RunBatchFileAsync(
            build.PauseLauncherPath!,
            ContractInput,
            TimeSpan.FromSeconds(20),
            CancellationToken.None);

        Assert.IsFalse(run.TimedOut, $"{language} timed out.{Environment.NewLine}{run.Output}");
        Assert.AreEqual(0, run.ExitCode, $"{language}:{Environment.NewLine}{run.Output}");
        AssertClearErasedStaleFrame(language, run.Output);
        foreach (string marker in new[]
        {
                "SMILE INTERACTIVE READY",
                "KEY_NONE",
                "KEY_W",
                "KEY_A",
                "KEY_S",
                "KEY_D",
                "KEY_UP",
                "KEY_DOWN",
                "KEY_LEFT",
                "KEY_RIGHT",
                "KEY_ENTER",
                "KEY_ESCAPE",
                "KEY_SPACE",
                "KEY_1",
                "KEY_2",
                "KEY_3",
                "KEY_4",
                "KEY_TAB",
                "KEY_OTHER",
                "SMILE INTERACTIVE CLEAN",
                "__SMILE_LAUNCH_EXIT__:0",
                "Press any key to exit"
        })
        {
            StringAssert.Contains(run.Output, marker, $"{language} did not prove {marker}.");
        }

        Console.WriteLine($"PASS interactive contract / {language} / {run.Duration.TotalMilliseconds:F0} ms");
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
    public async Task All_three_games_build_launch_redraw_and_exit_on_all_ten_targets(
        TargetLanguage language)
    {
        var transpiler = new SmileTranspiler();
        ToolchainRegistry toolchains = ToolchainRegistry.CreateDefault();
        var games = new[]
        {
            (File: "text-snake.smile", Title: "TRAIL RUNNER", Exit: "Thanks for running the trail!"),
            (File: "text-maze-muncher.smile", Title: "LANTERN MAZE", Exit: "The lanterns dim. Thanks for exploring!"),
            (File: "text-falling-blocks.smile", Title: "SKY FOUNDRY", Exit: "The foundry closes. Thanks for building!")
        };
        IReadOnlyList<PseudoConsoleInput> input = new[]
        {
            PseudoConsoleInput.BytesAfter(250, 0x0D),
            PseudoConsoleInput.Text(180, "d"),
            PseudoConsoleInput.BytesAfter(180, 0x1B, 0x5B, 0x41),
            PseudoConsoleInput.BytesAfter(220, 0x1B),
            PseudoConsoleInput.Text(350, "x")
        };

        foreach ((string file, string title, string exit) in games)
        {
            string source = await File.ReadAllTextAsync(Path.Combine(FindExamplesDirectory(), file));
            TranspileResult transpile = transpiler.Transpile(source, language);
            Assert.IsTrue(transpile.Success, $"{file} / {language}: {Join(transpile.Diagnostics)}");

            BuildRunResult build = await BuildForPseudoConsoleAsync(
                toolchains.Get(language),
                transpile.GeneratedProgram!);
            Assert.IsTrue(
                build.Success,
                $"{file} / {language}: {build.Stage}{Environment.NewLine}" +
                build.BuildOutput + Environment.NewLine + build.StandardError);
            Assert.IsFalse(
                HasCompilerWarning(build.BuildOutput),
                $"{file} / {language} emitted a compiler warning.{Environment.NewLine}{build.BuildOutput}");

            PseudoConsoleResult run = await WindowsPseudoConsole.RunBatchFileAsync(
                build.PauseLauncherPath!,
                input,
                TimeSpan.FromSeconds(20),
                CancellationToken.None);

            Assert.IsFalse(run.TimedOut, $"{file} / {language} timed out.{Environment.NewLine}{run.Output}");
            Assert.AreEqual(0, run.ExitCode, $"{file} / {language}:{Environment.NewLine}{run.Output}");
            StringAssert.Contains(run.Output, title, $"{file} / {language} did not display its title.");
            StringAssert.Contains(run.Output, exit, $"{file} / {language} did not take the Escape exit path.");
            StringAssert.Contains(
                run.Output,
                "Press any key to exit",
                $"{file} / {language} did not return to the restored launcher console.");
            StringAssert.Contains(
                run.Output,
                "__SMILE_LAUNCH_EXIT__:0",
                $"{file} / {language} returned a nonzero native exit status.");

            int titleOccurrences = Regex.Matches(run.Output, Regex.Escape(title)).Count;
            Assert.IsGreaterThanOrEqualTo(
                2,
                titleOccurrences,
                $"{file} / {language} did not show both its title and a redrawn game frame.");

            Console.WriteLine($"PASS game smoke / {file} / {language} / {run.Duration.TotalMilliseconds:F0} ms");
        }
    }

    [TestMethod]
    [TestCategory("CobolFocused")]
    public async Task Cobol_preserves_the_complete_playfield_geometry_of_all_three_games()
    {
        var transpiler = new SmileTranspiler();
        IToolchain toolchain = ToolchainRegistry.CreateDefault().Get(TargetLanguage.Cobol);
        var games = new[]
        {
            (
                File: "text-snake.smile",
                Title: "TRAIL RUNNER",
                Exit: "Thanks for running the trail!",
                FramePattern: @"(?m)^#(?=[^\r\n]{58}#\r?$)[^\r\n]* [^\r\n]*#\r?$"),
            (
                File: "text-maze-muncher.smile",
                Title: "LANTERN MAZE",
                Exit: "The lanterns dim. Thanks for exploring!",
                FramePattern: @"(?m)^#(?=[^\r\n]{69}#\r?$)[^\r\n]* [^\r\n]*#\r?$"),
            (
                File: "text-falling-blocks.smile",
                Title: "SKY FOUNDRY",
                Exit: "The foundry closes. Thanks for building!",
                FramePattern: @"(?m)^\|(?=[^\r\n]{30}\|\r?$)[^\r\n]* [^\r\n]*\|\r?$")
        };
        IReadOnlyList<PseudoConsoleInput> input = new[]
        {
            PseudoConsoleInput.BytesAfter(250, 0x0D),
            PseudoConsoleInput.Text(180, "d"),
            PseudoConsoleInput.BytesAfter(180, 0x1B, 0x5B, 0x41),
            PseudoConsoleInput.BytesAfter(220, 0x1B),
            PseudoConsoleInput.Text(350, "x")
        };

        foreach ((string file, string title, string exit, string framePattern) in games)
        {
            string source = await File.ReadAllTextAsync(Path.Combine(FindExamplesDirectory(), file));
            TranspileResult transpile = transpiler.Transpile(source, TargetLanguage.Cobol);
            Assert.IsTrue(transpile.Success, $"{file} / COBOL: {Join(transpile.Diagnostics)}");

            BuildRunResult build = await BuildForPseudoConsoleAsync(toolchain, transpile.GeneratedProgram!);
            Assert.IsTrue(
                build.Success,
                $"{file} / COBOL: {build.Stage}{Environment.NewLine}" +
                build.BuildOutput + Environment.NewLine + build.StandardError);
            Assert.IsFalse(
                HasCompilerWarning(build.BuildOutput),
                $"{file} / COBOL emitted a compiler warning.{Environment.NewLine}{build.BuildOutput}");

            PseudoConsoleResult run = await WindowsPseudoConsole.RunBatchFileAsync(
                build.PauseLauncherPath!,
                input,
                TimeSpan.FromSeconds(20),
                CancellationToken.None);

            Assert.IsFalse(run.TimedOut, $"{file} / COBOL timed out.{Environment.NewLine}{run.Output}");
            Assert.AreEqual(0, run.ExitCode, $"{file} / COBOL:{Environment.NewLine}{run.Output}");
            StringAssert.Contains(run.Output, title);
            StringAssert.Contains(run.Output, exit);
            string visibleOutput = Regex.Replace(
                run.Output,
                "\\x1B\\[[0-?]*[ -/]*[@-~]",
                string.Empty);
            Assert.IsTrue(
                Regex.IsMatch(visibleOutput, framePattern),
                $"{file} / COBOL did not preserve a full-width playfield row.{Environment.NewLine}{run.Output}");

            Console.WriteLine($"PASS COBOL frame geometry / {file} / {run.Duration.TotalMilliseconds:F0} ms");
        }
    }

    private static async Task<BuildRunResult> BuildForPseudoConsoleAsync(
        IToolchain toolchain,
        GeneratedProgram program)
    {
        ToolchainStatus status = await toolchain.DetectAsync(CancellationToken.None);
        Assert.IsTrue(status.IsAvailable, $"{toolchain.Language}: {status.Message}");

        BuildRunOptions options = BuildRunOptions.BuildOnly with { CreatePauseLauncher = true };
        BuildRunResult build = await toolchain.BuildAndRunAsync(program, CancellationToken.None, options);
        if (build.Success)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(build.PauseLauncherPath));
        }
        return build;
    }

    private static void AssertClearErasedStaleFrame(TargetLanguage language, string output)
    {
        int ready = output.IndexOf("SMILE INTERACTIVE READY", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, ready, $"{language}: ready frame was not printed.");

        int noKey = output.IndexOf("KEY_NONE", ready, StringComparison.Ordinal);
        Assert.IsGreaterThan(ready, noKey, $"{language}: key probe did not follow the ready frame.");

        int launcher = output.LastIndexOf("Run Program - Press Any Key.cmd", ready, StringComparison.Ordinal);
        int ansiErase = output.LastIndexOf("\x1b[2J", ready, StringComparison.Ordinal);
        string readyTrace = output[ready..noKey];
        Assert.IsTrue(
            ansiErase > launcher || readyTrace.Contains("\x1b[K", StringComparison.Ordinal),
            $"{language}: Clear Screen did not erase the stale attached-console frame.{Environment.NewLine}{output}");
    }

    private static bool HasCompilerWarning(string text) =>
        Regex.IsMatch(text, @"\bwarning(?:\s+[A-Z]+\d+|:)", RegexOptions.IgnoreCase);

    private static string Join(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.ToString()));

    private static string FindExamplesDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SMILE.sln")))
            {
                return Path.Combine(directory.FullName, "examples");
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the examples directory.");
    }
}
