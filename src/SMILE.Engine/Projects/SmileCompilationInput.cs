namespace SMILE.Engine;

// One immutable snapshot is shared by transpilation, formatting and running.
// Hosts load it on a worker thread; the editor can replace its startup text
// without changing dependency ownership or reading files on the UI thread.
public sealed record SmileCompilationInput(string Path, SmileProject? Project, string ProgramName,
    IReadOnlyList<SmileSource> Sources, IReadOnlyDictionary<string, IReadOnlySet<string>> References,
    IReadOnlyList<SmileLibraryIdentity> Dependencies)
{
    public IReadOnlyList<SmileApplicationAsset> Assets { get; init; } = [];
    public bool IsLibrary => Project?.IsLibrary == true;
    public string StartupPath => Sources[0].Path;
    public SmileCompilationInput WithStartupText(string text) => this with
    {
        Sources = Sources.Select((source, index) => index == 0 ? source with { Text = text } : source).ToArray()
    };
    public BindResult Bind() => new SmileTranspiler().BindSources(Sources, IsLibrary, References);
    public IReadOnlyList<TranspileResult> Transpile(IEnumerable<TargetLanguage> targets)
    {
        if (IsLibrary) throw new InvalidOperationException("Build library projects as .smilelib packages.");
        BindResult result = Bind();
        return targets.Distinct().Select(target => result.Success
            ? new TranspileResult(target, CodeGeneratorRegistry.Get(target).Generate(result.Program! with { ProgramName = ProgramName }) with { Assets = Assets }, result.Diagnostics)
            : new TranspileResult(target, null, result.Diagnostics)).ToArray();
    }
    public static SmileCompilationInput Load(string path, IEnumerable<string>? supportSources = null,
        IEnumerable<string>? libraries = null, string? applicationId = null) =>
        new SmileProjectLoader().Load(path, supportSources ?? [], libraries ?? [], applicationId);
}
