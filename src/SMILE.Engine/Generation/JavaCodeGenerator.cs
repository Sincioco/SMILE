using System.Text;

namespace SMILE.Engine;

internal sealed class JavaCodeGenerator : ICodeGenerator
{
    public TargetLanguage Language => TargetLanguage.Java;

    public GeneratedProgram Generate(BoundProgram program)
    {
        TargetIdentifierMap identifiers = TargetIdentifierMap.Create(program, Language);
        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(program);
        TargetIntegerProfile integers = TargetIntegerProfile.Analyze(program, analysis);
        var source = new StringBuilder();
        source.AppendLine("public final class Program");
        source.AppendLine("{");
        source.AppendLine("    public static void main(String[] args)");
        source.AppendLine("    {");

        AppendStatements(source, program.Statements, "        ", identifiers, integers);

        source.AppendLine("    }");
        source.AppendLine("}");

        return new GeneratedProgram(
            Language,
            new[] { new GeneratedFile("Program.java", TextOutput.EnsureOneTrailingNewLine(source.ToString()), IsPrimary: true) });
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
                    string initializer = TargetExpression.Java(let.Initializer, identifiers, integers);
                    source.AppendLine($"{indent}{TargetTypes.Java(let.Variable.Type, integers)} {identifiers.Get(let.Variable)} = {initializer};");
                    break;

                case BoundSetStatement set:
                    source.AppendLine($"{indent}{identifiers.Get(set.Variable)} = {TargetExpression.Java(set.Value, identifiers, integers)};");
                    break;

                case BoundPrintStatement print:
                    source.Append(indent).AppendLine(print.IsBlankLine
                        ? "System.out.println();"
                        : $"System.out.println({TargetExpression.JavaDisplay(print.Value, identifiers, integers)});");
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
                .Append(TargetExpression.Java(clause.Condition, identifiers, integers))
                .AppendLine(")");
            source.Append(indent).AppendLine("{");
            AppendStatements(source, clause.Statements, indent + "    ", identifiers, integers);
            source.Append(indent).AppendLine("}");
        }

        if (conditional.HasElseClause)
        {
            source.Append(indent).AppendLine("else");
            source.Append(indent).AppendLine("{");
            AppendStatements(source, conditional.ElseStatements, indent + "    ", identifiers, integers);
            source.Append(indent).AppendLine("}");
        }
    }
}
