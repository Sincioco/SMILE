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
}

public interface ICodeGenerator
{
    TargetLanguage Language { get; }

    // Generators consume the bound program, not source text. That keeps target
    // backends honest: they all see the same variables, literals, and
    // interpolation parts resolved by the binder.
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

public sealed class SmileTranspiler
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

        BindResult bindResult = new Binder().Bind(parseResult.Program);
        IReadOnlyList<Diagnostic> diagnostics = parseResult.Diagnostics
            .Concat(bindResult.Diagnostics)
            .ToArray();
        if (bindResult.Success && bindResult.Program is not null)
        {
            // Some whole-program rules, such as WHILE's portable bounded-String
            // requirement, can only be decided after the binder has built the
            // complete control-flow tree. Keep that validation at the shared
            // analysis boundary so every evaluator and target sees one answer.
            diagnostics = diagnostics
                .Concat(BoundProgramAnalysis.Create(bindResult.Program).Diagnostics)
                .ToArray();
        }

        return new BindResult(
            bindResult.Program,
            diagnostics);
    }

    public TranspileResult Transpile(string source, TargetLanguage targetLanguage) =>
        TranspileMany(source, new[] { targetLanguage }).Single();

    public IReadOnlyList<TranspileResult> TranspileMany(
        string source,
        IEnumerable<TargetLanguage> targetLanguages)
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

        // Simplification belongs between binding and target generation. The
        // binder remains the source of truth for SMILE's signed 64-bit
        // semantics, while every backend receives the same smaller, pure
        // bound tree and therefore cannot invent target-specific identities.
        BoundProgram simplifiedProgram = BoundProgramSimplifier.Simplify(bindResult.Program);

        bool requiresStandardInput = BoundStatementTree.Enumerate(simplifiedProgram)
            .Any(statement => statement is BoundInputStatement);

        return languages
            .Select(language =>
            {
                ICodeGenerator generator = CodeGeneratorRegistry.Get(language);
                GeneratedProgram generatedProgram = generator.Generate(simplifiedProgram) with
                {
                    RequiresStandardInput = requiresStandardInput
                };
                return new TranspileResult(language, generatedProgram, bindResult.Diagnostics);
            })
            .ToArray();
    }
}
