namespace SMILE.Engine;

internal static partial class CoreBasicCodeGenerator
{
    private sealed partial class StructuredWriter
    {
        private IReadOnlyList<string> PrepareCallArguments(
            IReadOnlyList<BoundExpression> arguments, IReadOnlyList<int>? parameterOrder, bool ordered = false)
        {
            var captured = new List<string>();
            bool captureOrder = ordered || parameterOrder is not null || UsesOrderedCExpressions || arguments.Any(ContainsArrayAccess);
            for (int index = 0; index < arguments.Count; index++)
            {
                BoundExpression argument = arguments[index];
                string value = captureOrder
                    ? LowerOrderedCExpression(argument)
                    : PreparedExpression(argument);
                if (argument is BoundVariableExpression && (parameterOrder is not null ||
                    captureOrder && arguments.Skip(index + 1).Any(ContainsRoutineCall)))
                {
                    value = NewOrderedValue(argument.Type, value);
                }
                captured.Add(value);
            }
            return RoutineArguments.InParameterOrder(captured, parameterOrder);
        }

        private static bool ContainsRoutineCall(BoundExpression expression) => expression switch
        {
            BoundCallExpression => true,
            BoundIntrinsicExpression intrinsic => intrinsic.Arguments.Any(ContainsRoutineCall),
            BoundArrayExpression array => array.Indices.Any(ContainsRoutineCall),
            BoundUnaryExpression unary => ContainsRoutineCall(unary.Operand),
            BoundBinaryExpression binary => ContainsRoutineCall(binary.Left) || ContainsRoutineCall(binary.Right),
            _ => false
        };
    }
}
