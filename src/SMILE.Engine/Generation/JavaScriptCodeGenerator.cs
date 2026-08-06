using System.Text;

namespace SMILE.Engine;

internal sealed class JavaScriptCodeGenerator : ICodeGenerator
{
    public TargetLanguage Language => TargetLanguage.JavaScript;

    public GeneratedProgram Generate(BoundProgram program)
    {
        TargetIdentifierMap identifiers = TargetIdentifierMap.Create(program, Language);
        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(program);
        TargetIntegerProfile integers = TargetIntegerProfile.Analyze(program, analysis);
        var source = new StringBuilder();

        AppendStatements(source, program.Statements, string.Empty, identifiers, integers);

        return new GeneratedProgram(
            Language,
            new[] { new GeneratedFile("Program.js", TextOutput.EnsureOneTrailingNewLine(source.ToString()), IsPrimary: true) });
    }

    private static void AppendStatements(
        StringBuilder source,
        IReadOnlyList<BoundStatement> statements,
        string indent,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers)
    {
        foreach (BoundStatement statement in statements)
        {
            switch (statement)
            {
                case BoundLetStatement let:
                    source.AppendLine($"{indent}let {identifiers.Get(let.Variable)} = {TargetExpression.JavaScript(let.Initializer, identifiers, integers)};");
                    break;

                case BoundSetStatement set:
                    source.AppendLine($"{indent}{identifiers.Get(set.Variable)} = {TargetExpression.JavaScript(set.Value, identifiers, integers)};");
                    break;

                case BoundPrintStatement print:
                    source.Append(indent).AppendLine(print.IsBlankLine
                        ? "console.log();"
                        : $"console.log({TargetExpression.JavaScriptDisplay(print.Value, identifiers, integers)});");
                    break;

                case BoundIfStatement conditional:
                    AppendIfStatement(source, conditional, indent, identifiers, integers);
                    break;
            }
        }
    }

    private static void AppendIfStatement(
        StringBuilder source,
        BoundIfStatement conditional,
        string indent,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers)
    {
        for (int clauseIndex = 0; clauseIndex < conditional.Clauses.Count; clauseIndex++)
        {
            BoundConditionalClause clause = conditional.Clauses[clauseIndex];
            source.Append(indent)
                .Append(clauseIndex == 0 ? "if (" : "else if (")
                .Append(TargetExpression.JavaScript(clause.Condition, identifiers, integers))
                .AppendLine(") {");
            AppendStatements(source, clause.Statements, indent + "    ", identifiers, integers);
            source.Append(indent).AppendLine("}");
        }

        if (conditional.HasElseClause)
        {
            source.Append(indent).AppendLine("else {");
            AppendStatements(source, conditional.ElseStatements, indent + "    ", identifiers, integers);
            source.Append(indent).AppendLine("}");
        }
    }
}
