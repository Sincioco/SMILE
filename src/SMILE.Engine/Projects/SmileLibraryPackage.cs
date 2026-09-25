using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SMILE.Engine;

public sealed record SmileLibraryIdentity(string Name, string Version)
{
    public string Provider => Name + "@" + Version;
}

// Archives remain in memory: authored code, rather than extracted files or
// metadata, is the input to binding. Resource limits also apply while reading.
public sealed partial class SmileLibraryPackage
{
    private const int Megabyte = 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public required string Path { get; init; }
    public required SmileLibraryIdentity Identity { get; init; }
    public required IReadOnlyList<string> Modules { get; init; }
    public required IReadOnlyList<SmileLibraryIdentity> Dependencies { get; init; }
    public required IReadOnlyList<SmileSource> Sources { get; init; }
    public required IReadOnlyDictionary<string, string> SourceIds { get; init; }
    public required string PublicApi { get; init; }

    public static SmileLibraryPackage Read(string path)
    {
        try { return ReadEnvelope(path); }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException or DecoderFallbackException)
        { throw new InvalidDataException("Malformed SMILE library package: " + path, error); }
    }

    private static SmileLibraryPackage ReadEnvelope(string path)
    {
        path = System.IO.Path.GetFullPath(path);
        using var file = File.OpenRead(path);
        if (file.Length > 64L * Megabyte) throw new InvalidDataException("SMILE library exceeds 64 MiB.");
        using var archive = new ZipArchive(file, ZipArchiveMode.Read, false, Utf8);
        if (archive.Entries.Count > 1026) throw new InvalidDataException("SMILE library exceeds 1,026 entries.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            ValidateEntryName(entry.FullName);
            if (!names.Add(entry.FullName)) throw new InvalidDataException("Duplicate library entry: " + entry.FullName);
        }
        long expanded = 0;
        byte[] ReadBytes(string name, int maximum)
        {
            ZipArchiveEntry entry = archive.GetEntry(name) ?? throw new InvalidDataException("Missing library entry: " + name);
            if (entry.Length > maximum) throw new InvalidDataException("Library entry exceeds its size limit: " + name);
            using Stream stream = entry.Open();
            using var output = new MemoryStream();
            byte[] buffer = new byte[81920];
            int count;
            while ((count = stream.Read(buffer)) != 0)
            {
                expanded += count;
                if (expanded > 64L * Megabyte || output.Length + count > maximum)
                    throw new InvalidDataException("Library expanded content exceeds its size limit.");
                output.Write(buffer, 0, count);
            }
            return output.ToArray();
        }
        using JsonDocument manifest = JsonDocument.Parse(ReadBytes("manifest.json", 4 * Megabyte));
        JsonElement root = manifest.RootElement;
        if (root.GetProperty("formatVersion").GetInt32() != 7)
            throw new InvalidDataException("SMILE library requires formatVersion 7; rebuild the library with the current compiler.");
        SmileLibraryIdentity identity = ReadIdentity(root);
        if (Text(root, "provider") != identity.Provider) throw new InvalidDataException("Library provider identity is not canonical.");
        string[] modules = Values(root, "modules"), sourceNames = Values(root, "sources");
        if (sourceNames.Length is < 1 or > 1024) throw new InvalidDataException("A library requires 1 to 1,024 sources.");
        SmileLibraryIdentity[] dependencies = root.GetProperty("dependencies").EnumerateArray().Select(ReadIdentity).ToArray();
        if (dependencies.Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != dependencies.Length ||
            dependencies.Any(item => item.Name.Equals(identity.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("A library cannot repeat a dependency or depend on itself.");
        JsonElement hashes = root.GetProperty("sourceHashes");
        if (hashes.EnumerateObject().Count() != sourceNames.Length || hashes.EnumerateObject().Any(item => !sourceNames.Contains(item.Name, StringComparer.Ordinal)))
            throw new InvalidDataException("Library sourceHashes must match sources exactly.");
        var sources = new List<SmileSource>();
        var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in sourceNames.Order(StringComparer.Ordinal))
        {
            ValidateEntryName(name);
            if (!name.StartsWith("src/", StringComparison.Ordinal) || !name.EndsWith(".smile", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Library sources must be .smile files inside src/.");
            byte[] bytes = ReadBytes(name, 4 * Megabyte);
            if (!Hash(bytes).Equals(hashes.GetProperty(name).GetString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Library source hash mismatch: " + name);
            string sourcePath = path + "!/" + name;
            sources.Add(new(sourcePath, Utf8.GetString(bytes), identity.Provider));
            ids.Add(sourcePath, name);
        }
        var allowed = sourceNames.Append("manifest.json").Append("api/public-symbols.json").ToHashSet(StringComparer.Ordinal);
        if (archive.Entries.Any(entry => !allowed.Contains(entry.FullName)))
            throw new InvalidDataException("Library contains an undeclared or executable payload.");
        return new() { Path = path, Identity = identity, Modules = modules, Dependencies = dependencies,
            Sources = sources, SourceIds = ids, PublicApi = Utf8.GetString(ReadBytes("api/public-symbols.json", 16 * Megabyte)) };
    }

    internal void ValidateApi(BoundProgram program)
    {
        string[] actualModules = program.Modules.Where(module => module.Provider == Identity.Provider).Select(module => module.Name).ToArray();
        if (!actualModules.ToHashSet(StringComparer.Ordinal).SetEquals(Modules))
            throw new InvalidDataException("Library module manifest disagrees with its sources: " + Path);
        if (new SmileLibraryApi(program, Identity.Name, Identity.Version, SourceIds).Build() != PublicApi)
            throw new InvalidDataException("Library public API metadata disagrees with its sources; rebuild: " + Path);
    }

    private static SmileLibraryIdentity ReadIdentity(JsonElement value)
    {
        string name = Text(value, "name"), version = Text(value, "version");
        if (!Regex.IsMatch(version, @"^\d+\.\d+\.\d+$", RegexOptions.CultureInvariant))
            throw new InvalidDataException("Library version must be exact major.minor.patch.");
        return new(name, version);
    }
    private static string Text(JsonElement value, string property) => value.GetProperty(property).GetString() is { Length: > 0 } text && !string.IsNullOrWhiteSpace(text)
        ? text : throw new InvalidDataException("Library requires non-empty " + property + ".");
    private static string[] Values(JsonElement root, string property)
    {
        string[] values = root.GetProperty(property).EnumerateArray().Select(value => value.GetString() ?? "").ToArray();
        if (values.Any(string.IsNullOrWhiteSpace) || values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != values.Length)
            throw new InvalidDataException("Library " + property + " contains empty or duplicate values.");
        return values;
    }
    private static void ValidateEntryName(string name)
    {
        if (name.Length > 512 || string.IsNullOrWhiteSpace(name) || name.Contains('\\') || name.Contains(':') ||
            name.Split('/').Any(part => part is "" or "." or "..")) throw new InvalidDataException("Unsafe library entry: " + name);
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
