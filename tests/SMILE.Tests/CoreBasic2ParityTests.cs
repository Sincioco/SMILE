using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using SMILE.Engine;
using SMILE.Toolchains;

namespace SMILE.Tests;

[TestClass]
[TestCategory("CoreBasic2Parity")]
public sealed class CoreBasic2ParityTests
{
    private static readonly string[] FixtureNames = ["canonical", "byval-scope", "recursion"];

    [TestMethod]
    [DoNotParallelize]
    public async Task Unchanged_profile_two_fixtures_compile_and_run_identically_in_both_repositories()
    {
        string repository = FindRepositoryRoot();
        string parityDirectory = Path.Combine(repository, "tests", "CoreBasic2Parity");
        string authorityCommit = ReadAuthorityCommit(Path.Combine(parityDirectory, "profile.json"));
        string smile2 = Path.GetFullPath(
            Environment.GetEnvironmentVariable("SMILE2_ROOT") ?? Path.Combine(repository, "..", "SMILE 2.0"));

        Assert.AreEqual(authorityCommit, (await RunAsync("git", ["rev-parse", "HEAD"], smile2)).StandardOutput.Trim());
        Assert.AreEqual(string.Empty, (await RunAsync("git", ["status", "--porcelain"], smile2)).StandardOutput.Trim());

        string compiler = Path.Combine(smile2, "src", "Smile.Compiler", "bin", "Debug", "net10.0", "smilec.exe");
        Assert.IsTrue(File.Exists(compiler), $"Build the authoritative compiler first: {compiler}");

        IToolchain smile1Toolchain = ToolchainRegistry.CreateDefault().Get(TargetLanguage.MasmX64);
        ToolchainStatus status = await smile1Toolchain.DetectAsync(CancellationToken.None);
        Assert.IsTrue(status.IsAvailable, status.Message);

        string runDirectory = Path.Combine(Path.GetTempPath(), "SMILE", "CoreBasic2Parity", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runDirectory);

        foreach (string fixtureName in FixtureNames)
        {
            string fixture = Path.Combine(parityDirectory, fixtureName + ".smile");
            string source = await File.ReadAllTextAsync(fixture);
            string expected = Normalize(await File.ReadAllTextAsync(Path.Combine(parityDirectory, fixtureName + ".stdout")));

            EvaluationResult evaluation = new SmileEvaluator().Evaluate(source);
            Assert.IsTrue(evaluation.Success, fixtureName + Environment.NewLine + Join(evaluation.Diagnostics));
            Assert.AreEqual(expected, Normalize(evaluation.Output), fixtureName + " evaluator");

            TranspileResult transpile = new SmileTranspiler().Transpile(source, TargetLanguage.MasmX64);
            Assert.IsTrue(transpile.Success, fixtureName + Environment.NewLine + Join(transpile.Diagnostics));
            BuildRunResult smile1Run = await smile1Toolchain.BuildAndRunAsync(
                transpile.GeneratedProgram!, CancellationToken.None);
            Assert.IsTrue(
                smile1Run.Success,
                fixtureName + Environment.NewLine + smile1Run.BuildOutput + Environment.NewLine + smile1Run.StandardError);
            Assert.AreEqual(expected, Normalize(smile1Run.StandardOutput), fixtureName + " SMILE 1.0");

            string smile2Executable = Path.Combine(runDirectory, fixtureName + "-smile2.exe");
            ProcessCapture compile = await RunAsync(
                compiler, [fixture, "--target", "windows-x64", "-o", smile2Executable], runDirectory);
            Assert.AreEqual(0, compile.ExitCode, compile.StandardOutput + Environment.NewLine + compile.StandardError);
            ProcessCapture smile2Run = await RunAsync(smile2Executable, [], runDirectory);
            Assert.AreEqual(0, smile2Run.ExitCode, smile2Run.StandardError);
            Assert.AreEqual(expected, Normalize(smile2Run.StandardOutput), fixtureName + " SMILE 2.0");
            Assert.AreEqual(Normalize(smile1Run.StandardOutput), Normalize(smile2Run.StandardOutput), fixtureName);
        }

        Assert.AreEqual(authorityCommit, (await RunAsync("git", ["rev-parse", "HEAD"], smile2)).StandardOutput.Trim());
        Assert.AreEqual(string.Empty, (await RunAsync("git", ["status", "--porcelain"], smile2)).StandardOutput.Trim());
    }

    [TestMethod]
    public void Profile_two_fixture_hashes_and_public_examples_are_reproducible()
    {
        string repository = FindRepositoryRoot();
        string parityDirectory = Path.Combine(repository, "tests", "CoreBasic2Parity");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(parityDirectory, "profile.json")));
        JsonElement fixtures = manifest.RootElement.GetProperty("fixtures");
        Assert.HasCount(6, fixtures.EnumerateArray().ToArray());

        foreach (JsonElement fixture in fixtures.EnumerateArray())
        {
            string relativePath = fixture.GetProperty("path").GetString()!;
            string expectedHash = fixture.GetProperty("sha256").GetString()!;
            string fullPath = Path.Combine(parityDirectory, relativePath);
            string actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fullPath))).ToLowerInvariant();
            Assert.AreEqual(expectedHash, actualHash, relativePath);
        }

        Dictionary<string, string> exampleNames = new()
        {
            ["canonical"] = "core-basic-2-canonical.smile",
            ["byval-scope"] = "core-basic-2-byval-scope.smile",
            ["recursion"] = "core-basic-2-recursion.smile"
        };
        foreach ((string fixtureName, string exampleName) in exampleNames)
        {
            CollectionAssert.AreEqual(
                File.ReadAllBytes(Path.Combine(parityDirectory, fixtureName + ".smile")),
                File.ReadAllBytes(Path.Combine(repository, "examples", exampleName)),
                fixtureName);
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(Environment.CurrentDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SMILE.sln"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the SMILE repository root.");
    }

    private static string ReadAuthorityCommit(string manifestPath)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        return document.RootElement.GetProperty("authority").GetProperty("commit").GetString()
            ?? throw new InvalidDataException("profile.json has no authority commit.");
    }

    private static async Task<ProcessCapture> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory)
    {
        var start = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);

        using Process process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await process.WaitForExitAsync(timeout.Token);
        return new ProcessCapture(process.ExitCode, await output, await error);
    }

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static string Join(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.ToString()));

    private sealed record ProcessCapture(int ExitCode, string StandardOutput, string StandardError);
}
