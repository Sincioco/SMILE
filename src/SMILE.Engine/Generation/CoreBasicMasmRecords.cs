using System.Text;

namespace SMILE.Engine;

internal sealed partial class CoreBasicMasmWriter
{
    private static int NativeValueSize(SmileType type) => type is RecordTypeSymbol record ? record.NativeSize : 8;
    private static bool IndirectRecord(SmileType? type) => type is RecordTypeSymbol { NativeSize: > 8 };
    private static bool IndirectVariable(VariableSymbol variable) => variable.IsByRef || variable.IsParameter && IndirectRecord(variable.Type);

    private void WriteRecordDeclarations()
    {
        foreach (RecordTypeSymbol type in _program.RecordTypes)
        {
            string name = _identifiers.Get(type);
            Line(name + " STRUCT");
            foreach (InstanceFieldSymbol field in type.Fields)
            {
                string fieldType = field.Type is RecordTypeSymbol nested ? _identifiers.Get(nested) : "QWORD";
                string value = field.Type is RecordTypeSymbol ? "<>" : "0";
                Line($"    {_identifiers.Get(field)} {fieldType} {(field.IsArray ? field.ElementCount + " DUP(" + value + ")" : value)}");
            }
            if (type.Fields.Count == 0) Line("    _smileEmpty QWORD 0");
            Line(name + " ENDS");
        }
    }

    private sealed partial class ProcedureEmitter
    {
        private readonly Dictionary<WithLocationSymbol, Storage> _withLocations = new();

        private bool WriteWith(BoundWithStatement block, int indent)
        {
            if (block.Location.Target.Type is ClassTypeSymbol) { EmitExpression(block.Location.Target, indent); EmitRequireClass(indent); }
            else EmitReferenceLocation(block.Location.Target, indent);
            Storage location = NewTemporary();
            if (block.Location.Target.Type is ClassTypeSymbol) _objectTemporaries.Add(location);
            _withLocations[block.Location] = location;
            Emit(indent, $"mov QWORD PTR {Address(location.Offset)}, rax");
            return WriteItems(block.SourceItems, indent);
        }

        private readonly List<(Storage Storage, RecordTypeSymbol Type)> _recordTemporaries = new();

        private Storage NewRecordTemporary(RecordTypeSymbol type)
        {
            Storage storage = Allocate(type.NativeSize);
            _recordTemporaries.Add((storage, type));
            return storage;
        }

        private void EmitRecordCopy(RecordTypeSymbol type, int indent)
        {
            // rax is the source and rcx the destination; structs occupy contiguous QWORDs.
            Emit(indent, "mov r10, rax");
            Emit(indent, "mov r11, rcx");
            Emit(indent, $"mov ecx, {type.NativeSize / 8}");
            string loop = NewLabel("record_copy");
            Label(loop);
            Emit(indent, "mov rax, QWORD PTR [r10]");
            Emit(indent, "mov QWORD PTR [r11], rax");
            Emit(indent, "add r10, 8");
            Emit(indent, "add r11, 8");
            Emit(indent, "dec ecx");
            Emit(indent, $"jnz {loop}");
        }

        private void WriteRecordSet(BoundExpression target, BoundExpression value, int indent)
        {
            EmitReferenceLocation(target, indent);
            Storage location = NewTemporary();
            Emit(indent, $"mov QWORD PTR {Address(location.Offset)}, rax");
            EmitExpression(value, indent);
            Emit(indent, $"mov rcx, QWORD PTR {Address(location.Offset)}");
            if (target.Type is RecordTypeSymbol record) EmitRecordCopy(record, indent);
            else Emit(indent, "mov QWORD PTR [rcx], rax");
        }

        private void EmitFieldLocation(BoundFieldExpression field, int indent)
        {
            EmitExpression(field.Receiver, indent);
            if (field.Receiver.Type is ClassTypeSymbol) { EmitRequireClass(indent); CaptureClassValue(indent); }
            Emit(indent, $"add rax, {field.Field.NativeOffset}");
            if (field.Indices.Count == 0) return;
            Storage location = NewTemporary();
            Emit(indent, $"mov QWORD PTR {Address(location.Offset)}, rax");
            for (int dimension = 0; dimension < field.Indices.Count; dimension++)
            {
                EmitExpression(field.Indices[dimension], indent);
                EmitBoundsCheck(field.Field.Dimensions[dimension], indent);
                int stride = NativeValueSize(field.Type) * (dimension == 0 && field.Indices.Count == 2 ? field.Field.Dimensions[1] : 1);
                Emit(indent, $"imul rax, {stride}");
                Emit(indent, $"add QWORD PTR {Address(location.Offset)}, rax");
            }
            Emit(indent, $"mov rax, QWORD PTR {Address(location.Offset)}");
        }

        private void EmitRecordReturn(BoundExpression value, int indent)
        {
            EmitExpression(value, indent);
            if (IndirectRecord(value.Type))
            {
                Emit(indent, $"mov rcx, QWORD PTR {Address(_returnStorage!.Offset)}");
                EmitRecordCopy((RecordTypeSymbol)value.Type, indent);
            }
            else
            {
                Emit(indent, "mov rax, QWORD PTR [rax]");
                Emit(indent, $"mov QWORD PTR {Address(_returnStorage!.Offset)}, rax");
                if (_owner._usesManagedText && ((RecordTypeSymbol)value.Type).ContainsText)
                {
                    Emit(indent, "mov rcx, rax");
                    NoteCall(1);
                    Emit(indent, "call smile_text_set_return_root");
                }
            }
        }

        private void BuildRecordStorage()
        {
            IEnumerable<VariableSymbol> variables = IsMain ? _owner._program.Variables : Routine!.Locals.Distinct();
            foreach (VariableSymbol variable in variables.Where(variable => !variable.IsParameter && variable.Type is RecordTypeSymbol))
            {
                string address = IsMain ? _owner.Name(variable) : Address(_storage[variable].Offset);
                InitializeRecordStorage(address, (RecordTypeSymbol)variable.Type, variable.IsArray ? variable.TotalElementCount : 1);
            }
            foreach (var temporary in _recordTemporaries)
                InitializeRecordStorage(Address(temporary.Storage.Offset), temporary.Type, 1);
        }

        private void InitializeRecordStorage(string address, RecordTypeSymbol type, int count)
        {
            Append(_initialization, 1, $"lea r10, {address}");
            Append(_initialization, 1, $"mov ecx, {type.NativeSize / 8 * count}");
            string loop = NewLabel("record_zero");
            _initialization.AppendLine(loop + ":");
            Append(_initialization, 1, "mov QWORD PTR [r10], 0");
            Append(_initialization, 1, "add r10, 8");
            Append(_initialization, 1, "dec ecx");
            Append(_initialization, 1, $"jnz {loop}");
            WalkRecordText(_initialization, address, type, count, initialize: true, register: false);
        }

        private void BuildRecordRoots()
        {
            IEnumerable<VariableSymbol> variables = IsMain ? _owner._program.Variables : Routine!.Locals.Distinct();
            foreach (VariableSymbol variable in variables.Where(variable => !IndirectVariable(variable) && variable.Type is RecordTypeSymbol { ContainsText: true }))
            {
                string address = IsMain ? _owner.Name(variable) : Address(_storage[variable].Offset);
                WalkRecordText(_initialization, address, (RecordTypeSymbol)variable.Type, variable.IsArray ? variable.TotalElementCount : 1, false, true);
                WalkRecordText(_cleanup, address, (RecordTypeSymbol)variable.Type, variable.IsArray ? variable.TotalElementCount : 1, false, false);
            }
            foreach (var temporary in _recordTemporaries)
            {
                WalkRecordText(_initialization, Address(temporary.Storage.Offset), temporary.Type, 1, false, true);
                WalkRecordText(_cleanup, Address(temporary.Storage.Offset), temporary.Type, 1, false, false);
            }
        }

        private void WalkRecordText(StringBuilder builder, string address, RecordTypeSymbol type, int count, bool initialize, bool register)
        {
            if (!type.ContainsText) return;
            Storage pointer = NewTemporary();
            Storage remaining = NewTemporary();
            Append(builder, 1, $"lea rax, {address}");
            Append(builder, 1, $"mov QWORD PTR {Address(pointer.Offset)}, rax");
            Append(builder, 1, $"mov QWORD PTR {Address(remaining.Offset)}, {count}");
            string loop = NewLabel("record_text");
            builder.AppendLine(loop + ":");
            foreach (InstanceFieldSymbol field in type.Fields)
            {
                if (field.Type != SmileType.String && field.Type is not RecordTypeSymbol { ContainsText: true }) continue;
                Append(builder, 1, $"mov r10, QWORD PTR {Address(pointer.Offset)}");
                if (field.Type is RecordTypeSymbol nested)
                    WalkRecordText(builder, $"[r10+{field.NativeOffset}]", nested, field.ElementCount, initialize, register);
                else if (initialize)
                {
                    Append(builder, 1, $"lea r10, [r10+{field.NativeOffset}]");
                    Append(builder, 1, $"lea rax, {_owner.InternString(string.Empty)}");
                    Append(builder, 1, $"mov ecx, {field.ElementCount}");
                    string fill = NewLabel("record_text_empty");
                    builder.AppendLine(fill + ":");
                    Append(builder, 1, "mov QWORD PTR [r10], rax");
                    Append(builder, 1, "add r10, 8");
                    Append(builder, 1, "dec ecx");
                    Append(builder, 1, $"jnz {fill}");
                }
                else
                {
                    Append(builder, 1, $"lea rcx, [r10+{field.NativeOffset}]");
                    Append(builder, 1, $"mov edx, {field.ElementCount}");
                    Append(builder, 1, $"call smile_text_{(register ? "register" : "unregister")}_range");
                }
            }
            Append(builder, 1, $"add QWORD PTR {Address(pointer.Offset)}, {type.NativeSize}");
            Append(builder, 1, $"dec QWORD PTR {Address(remaining.Offset)}");
            Append(builder, 1, $"jnz {loop}");
        }
    }
}
