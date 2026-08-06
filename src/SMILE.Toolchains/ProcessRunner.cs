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
    public ProcessInput StandardInput { get; init; } = ProcessInput.Closed;

    public bool CreateVisibleConsole { get; init; }

    public static ProcessCommand ForCmd(string command, string workingDirectory) =>
        new("cmd.exe", new[] { "/c", command }, workingDirectory);
}

public enum ProcessInputMode
{
    Closed,
    ScriptedText,
    InteractiveInherited
}

public sealed record ProcessInput(
    ProcessInputMode Mode,
    string? ScriptedText = null,
    byte[]? ScriptedBytesValue = null)
{
    public static ProcessInput Closed { get; } = new(ProcessInputMode.Closed);

    public static ProcessInput InteractiveInherited { get; } =
        new(ProcessInputMode.InteractiveInherited);

    public static ProcessInput Scripted(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new ProcessInput(ProcessInputMode.ScriptedText, text);
    }

    public static ProcessInput ScriptedBytes(ReadOnlySpan<byte> bytes) =>
        new(
            ProcessInputMode.ScriptedText,
            ScriptedText: null,
            ScriptedBytesValue: bytes.ToArray());
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

        using CancellationTokenSource? timeoutSource = timeout == Timeout.InfiniteTimeSpan
            ? null
            : new CancellationTokenSource(timeout);
        using CancellationTokenSource linkedSource = timeoutSource is null
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : CancellationTokenSource.CreateLinkedTokenSource(
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
            bool capturesOutput = command.StandardInput.Mode is not ProcessInputMode.InteractiveInherited;
            Task<CapturedStream> outputTask = capturesOutput
                ? DrainStreamAsync(process.StandardOutput, "stdout")
                : Task.FromResult(new CapturedStream(string.Empty, null));
            Task<CapturedStream> errorTask = capturesOutput
                ? DrainStreamAsync(process.StandardError, "stderr")
                : Task.FromResult(new CapturedStream(string.Empty, null));
            Task<string?> inputTask = PrepareStandardInputAsync(
                process,
                command.StandardInput,
                linkedSource.Token);

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
                timedOut = timeoutSource?.IsCancellationRequested == true &&
                    !cancellationToken.IsCancellationRequested;
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
            string? inputWarning = await CompleteInputAsync(inputTask).ConfigureAwait(false);

            stopwatch.Stop();

            int? exitCode = SafeExitCode(process, out string? exitWarning);
            string standardError = AppendDiagnosticsToCapturedError(
                error.Text,
                output.Error,
                error.Error,
                inputWarning,
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

        if (timeout <= TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
        {
            return "Process timeout must be positive or infinite.";
        }

        if (string.IsNullOrWhiteSpace(command.WorkingDirectory))
        {
            return "Process working directory was blank.";
        }

        if (command.CreateVisibleConsole &&
            command.StandardInput.Mode is not ProcessInputMode.InteractiveInherited)
        {
            return "A visible console requires interactive inherited standard streams.";
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
        bool interactive = command.StandardInput.Mode is ProcessInputMode.InteractiveInherited;
        var startInfo = new ProcessStartInfo(command.FileName)
        {
            WorkingDirectory = Path.GetFullPath(command.WorkingDirectory),
            UseShellExecute = command.CreateVisibleConsole,
            RedirectStandardOutput = !interactive,
            RedirectStandardError = !interactive,
            RedirectStandardInput = !interactive,
            CreateNoWindow = !interactive,
            WindowStyle = interactive
                ? ProcessWindowStyle.Normal
                : ProcessWindowStyle.Hidden
        };

        if (!interactive)
        {
            startInfo.StandardInputEncoding = new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true);
            startInfo.StandardOutputEncoding = Encoding.UTF8;
            startInfo.StandardErrorEncoding = Encoding.UTF8;
        }

        foreach (string argument in command.Arguments ?? Array.Empty<string>())
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static async Task<string?> PrepareStandardInputAsync(
        Process process,
        ProcessInput input,
        CancellationToken cancellationToken)
    {
        if (input.Mode is ProcessInputMode.InteractiveInherited)
        {
            return null;
        }

        try
        {
            if (input.Mode is ProcessInputMode.ScriptedText)
            {
                if (input.ScriptedBytesValue is byte[] scriptedBytes)
                {
                    // Raw scripted bytes let conformance tests exercise
                    // malformed UTF-8 without a shell or text encoder silently
                    // repairing the learner's input first.
                    await process.StandardInput.BaseStream.WriteAsync(
                            scriptedBytes.AsMemory(),
                            cancellationToken)
                        .ConfigureAwait(false);
                    await process.StandardInput.BaseStream.FlushAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    await process.StandardInput.WriteAsync(
                            (input.ScriptedText ?? string.Empty).AsMemory(),
                            cancellationToken)
                        .ConfigureAwait(false);
                    await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            // Closed compiler processes and completed scripted runs receive a
            // real EOF. Interactive programs inherit the learner's terminal
            // instead and never pass through this captured-stream path.
            process.StandardInput.Close();
            return null;
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // A generated program may intentionally stop after one invalid
            // line or simply leave extra scripted lines unread. Its closed
            // pipe is normal process behavior; the real exit code and exact
            // child stderr remain authoritative.
            return null;
        }
        catch (Exception ex) when (IsExpectedProcessException(ex) || ex is IOException)
        {
            return $"[SMILE could not finish writing stdin: {ex.GetType().Name}: {ex.Message}]";
        }
    }

    private static async Task<string?> CompleteInputAsync(Task<string?> task)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex) when (IsExpectedStreamException(ex))
        {
            return $"[SMILE could not finish writing stdin: {ex.GetType().Name}: {ex.Message}]";
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

    private static string AppendDiagnosticsToCapturedError(
        string capturedError,
        params string?[] diagnostics)
    {
        string diagnosticText = string.Join(
            Environment.NewLine,
            diagnostics
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.TrimEnd()));

        if (diagnosticText.Length == 0)
        {
            // Runtime conformance compares stderr exactly, including its final
            // line ending. Keep the child's bytes-as-text untouched when the
            // runner itself has nothing to report.
            return capturedError;
        }

        if (capturedError.Length == 0)
        {
            return diagnosticText;
        }

        string separator = capturedError[^1] is '\r' or '\n'
            ? string.Empty
            : Environment.NewLine;
        return capturedError + separator + diagnosticText;
    }

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
