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
        if ((expression is BoundUnaryExpression or BoundBinaryExpression) &&
            IsProvenWithoutVariableReads(expression))
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
