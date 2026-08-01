using System.Text;

namespace SMILE.Engine;

public sealed record GeneratedFile(
    string RelativePath,
    string Content,
    bool IsPrimary);

public sealed record GeneratedProgram(
    TargetLanguage Language,
    IReadOnlyList<GeneratedFile> Files)
{
    public GeneratedFile PrimaryFile => Files.Single(file => file.IsPrimary);
}

public interface ICodeGenerator
{
    TargetLanguage Language { get; }

    // Each generator receives the same SMILE syntax tree. This is the key
    // transpiler boundary: SMILE does not go through C# on the way to C,
    // assembly, JavaScript, or Java.
    GeneratedProgram Generate(SmileProgramSyntax program);
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

    public TranspileResult Transpile(string source, TargetLanguage targetLanguage) =>
        TranspileMany(source, new[] { targetLanguage }).Single();

    public IReadOnlyList<TranspileResult> TranspileMany(
        string source,
        IEnumerable<TargetLanguage> targetLanguages)
    {
        ArgumentNullException.ThrowIfNull(targetLanguages);

        TargetLanguage[] languages = targetLanguages.Distinct().ToArray();

        // Parse once, then hand the same syntax tree to every requested
        // generator. This keeps the UI fast and makes cross-target output
        // easier to reason about.
        ParseResult parseResult = Parse(source);

        if (!parseResult.Success || parseResult.Program is null)
        {
            return languages
                .Select(language => new TranspileResult(language, null, parseResult.Diagnostics))
                .ToArray();
        }

        return languages
            .Select(language =>
            {
                ICodeGenerator generator = CodeGeneratorRegistry.Get(language);
                return new TranspileResult(language, generator.Generate(parseResult.Program), parseResult.Diagnostics);
            })
            .ToArray();
    }
}

public static class CodeGeneratorRegistry
{
    private static readonly IReadOnlyDictionary<TargetLanguage, ICodeGenerator> Generators =
        new ICodeGenerator[]
        {
            new CSharpCodeGenerator(),
            new CCodeGenerator(),
            new MasmX64CodeGenerator(),
            new JavaScriptCodeGenerator(),
            new JavaCodeGenerator()
        }.ToDictionary(generator => generator.Language);

    public static ICodeGenerator Get(TargetLanguage language) => Generators[language];
}

internal sealed class CSharpCodeGenerator : ICodeGenerator
{
    public TargetLanguage Language => TargetLanguage.CSharp;

    public GeneratedProgram Generate(SmileProgramSyntax program)
    {
        // The generated C# is a complete console program, but it avoids
        // namespaces and other ceremony so PRINT maps clearly to WriteLine.
        var source = new StringBuilder();
        source.AppendLine("using System;");
        source.AppendLine();
        source.AppendLine("internal static class Program");
        source.AppendLine("{");
        source.AppendLine("    private static void Main()");
        source.AppendLine("    {");

        foreach (PrintStatementSyntax print in program.Statements.OfType<PrintStatementSyntax>())
        {
            source.AppendLine($"        Console.WriteLine({TargetEscapes.CSharpString(print.Text)});");
        }

        source.AppendLine("    }");
        source.AppendLine("}");

        const string project = """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
""";

        return new GeneratedProgram(
            Language,
            new[]
            {
                new GeneratedFile("Program.cs", TextOutput.EnsureOneTrailingNewLine(source.ToString()), IsPrimary: true),
                new GeneratedFile("GeneratedProgram.csproj", TextOutput.EnsureOneTrailingNewLine(project), IsPrimary: false)
            });
    }
}

internal sealed class CCodeGenerator : ICodeGenerator
{
    public TargetLanguage Language => TargetLanguage.C;

    public GeneratedProgram Generate(SmileProgramSyntax program)
    {
        // puts is the C standard-library call that naturally matches SMILE
        // PRINT because both append a newline.
        var source = new StringBuilder();
        source.AppendLine("#include <stdio.h>");
        source.AppendLine();
        source.AppendLine("int main(void)");
        source.AppendLine("{");

        foreach (PrintStatementSyntax print in program.Statements.OfType<PrintStatementSyntax>())
        {
            source.AppendLine($"    puts({TargetEscapes.CString(print.Text)});");
        }

        source.AppendLine("    return 0;");
        source.AppendLine("}");

        return SingleFile(TextOutput.EnsureOneTrailingNewLine(source.ToString()));
    }

    private GeneratedProgram SingleFile(string content) =>
        new(Language, new[] { new GeneratedFile("Program.c", content, IsPrimary: true) });
}

internal sealed class MasmX64CodeGenerator : ICodeGenerator
{
    public TargetLanguage Language => TargetLanguage.MasmX64;

    public GeneratedProgram Generate(SmileProgramSyntax program)
    {
        PrintStatementSyntax[] prints = program.Statements.OfType<PrintStatementSyntax>().ToArray();
        var source = new StringBuilder();

        source.AppendLine("option casemap:none");
        source.AppendLine();

        if (prints.Length > 0)
        {
            source.AppendLine("EXTERN GetStdHandle:PROC");
            source.AppendLine("EXTERN WriteFile:PROC");
        }

        source.AppendLine("EXTERN ExitProcess:PROC");
        source.AppendLine();

        if (prints.Length > 0)
        {
            source.AppendLine("STD_OUTPUT_HANDLE EQU -11");
            source.AppendLine();
            source.AppendLine(".data");

            for (int index = 0; index < prints.Length; index++)
            {
                string label = $"message{index}";
                source.AppendLine($"{label} BYTE {TargetEscapes.MasmByteInitializers(prints[index].Text)}");
                source.AppendLine($"{label}Length EQU $ - {label}");
            }

            source.AppendLine("bytesWritten DWORD ?");
            source.AppendLine();
        }

        source.AppendLine(".code");
        source.AppendLine("main PROC");
        source.AppendLine("    sub rsp, 28h");

        for (int index = 0; index < prints.Length; index++)
        {
            string label = $"message{index}";
            // One WriteFile block per PRINT keeps the educational relationship
            // obvious even though a later optimizer could combine writes.
            source.AppendLine();
            source.AppendLine("    mov ecx, STD_OUTPUT_HANDLE");
            source.AppendLine("    call GetStdHandle");
            source.AppendLine();
            source.AppendLine("    mov rcx, rax");
            source.AppendLine($"    lea rdx, {label}");
            source.AppendLine($"    mov r8d, {label}Length");
            source.AppendLine("    lea r9, bytesWritten");
            source.AppendLine("    mov QWORD PTR [rsp + 20h], 0");
            source.AppendLine("    call WriteFile");
        }

        source.AppendLine();
        source.AppendLine("    xor ecx, ecx");
        source.AppendLine("    call ExitProcess");
        source.AppendLine("main ENDP");
        source.AppendLine();
        source.AppendLine("END");

        return new GeneratedProgram(
            Language,
            new[] { new GeneratedFile("Program.asm", TextOutput.EnsureOneTrailingNewLine(source.ToString()), IsPrimary: true) });
    }
}

internal sealed class JavaScriptCodeGenerator : ICodeGenerator
{
    public TargetLanguage Language => TargetLanguage.JavaScript;

    public GeneratedProgram Generate(SmileProgramSyntax program)
    {
        // JavaScript needs no wrapper for this MVP; the file can be run
        // directly with Node.js when it is installed.
        var source = new StringBuilder();

        foreach (PrintStatementSyntax print in program.Statements.OfType<PrintStatementSyntax>())
        {
            source.AppendLine($"console.log({TargetEscapes.JavaScriptString(print.Text)});");
        }

        return new GeneratedProgram(
            Language,
            new[] { new GeneratedFile("Program.js", TextOutput.EnsureOneTrailingNewLine(source.ToString()), IsPrimary: true) });
    }
}

internal sealed class JavaCodeGenerator : ICodeGenerator
{
    public TargetLanguage Language => TargetLanguage.Java;

    public GeneratedProgram Generate(SmileProgramSyntax program)
    {
        // Java requires a class and main method, so this is the minimum normal
        // Java shape around the same PRINT statements.
        var source = new StringBuilder();
        source.AppendLine("public final class Program");
        source.AppendLine("{");
        source.AppendLine("    public static void main(String[] args)");
        source.AppendLine("    {");

        foreach (PrintStatementSyntax print in program.Statements.OfType<PrintStatementSyntax>())
        {
            source.AppendLine($"        System.out.println({TargetEscapes.JavaString(print.Text)});");
        }

        source.AppendLine("    }");
        source.AppendLine("}");

        return new GeneratedProgram(
            Language,
            new[] { new GeneratedFile("Program.java", TextOutput.EnsureOneTrailingNewLine(source.ToString()), IsPrimary: true) });
    }
}

internal static class TargetEscapes
{
    public static string CSharpString(string text) => Quote(Escape(text));

    public static string CString(string text) => Quote(Escape(text));

    public static string JavaScriptString(string text) => Quote(Escape(text));

    public static string JavaString(string text) => Quote(Escape(text));

    public static string MasmByteInitializers(string text)
    {
        // MASM BYTE initializers are safest when we emit ordinary printable
        // ASCII as quoted runs and everything else as numeric byte values.
        // That prevents a SMILE string containing quotes or non-ASCII bytes
        // from accidentally becoming invalid assembly syntax.
        byte[] bytes = Encoding.UTF8.GetBytes(text + "\r\n");
        var parts = new List<string>();
        var currentText = new StringBuilder();

        foreach (byte value in bytes)
        {
            if (value is >= 32 and <= 126 and not (byte)'"')
            {
                currentText.Append((char)value);
                continue;
            }

            FlushText();
            parts.Add(value.ToString());
        }

        FlushText();
        return string.Join(", ", parts);

        void FlushText()
        {
            if (currentText.Length == 0)
            {
                return;
            }

            parts.Add(Quote(currentText.ToString()));
            currentText.Clear();
        }
    }

    private static string Quote(string text) => $"\"{text}\"";

    private static string Escape(string text)
    {
        var builder = new StringBuilder();

        foreach (char value in text)
        {
            builder.Append(value switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\0' => "\\0",
                '\a' => "\\a",
                '\b' => "\\b",
                '\f' => "\\f",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                '\v' => "\\v",
                _ when char.IsControl(value) => $"\\u{(int)value:x4}",
                _ => value
            });
        }

        return builder.ToString();
    }
}

internal static class TextOutput
{
    public static string EnsureOneTrailingNewLine(string text)
    {
        string normalized = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Replace("\n", Environment.NewLine, StringComparison.Ordinal);

        return normalized.TrimEnd('\r', '\n') + Environment.NewLine;
    }
}
