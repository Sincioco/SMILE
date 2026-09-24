namespace SMILE.Engine;

internal sealed partial class CoreBasicMasmWriter
{
    private sealed partial class ProcedureEmitter
    {
        private void EmitReferenceLocation(BoundExpression expression, int indent)
        {
            if (expression is BoundFieldExpression field) { EmitFieldLocation(field, indent); return; }
            if (expression is BoundVariableExpression scalar)
            {
                if (_storage.TryGetValue(scalar.Variable, out Storage? storage))
                    Emit(indent, IndirectVariable(scalar.Variable)
                        ? $"mov rax, QWORD PTR {Address(storage.Offset)}"
                        : $"lea rax, {Address(storage.Offset)}");
                else Emit(indent, $"lea rax, {_owner.Name(scalar.Variable)}");
                return;
            }
            var element = (BoundArrayExpression)expression;
            Storage offset = EmitArrayOffset(element.Array, element.Indices, indent, checkEachDimension: true);
            EmitArrayBase(element.Array, "r10", indent);
            Emit(indent, $"mov rax, QWORD PTR {Address(offset.Offset)}");
            Emit(indent, $"imul rax, {NativeValueSize(element.Type)}");
            Emit(indent, "add rax, r10");
        }
    }
}
