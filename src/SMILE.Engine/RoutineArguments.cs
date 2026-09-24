namespace SMILE.Engine;

// Explicit values remain in source order. ParameterOrder is applied only after
// capture, keeping named arguments' side effects separate from native ABI order.
internal sealed record BoundArguments(IReadOnlyList<BoundExpression> Values, IReadOnlyList<int>? ParameterOrder);

internal static class RoutineArguments
{
    public static IReadOnlyList<T> InParameterOrder<T>(IReadOnlyList<T> values, IReadOnlyList<int>? order) =>
        order is null ? values : order.Select(index => values[index]).ToArray();

    public static VariableSymbol ParameterAtSourceIndex(RoutineSymbol routine, IReadOnlyList<int>? order, int index) =>
        routine.Parameters[order is null ? index : order.ToList().IndexOf(index)];

    public static BoundExpression Literal(SmileValue value) => value.Type switch
    {
        EnumTypeSymbol type => new BoundEnumExpression(type, value.IntegerValue),
        { Kind: SmileTypeKind.Double } => new BoundDoubleLiteralExpression(value.DoubleValue),
        { Kind: SmileTypeKind.String } => new BoundStringLiteralExpression(value.StringValue),
        { Kind: SmileTypeKind.Boolean } => new BoundBooleanLiteralExpression(value.BooleanValue),
        _ => new BoundIntegerLiteralExpression(value.IntegerValue)
    };
}
