using System.Globalization;
using System.Text;

namespace SMILE.Engine;

/// <summary>
/// Emits the beginner-facing MASM path with familiar CRT calls. Keeping this
/// target-local planner separate lets the main generator retain its uncommon
/// exact-String fallback without making that fallback the normal teaching code.
/// </summary>
internal static class MasmX64NativeGeneration
{
    public static bool TryGenerate(BoundProgram program, out GeneratedProgram? generatedProgram)
    {
        try
        {
            generatedProgram = new Writer(program).Generate();
            return true;
        }
        catch (UnsupportedNativeMasmException)
        {
            generatedProgram = null;
            return false;
        }
    }

    private sealed class Writer
    {
        private const int StringInputCapacity = 256;
        private const string InputFailureLabel = "smileInputFailure";
        private const string OverflowFailureLabel = "smileArithmeticOverflow";
        private const string DivisionFailureLabel = "smileDivisionFailure";

        private readonly BoundProgram _program;
        private readonly BoundProgramAnalysis _analysis;
        private readonly TargetIdentifierMap _identifiers;
        private readonly TargetIntegerProfile _integers;
        private readonly StringBuilder _data = new();
        private readonly StringBuilder _code = new();
        private readonly Dictionary<VariableSymbol, NativeVariable> _variables =
            new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<string, string> _sharedStrings = new(StringComparer.Ordinal);
        private int _textIndex;
        private int _inputIndex;
        private int _stringCopyIndex;
        private int _labelIndex;
        private bool _usesPrintf;
        private bool _usesScanf;
        private bool _usesStricmp;
        private bool _usesStrcmp;
        private bool _usesStrcpy;
        private bool _hasInputFailure;
        private bool _hasOverflowFailure;
        private bool _hasDivisionFailure;
        private string? _overflowMessageLabel;
        private string? _divisionMessageLabel;

        public Writer(BoundProgram program)
        {
            _program = program;
            _analysis = BoundProgramAnalysis.Create(program);
            _identifiers = TargetIdentifierMap.Create(program, TargetLanguage.MasmX64);
            _integers = TargetIntegerProfile.Analyze(program, _analysis);
        }

        public GeneratedProgram Generate()
        {
            DeclareVariables();
            AppendSourceItems(_program.SourceItems);
            PrepareFailureMessages();

            var source = new StringBuilder();
            source.AppendLine("option casemap:none");
            source.AppendLine();

            if (_usesPrintf || _usesScanf || _usesStricmp || _usesStrcmp || _usesStrcpy ||
                _hasOverflowFailure || _hasDivisionFailure)
            {
                source.AppendLine("includelib legacy_stdio_definitions.lib");
                source.AppendLine("includelib ucrt.lib");
            }

            source.AppendLine("includelib kernel32.lib");
            source.AppendLine();

            if (_usesPrintf)
            {
                source.AppendLine("extern printf:proc");
            }

            if (_usesScanf)
            {
                source.AppendLine("extern scanf:proc");
            }

            if (_usesStricmp)
            {
                source.AppendLine("extern _stricmp:proc");
            }

            if (_usesStrcmp)
            {
                source.AppendLine("extern strcmp:proc");
            }

            if (_usesStrcpy)
            {
                source.AppendLine("extern strcpy:proc");
            }

            if (_hasOverflowFailure || _hasDivisionFailure)
            {
                source.AppendLine("extern fputs:proc");
                source.AppendLine("extern __acrt_iob_func:proc");
            }

            source.AppendLine("extern ExitProcess:proc");
            source.AppendLine();
            source.AppendLine(".data");
            source.Append(_data);
            source.AppendLine();
            source.AppendLine(".code");
            source.AppendLine();
            source.AppendLine("main PROC");
            source.AppendLine("    ; Reserve Win64 shadow space and keep the stack aligned.");
            source.AppendLine("    sub rsp, 40");
            if (_code.Length > 0)
            {
                source.AppendLine();
                source.Append(_code);
            }

            source.AppendLine();
            source.AppendLine("    ; ExitProcess(0)");
            source.AppendLine("    xor ecx, ecx");
            source.AppendLine("    call ExitProcess");

            AppendFailurePaths(source);
            source.AppendLine();
            source.AppendLine("main ENDP");
            source.AppendLine();
            source.AppendLine("END");

            return new GeneratedProgram(
                TargetLanguage.MasmX64,
                new[]
                {
                    new GeneratedFile(
                        "Program.asm",
                        TextOutput.EnsureOneTrailingNewLine(source.ToString()),
                        IsPrimary: true)
                });
        }

        private void DeclareVariables()
        {
            foreach (BoundLetStatement declaration in _program.Statements.OfType<BoundLetStatement>())
            {
                BoundStatementAnalysis facts = _analysis.GetStatementFacts(declaration);
                string name = _identifiers.Get(declaration.Variable);
                var variable = new NativeVariable(declaration.Variable, name);
                _variables.Add(declaration.Variable, variable);

                switch (declaration.Variable.Type)
                {
                    case SmileType.Integer:
                        long integer = facts.Value.IsKnown ? facts.Value.Value.IntegerValue : 0;
                        _data.Append("    ").Append(name).Append(_integers.RequiresSigned64Storage ? " QWORD " : " DWORD ")
                            .AppendLine(IntegerInitializer(integer));
                        break;

                    case SmileType.Boolean:
                        bool boolean = facts.Value.IsKnown && facts.Value.Value.BooleanValue;
                        _data.Append("    ").Append(name).Append(" BYTE ")
                            .AppendLine(boolean ? "1" : "0");
                        break;

                    case SmileType.String:
                        if (!facts.Value.IsKnown)
                        {
                            throw new UnsupportedNativeMasmException();
                        }

                        string initialLabel = AddCString(facts.Value.Value.StringValue, "String initializer");
                        _data.Append("    ").Append(name).Append(" QWORD OFFSET ")
                            .AppendLine(initialLabel);
                        break;

                    default:
                        throw new UnsupportedNativeMasmException();
                }
            }
        }

        private void AppendSourceItems(IReadOnlyList<BoundSourceItem> sourceItems)
        {
            foreach (BoundSourceItem sourceItem in sourceItems)
            {
                switch (sourceItem)
                {
                    case BoundFullLineComment comment:
                        TargetComments.Append(_code, TargetLanguage.MasmX64, "    ", comment.Payload);
                        break;

                    case BoundBlankLine:
                        _code.AppendLine();
                        break;

                    case BoundLetStatement:
                        // Native storage is initialized directly in .data.
                        break;

                    case BoundSetStatement set:
                        AppendSet(set);
                        break;

                    case BoundInputStatement input:
                        AppendInput(input);
                        break;

                    case BoundPrintStatement print:
                        AppendPrint(print);
                        break;

                    case BoundIfStatement conditional:
                        AppendIf(conditional);
                        break;

                    case BoundWhileStatement loop:
                        AppendWhile(loop);
                        break;

                    default:
                        throw new UnsupportedNativeMasmException();
                }
            }
        }

        private void AppendSet(BoundSetStatement set)
        {
            NativeVariable target = Variable(set.Variable);
            BoundStatementAnalysis facts = _analysis.GetStatementFacts(set);
            _code.Append("    ; SET ").AppendLine(set.Variable.Name);

            switch (set.Variable.Type)
            {
                case SmileType.Integer:
                    AppendIntegerExpression(set.Value);
                    AppendStoreInteger(target);
                    break;

                case SmileType.Boolean:
                    AppendBooleanExpression(set.Value);
                    _code.Append("    mov BYTE PTR [").Append(target.Name).AppendLine("], al");
                    break;

                case SmileType.String when facts.Value.IsKnown:
                    string valueLabel = AddCString(facts.Value.Value.StringValue, "SET String value");
                    _code.Append("    lea rax, ").AppendLine(valueLabel);
                    _code.Append("    mov QWORD PTR [").Append(target.Name).AppendLine("], rax");
                    break;

                case SmileType.String when set.Value is BoundVariableExpression source:
                    if (ReferenceEquals(source.Variable, set.Variable))
                    {
                        // Direct self-assignment remains a real target storage
                        // update without copying one buffer onto itself.
                        _code.Append("    mov rax, QWORD PTR [").Append(target.Name).AppendLine("]");
                        _code.Append("    mov QWORD PTR [").Append(target.Name).AppendLine("], rax");
                        break;
                    }

                    // SMILE Strings have value semantics. A pointer alias to
                    // an INPUT buffer would let a later INPUT mutate this SET
                    // without executing another assignment, so copy into
                    // storage owned by this source statement.
                    _usesStrcpy = true;
                    int capacity = Math.Max(
                        1,
                        _analysis.MaximumAssignedUtf8ByteLength(set.Variable));
                    string copyBuffer = $"smileString{_stringCopyIndex++}Buffer";
                    _data.Append("    ").Append(copyBuffer).Append(" BYTE ")
                        .Append((capacity + 1).ToString(CultureInfo.InvariantCulture))
                        .AppendLine(" DUP (0)");
                    _code.Append("    lea rcx, ").AppendLine(copyBuffer);
                    _code.Append("    mov rdx, QWORD PTR [").Append(Variable(source.Variable).Name)
                        .AppendLine("]");
                    _code.AppendLine("    call strcpy");
                    _code.Append("    mov QWORD PTR [").Append(target.Name).AppendLine("], rax");
                    break;

                default:
                    throw new UnsupportedNativeMasmException();
            }

            _code.AppendLine();
        }

        private void AppendInput(BoundInputStatement input)
        {
            NativeVariable target = Variable(input.Variable);
            int inputIndex = _inputIndex++;
            _usesScanf = true;
            _code.Append("    ; INPUT ").AppendLine(input.Variable.Name);

            switch (input.Variable.Type)
            {
                case SmileType.Integer:
                    string integerFormat = AddSharedCString(
                        _integers.RequiresSigned64Storage ? "%lld" : "%d",
                        _integers.RequiresSigned64Storage ? "signed 64-bit input format" : "Integer input format");
                    _code.Append("    lea rcx, ").AppendLine(integerFormat);
                    _code.Append("    lea rdx, ").AppendLine(target.Name);
                    _code.AppendLine("    call scanf");
                    AppendScanfSuccessCheck();
                    break;

                case SmileType.Boolean:
                    _usesStricmp = true;
                    string booleanBuffer = $"smileInput{inputIndex}Boolean";
                    _data.Append("    ").Append(booleanBuffer).AppendLine(" BYTE 6 DUP (0)");
                    string booleanFormat = AddSharedCString("%5s", "Boolean input format");
                    string trueText = AddSharedCString("TRUE", "Boolean TRUE text");
                    string falseText = AddSharedCString("FALSE", "Boolean FALSE text");
                    string trueLabel = NewLabel("inputTrue");
                    string readyLabel = NewLabel("inputReady");
                    _code.Append("    lea rcx, ").AppendLine(booleanFormat);
                    _code.Append("    lea rdx, ").AppendLine(booleanBuffer);
                    _code.AppendLine("    call scanf");
                    AppendScanfSuccessCheck();
                    _code.Append("    lea rcx, ").AppendLine(booleanBuffer);
                    _code.Append("    lea rdx, ").AppendLine(trueText);
                    _code.AppendLine("    call _stricmp");
                    _code.AppendLine("    test eax, eax");
                    _code.Append("    jz ").AppendLine(trueLabel);
                    _code.Append("    lea rcx, ").AppendLine(booleanBuffer);
                    _code.Append("    lea rdx, ").AppendLine(falseText);
                    _code.AppendLine("    call _stricmp");
                    _code.AppendLine("    test eax, eax");
                    _code.Append("    jnz ").AppendLine(InputFailureLabel);
                    _code.AppendLine("    xor eax, eax");
                    _code.Append("    jmp ").AppendLine(readyLabel);
                    _code.Append(trueLabel).AppendLine(":");
                    _code.AppendLine("    mov eax, 1");
                    _code.Append(readyLabel).AppendLine(":");
                    _code.Append("    mov BYTE PTR [").Append(target.Name).AppendLine("], al");
                    break;

                case SmileType.String:
                    string stringBuffer = $"smileInput{inputIndex}String";
                    _data.Append("    ").Append(stringBuffer).Append(" BYTE ")
                        .Append(StringInputCapacity.ToString(CultureInfo.InvariantCulture)).AppendLine(" DUP (0)");
                    string stringFormat = AddSharedCString(" %255[^\r\n]", "String input format");
                    _code.Append("    lea rcx, ").AppendLine(stringFormat);
                    _code.Append("    lea rdx, ").AppendLine(stringBuffer);
                    _code.AppendLine("    call scanf");
                    AppendScanfSuccessCheck();
                    _code.Append("    lea rax, ").AppendLine(stringBuffer);
                    _code.Append("    mov QWORD PTR [").Append(target.Name).AppendLine("], rax");
                    break;

                default:
                    throw new UnsupportedNativeMasmException();
            }

            _code.AppendLine();
        }

        private void AppendScanfSuccessCheck()
        {
            _hasInputFailure = true;
            _code.AppendLine("    cmp eax, 1");
            _code.Append("    jne ").AppendLine(InputFailureLabel);
        }

        private void AppendPrint(BoundPrintStatement print)
        {
            _usesPrintf = true;
            BoundStatementAnalysis facts = _analysis.GetStatementFacts(print);
            _code.AppendLine("    ; PRINT");

            if (print.IsBlankLine)
            {
                AppendPrintfLiteral("\n");
                _code.AppendLine();
                return;
            }

            // A direct variable read should stay visible in learner-facing
            // output even when analysis can prove its current value. Literal
            // folding is useful only when the source expression is itself
            // variable-free.
            if (facts.Value.IsKnown && !ContainsVariable(print.Value))
            {
                AppendPrintfLiteral(facts.Value.Value.ToDisplayText() + "\n");
                _code.AppendLine();
                return;
            }

            foreach (RuntimeTextSegment segment in RuntimeTextPlan.Flatten(print.Value))
            {
                switch (segment)
                {
                    case RuntimeLiteralTextSegment { Text.Length: > 0 } literal:
                        AppendPrintfLiteral(literal.Text);
                        break;

                    case RuntimeLiteralTextSegment:
                        break;

                    case RuntimeExpressionTextSegment expression:
                        AppendPrintfExpression(expression.Expression);
                        break;

                    default:
                        throw new UnsupportedNativeMasmException();
                }
            }

            AppendPrintfLiteral("\n");
            _code.AppendLine();
        }

        private static bool ContainsVariable(BoundExpression expression) =>
            expression switch
            {
                BoundVariableExpression => true,
                BoundUnaryExpression unary => ContainsVariable(unary.Operand),
                BoundBinaryExpression binary =>
                    ContainsVariable(binary.Left) || ContainsVariable(binary.Right),
                BoundInterpolatedStringExpression interpolated => interpolated.Parts.Any(part =>
                    part is BoundInterpolationExpressionPart hole && ContainsVariable(hole.Expression)),
                _ => false
            };

        private void AppendPrintfLiteral(string text)
        {
            string format = AddCString(text.Replace("%", "%%", StringComparison.Ordinal), "PRINT format");
            _code.Append("    lea rcx, ").AppendLine(format);
            _code.AppendLine("    call printf");
        }

        private void AppendPrintfExpression(BoundExpression expression)
        {
            switch (expression.Type)
            {
                case SmileType.Integer:
                    AppendIntegerExpression(expression);
                    _code.AppendLine("    mov rdx, rax");
                    _code.Append("    lea rcx, ").AppendLine(AddSharedCString(
                        _integers.RequiresSigned64Storage ? "%lld" : "%d",
                        "Integer PRINT format"));
                    _code.AppendLine("    call printf");
                    break;

                case SmileType.String:
                    AppendStringPointer(expression);
                    _code.AppendLine("    mov rdx, rax");
                    _code.Append("    lea rcx, ").AppendLine(AddSharedCString("%s", "String PRINT format"));
                    _code.AppendLine("    call printf");
                    break;

                case SmileType.Boolean:
                    AppendBooleanExpression(expression);
                    string falseLabel = NewLabel("booleanFalse");
                    string readyLabel = NewLabel("booleanReady");
                    string trueText = AddSharedCString("TRUE", "Boolean TRUE text");
                    string falseText = AddSharedCString("FALSE", "Boolean FALSE text");
                    _code.AppendLine("    test eax, eax");
                    _code.Append("    jz ").AppendLine(falseLabel);
                    _code.Append("    lea rdx, ").AppendLine(trueText);
                    _code.Append("    jmp ").AppendLine(readyLabel);
                    _code.Append(falseLabel).AppendLine(":");
                    _code.Append("    lea rdx, ").AppendLine(falseText);
                    _code.Append(readyLabel).AppendLine(":");
                    _code.Append("    lea rcx, ").AppendLine(AddSharedCString("%s", "Boolean PRINT format"));
                    _code.AppendLine("    call printf");
                    break;

                default:
                    throw new UnsupportedNativeMasmException();
            }
        }

        private void AppendIf(BoundIfStatement conditional)
        {
            string endLabel = NewLabel("ifEnd");
            _code.AppendLine("    ; IF");

            for (int clauseIndex = 0; clauseIndex < conditional.Clauses.Count; clauseIndex++)
            {
                BoundConditionalClause clause = conditional.Clauses[clauseIndex];
                string nextLabel = NewLabel("ifNext");
                AppendBooleanExpression(clause.Condition);
                _code.AppendLine("    test eax, eax");
                _code.Append("    jz ").AppendLine(nextLabel);
                AppendSourceItems(clause.SourceItems);
                _code.Append("    jmp ").AppendLine(endLabel);
                _code.Append(nextLabel).AppendLine(":");
            }

            if (conditional.HasElseClause)
            {
                AppendSourceItems(conditional.ElseSourceItems);
            }

            _code.Append(endLabel).AppendLine(":");
            _code.AppendLine();
        }

        private void AppendWhile(BoundWhileStatement loop)
        {
            string headLabel = NewLabel("whileHead");
            string endLabel = NewLabel("whileEnd");
            _code.AppendLine("    ; WHILE");
            _code.Append(headLabel).AppendLine(":");
            AppendBooleanExpression(loop.Condition);
            _code.AppendLine("    test eax, eax");
            _code.Append("    jz ").AppendLine(endLabel);
            AppendSourceItems(loop.SourceItems);
            _code.Append("    jmp ").AppendLine(headLabel);
            _code.Append(endLabel).AppendLine(":");
            _code.AppendLine();
        }

        private void AppendIntegerExpression(BoundExpression expression)
        {
            switch (expression)
            {
                case BoundVariableExpression variable when variable.Variable.Type is SmileType.Integer:
                    NativeVariable storage = Variable(variable.Variable);
                    if (_integers.RequiresSigned64Storage)
                    {
                        _code.Append("    mov rax, QWORD PTR [").Append(storage.Name).AppendLine("]");
                    }
                    else
                    {
                        _code.Append("    movsxd rax, DWORD PTR [").Append(storage.Name).AppendLine("]");
                    }

                    return;

                case BoundIntegerLiteralExpression literal:
                    _code.Append("    mov rax, ").AppendLine(IntegerImmediate(literal.Value));
                    return;

                case BoundUnaryExpression unary when unary.Operator.Kind is BoundUnaryOperatorKind.Identity:
                    AppendIntegerExpression(unary.Operand);
                    return;

                case BoundUnaryExpression unary when unary.Operator.Kind is BoundUnaryOperatorKind.Negation:
                    AppendIntegerExpression(unary.Operand);
                    if (_integers.RequiresSigned64Storage)
                    {
                        _code.AppendLine("    neg rax");
                    }
                    else
                    {
                        _code.AppendLine("    neg eax");
                    }

                    AppendOverflowCheck();
                    if (!_integers.RequiresSigned64Storage)
                    {
                        _code.AppendLine("    movsxd rax, eax");
                    }

                    return;

                case BoundBinaryExpression binary when binary.Operator.Kind is
                    BoundBinaryOperatorKind.Addition or
                    BoundBinaryOperatorKind.Subtraction or
                    BoundBinaryOperatorKind.Multiplication or
                    BoundBinaryOperatorKind.Division:
                    AppendIntegerExpression(binary.Left);
                    _code.AppendLine("    push rax");
                    AppendIntegerExpression(binary.Right);
                    _code.AppendLine("    mov r10, rax");
                    _code.AppendLine("    pop rax");
                    AppendCheckedBinaryInteger(binary.Operator.Kind);
                    return;

                default:
                    throw new UnsupportedNativeMasmException();
            }
        }

        private void AppendCheckedBinaryInteger(BoundBinaryOperatorKind kind)
        {
            string left = _integers.RequiresSigned64Storage ? "rax" : "eax";
            string right = _integers.RequiresSigned64Storage ? "r10" : "r10d";
            switch (kind)
            {
                case BoundBinaryOperatorKind.Addition:
                case BoundBinaryOperatorKind.Subtraction:
                case BoundBinaryOperatorKind.Multiplication:
                    string instruction = kind switch
                    {
                        BoundBinaryOperatorKind.Addition => "add",
                        BoundBinaryOperatorKind.Subtraction => "sub",
                        BoundBinaryOperatorKind.Multiplication => "imul",
                        _ => throw new UnsupportedNativeMasmException()
                    };
                    _code.Append("    ").Append(instruction).Append(' ').Append(left)
                        .Append(", ").AppendLine(right);
                    AppendOverflowCheck();
                    if (!_integers.RequiresSigned64Storage)
                    {
                        _code.AppendLine("    movsxd rax, eax");
                    }

                    return;

                case BoundBinaryOperatorKind.Division:
                    _hasDivisionFailure = true;
                    _code.Append("    test ").Append(right).Append(", ").AppendLine(right);
                    _code.Append("    jz ").AppendLine(DivisionFailureLabel);
                    if (_integers.RequiresSigned64Storage)
                    {
                        _code.AppendLine("    mov r11, 08000000000000000h");
                        _code.AppendLine("    cmp rax, r11");
                        string divideReady = NewLabel("divideReady");
                        _code.Append("    jne ").AppendLine(divideReady);
                        _code.AppendLine("    cmp r10, -1");
                        AppendOverflowJumpIfEqual();
                        _code.Append(divideReady).AppendLine(":");
                        _code.AppendLine("    cqo");
                        _code.AppendLine("    idiv r10");
                    }
                    else
                    {
                        _code.AppendLine("    cmp eax, 80000000h");
                        string divideReady = NewLabel("divideReady");
                        _code.Append("    jne ").AppendLine(divideReady);
                        _code.AppendLine("    cmp r10d, -1");
                        AppendOverflowJumpIfEqual();
                        _code.Append(divideReady).AppendLine(":");
                        _code.AppendLine("    cdq");
                        _code.AppendLine("    idiv r10d");
                        _code.AppendLine("    movsxd rax, eax");
                    }

                    return;

                default:
                    throw new UnsupportedNativeMasmException();
            }
        }

        private void AppendOverflowCheck()
        {
            _hasOverflowFailure = true;
            _code.Append("    jo ").AppendLine(OverflowFailureLabel);
        }

        private void AppendOverflowJumpIfEqual()
        {
            _hasOverflowFailure = true;
            _code.Append("    je ").AppendLine(OverflowFailureLabel);
        }

        private void AppendBooleanExpression(BoundExpression expression)
        {
            switch (expression)
            {
                case BoundBooleanLiteralExpression literal:
                    _code.Append("    mov eax, ").AppendLine(literal.Value ? "1" : "0");
                    return;

                case BoundVariableExpression variable when variable.Variable.Type is SmileType.Boolean:
                    _code.Append("    movzx eax, BYTE PTR [").Append(Variable(variable.Variable).Name).AppendLine("]");
                    return;

                case BoundUnaryExpression unary when unary.Operator.Kind is BoundUnaryOperatorKind.LogicalNegation:
                    AppendBooleanExpression(unary.Operand);
                    _code.AppendLine("    xor eax, 1");
                    return;

                case BoundBinaryExpression binary when binary.Operator.Kind is BoundBinaryOperatorKind.LogicalAnd:
                    AppendLogicalAnd(binary);
                    return;

                case BoundBinaryExpression binary when binary.Operator.Kind is BoundBinaryOperatorKind.LogicalOr:
                    AppendLogicalOr(binary);
                    return;

                case BoundBinaryExpression comparison when comparison.Operator.Kind is
                    BoundBinaryOperatorKind.Equality or
                    BoundBinaryOperatorKind.Inequality or
                    BoundBinaryOperatorKind.Less or
                    BoundBinaryOperatorKind.LessOrEquals or
                    BoundBinaryOperatorKind.Greater or
                    BoundBinaryOperatorKind.GreaterOrEquals:
                    AppendComparison(comparison);
                    return;

                default:
                    throw new UnsupportedNativeMasmException();
            }
        }

        private void AppendLogicalAnd(BoundBinaryExpression binary)
        {
            string falseLabel = NewLabel("andFalse");
            string doneLabel = NewLabel("andDone");
            AppendBooleanExpression(binary.Left);
            _code.AppendLine("    test eax, eax");
            _code.Append("    jz ").AppendLine(falseLabel);
            AppendBooleanExpression(binary.Right);
            _code.Append("    jmp ").AppendLine(doneLabel);
            _code.Append(falseLabel).AppendLine(":");
            _code.AppendLine("    xor eax, eax");
            _code.Append(doneLabel).AppendLine(":");
        }

        private void AppendLogicalOr(BoundBinaryExpression binary)
        {
            string trueLabel = NewLabel("orTrue");
            string doneLabel = NewLabel("orDone");
            AppendBooleanExpression(binary.Left);
            _code.AppendLine("    test eax, eax");
            _code.Append("    jnz ").AppendLine(trueLabel);
            AppendBooleanExpression(binary.Right);
            _code.Append("    jmp ").AppendLine(doneLabel);
            _code.Append(trueLabel).AppendLine(":");
            _code.AppendLine("    mov eax, 1");
            _code.Append(doneLabel).AppendLine(":");
        }

        private void AppendComparison(BoundBinaryExpression comparison)
        {
            if (comparison.Left.Type is SmileType.Integer)
            {
                AppendIntegerExpression(comparison.Left);
                _code.AppendLine("    push rax");
                AppendIntegerExpression(comparison.Right);
                _code.AppendLine("    mov r10, rax");
                _code.AppendLine("    pop rax");
                _code.AppendLine("    cmp rax, r10");
                AppendSetFromComparison(comparison.Operator.Kind, signedRelational: true);
                return;
            }

            if (comparison.Left.Type is SmileType.Boolean)
            {
                AppendBooleanExpression(comparison.Left);
                _code.AppendLine("    push rax");
                AppendBooleanExpression(comparison.Right);
                _code.AppendLine("    mov r10d, eax");
                _code.AppendLine("    pop rax");
                _code.AppendLine("    cmp eax, r10d");
                AppendSetFromComparison(comparison.Operator.Kind, signedRelational: false);
                return;
            }

            if (comparison.Left.Type is SmileType.String && comparison.Operator.Kind is
                BoundBinaryOperatorKind.Equality or BoundBinaryOperatorKind.Inequality)
            {
                _usesStrcmp = true;
                AppendStringPointer(comparison.Left);
                _code.AppendLine("    push rax");
                AppendStringPointer(comparison.Right);
                _code.AppendLine("    mov rdx, rax");
                _code.AppendLine("    pop rcx");
                _code.AppendLine("    call strcmp");
                _code.AppendLine("    test eax, eax");
                _code.AppendLine(comparison.Operator.Kind is BoundBinaryOperatorKind.Equality
                    ? "    sete al"
                    : "    setne al");
                _code.AppendLine("    movzx eax, al");
                return;
            }

            throw new UnsupportedNativeMasmException();
        }

        private void AppendSetFromComparison(BoundBinaryOperatorKind kind, bool signedRelational)
        {
            string instruction = kind switch
            {
                BoundBinaryOperatorKind.Equality => "sete",
                BoundBinaryOperatorKind.Inequality => "setne",
                BoundBinaryOperatorKind.Less => signedRelational ? "setl" : "setb",
                BoundBinaryOperatorKind.LessOrEquals => signedRelational ? "setle" : "setbe",
                BoundBinaryOperatorKind.Greater => signedRelational ? "setg" : "seta",
                BoundBinaryOperatorKind.GreaterOrEquals => signedRelational ? "setge" : "setae",
                _ => throw new UnsupportedNativeMasmException()
            };
            _code.Append("    ").Append(instruction).AppendLine(" al");
            _code.AppendLine("    movzx eax, al");
        }

        private void AppendStringPointer(BoundExpression expression)
        {
            switch (expression)
            {
                case BoundVariableExpression variable when variable.Variable.Type is SmileType.String:
                    _code.Append("    mov rax, QWORD PTR [").Append(Variable(variable.Variable).Name).AppendLine("]");
                    return;

                case BoundStringLiteralExpression literal:
                    _code.Append("    lea rax, ").AppendLine(AddCString(literal.Value, "String expression"));
                    return;

                default:
                    throw new UnsupportedNativeMasmException();
            }
        }

        private void AppendStoreInteger(NativeVariable target)
        {
            _code.Append("    mov ")
                .Append(_integers.RequiresSigned64Storage ? "QWORD" : "DWORD")
                .Append(" PTR [").Append(target.Name).Append("], ")
                .AppendLine(_integers.RequiresSigned64Storage ? "rax" : "eax");
        }

        private void PrepareFailureMessages()
        {
            if (_hasOverflowFailure)
            {
                _overflowMessageLabel = AddSharedCString(
                    "SMILE Runtime Error SMILER1206: Integer arithmetic overflow.\n",
                    "Integer overflow message");
            }

            if (_hasDivisionFailure)
            {
                _divisionMessageLabel = AddSharedCString(
                    "SMILE Runtime Error SMILER1207: Division by zero.\n",
                    "division failure message");
            }
        }

        private void AppendFailurePaths(StringBuilder source)
        {
            if (_hasInputFailure)
            {
                source.AppendLine();
                source.Append(InputFailureLabel).AppendLine(":");
                source.AppendLine("    mov ecx, 1");
                source.AppendLine("    call ExitProcess");
            }

            if (_hasOverflowFailure)
            {
                AppendRuntimeFailurePath(
                    source,
                    OverflowFailureLabel,
                    _overflowMessageLabel ?? throw new InvalidOperationException());
            }

            if (_hasDivisionFailure)
            {
                AppendRuntimeFailurePath(
                    source,
                    DivisionFailureLabel,
                    _divisionMessageLabel ?? throw new InvalidOperationException());
            }
        }

        private static void AppendRuntimeFailurePath(
            StringBuilder source,
            string label,
            string messageLabel)
        {
            source.AppendLine();
            source.Append(label).AppendLine(":");
            source.AppendLine("    ; fputs(message, stderr)");
            source.AppendLine("    mov ecx, 2");
            source.AppendLine("    call __acrt_iob_func");
            source.AppendLine("    mov rdx, rax");
            source.Append("    lea rcx, ").AppendLine(messageLabel);
            source.AppendLine("    call fputs");
            source.AppendLine("    mov ecx, 1");
            source.AppendLine("    call ExitProcess");
        }

        private NativeVariable Variable(VariableSymbol symbol) =>
            _variables.TryGetValue(symbol, out NativeVariable? variable)
                ? variable
                : throw new UnsupportedNativeMasmException();

        private string AddSharedCString(string value, string purpose)
        {
            if (_sharedStrings.TryGetValue(value, out string? existing))
            {
                return existing;
            }

            string label = AddCString(value, purpose);
            _sharedStrings.Add(value, label);
            return label;
        }

        private string AddCString(string value, string purpose)
        {
            // CRT C strings cannot display or compare bytes after NUL. Keep
            // source-authored exact-NUL programs on the compatibility path;
            // ordinary learner strings remain on the concise native path.
            if (value.Contains('\0', StringComparison.Ordinal))
            {
                throw new UnsupportedNativeMasmException();
            }

            string label = $"smileText{_textIndex++}";
            string bytes = TargetEscapes.MasmByteInitializers(value);
            _data.Append("    ").Append(label).Append(" BYTE ");
            if (value.Length == 0)
            {
                _data.Append('0');
            }
            else
            {
                _data.Append(bytes).Append(", 0");
            }

            _data.Append("    ; ").AppendLine(purpose);
            return label;
        }

        private string NewLabel(string purpose) =>
            $"smile{purpose}{_labelIndex++}";

        private string IntegerInitializer(long value) =>
            _integers.RequiresSigned64Storage
                ? IntegerImmediate(value)
                : value.ToString(CultureInfo.InvariantCulture);

        private static string IntegerImmediate(long value) =>
            "0" + unchecked((ulong)value).ToString("X16", CultureInfo.InvariantCulture) + "h";

        private sealed record NativeVariable(VariableSymbol Symbol, string Name);
    }

    private sealed class UnsupportedNativeMasmException : Exception
    {
    }
}
