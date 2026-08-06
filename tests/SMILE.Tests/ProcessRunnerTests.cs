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
    public async Task Process_runner_closes_standard_input_by_default()
    {
        const string script = "$value=[Console]::In.ReadToEnd(); [Console]::Out.Write($value.Length)";
        ProcessResult result = await _runner.RunAsync(
            new ProcessCommand(
                "powershell",
                new[] { "-NoProfile", "-Command", script },
                Environment.CurrentDirectory),
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.IsTrue(result.Success, result.StandardError);
        Assert.AreEqual("0", result.StandardOutput);
    }

    [TestMethod]
    public async Task Process_runner_writes_scripted_text_exactly_and_then_closes_input()
    {
        const string scripted = "  Sin\0\t\u4f60\u597d\r\nfinal-without-newline";
        const string script = "[Console]::InputEncoding=[System.Text.UTF8Encoding]::new($false); [Console]::OutputEncoding=[System.Text.UTF8Encoding]::new($false); $value=[Console]::In.ReadToEnd(); [Console]::Out.Write($value); [Console]::Error.Write($value.Length)";
        var command = new ProcessCommand(
            "powershell",
            new[] { "-NoProfile", "-Command", script },
            Environment.CurrentDirectory)
        {
            StandardInput = ProcessInput.Scripted(scripted)
        };

        ProcessResult result = await _runner.RunAsync(
            command,
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.IsTrue(result.Success, result.StandardError);
        Assert.AreEqual(scripted, result.StandardOutput);
        Assert.AreEqual(scripted.Length.ToString(), result.StandardError);
    }

    [TestMethod]
    public async Task Process_runner_writes_raw_scripted_bytes_without_UTF8_repair()
    {
        byte[] scripted = [0xC3, 0x28, 0x00, 0x1A, 0xFF];
        const string script = "$stream=[Console]::OpenStandardInput(); $memory=New-Object IO.MemoryStream; $stream.CopyTo($memory); [Console]::Out.Write(([BitConverter]::ToString($memory.ToArray())).Replace('-', ''))";
        var command = new ProcessCommand(
            "powershell",
            new[] { "-NoProfile", "-Command", script },
            Environment.CurrentDirectory)
        {
            StandardInput = ProcessInput.ScriptedBytes(scripted)
        };

        ProcessResult result = await _runner.RunAsync(
            command,
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.IsTrue(result.Success, result.StandardError);
        Assert.AreEqual("C328001AFF", result.StandardOutput);
    }

    [TestMethod]
    public async Task Process_runner_can_inherit_interactive_standard_streams_without_capture()
    {
        ProcessCommand command = ProcessCommand.ForCmd("exit /b 0", Environment.CurrentDirectory) with
        {
            StandardInput = ProcessInput.InteractiveInherited
        };

        ProcessResult result = await _runner.RunAsync(
            command,
            Timeout.InfiniteTimeSpan,
            CancellationToken.None);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(string.Empty, result.StandardOutput);
        Assert.AreEqual(string.Empty, result.StandardError);
    }

    [TestMethod]
    public async Task Process_runner_preserves_captured_stderr_line_endings_exactly()
    {
        const string script = "[Console]::OutputEncoding=[System.Text.UTF8Encoding]::new($false); [Console]::Error.Write(\" line `n\")";
        ProcessResult result = await _runner.RunAsync(
            new ProcessCommand(
                "powershell",
                new[] { "-NoProfile", "-Command", script },
                Environment.CurrentDirectory),
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.IsTrue(result.Success, result.StandardError);
        Assert.AreEqual(" line \n", result.StandardError);
    }

    [TestMethod]
    public async Task Unread_scripted_input_does_not_pollute_the_childs_exact_stderr()
    {
        const string script = "$null=[Console]::In.ReadLine(); [Console]::Error.Write(\"runtime error`n\"); exit 1";
        string scripted = "invalid\n" + new string('x', 2_000_000);
        var command = new ProcessCommand(
            "powershell",
            new[] { "-NoProfile", "-Command", script },
            Environment.CurrentDirectory)
        {
            StandardInput = ProcessInput.Scripted(scripted)
        };

        ProcessResult result = await _runner.RunAsync(
            command,
            TimeSpan.FromSeconds(10),
            CancellationToken.None);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(1, result.ExitCode);
        Assert.AreEqual("runtime error\n", result.StandardError);
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
    public async Task Process_runner_times_out_a_scripted_input_program_after_closing_EOF()
    {
        const string script = "$null=[Console]::In.ReadToEnd(); Start-Sleep -Seconds 30";
        var command = new ProcessCommand(
            "powershell",
            new[] { "-NoProfile", "-Command", script },
            Environment.CurrentDirectory)
        {
            StandardInput = ProcessInput.Scripted("value\n")
        };

        ProcessResult result = await _runner.RunAsync(
            command,
            TimeSpan.FromMilliseconds(200),
            CancellationToken.None);

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.TimedOut);
        Assert.IsFalse(result.Cancelled);
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
    public async Task Process_runner_cancels_an_inherited_interactive_program_with_no_timeout()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        ProcessCommand command = ProcessCommand.ForCmd(
            "ping -n 30 127.0.0.1 >nul",
            Environment.CurrentDirectory) with
        {
            StandardInput = ProcessInput.InteractiveInherited
        };

        ProcessResult result = await _runner.RunAsync(
            command,
            Timeout.InfiniteTimeSpan,
            cancellation.Token);

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Cancelled);
        Assert.IsFalse(result.TimedOut);
        Assert.AreEqual(string.Empty, result.StandardOutput);
        Assert.AreEqual(string.Empty, result.StandardError);
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
