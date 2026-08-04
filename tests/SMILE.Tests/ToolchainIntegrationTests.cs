using System.IO;
using SMILE.Engine;
using SMILE.Toolchains;

namespace SMILE.Tests;

[TestClass]
public sealed class ToolchainIntegrationTests
{
    private const string SampleSource = """
LET Name = "Sin"

PRINT
PRINT "Hello World!"
PRINT Hello World!
PRINT Hello {Name}!
PRINT $"Hello {Name}!"
PRINT "Hello " + Name + "!"
PRINT Literal braces: {{Name}}
PRINT A; B; C
""";

    private const string ExpectedOutput = "\nHello World!\nHello World!\nHello Sin!\nHello Sin!\nHello Sin!\nLiteral braces: {Name}\nA; B; C\n";

    private readonly SmileTranspiler _transpiler = new();
    private readonly SmileEvaluator _evaluator = new();
    private readonly ToolchainRegistry _toolchains = ToolchainRegistry.CreateDefault();

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
    public async Task Installed_target_builds_or_runs_and_matches_expected_output(TargetLanguage language)
    {
        IToolchain toolchain = _toolchains.Get(language);
        ToolchainStatus status = await toolchain.DetectAsync(CancellationToken.None);

        if (!status.IsAvailable)
        {
            Assert.Inconclusive(status.Message);
        }

        GeneratedProgram program = _transpiler.Transpile(SampleSource, language).GeneratedProgram!;
        BuildRunResult result = await toolchain.BuildAndRunAsync(program, CancellationToken.None);

        Assert.IsTrue(result.Success, result.BuildOutput + Environment.NewLine + result.StandardError);
        Assert.AreEqual(Normalize(ExpectedOutput), Normalize(result.StandardOutput));
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
    public async Task Installed_target_writes_press_any_key_launcher_when_requested(TargetLanguage language)
    {
        IToolchain toolchain = _toolchains.Get(language);
        ToolchainStatus status = await toolchain.DetectAsync(CancellationToken.None);

        if (!status.IsAvailable)
        {
            Assert.Inconclusive(status.Message);
        }

        GeneratedProgram program = _transpiler.Transpile(SampleSource, language).GeneratedProgram!;
        BuildRunResult result = await toolchain.BuildAndRunAsync(
            program,
            CancellationToken.None,
            new BuildRunOptions(CreatePauseLauncher: true));

        Assert.IsTrue(result.Success, result.BuildOutput + Environment.NewLine + result.StandardError);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.PauseLauncherPath));
        string launcherPath = result.PauseLauncherPath!;
        Assert.IsTrue(File.Exists(launcherPath), launcherPath);

        string launcher = await File.ReadAllTextAsync(launcherPath);
        AssertPauseLauncherCommand(language, launcher);
        StringAssert.Contains(launcher, "Press any key to exit...");
    }

    [TestMethod]
    public async Task Installed_targets_produce_identical_normalized_output()
    {
        var outputs = new Dictionary<TargetLanguage, string>();

        foreach (TargetLanguage language in TargetLanguageInfo.All)
        {
            IToolchain toolchain = _toolchains.Get(language);
            ToolchainStatus status = await toolchain.DetectAsync(CancellationToken.None);

            if (!status.IsAvailable)
            {
                continue;
            }

            GeneratedProgram program = _transpiler.Transpile(SampleSource, language).GeneratedProgram!;
            BuildRunResult result = await toolchain.BuildAndRunAsync(program, CancellationToken.None);

            Assert.IsTrue(result.Success, result.BuildOutput + Environment.NewLine + result.StandardError);
            outputs[language] = Normalize(result.StandardOutput);
        }

        if (outputs.Count < 2)
        {
            Assert.Inconclusive("Fewer than two target toolchains are installed.");
        }

        string expected = outputs.Values.First();
        Assert.IsTrue(outputs.Values.All(output => output == expected));
    }

    [TestMethod]
    public async Task Installed_targets_run_idiomatic_generation_acceptance_programs()
    {
        (string Source, string ExpectedOutput)[] cases =
        {
            (
                """
LET Name = "Sin"

PRINT
PRINT "Hello World!"
PRINT Hello World!
PRINT Hello {Name}!
PRINT $"Hello {Name}!"
PRINT "Hello " + Name + "!"
""",
                "\nHello World!\nHello World!\nHello Sin!\nHello Sin!\nHello Sin!\n"),
            (
                """
LET Name = "Sin"

PRINT Progress: 100%
PRINT {Name} is 100% ready.
""",
                "Progress: 100%\nSin is 100% ready.\n"),
            (
                """
LET FirstName = "Sin"
LET LastName = "Cioco"

PRINT $"{FirstName} {LastName}"
PRINT $"{FirstName}{LastName}{FirstName}"
""",
                "Sin Cioco\nSinCiocoSin\n"),
            (
                """
LET Name = "Sin"

PRINT Literal braces: {{Name}}
PRINT $"Literal braces: {{Name}}"
PRINT "Literal braces: {Name}"
""",
                "Literal braces: {Name}\nLiteral braces: {Name}\nLiteral braces: {Name}\n")
        };

        TargetLanguage[] runnableTargets =
        {
            TargetLanguage.CSharp,
            TargetLanguage.C,
            TargetLanguage.MasmX64,
            TargetLanguage.JavaScript,
            TargetLanguage.Java,
            TargetLanguage.Cobol,
            TargetLanguage.ObjectiveC,
            TargetLanguage.Swift,
            TargetLanguage.Python,
            TargetLanguage.Cpp
        };

        int executed = 0;
        foreach ((string source, string expectedOutput) in cases)
        {
            foreach (TargetLanguage language in runnableTargets)
            {
                IToolchain toolchain = _toolchains.Get(language);
                ToolchainStatus status = await toolchain.DetectAsync(CancellationToken.None);
                if (!status.IsAvailable)
                {
                    continue;
                }

                GeneratedProgram program = _transpiler.Transpile(source, language).GeneratedProgram!;
                BuildRunResult result = await toolchain.BuildAndRunAsync(program, CancellationToken.None);

                Assert.IsTrue(result.Success, result.BuildOutput + Environment.NewLine + result.StandardError);
                Assert.AreEqual(Normalize(expectedOutput), Normalize(result.StandardOutput));
                executed++;
            }
        }

        if (executed == 0)
        {
            Assert.Inconclusive("No runnable target toolchains are installed.");
        }
    }

    [TestMethod]
    public async Task Installed_targets_match_reference_evaluator_for_let_v1_programs()
    {
        string[] sources =
        {
            """
LET Name = "Sin"
LET Copy = Name

PRINT {Copy}
""",
            """
LET FirstName = "Sin"
LET LastName = "Cioco"
LET FullName = FirstName + " " + LastName

PRINT {FullName}
""",
            """
LET Name = "Sin"
LET Greeting = $"Hello {Name}!"

PRINT {Greeting}
""",
            """
LET A = "A"
LET B = A + "B"
LET C = $"{B}C"
LET D = C + A

PRINT {D}
""",
            """
LET class = "A"
LET Console = "B"
LET printf = "C"
LET System = "D"

PRINT {class}
PRINT {Console}
PRINT {printf}
PRINT {System}
""",
            """
LET Age = 49
LET Adult = Age >= 18
LET Count = 2 + 3 * 4
LET Message = $"Age={Age}, Adult={Adult}, Count={Count}"

PRINT {Age}
PRINT {Adult}
PRINT {Count}
PRINT {Message}
"""
        };

        TargetLanguage[] runnableTargets =
        {
            TargetLanguage.CSharp,
            TargetLanguage.C,
            TargetLanguage.MasmX64,
            TargetLanguage.JavaScript,
            TargetLanguage.Java,
            TargetLanguage.Cobol,
            TargetLanguage.ObjectiveC,
            TargetLanguage.Swift,
            TargetLanguage.Python,
            TargetLanguage.Cpp
        };

        int executed = 0;
        foreach (string source in sources)
        {
            EvaluationResult evaluation = _evaluator.Evaluate(source);
            Assert.IsTrue(evaluation.Success, string.Join(Environment.NewLine, evaluation.Diagnostics));
            string expected = Normalize(evaluation.Output);

            foreach (TargetLanguage language in runnableTargets)
            {
                IToolchain toolchain = _toolchains.Get(language);
                ToolchainStatus status = await toolchain.DetectAsync(CancellationToken.None);
                if (!status.IsAvailable)
                {
                    continue;
                }

                GeneratedProgram program = _transpiler.Transpile(source, language).GeneratedProgram!;
                BuildRunResult result = await toolchain.BuildAndRunAsync(program, CancellationToken.None);

                Assert.IsTrue(result.Success, result.BuildOutput + Environment.NewLine + result.StandardError);
                Assert.AreEqual(expected, Normalize(result.StandardOutput));
                executed++;
            }
        }

        if (executed == 0)
        {
            Assert.Inconclusive("No runnable target toolchains are installed.");
        }
    }

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');

    private static string ExpectedPauseLauncherCommand(TargetLanguage language) =>
        language switch
        {
            TargetLanguage.CSharp => "\"bin\\Debug\\net10.0\\GeneratedProgram.exe\"",
            TargetLanguage.C => "\"Program.exe\"",
            TargetLanguage.MasmX64 => "\"Program.exe\"",
            TargetLanguage.JavaScript => "node Program.js",
            TargetLanguage.Java => "java Program",
            TargetLanguage.Cobol => "\"Program.exe\"",
            TargetLanguage.ObjectiveC => "\"Program.exe\"",
            TargetLanguage.Swift => "\"Program.exe\"",
            TargetLanguage.Python => "-B Program.py",
            TargetLanguage.Cpp => "\"Program.exe\"",
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, null)
        };

    private static void AssertPauseLauncherCommand(TargetLanguage language, string launcher)
    {
        if (language is TargetLanguage.Java)
        {
            // Java may be launched from PATH or from a discovered JDK folder.
            // Either form is valid as long as the launcher runs Program.class.
            bool usesPath = launcher.Contains("java Program", StringComparison.OrdinalIgnoreCase);
            bool usesDiscoveredJdk = launcher.Contains("java.exe\" Program", StringComparison.OrdinalIgnoreCase);
            Assert.IsTrue(usesPath || usesDiscoveredJdk, launcher);
            return;
        }

        StringAssert.Contains(launcher, ExpectedPauseLauncherCommand(language));
    }
}
