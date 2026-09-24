namespace SMILE.Engine;

public sealed partial class SmileEvaluator
{
    private static SmileRuntimeError NothingReferenceError() => new("SMILER3457", "Object reference is Nothing.");

    private bool TryNew(BoundNewExpression creation, CallFrame? caller, out SmileValue value, out SmileRuntimeError? error)
    {
        value = default;
        if (!TryCaptureCallArguments(creation.Class.Constructor, creation.Arguments, creation.ParameterOrder,
            caller, false, out IReadOnlyList<CallArgument> arguments, out error)) return false;
        SmileValue reference = SmileValue.FromClass(creation.Class, new SmileClassValue(creation.Class));
        if (!TryInvoke(creation.Class.Constructor, new[] { new CallArgument(reference) }.Concat(arguments).ToArray(), out _, out error)) return false;
        value = reference;
        return true;
    }
}
