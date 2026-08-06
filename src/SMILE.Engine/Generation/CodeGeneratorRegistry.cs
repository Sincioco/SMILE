namespace SMILE.Engine;

public static class CodeGeneratorRegistry
{
    private static readonly IReadOnlyDictionary<TargetLanguage, ICodeGenerator> Generators =
        new ICodeGenerator[]
        {
            new CSharpCodeGenerator(),
            new CCodeGenerator(),
            new MasmX64CodeGenerator(),
            new JavaScriptCodeGenerator(),
            new JavaCodeGenerator(),
            new CobolCodeGenerator(),
            new ObjectiveCCodeGenerator(),
            new SwiftCodeGenerator(),
            new PythonCodeGenerator(),
            new CppCodeGenerator()
        }.ToDictionary(generator => generator.Language);

    public static ICodeGenerator Get(TargetLanguage language) => Generators[language];
}
