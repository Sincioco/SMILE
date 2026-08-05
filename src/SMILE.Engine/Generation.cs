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
                    BoundExpression initializer = SimplifyExpression(let.Initializer, values);
                    statements.Add(let with { Initializer = initializer });
                    values.Add(let.Variable, EvaluateKnownValue(initializer, values));
                    break;

                case BoundSetStatement set:
                    // SET sees the old value throughout its complete right side.
                    // Only after simplification and evaluation succeeds does the
                    // new value become visible to later statements.
                    BoundExpression value = SimplifyExpression(set.Value, values);
                    statements.Add(set with { Value = value });
                    values[set.Variable] = EvaluateKnownValue(value, values);
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

    private static SmileValue EvaluateKnownValue(
        BoundExpression expression,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values)
    {
        if (BoundExpressionEvaluator.TryEvaluate(expression, values, out SmileValue value))
        {
            return value;
        }

        throw new InvalidOperationException(
            "A successfully bound SMILE expression could not be evaluated during simplification.");
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

            if (BoundExpressionEvaluator.TryEvaluate(left, values, out SmileValue leftValue) &&
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

    public static TargetIntegerProfile Analyze(
        BoundProgram program,
        BoundProgramExecutionTrace trace)
    {
        bool requiresSigned64 = false;
        bool requiresBigInt = false;

        void Observe(long value)
        {
            requiresSigned64 |= value is < int.MinValue or > int.MaxValue;
            requiresBigInt |= value is < -JavaScriptMaxSafeInteger or > JavaScriptMaxSafeInteger;
        }

        void Visit(
            BoundExpression expression,
            IReadOnlyDictionary<VariableSymbol, SmileValue> values)
        {
            // Evaluating every Integer-typed node records literal values,
            // variable operands, and arithmetic intermediates. A failed
            // evaluation can only be an intentionally unreachable expression
            // in a successfully bound program; its children are still visited.
            if (expression.Type is SmileType.Integer &&
                BoundExpressionEvaluator.TryEvaluate(expression, values, out SmileValue value))
            {
                Observe(value.IntegerValue);
            }

            switch (expression)
            {
                case BoundUnaryExpression unary:
                    Visit(unary.Operand, values);
                    break;

                case BoundBinaryExpression binary:
                    Visit(binary.Left, values);
                    Visit(binary.Right, values);
                    break;

                case BoundInterpolatedStringExpression interpolated:
                    foreach (BoundInterpolationExpressionPart hole in
                        interpolated.Parts.OfType<BoundInterpolationExpressionPart>())
                    {
                        Visit(hole.Expression, values);
                    }

                    break;
            }
        }

        for (int index = 0; index < program.Statements.Count; index++)
        {
            BoundStatementExecution step = trace.Steps[index];
            switch (step.Statement)
            {
                case BoundLetStatement let:
                    Visit(let.Initializer, step.ValuesBefore);
                    break;

                case BoundSetStatement set:
                    Visit(set.Value, step.ValuesBefore);
                    break;

                case BoundPrintStatement print when !print.IsBlankLine:
                    Visit(print.Value, step.ValuesBefore);
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
        BoundProgramExecutionTrace trace = BoundProgramExecutionTrace.Create(program);
        TargetIntegerProfile integers = TargetIntegerProfile.Analyze(program, trace);
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

                case BoundSetStatement set:
                    source.AppendLine($"        {identifiers.Get(set.Variable)} = {TargetExpression.CSharp(set.Value, identifiers, integers)};");
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
        BoundProgramExecutionTrace trace = BoundProgramExecutionTrace.Create(program);
        TargetIntegerProfile integers = TargetIntegerProfile.Analyze(program, trace);
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths =
            CreateExactStringLengthNames(program, identifiers, trace);
        var source = new StringBuilder();
        source.AppendLine("#include <stdio.h>");
        if (integers.RequiresSigned64Storage)
        {
            source.AppendLine("#include <stdint.h>");
        }

        if (CGenerationFacts.NeedsBooleanHeader(program, trace, exactStringLengths))
        {
            source.AppendLine("#include <stdbool.h>");
        }

        if (CGenerationFacts.NeedsStringComparison(program, trace, exactStringLengths))
        {
            source.AppendLine("#include <string.h>");
        }

        source.AppendLine();
        source.AppendLine("int main(void)");
        source.AppendLine("{");

        bool emittedDeclaration = false;
        bool emittedExecutable = false;
        bool emittedBodyStatement = false;

        for (int index = 0; index < trace.Steps.Count; index++)
        {
            BoundStatementExecution step = trace.Steps[index];
            switch (step.Statement)
            {
                case BoundLetStatement let:
                    SmileValue letValue = GeneratorValueFacts.Evaluate(let.Initializer, step.ValuesBefore);
                    string initializer = let.Variable.Type is SmileType.String
                        ? TargetExpression.CConstant(letValue, integers)
                        : TargetExpression.C(
                            let.Initializer,
                            identifiers,
                            integers,
                            step.ValuesBefore,
                            exactStringLengths);
                    source.AppendLine($"    {TargetTypes.CDeclaration(let.Variable.Type, identifiers.Get(let.Variable), integers)} = {initializer};");
                    if (exactStringLengths.TryGetValue(let.Variable, out string? letLengthName))
                    {
                        source.AppendLine($"    size_t {letLengthName} = {Utf8ByteLength(letValue)};");
                    }

                    emittedDeclaration = true;
                    emittedBodyStatement = true;
                    break;

                case BoundSetStatement set:
                    if (!emittedExecutable && emittedDeclaration)
                    {
                        source.AppendLine();
                    }

                    SmileValue setValue = GeneratorValueFacts.Evaluate(set.Value, step.ValuesBefore);
                    string value = set.Variable.Type is SmileType.String
                        ? TargetExpression.CConstant(setValue, integers)
                        : TargetExpression.C(
                            set.Value,
                            identifiers,
                            integers,
                            step.ValuesBefore,
                            exactStringLengths);
                    source.AppendLine($"    {identifiers.Get(set.Variable)} = {value};");
                    if (exactStringLengths.TryGetValue(set.Variable, out string? setLengthName))
                    {
                        source.AppendLine($"    {setLengthName} = {Utf8ByteLength(setValue)};");
                    }

                    emittedExecutable = true;
                    emittedBodyStatement = true;
                    break;

                case BoundPrintStatement print:
                    if (!emittedExecutable && emittedDeclaration)
                    {
                        source.AppendLine();
                    }

                    AppendCPrint(
                        source,
                        print,
                        identifiers,
                        integers,
                        step.ValuesBefore,
                        exactStringLengths);
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
        IReadOnlyDictionary<VariableSymbol, SmileValue> values,
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths)
    {
        if (TryAppendDirectStringVariablePrint(
            source,
            "    ",
            print,
            identifiers,
            exactStringLengths))
        {
            return;
        }

        if (TryAppendExactNulStringPrint(source, "    ", print, values))
        {
            return;
        }

        CPrintfPlan plan = CPrintfPlan.FromPrint(
            print,
            expression => TargetExpression.C(
                expression,
                identifiers,
                integers,
                values,
                exactStringLengths),
            integers.RequiresSigned64Storage);
        AppendPrintfCall(source, "    ", plan);
    }

    internal static bool TryAppendDirectStringVariablePrint(
        StringBuilder source,
        string indent,
        BoundPrintStatement print,
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths)
    {
        if (print.IsBlankLine ||
            print.Value is not BoundVariableExpression variable ||
            variable.Variable.Type is not SmileType.String)
        {
            return false;
        }

        string name = identifiers.Get(variable.Variable);
        if (exactStringLengths.TryGetValue(variable.Variable, out string? lengthName))
        {
            // Exact mutable Strings are pointer-plus-length values in C. Read
            // both pieces of current target storage instead of re-emitting the
            // statement's statically known bytes as an unrelated print literal.
            source.Append(indent).Append("fwrite(").Append(name).Append(", 1, ")
                .Append(lengthName).AppendLine(", stdout);");
            source.Append(indent).AppendLine("fputc('\\n', stdout);");
        }
        else
        {
            source.Append(indent).Append("printf(\"%s\\n\", ").Append(name).AppendLine(");");
        }

        return true;
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

    internal static IReadOnlyDictionary<VariableSymbol, string> CreateExactStringLengthNames(
        BoundProgram program,
        TargetIdentifierMap identifiers,
        BoundProgramExecutionTrace trace)
    {
        var names = new Dictionary<VariableSymbol, string>();
        var used = program.Variables
            .Select(identifiers.Get)
            .ToHashSet(StringComparer.Ordinal);

        for (int index = 0; index < program.Variables.Count; index++)
        {
            VariableSymbol variable = program.Variables[index];
            if (variable.Type is not SmileType.String ||
                !GeneratorValueFacts.AssignedValuesContainNul(trace, variable))
            {
                continue;
            }

            string preferred = $"smileString{index}Length";
            string name = preferred;
            int suffix = 2;
            while (!used.Add(name))
            {
                name = preferred + "_" + suffix.ToString(CultureInfo.InvariantCulture);
                suffix++;
            }

            names.Add(variable, name);
        }

        return names;
    }

    internal static int Utf8ByteLength(SmileValue value) =>
        Encoding.UTF8.GetByteCount(value.StringValue);

}

internal sealed class MasmX64CodeGenerator : ICodeGenerator
{
    public TargetLanguage Language => TargetLanguage.MasmX64;

    public GeneratedProgram Generate(BoundProgram program)
    {
        BoundProgramExecutionTrace trace = BoundProgramExecutionTrace.Create(program);
        BoundLetStatement[] lets = program.Statements.OfType<BoundLetStatement>().ToArray();
        BoundPrintStatement[] prints = program.Statements.OfType<BoundPrintStatement>().ToArray();
        IReadOnlyDictionary<VariableSymbol, int> variableIndexes = lets
            .Select((let, index) => (let.Variable, index))
            .ToDictionary(item => item.Variable, item => item.index);
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

        AppendMasmData(source, trace, variableIndexes, prints.Length);
        AppendMasmCode(source, trace, variableIndexes, prints.Length);

        return new GeneratedProgram(
            Language,
            new[] { new GeneratedFile("Program.asm", TextOutput.EnsureOneTrailingNewLine(source.ToString()), IsPrimary: true) });
    }

    private static void AppendMasmData(
        StringBuilder source,
        BoundProgramExecutionTrace trace,
        IReadOnlyDictionary<VariableSymbol, int> variableIndexes,
        int printCount)
    {
        if (variableIndexes.Count == 0 && printCount == 0)
        {
            return;
        }

        AppendMasmLine(source, ".data", "Static bytes and variables live here.");

        if (printCount > 0)
        {
            AppendMasmLine(source, "STD_OUTPUT_HANDLE EQU -11", "Magic value for the console output handle.");
        }

        int printIndex = 0;
        for (int statementIndex = 0; statementIndex < trace.Steps.Count; statementIndex++)
        {
            BoundStatementExecution step = trace.Steps[statementIndex];
            switch (step.Statement)
            {
                case BoundLetStatement let:
                    int variableIndex = variableIndexes[let.Variable];
                    string valueLabel = VariableValueLabel(variableIndex);
                    string initialText = GeneratorValueFacts
                        .Evaluate(let.Initializer, step.ValuesBefore)
                        .ToDisplayText();
                    AppendMasmStringData(
                        source,
                        valueLabel,
                        initialText,
                        $"LET {let.Variable.Name} initial text.",
                        "Length of the variable's current text.");
                    AppendMasmLine(source, $"{VariablePointerLabel(variableIndex)} QWORD ?", $"Runtime pointer for {let.Variable.Name}.");
                    AppendMasmLine(source, $"{VariableLengthLabel(variableIndex)} DWORD ?", $"Runtime length for {let.Variable.Name}.");
                    break;

                case BoundSetStatement set:
                    string setText = GeneratorValueFacts
                        .Evaluate(set.Value, step.ValuesBefore)
                        .ToDisplayText();
                    AppendMasmStringData(
                        source,
                        SetValueLabel(statementIndex),
                        setText,
                        $"SET {set.Variable.Name} assigned text.",
                        "Length of this assigned value.");
                    break;

                case BoundPrintStatement print:
                    if (print.Value is not BoundVariableExpression || print.IsBlankLine)
                    {
                        string text = print.IsBlankLine
                            ? string.Empty
                            : GeneratorValueFacts.DisplayText(print.Value, step.ValuesBefore);
                        string label = PrintLiteralLabel(printIndex, 0);
                        AppendMasmStringData(
                            source,
                            label,
                            text,
                            $"PRINT #{printIndex + 1} canonical text.",
                            "Length of this print text.");
                    }

                    printIndex++;
                    break;
            }
        }

        if (printCount > 0)
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
        BoundProgramExecutionTrace trace,
        IReadOnlyDictionary<VariableSymbol, int> variableIndexes,
        int printCount)
    {
        AppendMasmLine(source, ".code", "CPU instructions live here.");
        AppendMasmLine(source, "main PROC", "Program entry point.");
        AppendMasmLine(source, "    sub rsp, 28h", "Reserve Win64 shadow space and align the stack.");

        if (printCount > 0)
        {
            source.AppendLine();
            AppendMasmLine(source, "    mov ecx, STD_OUTPUT_HANDLE", "Ask Windows for stdout.");
            AppendMasmLine(source, "    call GetStdHandle", "RAX receives the stdout handle.");
            AppendMasmLine(source, "    mov QWORD PTR [stdoutHandle], rax", "Cache stdout for every PRINT segment.");
        }

        int printIndex = 0;
        for (int statementIndex = 0; statementIndex < trace.Steps.Count; statementIndex++)
        {
            BoundStatementExecution step = trace.Steps[statementIndex];
            switch (step.Statement)
            {
                case BoundLetStatement let:
                    AppendMasmStorageUpdate(
                        source,
                        variableIndexes[let.Variable],
                        VariableValueLabel(variableIndexes[let.Variable]),
                        $"Address of LET {let.Variable.Name} text.");
                    break;

                case BoundSetStatement set:
                    AppendMasmStorageUpdate(
                        source,
                        variableIndexes[set.Variable],
                        SetValueLabel(statementIndex),
                        $"Address of SET {set.Variable.Name} text.");
                    break;

                case BoundPrintStatement print:
                    source.AppendLine();
                    AppendMasmLine(source, $"; PRINT #{printIndex + 1}", "Write each expression segment, then newline.");
                    if (!print.IsBlankLine && print.Value is BoundVariableExpression variable)
                    {
                        AppendMasmWriteVariable(
                            source,
                            variable.Variable.Name,
                            variableIndexes[variable.Variable]);
                    }
                    else
                    {
                        AppendMasmWriteLiteral(source, PrintLiteralLabel(printIndex, 0));
                    }

                    AppendMasmWriteLiteral(source, "newline");
                    printIndex++;
                    break;
            }
        }

        source.AppendLine();
        AppendMasmLine(source, "    xor ecx, ecx", "ExitProcess arg 1: process exit code 0.");
        AppendMasmLine(source, "    call ExitProcess", "End the program.");
        AppendMasmLine(source, "main ENDP", "End of the main procedure.");
        source.AppendLine();
        source.AppendLine("END");
    }

    private static void AppendMasmStorageUpdate(
        StringBuilder source,
        int variableIndex,
        string valueLabel,
        string addressComment)
    {
        source.AppendLine();
        AppendMasmLine(source, $"    lea rax, {valueLabel}", addressComment);
        AppendMasmLine(source, $"    mov QWORD PTR [{VariablePointerLabel(variableIndex)}], rax", "Store the runtime string pointer.");
        AppendMasmLine(source, $"    mov DWORD PTR [{VariableLengthLabel(variableIndex)}], {valueLabel}Length", "Store the runtime string length.");
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

    private static string SetValueLabel(int statementIndex) => $"set{statementIndex}Value";

    private static string PrintLiteralLabel(int printIndex, int segmentIndex) =>
        $"print{printIndex}Segment{segmentIndex}";

}

internal sealed class JavaScriptCodeGenerator : ICodeGenerator
{
    public TargetLanguage Language => TargetLanguage.JavaScript;

    public GeneratedProgram Generate(BoundProgram program)
    {
        TargetIdentifierMap identifiers = TargetIdentifierMap.Create(program, Language);
        BoundProgramExecutionTrace trace = BoundProgramExecutionTrace.Create(program);
        TargetIntegerProfile integers = TargetIntegerProfile.Analyze(program, trace);
        var source = new StringBuilder();

        foreach (BoundStatement statement in program.Statements)
        {
            switch (statement)
            {
                case BoundLetStatement let:
                    source.AppendLine($"let {identifiers.Get(let.Variable)} = {TargetExpression.JavaScript(let.Initializer, identifiers, integers)};");
                    break;

                case BoundSetStatement set:
                    source.AppendLine($"{identifiers.Get(set.Variable)} = {TargetExpression.JavaScript(set.Value, identifiers, integers)};");
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
        BoundProgramExecutionTrace trace = BoundProgramExecutionTrace.Create(program);
        TargetIntegerProfile integers = TargetIntegerProfile.Analyze(program, trace);
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

                case BoundSetStatement set:
                    source.AppendLine($"        {identifiers.Get(set.Variable)} = {TargetExpression.Java(set.Value, identifiers, integers)};");
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
        BoundProgramExecutionTrace trace = BoundProgramExecutionTrace.Create(program);
        var source = new StringBuilder();
        BoundLetStatement[] lets = program.Statements.OfType<BoundLetStatement>().ToArray();
        IReadOnlyDictionary<VariableSymbol, string> logicalLengths =
            CreateLogicalLengthNames(program, identifiers, trace);

        source.AppendLine(">>SOURCE FORMAT IS FREE");
        source.AppendLine("IDENTIFICATION DIVISION.");
        source.AppendLine("PROGRAM-ID. Program.");

        if (lets.Length > 0)
        {
            source.AppendLine();
            source.AppendLine("DATA DIVISION.");
            source.AppendLine("WORKING-STORAGE SECTION.");
            source.AppendLine("*> SMILE LET values are stored before PROCEDURE DIVISION.");

            foreach (BoundStatementExecution step in trace.Steps)
            {
                if (step.Statement is BoundLetStatement let)
                {
                    AppendCobolLet(source, let, step.ValuesBefore, identifiers, trace, logicalLengths);
                }
            }
        }

        source.AppendLine();
        source.AppendLine("PROCEDURE DIVISION.");
        source.AppendLine("*> SMILE PRINT reads current storage when it directly names a variable.");

        foreach (BoundStatementExecution step in trace.Steps)
        {
            switch (step.Statement)
            {
                case BoundSetStatement set:
                    AppendCobolSet(source, set, step.ValuesBefore, identifiers, logicalLengths);
                    break;

                case BoundPrintStatement print:
                    AppendCobolPrint(
                        source,
                        print,
                        step.ValuesBefore,
                        identifiers,
                        logicalLengths);
                    break;
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
        IReadOnlyDictionary<VariableSymbol, SmileValue> valuesBefore,
        TargetIdentifierMap identifiers,
        BoundProgramExecutionTrace trace,
        IReadOnlyDictionary<VariableSymbol, string> logicalLengths)
    {
        string name = identifiers.Get(let.Variable);
        SmileValue initialValue = GeneratorValueFacts.Evaluate(let.Initializer, valuesBefore);
        string text = initialValue.ToDisplayText();
        int storageLength = Math.Max(1, GeneratorValueFacts.MaximumAssignedUtf8ByteLength(trace, let.Variable));
        string picture = storageLength == 1 ? "PIC X" : $"PIC X({storageLength})";
        string storageValue = text.Length == 0
            ? storageLength == 1 ? "SPACE" : "SPACES"
            : TargetEscapes.CobolString(text);
        source.AppendLine($"01 {name} {picture} VALUE {storageValue}.");

        if (logicalLengths.TryGetValue(let.Variable, out string? lengthName))
        {
            source.AppendLine(
                $"01 {lengthName} PIC 9(9) COMP-5 VALUE {TargetEscapes.CobolByteLength(text)}.");
        }
    }

    private static void AppendCobolSet(
        StringBuilder source,
        BoundSetStatement set,
        IReadOnlyDictionary<VariableSymbol, SmileValue> valuesBefore,
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<VariableSymbol, string> logicalLengths)
    {
        string text = GeneratorValueFacts.Evaluate(set.Value, valuesBefore).ToDisplayText();
        string storageValue = text.Length == 0 ? "SPACES" : TargetEscapes.CobolString(text);
        source.AppendLine($"    MOVE {storageValue} TO {identifiers.Get(set.Variable)}.");
        source.AppendLine(
            $"    MOVE {TargetEscapes.CobolByteLength(text)} TO {logicalLengths[set.Variable]}.");
    }

    private static void AppendCobolPrint(
        StringBuilder source,
        BoundPrintStatement print,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values,
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<VariableSymbol, string> logicalLengths)
    {
        if (!print.IsBlankLine && print.Value is BoundVariableExpression variable)
        {
            string name = identifiers.Get(variable.Variable);
            if (logicalLengths.TryGetValue(variable.Variable, out string? lengthName))
            {
                // COBOL String values used by direct PRINT, and every mutated
                // value, carry a logical length beside their PIC X storage.
                // Reference modification reads the current logical bytes so
                // old data and padding never leak after SET. The explicit LF
                // also keeps an empty value to exactly one SMILE newline.
                source.AppendLine($"    IF {lengthName} = 0");
                source.AppendLine("        DISPLAY X\"0A\" WITH NO ADVANCING");
                source.AppendLine("    ELSE");
                source.AppendLine($"        DISPLAY {name}(1:{lengthName}) WITH NO ADVANCING");
                source.AppendLine("        DISPLAY X\"0A\" WITH NO ADVANCING");
                source.AppendLine("    END-IF.");
                return;
            }

            string currentText = GeneratorValueFacts.DisplayText(print.Value, values);
            if (currentText.Length == 0)
            {
                source.AppendLine("    DISPLAY X\"0A\" WITH NO ADVANCING.");
                return;
            }

            source.AppendLine($"    DISPLAY {name} WITH NO ADVANCING.");
            source.AppendLine("    DISPLAY X\"0A\" WITH NO ADVANCING.");
            return;
        }

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

    private static IReadOnlyDictionary<VariableSymbol, string> CreateLogicalLengthNames(
        BoundProgram program,
        TargetIdentifierMap identifiers,
        BoundProgramExecutionTrace trace)
    {
        var names = new Dictionary<VariableSymbol, string>();
        var used = program.Variables
            .Select(identifiers.Get)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var directStringReads = program.Statements
            .OfType<BoundPrintStatement>()
            .Where(print => !print.IsBlankLine)
            .Select(print => print.Value)
            .OfType<BoundVariableExpression>()
            .Where(variable => variable.Variable.Type is SmileType.String)
            .Select(variable => variable.Variable)
            .ToHashSet();

        for (int index = 0; index < program.Variables.Count; index++)
        {
            VariableSymbol variable = program.Variables[index];
            if (trace.MutatedVariables.Contains(variable) || directStringReads.Contains(variable))
            {
                string preferred = $"SMILE-SET-LENGTH-{index}";
                string name = preferred;
                int suffix = 2;
                while (!used.Add(name))
                {
                    name = preferred + "-" + suffix.ToString(CultureInfo.InvariantCulture);
                    suffix++;
                }

                names.Add(variable, name);
            }
        }

        return names;
    }
}

internal sealed class ObjectiveCCodeGenerator : ICodeGenerator
{
    public TargetLanguage Language => TargetLanguage.ObjectiveC;

    public GeneratedProgram Generate(BoundProgram program)
    {
        TargetIdentifierMap identifiers = TargetIdentifierMap.Create(program, Language);
        BoundProgramExecutionTrace trace = BoundProgramExecutionTrace.Create(program);
        TargetIntegerProfile integers = TargetIntegerProfile.Analyze(program, trace);
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths =
            CCodeGenerator.CreateExactStringLengthNames(program, identifiers, trace);
        var source = new StringBuilder();
        source.AppendLine("#include <stdio.h>");
        if (integers.RequiresSigned64Storage)
        {
            source.AppendLine("#include <stdint.h>");
        }

        if (CGenerationFacts.NeedsBooleanHeader(program, trace, exactStringLengths))
        {
            source.AppendLine("#include <stdbool.h>");
        }

        if (CGenerationFacts.NeedsStringComparison(program, trace, exactStringLengths))
        {
            source.AppendLine("#include <string.h>");
        }

        source.AppendLine();
        source.AppendLine("int main(void)");
        source.AppendLine("{");

        bool emittedDeclaration = false;
        bool emittedExecutable = false;
        bool emittedBodyStatement = false;

        for (int index = 0; index < trace.Steps.Count; index++)
        {
            BoundStatementExecution step = trace.Steps[index];
            switch (step.Statement)
            {
                case BoundLetStatement let:
                    // The Windows-local Objective-C toolchain uses Clang/MSYS2
                    // without Foundation. C-compatible console types keep this
                    // target easy to build while still compiling as Objective-C.
                    SmileValue letValue = GeneratorValueFacts.Evaluate(let.Initializer, step.ValuesBefore);
                    string initializer = let.Variable.Type is SmileType.String
                        ? TargetExpression.CConstant(letValue, integers)
                        : TargetExpression.ObjectiveC(
                            let.Initializer,
                            identifiers,
                            integers,
                            step.ValuesBefore,
                            exactStringLengths);
                    source.AppendLine($"    {TargetTypes.CDeclaration(let.Variable.Type, identifiers.Get(let.Variable), integers)} = {initializer};");
                    if (exactStringLengths.TryGetValue(let.Variable, out string? letLengthName))
                    {
                        source.AppendLine($"    size_t {letLengthName} = {CCodeGenerator.Utf8ByteLength(letValue)};");
                    }

                    emittedDeclaration = true;
                    emittedBodyStatement = true;
                    break;

                case BoundSetStatement set:
                    if (!emittedExecutable && emittedDeclaration)
                    {
                        source.AppendLine();
                    }

                    SmileValue setValue = GeneratorValueFacts.Evaluate(set.Value, step.ValuesBefore);
                    string value = set.Variable.Type is SmileType.String
                        ? TargetExpression.CConstant(setValue, integers)
                        : TargetExpression.ObjectiveC(
                            set.Value,
                            identifiers,
                            integers,
                            step.ValuesBefore,
                            exactStringLengths);
                    source.AppendLine($"    {identifiers.Get(set.Variable)} = {value};");
                    if (exactStringLengths.TryGetValue(set.Variable, out string? setLengthName))
                    {
                        source.AppendLine($"    {setLengthName} = {CCodeGenerator.Utf8ByteLength(setValue)};");
                    }

                    emittedExecutable = true;
                    emittedBodyStatement = true;
                    break;

                case BoundPrintStatement print:
                    if (!emittedExecutable && emittedDeclaration)
                    {
                        source.AppendLine();
                    }

                    AppendObjectiveCPrint(
                        source,
                        print,
                        identifiers,
                        integers,
                        step.ValuesBefore,
                        exactStringLengths);
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
        IReadOnlyDictionary<VariableSymbol, SmileValue> values,
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths)
    {
        if (CCodeGenerator.TryAppendDirectStringVariablePrint(
            source,
            "    ",
            print,
            identifiers,
            exactStringLengths))
        {
            return;
        }

        if (CCodeGenerator.TryAppendExactNulStringPrint(source, "    ", print, values))
        {
            return;
        }

        CPrintfPlan plan = CPrintfPlan.FromPrint(
            print,
            expression => TargetExpression.ObjectiveC(
                expression,
                identifiers,
                integers,
                values,
                exactStringLengths),
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
        BoundProgramExecutionTrace trace = BoundProgramExecutionTrace.Create(program);
        TargetIntegerProfile integers = TargetIntegerProfile.Analyze(program, trace);
        var source = new StringBuilder();

        foreach (BoundStatement statement in program.Statements)
        {
            switch (statement)
            {
                case BoundLetStatement let:
                    string initializer = TargetExpression.Swift(let.Initializer, identifiers, integers);
                    string declaration = trace.MutatedVariables.Contains(let.Variable) ? "var" : "let";
                    source.AppendLine($"{declaration} {identifiers.Get(let.Variable)}: {TargetTypes.Swift(let.Variable.Type, integers)} = {initializer}");
                    break;

                case BoundSetStatement set:
                    string name = identifiers.Get(set.Variable);
                    string value = TargetExpression.Swift(set.Value, identifiers, integers);
                    if (set.Value is BoundVariableExpression variable &&
                        variable.Variable == set.Variable)
                    {
                        // Swift rejects a plain `value = value` as a compile-time
                        // error even though direct self-assignment is valid SMILE.
                        // Keep the required target storage update with the
                        // smallest type-preserving identity expression.
                        value = set.Variable.Type switch
                        {
                            SmileType.String => value + " + \"\"",
                            SmileType.Integer => value + " + 0",
                            SmileType.Boolean => value + " || false",
                            _ => value
                        };
                    }

                    source.AppendLine($"{name} = {value}");
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
        BoundProgramExecutionTrace trace = BoundProgramExecutionTrace.Create(program);
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
            for (int index = 0; index < trace.Steps.Count; index++)
            {
                BoundStatementExecution step = trace.Steps[index];
                var expressions = new PythonExpressionWriter(identifiers, step.ValuesBefore);
                switch (step.Statement)
                {
                    case BoundLetStatement let:
                        source.AppendLine($"    {identifiers.Get(let.Variable)} = {expressions.Write(let.Initializer)}");
                        break;

                    case BoundSetStatement set:
                        source.AppendLine($"    {identifiers.Get(set.Variable)} = {expressions.Write(set.Value)}");
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
        BoundProgramExecutionTrace trace = BoundProgramExecutionTrace.Create(program);
        TargetIntegerProfile integers = TargetIntegerProfile.Analyze(program, trace);
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
        for (int index = 0; index < trace.Steps.Count; index++)
        {
            BoundStatementExecution step = trace.Steps[index];
            switch (step.Statement)
            {
                case BoundLetStatement let:
                    source.AppendLine(
                        $"    {TargetTypes.Cpp(let.Variable.Type, integers)} {identifiers.Get(let.Variable)} = {expressions.Write(let.Initializer)};");
                    emittedDeclaration = true;
                    break;

                case BoundSetStatement set:
                    if (!emittedExecutable && emittedDeclaration)
                    {
                        source.AppendLine();
                    }

                    source.AppendLine($"    {identifiers.Get(set.Variable)} = {expressions.Write(set.Value)};");
                    emittedExecutable = true;
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
            BoundSetStatement set => ContainsStringFacility(set.Value),
            BoundPrintStatement print when !print.IsBlankLine =>
                ContainsDirectStreamStringFacility(print.Value),
            _ => false
        });

    private static bool ContainsDirectStreamStringFacility(BoundExpression expression) =>
        expression is BoundInterpolatedStringExpression interpolated
            ? interpolated.Parts.Any(part => part switch
            {
                BoundInterpolatedTextPart text => text.Text.Contains('\0', StringComparison.Ordinal),
                BoundInterpolationExpressionPart hole => ContainsStringFacility(hole.Expression),
                _ => false
            })
            : ContainsStringFacility(expression);

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
            BoundSetStatement set => ContainsTextConversion(set.Value),
            BoundPrintStatement print when !print.IsBlankLine =>
                print.Value.Type is not SmileType.String || ContainsTextConversion(print.Value),
            _ => false
        });

    public static bool NeedsDivisionHelper(BoundProgram program) =>
        program.Statements.Any(statement => statement switch
        {
            BoundLetStatement let => ContainsDivision(let.Initializer),
            BoundSetStatement set => ContainsDivision(set.Value),
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
    public static bool NeedsBooleanHeader(
        BoundProgram program,
        BoundProgramExecutionTrace trace,
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths)
    {
        if (program.Variables.Any(variable => variable.Type is SmileType.Boolean))
        {
            return true;
        }

        for (int index = 0; index < program.Statements.Count; index++)
        {
            if (program.Statements[index] is BoundPrintStatement { IsBlankLine: false } print &&
                (ContainsBooleanLiteral(print.Value) ||
                 ContainsNulSensitiveStringComparison(
                     print.Value,
                     trace.Steps[index].ValuesBefore,
                     exactStringLengths)))
            {
                return true;
            }
        }

        return false;
    }

    public static bool NeedsStringComparison(
        BoundProgram program,
        BoundProgramExecutionTrace trace,
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths)
    {
        for (int index = 0; index < program.Statements.Count; index++)
        {
            BoundStatementExecution step = trace.Steps[index];
            bool needsComparison = step.Statement switch
            {
                BoundLetStatement let when let.Variable.Type is not SmileType.String =>
                    ContainsStringComparison(let.Initializer, step.ValuesBefore, exactStringLengths),
                BoundSetStatement set when set.Variable.Type is not SmileType.String =>
                    ContainsStringComparison(set.Value, step.ValuesBefore, exactStringLengths),
                BoundPrintStatement print when !print.IsBlankLine =>
                    ContainsStringComparison(print.Value, step.ValuesBefore, exactStringLengths),
                _ => false
            };

            if (needsComparison)
            {
                return true;
            }
        }

        return false;
    }

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
        IReadOnlyDictionary<VariableSymbol, SmileValue> values,
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths) =>
        expression switch
        {
            BoundBinaryExpression binary =>
                NeedsCStringComparisonFacility(binary, values, exactStringLengths) ||
                ContainsStringComparison(binary.Left, values, exactStringLengths) ||
                ContainsStringComparison(binary.Right, values, exactStringLengths),
            BoundUnaryExpression unary =>
                ContainsStringComparison(unary.Operand, values, exactStringLengths),
            BoundInterpolatedStringExpression interpolated => interpolated.Parts.Any(part =>
                part is BoundInterpolationExpressionPart interpolation &&
                ContainsStringComparison(interpolation.Expression, values, exactStringLengths)),
            _ => false
        };

    private static bool ContainsNulSensitiveStringComparison(
        BoundExpression expression,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values,
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths) =>
        expression switch
        {
            BoundBinaryExpression binary =>
                (IsNulSensitiveStringComparison(binary, values) &&
                 !ShouldUseExactStorageComparison(binary, exactStringLengths)) ||
                ContainsNulSensitiveStringComparison(binary.Left, values, exactStringLengths) ||
                ContainsNulSensitiveStringComparison(binary.Right, values, exactStringLengths),
            BoundUnaryExpression unary =>
                ContainsNulSensitiveStringComparison(unary.Operand, values, exactStringLengths),
            BoundInterpolatedStringExpression interpolated => interpolated.Parts.Any(part =>
                part is BoundInterpolationExpressionPart interpolation &&
                ContainsNulSensitiveStringComparison(
                    interpolation.Expression,
                    values,
                    exactStringLengths)),
            _ => false
        };

    internal static bool ShouldUseExactStorageComparison(
        BoundBinaryExpression expression,
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths)
    {
        if (expression.Left.Type is not SmileType.String ||
            expression.Operator.Kind is not (BoundBinaryOperatorKind.Equality or BoundBinaryOperatorKind.Inequality) ||
            !IsDirectStringStorageOperand(expression.Left) ||
            !IsDirectStringStorageOperand(expression.Right) ||
            expression.Left is not BoundVariableExpression &&
            expression.Right is not BoundVariableExpression)
        {
            return false;
        }

        return RequiresExactLength(expression.Left, exactStringLengths) ||
            RequiresExactLength(expression.Right, exactStringLengths);
    }

    private static bool IsDirectStringStorageOperand(BoundExpression expression) =>
        expression is BoundVariableExpression or BoundStringLiteralExpression;

    private static bool RequiresExactLength(
        BoundExpression expression,
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths) =>
        expression switch
        {
            BoundVariableExpression variable => exactStringLengths.ContainsKey(variable.Variable),
            BoundStringLiteralExpression literal => literal.Value.Contains('\0', StringComparison.Ordinal),
            _ => false
        };

    private static bool NeedsCStringComparisonFacility(
        BoundBinaryExpression expression,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values,
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths) =>
        ShouldUseExactStorageComparison(expression, exactStringLengths) || NeedsStrcmp(expression, values);

    private static bool IsNulSensitiveStringComparison(
        BoundBinaryExpression expression,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values) =>
        expression.Left.Type is SmileType.String &&
        (expression.Operator.Kind is BoundBinaryOperatorKind.Equality or BoundBinaryOperatorKind.Inequality) &&
        (GeneratorValueFacts.TryGetNulContainingString(expression.Left, values, out _) ||
         GeneratorValueFacts.TryGetNulContainingString(expression.Right, values, out _));

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
            BoundSetStatement set => NeedsInvariantCulture(set.Value, displayContext: false),
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
    public static SmileValue Evaluate(
        BoundExpression expression,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values)
    {
        if (BoundExpressionEvaluator.TryEvaluate(expression, values, out SmileValue value))
        {
            return value;
        }

        throw new InvalidOperationException("Bound expression could not be evaluated for target lowering.");
    }

    public static bool AssignedValuesContainNul(
        BoundProgramExecutionTrace trace,
        VariableSymbol variable) =>
        trace.AssignedValues.TryGetValue(variable, out var values) &&
        values.Any(value =>
            value.Type is SmileType.String &&
            value.StringValue.Contains('\0', StringComparison.Ordinal));

    public static int MaximumAssignedUtf8ByteLength(
        BoundProgramExecutionTrace trace,
        VariableSymbol variable) =>
        trace.AssignedValues.TryGetValue(variable, out var values)
            ? values.Max(value => Encoding.UTF8.GetByteCount(value.ToDisplayText()))
            : 0;

    public static bool TryGetNulContainingString(
        BoundExpression expression,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values,
        out string value)
    {
        if (BoundExpressionEvaluator.TryEvaluate(expression, values, out SmileValue evaluated) &&
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
        return Evaluate(expression, values).ToDisplayText();
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
        IReadOnlyDictionary<VariableSymbol, SmileValue> values,
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths) =>
        new Writer(
            TargetLanguage.C,
            identifiers,
            integers,
            values,
            exactStringLengths).Write(expression);

    public static string ObjectiveC(
        BoundExpression expression,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values,
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths) =>
        new Writer(
            TargetLanguage.ObjectiveC,
            identifiers,
            integers,
            values,
            exactStringLengths).Write(expression);

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
        private readonly IReadOnlyDictionary<VariableSymbol, string>? _exactStringLengths;

        private readonly record struct CStringStorageOperand(string Value, string Length);

        public Writer(
            TargetLanguage language,
            TargetIdentifierMap identifiers,
            TargetIntegerProfile integers,
            IReadOnlyDictionary<VariableSymbol, SmileValue>? values = null,
            IReadOnlyDictionary<VariableSymbol, string>? exactStringLengths = null)
        {
            _language = language;
            _identifiers = identifiers;
            _integers = integers;
            _values = values;
            _exactStringLengths = exactStringLengths;
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
            if (_exactStringLengths is not null &&
                CGenerationFacts.ShouldUseExactStorageComparison(expression, _exactStringLengths))
            {
                CStringStorageOperand left = WriteCStringStorageOperand(expression.Left);
                CStringStorageOperand right = WriteCStringStorageOperand(expression.Right);
                bool equality = expression.Operator.Kind is BoundBinaryOperatorKind.Equality;
                string lengthOperator = equality ? " == " : " != ";
                string logicalOperator = equality ? " && " : " || ";
                string byteOperator = equality ? " == 0" : " != 0";

                // Compare lengths first so memcmp is reached only when both
                // operands expose the same number of logical UTF-8 bytes. That
                // keeps prefix collisions exact and never reads past either
                // current target value.
                return "(" +
                    left.Length + lengthOperator + right.Length +
                    logicalOperator +
                    "memcmp(" + left.Value + ", " + right.Value + ", " + left.Length + ")" +
                    byteOperator +
                    ")";
            }

            if (_values is not null &&
                (GeneratorValueFacts.TryGetNulContainingString(expression.Left, _values, out _) ||
                 GeneratorValueFacts.TryGetNulContainingString(expression.Right, _values, out _)) &&
                BoundExpressionEvaluator.TryEvaluate(expression, _values, out SmileValue evaluated) &&
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

        private CStringStorageOperand WriteCStringStorageOperand(BoundExpression expression) =>
            expression switch
            {
                BoundVariableExpression variable => WriteCStringVariableStorageOperand(variable),
                BoundStringLiteralExpression literal => new CStringStorageOperand(
                    TargetEscapes.CString(literal.Value),
                    Encoding.UTF8.GetByteCount(literal.Value).ToString(CultureInfo.InvariantCulture)),
                _ => throw new InvalidOperationException(
                    "Exact C String storage comparisons require a variable or literal operand.")
            };

        private CStringStorageOperand WriteCStringVariableStorageOperand(BoundVariableExpression variable)
        {
            string name = _identifiers.Get(variable.Variable);
            string length = _exactStringLengths!.TryGetValue(variable.Variable, out string? exactLength)
                ? exactLength
                : $"strlen({name})";
            return new CStringStorageOperand(name, length);
        }

        private string WriteCStringEqualityOperand(BoundExpression expression)
        {
            if (expression is BoundStringLiteralExpression or BoundVariableExpression)
            {
                return WriteExpression(expression, 0, isRightChild: false, parentOperator: null);
            }

            if (_values is not null &&
                BoundExpressionEvaluator.TryEvaluate(expression, _values, out SmileValue value) &&
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
