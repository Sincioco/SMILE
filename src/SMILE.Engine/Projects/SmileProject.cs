using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace SMILE.Engine;

public sealed record SmileProjectSource(string Include, string Path, bool StartupOnly, bool IsStartup);
public sealed record SmileProjectReference(string Path, bool IsPackage);
public sealed record SmileAssetInclude(string Pattern, TextSpan Span);

public sealed record SmileProject(string Path, string Kind, string OutputName, string? ApplicationId,
    string LibraryName, string Version, IReadOnlyList<SmileProjectSource> Sources,
    IReadOnlyList<SmileProjectReference> References, IReadOnlyList<SmileAssetInclude> Assets)
{
    public bool IsLibrary => Kind.Equals("Library", StringComparison.OrdinalIgnoreCase);
    public string EffectiveApplicationId => ApplicationId ?? OutputName;
    public IEnumerable<SmileProjectSource> CompilationSources => IsLibrary ? Sources
        : Sources.Where(source => source.IsStartup).Concat(Sources.Where(source => !source.IsStartup && !source.StartupOnly));

    public static SmileProject Load(string path) => Parse(path, File.ReadAllText(path));

    public static SmileProject Parse(string path, string xml)
    {
        path = System.IO.Path.GetFullPath(path);
        XElement? root = XDocument.Parse(xml, LoadOptions.SetLineInfo).Root;
        if (root?.Name.LocalName != "SmileProject") throw new InvalidDataException("A SMILE project requires a SmileProject root.");
        string directory = System.IO.Path.GetDirectoryName(path)!;
        XElement[] propertyGroups = root.Elements().Where(element => element.Name.LocalName == "PropertyGroup").ToArray();
        string Property(string name) => propertyGroups.FirstOrDefault()?.Elements().FirstOrDefault(element => element.Name.LocalName == name)?.Value.Trim() ?? "";
        string kind = Property("ProjectKind");
        bool libraryExtension = System.IO.Path.GetExtension(path).Equals(".smilelibproj", StringComparison.OrdinalIgnoreCase);
        if (kind.Length == 0) kind = libraryExtension ? "Library" : "Console";
        bool library = kind.Equals("Library", StringComparison.OrdinalIgnoreCase);
        if (!library && !kind.Equals("Console", StringComparison.OrdinalIgnoreCase) && !kind.Equals("Game", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Unknown ProjectKind '{kind}'.");
        if (libraryExtension && !library) throw new InvalidDataException("A .smilelibproj must have ProjectKind Library.");
        string name = Property("LibraryName"), version = Property("Version"), output = Property("OutputName");
        if (library && (name.Length == 0 || !Regex.IsMatch(version, @"^\d+\.\d+\.\d+$", RegexOptions.CultureInvariant)))
            throw new InvalidDataException("A library requires LibraryName and a major.minor.patch Version.");
        if (output.Length == 0) output = library ? name : System.IO.Path.GetFileNameWithoutExtension(path);
        XElement[] identities = propertyGroups.SelectMany(group => group.Elements()).Where(element => element.Name.LocalName == "ApplicationId").ToArray();
        if (identities.Length > 1) throw Failure("SMILE3801", "ApplicationId may be declared only once.", identities[1], path);
        string? applicationId = null;
        if (identities.Length == 1)
        {
            if (library) throw Failure("SMILE3802", "A library does not own an ApplicationId.", identities[0], path);
            applicationId = SmileApplicationIdentity.Validate(identities[0].Value.Trim(), Span(identities[0], path));
        }
        string startup = Property("StartupFile");
        if (library && startup.Length > 0) throw new InvalidDataException("Library projects do not have a StartupFile.");
        if (!library && startup.Length == 0) startup = "Program.smile";
        string startupPath = library ? "" : System.IO.Path.GetFullPath(System.IO.Path.Combine(directory, startup));
        XElement[] items = root.Elements().Where(element => element.Name.LocalName == "ItemGroup").SelectMany(group => group.Elements()).ToArray();
        XElement[] sourceItems = items.Where(element => element.Name.LocalName == "SmileSource").ToArray();
        if (sourceItems.Length == 0 && !library) sourceItems = [new("SmileSource", new XAttribute("Include", startup))];
        if (sourceItems.Length == 0) throw new InvalidDataException("A library requires at least one SmileSource.");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sources = new List<SmileProjectSource>();
        foreach (XElement element in sourceItems)
        {
            string include = Include(element, ".smile");
            string fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(directory, include));
            if (!paths.Add(fullPath)) throw Failure("SMILE3520", $"Duplicate SmileSource '{include}'.", element, path);
            bool startupOnly = false;
            if (element.Attribute("StartupOnly") is { } attribute && !bool.TryParse(attribute.Value.Trim(), out startupOnly))
                throw Failure("SMILE3520", "StartupOnly must be true or false.", element, path);
            sources.Add(new(include, fullPath, startupOnly, StringComparer.OrdinalIgnoreCase.Equals(fullPath, startupPath)));
        }
        if (!library && !paths.Contains(startupPath)) throw new InvalidDataException("StartupFile must be listed as a SmileSource.");
        var references = new List<SmileProjectReference>();
        paths.Clear();
        foreach (XElement element in items.Where(element => element.Name.LocalName is "SmileProjectReference" or "SmileLibraryReference"))
        {
            bool package = element.Name.LocalName == "SmileLibraryReference";
            string fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(directory, Include(element, package ? ".smilelib" : ".smilelibproj")));
            if (!paths.Add(fullPath)) throw Failure("SMILE3520", "Duplicate library reference.", element, path);
            references.Add(new(fullPath, package));
        }
        SmileAssetInclude[] assets = items.Where(element => element.Name.LocalName == "Asset")
            .Select(element => new SmileAssetInclude(((string?)element.Attribute("Include") ?? "").Trim(), Span(element, path))).ToArray();
        if (library && assets.Length > 0) throw new SmileInputException(new("SMILE3600", DiagnosticSeverity.Error, "Library projects cannot own application assets.", assets[0].Span));
        return new(path, kind, output, applicationId, name, version, sources, references, assets);
    }

    private static string Include(XElement element, string extension)
    {
        string include = ((string?)element.Attribute("Include") ?? "").Trim();
        if (!System.IO.Path.GetExtension(include).Equals(extension, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{element.Name.LocalName} Include requires a {extension} path.");
        return include;
    }

    private static TextSpan Span(XElement element, string path)
    {
        var location = (IXmlLineInfo)element;
        return new(0, element.Value.Length, location.HasLineInfo() ? location.LineNumber : 1,
            location.HasLineInfo() ? location.LinePosition : 1) { SourcePath = path };
    }

    private static SmileInputException Failure(string code, string message, XElement element, string path) =>
        new(new(code, DiagnosticSeverity.Error, message, Span(element, path)));
}

public sealed class SmileInputException(Diagnostic diagnostic) : Exception(diagnostic.ToString())
{
    public Diagnostic Diagnostic { get; } = diagnostic;
}
