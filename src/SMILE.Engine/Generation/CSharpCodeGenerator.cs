using System.Text;

namespace SMILE.Engine;

internal sealed class CSharpCodeGenerator : ICodeGenerator
{
    public TargetLanguage Language => TargetLanguage.CSharp;

    public GeneratedProgram Generate(BoundProgram program)
    {
        TargetIdentifierMap identifiers = TargetIdentifierMap.Create(program, Language);
        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(program);
        TargetIntegerProfile integers = TargetIntegerProfile.Analyze(program, analysis);
        // Console.ReadLine is already the native C# INPUT facility. INPUT by
        // itself should not inject encoding setup into a tiny learner program.
        bool requiresUtf8Output = TargetRuntimeFacts.RequiresUtf8OutputFromSource(program);
        bool checkedArithmetic = TargetRuntimeFacts.NeedsCheckedIntegerArithmetic(program);
        bool needsConditionHelper = BoundStatementTree.Enumerate(program).Any(statement =>
            statement switch
            {
                BoundIfStatement conditional => conditional.Clauses.Any(clause =>
                    GeneratorConditionFacts.RequiresWarningSafeWrapper(clause.Condition)),
                BoundWhileStatement loop =>
                    GeneratorConditionFacts.RequiresWarningSafeWrapper(loop.Condition),
                _ => false
            });
        var source = new StringBuilder();
        source.AppendLine("using System;");
        if (requiresUtf8Output)
        {
            source.AppendLine("using System.Text;");
        }

        source.AppendLine();
        source.AppendLine("internal static class Program");
        source.AppendLine("{");
        source.AppendLine("    private static void Main()");
        source.AppendLine("    {");

        if (requiresUtf8Output)
        {
            source.AppendLine("        Console.OutputEncoding = new UTF8Encoding(false);");
        }

        AppendSourceItems(
            source,
            program.SourceItems,
            "        ",
            identifiers,
            integers,
            needsConditionHelper,
            checkedArithmetic);

        source.AppendLine("    }");
        if (needsConditionHelper)
        {
            source.AppendLine();
            source.AppendLine("    // Keep valid source-constant control flow genuine without CS0162.");
            source.AppendLine("    private static bool _smile_condition(bool value) => value;");
        }

        if (checkedArithmetic)
        {
            AppendFailureHelper(source);
        }

        if (checkedArithmetic)
        {
            AppendCheckedArithmeticHelpers(source, integers);
        }

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
                new GeneratedFile("Program.cs", TextOutput.EnsureOneTrailingNewLinePreservingExistingLineEndings(source.ToString()), IsPrimary: true),
                new GeneratedFile("GeneratedProgram.csproj", TextOutput.EnsureOneTrailingNewLine(project), IsPrimary: false)
            });
    }

    private static void AppendSourceItems(
        StringBuilder source,
        IReadOnlyList<BoundSourceItem> sourceItems,
        string indent,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        bool hasConditionHelper,
        bool checkedArithmetic)
    {
        foreach (BoundSourceItem sourceItem in sourceItems)
        {
            switch (sourceItem)
            {
                case BoundFullLineComment comment:
                    TargetComments.Append(source, TargetLanguage.CSharp, indent, comment.Payload);
                    break;

                case BoundBlankLine:
                    source.AppendLine();
                    break;

                case BoundLetStatement let:
                    string initializer = WriteDirectExpression(
                        let.Initializer,
                        indent,
                        identifiers,
                        integers,
                        checkedArithmetic);
                    source.AppendLine($"{indent}{TargetTypes.CSharp(let.Variable.Type, integers)} {identifiers.Get(let.Variable)} = {initializer};");
                    break;

                case BoundSetStatement set:
                    string name = identifiers.Get(set.Variable);
                    string value = WriteDirectExpression(
                        set.Value,
                        indent,
                        identifiers,
                        integers,
                        checkedArithmetic);
                    if (set.Value is BoundVariableExpression variable &&
                        ReferenceEquals(variable.Variable, set.Variable))
                    {
                        // Direct self-assignment is valid SMILE, but C# warns
                        // about a plain `value = value` (CS1717). Keep the real
                        // storage update with the smallest type-preserving
                        // identity expression instead of deleting the SET.
                        value = set.Variable.Type switch
                        {
                            SmileType.String => value + " + \"\"",
                            SmileType.Integer => value + " + 0",
                            SmileType.Boolean => value + " || false",
                            _ => value
                        };
                    }

                    source.AppendLine($"{indent}{name} = {value};");
                    break;

                case BoundInputStatement input:
                    string inputExpression = input.Variable.Type switch
                    {
                        SmileType.String => "Console.ReadLine() ?? string.Empty",
                        SmileType.Integer => integers.RequiresSigned64Storage
                            ? "long.Parse(Console.ReadLine()!)"
                            : "int.Parse(Console.ReadLine()!)",
                        SmileType.Boolean => "bool.Parse(Console.ReadLine()!)",
                        _ => throw new InvalidOperationException("Unsupported INPUT target type.")
                    };
                    source.Append(indent).Append(identifiers.Get(input.Variable)).Append(" = ")
                        .Append(inputExpression).AppendLine(";");
                    break;

                case BoundPrintStatement print:
                    if (print.IsBlankLine)
                    {
                        source.Append(indent).AppendLine("Console.WriteLine();");
                    }
                    else
                    {
                        source.AppendLine($"{indent}Console.WriteLine({WriteDirectDisplayExpression(print.Value, indent, identifiers, integers, checkedArithmetic)});");
                    }

                    break;

                case BoundIfStatement conditional:
                    AppendIfStatement(
                        source,
                        conditional,
                        indent,
                        identifiers,
                        integers,
                        hasConditionHelper,
                        checkedArithmetic);
                    break;

                case BoundWhileStatement loop:
                    AppendWhileStatement(
                        source,
                        loop,
                        indent,
                        identifiers,
                        integers,
                        hasConditionHelper,
                        checkedArithmetic);
                    break;
            }
        }
    }

    private static string WriteDirectExpression(
        BoundExpression expression,
        string structuralIndent,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        bool checkedArithmetic) =>
        expression is BoundStringLiteralExpression literal &&
        TargetMultilineLiterals.TryCSharp(literal.Value, structuralIndent, out string multiline)
            ? multiline
            : TargetExpression.CSharp(expression, identifiers, integers, checkedArithmetic);

    private static string WriteDirectDisplayExpression(
        BoundExpression expression,
        string structuralIndent,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        bool checkedArithmetic) =>
        expression is BoundStringLiteralExpression literal &&
        TargetMultilineLiterals.TryCSharp(literal.Value, structuralIndent, out string multiline)
            ? multiline
            : TargetExpression.CSharpDisplay(expression, identifiers, integers, checkedArithmetic);

    private static void AppendIfStatement(
        StringBuilder source,
        BoundIfStatement conditional,
        string indent,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        bool hasConditionHelper,
        bool checkedArithmetic)
    {
        for (int clauseIndex = 0; clauseIndex < conditional.Clauses.Count; clauseIndex++)
        {
            BoundConditionalClause clause = conditional.Clauses[clauseIndex];
            string condition = TargetExpression.CSharp(clause.Condition, identifiers, integers, checkedArithmetic);
            if (GeneratorConditionFacts.RequiresWarningSafeWrapper(clause.Condition))
            {
                condition = $"_smile_condition({condition})";
            }

            source.Append(indent)
                .Append(clauseIndex == 0 ? "if (" : "else if (")
                .Append(condition)
                .AppendLine(")");
            source.Append(indent).AppendLine("{");
            AppendSourceItems(
                source,
                clause.SourceItems,
                indent + "    ",
                identifiers,
                integers,
                hasConditionHelper,
                checkedArithmetic);
            source.Append(indent).AppendLine("}");
        }

        if (conditional.HasElseClause)
        {
            source.Append(indent).AppendLine("else");
            source.Append(indent).AppendLine("{");
            AppendSourceItems(
                source,
                conditional.ElseSourceItems,
                indent + "    ",
                identifiers,
                integers,
                hasConditionHelper,
                checkedArithmetic);
            source.Append(indent).AppendLine("}");
        }
    }

    private static void AppendWhileStatement(
        StringBuilder source,
        BoundWhileStatement loop,
        string indent,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        bool hasConditionHelper,
        bool checkedArithmetic)
    {
        string condition = TargetExpression.CSharp(
            loop.Condition,
            identifiers,
            integers,
            checkedArithmetic);
        if (GeneratorConditionFacts.RequiresWarningSafeWrapper(loop.Condition))
        {
            condition = $"_smile_condition({condition})";
        }

        source.Append(indent).Append("while (").Append(condition).AppendLine(")");
        source.Append(indent).AppendLine("{");
        AppendSourceItems(
            source,
            loop.SourceItems,
            indent + "    ",
            identifiers,
            integers,
            hasConditionHelper,
            checkedArithmetic);
        source.Append(indent).AppendLine("}");
    }

    private static void AppendFailureHelper(StringBuilder source)
    {
        source.AppendLine();
        source.AppendLine("    private static void _smile_fail(string message)");
        source.AppendLine("    {");
        source.AppendLine("        Console.Error.WriteLine(message);");
        source.AppendLine("        Environment.Exit(1);");
        source.AppendLine("    }");
    }

    private static void AppendCheckedArithmeticHelpers(
        StringBuilder source,
        TargetIntegerProfile integers)
    {
        string type = integers.RequiresSigned64Storage ? "long" : "int";
        source.AppendLine();
        source.AppendLine($"    private static {type} _smile_add({type} left, {type} right)");
        source.AppendLine("    {");
        source.AppendLine("        try { return checked(left + right); }");
        source.AppendLine("        catch (OverflowException) { _smile_fail(\"SMILE Runtime Error SMILER1206: Integer arithmetic overflow.\"); return 0; }");
        source.AppendLine("    }");
        source.AppendLine();
        source.AppendLine($"    private static {type} _smile_subtract({type} left, {type} right)");
        source.AppendLine("    {");
        source.AppendLine("        try { return checked(left - right); }");
        source.AppendLine("        catch (OverflowException) { _smile_fail(\"SMILE Runtime Error SMILER1206: Integer arithmetic overflow.\"); return 0; }");
        source.AppendLine("    }");
        source.AppendLine();
        source.AppendLine($"    private static {type} _smile_multiply({type} left, {type} right)");
        source.AppendLine("    {");
        source.AppendLine("        try { return checked(left * right); }");
        source.AppendLine("        catch (OverflowException) { _smile_fail(\"SMILE Runtime Error SMILER1206: Integer arithmetic overflow.\"); return 0; }");
        source.AppendLine("    }");
        source.AppendLine();
        source.AppendLine($"    private static {type} _smile_negate({type} value)");
        source.AppendLine("    {");
        source.AppendLine("        try { return checked(-value); }");
        source.AppendLine("        catch (OverflowException) { _smile_fail(\"SMILE Runtime Error SMILER1206: Integer arithmetic overflow.\"); return 0; }");
        source.AppendLine("    }");
        source.AppendLine();
        source.AppendLine($"    private static {type} _smile_divide({type} left, {type} right)");
        source.AppendLine("    {");
        source.AppendLine("        if (right == 0) { _smile_fail(\"SMILE Runtime Error SMILER1207: Division by zero.\"); return 0; }");
        source.AppendLine($"        if (left == {type}.MinValue && right == -1) {{ _smile_fail(\"SMILE Runtime Error SMILER1206: Integer arithmetic overflow.\"); return 0; }}");
        source.AppendLine("        return left / right;");
        source.AppendLine("    }");
    }
}
