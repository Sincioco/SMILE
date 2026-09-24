namespace SMILE.Engine;

public sealed partial class SmileEvaluator
{
    private readonly Dictionary<WithLocationSymbol, WritableLocation> _withLocations = new();

    private SmileRuntimeError? ExecuteWith(BoundWithStatement block, CallFrame? frame)
    {
        if (!TryCaptureLocation(block.Location.Target, frame, out WritableLocation? location, out SmileRuntimeError? error)) return error;
        // Recursion may enter the same source block while its caller is suspended.
        _withLocations.TryGetValue(block.Location, out WritableLocation? previous);
        _withLocations[block.Location] = location!;
        try { return ExecuteStatements(block.SourceItems.OfType<BoundStatement>().ToArray(), frame); }
        finally
        {
            if (previous is null) _withLocations.Remove(block.Location);
            else _withLocations[block.Location] = previous;
        }
    }

    private bool TryCaptureField(BoundFieldExpression expression, CallFrame? frame,
        out WritableLocation? location, out SmileRuntimeError? error)
    {
        location = null;
        if (!TryEvaluateExpression(expression.Receiver, frame, out SmileValue receiver, out error)) return false;
        SmileValue[] cells = receiver.RecordValue.Fields[expression.Field.Ordinal];
        int offset = 0;
        for (int dimension = 0; dimension < expression.Indices.Count; dimension++)
        {
            if (!TryEvaluateExpression(expression.Indices[dimension], frame, out SmileValue index, out error)) return false;
            int length = expression.Field.Dimensions[dimension];
            if (index.IntegerValue < 0 || index.IntegerValue >= length)
            {
                error = new SmileRuntimeError("SMILER1210", $"Array index {index.IntegerValue} is outside field '{expression.Field.Name}' dimension {dimension + 1}.");
                return false;
            }
            offset = checked(offset * length + (int)index.IntegerValue);
        }
        location = new WritableLocation(() => cells[offset], value => cells[offset] = SmileRecordValue.Store(cells[offset], value));
        return Success(out error);
    }
}
