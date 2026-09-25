using System.IO;
using SMILE.Engine;

namespace SMILE.Desktop;

// The project owns dependencies and identity; the editor owns its startup text.
// Background generation reloads support sources/assets edited in other tools.
internal sealed class DesktopProjectSession(string path, string startupPath)
{
    public string Path { get; } = path;
    public string StartupPath { get; } = startupPath;
    public SmileCompilationInput Snapshot(string text)
    {
        SmileCompilationInput input = SmileCompilationInput.Load(Path);
        if (input.IsLibrary) throw new InvalidDataException("Build library projects with the CLI --target library command.");
        if (!input.StartupPath.Equals(StartupPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("StartupFile changed; reopen the project to select its new startup source.");
        return input.WithStartupText(text);
    }
    public static (DesktopProjectSession? Session, string SourcePath, string Text) Open(string path)
    {
        if (System.IO.Path.GetExtension(path).Equals(".smile", StringComparison.OrdinalIgnoreCase))
            return (null, path, File.ReadAllText(path));
        SmileCompilationInput input = SmileCompilationInput.Load(path);
        if (input.IsLibrary) throw new InvalidDataException("Build library projects with the CLI --target library command.");
        return (new(path, input.StartupPath), input.StartupPath, input.Sources[0].Text);
    }
}
