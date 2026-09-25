namespace SMILE.Engine;

internal sealed class SmileProjectLoader
{
    private sealed record Library(string Path, SmileLibraryIdentity Identity, IReadOnlyList<SmileSource> Sources,
        IReadOnlyList<SmileLibraryIdentity> Dependencies, SmileLibraryPackage? Package);
    private readonly Dictionary<string, Library> _libraries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _paths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _loading = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlySet<string>> _references = new(StringComparer.OrdinalIgnoreCase);

    public SmileCompilationInput Load(string path, IEnumerable<string> support, IEnumerable<string> packages, string? applicationId)
    {
        path = System.IO.Path.GetFullPath(path);
        string extension = System.IO.Path.GetExtension(path);
        SmileProject? project = extension.Equals(".smileproj", StringComparison.OrdinalIgnoreCase) || extension.Equals(".smilelibproj", StringComparison.OrdinalIgnoreCase)
            ? SmileProject.Load(path) : null;
        if (project is null && !extension.Equals(".smile", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Open a .smile, .smileproj or .smilelibproj input.");
        if (project is not null && (support.Any() || packages.Any()))
            throw new InvalidDataException("Project source and library references belong in the project file.");
        string provider = project?.IsLibrary == true ? new SmileLibraryIdentity(project.LibraryName, project.Version).Provider : path;
        SmileSource[] ownSources = project is not null ? ReadSources(project, provider)
            : new[] { path }.Concat(support.Select(System.IO.Path.GetFullPath)).Select(source => ReadSource(source, provider)).ToArray();
        SmileLibraryIdentity[] dependencies = project is not null
            ? project.References.Select(reference => AddLibrary(reference.Path)).ToArray()
            : packages.Select(AddLibrary).ToArray();
        if (project?.IsLibrary == true)
        {
            var identity = new SmileLibraryIdentity(project.LibraryName, project.Version);
            Register(new(path, identity, ownSources, dependencies, null));
        }
        _references[provider] = dependencies.Select(item => item.Provider).ToHashSet(StringComparer.OrdinalIgnoreCase);
        ResolveGraph();
        // Validate each provider using only its declared graph, before an
        // application's globals or unrelated siblings could affect binding.
        foreach (Library library in _libraries.Values)
        {
            if (library.Path.Equals(path, StringComparison.OrdinalIgnoreCase)) continue;
            SmileSource[] sources = Closure(library).SelectMany(item => item.Sources).ToArray();
            BindResult result = new SmileTranspiler().BindSources(sources, library: true, _references);
            if (!result.Success) throw new SmileInputException(result.Diagnostics.First(item => item.Severity == DiagnosticSeverity.Error));
            library.Package?.ValidateApi(result.Program!);
        }
        string programName = project is not null ? SmileApplicationIdentity.Resolve(project, applicationId)
            : applicationId is not null ? SmileApplicationIdentity.Validate(applicationId, default) : System.IO.Path.GetFileNameWithoutExtension(path);
        SmileSource[] allSources = ownSources.Concat(_libraries.Values.Where(library => library.Identity.Provider != provider).SelectMany(library => library.Sources)).ToArray();
        return new(path, project, programName, allSources, _references, dependencies)
        {
            Assets = project is not null ? SmileProjectAssets.Resolve(project) : []
        };
    }

    private SmileLibraryIdentity AddLibrary(string path)
    {
        path = System.IO.Path.GetFullPath(path);
        if (_loading.Contains(path)) throw new InvalidDataException("Library project reference cycle: " + path);
        if (_paths.TryGetValue(path, out string? provider)) return _libraries[provider].Identity;
        _loading.Add(path);
        Library library;
        if (System.IO.Path.GetExtension(path).Equals(".smilelib", StringComparison.OrdinalIgnoreCase))
        {
            SmileLibraryPackage package = SmileLibraryPackage.Read(path);
            library = new(path, package.Identity, package.Sources, package.Dependencies, package);
        }
        else
        {
            SmileProject project = SmileProject.Load(path);
            if (!project.IsLibrary) throw new InvalidDataException("Referenced projects must be libraries: " + path);
            var identity = new SmileLibraryIdentity(project.LibraryName, project.Version);
            SmileLibraryIdentity[] dependencies = project.References.Select(reference => AddLibrary(reference.Path)).ToArray();
            library = new(path, identity, ReadSources(project, identity.Provider), dependencies, null);
        }
        Register(library);
        _loading.Remove(path);
        return library.Identity;
    }

    private void Register(Library library)
    {
        Library? other = _libraries.Values.FirstOrDefault(item => item.Identity.Name.Equals(library.Identity.Name, StringComparison.OrdinalIgnoreCase));
        if (other is not null) throw new InvalidDataException($"Duplicate or conflicting provider for library '{library.Identity.Name}': {other.Path} and {library.Path}");
        _libraries.Add(library.Identity.Provider, library);
        _paths.Add(library.Path, library.Identity.Provider);
        _references[library.Identity.Provider] = library.Dependencies.Select(item => item.Provider).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private void ResolveGraph()
    {
        foreach (Library library in _libraries.Values)
        {
            foreach (SmileLibraryIdentity dependency in library.Dependencies)
                if (!_libraries.ContainsKey(dependency.Provider))
                    throw new InvalidDataException($"Library '{library.Identity.Provider}' requires exact dependency '{dependency.Provider}'. Add an explicit project or package reference.");
            _ = Closure(library).ToArray();
        }
    }

    private IEnumerable<Library> Closure(Library root)
    {
        var result = new List<Library>();
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Visit(Library library)
        {
            if (active.Contains(library.Identity.Provider)) throw new InvalidDataException("Library dependency cycle: " + library.Identity.Provider);
            if (!seen.Add(library.Identity.Provider)) return;
            active.Add(library.Identity.Provider);
            result.Add(library);
            foreach (SmileLibraryIdentity dependency in library.Dependencies) Visit(_libraries[dependency.Provider]);
            active.Remove(library.Identity.Provider);
        }
        Visit(root);
        return result;
    }

    private static SmileSource[] ReadSources(SmileProject project, string provider) => project.CompilationSources
        .Select(source => ReadSource(source.Path, provider)).ToArray();
    private static SmileSource ReadSource(string path, string provider) => new(path, File.ReadAllText(path), provider);
}
