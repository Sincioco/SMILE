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
        return new BindResult(
            bindResult.Program,
            parseResult.Diagnostics.Concat(bindResult.Diagnostics).ToArray());
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

        return languages
            .Select(language =>
            {
                ICodeGenerator generator = CodeGeneratorRegistry.Get(language);
                return new TranspileResult(language, generator.Generate(bindResult.Program), bindResult.Diagnostics);
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

    public GeneratedProgram Generate(BoundProgram program)
    {
        var source = new StringBuilder();
        source.AppendLine("using System;");
        source.AppendLine();
        source.AppendLine("internal static class Program");
        source.AppendLine("{");
        source.AppendLine("    private static void Main()");
        source.AppendLine("    {");

        foreach (BoundStatement statement in program.Statements)
        {
            switch (statement)
            {
                case BoundLetStatement let:
                    source.AppendLine($"        string {let.Variable.Name} = {TargetExpression.CSharp(let.Initializer)};");
                    break;

                case BoundPrintStatement print:
                    source.AppendLine($"        Console.WriteLine({TargetExpression.CSharp(print.Value)});");
                    break;
            }
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

    public GeneratedProgram Generate(BoundProgram program)
    {
        var source = new StringBuilder();
        source.AppendLine("#include <stdio.h>");
        source.AppendLine();
        source.AppendLine("int main(void)");
        source.AppendLine("{");

        foreach (BoundStatement statement in program.Statements)
        {
            switch (statement)
            {
                case BoundLetStatement let:
                    source.AppendLine($"    const char *{let.Variable.Name} = {TargetEscapes.CString(GetLiteralInitializer(let))};");
                    break;

                case BoundPrintStatement print:
                    AppendCPrint(source, print.Value);
                    break;
            }
        }

        source.AppendLine("    return 0;");
        source.AppendLine("}");

        return new GeneratedProgram(
            Language,
            new[] { new GeneratedFile("Program.c", TextOutput.EnsureOneTrailingNewLine(source.ToString()), IsPrimary: true) });
    }

    private static void AppendCPrint(StringBuilder source, BoundExpression expression)
    {
        IReadOnlyList<PrintSegment> segments = BoundStringExpression.Flatten(expression);
        if (segments.Count == 0)
        {
            source.AppendLine("    putchar('\\n');");
            return;
        }

        foreach (PrintSegment segment in segments)
        {
            switch (segment)
            {
                case LiteralPrintSegment literal:
                    source.AppendLine($"    fputs({TargetEscapes.CString(literal.Text)}, stdout);");
                    break;

                case VariablePrintSegment variable:
                    source.AppendLine($"    fputs({variable.Variable.Name}, stdout);");
                    break;
            }
        }

        source.AppendLine("    putchar('\\n');");
    }

    private static string GetLiteralInitializer(BoundLetStatement let) =>
        let.Initializer is BoundStringLiteralExpression literal ? literal.Value : string.Empty;
}

internal sealed class MasmX64CodeGenerator : ICodeGenerator
{
    public TargetLanguage Language => TargetLanguage.MasmX64;

    public GeneratedProgram Generate(BoundProgram program)
    {
        BoundLetStatement[] lets = program.Statements.OfType<BoundLetStatement>().ToArray();
        BoundPrintStatement[] prints = program.Statements.OfType<BoundPrintStatement>().ToArray();
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

        AppendMasmData(source, lets, prints);
        AppendMasmCode(source, lets, prints);

        return new GeneratedProgram(
            Language,
            new[] { new GeneratedFile("Program.asm", TextOutput.EnsureOneTrailingNewLine(source.ToString()), IsPrimary: true) });
    }

    private static void AppendMasmData(
        StringBuilder source,
        IReadOnlyList<BoundLetStatement> lets,
        IReadOnlyList<BoundPrintStatement> prints)
    {
        if (lets.Count == 0 && prints.Count == 0)
        {
            return;
        }

        AppendMasmLine(source, ".data", "Static bytes and variables live here.");

        if (prints.Count > 0)
        {
            AppendMasmLine(source, "STD_OUTPUT_HANDLE EQU -11", "Magic value for the console output handle.");
        }

        for (int index = 0; index < lets.Count; index++)
        {
            BoundLetStatement let = lets[index];
            string valueLabel = VariableValueLabel(index);
            AppendMasmLine(source, $"{valueLabel} BYTE {TargetEscapes.MasmByteInitializers(GetLiteralInitializer(let))}", $"LET {let.Variable.Name} initial text.");
            AppendMasmLine(source, $"{valueLabel}Length EQU $ - {valueLabel}", "Length of the variable's current text.");
            AppendMasmLine(source, $"{VariablePointerLabel(index)} QWORD ?", $"Runtime pointer for {let.Variable.Name}.");
            AppendMasmLine(source, $"{VariableLengthLabel(index)} DWORD ?", $"Runtime length for {let.Variable.Name}.");
        }

        for (int printIndex = 0; printIndex < prints.Count; printIndex++)
        {
            IReadOnlyList<PrintSegment> segments = BoundStringExpression.Flatten(prints[printIndex].Value);
            for (int segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
            {
                if (segments[segmentIndex] is not LiteralPrintSegment literal)
                {
                    continue;
                }

                string label = PrintLiteralLabel(printIndex, segmentIndex);
                AppendMasmLine(source, $"{label} BYTE {TargetEscapes.MasmByteInitializers(literal.Text)}", $"PRINT #{printIndex + 1} literal segment.");
                AppendMasmLine(source, $"{label}Length EQU $ - {label}", "Length of this literal segment.");
            }
        }

        if (prints.Count > 0)
        {
            AppendMasmLine(source, "newline BYTE 13, 10", "SMILE PRINT appends CR/LF on Windows.");
            AppendMasmLine(source, "newlineLength EQU $ - newline", "Length of the newline bytes.");
            AppendMasmLine(source, "stdoutHandle QWORD ?", "Cached standard output handle.");
            AppendMasmLine(source, "bytesWritten DWORD ?", "WriteFile stores how many bytes it wrote.");
        }

        source.AppendLine();
    }

    private static void AppendMasmCode(
        StringBuilder source,
        IReadOnlyList<BoundLetStatement> lets,
        IReadOnlyList<BoundPrintStatement> prints)
    {
        AppendMasmLine(source, ".code", "CPU instructions live here.");
        AppendMasmLine(source, "main PROC", "Program entry point.");
        AppendMasmLine(source, "    sub rsp, 28h", "Reserve Win64 shadow space and align the stack.");

        if (prints.Count > 0)
        {
            source.AppendLine();
            AppendMasmLine(source, "    mov ecx, STD_OUTPUT_HANDLE", "Ask Windows for stdout.");
            AppendMasmLine(source, "    call GetStdHandle", "RAX receives the stdout handle.");
            AppendMasmLine(source, "    mov QWORD PTR [stdoutHandle], rax", "Cache stdout for every PRINT segment.");
        }

        for (int index = 0; index < lets.Count; index++)
        {
            string valueLabel = VariableValueLabel(index);
            source.AppendLine();
            AppendMasmLine(source, $"    lea rax, {valueLabel}", $"Address of LET {lets[index].Variable.Name} text.");
            AppendMasmLine(source, $"    mov QWORD PTR [{VariablePointerLabel(index)}], rax", "Store the runtime string pointer.");
            AppendMasmLine(source, $"    mov DWORD PTR [{VariableLengthLabel(index)}], {valueLabel}Length", "Store the runtime string length.");
        }

        for (int printIndex = 0; printIndex < prints.Count; printIndex++)
        {
            source.AppendLine();
            AppendMasmLine(source, $"; PRINT #{printIndex + 1}", "Write each expression segment, then newline.");
            IReadOnlyList<PrintSegment> segments = BoundStringExpression.Flatten(prints[printIndex].Value);
            for (int segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
            {
                switch (segments[segmentIndex])
                {
                    case LiteralPrintSegment:
                        AppendMasmWriteLiteral(source, PrintLiteralLabel(printIndex, segmentIndex));
                        break;

                    case VariablePrintSegment variable:
                        int variableIndex = lets.IndexOf(let => ReferenceEquals(let.Variable, variable.Variable));
                        AppendMasmWriteVariable(source, variable.Variable.Name, variableIndex);
                        break;
                }
            }

            AppendMasmWriteLiteral(source, "newline");
        }

        source.AppendLine();
        AppendMasmLine(source, "    xor ecx, ecx", "ExitProcess arg 1: process exit code 0.");
        AppendMasmLine(source, "    call ExitProcess", "End the program.");
        AppendMasmLine(source, "main ENDP", "End of the main procedure.");
        source.AppendLine();
        source.AppendLine("END");
    }

    private static void AppendMasmWriteLiteral(StringBuilder source, string label)
    {
        AppendMasmLine(source, "    mov rcx, QWORD PTR [stdoutHandle]", "WriteFile arg 1: stdout handle.");
        AppendMasmLine(source, $"    lea rdx, {label}", "WriteFile arg 2: address of literal bytes.");
        AppendMasmLine(source, $"    mov r8d, {label}Length", "WriteFile arg 3: byte count.");
        AppendMasmLine(source, "    lea r9, bytesWritten", "WriteFile arg 4: address for bytes-written result.");
        AppendMasmLine(source, "    mov QWORD PTR [rsp + 20h], 0", "WriteFile arg 5 on stack: no overlapped I/O.");
        AppendMasmLine(source, "    call WriteFile", "Emit this literal segment.");
    }

    private static void AppendMasmWriteVariable(StringBuilder source, string name, int variableIndex)
    {
        AppendMasmLine(source, "    mov rcx, QWORD PTR [stdoutHandle]", "WriteFile arg 1: stdout handle.");
        AppendMasmLine(source, $"    mov rdx, QWORD PTR [{VariablePointerLabel(variableIndex)}]", $"WriteFile arg 2: {name} pointer.");
        AppendMasmLine(source, $"    mov r8d, DWORD PTR [{VariableLengthLabel(variableIndex)}]", $"WriteFile arg 3: {name} length.");
        AppendMasmLine(source, "    lea r9, bytesWritten", "WriteFile arg 4: address for bytes-written result.");
        AppendMasmLine(source, "    mov QWORD PTR [rsp + 20h], 0", "WriteFile arg 5 on stack: no overlapped I/O.");
        AppendMasmLine(source, "    call WriteFile", "Emit this variable segment.");
    }

    private static void AppendMasmLine(StringBuilder source, string code, string? comment = null)
    {
        if (comment is null)
        {
            source.AppendLine(code);
            return;
        }

        const int commentColumn = 48;
        int padding = Math.Max(1, commentColumn - code.Length);
        source.AppendLine(code + new string(' ', padding) + "; " + comment);
    }

    private static string VariableValueLabel(int index) => $"variable{index}Value";

    private static string VariablePointerLabel(int index) => $"variable{index}Ptr";

    private static string VariableLengthLabel(int index) => $"variable{index}Length";

    private static string PrintLiteralLabel(int printIndex, int segmentIndex) =>
        $"print{printIndex}Segment{segmentIndex}";

    private static string GetLiteralInitializer(BoundLetStatement let) =>
        let.Initializer is BoundStringLiteralExpression literal ? literal.Value : string.Empty;
}

internal sealed class JavaScriptCodeGenerator : ICodeGenerator
{
    public TargetLanguage Language => TargetLanguage.JavaScript;

    public GeneratedProgram Generate(BoundProgram program)
    {
        var source = new StringBuilder();

        foreach (BoundStatement statement in program.Statements)
        {
            switch (statement)
            {
                case BoundLetStatement let:
                    source.AppendLine($"let {let.Variable.Name} = {TargetExpression.JavaScript(let.Initializer)};");
                    break;

                case BoundPrintStatement print:
                    source.AppendLine($"console.log({TargetExpression.JavaScript(print.Value)});");
                    break;
            }
        }

        return new GeneratedProgram(
            Language,
            new[] { new GeneratedFile("Program.js", TextOutput.EnsureOneTrailingNewLine(source.ToString()), IsPrimary: true) });
    }
}

internal sealed class JavaCodeGenerator : ICodeGenerator
{
    public TargetLanguage Language => TargetLanguage.Java;

    public GeneratedProgram Generate(BoundProgram program)
    {
        var source = new StringBuilder();
        source.AppendLine("public final class Program");
        source.AppendLine("{");
        source.AppendLine("    public static void main(String[] args)");
        source.AppendLine("    {");

        foreach (BoundStatement statement in program.Statements)
        {
            switch (statement)
            {
                case BoundLetStatement let:
                    source.AppendLine($"        String {let.Variable.Name} = {TargetExpression.Java(let.Initializer)};");
                    break;

                case BoundPrintStatement print:
                    source.AppendLine($"        System.out.println({TargetExpression.Java(print.Value)});");
                    break;
            }
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

    public GeneratedProgram Generate(BoundProgram program)
    {
        var source = new StringBuilder();
        source.AppendLine("#import <Foundation/Foundation.h>");
        source.AppendLine("#include <stdio.h>");
        source.AppendLine();
        source.AppendLine("int main(void)");
        source.AppendLine("{");
        source.AppendLine("    @autoreleasepool");
        source.AppendLine("    {");

        foreach (BoundStatement statement in program.Statements)
        {
            switch (statement)
            {
                case BoundLetStatement let:
                    source.AppendLine($"        NSString *{let.Variable.Name} = {TargetEscapes.ObjectiveCString(GetLiteralInitializer(let))};");
                    break;

                case BoundPrintStatement print:
                    AppendObjectiveCPrint(source, print.Value);
                    break;
            }
        }

        source.AppendLine("    }");
        source.AppendLine();
        source.AppendLine("    return 0;");
        source.AppendLine("}");

        return new GeneratedProgram(
            Language,
            new[] { new GeneratedFile("Program.m", TextOutput.EnsureOneTrailingNewLine(source.ToString()), IsPrimary: true) });
    }

    private static void AppendObjectiveCPrint(StringBuilder source, BoundExpression expression)
    {
        IReadOnlyList<PrintSegment> segments = BoundStringExpression.Flatten(expression);
        if (segments.Count == 0)
        {
            source.AppendLine("        putchar('\\n');");
            return;
        }

        foreach (PrintSegment segment in segments)
        {
            switch (segment)
            {
                case LiteralPrintSegment literal:
                    source.AppendLine($"        fputs({TargetEscapes.CString(literal.Text)}, stdout);");
                    break;

                case VariablePrintSegment variable:
                    source.AppendLine($"        fputs([{variable.Variable.Name} UTF8String], stdout);");
                    break;
            }
        }

        source.AppendLine("        putchar('\\n');");
    }

    private static string GetLiteralInitializer(BoundLetStatement let) =>
        let.Initializer is BoundStringLiteralExpression literal ? literal.Value : string.Empty;
}

internal sealed class SwiftCodeGenerator : ICodeGenerator
{
    public TargetLanguage Language => TargetLanguage.Swift;

    public GeneratedProgram Generate(BoundProgram program)
    {
        var source = new StringBuilder();

        foreach (BoundStatement statement in program.Statements)
        {
            switch (statement)
            {
                case BoundLetStatement let:
                    source.AppendLine($"let {let.Variable.Name} = {TargetExpression.Swift(let.Initializer)}");
                    break;

                case BoundPrintStatement print:
                    source.AppendLine($"print({TargetExpression.Swift(print.Value)})");
                    break;
            }
        }

        return new GeneratedProgram(
            Language,
            new[] { new GeneratedFile("Program.swift", TextOutput.EnsureOneTrailingNewLine(source.ToString()), IsPrimary: true) });
    }
}

internal static class TargetExpression
{
    public static string CSharp(BoundExpression expression) =>
        Join(BoundStringExpression.Flatten(expression), TargetEscapes.CSharpString);

    public static string JavaScript(BoundExpression expression) =>
        Join(BoundStringExpression.Flatten(expression), TargetEscapes.JavaScriptString);

    public static string Java(BoundExpression expression) =>
        Join(BoundStringExpression.Flatten(expression), TargetEscapes.JavaString);

    public static string Swift(BoundExpression expression) =>
        Join(BoundStringExpression.Flatten(expression), TargetEscapes.SwiftString);

    private static string Join(
        IReadOnlyList<PrintSegment> segments,
        Func<string, string> quoteLiteral)
    {
        if (segments.Count == 0)
        {
            return quoteLiteral(string.Empty);
        }

        return string.Join(
            " + ",
            segments.Select(segment => segment switch
            {
                LiteralPrintSegment literal => quoteLiteral(literal.Text),
                VariablePrintSegment variable => variable.Variable.Name,
                _ => quoteLiteral(string.Empty)
            }));
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
        byte[] bytes = Encoding.UTF8.GetBytes(text);
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
        return parts.Count == 0 ? "0" : string.Join(", ", parts);

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

internal static class ReadOnlyListExtensions
{
    public static int IndexOf<T>(this IReadOnlyList<T> values, Func<T, bool> predicate)
    {
        for (int index = 0; index < values.Count; index++)
        {
            if (predicate(values[index]))
            {
                return index;
            }
        }

        throw new InvalidOperationException("Expected value was not found.");
    }
}
