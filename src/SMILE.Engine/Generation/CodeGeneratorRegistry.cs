namespace SMILE.Engine;

public static class CodeGeneratorRegistry
{
    private static readonly IReadOnlyDictionary<TargetLanguage, ICodeGenerator> Generators =
        TargetLanguageInfo.All
            .Select(language => (ICodeGenerator)new CoreBasicTargetCodeGenerator(language))
            .ToDictionary(generator => generator.Language);

    public static ICodeGenerator Get(TargetLanguage language) => Generators[language];

    private sealed class CoreBasicTargetCodeGenerator(TargetLanguage language) : ICodeGenerator
    {
        public TargetLanguage Language { get; } = language;

        public GeneratedProgram Generate(BoundProgram program) =>
            CoreBasicCodeGenerator.Generate(program, Language);
    }
}
