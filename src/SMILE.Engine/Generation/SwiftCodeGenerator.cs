using System.Text;

namespace SMILE.Engine;

internal sealed class SwiftCodeGenerator : ICodeGenerator
{
    public TargetLanguage Language => TargetLanguage.Swift;

    public GeneratedProgram Generate(BoundProgram program)
    {
        TargetIdentifierMap identifiers = TargetIdentifierMap.Create(program, Language);
        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(program);
        TargetIntegerProfile integers = TargetIntegerProfile.Analyze(program, analysis);
        var source = new StringBuilder();
        IReadOnlySet<VariableSymbol> mutatedVariables = BoundStatementTree.Enumerate(program)
            .OfType<BoundSetStatement>()
            .Select(set => set.Variable)
            .ToHashSet();
        bool needsConditionHelper = BoundStatementTree.Enumerate(program)
            .OfType<BoundIfStatement>()
            .SelectMany(conditional => conditional.Clauses)
            .Any(clause => GeneratorConditionFacts.RequiresWarningSafeWrapper(clause.Condition));

        if (needsConditionHelper)
        {
            source.AppendLine("// Keep a valid source-constant IF as genuine control flow without warnings.");
            source.AppendLine("@inline(never)");
            source.AppendLine("func _smile_condition(_ value: Bool) -> Bool {");
            source.AppendLine("    value");
            source.AppendLine("}");
            source.AppendLine();
        }

        AppendStatements(
            source,
            program.Statements,
            string.Empty,
            identifiers,
            integers,
            mutatedVariables,
            needsConditionHelper);

        return new GeneratedProgram(
            Language,
            new[] { new GeneratedFile("Program.swift", TextOutput.EnsureOneTrailingNewLine(source.ToString()), IsPrimary: true) });
    }

    private static void AppendStatements(
        StringBuilder source,
        IReadOnlyList<BoundStatement> statements,
        string indent,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        IReadOnlySet<VariableSymbol> mutatedVariables,
        bool hasConditionHelper)
    {
        foreach (BoundStatement statement in statements)
        {
            switch (statement)
            {
                case BoundLetStatement let:
                    string initializer = TargetExpression.Swift(let.Initializer, identifiers, integers);
                    string declaration = mutatedVariables.Contains(let.Variable) ? "var" : "let";
                    source.AppendLine($"{indent}{declaration} {identifiers.Get(let.Variable)}: {TargetTypes.Swift(let.Variable.Type, integers)} = {initializer}");
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

                    source.AppendLine($"{indent}{name} = {value}");
                    break;

                case BoundPrintStatement print:
                    source.Append(indent).AppendLine(print.IsBlankLine
                        ? "print()"
                        : $"print({TargetExpression.SwiftDisplay(print.Value, identifiers, integers)})");
                    break;

                case BoundIfStatement conditional:
                    AppendIfStatement(
                        source,
                        conditional,
                        indent,
                        identifiers,
                        integers,
                        mutatedVariables,
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
        IReadOnlySet<VariableSymbol> mutatedVariables,
        bool hasConditionHelper)
    {
        for (int clauseIndex = 0; clauseIndex < conditional.Clauses.Count; clauseIndex++)
        {
            BoundConditionalClause clause = conditional.Clauses[clauseIndex];
            string condition = TargetExpression.Swift(clause.Condition, identifiers, integers);
            if (GeneratorConditionFacts.RequiresWarningSafeWrapper(clause.Condition))
            {
                condition = $"_smile_condition({condition})";
            }

            source.Append(indent)
                .Append(clauseIndex == 0 ? "if " : "else if ")
                .Append(condition)
                .AppendLine(" {");
            AppendStatements(
                source,
                clause.Statements,
                indent + "    ",
                identifiers,
                integers,
                mutatedVariables,
                hasConditionHelper);
            source.Append(indent).AppendLine("}");
        }

        if (conditional.HasElseClause)
        {
            source.Append(indent).AppendLine("else {");
            AppendStatements(
                source,
                conditional.ElseStatements,
                indent + "    ",
                identifiers,
                integers,
                mutatedVariables,
                hasConditionHelper);
            source.Append(indent).AppendLine("}");
        }
    }
}
