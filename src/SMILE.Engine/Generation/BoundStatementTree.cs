namespace SMILE.Engine;

internal static class BoundStatementTree
{
    public static IEnumerable<BoundStatement> Enumerate(BoundProgram program) =>
        Enumerate(program.Statements);

    public static IEnumerable<BoundStatement> Enumerate(
        IReadOnlyList<BoundStatement> statements)
    {
        foreach (BoundStatement statement in statements)
        {
            yield return statement;

            if (statement is not BoundIfStatement conditional)
            {
                continue;
            }

            foreach (BoundConditionalClause clause in conditional.Clauses)
            {
                foreach (BoundStatement nested in Enumerate(clause.Statements))
                {
                    yield return nested;
                }
            }

            foreach (BoundStatement nested in Enumerate(conditional.ElseStatements))
            {
                yield return nested;
            }
        }
    }

    public static IEnumerable<BoundExpression> EnumerateExpressions(BoundProgram program)
    {
        foreach (BoundStatement statement in Enumerate(program))
        {
            switch (statement)
            {
                case BoundLetStatement let:
                    yield return let.Initializer;
                    break;

                case BoundSetStatement set:
                    yield return set.Value;
                    break;

                case BoundPrintStatement print when !print.IsBlankLine:
                    yield return print.Value;
                    break;

                case BoundIfStatement conditional:
                    foreach (BoundConditionalClause clause in conditional.Clauses)
                    {
                        yield return clause.Condition;
                    }

                    break;
            }
        }
    }
}
