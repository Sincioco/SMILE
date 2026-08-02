using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace SMILE.Desktop;

public interface IAppErrorReporter
{
    string SessionId { get; }

    string Report(
        string operation,
        Exception exception,
        string? target = null,
        string? stage = null,
        long? sourceRevision = null);
}

public sealed class AppErrorReporter : IAppErrorReporter
{
    private readonly string _preferredLogRoot;
    private readonly string _fallbackLogRoot;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Assembly _versionAssembly;

    public AppErrorReporter(
        string? preferredLogRoot = null,
        string? fallbackLogRoot = null,
        string? sessionId = null,
        Func<DateTimeOffset>? clock = null,
        Assembly? versionAssembly = null)
    {
        _preferredLogRoot = preferredLogRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SMILE",
            "Logs");
        _fallbackLogRoot = fallbackLogRoot ?? Path.Combine(Path.GetTempPath(), "SMILE", "Logs");
        SessionId = string.IsNullOrWhiteSpace(sessionId)
            ? DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]
            : sessionId;
        _clock = clock ?? (() => DateTimeOffset.Now);
        _versionAssembly = versionAssembly ?? typeof(AppErrorReporter).Assembly;
    }

    public static AppErrorReporter Shared { get; } = new();

    public string SessionId { get; }

    public string Report(
        string operation,
        Exception exception,
        string? target = null,
        string? stage = null,
        long? sourceRevision = null)
    {
        try
        {
            string entry = BuildEntry(operation, exception, target, stage, sourceRevision);
            return TryWrite(_preferredLogRoot, entry) ??
                   TryWrite(_fallbackLogRoot, entry) ??
                   "diagnostic log unavailable";
        }
        catch
        {
            // Logging is best-effort by design. The reporter exists to contain
            // failures, so it must never become another source of UI crashes.
            return "diagnostic log unavailable";
        }
    }

    private string BuildEntry(
        string operation,
        Exception exception,
        string? target,
        string? stage,
        long? sourceRevision)
    {
        DateTimeOffset local = _clock();
        string version =
            _versionAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
            _versionAssembly.GetName().Version?.ToString() ??
            "unknown";

        var builder = new StringBuilder();
        builder.AppendLine("=== SMILE diagnostic ===");
        builder.AppendLine($"UTC timestamp: {local.UtcDateTime:O}");
        builder.AppendLine($"Local timestamp: {local:O}");
        builder.AppendLine($"Session ID: {SessionId}");
        builder.AppendLine($"Application version: {version}");
        builder.AppendLine($"Operation: {operation}");
        builder.AppendLine($"Target: {target ?? "n/a"}");
        builder.AppendLine($"Stage: {stage ?? "n/a"}");
        builder.AppendLine($"Source revision: {(sourceRevision.HasValue ? sourceRevision.Value.ToString() : "n/a")}");
        builder.AppendLine($"Exception type: {exception.GetType().FullName}");
        builder.AppendLine($"Exception message: {exception.Message}");
        builder.AppendLine($"OS version: {Environment.OSVersion}");
        builder.AppendLine($"Process architecture: {RuntimeInformation.ProcessArchitecture}");
        builder.AppendLine($"Current thread ID: {Environment.CurrentManagedThreadId}");
        builder.AppendLine($"WPF dispatcher thread: {IsDispatcherThread()}");
        builder.AppendLine("Exception:");
        builder.AppendLine(exception.ToString());
        builder.AppendLine();
        return builder.ToString();
    }

    private static bool IsDispatcherThread()
    {
        try
        {
            return Application.Current?.Dispatcher.CheckAccess() ?? false;
        }
        catch
        {
            return false;
        }
    }

    private static string? TryWrite(string logRoot, string entry)
    {
        try
        {
            Directory.CreateDirectory(logRoot);
            string path = Path.Combine(logRoot, "SMILE-" + DateTimeOffset.Now.ToString("yyyy-MM-dd") + ".log");
            File.AppendAllText(path, entry, Encoding.UTF8);
            return path;
        }
        catch
        {
            return null;
        }
    }
}

internal static class DesktopExceptionPolicy
{
    public static bool IsFatal(Exception exception) =>
        exception is OutOfMemoryException or AccessViolationException;
}
