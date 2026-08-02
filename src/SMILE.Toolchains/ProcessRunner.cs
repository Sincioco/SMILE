using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
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
    public const int MaxCapturedCharactersPerStream = 1_000_000;
    private static readonly TimeSpan KillGracePeriod = TimeSpan.FromSeconds(5);

    public async Task<ProcessResult> RunAsync(
        ProcessCommand command,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        string? validationError = Validate(command, timeout);
        if (validationError is not null)
        {
            stopwatch.Stop();
            return Failure(validationError, stopwatch.Elapsed, cancellationToken.IsCancellationRequested);
        }

        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);

        Process? process = null;
        try
        {
            process = new Process
            {
                StartInfo = CreateStartInfo(command),
                EnableRaisingEvents = true
            };

            process.Start();
        }
        catch (Exception ex) when (IsExpectedProcessException(ex))
        {
            process?.Dispose();
            stopwatch.Stop();
            return Failure(
                $"Process launch failed: {ex.GetType().Name}: {ex.Message}",
                stopwatch.Elapsed,
                cancellationToken.IsCancellationRequested);
        }

        using (process)
        {
            CloseStandardInput(process);

            // The stream drainers keep reading even after the display cap is
            // reached. That prevents child-process pipe deadlocks while also
            // protecting the desktop process from unbounded output growth.
            Task<CapturedStream> outputTask = DrainStreamAsync(process.StandardOutput, "stdout");
            Task<CapturedStream> errorTask = DrainStreamAsync(process.StandardError, "stderr");

            bool timedOut = false;
            bool cancelled = false;
            string? waitWarning = null;
            string? killWarning = null;

            try
            {
                await process.WaitForExitAsync(linkedSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                timedOut = timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested;
                cancelled = cancellationToken.IsCancellationRequested;

                killWarning = TryKillProcessTree(process);
                waitWarning = await WaitForExitAfterKillAsync(process).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsExpectedProcessException(ex))
            {
                waitWarning = $"Process wait failed: {ex.GetType().Name}: {ex.Message}";
            }

            CapturedStream output = await CompleteStreamAsync(outputTask, "stdout").ConfigureAwait(false);
            CapturedStream error = await CompleteStreamAsync(errorTask, "stderr").ConfigureAwait(false);

            stopwatch.Stop();

            int? exitCode = SafeExitCode(process, out string? exitWarning);
            string standardError = JoinNonEmpty(
                error.Text,
                output.Error,
                error.Error,
                killWarning,
                waitWarning,
                exitWarning);

            return new ProcessResult(
                exitCode,
                output.Text,
                standardError,
                stopwatch.Elapsed,
                timedOut,
                cancelled);
        }
    }

    private static string? Validate(ProcessCommand? command, TimeSpan timeout)
    {
        if (command is null)
        {
            return "Process command was not provided.";
        }

        if (string.IsNullOrWhiteSpace(command.FileName))
        {
            return "Process filename was blank.";
        }

        if (timeout <= TimeSpan.Zero)
        {
            return "Process timeout must be positive.";
        }

        if (string.IsNullOrWhiteSpace(command.WorkingDirectory))
        {
            return "Process working directory was blank.";
        }

        try
        {
            string fullWorkingDirectory = Path.GetFullPath(command.WorkingDirectory);
            if (!Directory.Exists(fullWorkingDirectory))
            {
                return $"Process working directory does not exist: {fullWorkingDirectory}";
            }
        }
        catch (Exception ex) when (IsExpectedProcessException(ex))
        {
            return $"Process working directory was invalid: {ex.GetType().Name}: {ex.Message}";
        }

        return null;
    }

    private static ProcessResult Failure(string message, TimeSpan duration, bool cancelled) =>
        new(
            null,
            string.Empty,
            message,
            duration,
            TimedOut: false,
            Cancelled: cancelled);

    private static ProcessStartInfo CreateStartInfo(ProcessCommand command)
    {
        var startInfo = new ProcessStartInfo(command.FileName)
        {
            WorkingDirectory = Path.GetFullPath(command.WorkingDirectory),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (string argument in command.Arguments ?? Array.Empty<string>())
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
        catch (Exception ex) when (IsExpectedProcessException(ex) || ex is IOException)
        {
        }
    }

    private static string? TryKillProcessTree(Process process)
    {
        try
        {
            if (!SafeHasExited(process, out string? hasExitedWarning))
            {
                process.Kill(entireProcessTree: true);
            }

            return hasExitedWarning;
        }
        catch (Exception ex) when (IsExpectedProcessException(ex))
        {
            return $"Process termination warning: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private static async Task<string?> WaitForExitAfterKillAsync(Process process)
    {
        try
        {
            using var graceSource = new CancellationTokenSource(KillGracePeriod);
            await process.WaitForExitAsync(graceSource.Token).ConfigureAwait(false);
            return null;
        }
        catch (OperationCanceledException)
        {
            return "Process termination warning: process-tree exit could not be confirmed before the kill grace period expired.";
        }
        catch (Exception ex) when (IsExpectedProcessException(ex))
        {
            return $"Process termination warning: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private static bool SafeHasExited(Process process, out string? warning)
    {
        try
        {
            warning = null;
            return process.HasExited;
        }
        catch (Exception ex) when (IsExpectedProcessException(ex))
        {
            warning = $"Process status warning: {ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    private static int? SafeExitCode(Process process, out string? warning)
    {
        try
        {
            warning = null;
            return process.HasExited ? process.ExitCode : null;
        }
        catch (Exception ex) when (IsExpectedProcessException(ex))
        {
            warning = $"Process exit-code warning: {ex.GetType().Name}: {ex.Message}";
            return null;
        }
    }

    private static async Task<CapturedStream> CompleteStreamAsync(
        Task<CapturedStream> task,
        string streamName)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        catch (Exception ex) when (IsExpectedStreamException(ex))
        {
            return new CapturedStream(
                string.Empty,
                $"[SMILE could not finish reading {streamName}: {ex.GetType().Name}: {ex.Message}]");
        }
    }

    private static async Task<CapturedStream> DrainStreamAsync(
        StreamReader reader,
        string streamName)
    {
        var builder = new StringBuilder();
        var buffer = new char[8192];
        long omitted = 0;

        try
        {
            while (true)
            {
                int read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length)).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                int remaining = MaxCapturedCharactersPerStream - builder.Length;
                if (remaining > 0)
                {
                    int toAppend = Math.Min(remaining, read);
                    builder.Append(buffer, 0, toAppend);
                    omitted += read - toAppend;
                }
                else
                {
                    omitted += read;
                }
            }
        }
        catch (Exception ex) when (IsExpectedStreamException(ex))
        {
            return new CapturedStream(
                AppendTruncationMarker(builder, streamName, omitted),
                $"[SMILE could not finish reading {streamName}: {ex.GetType().Name}: {ex.Message}]");
        }

        return new CapturedStream(AppendTruncationMarker(builder, streamName, omitted), null);
    }

    private static string AppendTruncationMarker(StringBuilder builder, string streamName, long omitted)
    {
        if (omitted <= 0)
        {
            return builder.ToString();
        }

        if (builder.Length > 0 && builder[^1] != '\n')
        {
            builder.AppendLine();
        }

        builder.Append("[SMILE truncated ");
        builder.Append(omitted.ToString(CultureInfo.InvariantCulture));
        builder.Append(" additional ");
        builder.Append(streamName);
        builder.Append(" characters.]");
        return builder.ToString();
    }

    private static string JoinNonEmpty(params string?[] values) =>
        string.Join(
            Environment.NewLine,
            values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.TrimEnd()));

    private static bool IsExpectedProcessException(Exception exception) =>
        exception is Win32Exception or
            InvalidOperationException or
            IOException or
            UnauthorizedAccessException or
            ArgumentException or
            NotSupportedException or
            ObjectDisposedException;

    private static bool IsExpectedStreamException(Exception exception) =>
        exception is IOException or InvalidOperationException or ObjectDisposedException;

    private sealed record CapturedStream(string Text, string? Error);
}
