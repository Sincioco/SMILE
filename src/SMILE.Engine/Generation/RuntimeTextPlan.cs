namespace SMILE.Engine;

internal abstract record RuntimeTextSegment;

internal sealed record RuntimeLiteralTextSegment(string Text) : RuntimeTextSegment;

internal sealed record RuntimeExpressionTextSegment(BoundExpression Expression) : RuntimeTextSegment;

internal static class RuntimeTextPlan
{
    public static IReadOnlyList<RuntimeTextSegment> Flatten(BoundExpression expression)
    {
        var segments = new List<RuntimeTextSegment>();
        Append(expression, segments);
        return segments;
    }

    public static bool CanFlatten(BoundExpression expression) =>
        expression switch
        {
            BoundStringLiteralExpression => true,
            BoundVariableExpression => true,
            BoundBinaryExpression { Operator.Kind: BoundBinaryOperatorKind.StringConcatenation } binary =>
                CanFlatten(binary.Left) && CanFlatten(binary.Right),
            BoundInterpolatedStringExpression interpolated => interpolated.Parts.All(part => part switch
            {
                BoundInterpolatedTextPart => true,
                BoundInterpolationExpressionPart => true,
                _ => false
            }),
            _ when expression.Type is not SmileType.String => true,
            _ => false
        };

    private static void Append(
        BoundExpression expression,
        List<RuntimeTextSegment> segments)
    {
        switch (expression)
        {
            case BoundStringLiteralExpression literal:
                AppendLiteral(segments, literal.Value);
                break;

            case BoundVariableExpression:
                segments.Add(new RuntimeExpressionTextSegment(expression));
                break;

            case BoundBinaryExpression { Operator.Kind: BoundBinaryOperatorKind.StringConcatenation } binary:
                Append(binary.Left, segments);
                Append(binary.Right, segments);
                break;

            case BoundInterpolatedStringExpression interpolated:
                foreach (BoundInterpolatedPart part in interpolated.Parts)
                {
                    switch (part)
                    {
                        case BoundInterpolatedTextPart text:
                            AppendLiteral(segments, text.Text);
                            break;

                        case BoundInterpolationExpressionPart hole:
                            Append(hole.Expression, segments);
                            break;
                    }
                }

                break;

            default:
                segments.Add(new RuntimeExpressionTextSegment(expression));
                break;
        }
    }

    private static void AppendLiteral(
        List<RuntimeTextSegment> segments,
        string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        if (segments.LastOrDefault() is RuntimeLiteralTextSegment previous)
        {
            segments[^1] = previous with { Text = previous.Text + text };
        }
        else
        {
            segments.Add(new RuntimeLiteralTextSegment(text));
        }
    }
}
