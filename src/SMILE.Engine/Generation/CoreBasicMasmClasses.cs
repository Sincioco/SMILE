using System.Text;

namespace SMILE.Engine;

internal sealed partial class CoreBasicMasmWriter
{
    private void WriteClassPrototypes()
    {
        if (_program.ClassTypes.Count == 0) return;
        Line("smile_object_initialize PROTO");
        Line("smile_object_shutdown PROTO");
        Line("smile_object_collect PROTO");
        Line("smile_object_register PROTO :PTR QWORD");
        Line("smile_object_unregister PROTO :PTR QWORD");
        Line("smile_object_require PROTO :PTR BYTE");
        Line("smile_object_return PROTO :PTR BYTE");
        foreach (ClassTypeSymbol type in _program.ClassTypes)
            Line(NativeClassSupport.MasmAllocator(_program, type) + " PROTO");
    }

    private sealed partial class ProcedureEmitter
    {
        private readonly List<Storage> _objectTemporaries = new();

        private void EmitRequireClass(int indent)
        {
            Emit(indent, "mov rcx, rax");
            NoteCall(1);
            Emit(indent, "call smile_object_require");
        }

        private Storage CaptureClassValue(int indent)
        {
            Storage result = NewTemporary();
            _objectTemporaries.Add(result);
            Emit(indent, $"mov QWORD PTR {Address(result.Offset)}, rax");
            return result;
        }

        private void EmitNew(BoundNewExpression creation, int indent)
        {
            NoteCall(0);
            Emit(indent, $"call {NativeClassSupport.MasmAllocator(_owner._program, creation.Class)}");
            Storage receiver = CaptureClassValue(indent);
            EmitCall(creation.Class.Constructor, creation.Arguments, indent, creation.ParameterOrder, receiver);
            Emit(indent, $"mov rax, QWORD PTR {Address(receiver.Offset)}");
        }

        private void EmitIdentity(BoundIdentityExpression identity, int indent)
        {
            EmitExpression(identity.Left, indent);
            Storage left = CaptureClassValue(indent);
            EmitExpression(identity.Right, indent);
            Emit(indent, $"cmp rax, QWORD PTR {Address(left.Offset)}");
            Emit(indent, identity.Negated ? "setne al" : "sete al");
            Emit(indent, "movzx rax, al");
        }

        private void BuildClassLifetime()
        {
            if (_owner._program.ClassTypes.Count == 0) return;
            NoteCall(1);
            if (IsMain) Append(_initialization, 1, "call smile_object_initialize");
            IEnumerable<VariableSymbol> variables = IsMain ? _owner._program.Variables : Routine!.Locals.Distinct();
            var roots = variables.Where(variable => variable.Type is ClassTypeSymbol && !variable.IsByRef)
                .Select(variable => IsMain ? _owner.Name(variable) : Address(_storage[variable].Offset)).ToList();
            foreach (Storage temporary in _objectTemporaries)
            {
                string address = Address(temporary.Offset);
                Append(_initialization, 1, $"mov QWORD PTR {address}, 0");
                roots.Add(address);
            }
            foreach (string root in roots)
            {
                Append(_initialization, 1, $"lea rcx, {root}");
                Append(_initialization, 1, "call smile_object_register");
            }
            var cleanup = new StringBuilder();
            foreach (string root in roots.AsEnumerable().Reverse())
            {
                Append(cleanup, 1, $"lea rcx, {root}");
                Append(cleanup, 1, "call smile_object_unregister");
            }
            Append(cleanup, 1, "call smile_object_collect");
            _cleanup.Insert(0, cleanup);
        }

        private void EndClassStatement(int firstTemporary, int indent)
        {
            if (_owner._program.ClassTypes.Count == 0) return;
            foreach (Storage temporary in _objectTemporaries.Skip(firstTemporary))
                Emit(indent, $"mov QWORD PTR {Address(temporary.Offset)}, 0");
            NoteCall(0);
            Emit(indent, "call smile_object_collect");
        }
    }
}
