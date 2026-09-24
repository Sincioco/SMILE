namespace SMILE.Engine;

public sealed partial class SmileEvaluator
{
    // A reference owns a captured location, never a copy to write back after a call.
    // This also makes forwarding an existing ByRef parameter preserve its identity.
    private sealed record WritableLocation(Func<SmileValue> Read, Action<SmileValue> Write);
    private readonly record struct CallArgument(SmileValue Value, WritableLocation? Location = null);

    private bool TryInvokeCall(RoutineSymbol routine, IReadOnlyList<BoundExpression> expressions,
        IReadOnlyList<int>? order, CallFrame? caller, out SmileValue value, out SmileRuntimeError? error)
    {
        var arguments = new List<CallArgument>();
        value = default;
        for (int index = 0; index < expressions.Count; index++)
        {
            BoundExpression expression = expressions[index];
            if (RoutineArguments.ParameterAtSourceIndex(routine, order, index).IsByRef)
            {
                if (!TryCaptureLocation(expression, caller, out WritableLocation? location, out error)) return false;
                arguments.Add(new CallArgument(default, location));
            }
            else
            {
                if (!TryEvaluateExpression(expression, caller, out SmileValue argument, out error)) return false;
                arguments.Add(new CallArgument(SmileRecordValue.Copy(argument)));
            }
        }
        return TryInvoke(routine, RoutineArguments.InParameterOrder(arguments, order), out value, out error);
    }

    private bool TryCaptureLocation(BoundExpression expression, CallFrame? frame,
        out WritableLocation? location, out SmileRuntimeError? error)
    {
        location = null;
        if (expression is BoundFieldExpression field) return TryCaptureField(field, frame, out location, out error);
        if (expression is BoundVariableExpression scalar)
        {
            VariableSymbol variable = scalar.Variable;
            location = variable.IsByRef ? frame!.References[variable]
                : new WritableLocation(() => GetValue(variable, frame), value => SetValue(variable, frame, value));
            return Success(out error);
        }

        var element = (BoundArrayExpression)expression;
        long[] indices = new long[element.Indices.Count];
        for (int dimension = 0; dimension < indices.Length; dimension++)
        {
            if (!TryEvaluateExpression(element.Indices[dimension], frame, out SmileValue index, out error)) return false;
            int length = dimension == 0 ? element.Array.ArrayLength : element.Array.ArraySecondLength;
            if (index.IntegerValue < 0 || index.IntegerValue >= length)
            {
                error = new SmileRuntimeError("SMILER1210",
                    $"Array index {index.IntegerValue} for dimension {dimension + 1} is outside the valid range 0 through {length - 1} for '{element.Array.Name}'.");
                return false;
            }
            indices[dimension] = index.IntegerValue;
        }
        if (!TryGetArrayElement(element.Array, indices, frame, out SmileValue[]? array, out int offset, out error)) return false;
        location = new WritableLocation(() => array![offset], value => array![offset] = SmileRecordValue.Store(array[offset], value));
        return true;
    }
}
