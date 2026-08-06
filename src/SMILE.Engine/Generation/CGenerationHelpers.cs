using System.Text;

namespace SMILE.Engine;

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
        EnumerateRootExpressions(program).Any(ContainsBooleanLiteral);

    public static bool NeedsStringComparison(BoundProgramAnalysis analysis)
    {
        foreach (BoundStatement statement in analysis.EnumerateStatements())
        {
            BoundStatementAnalysis facts = analysis.GetStatementFacts(statement);
            bool needsComparison = statement switch
            {
                BoundLetStatement let when
                    let.Variable.Type is not SmileType.String || !facts.Value.IsKnown =>
                    ContainsStringComparison(let.Initializer),
                BoundSetStatement set when
                    set.Variable.Type is not SmileType.String || !facts.Value.IsKnown =>
                    ContainsStringComparison(set.Value),
                BoundPrintStatement { IsBlankLine: false } print =>
                    ContainsStringComparison(print.Value),
                BoundIfStatement conditional => conditional.Clauses.Any(clause =>
                    ContainsStringComparison(clause.Condition)),
                _ => false
            };

            if (needsComparison)
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<BoundExpression> EnumerateRootExpressions(BoundProgram program)
    {
        foreach (BoundStatement statement in BoundStatementTree.Enumerate(program))
        {
            switch (statement)
            {
                case BoundLetStatement let:
                    yield return let.Initializer;
                    break;

                case BoundSetStatement set:
                    yield return set.Value;
                    break;

                case BoundPrintStatement { IsBlankLine: false } print:
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

    private static bool ContainsStringComparison(BoundExpression expression) =>
        expression switch
        {
            BoundBinaryExpression binary =>
                binary.Left.Type is SmileType.String &&
                    binary.Operator.Kind is BoundBinaryOperatorKind.Equality or
                        BoundBinaryOperatorKind.Inequality ||
                ContainsStringComparison(binary.Left) ||
                ContainsStringComparison(binary.Right),
            BoundUnaryExpression unary =>
                ContainsStringComparison(unary.Operand),
            BoundInterpolatedStringExpression interpolated => interpolated.Parts.Any(part =>
                part is BoundInterpolationExpressionPart interpolation &&
                ContainsStringComparison(interpolation.Expression)),
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

}
