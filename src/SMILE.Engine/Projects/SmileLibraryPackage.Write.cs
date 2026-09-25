using System.Diagnostics;
using System.IO.Compression;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace SMILE.Engine;

public sealed partial class SmileLibraryPackage
{
    public static void Write(string outputPath, SmileCompilationInput input, CancellationToken cancellationToken = default)
    {
        if (input.Project is not { IsLibrary: true } project) throw new InvalidDataException("A package requires a library project.");
        BindResult bind = input.Bind();
        if (!bind.Success) throw new SmileInputException(bind.Diagnostics.First(item => item.Severity == DiagnosticSeverity.Error));
        BoundProgram program = bind.Program!;
        string provider = new SmileLibraryIdentity(project.LibraryName, project.Version).Provider;
        ModuleSymbol[] modules = program.Modules.Where(module => module.Provider == provider).ToArray();
        if (modules.Length == 0) throw new InvalidDataException("A library must declare a Module.");
        var ids = project.Sources.ToDictionary(source => source.Path, source => "src/" + source.Include.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase);
        var entries = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (SmileSource source in input.Sources.Where(source => source.Provider == provider))
        {
            string name = ids[source.Path];
            ValidateEntryName(name);
            entries.Add(name, Utf8.GetBytes(source.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')));
        }
        entries.Add("manifest.json", Utf8.GetBytes(Manifest(project, modules.Select(module => module.Name), entries, input.Dependencies)));
        entries.Add("api/public-symbols.json", Utf8.GetBytes(new SmileLibraryApi(program, project.LibraryName, project.Version, ids).Build()));
        outputPath = System.IO.Path.GetFullPath(outputPath);
        if (!System.IO.Path.GetExtension(outputPath).Equals(".smilelib", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A library output must use the .smilelib extension.");
        string directory = System.IO.Path.GetDirectoryName(outputPath)!;
        Directory.CreateDirectory(directory);
        using FileStream outputLock = AcquireLock(outputPath, cancellationToken);
        string temporary = System.IO.Path.Combine(directory, "." + System.IO.Path.GetFileName(outputPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using (var archive = new ZipArchive(file, ZipArchiveMode.Create, true, Utf8))
                    foreach ((string name, byte[] bytes) in entries)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
                        entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                        using Stream stream = entry.Open();
                        stream.Write(bytes);
                    }
                file.Flush(true);
            }
            _ = Read(temporary);
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(outputPath)) File.Replace(temporary, outputPath, null);
            else File.Move(temporary, outputPath);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static FileStream AcquireLock(string path, CancellationToken cancellationToken)
    {
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose); }
            catch (IOException) when (elapsed.Elapsed < TimeSpan.FromSeconds(5))
            { cancellationToken.WaitHandle.WaitOne(50); }
        }
    }

    private static string Manifest(SmileProject project, IEnumerable<string> modules, IReadOnlyDictionary<string, byte[]> sources,
        IReadOnlyList<SmileLibraryIdentity> dependencies)
    {
        string Quote(string value) => JsonSerializer.Serialize(value, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        string moduleJson = string.Join(", ", modules.Order(StringComparer.OrdinalIgnoreCase).ThenBy(name => name, StringComparer.Ordinal).Select(Quote));
        string sourceJson = string.Join(", ", sources.Keys.Select(Quote));
        string hashes = string.Join(",\n", sources.Select(item => "    " + Quote(item.Key) + ": " + Quote(Hash(item.Value))));
        string dependencyJson = string.Join(",\n", dependencies.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.Ordinal).ThenBy(item => item.Version, StringComparer.Ordinal)
            .Select(item => "    {\"name\": " + Quote(item.Name) + ", \"version\": " + Quote(item.Version) + "}"));
        return "{\n  \"formatVersion\": 7,\n  \"name\": " + Quote(project.LibraryName) + ",\n  \"version\": " + Quote(project.Version) +
            ",\n  \"provider\": " + Quote(project.LibraryName + "@" + project.Version) + ",\n  \"modules\": [" + moduleJson +
            "],\n  \"sources\": [" + sourceJson + "],\n  \"sourceHashes\": {\n" + hashes + "\n  },\n  \"dependencies\": [\n" + dependencyJson + "\n  ]\n}\n";
    }
}
