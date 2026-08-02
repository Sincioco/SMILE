using System.Diagnostics;
using System.IO;

namespace SMILE.Desktop;

public interface IFolderOpener
{
    Task OpenAsync(string folderPath, CancellationToken cancellationToken);
}

public sealed class SystemFolderOpener : IFolderOpener
{
    public Task OpenAsync(string folderPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string fullPath = Path.GetFullPath(folderPath);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"Generated folder does not exist: {fullPath}");
        }

        // KISS stability choice: ask Explorer to open the folder and let the
        // shell decide whether to reuse an existing window. This avoids the
        // fragile COM automation path that can vary by desktop state.
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = QuoteForExplorer(fullPath),
            UseShellExecute = true
        });

        return Task.CompletedTask;
    }

    private static string QuoteForExplorer(string path) =>
        "\"" + path.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
