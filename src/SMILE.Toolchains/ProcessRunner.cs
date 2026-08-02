using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace SMILE.Toolchains;

public sealed record ProcessCommand(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory)
{
    public static ProcessCommand ForCmd(string command, string workingDirectory) =>
        new("cmd.exe", new[] { "/c", command }, workingDirectory);
}

public sealed record ProcessResult(
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration,
    bool TimedOut,
    bool Cancelled)
{
    public bool Success => ExitCode == 0 && !TimedOut && !Cancelled;
}

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        ProcessCommand command,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(
        ProcessCommand command,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var stopwatch = Stopwatch.StartNew();
        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);

        // Redirecting both streams and reading them asynchronously prevents the
        // classic deadlock where a child process blocks because its output pipe
        // filled while the parent waits for exit.
        using var process = new Process
        {
            StartInfo = CreateStartInfo(command),
            EnableRaisingEvents = true
        };

        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            stopwatch.Stop();
            return new ProcessResult(
                null,
                string.Empty,
                ex.Message,
                stopwatch.Elapsed,
                TimedOut: false,
                Cancelled: cancellationToken.IsCancellationRequested);
        }

        CloseStandardInput(process);

        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();

        bool timedOut = false;
        bool cancelled = false;

        try
        {
            // WaitForExitAsync accepts a cancellation token, so the UI can
            // cancel without blocking a window thread on Process.WaitForExit().
            await process.WaitForExitAsync(linkedSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            timedOut = timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested;
            cancelled = cancellationToken.IsCancellationRequested;

            // Killing the entire process tree matters for compiler tools that
            // launch helper processes. Cancelling only the root process can
            // leave children running in the background.
            TryKillProcessTree(process);
            await WaitForExitAfterKillAsync(process).ConfigureAwait(false);
        }

        string output = await ReadCompletedOutputAsync(outputTask).ConfigureAwait(false);
        string error = await ReadCompletedOutputAsync(errorTask).ConfigureAwait(false);

        stopwatch.Stop();

        int? exitCode = process.HasExited ? process.ExitCode : null;
        return new ProcessResult(exitCode, output, error, stopwatch.Elapsed, timedOut, cancelled);
    }

    private static ProcessStartInfo CreateStartInfo(ProcessCommand command)
    {
        var startInfo = new ProcessStartInfo(command.FileName)
        {
            WorkingDirectory = command.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (string argument in command.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static void CloseStandardInput(Process process)
    {
        try
        {
            // SMILE-run programs are captured by the app, not interacted with
            // directly. Closing stdin makes accidental reads finish or fail
            // instead of waiting forever inside an invisible console.
            process.StandardInput.Close();
        }
        catch (InvalidOperationException)
        {
        }
        catch (IOException)
        {
        }
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static async Task WaitForExitAfterKillAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync().ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static async Task<string> ReadCompletedOutputAsync(Task<string> task)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return string.Empty;
        }
    }
}
