using System.IO;
using SMILE.Toolchains;

namespace SMILE.Tests;

[TestClass]
public sealed class ProcessRunnerTests
{
    private readonly ProcessRunner _runner = new();

    [TestMethod]
    public void Toolchain_timeouts_keep_builds_and_program_runs_separate()
    {
        Assert.AreEqual(TimeSpan.FromSeconds(120), ToolchainBase.BuildTimeout);
        Assert.AreEqual(TimeSpan.FromSeconds(10), ToolchainBase.ProgramTimeout);
    }

    [TestMethod]
    public async Task Process_runner_captures_standard_output_and_error()
    {
        ProcessResult result = await _runner.RunAsync(
            ProcessCommand.ForCmd("echo out && echo err 1>&2", Environment.CurrentDirectory),
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.IsTrue(result.Success);
        StringAssert.Contains(result.StandardOutput, "out");
        StringAssert.Contains(result.StandardError, "err");
    }

    [TestMethod]
    public async Task Process_runner_reports_nonzero_exit_code()
    {
        ProcessResult result = await _runner.RunAsync(
            ProcessCommand.ForCmd("exit /b 7", Environment.CurrentDirectory),
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(7, result.ExitCode);
    }

    [TestMethod]
    public async Task Process_runner_reports_missing_tool()
    {
        ProcessResult result = await _runner.RunAsync(
            new ProcessCommand("definitely-not-a-smile-tool.exe", Array.Empty<string>(), Environment.CurrentDirectory),
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.IsFalse(result.Success);
        Assert.IsNull(result.ExitCode);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.StandardError));
    }

    [TestMethod]
    public async Task Process_runner_returns_failure_for_invalid_inputs()
    {
        ProcessResult blankFile = await _runner.RunAsync(
            new ProcessCommand("", Array.Empty<string>(), Environment.CurrentDirectory),
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        ProcessResult missingDirectory = await _runner.RunAsync(
            new ProcessCommand("cmd.exe", Array.Empty<string>(), Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))),
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        ProcessResult invalidTimeout = await _runner.RunAsync(
            new ProcessCommand("cmd.exe", Array.Empty<string>(), Environment.CurrentDirectory),
            TimeSpan.Zero,
            CancellationToken.None);

        Assert.IsFalse(blankFile.Success);
        StringAssert.Contains(blankFile.StandardError, "filename was blank");
        Assert.IsFalse(missingDirectory.Success);
        StringAssert.Contains(missingDirectory.StandardError, "working directory does not exist");
        Assert.IsFalse(invalidTimeout.Success);
        StringAssert.Contains(invalidTimeout.StandardError, "timeout must be positive");
    }

    [TestMethod]
    public async Task Process_runner_times_out_and_kills_process_tree()
    {
        ProcessResult result = await _runner.RunAsync(
            ProcessCommand.ForCmd("ping -n 6 127.0.0.1 >nul", Environment.CurrentDirectory),
            TimeSpan.FromMilliseconds(200),
            CancellationToken.None);

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.TimedOut);
    }

    [TestMethod]
    public async Task Process_runner_supports_cancellation()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        ProcessResult result = await _runner.RunAsync(
            ProcessCommand.ForCmd("ping -n 6 127.0.0.1 >nul", Environment.CurrentDirectory),
            TimeSpan.FromSeconds(10),
            cancellation.Token);

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Cancelled);
    }

    [TestMethod]
    public async Task Process_runner_bounds_standard_output_and_error()
    {
        string script = "$s='x' * " + (ProcessRunner.MaxCapturedCharactersPerStream + 1000) + "; [Console]::Out.Write($s); [Console]::Error.Write($s)";

        ProcessResult result = await _runner.RunAsync(
            new ProcessCommand("powershell", new[] { "-NoProfile", "-Command", script }, Environment.CurrentDirectory),
            TimeSpan.FromSeconds(20),
            CancellationToken.None);

        Assert.IsLessThan(ProcessRunner.MaxCapturedCharactersPerStream + 200, result.StandardOutput.Length);
        Assert.IsLessThan(ProcessRunner.MaxCapturedCharactersPerStream + 200, result.StandardError.Length);
        StringAssert.Contains(result.StandardOutput, "SMILE truncated 1000 additional stdout characters");
        StringAssert.Contains(result.StandardError, "SMILE truncated 1000 additional stderr characters");
    }
}
