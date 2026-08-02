using System.Diagnostics;
using System.IO;
using Microsoft.CSharp.RuntimeBinder;
using System.Runtime.InteropServices;

namespace SMILE.Desktop;

public static class FolderOpener
{
    private const int SwRestore = 9;

    public static void OpenOrActivate(string folderPath)
    {
        string fullPath = Path.GetFullPath(folderPath);

        if (TryActivateExistingExplorerWindow(fullPath))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = QuoteForExplorer(fullPath),
            UseShellExecute = true
        });
    }

    private static bool TryActivateExistingExplorerWindow(string folderPath)
    {
        Type? shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is null)
        {
            return false;
        }

        object? shell = Activator.CreateInstance(shellType);
        if (shell is null)
        {
            return false;
        }

        try
        {
            // Shell.Application exposes the currently open Explorer windows.
            // Using it here avoids opening duplicate windows for the same
            // generated-code folder; we restore and foreground the existing one.
            dynamic windows = ((dynamic)shell).Windows();
            foreach (dynamic window in windows)
            {
                if (!TryGetExplorerFolderPath(window, out string? existingPath) ||
                    existingPath is null)
                {
                    continue;
                }

                string existingFullPath = Path.GetFullPath(existingPath!);
                if (!string.Equals(existingFullPath, folderPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var handle = new IntPtr((int)window.HWND);
                ShowWindow(handle, SwRestore);
                SetForegroundWindow(handle);
                return true;
            }
        }
        catch (COMException)
        {
            return false;
        }
        catch (RuntimeBinderException)
        {
            return false;
        }
        finally
        {
            if (Marshal.IsComObject(shell))
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }

        return false;
    }

    private static bool TryGetExplorerFolderPath(dynamic window, out string? folderPath)
    {
        try
        {
            folderPath = window.Document?.Folder?.Self?.Path as string;
            return !string.IsNullOrWhiteSpace(folderPath) && Directory.Exists(folderPath);
        }
        catch (COMException)
        {
            folderPath = null;
            return false;
        }
        catch (RuntimeBinderException)
        {
            folderPath = null;
            return false;
        }
    }

    private static string QuoteForExplorer(string path) =>
        "\"" + path.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
