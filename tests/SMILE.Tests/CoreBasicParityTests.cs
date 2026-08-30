using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using SMILE.Engine;
using SMILE.Toolchains;

namespace SMILE.Tests;

[TestClass]
[TestCategory("CoreBasicParity")]
public sealed class CoreBasicParityTests
{
    [TestMethod]
    [DoNotParallelize]
    public async Task Unchanged_positive_fixtures_compile_and_run_identically_in_both_repositories()
    {
        string repository = FindRepositoryRoot();
        string parityDirectory = Path.Combine(repository, "tests", "CoreBasicParity");
        string authorityCommit = ReadAuthorityCommit(Path.Combine(parityDirectory, "profile.json"));
        string smile2 = Path.GetFullPath(
            Environment.GetEnvironmentVariable("SMILE2_ROOT") ??
            Path.Combine(repository, "..", "SMILE 2.0"));

        Assert.AreEqual(authorityCommit, (await RunAsync("git", ["rev-parse", "HEAD"], smile2)).StandardOutput.Trim());
        Assert.AreEqual(string.Empty, (await RunAsync("git", ["status", "--porcelain"], smile2)).StandardOutput.Trim());

        IToolchain smile1Toolchain = ToolchainRegistry.CreateDefault().Get(TargetLanguage.MasmX64);
        ToolchainStatus smile1Status = await smile1Toolchain.DetectAsync(CancellationToken.None);
        Assert.IsTrue(smile1Status.IsAvailable, smile1Status.Message);
        string compiler = Path.Combine(
            smile2,
            "src",
            "Smile.Compiler",
            "bin",
            "Debug",
            "net10.0",
            "smilec.exe");
        Assert.IsTrue(File.Exists(compiler), $"Build the authoritative compiler first: {compiler}");

        string parityRun = Path.Combine(
            Path.GetTempPath(),
            "SMILE",
            "Parity",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parityRun);
        foreach (string fixtureName in new[] { "canonical", "counter-semantics" })
        {
            string fixture = Path.Combine(parityDirectory, fixtureName + ".smile");
            string expected = Normalize(await File.ReadAllTextAsync(
                Path.Combine(parityDirectory, fixtureName + ".stdout")));
            string source = await File.ReadAllTextAsync(fixture);

            EvaluationResult evaluation = new SmileEvaluator().Evaluate(source);
            Assert.IsTrue(evaluation.Success, fixtureName + Environment.NewLine + Join(evaluation.Diagnostics));
            Assert.AreEqual(expected, Normalize(evaluation.Output), fixtureName);

            TranspileResult smile1Program = new SmileTranspiler().Transpile(source, TargetLanguage.MasmX64);
            Assert.IsTrue(smile1Program.Success, fixtureName + Environment.NewLine + Join(smile1Program.Diagnostics));
            BuildRunResult smile1Run = await smile1Toolchain.BuildAndRunAsync(
                smile1Program.GeneratedProgram!,
                CancellationToken.None);
            Assert.IsTrue(
                smile1Run.Success,
                fixtureName + Environment.NewLine + smile1Run.BuildOutput + Environment.NewLine + smile1Run.StandardError);
            Assert.AreEqual(expected, Normalize(smile1Run.StandardOutput), fixtureName);

            string smile2Executable = Path.Combine(parityRun, fixtureName + "-smile2.exe");
            ProcessCapture compile = await RunAsync(
                compiler,
                [fixture, "--target", "windows-x64", "-o", smile2Executable],
                parityRun);
            Assert.AreEqual(
                0,
                compile.ExitCode,
                fixtureName + Environment.NewLine + compile.StandardOutput + Environment.NewLine + compile.StandardError);

            ProcessCapture smile2Run = await RunAsync(smile2Executable, [], parityRun);
            Assert.AreEqual(0, smile2Run.ExitCode, fixtureName + Environment.NewLine + smile2Run.StandardError);
            Assert.AreEqual(expected, Normalize(smile2Run.StandardOutput), fixtureName);
            Assert.AreEqual(
                Normalize(smile1Run.StandardOutput),
                Normalize(smile2Run.StandardOutput),
                fixtureName);
        }

        Assert.AreEqual(authorityCommit, (await RunAsync("git", ["rev-parse", "HEAD"], smile2)).StandardOutput.Trim());
        Assert.AreEqual(string.Empty, (await RunAsync("git", ["status", "--porcelain"], smile2)).StandardOutput.Trim());
    }

    [TestMethod]
    public async Task Every_obsolete_fixture_is_rejected_by_the_only_front_end()
    {
        string rejectedDirectory = Path.Combine(
            FindRepositoryRoot(),
            "tests",
            "CoreBasicParity",
            "rejected");
        string[] fixtures = Directory.GetFiles(rejectedDirectory, "*.smile").Order().ToArray();
        Assert.IsGreaterThanOrEqualTo(8, fixtures.Length);

        var transpiler = new SmileTranspiler();
        foreach (string fixture in fixtures)
        {
            BindResult result = transpiler.Bind(await File.ReadAllTextAsync(fixture));
            Assert.IsFalse(result.Success, Path.GetFileName(fixture));
        }
    }

    [TestMethod]
    public void Manifest_fixture_hashes_are_complete_and_reproducible()
    {
        string parityDirectory = Path.Combine(FindRepositoryRoot(), "tests", "CoreBasicParity");
        string manifestPath = Path.Combine(parityDirectory, "profile.json");
        string manifestText = File.ReadAllText(manifestPath);
        Assert.IsFalse(manifestText.Contains("D:\\", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(manifestText.Contains("C:\\", StringComparison.OrdinalIgnoreCase));

        using JsonDocument document = JsonDocument.Parse(manifestText);
        Assert.AreEqual(1, document.RootElement.GetProperty("manifestFormatVersion").GetInt32());
        JsonElement fixtures = document.RootElement.GetProperty("fixtures");
        Assert.HasCount(13, fixtures.EnumerateArray().ToArray());
        foreach (JsonElement fixture in fixtures.EnumerateArray())
        {
            string relativePath = fixture.GetProperty("path").GetString()!;
            string expectedHash = fixture.GetProperty("sha256").GetString()!;
            string fullPath = Path.GetFullPath(Path.Combine(
                parityDirectory,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            Assert.IsTrue(fullPath.StartsWith(parityDirectory, StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(File.Exists(fullPath), relativePath);
            string actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fullPath)))
                .ToLowerInvariant();
            Assert.AreEqual(expectedHash, actualHash, relativePath);
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(Environment.CurrentDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SMILE.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the SMILE repository root.");
    }

    private static string ReadAuthorityCommit(string manifestPath)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        return document.RootElement
            .GetProperty("authority")
            .GetProperty("commit")
            .GetString()
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
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException($"Could not start {fileName}.");
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await process.WaitForExitAsync(timeout.Token);
        return new ProcessCapture(
            process.ExitCode,
            await standardOutput,
            await standardError);
    }

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static string Join(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.ToString()));

    private sealed record ProcessCapture(int ExitCode, string StandardOutput, string StandardError);
}
