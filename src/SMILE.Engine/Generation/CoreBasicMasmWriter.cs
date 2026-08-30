using System.Globalization;
using System.Text;

namespace SMILE.Engine;

// Native Windows x64 lowering. Every routine owns an rbp-based frame; caller
// shadow space and stack arguments follow the Microsoft x64 ABI, so recursive
// calls and parameter counts beyond four remain ordinary native calls.
internal sealed class CoreBasicMasmWriter
{
    private readonly BoundProgram _program;
    private readonly TargetIdentifierMap _identifiers;
    private readonly IReadOnlyDictionary<VariableSymbol, SmileValue> _constants;
    private readonly Dictionary<string, string> _strings = new(StringComparer.Ordinal);
    private readonly StringBuilder _builder = new();
    private bool _usesBounds;
    private bool _usesMalloc;
    private bool _usesPrintf;
    private bool _usesSprintf;
    private bool _usesStrcmp;
    private int _labelId;

    public CoreBasicMasmWriter(BoundProgram program)
    {
        _program = program;
        _identifiers = TargetIdentifierMap.Create(program, TargetLanguage.MasmX64);
        _constants = EnumerateStatements(program.SourceItems)
            .OfType<BoundConstStatement>()
            .ToDictionary(statement => statement.Variable, statement => statement.Value);
        if (_program.Variables.Any(variable =>
            !variable.IsConstant && variable.Type is SmileType.String))
        {
            InternString(string.Empty);
        }
    }

    public string Write()
    {
        ProcedureEmitter[] routines = _program.Routines
            .Select(routine => new ProcedureEmitter(this, routine))
            .ToArray();
        foreach (ProcedureEmitter routine in routines)
        {
            routine.Generate();
        }

        var main = new ProcedureEmitter(this, _program.SourceItems);
        main.Generate();

        string? boundsMessage = _usesBounds
            ? InternString("SMILE Runtime Error SMILER1210: Array index is outside the declared bounds.\n")
            : null;

        Line("option casemap:none");
        Line("ExitProcess PROTO :DWORD");
        if (_usesPrintf) Line("printf PROTO :PTR BYTE, :VARARG");
        if (_usesStrcmp) Line("strcmp PROTO :PTR BYTE, :PTR BYTE");
        if (_usesSprintf) Line("sprintf PROTO :PTR BYTE, :PTR BYTE, :VARARG");
        if (_usesMalloc) Line("malloc PROTO :QWORD");
        Line("includelib kernel32.lib");
        if (_usesPrintf || _usesStrcmp || _usesSprintf || _usesMalloc)
        {
            Line("includelib msvcrt.lib");
        }
        Line();
        Line(".const");
        foreach ((string value, string label) in _strings)
        {
            Line($"{label} BYTE {TargetEscapes.MasmByteInitializers(value)}, 0");
        }

        Line();
        Line(".data");
        foreach (VariableSymbol variable in _program.Variables.Where(item => !item.IsConstant))
        {
            string name = Name(variable);
            if (variable.IsArray)
            {
                string value = variable.Type is SmileType.String
                    ? $"OFFSET {InternString(string.Empty)}"
                    : "0";
                Line($"{name} QWORD {variable.ArrayLength} DUP({value})");
            }
            else
            {
                string value = variable.Type is SmileType.String
                    ? $"OFFSET {InternString(string.Empty)}"
                    : "0";
                Line($"{name} QWORD {value}");
            }
        }

        Line();
        Line(".code");
        if (_usesBounds)
        {
            Line("smile_bounds_fail PROC");
            Line("    sub rsp, 40");
            Line($"    lea rcx, {boundsMessage}");
            Line("    call printf");
            Line("    mov ecx, 1");
            Line("    call ExitProcess");
            Line("smile_bounds_fail ENDP");
            Line();
        }

        foreach (ProcedureEmitter routine in routines)
        {
            AppendProcedure(routine);
            Line();
        }

        AppendProcedure(main);
        Line("END");
        return _builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private void AppendProcedure(ProcedureEmitter procedure)
    {
        string name = procedure.IsMain ? "main" : RoutineName(procedure.Routine!.Symbol);
        Line($"{name} PROC");
        Line("    push rbp");
        Line("    mov rbp, rsp");
        Line($"    sub rsp, {procedure.FrameSize}");
        _builder.Append(procedure.Initialization);
        _builder.Append(procedure.Body);
        if (procedure.IsMain)
        {
            Line("smile_program_end:");
            Line("    xor ecx, ecx");
            Line("    call ExitProcess");
        }
        else
        {
            Line(procedure.ReturnLabel + ":");
            Line("    mov rsp, rbp");
            Line("    pop rbp");
            Line("    ret");
        }

        Line($"{name} ENDP");
    }

    private string Name(VariableSymbol variable) => _identifiers.Get(variable);

    private string RoutineName(RoutineSymbol routine) => _identifiers.Get(routine);

    private string InternString(string value)
    {
        if (_strings.TryGetValue(value, out string? label))
        {
            return label;
        }

        label = $"smileText{_strings.Count}";
        _strings.Add(value, label);
        return label;
    }

    private string ConstantOperand(VariableSymbol variable)
    {
        SmileValue value = _constants[variable];
        return value.Type switch
        {
            SmileType.Integer => value.IntegerValue.ToString(CultureInfo.InvariantCulture),
            SmileType.Boolean => value.BooleanValue ? "1" : "0",
            _ => $"OFFSET {InternString(value.StringValue)}"
        };
    }

    private static IEnumerable<BoundStatement> EnumerateStatements(IReadOnlyList<BoundSourceItem> items)
    {
        foreach (BoundSourceItem item in items)
        {
            if (item is not BoundStatement statement)
            {
                continue;
            }

            yield return statement;
            switch (statement)
            {
                case BoundIfStatement conditional:
                    foreach (BoundConditionalClause clause in conditional.Clauses)
                    {
                        foreach (BoundStatement nested in EnumerateStatements(clause.SourceItems)) yield return nested;
                    }

                    foreach (BoundStatement nested in EnumerateStatements(conditional.ElseSourceItems)) yield return nested;
                    break;
                case BoundSelectStatement select:
                    foreach (BoundSelectCaseClause clause in select.Cases)
                    {
                        foreach (BoundStatement nested in EnumerateStatements(clause.SourceItems)) yield return nested;
                    }

                    break;
                case BoundForStatement loop:
                    foreach (BoundStatement nested in EnumerateStatements(loop.SourceItems)) yield return nested;
                    break;
                case BoundDoStatement loop:
                    foreach (BoundStatement nested in EnumerateStatements(loop.SourceItems)) yield return nested;
                    break;
            }
        }
    }

    private void Line(string text = "") => _builder.AppendLine(text);

    private sealed record Storage(int Offset, int ArrayLength = 0)
    {
        public bool IsArray => ArrayLength > 0;
    }

    private sealed record LoopFrame(BoundExitKind Kind, string EndLabel);

    private sealed class ProcedureEmitter
    {
        private static readonly string[] ParameterRegisters = { "rcx", "rdx", "r8", "r9" };

        private readonly CoreBasicMasmWriter _owner;
        private readonly IReadOnlyList<BoundSourceItem> _items;
        private readonly Dictionary<VariableSymbol, Storage> _storage = new();
        private readonly StringBuilder _initialization = new();
        private readonly StringBuilder _body = new();
        private readonly List<LoopFrame> _loops = new();
        private int _usedBytes;
        private int _maxCallArguments;

        public ProcedureEmitter(CoreBasicMasmWriter owner, BoundRoutineDeclaration routine)
        {
            _owner = owner;
            Routine = routine;
            _items = routine.SourceItems;
            ReturnLabel = $"{owner.RoutineName(routine.Symbol)}_return";
            foreach (VariableSymbol variable in routine.Locals.Distinct())
            {
                _storage[variable] = Allocate(variable.IsArray ? variable.ArrayLength * 8 : 8, variable.ArrayLength);
            }
        }

        public ProcedureEmitter(CoreBasicMasmWriter owner, IReadOnlyList<BoundSourceItem> mainItems)
        {
            _owner = owner;
            _items = mainItems;
            IsMain = true;
            ReturnLabel = "smile_program_end";
        }

        public BoundRoutineDeclaration? Routine { get; }

        public bool IsMain { get; }

        public string ReturnLabel { get; }

        public string Initialization => _initialization.ToString();

        public string Body => _body.ToString();

        public int FrameSize
        {
            get
            {
                int outgoing = Math.Max(0, _maxCallArguments - 4) * 8;
                int required = _usedBytes + 32 + outgoing;
                return Math.Max(32, (required + 15) / 16 * 16);
            }
        }

        public void Generate()
        {
            if (Routine is not null)
            {
                InitializeRoutineFrame();
            }

            WriteItems(_items, 1);
        }

        private void InitializeRoutineFrame()
        {
            RoutineSymbol symbol = Routine!.Symbol;
            // Capture ABI parameters before any array initialization uses a
            // volatile argument register as a loop counter.
            for (int index = 0; index < symbol.Parameters.Count; index++)
            {
                Storage storage = _storage[symbol.Parameters[index]];
                if (index < 4)
                {
                    Append(_initialization, 1, $"mov QWORD PTR {Address(storage.Offset)}, {ParameterRegisters[index]}");
                }
                else
                {
                    Append(_initialization, 1, $"mov rax, QWORD PTR [rbp+{48 + (index - 4) * 8}]");
                    Append(_initialization, 1, $"mov QWORD PTR {Address(storage.Offset)}, rax");
                }
            }

            foreach (VariableSymbol variable in Routine.Locals.Where(item => !item.IsParameter))
            {
                Storage storage = _storage[variable];
                if (variable.IsArray)
                {
                    string defaultOperand = variable.Type is SmileType.String
                        ? $"OFFSET {_owner.InternString(string.Empty)}"
                        : "0";
                    string loop = $"smile_init_{++_owner._labelId}";
                    Append(_initialization, 1, $"lea r10, {Address(storage.Offset)}");
                    Append(_initialization, 1, $"mov ecx, {variable.ArrayLength}");
                    Append(_initialization, 1, $"mov rax, {defaultOperand}");
                    _initialization.AppendLine(loop + ":");
                    Append(_initialization, 1, "mov QWORD PTR [r10], rax");
                    Append(_initialization, 1, "add r10, 8");
                    Append(_initialization, 1, "dec ecx");
                    Append(_initialization, 1, $"jnz {loop}");
                }
                else
                {
                    string defaultOperand = variable.Type is SmileType.String
                        ? $"OFFSET {_owner.InternString(string.Empty)}"
                        : "0";
                    Append(_initialization, 1, $"mov rax, {defaultOperand}");
                    Append(_initialization, 1, $"mov QWORD PTR {Address(storage.Offset)}, rax");
                }
            }

        }

        private bool WriteItems(IReadOnlyList<BoundSourceItem> items, int indent)
        {
            foreach (BoundSourceItem item in items)
            {
                switch (item)
                {
                    case BoundBlankLine:
                        _body.AppendLine();
                        break;
                    case BoundFullLineComment comment:
                        Emit(indent, ";" + comment.Payload);
                        break;
                    case BoundDimStatement or BoundConstStatement:
                        break;
                    case BoundSetStatement set:
                        EmitExpression(set.Value, indent);
                        StoreVariable(set.Variable, "rax", indent);
                        break;
                    case BoundArraySetStatement set:
                        WriteArraySet(set, indent);
                        break;
                    case BoundCallStatement call:
                        EmitCall(call.Routine, call.Arguments, indent);
                        break;
                    case BoundReturnStatement returnStatement:
                        if (returnStatement.Value is not null)
                        {
                            EmitExpression(returnStatement.Value, indent);
                        }

                        Emit(indent, $"jmp {ReturnLabel}");
                        return true;
                    case BoundCorePrintStatement print:
                        foreach (BoundExpression value in print.Values) EmitPrint(value, indent);
                        if (!print.SuppressNewLine) EmitPrint(new BoundStringLiteralExpression("\n"), indent);
                        break;
                    case BoundIfStatement conditional:
                        WriteIf(conditional, indent);
                        break;
                    case BoundSelectStatement select:
                        WriteSelect(select, indent);
                        break;
                    case BoundForStatement loop:
                        WriteFor(loop, indent);
                        break;
                    case BoundDoStatement loop:
                        WriteDo(loop, indent);
                        break;
                    case BoundExitStatement exit:
                    {
                        LoopFrame? target = _loops.LastOrDefault(loop => loop.Kind == exit.Kind);
                        if (target is not null) Emit(indent, $"jmp {target.EndLabel}");
                        return true;
                    }
                    case BoundEndProgramStatement:
                        if (IsMain)
                        {
                            Emit(indent, "jmp smile_program_end");
                        }
                        else
                        {
                            Emit(indent, "xor ecx, ecx");
                            Emit(indent, "call ExitProcess");
                        }
                        return true;
                }
            }

            return false;
        }

        private void WriteArraySet(BoundArraySetStatement set, int indent)
        {
            EmitExpression(set.Index, indent);
            EmitBoundsCheck(set.Array, indent);
            Storage index = NewTemporary();
            Emit(indent, $"mov QWORD PTR {Address(index.Offset)}, rax");
            EmitExpression(set.Value, indent);
            Emit(indent, $"mov r10, QWORD PTR {Address(index.Offset)}");
            EmitArrayBase(set.Array, "r11", indent);
            Emit(indent, "mov QWORD PTR [r11+r10*8], rax");
        }

        private void WriteIf(BoundIfStatement conditional, int indent)
        {
            string end = NewLabel("if_end");
            for (int index = 0; index < conditional.Clauses.Count; index++)
            {
                string next = NewLabel("if_next");
                EmitExpression(conditional.Clauses[index].Condition, indent);
                Emit(indent, "test rax, rax");
                Emit(indent, $"jz {next}");
                WriteItems(conditional.Clauses[index].SourceItems, indent);
                Emit(indent, $"jmp {end}");
                Label(next);
            }

            if (conditional.HasElseClause)
            {
                WriteItems(conditional.ElseSourceItems, indent);
            }

            Label(end);
        }

        private void WriteSelect(BoundSelectStatement select, int indent)
        {
            EmitExpression(select.Selector, indent);
            Storage selector = NewTemporary();
            Emit(indent, $"mov QWORD PTR {Address(selector.Offset)}, rax");
            string end = NewLabel("select_end");
            BoundSelectCaseClause? fallback = null;
            foreach (BoundSelectCaseClause clause in select.Cases)
            {
                if (clause.IsElse)
                {
                    fallback = clause;
                    continue;
                }

                string next = NewLabel("select_next");
                SmileValue value = clause.Value!.Value;
                if (value.Type is SmileType.String)
                {
                    _owner._usesStrcmp = true;
                    Emit(indent, $"mov rcx, QWORD PTR {Address(selector.Offset)}");
                    Emit(indent, $"lea rdx, {_owner.InternString(value.StringValue)}");
                    NoteCall(2);
                    Emit(indent, "call strcmp");
                    Emit(indent, "test eax, eax");
                    Emit(indent, $"jnz {next}");
                }
                else
                {
                    Emit(indent, $"mov rax, QWORD PTR {Address(selector.Offset)}");
                    Emit(indent, $"cmp rax, {(value.Type is SmileType.Boolean ? (value.BooleanValue ? "1" : "0") : value.IntegerValue.ToString(CultureInfo.InvariantCulture))}");
                    Emit(indent, $"jne {next}");
                }

                WriteItems(clause.SourceItems, indent);
                Emit(indent, $"jmp {end}");
                Label(next);
            }

            if (fallback is not null)
            {
                WriteItems(fallback.SourceItems, indent);
            }

            Label(end);
        }

        private void WriteFor(BoundForStatement loop, int indent)
        {
            EmitExpression(loop.LowerBound, indent);
            StoreVariable(loop.Counter, "rax", indent);
            EmitExpression(loop.UpperBound, indent);
            Storage upper = NewTemporary();
            Emit(indent, $"mov QWORD PTR {Address(upper.Offset)}, rax");
            string start = NewLabel("for_start");
            string end = NewLabel("for_end");
            _loops.Add(new LoopFrame(BoundExitKind.For, end));
            Label(start);
            LoadVariable(loop.Counter, indent);
            Emit(indent, $"cmp rax, QWORD PTR {Address(upper.Offset)}");
            Emit(indent, $"{(loop.IsDescending ? "jl" : "jg")} {end}");
            WriteItems(loop.SourceItems, indent);
            LoadVariable(loop.Counter, indent);
            Emit(indent, loop.IsDescending ? "dec rax" : "inc rax");
            StoreVariable(loop.Counter, "rax", indent);
            Emit(indent, $"jmp {start}");
            Label(end);
            _loops.RemoveAt(_loops.Count - 1);
        }

        private void WriteDo(BoundDoStatement loop, int indent)
        {
            string start = NewLabel("do_start");
            string end = NewLabel("do_end");
            _loops.Add(new LoopFrame(BoundExitKind.Do, end));
            Label(start);
            WriteItems(loop.SourceItems, indent);
            if (loop.UntilCondition is null)
            {
                Emit(indent, $"jmp {start}");
            }
            else
            {
                EmitExpression(loop.UntilCondition, indent);
                Emit(indent, "test rax, rax");
                Emit(indent, $"jz {start}");
            }

            Label(end);
            _loops.RemoveAt(_loops.Count - 1);
        }

        private void EmitPrint(BoundExpression expression, int indent)
        {
            _owner._usesPrintf = true;
            EmitExpression(expression, indent);
            if (expression.Type is SmileType.Boolean)
            {
                string falseLabel = NewLabel("bool_false");
                string ready = NewLabel("bool_ready");
                Emit(indent, "test rax, rax");
                Emit(indent, $"jz {falseLabel}");
                Emit(indent, $"lea rax, {_owner.InternString("True")}");
                Emit(indent, $"jmp {ready}");
                Label(falseLabel);
                Emit(indent, $"lea rax, {_owner.InternString("False")}");
                Label(ready);
            }

            Emit(indent, "mov rdx, rax");
            Emit(indent, $"lea rcx, {_owner.InternString(expression.Type is SmileType.Integer ? "%lld" : "%s")}");
            NoteCall(2);
            Emit(indent, "call printf");
        }

        private void EmitExpression(BoundExpression expression, int indent)
        {
            switch (expression)
            {
                case BoundIntegerLiteralExpression number:
                    Emit(indent, $"mov rax, {number.Value.ToString(CultureInfo.InvariantCulture)}");
                    return;
                case BoundBooleanLiteralExpression boolean:
                    Emit(indent, $"mov rax, {(boolean.Value ? 1 : 0)}");
                    return;
                case BoundStringLiteralExpression text:
                    Emit(indent, $"lea rax, {_owner.InternString(text.Value)}");
                    return;
                case BoundVariableExpression variable:
                    LoadVariable(variable.Variable, indent);
                    return;
                case BoundArrayExpression array:
                    EmitExpression(array.Index, indent);
                    EmitBoundsCheck(array.Array, indent);
                    Emit(indent, "mov r10, rax");
                    EmitArrayBase(array.Array, "r11", indent);
                    Emit(indent, "mov rax, QWORD PTR [r11+r10*8]");
                    return;
                case BoundCallExpression call:
                    EmitCall(call.Routine, call.Arguments, indent);
                    return;
                case BoundUnaryExpression unary:
                    EmitExpression(unary.Operand, indent);
                    if (unary.Operator.Kind is BoundUnaryOperatorKind.Negation) Emit(indent, "neg rax");
                    if (unary.Operator.Kind is BoundUnaryOperatorKind.LogicalNegation) Emit(indent, "xor rax, 1");
                    return;
                case BoundBinaryExpression binary:
                    EmitBinary(binary, indent);
                    return;
                default:
                    Emit(indent, "xor eax, eax");
                    return;
            }
        }

        private void EmitBinary(BoundBinaryExpression binary, int indent)
        {
            if (binary.Operator.Kind is BoundBinaryOperatorKind.LogicalAnd or BoundBinaryOperatorKind.LogicalOr)
            {
                string done = NewLabel("logic_done");
                EmitExpression(binary.Left, indent);
                Emit(indent, "test rax, rax");
                Emit(indent, $"{(binary.Operator.Kind is BoundBinaryOperatorKind.LogicalAnd ? "jz" : "jnz")} {done}");
                EmitExpression(binary.Right, indent);
                Label(done);
                return;
            }

            EmitExpression(binary.Left, indent);
            Storage left = NewTemporary();
            Emit(indent, $"mov QWORD PTR {Address(left.Offset)}, rax");
            EmitExpression(binary.Right, indent);
            Storage right = NewTemporary();
            Emit(indent, $"mov QWORD PTR {Address(right.Offset)}, rax");
            Emit(indent, $"mov rax, QWORD PTR {Address(left.Offset)}");
            Emit(indent, $"mov r10, QWORD PTR {Address(right.Offset)}");

            if (binary.Operator.Kind is BoundBinaryOperatorKind.StringConcatenation)
            {
                _owner._usesMalloc = true;
                _owner._usesSprintf = true;
                NoteCall(1);
                Emit(indent, "mov ecx, 65536");
                Emit(indent, "call malloc");
                Storage buffer = NewTemporary();
                Emit(indent, $"mov QWORD PTR {Address(buffer.Offset)}, rax");
                Emit(indent, "mov rcx, rax");
                Emit(indent, $"lea rdx, {_owner.InternString("%s%s")}");
                Emit(indent, $"mov r8, QWORD PTR {Address(left.Offset)}");
                Emit(indent, $"mov r9, QWORD PTR {Address(right.Offset)}");
                NoteCall(4);
                Emit(indent, "call sprintf");
                Emit(indent, $"mov rax, QWORD PTR {Address(buffer.Offset)}");
                return;
            }

            if (binary.Left.Type is SmileType.String &&
                binary.Operator.Kind is BoundBinaryOperatorKind.Equality or BoundBinaryOperatorKind.Inequality)
            {
                _owner._usesStrcmp = true;
                Emit(indent, "mov rcx, rax");
                Emit(indent, "mov rdx, r10");
                NoteCall(2);
                Emit(indent, "call strcmp");
                Emit(indent, "test eax, eax");
                Emit(indent, $"{(binary.Operator.Kind is BoundBinaryOperatorKind.Equality ? "sete" : "setne")} al");
                Emit(indent, "movzx rax, al");
                return;
            }

            switch (binary.Operator.Kind)
            {
                case BoundBinaryOperatorKind.Addition: Emit(indent, "add rax, r10"); break;
                case BoundBinaryOperatorKind.Subtraction: Emit(indent, "sub rax, r10"); break;
                case BoundBinaryOperatorKind.Multiplication: Emit(indent, "imul rax, r10"); break;
                case BoundBinaryOperatorKind.Division:
                case BoundBinaryOperatorKind.Modulo:
                    Emit(indent, "cqo");
                    Emit(indent, "idiv r10");
                    if (binary.Operator.Kind is BoundBinaryOperatorKind.Modulo) Emit(indent, "mov rax, rdx");
                    break;
                default:
                    string set = binary.Operator.Kind switch
                    {
                        BoundBinaryOperatorKind.Equality => "sete",
                        BoundBinaryOperatorKind.Inequality => "setne",
                        BoundBinaryOperatorKind.Less => "setl",
                        BoundBinaryOperatorKind.LessOrEquals => "setle",
                        BoundBinaryOperatorKind.Greater => "setg",
                        BoundBinaryOperatorKind.GreaterOrEquals => "setge",
                        _ => "sete"
                    };
                    Emit(indent, "cmp rax, r10");
                    Emit(indent, $"{set} al");
                    Emit(indent, "movzx rax, al");
                    break;
            }
        }

        private void EmitCall(RoutineSymbol routine, IReadOnlyList<BoundExpression> arguments, int indent)
        {
            var captured = new List<Storage>();
            foreach (BoundExpression argument in arguments)
            {
                EmitExpression(argument, indent);
                Storage temporary = NewTemporary();
                Emit(indent, $"mov QWORD PTR {Address(temporary.Offset)}, rax");
                captured.Add(temporary);
            }

            for (int index = 0; index < captured.Count; index++)
            {
                if (index < 4)
                {
                    Emit(indent, $"mov {ParameterRegisters[index]}, QWORD PTR {Address(captured[index].Offset)}");
                }
                else
                {
                    Emit(indent, $"mov rax, QWORD PTR {Address(captured[index].Offset)}");
                    Emit(indent, $"mov QWORD PTR [rsp+{32 + (index - 4) * 8}], rax");
                }
            }

            NoteCall(arguments.Count);
            Emit(indent, $"call {_owner.RoutineName(routine)}");
        }

        private void EmitBoundsCheck(VariableSymbol array, int indent)
        {
            _owner._usesBounds = true;
            _owner._usesPrintf = true;
            string failed = NewLabel("bounds_failed");
            string okay = NewLabel("bounds_ok");
            Emit(indent, "test rax, rax");
            Emit(indent, $"js {failed}");
            Emit(indent, $"cmp rax, {array.ArrayLength}");
            Emit(indent, $"jl {okay}");
            Label(failed);
            NoteCall(0);
            Emit(indent, "call smile_bounds_fail");
            Emit(indent, $"jmp {okay}");
            Label(okay);
        }

        private void LoadVariable(VariableSymbol variable, int indent)
        {
            if (variable.IsConstant)
            {
                Emit(indent, $"mov rax, {_owner.ConstantOperand(variable)}");
            }
            else if (_storage.TryGetValue(variable, out Storage? storage))
            {
                Emit(indent, $"mov rax, QWORD PTR {Address(storage.Offset)}");
            }
            else
            {
                Emit(indent, $"mov rax, QWORD PTR [{_owner.Name(variable)}]");
            }
        }

        private void StoreVariable(VariableSymbol variable, string register, int indent)
        {
            if (_storage.TryGetValue(variable, out Storage? storage))
            {
                Emit(indent, $"mov QWORD PTR {Address(storage.Offset)}, {register}");
            }
            else
            {
                Emit(indent, $"mov QWORD PTR [{_owner.Name(variable)}], {register}");
            }
        }

        private void EmitArrayBase(VariableSymbol array, string register, int indent)
        {
            if (_storage.TryGetValue(array, out Storage? storage))
            {
                Emit(indent, $"lea {register}, {Address(storage.Offset)}");
            }
            else
            {
                Emit(indent, $"lea {register}, {_owner.Name(array)}");
            }
        }

        private Storage NewTemporary() => Allocate(8);

        private Storage Allocate(int bytes, int arrayLength = 0)
        {
            _usedBytes += bytes;
            return new Storage(-_usedBytes, arrayLength);
        }

        private void NoteCall(int arguments) => _maxCallArguments = Math.Max(_maxCallArguments, arguments);

        private string NewLabel(string purpose) => $"smile_{purpose}_{++_owner._labelId}";

        private void Label(string label) => _body.AppendLine(label + ":");

        private void Emit(int indent, string instruction) => Append(_body, indent, instruction);

        private static void Append(StringBuilder builder, int indent, string instruction)
        {
            builder.Append(' ', indent * 4);
            builder.AppendLine(instruction);
        }

        private static string Address(int offset) => offset < 0
            ? $"[rbp{offset}]"
            : $"[rbp+{offset}]";
    }
}
