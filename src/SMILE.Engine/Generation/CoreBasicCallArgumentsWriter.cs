namespace SMILE.Engine;

internal static partial class CoreBasicCodeGenerator
{
    private sealed partial class StructuredWriter
    {
        private IReadOnlyList<string> PrepareCallArguments(
            IReadOnlyList<BoundExpression> arguments, IReadOnlyList<int>? parameterOrder, bool ordered = false)
        {
            var captured = new List<string>();
            foreach (BoundExpression argument in arguments)
            {
                string value = ordered || parameterOrder is not null
                    ? LowerOrderedCExpression(argument)
                    : PreparedExpression(argument);
                if (parameterOrder is not null && argument is BoundVariableExpression)
                {
                    value = NewOrderedValue(argument.Type, value);
                }
                captured.Add(value);
            }
            return RoutineArguments.InParameterOrder(captured, parameterOrder);
        }
    }
}
