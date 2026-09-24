namespace SMILE.Engine;

internal sealed partial class CoreBasicMasmWriter
{
    private sealed partial class ProcedureEmitter
    {
        private void WriteDataLoad(BoundDataLoadStatement load, int indent)
        {
            Storage key = CaptureDataValue(load.Key, indent);
            Emit(indent, $"mov rcx, QWORD PTR {Address(key.Offset)}");
            NoteCall(1); Emit(indent, "call strlen");
            Emit(indent, "mov rdx, rax");
            Emit(indent, $"mov rcx, QWORD PTR {Address(key.Offset)}");
            EmitArrayBase(load.Destination, "r8", indent);
            Emit(indent, $"mov r9, {load.Destination.ArrayLength}");
            Storage count = NewTemporary();
            Emit(indent, $"lea rax, {Address(count.Offset)}");
            Emit(indent, "mov QWORD PTR [rsp+32], rax");
            Emit(indent, $"mov QWORD PTR [rsp+40], {(load.Status is null ? 0 : 1)}");
            NoteCall(6); Emit(indent, "call smile_load_data");
            Storage status = NewTemporary();
            Emit(indent, $"mov QWORD PTR {Address(status.Offset)}, rax");
            WriteDataTarget(load.Count, count, indent);
            if (load.Status is not null) WriteDataTarget(load.Status, status, indent);
        }

        private void WriteDataSave(BoundDataSaveStatement save, int indent)
        {
            Storage count = CaptureDataValue(save.Count, indent);
            Storage key = CaptureDataValue(save.Key, indent);
            Emit(indent, $"mov rcx, QWORD PTR {Address(key.Offset)}");
            NoteCall(1); Emit(indent, "call strlen");
            Emit(indent, "mov QWORD PTR [rsp+32], rax");
            Emit(indent, $"mov QWORD PTR [rsp+40], {(save.Status is null ? 0 : 1)}");
            EmitArrayBase(save.Source, "rcx", indent);
            Emit(indent, $"mov rdx, {save.Source.ArrayLength}");
            Emit(indent, $"mov r8, QWORD PTR {Address(count.Offset)}");
            Emit(indent, $"mov r9, QWORD PTR {Address(key.Offset)}");
            NoteCall(6); Emit(indent, "call smile_save_data");
            if (save.Status is null) return;
            Storage status = NewTemporary();
            Emit(indent, $"mov QWORD PTR {Address(status.Offset)}, rax");
            WriteDataTarget(save.Status, status, indent);
        }

        private Storage CaptureDataValue(BoundExpression expression, int indent)
        {
            EmitExpression(expression, indent);
            Storage temporary = NewTemporary();
            Emit(indent, $"mov QWORD PTR {Address(temporary.Offset)}, rax");
            if (_owner._usesManagedText && expression.Type is { Kind: SmileTypeKind.String }) _textTemporaryRoots.Add(temporary);
            return temporary;
        }

        private void WriteDataTarget(BoundExpression expression, Storage value, int indent)
        {
            EmitReferenceLocation(expression, indent);
            Emit(indent, $"mov r10, QWORD PTR {Address(value.Offset)}");
            Emit(indent, "mov QWORD PTR [rax], r10");
        }
    }
}
