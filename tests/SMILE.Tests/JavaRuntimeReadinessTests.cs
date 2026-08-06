using System.IO;
using System.Text;
using SMILE.Engine;
using SMILE.Toolchains;

namespace SMILE.Tests;

[TestClass]
public sealed class JavaToolchainDetectionTests
{
    [TestMethod]
    public async Task Detection_reports_a_full_JDK_only_when_java_and_javac_share_one_bin()
    {
        using var probe = new JavaProbeDirectory();
        probe.CreateExecutable("java.exe");
        probe.CreateExecutable("javac.exe");
        var runner = new JavaProbeRunner(command =>
            Path.GetFileName(command.FileName).Equals("javac.exe", StringComparison.OrdinalIgnoreCase)
                ? Success(standardOutput: "javac 25.0.4")
                : Success(standardError: "openjdk version \"25.0.4\""));
        var toolchain = new JavaToolchain(runner, () => new[] { probe.BinDirectory });

        ToolchainStatus status = await toolchain.DetectAsync(CancellationToken.None);

        Assert.IsTrue(status.IsAvailable);
        Assert.AreEqual(probe.BinDirectory, status.Location);
        StringAssert.Contains(status.Message, "Full JDK detected");
        Assert.HasCount(2, runner.Commands);
        Assert.IsTrue(runner.Commands.All(command =>
            Path.GetDirectoryName(command.FileName) == probe.BinDirectory));
    }

    [TestMethod]
    public async Task Detection_reports_a_runtime_only_when_javac_is_absent()
    {
        using var probe = new JavaProbeDirectory();
        probe.CreateExecutable("java.exe");
        var runner = new JavaProbeRunner(_ =>
            Success(standardError: "openjdk version \"25.0.4\""));
        var toolchain = new JavaToolchain(runner, () => new[] { probe.BinDirectory });

        ToolchainStatus status = await toolchain.DetectAsync(CancellationToken.None);

        Assert.IsFalse(status.IsAvailable);
        Assert.AreEqual(probe.BinDirectory, status.Location);
        StringAssert.Contains(status.Message, "Java runtime detected, but javac is missing");
        Assert.HasCount(1, runner.Commands);
        Assert.AreEqual("java.exe", Path.GetFileName(runner.Commands[0].FileName));
    }

    [TestMethod]
    public async Task Detection_reports_JDK_missing_when_no_real_Java_executable_exists()
    {
        var runner = new JavaProbeRunner(_ =>
            throw new InvalidOperationException("Missing candidates must not launch a process."));
        var toolchain = new JavaToolchain(runner, () => Array.Empty<string>());

        ToolchainStatus status = await toolchain.DetectAsync(CancellationToken.None);

        Assert.IsFalse(status.IsAvailable);
        Assert.IsNull(status.Location);
        StringAssert.Contains(status.Message, "JDK missing");
        Assert.HasCount(0, runner.Commands);
    }

    [TestMethod]
    public async Task Detection_never_executes_Windows_Store_aliases()
    {
        using var probe = new JavaProbeDirectory(Path.Combine("Microsoft", "WindowsApps"));
        probe.CreateExecutable("java.exe");
        probe.CreateExecutable("javac.exe");
        var runner = new JavaProbeRunner(_ =>
            throw new InvalidOperationException("Windows Store aliases must never be executed."));
        var toolchain = new JavaToolchain(runner, () => new[] { probe.BinDirectory });

        ToolchainStatus status = await toolchain.DetectAsync(CancellationToken.None);

        Assert.IsFalse(status.IsAvailable);
        StringAssert.Contains(status.Message, "JDK missing");
        Assert.HasCount(0, runner.Commands);
    }

    private static ProcessResult Success(
        string standardOutput = "",
        string standardError = "") =>
        new(
            ExitCode: 0,
            standardOutput,
            standardError,
            Duration: TimeSpan.Zero,
            TimedOut: false,
            Cancelled: false);

    private sealed class JavaProbeRunner : IProcessRunner
    {
        private readonly Func<ProcessCommand, ProcessResult> _resultFactory;

        public JavaProbeRunner(Func<ProcessCommand, ProcessResult> resultFactory)
        {
            _resultFactory = resultFactory;
        }

        public List<ProcessCommand> Commands { get; } = new();

        public Task<ProcessResult> RunAsync(
            ProcessCommand command,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            Commands.Add(command);
            return Task.FromResult(_resultFactory(command));
        }
    }

    private sealed class JavaProbeDirectory : IDisposable
    {
        private readonly string _root;

        public JavaProbeDirectory(string? relativeBinPath = null)
        {
            _root = Path.Combine(
                Path.GetTempPath(),
                "SMILE",
                "JavaDetectionTests",
                Guid.NewGuid().ToString("N"));
            BinDirectory = relativeBinPath is null
                ? Path.Combine(_root, "bin")
                : Path.Combine(_root, relativeBinPath);
            Directory.CreateDirectory(BinDirectory);
        }

        public string BinDirectory { get; }

        public void CreateExecutable(string fileName) =>
            File.WriteAllText(Path.Combine(BinDirectory, fileName), string.Empty);

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}

[TestClass]
public sealed class JavaRuntimeReadinessTests
{
    private const string RequireJavaEnvironmentVariable = "SMILE_REQUIRE_JAVA";

    private readonly SmileEvaluator _evaluator = new();
    private readonly SmileTranspiler _transpiler = new();
    private readonly IToolchain _java = ToolchainRegistry.CreateDefault().Get(TargetLanguage.Java);

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task Java_detection_selects_a_complete_same_bin_JDK()
    {
        ToolchainStatus status = await RequireFullJdkAsync();

        Assert.IsFalse(string.IsNullOrWhiteSpace(status.Location));
        string javaPath = Path.Combine(status.Location!, "java.exe");
        string javacPath = Path.Combine(status.Location!, "javac.exe");
        Assert.IsTrue(File.Exists(javaPath), javaPath);
        Assert.IsTrue(File.Exists(javacPath), javacPath);
        StringAssert.Contains(status.Message, "Full JDK detected");
    }

    [TestMethod]
    public Task Java_runs_ordinary_SET_against_the_reference_evaluator() =>
        AssertJavaMatchesEvaluatorAsync(
            "ordinary SET",
            "LET Counter = 0\nSET Counter = Counter + 1\nPRINT {Counter}");

    [TestMethod]
    public Task Java_runs_String_reassignment_against_the_reference_evaluator() =>
        AssertJavaMatchesEvaluatorAsync(
            "String reassignment",
            "LET Name = \"Sin\"\nSET Name = \"Louiery\"\nPRINT {Name}");

    [TestMethod]
    public Task Java_runs_SET_Block_String_against_the_reference_evaluator() =>
        AssertJavaMatchesEvaluatorAsync(
            "SET Block String",
            """
            LET Name = ""

            SET Name ="
            S
             I
              N
            "

            PRINT {Name}
            """);

    [TestMethod]
    public Task Java_preserves_exact_embedded_NUL_bytes_after_SET() =>
        AssertJavaMatchesEvaluatorAsync(
            "embedded NUL SET",
            "LET Data = \"A\\0B\"\nSET Data = \"A\\0C\"\nPRINT {Data}",
            expectedHex: "4100430A");

    [TestMethod]
    public Task Java_runs_a_wide_Integer_introduced_by_SET() =>
        AssertJavaMatchesEvaluatorAsync(
            "wide Integer SET",
            "LET Value = 1\nSET Value = 5000000000\nPRINT {Value}");

    [TestMethod]
    public async Task Java_runs_the_deployed_cumulative_language_reference()
    {
        string languagePath = Path.Combine(AppContext.BaseDirectory, "language.smile");
        Assert.IsTrue(File.Exists(languagePath), $"Deployed language reference not found: {languagePath}");

        string source = await File.ReadAllTextAsync(languagePath, Encoding.UTF8);
        await AssertJavaMatchesEvaluatorAsync(
            "language.smile",
            source,
            scriptedInput: InputTestData.CanonicalScriptedInput);
    }

    private async Task AssertJavaMatchesEvaluatorAsync(
        string programName,
        string source,
        string? expectedHex = null,
        string? scriptedInput = null)
    {
        ToolchainStatus status = await RequireFullJdkAsync();
        EvaluationResult expected = scriptedInput is null
            ? _evaluator.Evaluate(source)
            : _evaluator.Evaluate(source, scriptedInput);
        Assert.IsTrue(expected.Success, string.Join(Environment.NewLine, expected.Diagnostics));

        TranspileResult transpile = _transpiler.Transpile(source, TargetLanguage.Java);
        Assert.IsTrue(transpile.Success, string.Join(Environment.NewLine, transpile.Diagnostics));

        BuildRunResult actual = await _java.BuildAndRunAsync(
            transpile.GeneratedProgram!,
            CancellationToken.None,
            scriptedInput is null ? null : BuildRunOptions.Scripted(scriptedInput));

        TestContext.WriteLine($"Java acceptance program: {programName}");
        TestContext.WriteLine($"javac path: {Path.Combine(status.Location!, "javac.exe")}");
        TestContext.WriteLine($"java path: {Path.Combine(status.Location!, "java.exe")}");
        TestContext.WriteLine($"javac succeeded: {actual.Stage != "Building"}");
        TestContext.WriteLine($"java succeeded: {actual.Success && actual.ExitCode == 0}");
        TestContext.WriteLine($"java exit code: {actual.ExitCode?.ToString() ?? "unavailable"}");
        TestContext.WriteLine(actual.BuildOutput);

        Assert.IsTrue(
            actual.Success,
            actual.BuildOutput + Environment.NewLine + actual.StandardError);
        Assert.AreEqual(0, actual.ExitCode);

        byte[] expectedBytes = Encoding.UTF8.GetBytes(NormalizePhysicalNewlines(expected.Output));
        byte[] actualBytes = Encoding.UTF8.GetBytes(NormalizePhysicalNewlines(actual.StandardOutput));
        string expectedOutputHex = Convert.ToHexString(expectedBytes);
        string actualOutputHex = Convert.ToHexString(actualBytes);
        TestContext.WriteLine($"stdout UTF-8 bytes: {actualOutputHex}");
        Assert.AreEqual(expectedOutputHex, actualOutputHex, "stdout differed from SmileEvaluator.");

        if (expectedHex is not null)
        {
            Assert.AreEqual(expectedHex, actualOutputHex, "Exact acceptance bytes differed.");
        }
    }

    private async Task<ToolchainStatus> RequireFullJdkAsync()
    {
        ToolchainStatus status = await _java.DetectAsync(CancellationToken.None);
        TestContext.WriteLine($"Java status: {status.Message}");
        TestContext.WriteLine($"Java location: {status.Location ?? "unavailable"}");
        TestContext.WriteLine($"Java detected version:{Environment.NewLine}{status.Version ?? "unavailable"}");

        if (status.IsAvailable)
        {
            return status;
        }

        if (string.Equals(
            Environment.GetEnvironmentVariable(RequireJavaEnvironmentVariable),
            "1",
            StringComparison.Ordinal))
        {
            Assert.Fail(
                $"{RequireJavaEnvironmentVariable}=1 requires a complete JDK, but detection reported: {status.Message}");
        }

        Assert.Inconclusive(
            $"{status.Message} Set {RequireJavaEnvironmentVariable}=1 for official release validation.");
        return status;
    }

    private static string NormalizePhysicalNewlines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal);
}
