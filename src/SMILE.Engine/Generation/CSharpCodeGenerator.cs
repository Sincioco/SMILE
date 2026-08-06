using System.Globalization;
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
        bool needsConditionHelper = BoundStatementTree.Enumerate(program)
            .OfType<BoundIfStatement>()
            .SelectMany(conditional => conditional.Clauses)
            .Any(clause => GeneratorConditionFacts.RequiresWarningSafeWrapper(clause.Condition));
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

        AppendStatements(
            source,
            program.Statements,
            "        ",
            identifiers,
            integers,
            needsConditionHelper);

        source.AppendLine("    }");
        if (needsConditionHelper)
        {
            source.AppendLine();
            source.AppendLine("    // Keep a valid source-constant IF as genuine control flow without CS0162.");
            source.AppendLine("    private static bool _smile_condition(bool value) => value;");
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
                new GeneratedFile("Program.cs", TextOutput.EnsureOneTrailingNewLine(source.ToString()), IsPrimary: true),
                new GeneratedFile("GeneratedProgram.csproj", TextOutput.EnsureOneTrailingNewLine(project), IsPrimary: false)
            });
    }

    private static void AppendStatements(
        StringBuilder source,
        IReadOnlyList<BoundStatement> statements,
        string indent,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        bool hasConditionHelper)
    {
        foreach (BoundStatement statement in statements)
        {
            switch (statement)
            {
                case BoundLetStatement let:
                    string initializer = TargetExpression.CSharp(let.Initializer, identifiers, integers);
                    source.AppendLine($"{indent}{TargetTypes.CSharp(let.Variable.Type, integers)} {identifiers.Get(let.Variable)} = {initializer};");
                    break;

                case BoundSetStatement set:
                    string name = identifiers.Get(set.Variable);
                    string value = TargetExpression.CSharp(set.Value, identifiers, integers);
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

                case BoundPrintStatement print:
                    if (print.IsBlankLine)
                    {
                        source.Append(indent).AppendLine("Console.WriteLine();");
                    }
                    else
                    {
                        source.AppendLine($"{indent}Console.WriteLine({TargetExpression.CSharpDisplay(print.Value, identifiers, integers)});");
                    }

                    break;

                case BoundIfStatement conditional:
                    AppendIfStatement(
                        source,
                        conditional,
                        indent,
                        identifiers,
                        integers,
                        hasConditionHelper);
                    break;
            }
        }
    }

    private static void AppendIfStatement(
        StringBuilder source,
        BoundIfStatement conditional,
        string indent,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        bool hasConditionHelper)
    {
        for (int clauseIndex = 0; clauseIndex < conditional.Clauses.Count; clauseIndex++)
        {
            BoundConditionalClause clause = conditional.Clauses[clauseIndex];
            string condition = TargetExpression.CSharp(clause.Condition, identifiers, integers);
            if (GeneratorConditionFacts.RequiresWarningSafeWrapper(clause.Condition))
            {
                condition = $"_smile_condition({condition})";
            }

            source.Append(indent)
                .Append(clauseIndex == 0 ? "if (" : "else if (")
                .Append(condition)
                .AppendLine(")");
            source.Append(indent).AppendLine("{");
            AppendStatements(
                source,
                clause.Statements,
                indent + "    ",
                identifiers,
                integers,
                hasConditionHelper);
            source.Append(indent).AppendLine("}");
        }

        if (conditional.HasElseClause)
        {
            source.Append(indent).AppendLine("else");
            source.Append(indent).AppendLine("{");
            AppendStatements(
                source,
                conditional.ElseStatements,
                indent + "    ",
                identifiers,
                integers,
                hasConditionHelper);
            source.Append(indent).AppendLine("}");
        }
    }
}

internal static class CSharpGenerationFacts
{
    public static bool NeedsInvariantCulture(BoundProgram program) =>
        BoundStatementTree.Enumerate(program).Any(statement => statement switch
        {
            BoundLetStatement let => NeedsInvariantCulture(let.Initializer, displayContext: false),
            BoundSetStatement set => NeedsInvariantCulture(set.Value, displayContext: false),
            BoundPrintStatement print => !print.IsBlankLine && NeedsInvariantCulture(print.Value, displayContext: true),
            BoundIfStatement conditional => conditional.Clauses.Any(clause =>
                NeedsInvariantCulture(clause.Condition, displayContext: false)),
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
