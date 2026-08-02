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
            new CobolCodeGenerator(),
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
        TargetIdentifierMap identifiers = TargetIdentifierMap.Create(program, Language);
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
                    source.AppendLine($"        string {identifiers.Get(let.Variable)} = {TargetExpression.CSharp(let.Initializer, identifiers)};");
                    break;

                case BoundPrintStatement print:
                    if (print.IsBlankLine)
                    {
                        source.AppendLine("        Console.WriteLine();");
                    }
                    else
                    {
                        source.AppendLine($"        Console.WriteLine({TargetExpression.CSharp(print.Value, identifiers)});");
                    }

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
        TargetIdentifierMap identifiers = TargetIdentifierMap.Create(program, Language);
        var source = new StringBuilder();
        source.AppendLine("#include <stdio.h>");
        source.AppendLine();
        source.AppendLine("int main(void)");
        source.AppendLine("{");

        bool emittedDeclaration = false;
        bool emittedExecutable = false;
        bool emittedBodyStatement = false;

        foreach (BoundStatement statement in program.Statements)
        {
            switch (statement)
            {
                case BoundLetStatement let:
                    source.AppendLine($"    const char *{identifiers.Get(let.Variable)} = {TargetEscapes.CString(let.ConstantValue)};");
                    emittedDeclaration = true;
                    emittedBodyStatement = true;
                    break;

                case BoundPrintStatement print:
                    if (!emittedExecutable && emittedDeclaration)
                    {
                        source.AppendLine();
                    }

                    AppendCPrint(source, print, identifiers);
                    emittedExecutable = true;
                    emittedBodyStatement = true;
                    break;
            }
        }

        if (emittedBodyStatement)
        {
            source.AppendLine();
        }

        source.AppendLine("    return 0;");
        source.AppendLine("}");

        return new GeneratedProgram(
            Language,
            new[] { new GeneratedFile("Program.c", TextOutput.EnsureOneTrailingNewLine(source.ToString()), IsPrimary: true) });
    }

    private static void AppendCPrint(
        StringBuilder source,
        BoundPrintStatement print,
        TargetIdentifierMap identifiers)
    {
        CPrintfPlan plan = CPrintfPlan.FromPrint(print, variable => identifiers.Get(variable));
        AppendPrintfCall(source, "    ", plan);
    }

    internal static void AppendPrintfCall(StringBuilder source, string indent, CPrintfPlan plan)
    {
        source.Append(indent);
        source.Append("printf(");
        source.Append(TargetEscapes.CPrintfFormatString(plan.FormatText));

        foreach (string argument in plan.Arguments)
        {
            source.Append(", ");
            source.Append(argument);
        }

        source.AppendLine(");");
    }

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
            AppendMasmStringData(source, valueLabel, let.ConstantValue, $"LET {let.Variable.Name} initial text.", "Length of the variable's current text.");
            AppendMasmLine(source, $"{VariablePointerLabel(index)} QWORD ?", $"Runtime pointer for {let.Variable.Name}.");
            AppendMasmLine(source, $"{VariableLengthLabel(index)} DWORD ?", $"Runtime length for {let.Variable.Name}.");
        }

        for (int printIndex = 0; printIndex < prints.Count; printIndex++)
        {
            IReadOnlyList<PrintSegment> segments = BoundStringExpression.FlattenForOutput(prints[printIndex].Value);
            for (int segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
            {
                if (segments[segmentIndex] is not LiteralPrintSegment literal)
                {
                    continue;
                }

                string label = PrintLiteralLabel(printIndex, segmentIndex);
                AppendMasmStringData(source, label, literal.Text, $"PRINT #{printIndex + 1} literal segment.", "Length of this literal segment.");
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

    private static void AppendMasmStringData(
        StringBuilder source,
        string label,
        string value,
        string valueComment,
        string lengthComment)
    {
        AppendMasmLine(source, $"{label} BYTE {TargetEscapes.MasmByteInitializers(value)}", valueComment);

        // MASM needs at least one byte after a BYTE label, so the empty string
        // uses a 0 placeholder for storage. The logical SMILE string length is
        // still zero; otherwise WriteFile would emit an invisible NUL byte.
        string lengthExpression = Encoding.UTF8.GetByteCount(value) == 0
            ? "0"
            : $"$ - {label}";
        AppendMasmLine(source, $"{label}Length EQU {lengthExpression}", lengthComment);
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
            IReadOnlyList<PrintSegment> segments = BoundStringExpression.FlattenForOutput(prints[printIndex].Value);
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

}

internal sealed class JavaScriptCodeGenerator : ICodeGenerator
{
    public TargetLanguage Language => TargetLanguage.JavaScript;

    public GeneratedProgram Generate(BoundProgram program)
    {
        TargetIdentifierMap identifiers = TargetIdentifierMap.Create(program, Language);
        var source = new StringBuilder();

        foreach (BoundStatement statement in program.Statements)
        {
            switch (statement)
            {
                case BoundLetStatement let:
                    source.AppendLine($"let {identifiers.Get(let.Variable)} = {TargetExpression.JavaScript(let.Initializer, identifiers)};");
                    break;

                case BoundPrintStatement print:
                    source.AppendLine(print.IsBlankLine
                        ? "console.log();"
                        : $"console.log({TargetExpression.JavaScript(print.Value, identifiers)});");
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
        TargetIdentifierMap identifiers = TargetIdentifierMap.Create(program, Language);
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
                    source.AppendLine($"        String {identifiers.Get(let.Variable)} = {TargetExpression.Java(let.Initializer, identifiers)};");
                    break;

                case BoundPrintStatement print:
                    source.AppendLine(print.IsBlankLine
                        ? "        System.out.println();"
                        : $"        System.out.println({TargetExpression.Java(print.Value, identifiers)});");
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

internal sealed class CobolCodeGenerator : ICodeGenerator
{
    public TargetLanguage Language => TargetLanguage.Cobol;

    public GeneratedProgram Generate(BoundProgram program)
    {
        TargetIdentifierMap identifiers = TargetIdentifierMap.Create(program, Language);
        var source = new StringBuilder();
        BoundLetStatement[] lets = program.Statements.OfType<BoundLetStatement>().ToArray();
        var values = lets.ToDictionary(let => let.Variable, let => let.ConstantValue);

        source.AppendLine(">>SOURCE FORMAT IS FREE");
        source.AppendLine("IDENTIFICATION DIVISION.");
        source.AppendLine("PROGRAM-ID. Program.");

        if (lets.Length > 0)
        {
            source.AppendLine();
            source.AppendLine("DATA DIVISION.");
            source.AppendLine("WORKING-STORAGE SECTION.");
            source.AppendLine("*> SMILE LET values are stored before PROCEDURE DIVISION.");

            foreach (BoundLetStatement let in lets)
            {
                AppendCobolLet(source, let, identifiers);
            }
        }

        source.AppendLine();
        source.AppendLine("PROCEDURE DIVISION.");
        source.AppendLine("*> Each SMILE PRINT becomes one DISPLAY operation.");

        foreach (BoundStatement statement in program.Statements)
        {
            if (statement is BoundPrintStatement print)
            {
                AppendCobolPrint(source, print, identifiers, values);
            }
        }

        source.AppendLine("    STOP RUN.");

        return new GeneratedProgram(
            Language,
            new[] { new GeneratedFile("Program.cob", TextOutput.EnsureOneTrailingNewLine(source.ToString()), IsPrimary: true) });
    }

    private static void AppendCobolLet(
        StringBuilder source,
        BoundLetStatement let,
        TargetIdentifierMap identifiers)
    {
        string name = identifiers.Get(let.Variable);
        if (let.ConstantValue.Length == 0)
        {
            // COBOL has no zero-length PIC X storage item. The placeholder is
            // never displayed for an empty SMILE value; the PRINT lowering skips
            // zero-length variable operands so COBOL padding cannot leak out.
            source.AppendLine($"01 {name} PIC X VALUE SPACE.");
            return;
        }

        int byteLength = TargetEscapes.CobolByteLength(let.ConstantValue);
        string picture = byteLength == 1 ? "PIC X" : $"PIC X({byteLength})";
        source.AppendLine($"01 {name} {picture} VALUE {TargetEscapes.CobolString(let.ConstantValue)}.");
    }

    private static void AppendCobolPrint(
        StringBuilder source,
        BoundPrintStatement print,
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<VariableSymbol, string> values)
    {
        List<string> operands = BuildDisplayOperands(print, identifiers, values);
        if (print.IsBlankLine || operands.Count == 0)
        {
            // DISPLAY "" emits one space in GnuCOBOL. A no-advancing line-feed
            // emits exactly the blank line SMILE PRINT requires.
            source.AppendLine("    DISPLAY X\"0A\" WITH NO ADVANCING.");
            return;
        }

        source.Append("    DISPLAY ");
        source.Append(string.Join(" ", operands));
        source.AppendLine(".");
    }

    private static List<string> BuildDisplayOperands(
        BoundPrintStatement print,
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<VariableSymbol, string> values)
    {
        var operands = new List<string>();
        foreach (PrintSegment segment in BoundStringExpression.FlattenForOutput(print.Value))
        {
            switch (segment)
            {
                case LiteralPrintSegment literal when literal.Text.Length > 0:
                    operands.Add(TargetEscapes.CobolString(literal.Text));
                    break;

                case VariablePrintSegment variable:
                    if (values.TryGetValue(variable.Variable, out string? value) && value.Length > 0)
                    {
                        operands.Add(identifiers.Get(variable.Variable));
                    }

                    break;
            }
        }

        return operands;
    }
}

internal sealed class ObjectiveCCodeGenerator : ICodeGenerator
{
    public TargetLanguage Language => TargetLanguage.ObjectiveC;

    public GeneratedProgram Generate(BoundProgram program)
    {
        TargetIdentifierMap identifiers = TargetIdentifierMap.Create(program, Language);
        var source = new StringBuilder();
        source.AppendLine("#include <stdio.h>");
        source.AppendLine();
        source.AppendLine("int main(void)");
        source.AppendLine("{");

        bool emittedDeclaration = false;
        bool emittedExecutable = false;
        bool emittedBodyStatement = false;

        foreach (BoundStatement statement in program.Statements)
        {
            switch (statement)
            {
                case BoundLetStatement let:
                    // The Windows-local Objective-C toolchain uses Clang/MSYS2
                    // without Foundation. SMILE v1.0 strings are immutable
                    // compile-time values, so plain C string pointers keep this
                    // target easy to build while still compiling as Objective-C.
                    source.AppendLine($"    const char *{identifiers.Get(let.Variable)} = {TargetEscapes.CString(let.ConstantValue)};");
                    emittedDeclaration = true;
                    emittedBodyStatement = true;
                    break;

                case BoundPrintStatement print:
                    if (!emittedExecutable && emittedDeclaration)
                    {
                        source.AppendLine();
                    }

                    AppendObjectiveCPrint(source, print, identifiers);
                    emittedExecutable = true;
                    emittedBodyStatement = true;
                    break;
            }
        }

        if (emittedBodyStatement)
        {
            source.AppendLine();
        }

        source.AppendLine("    return 0;");
        source.AppendLine("}");

        return new GeneratedProgram(
            Language,
            new[] { new GeneratedFile("Program.m", TextOutput.EnsureOneTrailingNewLine(source.ToString()), IsPrimary: true) });
    }

    private static void AppendObjectiveCPrint(
        StringBuilder source,
        BoundPrintStatement print,
        TargetIdentifierMap identifiers)
    {
        CPrintfPlan plan = CPrintfPlan.FromPrint(print, variable => identifiers.Get(variable));
        CCodeGenerator.AppendPrintfCall(source, "    ", plan);
    }
}

internal sealed class SwiftCodeGenerator : ICodeGenerator
{
    public TargetLanguage Language => TargetLanguage.Swift;

    public GeneratedProgram Generate(BoundProgram program)
    {
        TargetIdentifierMap identifiers = TargetIdentifierMap.Create(program, Language);
        var source = new StringBuilder();

        foreach (BoundStatement statement in program.Statements)
        {
            switch (statement)
            {
                case BoundLetStatement let:
                    source.AppendLine($"let {identifiers.Get(let.Variable)} = {TargetExpression.Swift(let.Initializer, identifiers)}");
                    break;

                case BoundPrintStatement print:
                    source.AppendLine(print.IsBlankLine
                        ? "print()"
                        : $"print({TargetExpression.Swift(print.Value, identifiers)})");
                    break;
            }
        }

        return new GeneratedProgram(
            Language,
            new[] { new GeneratedFile("Program.swift", TextOutput.EnsureOneTrailingNewLine(source.ToString()), IsPrimary: true) });
    }
}

internal sealed record CPrintfPlan(
    string FormatText,
    IReadOnlyList<string> Arguments)
{
    public static CPrintfPlan FromPrint(
        BoundPrintStatement print,
        Func<VariableSymbol, string> renderVariable)
    {
        var format = new StringBuilder();
        var arguments = new List<string>();

        // FormatText is raw printf format text, not C source text. Literal
        // percent signs are doubled here for printf safety; C string escaping
        // happens later exactly once when the call is emitted.
        foreach (PrintSegment segment in BoundStringExpression.FlattenForOutput(print.Value))
        {
            switch (segment)
            {
                case LiteralPrintSegment literal:
                    AppendLiteralToFormat(format, literal.Text);
                    break;

                case VariablePrintSegment variable:
                    format.Append("%s");
                    arguments.Add(renderVariable(variable.Variable));
                    break;
            }
        }

        format.Append('\n');
        return new CPrintfPlan(format.ToString(), arguments);
    }

    private static void AppendLiteralToFormat(StringBuilder format, string text)
    {
        foreach (char value in text)
        {
            // A user-authored '%' is data, never a printf directive. Doubling
            // it keeps every generated format string compiler-owned and safe.
            format.Append(value == '%' ? "%%" : value);
        }
    }
}

internal static class TargetExpression
{
    public static string CSharp(BoundExpression expression, TargetIdentifierMap identifiers) =>
        expression switch
        {
            BoundStringLiteralExpression literal => TargetEscapes.CSharpString(literal.Value),
            BoundVariableExpression variable => identifiers.Get(variable.Variable),
            BoundConcatenationExpression concatenation => JoinConcatenation(
                concatenation,
                part => CSharp(part, identifiers)),
            BoundInterpolatedStringExpression interpolated => CSharpInterpolatedString(interpolated, identifiers),
            _ => TargetEscapes.CSharpString(string.Empty)
        };

    public static string JavaScript(BoundExpression expression, TargetIdentifierMap identifiers) =>
        expression switch
        {
            BoundStringLiteralExpression literal => TargetEscapes.JavaScriptString(literal.Value),
            BoundVariableExpression variable => identifiers.Get(variable.Variable),
            BoundConcatenationExpression concatenation => JoinConcatenation(
                concatenation,
                part => JavaScript(part, identifiers)),
            BoundInterpolatedStringExpression interpolated => JavaScriptTemplateLiteral(interpolated, identifiers),
            _ => TargetEscapes.JavaScriptString(string.Empty)
        };

    public static string Java(BoundExpression expression, TargetIdentifierMap identifiers) =>
        expression switch
        {
            BoundStringLiteralExpression literal => TargetEscapes.JavaString(literal.Value),
            BoundVariableExpression variable => identifiers.Get(variable.Variable),
            BoundConcatenationExpression concatenation => JoinConcatenation(
                concatenation,
                part => Java(part, identifiers)),
            BoundInterpolatedStringExpression interpolated => JoinSegments(
                BoundStringExpression.FlattenForOutput(interpolated),
                TargetEscapes.JavaString,
                identifiers),
            _ => TargetEscapes.JavaString(string.Empty)
        };

    public static string Swift(BoundExpression expression, TargetIdentifierMap identifiers) =>
        expression switch
        {
            BoundStringLiteralExpression literal => TargetEscapes.SwiftString(literal.Value),
            BoundVariableExpression variable => identifiers.Get(variable.Variable),
            BoundConcatenationExpression concatenation => JoinConcatenation(
                concatenation,
                part => Swift(part, identifiers)),
            BoundInterpolatedStringExpression interpolated => SwiftInterpolatedString(interpolated, identifiers),
            _ => TargetEscapes.SwiftString(string.Empty)
        };

    private static string JoinConcatenation(
        BoundConcatenationExpression expression,
        Func<BoundExpression, string> renderExpression) =>
        renderExpression(expression.Left) + " + " + renderExpression(expression.Right);

    private static string CSharpInterpolatedString(
        BoundInterpolatedStringExpression expression,
        TargetIdentifierMap identifiers) =>
        "$\"" + string.Concat(expression.Parts.Select(part => part switch
        {
            BoundInterpolatedTextPart text => TargetEscapes.CSharpInterpolatedText(text.Text),
            BoundInterpolationExpressionPart interpolation => "{" + CSharp(interpolation.Expression, identifiers) + "}",
            _ => string.Empty
        })) + "\"";

    private static string JavaScriptTemplateLiteral(
        BoundInterpolatedStringExpression expression,
        TargetIdentifierMap identifiers) =>
        "`" + string.Concat(expression.Parts.Select(part => part switch
        {
            BoundInterpolatedTextPart text => TargetEscapes.JavaScriptTemplateText(text.Text),
            BoundInterpolationExpressionPart interpolation => "${" + JavaScript(interpolation.Expression, identifiers) + "}",
            _ => string.Empty
        })) + "`";

    private static string SwiftInterpolatedString(
        BoundInterpolatedStringExpression expression,
        TargetIdentifierMap identifiers) =>
        "\"" + string.Concat(expression.Parts.Select(part => part switch
        {
            BoundInterpolatedTextPart text => TargetEscapes.SwiftInterpolatedText(text.Text),
            BoundInterpolationExpressionPart interpolation => "\\(" + Swift(interpolation.Expression, identifiers) + ")",
            _ => string.Empty
        })) + "\"";

    private static string JoinSegments(
        IReadOnlyList<PrintSegment> segments,
        Func<string, string> quoteLiteral,
        TargetIdentifierMap identifiers)
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
                VariablePrintSegment variable => identifiers.Get(variable.Variable),
                _ => quoteLiteral(string.Empty)
            }));
    }
}

internal static class TargetEscapes
{
    public static string CSharpString(string text) => Quote(EscapeCSharp(text));

    public static string CString(string text) => Quote(EscapeCStyle(text));

    public static string ObjectiveCString(string text) => "@" + Quote(EscapeCStyle(text));

    public static string CPrintfFormatString(string text) => CString(text);

    public static string JavaScriptString(string text) => Quote(EscapeJavaScript(text));

    public static string JavaString(string text) => Quote(EscapeJava(text));

    public static string CobolString(string text) =>
        CanUsePlainCobolLiteral(text)
            ? Quote(text.Replace("\"", "\"\"", StringComparison.Ordinal))
            : "X\"" + ToHex(Encoding.UTF8.GetBytes(text)) + "\"";

    public static int CobolByteLength(string text) =>
        Encoding.UTF8.GetByteCount(text);

    public static string SwiftString(string text) => Quote(EscapeSwift(text));

    public static string CSharpInterpolatedText(string text) => EscapeCSharpInterpolatedText(text);

    public static string JavaScriptTemplateText(string text) => EscapeJavaScriptTemplateText(text);

    public static string SwiftInterpolatedText(string text) => EscapeSwift(text);

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

    private static bool CanUsePlainCobolLiteral(string text) =>
        text.Length > 0 &&
        text.All(value => value is >= ' ' and <= '~');

    private static string ToHex(byte[] bytes)
    {
        const string digits = "0123456789ABCDEF";
        var builder = new StringBuilder(bytes.Length * 2);

        foreach (byte value in bytes)
        {
            builder.Append(digits[value >> 4]);
            builder.Append(digits[value & 0xF]);
        }

        return builder.ToString();
    }

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

    private static string EscapeCSharpInterpolatedText(string text)
    {
        var builder = new StringBuilder();

        foreach (char value in text)
        {
            builder.Append(value switch
            {
                '{' => "{{",
                '}' => "}}",
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

    private static string EscapeJavaScriptTemplateText(string text)
    {
        var builder = new StringBuilder();

        for (int index = 0; index < text.Length; index++)
        {
            char value = text[index];
            builder.Append(value switch
            {
                '\\' => "\\\\",
                '`' => "\\`",
                '$' when index + 1 < text.Length && text[index + 1] == '{' => "\\$",
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
