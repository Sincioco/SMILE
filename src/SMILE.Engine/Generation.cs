using System.Globalization;
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

        // Simplification belongs between binding and target generation. The
        // binder remains the source of truth for SMILE's signed 64-bit
        // semantics, while every backend receives the same smaller, pure
        // bound tree and therefore cannot invent target-specific identities.
        BoundProgram simplifiedProgram = BoundProgramSimplifier.Simplify(bindResult.Program);

        return languages
            .Select(language =>
            {
                ICodeGenerator generator = CodeGeneratorRegistry.Get(language);
                return new TranspileResult(language, generator.Generate(simplifiedProgram), bindResult.Diagnostics);
            })
            .ToArray();
    }
}

internal static class BoundProgramSimplifier
{
    public static BoundProgram Simplify(BoundProgram program)
    {
        var values = new Dictionary<VariableSymbol, SmileValue>();
        var statements = new List<BoundStatement>(program.Statements.Count);

        foreach (BoundStatement statement in program.Statements)
        {
            switch (statement)
            {
                case BoundLetStatement let:
                    statements.Add(let with
                    {
                        Initializer = SimplifyExpression(let.Initializer, values)
                    });
                    values.Add(let.Variable, let.ConstantValue);
                    break;

                case BoundPrintStatement print:
                    statements.Add(print with
                    {
                        Value = SimplifyExpression(print.Value, values)
                    });
                    break;

                default:
                    statements.Add(statement);
                    break;
            }
        }

        return new BoundProgram(statements, program.Variables);
    }

    private static BoundExpression SimplifyExpression(
        BoundExpression expression,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values) =>
        expression switch
        {
            BoundUnaryExpression unary => SimplifyUnary(unary, values),
            BoundBinaryExpression binary => SimplifyBinary(binary, values),
            BoundInterpolatedStringExpression interpolated => interpolated with
            {
                Parts = interpolated.Parts.Select(part => part switch
                {
                    BoundInterpolationExpressionPart hole =>
                        hole with { Expression = SimplifyExpression(hole.Expression, values) },
                    _ => part
                }).ToArray()
            },
            _ => expression
        };

    private static BoundExpression SimplifyUnary(
        BoundUnaryExpression expression,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values)
    {
        BoundExpression operand = SimplifyExpression(expression.Operand, values);
        if (expression.Operator.Kind is BoundUnaryOperatorKind.LogicalNegation &&
            operand is BoundBooleanLiteralExpression literal)
        {
            return new BoundBooleanLiteralExpression(!literal.Value);
        }

        return expression with { Operand = operand };
    }

    private static BoundExpression SimplifyBinary(
        BoundBinaryExpression expression,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values)
    {
        BoundExpression left = SimplifyExpression(expression.Left, values);

        if (expression.Operator.Kind is BoundBinaryOperatorKind.LogicalAnd or
            BoundBinaryOperatorKind.LogicalOr)
        {
            // Preserve the two readable right-side identity forms without
            // traversing the right subtree. This keeps examples such as
            // Adult AND TRUE as Adult and still respects evaluation order.
            if ((expression.Operator.Kind is BoundBinaryOperatorKind.LogicalAnd &&
                 expression.Right is BoundBooleanLiteralExpression { Value: true }) ||
                (expression.Operator.Kind is BoundBinaryOperatorKind.LogicalOr &&
                 expression.Right is BoundBooleanLiteralExpression { Value: false }))
            {
                return left;
            }

            if (BoundConstantEvaluator.TryEvaluate(left, values, out SmileValue leftValue) &&
                leftValue.Type is SmileType.Boolean)
            {
                bool rightIsUnreachable =
                    expression.Operator.Kind is BoundBinaryOperatorKind.LogicalAnd && !leftValue.BooleanValue ||
                    expression.Operator.Kind is BoundBinaryOperatorKind.LogicalOr && leftValue.BooleanValue;
                if (rightIsUnreachable)
                {
                    // Binding has already validated both operands. Skipping
                    // simplification here prevents an unreachable division or
                    // overflow from leaking into a strict target compiler.
                    return new BoundBooleanLiteralExpression(leftValue.BooleanValue);
                }

                BoundExpression reachableRight = SimplifyExpression(expression.Right, values);
                if ((expression.Operator.Kind is BoundBinaryOperatorKind.LogicalAnd &&
                     reachableRight is BoundBooleanLiteralExpression { Value: true }) ||
                    (expression.Operator.Kind is BoundBinaryOperatorKind.LogicalOr &&
                     reachableRight is BoundBooleanLiteralExpression { Value: false }))
                {
                    return left;
                }

                return reachableRight;
            }
        }

        BoundExpression right = SimplifyExpression(expression.Right, values);

        // All current SMILE expressions are pure. These Boolean identities
        // can therefore remove redundant work without changing observable
        // behavior, including the language's left-to-right short circuiting.
        if (expression.Operator.Kind is BoundBinaryOperatorKind.LogicalAnd)
        {
            if (left is BoundBooleanLiteralExpression { Value: false } ||
                right is BoundBooleanLiteralExpression { Value: false })
            {
                return new BoundBooleanLiteralExpression(false);
            }

            if (left is BoundBooleanLiteralExpression { Value: true })
            {
                return right;
            }

            if (right is BoundBooleanLiteralExpression { Value: true })
            {
                return left;
            }
        }

        if (expression.Operator.Kind is BoundBinaryOperatorKind.LogicalOr)
        {
            if (left is BoundBooleanLiteralExpression { Value: true } ||
                right is BoundBooleanLiteralExpression { Value: true })
            {
                return new BoundBooleanLiteralExpression(true);
            }

            if (left is BoundBooleanLiteralExpression { Value: false })
            {
                return right;
            }

            if (right is BoundBooleanLiteralExpression { Value: false })
            {
                return left;
            }
        }

        return expression with { Left = left, Right = right };
    }
}

internal sealed record TargetIntegerProfile(
    bool RequiresSigned64Storage,
    bool RequiresJavaScriptBigInt)
{
    private const long JavaScriptMaxSafeInteger = 9_007_199_254_740_991L;

    public static TargetIntegerProfile Analyze(BoundProgram program)
    {
        IReadOnlyDictionary<VariableSymbol, SmileValue> values =
            GeneratorValueFacts.ConstantValues(program);
        bool requiresSigned64 = false;
        bool requiresBigInt = false;

        void Observe(long value)
        {
            requiresSigned64 |= value is < int.MinValue or > int.MaxValue;
            requiresBigInt |= value is < -JavaScriptMaxSafeInteger or > JavaScriptMaxSafeInteger;
        }

        void Visit(BoundExpression expression)
        {
            // Evaluating every Integer-typed node records literal values,
            // variable operands, and arithmetic intermediates. A failed
            // evaluation can only be an intentionally unreachable expression
            // in a successfully bound program; its children are still visited.
            if (expression.Type is SmileType.Integer &&
                BoundConstantEvaluator.TryEvaluate(expression, values, out SmileValue value))
            {
                Observe(value.IntegerValue);
            }

            switch (expression)
            {
                case BoundUnaryExpression unary:
                    Visit(unary.Operand);
                    break;

                case BoundBinaryExpression binary:
                    Visit(binary.Left);
                    Visit(binary.Right);
                    break;

                case BoundInterpolatedStringExpression interpolated:
                    foreach (BoundInterpolationExpressionPart hole in
                        interpolated.Parts.OfType<BoundInterpolationExpressionPart>())
                    {
                        Visit(hole.Expression);
                    }

                    break;
            }
        }

        foreach (BoundStatement statement in program.Statements)
        {
            switch (statement)
            {
                case BoundLetStatement let:
                    if (let.ConstantValue.Type is SmileType.Integer)
                    {
                        Observe(let.ConstantValue.IntegerValue);
                    }

                    Visit(let.Initializer);
                    break;

                case BoundPrintStatement print when !print.IsBlankLine:
                    Visit(print.Value);
                    break;
            }
        }

        return new TargetIntegerProfile(requiresSigned64, requiresBigInt);
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
            new SwiftCodeGenerator(),
            new PythonCodeGenerator(),
            new CppCodeGenerator()
        }.ToDictionary(generator => generator.Language);

    public static ICodeGenerator Get(TargetLanguage language) => Generators[language];
}

internal sealed class CSharpCodeGenerator : ICodeGenerator
{
    public TargetLanguage Language => TargetLanguage.CSharp;

    public GeneratedProgram Generate(BoundProgram program)
    {
        TargetIdentifierMap identifiers = TargetIdentifierMap.Create(program, Language);
        TargetIntegerProfile integers = TargetIntegerProfile.Analyze(program);
        var source = new StringBuilder();
        source.AppendLine("using System;");
        if (CSharpGenerationFacts.NeedsInvariantCulture(program))
        {
            source.AppendLine("using System.Globalization;");
        }

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
                    string initializer = TargetExpression.CSharp(let.Initializer, identifiers, integers);
                    source.AppendLine($"        {TargetTypes.CSharp(let.Variable.Type, integers)} {identifiers.Get(let.Variable)} = {initializer};");
                    break;

                case BoundPrintStatement print:
                    if (print.IsBlankLine)
                    {
                        source.AppendLine("        Console.WriteLine();");
                    }
                    else
                    {
                        source.AppendLine($"        Console.WriteLine({TargetExpression.CSharpDisplay(print.Value, identifiers, integers)});");
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
        TargetIntegerProfile integers = TargetIntegerProfile.Analyze(program);
        IReadOnlyDictionary<VariableSymbol, SmileValue> values = GeneratorValueFacts.ConstantValues(program);
        var source = new StringBuilder();
        source.AppendLine("#include <stdio.h>");
        if (integers.RequiresSigned64Storage)
        {
            source.AppendLine("#include <stdint.h>");
        }

        if (CGenerationFacts.NeedsBooleanHeader(program))
        {
            source.AppendLine("#include <stdbool.h>");
        }

        if (CGenerationFacts.NeedsStringComparison(program, values))
        {
            source.AppendLine("#include <string.h>");
        }

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
                    string initializer = let.Variable.Type is SmileType.String
                        ? TargetExpression.CConstant(let.ConstantValue, integers)
                        : TargetExpression.C(let.Initializer, identifiers, integers, values);
                    source.AppendLine($"    {TargetTypes.CDeclaration(let.Variable.Type, identifiers.Get(let.Variable), integers)} = {initializer};");
                    emittedDeclaration = true;
                    emittedBodyStatement = true;
                    break;

                case BoundPrintStatement print:
                    if (!emittedExecutable && emittedDeclaration)
                    {
                        source.AppendLine();
                    }

                    AppendCPrint(source, print, identifiers, integers, values);
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
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values)
    {
        if (TryAppendExactNulStringPrint(source, "    ", print, values))
        {
            return;
        }

        CPrintfPlan plan = CPrintfPlan.FromPrint(
            print,
            expression => TargetExpression.C(expression, identifiers, integers, values),
            integers.RequiresSigned64Storage);
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

    internal static bool TryAppendExactNulStringPrint(
        StringBuilder source,
        string indent,
        BoundPrintStatement print,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values)
    {
        if (print.IsBlankLine ||
            !GeneratorValueFacts.TryGetNulContainingString(print.Value, values, out string value))
        {
            return false;
        }

        byte[] bytes = Encoding.UTF8.GetBytes(value);

        // A tiny nested scope lets every exact PRINT reuse the same readable
        // compiler-owned name without colliding with a SMILE variable in the
        // surrounding main function. The byte array avoids C's NUL-terminated
        // String convention and makes the UTF-8 length explicit to fwrite.
        source.Append(indent).AppendLine("{");
        source.Append(indent).Append("    static const unsigned char smilePrintBytes[] = { ");
        source.Append(string.Join(", ", bytes.Select(value => value.ToString(CultureInfo.InvariantCulture))));
        source.AppendLine(" };");
        source.Append(indent).Append("    fwrite(smilePrintBytes, 1, ");
        source.Append(bytes.Length.ToString(CultureInfo.InvariantCulture));
        source.AppendLine(", stdout);");
        source.Append(indent).AppendLine("    fputc('\\n', stdout);");
        source.Append(indent).AppendLine("}");
        return true;
    }

}

internal sealed class MasmX64CodeGenerator : ICodeGenerator
{
    public TargetLanguage Language => TargetLanguage.MasmX64;

    public GeneratedProgram Generate(BoundProgram program)
    {
        BoundLetStatement[] lets = program.Statements.OfType<BoundLetStatement>().ToArray();
        BoundPrintStatement[] prints = program.Statements.OfType<BoundPrintStatement>().ToArray();
        IReadOnlyDictionary<VariableSymbol, SmileValue> values = GeneratorValueFacts.ConstantValues(program);
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

        AppendMasmData(source, lets, prints, values);
        AppendMasmCode(source, lets, prints);

        return new GeneratedProgram(
            Language,
            new[] { new GeneratedFile("Program.asm", TextOutput.EnsureOneTrailingNewLine(source.ToString()), IsPrimary: true) });
    }

    private static void AppendMasmData(
        StringBuilder source,
        IReadOnlyList<BoundLetStatement> lets,
        IReadOnlyList<BoundPrintStatement> prints,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values)
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
            AppendMasmStringData(source, valueLabel, let.ConstantValue.ToDisplayText(), $"LET {let.Variable.Name} initial text.", "Length of the variable's current text.");
            AppendMasmLine(source, $"{VariablePointerLabel(index)} QWORD ?", $"Runtime pointer for {let.Variable.Name}.");
            AppendMasmLine(source, $"{VariableLengthLabel(index)} DWORD ?", $"Runtime length for {let.Variable.Name}.");
        }

        for (int printIndex = 0; printIndex < prints.Count; printIndex++)
        {
            string text = prints[printIndex].IsBlankLine
                ? string.Empty
                : GeneratorValueFacts.DisplayText(prints[printIndex].Value, values);
            string label = PrintLiteralLabel(printIndex, 0);
            AppendMasmStringData(source, label, text, $"PRINT #{printIndex + 1} canonical text.", "Length of this print text.");
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
            AppendMasmWriteLiteral(source, PrintLiteralLabel(printIndex, 0));
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
        TargetIntegerProfile integers = TargetIntegerProfile.Analyze(program);
        var source = new StringBuilder();

        foreach (BoundStatement statement in program.Statements)
        {
            switch (statement)
            {
                case BoundLetStatement let:
                    source.AppendLine($"let {identifiers.Get(let.Variable)} = {TargetExpression.JavaScript(let.Initializer, identifiers, integers)};");
                    break;

                case BoundPrintStatement print:
                    source.AppendLine(print.IsBlankLine
                        ? "console.log();"
                        : $"console.log({TargetExpression.JavaScriptDisplay(print.Value, identifiers, integers)});");
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
        TargetIntegerProfile integers = TargetIntegerProfile.Analyze(program);
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
                    string initializer = TargetExpression.Java(let.Initializer, identifiers, integers);
                    source.AppendLine($"        {TargetTypes.Java(let.Variable.Type, integers)} {identifiers.Get(let.Variable)} = {initializer};");
                    break;

                case BoundPrintStatement print:
                    source.AppendLine(print.IsBlankLine
                        ? "        System.out.println();"
                        : $"        System.out.println({TargetExpression.JavaDisplay(print.Value, identifiers, integers)});");
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
        string value = let.ConstantValue.ToDisplayText();
        if (value.Length == 0)
        {
            // COBOL has no zero-length PIC X storage item. The placeholder is
            // never displayed for an empty SMILE value; the PRINT lowering skips
            // zero-length variable operands so COBOL padding cannot leak out.
            source.AppendLine($"01 {name} PIC X VALUE SPACE.");
            return;
        }

        int byteLength = TargetEscapes.CobolByteLength(value);
        string picture = byteLength == 1 ? "PIC X" : $"PIC X({byteLength})";
        source.AppendLine($"01 {name} {picture} VALUE {TargetEscapes.CobolString(value)}.");
    }

    private static void AppendCobolPrint(
        StringBuilder source,
        BoundPrintStatement print,
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values)
    {
        string text = print.IsBlankLine
            ? string.Empty
            : GeneratorValueFacts.DisplayText(print.Value, values);
        if (text.Length == 0)
        {
            // DISPLAY "" emits one space in GnuCOBOL. A no-advancing line-feed
            // emits exactly the blank line SMILE PRINT requires.
            source.AppendLine("    DISPLAY X\"0A\" WITH NO ADVANCING.");
            return;
        }

        source.Append("    DISPLAY ");
        source.Append(TargetEscapes.CobolString(text));
        source.AppendLine(".");
    }
}

internal sealed class ObjectiveCCodeGenerator : ICodeGenerator
{
    public TargetLanguage Language => TargetLanguage.ObjectiveC;

    public GeneratedProgram Generate(BoundProgram program)
    {
        TargetIdentifierMap identifiers = TargetIdentifierMap.Create(program, Language);
        TargetIntegerProfile integers = TargetIntegerProfile.Analyze(program);
        IReadOnlyDictionary<VariableSymbol, SmileValue> values = GeneratorValueFacts.ConstantValues(program);
        var source = new StringBuilder();
        source.AppendLine("#include <stdio.h>");
        if (integers.RequiresSigned64Storage)
        {
            source.AppendLine("#include <stdint.h>");
        }

        if (CGenerationFacts.NeedsBooleanHeader(program))
        {
            source.AppendLine("#include <stdbool.h>");
        }

        if (CGenerationFacts.NeedsStringComparison(program, values))
        {
            source.AppendLine("#include <string.h>");
        }

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
                    // without Foundation. C-compatible console types keep this
                    // target easy to build while still compiling as Objective-C.
                    string initializer = let.Variable.Type is SmileType.String
                        ? TargetExpression.CConstant(let.ConstantValue, integers)
                        : TargetExpression.ObjectiveC(let.Initializer, identifiers, integers, values);
                    source.AppendLine($"    {TargetTypes.CDeclaration(let.Variable.Type, identifiers.Get(let.Variable), integers)} = {initializer};");
                    emittedDeclaration = true;
                    emittedBodyStatement = true;
                    break;

                case BoundPrintStatement print:
                    if (!emittedExecutable && emittedDeclaration)
                    {
                        source.AppendLine();
                    }

                    AppendObjectiveCPrint(source, print, identifiers, integers, values);
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
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values)
    {
        if (CCodeGenerator.TryAppendExactNulStringPrint(source, "    ", print, values))
        {
            return;
        }

        CPrintfPlan plan = CPrintfPlan.FromPrint(
            print,
            expression => TargetExpression.ObjectiveC(expression, identifiers, integers, values),
            integers.RequiresSigned64Storage);
        CCodeGenerator.AppendPrintfCall(source, "    ", plan);
    }
}

internal sealed class SwiftCodeGenerator : ICodeGenerator
{
    public TargetLanguage Language => TargetLanguage.Swift;

    public GeneratedProgram Generate(BoundProgram program)
    {
        TargetIdentifierMap identifiers = TargetIdentifierMap.Create(program, Language);
        TargetIntegerProfile integers = TargetIntegerProfile.Analyze(program);
        var source = new StringBuilder();

        foreach (BoundStatement statement in program.Statements)
        {
            switch (statement)
            {
                case BoundLetStatement let:
                    string initializer = TargetExpression.Swift(let.Initializer, identifiers, integers);
                    source.AppendLine($"let {identifiers.Get(let.Variable)}: {TargetTypes.Swift(let.Variable.Type, integers)} = {initializer}");
                    break;

                case BoundPrintStatement print:
                    source.AppendLine(print.IsBlankLine
                        ? "print()"
                        : $"print({TargetExpression.SwiftDisplay(print.Value, identifiers, integers)})");
                    break;
            }
        }

        return new GeneratedProgram(
            Language,
            new[] { new GeneratedFile("Program.swift", TextOutput.EnsureOneTrailingNewLine(source.ToString()), IsPrimary: true) });
    }
}

internal sealed class PythonCodeGenerator : ICodeGenerator
{
    public TargetLanguage Language => TargetLanguage.Python;

    public GeneratedProgram Generate(BoundProgram program)
    {
        TargetIdentifierMap identifiers = TargetIdentifierMap.Create(program, Language);
        IReadOnlyDictionary<VariableSymbol, SmileValue> values = GeneratorValueFacts.ConstantValues(program);
        var expressions = new PythonExpressionWriter(identifiers, values);
        var source = new StringBuilder();
        bool emittedHelper = false;

        if (PythonGenerationFacts.NeedsTextHelper(program))
        {
            source.AppendLine("def _smile_text(value: object) -> str:");
            source.AppendLine("    if isinstance(value, bool):");
            source.AppendLine("        return \"TRUE\" if value else \"FALSE\"");
            source.AppendLine();
            source.AppendLine("    return str(value)");
            emittedHelper = true;
        }

        if (PythonGenerationFacts.NeedsDivisionHelper(program))
        {
            if (emittedHelper)
            {
                source.AppendLine();
                source.AppendLine();
            }

            source.AppendLine("def _smile_div(left: int, right: int) -> int:");
            source.AppendLine("    quotient = abs(left) // abs(right)");
            source.AppendLine("    return -quotient if (left < 0) != (right < 0) else quotient");
            emittedHelper = true;
        }

        if (emittedHelper)
        {
            source.AppendLine();
            source.AppendLine();
        }

        source.AppendLine("def main() -> None:");
        if (program.Statements.Count == 0)
        {
            source.AppendLine("    pass");
        }
        else
        {
            foreach (BoundStatement statement in program.Statements)
            {
                switch (statement)
                {
                    case BoundLetStatement let:
                        source.AppendLine($"    {identifiers.Get(let.Variable)} = {expressions.Write(let.Initializer)}");
                        break;

                    case BoundPrintStatement print:
                        source.AppendLine(print.IsBlankLine
                            ? "    print()"
                            : $"    print({expressions.WriteDisplay(print.Value)})");
                        break;
                }
            }
        }

        source.AppendLine();
        source.AppendLine();
        source.AppendLine("if __name__ == \"__main__\":");
        source.AppendLine("    main()");

        return new GeneratedProgram(
            Language,
            new[] { new GeneratedFile("Program.py", TextOutput.EnsureOneTrailingNewLine(source.ToString()), IsPrimary: true) });
    }
}

internal sealed class CppCodeGenerator : ICodeGenerator
{
    public TargetLanguage Language => TargetLanguage.Cpp;

    public GeneratedProgram Generate(BoundProgram program)
    {
        TargetIdentifierMap identifiers = TargetIdentifierMap.Create(program, Language);
        TargetIntegerProfile integers = TargetIntegerProfile.Analyze(program);
        var expressions = new CppExpressionWriter(identifiers, integers);
        var source = new StringBuilder();

        bool needsIostream = program.Statements.Any(statement => statement is BoundPrintStatement);
        bool needsString = CppGenerationFacts.NeedsStringHeader(program);

        if (needsIostream)
        {
            source.AppendLine("#include <iostream>");
        }

        if (needsString)
        {
            source.AppendLine("#include <string>");
        }

        if (integers.RequiresSigned64Storage)
        {
            source.AppendLine("#include <cstdint>");
        }

        if (needsIostream || needsString || integers.RequiresSigned64Storage)
        {
            source.AppendLine();
        }

        source.AppendLine("int main()");
        source.AppendLine("{");

        bool emittedDeclaration = false;
        bool emittedExecutable = false;
        foreach (BoundStatement statement in program.Statements)
        {
            switch (statement)
            {
                case BoundLetStatement let:
                    source.AppendLine(
                        $"    {TargetTypes.Cpp(let.Variable.Type, integers)} {identifiers.Get(let.Variable)} = {expressions.Write(let.Initializer)};");
                    emittedDeclaration = true;
                    break;

                case BoundPrintStatement print:
                    if (!emittedExecutable && emittedDeclaration)
                    {
                        source.AppendLine();
                    }

                    AppendPrint(source, print, expressions);
                    emittedExecutable = true;
                    break;
            }
        }

        if (program.Statements.Count > 0)
        {
            source.AppendLine();
        }

        source.AppendLine("    return 0;");
        source.AppendLine("}");

        return new GeneratedProgram(
            Language,
            new[]
            {
                new GeneratedFile(
                    "Program.cpp",
                    TextOutput.EnsureOneTrailingNewLine(source.ToString()),
                    IsPrimary: true)
            });
    }

    private static void AppendPrint(
        StringBuilder source,
        BoundPrintStatement print,
        CppExpressionWriter expressions)
    {
        source.Append("    std::cout");

        if (print.IsBlankLine)
        {
            source.AppendLine(" << '\\n';");
            return;
        }

        if (print.Value is BoundInterpolatedStringExpression interpolated)
        {
            bool emittedPart = false;
            foreach (BoundInterpolatedPart part in interpolated.Parts)
            {
                string? text = part switch
                {
                    BoundInterpolatedTextPart literal when literal.Text.Length > 0 =>
                        expressions.WriteStringLiteral(literal.Text),
                    BoundInterpolationExpressionPart hole => expressions.WriteForStream(hole.Expression),
                    _ => null
                };

                if (text is not null)
                {
                    source.Append(" << ");
                    source.Append(text);
                    emittedPart = true;
                }
            }

            if (!emittedPart)
            {
                source.Append(" << \"\"");
            }
        }
        else
        {
            source.Append(" << ");
            source.Append(expressions.WriteForStream(print.Value));
        }

        source.AppendLine(" << '\\n';");
    }
}

internal static class CppGenerationFacts
{
    public static bool NeedsStringHeader(BoundProgram program) =>
        program.Statements.Any(statement => statement switch
        {
            BoundLetStatement let =>
                let.Variable.Type is SmileType.String || ContainsStringFacility(let.Initializer),
            BoundPrintStatement print when !print.IsBlankLine => ContainsStringFacility(print.Value),
            _ => false
        });

    private static bool ContainsStringFacility(BoundExpression expression) =>
        expression switch
        {
            BoundStringLiteralExpression literal => literal.Value.Contains('\0', StringComparison.Ordinal),
            BoundVariableExpression variable => variable.Variable.Type is SmileType.String,
            BoundUnaryExpression unary => ContainsStringFacility(unary.Operand),
            BoundBinaryExpression binary =>
                binary.Operator.Kind is BoundBinaryOperatorKind.StringConcatenation ||
                (binary.Left.Type is SmileType.String &&
                    binary.Operator.Kind is BoundBinaryOperatorKind.Equality or BoundBinaryOperatorKind.Inequality) ||
                ContainsStringFacility(binary.Left) ||
                ContainsStringFacility(binary.Right),
            BoundInterpolatedStringExpression => true,
            _ => false
        };
}

internal sealed class CppExpressionWriter
{
    private readonly TargetIdentifierMap _identifiers;
    private readonly TargetIntegerProfile _integers;

    public CppExpressionWriter(
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers)
    {
        _identifiers = identifiers;
        _integers = integers;
    }

    public string Write(BoundExpression expression) =>
        WriteExpression(expression, parentPrecedence: 0, isRightChild: false, parentOperator: null);

    public string WriteForStream(BoundExpression expression) =>
        expression.Type is SmileType.Boolean
            ? $"({Write(expression)} ? \"TRUE\" : \"FALSE\")"
            : Write(expression);

    public string WriteStringLiteral(string value) =>
        value.Contains('\0', StringComparison.Ordinal)
            ? $"std::string{{{TargetEscapes.CString(value)}, {Encoding.UTF8.GetByteCount(value).ToString(CultureInfo.InvariantCulture)}}}"
            : TargetEscapes.CString(value);

    private string WriteExpression(
        BoundExpression expression,
        int parentPrecedence,
        bool isRightChild,
        BoundBinaryOperatorKind? parentOperator) =>
        expression switch
        {
            BoundStringLiteralExpression literal => WriteStringLiteral(literal.Value),
            BoundIntegerLiteralExpression literal => IntegerLiteral(literal.Value),
            BoundBooleanLiteralExpression literal => literal.Value ? "true" : "false",
            BoundVariableExpression variable => _identifiers.Get(variable.Variable),
            BoundUnaryExpression unary => WriteUnary(unary, parentPrecedence),
            BoundBinaryExpression binary => WriteBinary(binary, parentPrecedence, isRightChild, parentOperator),
            BoundInterpolatedStringExpression interpolated => WriteInterpolatedString(interpolated),
            _ => "std::string{}"
        };

    private string WriteUnary(BoundUnaryExpression expression, int parentPrecedence)
    {
        const int precedence = 7;
        string op = expression.Operator.Kind switch
        {
            BoundUnaryOperatorKind.Identity => "+",
            BoundUnaryOperatorKind.Negation => "-",
            BoundUnaryOperatorKind.LogicalNegation => "!",
            _ => string.Empty
        };
        string operand = WriteExpression(expression.Operand, precedence, isRightChild: true, parentOperator: null);
        string text = op + operand;
        return precedence < parentPrecedence ? "(" + text + ")" : text;
    }

    private string WriteBinary(
        BoundBinaryExpression expression,
        int parentPrecedence,
        bool isRightChild,
        BoundBinaryOperatorKind? parentOperator)
    {
        int precedence = Precedence(expression.Operator.Kind);
        string left = WriteExpression(expression.Left, precedence, isRightChild: false, expression.Operator.Kind);
        string right = WriteExpression(expression.Right, precedence, isRightChild: true, expression.Operator.Kind);

        if (expression.Operator.Kind is BoundBinaryOperatorKind.StringConcatenation &&
            !ProducesOwnedString(expression.Left))
        {
            // Two C++ string literals cannot be added because both decay to
            // pointers. Starting the chain with an owned std::string keeps the
            // source natural while making every legal SMILE concatenation valid.
            left = "std::string{" + left + "}";
        }

        if (expression.Left.Type is SmileType.String &&
            expression.Operator.Kind is BoundBinaryOperatorKind.Equality or BoundBinaryOperatorKind.Inequality &&
            !ProducesOwnedString(expression.Left))
        {
            // std::string equality is length-aware, including embedded NUL.
            // Owning a literal left operand also avoids pointer comparison.
            left = "std::string{" + left + "}";
        }

        string text = left + " " + OperatorText(expression.Operator.Kind) + " " + right;
        return NeedsParentheses(precedence, parentPrecedence, isRightChild, parentOperator)
            ? "(" + text + ")"
            : text;
    }

    private string WriteInterpolatedString(BoundInterpolatedStringExpression expression)
    {
        var segments = new List<(string Text, bool IsOwned)>();

        foreach (BoundInterpolatedPart part in expression.Parts)
        {
            switch (part)
            {
                case BoundInterpolatedTextPart text when text.Text.Length > 0:
                    segments.Add((WriteStringLiteral(text.Text), text.Text.Contains('\0', StringComparison.Ordinal)));
                    break;

                case BoundInterpolationExpressionPart hole when hole.Expression.Type is SmileType.String:
                    segments.Add((Write(hole.Expression), ProducesOwnedString(hole.Expression)));
                    break;

                case BoundInterpolationExpressionPart hole when hole.Expression.Type is SmileType.Integer:
                    segments.Add(($"std::to_string({Write(hole.Expression)})", true));
                    break;

                case BoundInterpolationExpressionPart hole when hole.Expression.Type is SmileType.Boolean:
                    segments.Add(($"({Write(hole.Expression)} ? \"TRUE\" : \"FALSE\")", false));
                    break;
            }
        }

        if (segments.Count == 0)
        {
            return "std::string{}";
        }

        if (!segments[0].IsOwned)
        {
            segments[0] = ("std::string{" + segments[0].Text + "}", true);
        }

        return string.Join(" + ", segments.Select(segment => segment.Text));
    }

    private string IntegerLiteral(long value)
    {
        if (!_integers.RequiresSigned64Storage)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        if (value == long.MinValue)
        {
            return "INT64_MIN";
        }

        return value < 0
            ? "-INT64_C(" + (-value).ToString(CultureInfo.InvariantCulture) + ")"
            : "INT64_C(" + value.ToString(CultureInfo.InvariantCulture) + ")";
    }

    private static bool ProducesOwnedString(BoundExpression expression) =>
        expression switch
        {
            BoundStringLiteralExpression literal => literal.Value.Contains('\0', StringComparison.Ordinal),
            BoundVariableExpression variable => variable.Variable.Type is SmileType.String,
            BoundBinaryExpression binary => binary.Operator.Kind is BoundBinaryOperatorKind.StringConcatenation,
            BoundInterpolatedStringExpression => true,
            _ => false
        };

    private static string OperatorText(BoundBinaryOperatorKind kind) =>
        kind switch
        {
            BoundBinaryOperatorKind.Addition or BoundBinaryOperatorKind.StringConcatenation => "+",
            BoundBinaryOperatorKind.Subtraction => "-",
            BoundBinaryOperatorKind.Multiplication => "*",
            BoundBinaryOperatorKind.Division => "/",
            BoundBinaryOperatorKind.Equality => "==",
            BoundBinaryOperatorKind.Inequality => "!=",
            BoundBinaryOperatorKind.Less => "<",
            BoundBinaryOperatorKind.LessOrEquals => "<=",
            BoundBinaryOperatorKind.Greater => ">",
            BoundBinaryOperatorKind.GreaterOrEquals => ">=",
            BoundBinaryOperatorKind.LogicalAnd => "&&",
            BoundBinaryOperatorKind.LogicalOr => "||",
            _ => string.Empty
        };

    private static int Precedence(BoundBinaryOperatorKind kind) =>
        kind switch
        {
            BoundBinaryOperatorKind.Multiplication or BoundBinaryOperatorKind.Division => 6,
            BoundBinaryOperatorKind.Addition or BoundBinaryOperatorKind.Subtraction or
                BoundBinaryOperatorKind.StringConcatenation => 5,
            BoundBinaryOperatorKind.Less or BoundBinaryOperatorKind.LessOrEquals or
                BoundBinaryOperatorKind.Greater or BoundBinaryOperatorKind.GreaterOrEquals => 4,
            BoundBinaryOperatorKind.Equality or BoundBinaryOperatorKind.Inequality => 3,
            BoundBinaryOperatorKind.LogicalAnd => 2,
            BoundBinaryOperatorKind.LogicalOr => 1,
            _ => 0
        };

    private static bool NeedsParentheses(
        int precedence,
        int parentPrecedence,
        bool isRightChild,
        BoundBinaryOperatorKind? parentOperator)
    {
        if (precedence < parentPrecedence)
        {
            return true;
        }

        return isRightChild &&
            precedence == parentPrecedence &&
            parentOperator is not (
                BoundBinaryOperatorKind.Addition or
                BoundBinaryOperatorKind.Multiplication or
                BoundBinaryOperatorKind.StringConcatenation or
                BoundBinaryOperatorKind.LogicalAnd or
                BoundBinaryOperatorKind.LogicalOr);
    }
}

internal static class PythonGenerationFacts
{
    public static bool NeedsTextHelper(BoundProgram program) =>
        program.Statements.Any(statement => statement switch
        {
            BoundLetStatement let => ContainsTextConversion(let.Initializer),
            BoundPrintStatement print when !print.IsBlankLine =>
                print.Value.Type is not SmileType.String || ContainsTextConversion(print.Value),
            _ => false
        });

    public static bool NeedsDivisionHelper(BoundProgram program) =>
        program.Statements.Any(statement => statement switch
        {
            BoundLetStatement let => ContainsDivision(let.Initializer),
            BoundPrintStatement print when !print.IsBlankLine => ContainsDivision(print.Value),
            _ => false
        });

    private static bool ContainsTextConversion(BoundExpression expression) =>
        expression switch
        {
            BoundUnaryExpression unary => ContainsTextConversion(unary.Operand),
            BoundBinaryExpression binary =>
                ContainsTextConversion(binary.Left) || ContainsTextConversion(binary.Right),
            BoundInterpolatedStringExpression interpolated => interpolated.Parts.Any(part =>
                part is BoundInterpolationExpressionPart hole &&
                (hole.Expression.Type is not SmileType.String || ContainsTextConversion(hole.Expression))),
            _ => false
        };

    private static bool ContainsDivision(BoundExpression expression) =>
        expression switch
        {
            BoundUnaryExpression unary => ContainsDivision(unary.Operand),
            BoundBinaryExpression binary =>
                binary.Operator.Kind is BoundBinaryOperatorKind.Division ||
                ContainsDivision(binary.Left) ||
                ContainsDivision(binary.Right),
            BoundInterpolatedStringExpression interpolated => interpolated.Parts.Any(part =>
                part is BoundInterpolationExpressionPart hole && ContainsDivision(hole.Expression)),
            _ => false
        };
}

internal sealed class PythonExpressionWriter
{
    private const int OrPrecedence = 1;
    private const int AndPrecedence = 2;
    private const int NotPrecedence = 3;
    private const int ComparisonPrecedence = 4;
    private const int AdditionPrecedence = 5;
    private const int MultiplicationPrecedence = 6;
    private const int UnaryPrecedence = 7;
    private const int CallPrecedence = 8;

    private readonly TargetIdentifierMap _identifiers;
    private readonly IReadOnlyDictionary<VariableSymbol, SmileValue> _values;

    public PythonExpressionWriter(
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values)
    {
        _identifiers = identifiers;
        _values = values;
    }

    public string Write(BoundExpression expression) =>
        WriteExpression(expression, parentPrecedence: 0, isRightChild: false, parentOperator: null);

    public string WriteDisplay(BoundExpression expression) =>
        expression.Type is SmileType.String
            ? Write(expression)
            : $"_smile_text({Write(expression)})";

    private string WriteExpression(
        BoundExpression expression,
        int parentPrecedence,
        bool isRightChild,
        BoundBinaryOperatorKind? parentOperator) =>
        expression switch
        {
            BoundStringLiteralExpression literal => TargetEscapes.PythonString(literal.Value),
            BoundIntegerLiteralExpression literal => literal.Value.ToString(CultureInfo.InvariantCulture),
            BoundBooleanLiteralExpression literal => literal.Value ? "True" : "False",
            BoundVariableExpression variable => _identifiers.Get(variable.Variable),
            BoundUnaryExpression unary => WriteUnary(unary, parentPrecedence),
            BoundBinaryExpression binary => WriteBinary(binary, parentPrecedence, isRightChild, parentOperator),
            BoundInterpolatedStringExpression interpolated => WriteInterpolatedString(interpolated),
            _ => TargetEscapes.PythonString(string.Empty)
        };

    private string WriteUnary(BoundUnaryExpression expression, int parentPrecedence)
    {
        if (expression.Operator.Kind is BoundUnaryOperatorKind.LogicalNegation)
        {
            string operand = expression.Operand is BoundBinaryExpression
                ? "(" + WriteExpression(expression.Operand, 0, isRightChild: false, parentOperator: null) + ")"
                : WriteExpression(expression.Operand, NotPrecedence, isRightChild: true, parentOperator: null);
            string logicalText = "not " + operand;
            return NotPrecedence < parentPrecedence ? "(" + logicalText + ")" : logicalText;
        }

        string op = expression.Operator.Kind is BoundUnaryOperatorKind.Negation ? "-" : "+";
        string value = WriteExpression(
            expression.Operand,
            UnaryPrecedence,
            isRightChild: true,
            parentOperator: null);
        string text = op + value;
        return UnaryPrecedence < parentPrecedence ? "(" + text + ")" : text;
    }

    private string WriteBinary(
        BoundBinaryExpression expression,
        int parentPrecedence,
        bool isRightChild,
        BoundBinaryOperatorKind? parentOperator)
    {
        if (expression.Operator.Kind is BoundBinaryOperatorKind.Division)
        {
            string call =
                "_smile_div(" +
                WriteExpression(expression.Left, 0, isRightChild: false, parentOperator: null) +
                ", " +
                WriteExpression(expression.Right, 0, isRightChild: false, parentOperator: null) +
                ")";
            return CallPrecedence < parentPrecedence ? "(" + call + ")" : call;
        }

        int precedence = Precedence(expression.Operator.Kind);
        string left = WriteExpression(
            expression.Left,
            precedence,
            isRightChild: false,
            parentOperator: expression.Operator.Kind);
        string right = WriteExpression(
            expression.Right,
            precedence,
            isRightChild: true,
            parentOperator: expression.Operator.Kind);
        string text = left + " " + OperatorText(expression.Operator.Kind) + " " + right;

        return NeedsParentheses(
            expression.Operator.Kind,
            precedence,
            parentPrecedence,
            isRightChild,
            parentOperator)
            ? "(" + text + ")"
            : text;
    }

    private string WriteInterpolatedString(BoundInterpolatedStringExpression expression)
    {
        if (!expression.Parts.Any(part => part is BoundInterpolationExpressionPart))
        {
            string literalText = string.Concat(
                expression.Parts.OfType<BoundInterpolatedTextPart>().Select(part => part.Text));
            return TargetEscapes.PythonString(literalText);
        }

        var fStringText = new StringBuilder();
        bool emittedExpressionHole = false;

        foreach (BoundInterpolatedPart part in expression.Parts)
        {
            switch (part)
            {
                case BoundInterpolatedTextPart literal:
                    fStringText.Append(TargetEscapes.PythonFStringText(literal.Text));
                    break;

                case BoundInterpolationExpressionPart hole when ContainsStringLiteral(hole.Expression):
                    // Python 3.10 rejects backslashes and same-quote literals
                    // inside f-string expressions. Current SMILE holes are
                    // compile-time constants, so fold only that unsafe hole
                    // into f-string text while preserving all safe holes.
                    fStringText.Append(TargetEscapes.PythonFStringText(
                        GeneratorValueFacts.DisplayText(hole.Expression, _values)));
                    break;

                case BoundInterpolationExpressionPart hole:
                    fStringText.Append('{').Append(WriteDisplay(hole.Expression)).Append('}');
                    emittedExpressionHole = true;
                    break;
            }
        }

        if (!emittedExpressionHole)
        {
            return TargetEscapes.PythonString(
                GeneratorValueFacts.DisplayText(expression, _values));
        }

        return "f\"" + fStringText + "\"";
    }

    private static bool ContainsStringLiteral(BoundExpression expression) =>
        expression switch
        {
            BoundStringLiteralExpression => true,
            BoundUnaryExpression unary => ContainsStringLiteral(unary.Operand),
            BoundBinaryExpression binary =>
                ContainsStringLiteral(binary.Left) || ContainsStringLiteral(binary.Right),
            BoundInterpolatedStringExpression => true,
            _ => false
        };

    private static string OperatorText(BoundBinaryOperatorKind kind) =>
        kind switch
        {
            BoundBinaryOperatorKind.Addition or BoundBinaryOperatorKind.StringConcatenation => "+",
            BoundBinaryOperatorKind.Subtraction => "-",
            BoundBinaryOperatorKind.Multiplication => "*",
            BoundBinaryOperatorKind.Equality => "==",
            BoundBinaryOperatorKind.Inequality => "!=",
            BoundBinaryOperatorKind.Less => "<",
            BoundBinaryOperatorKind.LessOrEquals => "<=",
            BoundBinaryOperatorKind.Greater => ">",
            BoundBinaryOperatorKind.GreaterOrEquals => ">=",
            BoundBinaryOperatorKind.LogicalAnd => "and",
            BoundBinaryOperatorKind.LogicalOr => "or",
            _ => string.Empty
        };

    private static int Precedence(BoundBinaryOperatorKind kind) =>
        kind switch
        {
            BoundBinaryOperatorKind.Multiplication => MultiplicationPrecedence,
            BoundBinaryOperatorKind.Addition or
            BoundBinaryOperatorKind.Subtraction or
            BoundBinaryOperatorKind.StringConcatenation => AdditionPrecedence,
            BoundBinaryOperatorKind.Less or
            BoundBinaryOperatorKind.LessOrEquals or
            BoundBinaryOperatorKind.Greater or
            BoundBinaryOperatorKind.GreaterOrEquals or
            BoundBinaryOperatorKind.Equality or
            BoundBinaryOperatorKind.Inequality => ComparisonPrecedence,
            BoundBinaryOperatorKind.LogicalAnd => AndPrecedence,
            BoundBinaryOperatorKind.LogicalOr => OrPrecedence,
            _ => 0
        };

    private static bool NeedsParentheses(
        BoundBinaryOperatorKind currentOperator,
        int precedence,
        int parentPrecedence,
        bool isRightChild,
        BoundBinaryOperatorKind? parentOperator)
    {
        if (precedence < parentPrecedence)
        {
            return true;
        }

        // Python chains adjacent comparisons. Parenthesize either child so a
        // nested SMILE equality tree never becomes Python's chained syntax.
        if (parentOperator.HasValue &&
            IsComparison(currentOperator) &&
            IsComparison(parentOperator.Value))
        {
            return true;
        }

        return isRightChild &&
            precedence == parentPrecedence &&
            parentOperator is not (
                BoundBinaryOperatorKind.Addition or
                BoundBinaryOperatorKind.Multiplication or
                BoundBinaryOperatorKind.StringConcatenation or
                BoundBinaryOperatorKind.LogicalAnd or
                BoundBinaryOperatorKind.LogicalOr);
    }

    private static bool IsComparison(BoundBinaryOperatorKind kind) =>
        kind is BoundBinaryOperatorKind.Equality or
            BoundBinaryOperatorKind.Inequality or
            BoundBinaryOperatorKind.Less or
            BoundBinaryOperatorKind.LessOrEquals or
            BoundBinaryOperatorKind.Greater or
            BoundBinaryOperatorKind.GreaterOrEquals;
}

internal sealed record CPrintfPlan(
    string FormatText,
    IReadOnlyList<string> Arguments)
{
    public static CPrintfPlan FromPrint(
        BoundPrintStatement print,
        Func<BoundExpression, string> renderExpression,
        bool usesSigned64Integers)
    {
        var format = new StringBuilder();
        var arguments = new List<string>();

        // FormatText is raw printf format text, not C source text. Literal
        // percent signs are doubled here for printf safety; C string escaping
        // happens later exactly once when the call is emitted.
        if (!print.IsBlankLine)
        {
            AppendExpression(format, arguments, print.Value, renderExpression, usesSigned64Integers);
        }

        format.Append('\n');
        return new CPrintfPlan(format.ToString(), arguments);
    }

    private static void AppendExpression(
        StringBuilder format,
        List<string> arguments,
        BoundExpression expression,
        Func<BoundExpression, string> renderExpression,
        bool usesSigned64Integers)
    {
        if (expression.Type is not SmileType.String)
        {
            AppendTypedArgument(format, arguments, expression, renderExpression, usesSigned64Integers);
            return;
        }

        switch (expression)
        {
            case BoundStringLiteralExpression literal:
                AppendLiteralToFormat(format, literal.Value);
                break;

            case BoundVariableExpression:
                format.Append("%s");
                arguments.Add(renderExpression(expression));
                break;

            case BoundBinaryExpression { Operator.Kind: BoundBinaryOperatorKind.StringConcatenation } binary:
                AppendExpression(format, arguments, binary.Left, renderExpression, usesSigned64Integers);
                AppendExpression(format, arguments, binary.Right, renderExpression, usesSigned64Integers);
                break;

            case BoundInterpolatedStringExpression interpolated:
                foreach (BoundInterpolatedPart part in interpolated.Parts)
                {
                    switch (part)
                    {
                        case BoundInterpolatedTextPart text:
                            AppendLiteralToFormat(format, text.Text);
                            break;

                        case BoundInterpolationExpressionPart interpolation:
                            AppendExpression(format, arguments, interpolation.Expression, renderExpression, usesSigned64Integers);
                            break;
                    }
                }

                break;

            default:
                // Current String expressions are literals, variables,
                // concatenation, or interpolation. Keeping a defensive %s
                // fallback makes future String nodes fail visibly in target
                // compilation rather than silently dropping output.
                format.Append("%s");
                arguments.Add(renderExpression(expression));
                break;
        }
    }

    private static void AppendTypedArgument(
        StringBuilder format,
        List<string> arguments,
        BoundExpression expression,
        Func<BoundExpression, string> renderExpression,
        bool usesSigned64Integers)
    {
        string rendered = renderExpression(expression);
        switch (expression.Type)
        {
            case SmileType.Integer:
                if (usesSigned64Integers)
                {
                    // int64_t is not guaranteed to alias long long on every
                    // C implementation. The explicit value-preserving cast
                    // keeps the conventional %lld format portable.
                    format.Append("%lld");
                    arguments.Add("(long long)(" + rendered + ")");
                }
                else
                {
                    format.Append("%d");
                    arguments.Add(rendered);
                }

                break;

            case SmileType.Boolean:
                format.Append("%s");
                string condition = expression is BoundVariableExpression or BoundBooleanLiteralExpression
                    ? rendered
                    : "(" + rendered + ")";
                arguments.Add(condition + " ? \"TRUE\" : \"FALSE\"");
                break;

            case SmileType.String:
                format.Append("%s");
                arguments.Add(rendered);
                break;
        }
    }

    private static void AppendLiteralToFormat(StringBuilder format, string text)
    {
        foreach (char value in text)
        {
            // A user-authored '%' is data, never a printf directive. Doubling
            // it keeps every generated format string compiler-owned and safe.
            if (value == '%')
            {
                format.Append("%%");
            }
            else
            {
                format.Append(value);
            }
        }
    }
}

internal static class CGenerationFacts
{
    public static bool NeedsBooleanHeader(BoundProgram program) =>
        program.Variables.Any(variable => variable.Type is SmileType.Boolean) ||
        program.Statements.OfType<BoundPrintStatement>().Any(print =>
            !print.IsBlankLine && ContainsBooleanLiteral(print.Value));

    public static bool NeedsStringComparison(
        BoundProgram program,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values) =>
        program.Statements.Any(statement => statement switch
        {
            BoundLetStatement let when let.Variable.Type is not SmileType.String =>
                ContainsStringComparison(let.Initializer, values),
            BoundPrintStatement print when !print.IsBlankLine =>
                ContainsStringComparison(print.Value, values),
            _ => false
        });

    private static bool ContainsBooleanLiteral(BoundExpression expression) =>
        expression switch
        {
            BoundBooleanLiteralExpression => true,
            BoundUnaryExpression unary => ContainsBooleanLiteral(unary.Operand),
            BoundBinaryExpression binary => ContainsBooleanLiteral(binary.Left) ||
                ContainsBooleanLiteral(binary.Right),
            BoundInterpolatedStringExpression interpolated => interpolated.Parts.Any(part =>
                part is BoundInterpolationExpressionPart interpolation &&
                ContainsBooleanLiteral(interpolation.Expression)),
            _ => false
        };

    private static bool ContainsStringComparison(
        BoundExpression expression,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values) =>
        expression switch
        {
            BoundBinaryExpression binary =>
                NeedsStrcmp(binary, values) ||
                ContainsStringComparison(binary.Left, values) ||
                ContainsStringComparison(binary.Right, values),
            BoundUnaryExpression unary => ContainsStringComparison(unary.Operand, values),
            BoundInterpolatedStringExpression interpolated => interpolated.Parts.Any(part =>
                part is BoundInterpolationExpressionPart interpolation &&
                ContainsStringComparison(interpolation.Expression, values)),
            _ => false
        };

    private static bool NeedsStrcmp(
        BoundBinaryExpression expression,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values) =>
        expression.Left.Type is SmileType.String &&
        (expression.Operator.Kind is BoundBinaryOperatorKind.Equality or BoundBinaryOperatorKind.Inequality) &&
        !GeneratorValueFacts.TryGetNulContainingString(expression.Left, values, out _) &&
        !GeneratorValueFacts.TryGetNulContainingString(expression.Right, values, out _);
}

internal static class TargetTypes
{
    public static string CSharp(SmileType type, TargetIntegerProfile integers) =>
        type switch
        {
            SmileType.String => "string",
            SmileType.Integer => integers.RequiresSigned64Storage ? "long" : "int",
            SmileType.Boolean => "bool",
            _ => "object"
        };

    public static string Java(SmileType type, TargetIntegerProfile integers) =>
        type switch
        {
            SmileType.String => "String",
            SmileType.Integer => integers.RequiresSigned64Storage ? "long" : "int",
            SmileType.Boolean => "boolean",
            _ => "Object"
        };

    public static string Swift(SmileType type, TargetIntegerProfile integers) =>
        type switch
        {
            SmileType.String => "String",
            SmileType.Integer => integers.RequiresSigned64Storage ? "Int64" : "Int",
            SmileType.Boolean => "Bool",
            _ => "String"
        };

    public static string C(SmileType type, TargetIntegerProfile integers) =>
        type switch
        {
            SmileType.String => "const char *",
            SmileType.Integer => integers.RequiresSigned64Storage ? "int64_t" : "int",
            SmileType.Boolean => "bool",
            _ => "const char *"
        };

    public static string CDeclaration(
        SmileType type,
        string name,
        TargetIntegerProfile integers) =>
        type is SmileType.String
            ? C(type, integers) + name
            : C(type, integers) + " " + name;

    public static string Cpp(SmileType type, TargetIntegerProfile integers) =>
        type switch
        {
            SmileType.String => "std::string",
            SmileType.Integer => integers.RequiresSigned64Storage ? "std::int64_t" : "int",
            SmileType.Boolean => "bool",
            _ => "std::string"
        };
}

internal static class CSharpGenerationFacts
{
    public static bool NeedsInvariantCulture(BoundProgram program) =>
        program.Statements.Any(statement => statement switch
        {
            BoundLetStatement let => NeedsInvariantCulture(let.Initializer, displayContext: false),
            BoundPrintStatement print => !print.IsBlankLine && NeedsInvariantCulture(print.Value, displayContext: true),
            _ => false
        });

    private static bool NeedsInvariantCulture(BoundExpression expression, bool displayContext)
    {
        // C# only needs CultureInfo when a SMILE Integer is converted to text.
        // Its storage type is selected once from the complete bound program.
        if (displayContext && expression.Type is SmileType.Integer)
        {
            return true;
        }

        return expression switch
        {
            BoundUnaryExpression unary => NeedsInvariantCulture(unary.Operand, displayContext: false),
            BoundBinaryExpression binary => NeedsInvariantCulture(binary.Left, displayContext: false) ||
                NeedsInvariantCulture(binary.Right, displayContext: false),
            BoundInterpolatedStringExpression interpolated => interpolated.Parts.Any(part =>
                part is BoundInterpolationExpressionPart interpolation &&
                NeedsInvariantCulture(interpolation.Expression, displayContext: true)),
            _ => false
        };
    }
}

internal static class GeneratorValueFacts
{
    public static IReadOnlyDictionary<VariableSymbol, SmileValue> ConstantValues(BoundProgram program) =>
        program.Statements
            .OfType<BoundLetStatement>()
            .ToDictionary(let => let.Variable, let => let.ConstantValue);

    public static bool TryGetNulContainingString(
        BoundExpression expression,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values,
        out string value)
    {
        if (BoundConstantEvaluator.TryEvaluate(expression, values, out SmileValue evaluated) &&
            evaluated.Type is SmileType.String &&
            evaluated.StringValue.Contains('\0', StringComparison.Ordinal))
        {
            value = evaluated.StringValue;
            return true;
        }

        value = string.Empty;
        return false;
    }

    public static string DisplayText(
        BoundExpression expression,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values)
    {
        if (BoundConstantEvaluator.TryEvaluate(expression, values, out SmileValue value))
        {
            return value.ToDisplayText();
        }

        throw new InvalidOperationException("Bound expression could not be evaluated for target lowering.");
    }
}

internal static class TargetExpression
{
    public static string CSharp(
        BoundExpression expression,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers) =>
        new Writer(TargetLanguage.CSharp, identifiers, integers).Write(expression);

    public static string CSharpDisplay(
        BoundExpression expression,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers) =>
        new Writer(TargetLanguage.CSharp, identifiers, integers).WriteDisplay(expression);

    public static string JavaScript(
        BoundExpression expression,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers) =>
        new Writer(TargetLanguage.JavaScript, identifiers, integers).Write(expression);

    public static string JavaScriptDisplay(
        BoundExpression expression,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers) =>
        new Writer(TargetLanguage.JavaScript, identifiers, integers).WriteDisplay(expression);

    public static string Java(
        BoundExpression expression,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers) =>
        new Writer(TargetLanguage.Java, identifiers, integers).Write(expression);

    public static string JavaDisplay(
        BoundExpression expression,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers) =>
        new Writer(TargetLanguage.Java, identifiers, integers).WriteDisplay(expression);

    public static string Swift(
        BoundExpression expression,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers) =>
        new Writer(TargetLanguage.Swift, identifiers, integers).Write(expression);

    public static string SwiftDisplay(
        BoundExpression expression,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers) =>
        new Writer(TargetLanguage.Swift, identifiers, integers).WriteDisplay(expression);

    public static string C(
        BoundExpression expression,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values) =>
        new Writer(TargetLanguage.C, identifiers, integers, values).Write(expression);

    public static string ObjectiveC(
        BoundExpression expression,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values) =>
        new Writer(TargetLanguage.ObjectiveC, identifiers, integers, values).Write(expression);

    public static string CConstant(SmileValue value, TargetIntegerProfile integers) =>
        value.Type switch
        {
            SmileType.String => TargetEscapes.CString(value.StringValue),
            SmileType.Integer => CIntegerLiteral(value.IntegerValue, integers),
            SmileType.Boolean => value.BooleanValue ? "true" : "false",
            _ => TargetEscapes.CString(string.Empty)
        };

    private static string CIntegerLiteral(long value, TargetIntegerProfile integers)
    {
        if (!integers.RequiresSigned64Storage)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        if (value == long.MinValue)
        {
            return "INT64_MIN";
        }

        return value < 0
            ? "-INT64_C(" + (-value).ToString(CultureInfo.InvariantCulture) + ")"
            : "INT64_C(" + value.ToString(CultureInfo.InvariantCulture) + ")";
    }

    private sealed class Writer
    {
        private readonly TargetLanguage _language;
        private readonly TargetIdentifierMap _identifiers;
        private readonly TargetIntegerProfile _integers;
        private readonly IReadOnlyDictionary<VariableSymbol, SmileValue>? _values;

        public Writer(
            TargetLanguage language,
            TargetIdentifierMap identifiers,
            TargetIntegerProfile integers,
            IReadOnlyDictionary<VariableSymbol, SmileValue>? values = null)
        {
            _language = language;
            _identifiers = identifiers;
            _integers = integers;
            _values = values;
        }

        public string Write(BoundExpression expression) =>
            WriteExpression(expression, parentPrecedence: 0, isRightChild: false, parentOperator: null);

        public string WriteDisplay(BoundExpression expression) =>
            expression.Type switch
            {
                SmileType.String => Write(expression),
                SmileType.Integer => _language switch
                {
                    TargetLanguage.CSharp => $"{MaybeParenthesizeForCall(Write(expression))}.ToString(CultureInfo.InvariantCulture)",
                    TargetLanguage.JavaScript => $"({Write(expression)}).toString()",
                    TargetLanguage.Java => _integers.RequiresSigned64Storage
                        ? $"Long.toString({Write(expression)})"
                        : $"Integer.toString({Write(expression)})",
                    TargetLanguage.Swift => $"String({Write(expression)})",
                    _ => Write(expression)
                },
                SmileType.Boolean => _language switch
                {
                    TargetLanguage.CSharp => $"({Write(expression)} ? \"TRUE\" : \"FALSE\")",
                    TargetLanguage.JavaScript => $"({Write(expression)} ? \"TRUE\" : \"FALSE\")",
                    TargetLanguage.Java => $"({Write(expression)} ? \"TRUE\" : \"FALSE\")",
                    TargetLanguage.Swift => $"({Write(expression)} ? \"TRUE\" : \"FALSE\")",
                    _ => Write(expression)
                },
                _ => EmptyStringLiteral()
            };

        private string WriteExpression(
            BoundExpression expression,
            int parentPrecedence,
            bool isRightChild,
            BoundBinaryOperatorKind? parentOperator)
        {
            return expression switch
            {
                BoundStringLiteralExpression literal => StringLiteral(literal.Value),
                BoundIntegerLiteralExpression literal => IntegerLiteral(literal.Value),
                BoundBooleanLiteralExpression literal => BooleanLiteral(literal.Value),
                BoundVariableExpression variable => _identifiers.Get(variable.Variable),
                BoundUnaryExpression unary => WriteUnary(unary, parentPrecedence),
                BoundBinaryExpression binary => WriteBinary(binary, parentPrecedence, isRightChild, parentOperator),
                BoundInterpolatedStringExpression interpolated => WriteInterpolatedString(interpolated),
                _ => EmptyStringLiteral()
            };
        }

        private string WriteUnary(BoundUnaryExpression expression, int parentPrecedence)
        {
            int precedence = 7;
            string op = expression.Operator.Kind switch
            {
                // JavaScript BigInt deliberately has no unary-plus operator.
                // The SMILE identity operator is still preserved semantically
                // by emitting its already-typed operand unchanged.
                BoundUnaryOperatorKind.Identity when
                    _language is TargetLanguage.JavaScript &&
                    _integers.RequiresJavaScriptBigInt => string.Empty,
                BoundUnaryOperatorKind.Identity => "+",
                BoundUnaryOperatorKind.Negation => "-",
                BoundUnaryOperatorKind.LogicalNegation => _language is TargetLanguage.Swift ? "!" : "!",
                _ => string.Empty
            };

            string operand = WriteExpression(expression.Operand, precedence, isRightChild: true, parentOperator: null);
            string text = op + operand;
            return precedence < parentPrecedence ? "(" + text + ")" : text;
        }

        private string WriteBinary(
            BoundBinaryExpression expression,
            int parentPrecedence,
            bool isRightChild,
            BoundBinaryOperatorKind? parentOperator)
        {
            if (_language is TargetLanguage.JavaScript &&
                !_integers.RequiresJavaScriptBigInt &&
                expression.Operator.Kind is BoundBinaryOperatorKind.Division)
            {
                // Number division is floating point. Math.trunc restores
                // SMILE's signed Integer quotient semantics while leaving
                // ordinary safe-Integer programs on idiomatic Number values.
                const int divisionPrecedence = 6;
                string call =
                    "Math.trunc(" +
                    WriteExpression(
                        expression.Left,
                        divisionPrecedence,
                        isRightChild: false,
                        parentOperator: BoundBinaryOperatorKind.Division) +
                    " / " +
                    WriteExpression(
                        expression.Right,
                        divisionPrecedence,
                        isRightChild: true,
                        parentOperator: BoundBinaryOperatorKind.Division) +
                    ")";
                return parentPrecedence > 7 ? "(" + call + ")" : call;
            }

            if (_language is TargetLanguage.Java &&
                expression.Left.Type is SmileType.String &&
                expression.Operator.Kind is BoundBinaryOperatorKind.Equality or BoundBinaryOperatorKind.Inequality)
            {
                return WriteJavaStringEquality(expression, parentPrecedence, isRightChild, parentOperator);
            }

            if (_language is TargetLanguage.C or TargetLanguage.ObjectiveC &&
                expression.Left.Type is SmileType.String &&
                expression.Operator.Kind is BoundBinaryOperatorKind.Equality or BoundBinaryOperatorKind.Inequality)
            {
                return WriteCStringEquality(expression, parentPrecedence, isRightChild, parentOperator);
            }

            int precedence = Precedence(expression.Operator.Kind);
            string left = WriteExpression(expression.Left, precedence, isRightChild: false, expression.Operator.Kind);
            string right = WriteExpression(expression.Right, precedence, isRightChild: true, expression.Operator.Kind);
            string text = left + " " + OperatorText(expression.Operator.Kind) + " " + right;

            if (NeedsParentheses(precedence, parentPrecedence, isRightChild, parentOperator))
            {
                return "(" + text + ")";
            }

            return text;
        }

        private string WriteJavaStringEquality(
            BoundBinaryExpression expression,
            int parentPrecedence,
            bool isRightChild,
            BoundBinaryOperatorKind? parentOperator)
        {
            int precedence = expression.Operator.Kind is BoundBinaryOperatorKind.Inequality
                ? 7
                : Precedence(expression.Operator.Kind);
            string receiver = IsSimpleReceiver(expression.Left)
                ? WriteExpression(expression.Left, 8, isRightChild: false, parentOperator: null)
                : "(" + WriteExpression(expression.Left, 0, isRightChild: false, parentOperator: null) + ")";
            string text = receiver + ".equals(" + WriteExpression(expression.Right, 0, isRightChild: false, parentOperator: null) + ")";
            if (expression.Operator.Kind is BoundBinaryOperatorKind.Inequality)
            {
                text = "!" + text;
            }

            return NeedsParentheses(precedence, parentPrecedence, isRightChild, parentOperator)
                ? "(" + text + ")"
                : text;
        }

        private string WriteCStringEquality(
            BoundBinaryExpression expression,
            int parentPrecedence,
            bool isRightChild,
            BoundBinaryOperatorKind? parentOperator)
        {
            if (_values is not null &&
                (GeneratorValueFacts.TryGetNulContainingString(expression.Left, _values, out _) ||
                 GeneratorValueFacts.TryGetNulContainingString(expression.Right, _values, out _)) &&
                BoundConstantEvaluator.TryEvaluate(expression, _values, out SmileValue evaluated) &&
                evaluated.Type is SmileType.Boolean)
            {
                // strcmp stops at the first NUL and would treat values such as
                // "A\0B" and "A\0C" as equal. Current SMILE expressions are
                // pure constants, so lowering only this NUL-sensitive case to
                // its exact evaluated result is the smallest correct C form.
                return BooleanLiteral(evaluated.BooleanValue);
            }

            int precedence = Precedence(expression.Operator.Kind);
            string comparison = expression.Operator.Kind is BoundBinaryOperatorKind.Equality
                ? " == 0"
                : " != 0";
            string text =
                "strcmp(" +
                WriteCStringEqualityOperand(expression.Left) +
                ", " +
                WriteCStringEqualityOperand(expression.Right) +
                ")" +
                comparison;
            return NeedsParentheses(precedence, parentPrecedence, isRightChild, parentOperator)
                ? "(" + text + ")"
                : text;
        }

        private string WriteCStringEqualityOperand(BoundExpression expression)
        {
            if (expression is BoundStringLiteralExpression or BoundVariableExpression)
            {
                return WriteExpression(expression, 0, isRightChild: false, parentOperator: null);
            }

            if (_values is not null &&
                BoundConstantEvaluator.TryEvaluate(expression, _values, out SmileValue value) &&
                value.Type is SmileType.String)
            {
                // C has no native String concatenation or interpolation value.
                // Lower only complex strcmp operands; simple names and literals
                // remain readable target expressions.
                return TargetEscapes.CString(value.StringValue);
            }

            return WriteExpression(expression, 0, isRightChild: false, parentOperator: null);
        }

        private string WriteInterpolatedString(BoundInterpolatedStringExpression expression) =>
            _language switch
            {
                TargetLanguage.CSharp => "$\"" + string.Concat(expression.Parts.Select(part => part switch
                {
                    BoundInterpolatedTextPart text => TargetEscapes.CSharpInterpolatedText(text.Text),
                    BoundInterpolationExpressionPart interpolation => "{" + WriteDisplay(interpolation.Expression) + "}",
                    _ => string.Empty
                })) + "\"",

                TargetLanguage.JavaScript => "`" + string.Concat(expression.Parts.Select(part => part switch
                {
                    BoundInterpolatedTextPart text => TargetEscapes.JavaScriptTemplateText(text.Text),
                    BoundInterpolationExpressionPart interpolation => "${" + WriteDisplay(interpolation.Expression) + "}",
                    _ => string.Empty
                })) + "`",

                TargetLanguage.Java => JoinJavaDisplaySegments(expression.Parts),

                TargetLanguage.Swift => "\"" + string.Concat(expression.Parts.Select(part => part switch
                {
                    BoundInterpolatedTextPart text => TargetEscapes.SwiftInterpolatedText(text.Text),
                    BoundInterpolationExpressionPart interpolation => "\\(" + WriteDisplay(interpolation.Expression) + ")",
                    _ => string.Empty
                })) + "\"",

                _ => EmptyStringLiteral()
            };

        private string JoinJavaDisplaySegments(IReadOnlyList<BoundInterpolatedPart> parts)
        {
            string[] segments = parts
                .Select(part => part switch
                {
                    BoundInterpolatedTextPart text => TargetEscapes.JavaString(text.Text),
                    BoundInterpolationExpressionPart interpolation => WriteDisplay(interpolation.Expression),
                    _ => TargetEscapes.JavaString(string.Empty)
                })
                .Where(segment => segment != TargetEscapes.JavaString(string.Empty))
                .ToArray();
            return segments.Length == 0
                ? TargetEscapes.JavaString(string.Empty)
                : string.Join(" + ", segments);
        }

        private string StringLiteral(string value) =>
            _language switch
            {
                TargetLanguage.CSharp => TargetEscapes.CSharpString(value),
                TargetLanguage.JavaScript => TargetEscapes.JavaScriptString(value),
                TargetLanguage.Java => TargetEscapes.JavaString(value),
                TargetLanguage.Swift => TargetEscapes.SwiftString(value),
                _ => TargetEscapes.CString(value)
            };

        private string EmptyStringLiteral() => StringLiteral(string.Empty);

        private string IntegerLiteral(long value) =>
            _language switch
            {
                TargetLanguage.CSharp when _integers.RequiresSigned64Storage =>
                    value == long.MinValue
                        ? "long.MinValue"
                        : value.ToString(CultureInfo.InvariantCulture) + "L",
                TargetLanguage.JavaScript when _integers.RequiresJavaScriptBigInt =>
                    value == long.MinValue
                        ? "(-9223372036854775808n)"
                        : value.ToString(CultureInfo.InvariantCulture) + "n",
                TargetLanguage.Java when _integers.RequiresSigned64Storage =>
                    value == long.MinValue
                        ? "Long.MIN_VALUE"
                        : value.ToString(CultureInfo.InvariantCulture) + "L",
                TargetLanguage.Swift when _integers.RequiresSigned64Storage && value == long.MinValue =>
                    "Int64.min",
                TargetLanguage.C or TargetLanguage.ObjectiveC => CIntegerLiteral(value, _integers),
                _ => value.ToString(CultureInfo.InvariantCulture)
            };

        private string BooleanLiteral(bool value) =>
            _language is TargetLanguage.Swift
                ? value ? "true" : "false"
                : value ? "true" : "false";

        private string OperatorText(BoundBinaryOperatorKind kind) =>
            _language switch
            {
                TargetLanguage.JavaScript => kind switch
                {
                    BoundBinaryOperatorKind.Equality => "===",
                    BoundBinaryOperatorKind.Inequality => "!==",
                    BoundBinaryOperatorKind.LogicalAnd => "&&",
                    BoundBinaryOperatorKind.LogicalOr => "||",
                    _ => CommonOperatorText(kind)
                },
                TargetLanguage.Swift => kind switch
                {
                    BoundBinaryOperatorKind.LogicalAnd => "&&",
                    BoundBinaryOperatorKind.LogicalOr => "||",
                    _ => CommonOperatorText(kind)
                },
                _ => kind switch
                {
                    BoundBinaryOperatorKind.LogicalAnd => "&&",
                    BoundBinaryOperatorKind.LogicalOr => "||",
                    _ => CommonOperatorText(kind)
                }
            };

        private static string CommonOperatorText(BoundBinaryOperatorKind kind) =>
            kind switch
            {
                BoundBinaryOperatorKind.Addition or BoundBinaryOperatorKind.StringConcatenation => "+",
                BoundBinaryOperatorKind.Subtraction => "-",
                BoundBinaryOperatorKind.Multiplication => "*",
                BoundBinaryOperatorKind.Division => "/",
                BoundBinaryOperatorKind.Equality => "==",
                BoundBinaryOperatorKind.Inequality => "!=",
                BoundBinaryOperatorKind.Less => "<",
                BoundBinaryOperatorKind.LessOrEquals => "<=",
                BoundBinaryOperatorKind.Greater => ">",
                BoundBinaryOperatorKind.GreaterOrEquals => ">=",
                _ => string.Empty
            };

        private static int Precedence(BoundBinaryOperatorKind kind) =>
            kind switch
            {
                BoundBinaryOperatorKind.Multiplication or BoundBinaryOperatorKind.Division => 6,
                BoundBinaryOperatorKind.Addition or
                BoundBinaryOperatorKind.Subtraction or
                BoundBinaryOperatorKind.StringConcatenation => 5,
                BoundBinaryOperatorKind.Less or
                BoundBinaryOperatorKind.LessOrEquals or
                BoundBinaryOperatorKind.Greater or
                BoundBinaryOperatorKind.GreaterOrEquals => 4,
                BoundBinaryOperatorKind.Equality or BoundBinaryOperatorKind.Inequality => 3,
                BoundBinaryOperatorKind.LogicalAnd => 2,
                BoundBinaryOperatorKind.LogicalOr => 1,
                _ => 0
            };

        private static bool NeedsParentheses(
            int precedence,
            int parentPrecedence,
            bool isRightChild,
            BoundBinaryOperatorKind? parentOperator)
        {
            if (precedence < parentPrecedence)
            {
                return true;
            }

            return isRightChild &&
                precedence == parentPrecedence &&
                parentOperator is not (
                    BoundBinaryOperatorKind.Addition or
                    BoundBinaryOperatorKind.Multiplication or
                    BoundBinaryOperatorKind.StringConcatenation or
                    BoundBinaryOperatorKind.LogicalAnd or
                    BoundBinaryOperatorKind.LogicalOr);
        }

        private static bool IsSimpleReceiver(BoundExpression expression) =>
            expression is BoundStringLiteralExpression or BoundVariableExpression;

        private static string MaybeParenthesizeForCall(string expression) =>
            IsSimpleCSharpCallReceiver(expression)
                ? expression
                : "(" + expression + ")";

        private static bool IsSimpleCSharpCallReceiver(string expression)
        {
            if (string.IsNullOrEmpty(expression) ||
                !SyntaxFacts.IsIdentifierStart(expression[0]))
            {
                return false;
            }

            // Identifiers and dotted constants such as long.MinValue can receive
            // a method call directly. Operators, negative literals, and grouped
            // expressions are parenthesized before .ToString(...) is appended.
            return expression.All(character =>
                SyntaxFacts.IsIdentifierPart(character) ||
                character == '.');
        }
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

    public static string PythonString(string text) => Quote(EscapePython(text, escapeBraces: false));

    public static string CSharpInterpolatedText(string text) => EscapeCSharpInterpolatedText(text);

    public static string JavaScriptTemplateText(string text) => EscapeJavaScriptTemplateText(text);

    public static string SwiftInterpolatedText(string text) => EscapeSwift(text);

    public static string PythonFStringText(string text) => EscapePython(text, escapeBraces: true);

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

    private static string EscapePython(string text, bool escapeBraces)
    {
        var builder = new StringBuilder();

        foreach (char value in text)
        {
            builder.Append(value switch
            {
                '{' when escapeBraces => "{{",
                '}' when escapeBraces => "}}",
                '\\' => "\\\\",
                '"' => "\\\"",
                '\0' => "\\x00",
                '\b' => "\\x08",
                '\f' => "\\x0c",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
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
