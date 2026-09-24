namespace SMILE.Engine;

internal static partial class CoreBasicCodeGenerator
{
    private sealed partial class StructuredWriter
    {
        private IReadOnlyList<string> PrepareCallArguments(
            IReadOnlyList<BoundExpression> arguments, IReadOnlyList<int>? parameterOrder, bool ordered = false, RoutineSymbol? routine = null)
        {
            var captured = new List<string>();
            bool captureOrder = ordered || parameterOrder is not null || UsesOrderedCExpressions ||
                routine?.ExecutionParameters.Any(parameter => parameter.IsByRef) == true || arguments.Any(ContainsArrayAccess);
            for (int index = 0; index < arguments.Count; index++)
            {
                BoundExpression argument = arguments[index];
                VariableSymbol? parameter = routine is null ? null : RoutineArguments.ParameterAtSourceIndex(routine, parameterOrder, index);
                if (parameter?.IsByRef == true)
                {
                    if (parameter.IsReceiver && routine is not null && HasImplicitReceiver(routine))
                    {
                        captured.Add(PrepareMemberReceiver(argument));
                        continue;
                    }
                    captured.Add(PrepareReferenceArgument(argument, parameter));
                    continue;
                }
                string value = captureOrder
                    ? LowerOrderedCExpression(argument)
                    : PreparedExpression(argument);
                if (argument.Type is RecordTypeSymbol)
                    value = NewOrderedValue(argument.Type, RecordCopy(argument.Type, value));
                else if (argument is BoundVariableExpression && (parameterOrder is not null ||
                    captureOrder && arguments.Skip(index + 1).Any(ContainsRoutineCall)))
                {
                    value = NewOrderedValue(argument.Type, value);
                }
                captured.Add(value);
            }
            return RoutineArguments.InParameterOrder(captured, parameterOrder);
        }

    }

    internal static bool ContainsRoutineCall(BoundExpression expression) => expression switch
    {
        BoundCallExpression => true,
        BoundIntrinsicExpression intrinsic => intrinsic.Arguments.Any(ContainsRoutineCall),
        BoundArrayExpression array => array.Indices.Any(ContainsRoutineCall),
        BoundFieldExpression field => ContainsRoutineCall(field.Receiver) || field.Indices.Any(ContainsRoutineCall),
        BoundUnaryExpression unary => ContainsRoutineCall(unary.Operand),
        BoundBinaryExpression binary => ContainsRoutineCall(binary.Left) || ContainsRoutineCall(binary.Right),
        _ => false
    };
}
