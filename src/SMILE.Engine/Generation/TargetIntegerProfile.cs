namespace SMILE.Engine;

internal sealed record TargetIntegerProfile(
    bool RequiresSigned64Storage,
    bool RequiresJavaScriptBigInt)
{
    private const long JavaScriptMaxSafeInteger = 9_007_199_254_740_991L;

    public static TargetIntegerProfile Analyze(
        BoundProgram program,
        BoundProgramAnalysis analysis)
    {
        bool requiresSigned64 = false;
        bool requiresBigInt = false;

        void Observe(long value)
        {
            requiresSigned64 |= value is < int.MinValue or > int.MaxValue;
            requiresBigInt |= value is < -JavaScriptMaxSafeInteger or > JavaScriptMaxSafeInteger;
        }

        void Visit(BoundExpression expression)
        {
            // The branch-aware range is compositional: it covers every path
            // without enumerating a Cartesian product, including an unselected
            // branch and a later arithmetic intermediate fed by a merged value.
            if (expression.Type is SmileType.Integer)
            {
                AnalyzedIntegerRange range = analysis.GetPossibleIntegerRange(expression);
                Observe(range.Minimum);
                Observe(range.Maximum);
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

        foreach (BoundStatement statement in analysis.EnumerateStatements())
        {
            switch (statement)
            {
                case BoundLetStatement let:
                    Visit(let.Initializer);
                    break;

                case BoundSetStatement set:
                    Visit(set.Value);
                    break;

                case BoundPrintStatement print when !print.IsBlankLine:
                    Visit(print.Value);
                    break;

                case BoundIfStatement conditional:
                    foreach (BoundConditionalClause clause in conditional.Clauses)
                    {
                        Visit(clause.Condition);
                    }

                    break;
            }
        }

        return new TargetIntegerProfile(requiresSigned64, requiresBigInt);
    }
}
