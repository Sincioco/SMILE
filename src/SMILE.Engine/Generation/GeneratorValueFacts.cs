namespace SMILE.Engine;

internal static class GeneratorValueFacts
{
    public static SmileValue Evaluate(
        BoundExpression expression,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values)
    {
        if (BoundExpressionEvaluator.TryEvaluate(expression, values, out SmileValue value))
        {
            return value;
        }

        throw new InvalidOperationException("Bound expression could not be evaluated for target lowering.");
    }

    public static bool AssignedValuesContainNul(
        BoundProgramAnalysis analysis,
        VariableSymbol variable) =>
        analysis.AssignedValuesMayContainNul(variable);

    public static int MaximumAssignedUtf8ByteLength(
        BoundProgramAnalysis analysis,
        VariableSymbol variable) =>
        analysis.MaximumAssignedUtf8ByteLength(variable);

    public static bool TryGetNulContainingString(
        BoundExpression expression,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values,
        out string value)
    {
        if (BoundExpressionEvaluator.TryEvaluate(expression, values, out SmileValue evaluated) &&
            evaluated.Type is SmileType.String &&
            evaluated.StringValue.Contains('\0', StringComparison.Ordinal))
        {
            value = evaluated.StringValue;
            return true;
        }

        value = string.Empty;
        return false;
    }

    public static string DisplayText(
        BoundExpression expression,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values)
    {
        return Evaluate(expression, values).ToDisplayText();
    }
}
