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
            new JavaCodeGenerator(),
            new ObjectiveCCodeGenerator(),
            new SwiftCodeGenerator()
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

        AppendMasmLine(source, "option casemap:none", "Keep symbol names case-sensitive.");
        source.AppendLine();

        if (prints.Length > 0)
        {
            AppendMasmLine(source, "EXTERN GetStdHandle:PROC", "Windows API: get standard console handles.");
            AppendMasmLine(source, "EXTERN WriteFile:PROC", "Windows API: write bytes to the console.");
        }

        AppendMasmLine(source, "EXTERN ExitProcess:PROC", "Windows API: terminate the process.");
        source.AppendLine();

        if (prints.Length > 0)
        {
            AppendMasmLine(source, "STD_OUTPUT_HANDLE EQU -11", "Magic value for the console output handle.");
            source.AppendLine();
            AppendMasmLine(source, ".data", "Static bytes and variables live here.");

            for (int index = 0; index < prints.Length; index++)
            {
                string label = $"message{index}";
                AppendMasmLine(source, $"{label} BYTE {TargetEscapes.MasmByteInitializers(prints[index].Text)}", $"PRINT text #{index + 1}, ending with CR/LF.");
                AppendMasmLine(source, $"{label}Length EQU $ - {label}", "Length equals current address minus the label.");
            }

            AppendMasmLine(source, "bytesWritten DWORD ?", "WriteFile stores how many bytes it wrote.");
            source.AppendLine();
        }

        AppendMasmLine(source, ".code", "CPU instructions live here.");
        AppendMasmLine(source, "main PROC", "Program entry point.");
        AppendMasmLine(source, "    sub rsp, 28h", "Reserve Win64 shadow space and align the stack.");

        for (int index = 0; index < prints.Length; index++)
        {
            string label = $"message{index}";
            // One WriteFile block per PRINT keeps the educational relationship
            // obvious even though a later optimizer could combine writes.
            source.AppendLine();
            AppendMasmLine(source, "    mov ecx, STD_OUTPUT_HANDLE", "First argument: ask for stdout.");
            AppendMasmLine(source, "    call GetStdHandle", "RAX now holds the stdout handle.");
            source.AppendLine();
            AppendMasmLine(source, "    mov rcx, rax", "WriteFile arg 1: stdout handle.");
            AppendMasmLine(source, $"    lea rdx, {label}", "WriteFile arg 2: address of message bytes.");
            AppendMasmLine(source, $"    mov r8d, {label}Length", "WriteFile arg 3: byte count.");
            AppendMasmLine(source, "    lea r9, bytesWritten", "WriteFile arg 4: address for bytes-written result.");
            AppendMasmLine(source, "    mov QWORD PTR [rsp + 20h], 0", "WriteFile arg 5 on stack: no overlapped I/O.");
            AppendMasmLine(source, "    call WriteFile", "Emit the PRINT line.");
        }

        source.AppendLine();
        AppendMasmLine(source, "    xor ecx, ecx", "ExitProcess arg 1: process exit code 0.");
        AppendMasmLine(source, "    call ExitProcess", "End the program.");
        AppendMasmLine(source, "main ENDP", "End of the main procedure.");
        source.AppendLine();
        source.AppendLine("END");

        return new GeneratedProgram(
            Language,
            new[] { new GeneratedFile("Program.asm", TextOutput.EnsureOneTrailingNewLine(source.ToString()), IsPrimary: true) });
    }

    private static void AppendMasmLine(StringBuilder source, string code, string? comment = null)
    {
        if (comment is null)
        {
            source.AppendLine(code);
            return;
        }

        // Assembly programmers often keep instruction comments in a right-side
        // column. Padding keeps the generated tutorial code scannable while
        // preserving the exact instructions MASM assembles.
        const int commentColumn = 48;
        int padding = Math.Max(1, commentColumn - code.Length);
        source.AppendLine(code + new string(' ', padding) + "; " + comment);
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

internal sealed class ObjectiveCCodeGenerator : ICodeGenerator
{
    public TargetLanguage Language => TargetLanguage.ObjectiveC;

    public GeneratedProgram Generate(SmileProgramSyntax program)
    {
        // Objective-C usually wraps even tiny command-line programs in an
        // autorelease pool. That gives learners one real Objective-C runtime
        // idea without adding classes or other ceremony to this PRINT slice.
        var source = new StringBuilder();
        source.AppendLine("#import <Foundation/Foundation.h>");
        source.AppendLine("#include <stdio.h>");
        source.AppendLine();
        source.AppendLine("int main(void)");
        source.AppendLine("{");
        source.AppendLine("    @autoreleasepool");
        source.AppendLine("    {");

        foreach (PrintStatementSyntax print in program.Statements.OfType<PrintStatementSyntax>())
        {
            source.AppendLine($"        puts([{TargetEscapes.ObjectiveCString(print.Text)} UTF8String]);");
        }

        source.AppendLine("    }");
        source.AppendLine();
        source.AppendLine("    return 0;");
        source.AppendLine("}");

        return new GeneratedProgram(
            Language,
            new[] { new GeneratedFile("Program.m", TextOutput.EnsureOneTrailingNewLine(source.ToString()), IsPrimary: true) });
    }
}

internal sealed class SwiftCodeGenerator : ICodeGenerator
{
    public TargetLanguage Language => TargetLanguage.Swift;

    public GeneratedProgram Generate(SmileProgramSyntax program)
    {
        // Swift's top-level statements are perfect for this first SMILE slice:
        // PRINT maps directly to print without needing a class or main method.
        var source = new StringBuilder();

        foreach (PrintStatementSyntax print in program.Statements.OfType<PrintStatementSyntax>())
        {
            source.AppendLine($"print({TargetEscapes.SwiftString(print.Text)})");
        }

        return new GeneratedProgram(
            Language,
            new[] { new GeneratedFile("Program.swift", TextOutput.EnsureOneTrailingNewLine(source.ToString()), IsPrimary: true) });
    }
}

internal static class TargetEscapes
{
    public static string CSharpString(string text) => Quote(EscapeCSharp(text));

    public static string CString(string text) => Quote(EscapeCStyle(text));

    public static string ObjectiveCString(string text) => "@" + Quote(EscapeCStyle(text));

    public static string JavaScriptString(string text) => Quote(EscapeJavaScript(text));

    public static string JavaString(string text) => Quote(EscapeJava(text));

    public static string SwiftString(string text) => Quote(EscapeSwift(text));

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

    private static string EscapeCSharp(string text)
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

    private static string EscapeCStyle(string text)
    {
        var builder = new StringBuilder();

        foreach (char value in text)
        {
            builder.Append(value switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\0' => "\\000",
                '\a' => "\\007",
                '\b' => "\\b",
                '\f' => "\\f",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                '\v' => "\\013",
                _ when char.IsControl(value) => EscapeUtf8BytesAsOctal(value),
                _ => value
            });
        }

        return builder.ToString();
    }

    private static string EscapeUtf8BytesAsOctal(char value)
    {
        // C and Objective-C source files are written as UTF-8. For control
        // characters without a named C escape, fixed three-digit octal byte
        // escapes avoid raw invisible characters and avoid accidental merging
        // with a following digit.
        byte[] bytes = Encoding.UTF8.GetBytes(value.ToString());
        var builder = new StringBuilder();

        foreach (byte utf8Byte in bytes)
        {
            builder.Append('\\');
            builder.Append(ToFixedOctal(utf8Byte));
        }

        return builder.ToString();
    }

    private static string ToFixedOctal(byte value)
    {
        // Fixed-width octal means exactly three base-8 digits. That matters in
        // languages like C and Java where a following digit could otherwise be
        // mistaken for part of the same escape sequence.
        Span<char> digits = stackalloc char[3];
        digits[0] = (char)('0' + ((value >> 6) & 0b111));
        digits[1] = (char)('0' + ((value >> 3) & 0b111));
        digits[2] = (char)('0' + (value & 0b111));
        return new string(digits);
    }

    private static string EscapeJava(string text)
    {
        var builder = new StringBuilder();

        foreach (char value in text)
        {
            builder.Append(value switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\b' => "\\b",
                '\t' => "\\t",
                '\n' => "\\n",
                '\f' => "\\f",
                '\r' => "\\r",
                _ when value < 32 => "\\" + ToFixedOctal((byte)value),
                _ when char.IsControl(value) => $"\\u{(int)value:x4}",
                _ => value
            });
        }

        return builder.ToString();
    }

    private static string EscapeJavaScript(string text)
    {
        var builder = new StringBuilder();

        foreach (char value in text)
        {
            builder.Append(value switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\b' => "\\b",
                '\t' => "\\t",
                '\n' => "\\n",
                '\f' => "\\f",
                '\r' => "\\r",
                _ when char.IsControl(value) => $"\\u{(int)value:x4}",
                _ => value
            });
        }

        return builder.ToString();
    }

    private static string EscapeSwift(string text)
    {
        var builder = new StringBuilder();

        foreach (char value in text)
        {
            builder.Append(value switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\0' => "\\0",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ when char.IsControl(value) => $"\\u{{{(int)value:x}}}",
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
