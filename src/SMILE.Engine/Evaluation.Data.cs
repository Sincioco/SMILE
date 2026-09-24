namespace SMILE.Engine;

public sealed partial class SmileEvaluator
{
    private SmileRuntimeError? LoadData(BoundDataLoadStatement load, CallFrame? frame)
    {
        if (!TryEvaluateExpression(load.Key, frame, out SmileValue key, out SmileRuntimeError? error)) return error;
        SmileValue[] array = GetArray(load.Destination, frame);
        long[] bytes = array.Select(value => value.IntegerValue).ToArray();
        SmileDataResult result = _storage.LoadData(key.StringValue, bytes, recover: load.Status is not null);
        // Strict loads clear the destination even when the operation fails. Checked
        // loads preserve it on failure and leave cells after Count untouched.
        for (int index = 0; index < bytes.Length; index++) array[index] = SmileValue.FromInteger(bytes[index]);
        if (load.Status is null && result.Status is not (SmileDataStatus.Ok or SmileDataStatus.Missing))
            return new SmileRuntimeError("SMILER3506", "Load Data encountered an invalid destination, corrupt block, oversized block, or unavailable storage.");
        if (!WriteDataTarget(load.Count, result.Count, frame, out error)) return error;
        if (load.Status is not null && !WriteDataTarget(load.Status, (long)result.Status, frame, out error)) return error;
        return null;
    }

    private SmileRuntimeError? SaveData(BoundDataSaveStatement save, CallFrame? frame)
    {
        if (!TryEvaluateExpression(save.Count, frame, out SmileValue count, out SmileRuntimeError? error)) return error;
        if (!TryEvaluateExpression(save.Key, frame, out SmileValue key, out error)) return error;
        long[] bytes = GetArray(save.Source, frame).Select(value => value.IntegerValue).ToArray();
        SmileDataStatus status = _storage.SaveData(bytes, count.IntegerValue, key.StringValue, recover: save.Status is not null);
        if (save.Status is null && status != SmileDataStatus.Ok)
            return new SmileRuntimeError("SMILER3506", "Save Data received invalid bytes/count or could not atomically store the block.");
        if (save.Status is not null && !WriteDataTarget(save.Status, (long)status, frame, out error)) return error;
        return null;
    }

    private bool WriteDataTarget(BoundExpression target, long value, CallFrame? frame, out SmileRuntimeError? error)
    {
        if (!TryCaptureLocation(target, frame, out WritableLocation? location, out error)) return false;
        location!.Write(SmileValue.FromInteger(value));
        return true;
    }
}
