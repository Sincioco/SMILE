namespace SMILE.Engine;

public sealed partial class SmileTranspiler
{
    // The first source is startup. Later application sources supply declarations
    // and initializers; modules initialize after their imported dependencies.
    public BindResult BindSources(IReadOnlyList<SmileSource> sources, bool library = false,
        IReadOnlyDictionary<string, IReadOnlySet<string>>? references = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Select(source => source.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != sources.Count)
            return new(null, [new("SMILE3509", DiagnosticSeverity.Error, "A compilation cannot contain the same physical source twice.", default)]);
        return new ModuleCompilation().Bind(sources, library, references);
    }

    public IReadOnlyList<TranspileResult> TranspileSources(IReadOnlyList<SmileSource> sources,
        IEnumerable<TargetLanguage> targets, string programName = "Program")
    {
        BindResult bind = BindSources(sources);
        return targets.Distinct().Select(target => bind.Success
            ? new TranspileResult(target, CodeGeneratorRegistry.Get(target).Generate(bind.Program! with { ProgramName = programName }), bind.Diagnostics)
            : new TranspileResult(target, null, bind.Diagnostics)).ToArray();
    }
}
