namespace SMILE.Engine;

public sealed record GeneratedFile(
    string RelativePath,
    string Content,
    bool IsPrimary);

public sealed record GeneratedProgram(
    TargetLanguage Language,
    IReadOnlyList<GeneratedFile> Files,
    bool RequiresStandardInput = false)
{
    public GeneratedFile PrimaryFile => Files.Single(file => file.IsPrimary);
    public IReadOnlyList<SmileApplicationAsset> Assets { get; init; } = [];
}

public interface ICodeGenerator
{
    TargetLanguage Language { get; }

    // Generators consume the bound program, not source text. That keeps target
    // backends honest: they all see the same typed variables, calls, arrays,
    // and expressions resolved by the binder.
    GeneratedProgram Generate(BoundProgram program);
}

public sealed record TranspileResult(
    TargetLanguage Language,
    GeneratedProgram? GeneratedProgram,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool Success =>
        GeneratedProgram is not null &&
        Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
}

public sealed partial class SmileTranspiler
{
    public ParseResult Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new Parser(source).Parse();
    }

    public BindResult Bind(string source)
    {
        ParseResult parseResult = Parse(source);
        if (!parseResult.Success || parseResult.Program is null)
        {
            return new BindResult(null, parseResult.Diagnostics);
        }

        if (parseResult.Program.Statements.Any(item => item is ModuleDeclarationSyntax or ImportStatementSyntax or VisibilityDeclarationSyntax))
            return BindSources([new SmileSource("<source>", source)]);

        BindResult bindResult = new Binder().Bind(parseResult.Program);
        IReadOnlyList<Diagnostic> diagnostics = parseResult.Diagnostics
            .Concat(bindResult.Diagnostics)
            .ToArray();
        return new BindResult(
            bindResult.Program,
            diagnostics);
    }

    public TranspileResult Transpile(string source, TargetLanguage targetLanguage, string programName = "Program") =>
        TranspileMany(source, new[] { targetLanguage }, programName).Single();

    public IReadOnlyList<TranspileResult> TranspileMany(
        string source,
        IEnumerable<TargetLanguage> targetLanguages,
        string programName = "Program")
    {
        ArgumentNullException.ThrowIfNull(targetLanguages);

        TargetLanguage[] languages = targetLanguages.Distinct().ToArray();

        BindResult bindResult = Bind(source);
        if (!bindResult.Success || bindResult.Program is null)
        {
            return languages
                .Select(language => new TranspileResult(language, null, bindResult.Diagnostics))
                .ToArray();
        }

        return languages
            .Select(language =>
            {
                GeneratedProgram generatedProgram = CodeGeneratorRegistry.Get(language).Generate(bindResult.Program with { ProgramName = programName });
                return new TranspileResult(language, generatedProgram, bindResult.Diagnostics);
            })
            .ToArray();
    }
}
