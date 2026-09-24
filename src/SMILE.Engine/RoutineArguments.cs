namespace SMILE.Engine;

// Explicit values remain in source order. ParameterOrder is applied only after
// capture, keeping named arguments' side effects separate from native ABI order.
internal sealed record BoundArguments(IReadOnlyList<BoundExpression> Values, IReadOnlyList<int>? ParameterOrder);

internal static class RoutineArguments
{
    public static IReadOnlyList<T> InParameterOrder<T>(IReadOnlyList<T> values, IReadOnlyList<int>? order) =>
        order is null ? values : order.Select(index => values[index]).ToArray();

    public static BoundExpression Literal(SmileValue value) => value.Type switch
    {
        SmileType.Double => new BoundDoubleLiteralExpression(value.DoubleValue),
        SmileType.String => new BoundStringLiteralExpression(value.StringValue),
        SmileType.Boolean => new BoundBooleanLiteralExpression(value.BooleanValue),
        _ => new BoundIntegerLiteralExpression(value.IntegerValue)
    };
}
