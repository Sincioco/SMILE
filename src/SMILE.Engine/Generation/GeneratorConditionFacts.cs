namespace SMILE.Engine;

internal static class GeneratorConditionFacts
{
    private static readonly IReadOnlyDictionary<VariableSymbol, SmileValue> NoValues =
        new Dictionary<VariableSymbol, SmileValue>();

    public static bool IsProvenWithoutVariableReads(BoundExpression expression) =>
        TryEvaluateWithoutVariableReads(expression, out SmileValue value) &&
        value.Type is SmileType.Boolean;

    public static bool RequiresWarningSafeWrapper(BoundExpression expression)
    {
        // Shared simplification can reduce a complete source comparison to a
        // Boolean literal. Literals need the same runtime-opaque wrapper as a
        // larger proven condition: C# warns about the body of while (false),
        // while Java rejects that body as unreachable source.
        if (IsProvenWithoutVariableReads(expression))
        {
            return true;
        }

        return expression switch
        {
            BoundUnaryExpression unary => RequiresWarningSafeWrapper(unary.Operand),
            BoundBinaryExpression binary => RequiresWarningSafeWrapper(binary.Left) ||
                RequiresWarningSafeWrapper(binary.Right),
            _ => false
        };
    }

    public static bool ContainsVariableRead(BoundExpression expression) =>
        expression switch
        {
            BoundVariableExpression => true,
            BoundUnaryExpression unary => ContainsVariableRead(unary.Operand),
            BoundBinaryExpression binary => ContainsVariableRead(binary.Left) ||
                ContainsVariableRead(binary.Right),
            BoundInterpolatedStringExpression interpolated => interpolated.Parts
                .OfType<BoundInterpolationExpressionPart>()
                .Any(part => ContainsVariableRead(part.Expression)),
            _ => false
        };

    public static bool TryEvaluateWithoutVariableReads(
        BoundExpression expression,
        out SmileValue value) =>
        BoundExpressionEvaluator.TryEvaluate(expression, NoValues, out value);

    public static bool TryEvaluateFromAnalyzedValues(
        BoundExpression expression,
        IReadOnlyDictionary<VariableSymbol, AnalyzedValue> analyzedValues,
        out SmileValue value)
    {
        var knownValues = new Dictionary<VariableSymbol, SmileValue>();
        foreach ((VariableSymbol variable, AnalyzedValue analyzed) in analyzedValues)
        {
            if (analyzed.IsKnown)
            {
                knownValues.Add(variable, analyzed.Value);
            }
        }

        return BoundExpressionEvaluator.TryEvaluate(expression, knownValues, out value);
    }

    public static IReadOnlyDictionary<VariableSymbol, SmileValue> KnownValues(
        IReadOnlyDictionary<VariableSymbol, AnalyzedValue> analyzedValues) =>
        analyzedValues
            .Where(pair => pair.Value.IsKnown)
            .ToDictionary(pair => pair.Key, pair => pair.Value.Value);

}
