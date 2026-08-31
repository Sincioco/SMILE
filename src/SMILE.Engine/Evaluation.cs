using System.Text;

namespace SMILE.Engine;

public sealed record SmileRuntimeError(string Code, string Message)
{
    public override string ToString() => $"SMILE Runtime Error {Code}: {Message}";
}

public sealed record EvaluationResult(
    bool Success,
    string Output,
    IReadOnlyList<Diagnostic> Diagnostics,
    string ErrorOutput = "",
    int ExitCode = 0,
    SmileRuntimeError? RuntimeError = null)
{
    public string StandardOutput => Output;

    public string StandardError => ErrorOutput;
}

public sealed class SmileEvaluator
{
    private readonly SmileTranspiler _transpiler = new();
    private readonly Dictionary<VariableSymbol, SmileValue> _globalValues = new();
    private readonly Dictionary<VariableSymbol, SmileValue[]> _globalArrays = new();
    private readonly Dictionary<RoutineSymbol, BoundRoutineDeclaration> _routines = new();
    private readonly StringBuilder _output = new();
    private ISmileEvaluationHost _host = new ScriptedSmileEvaluationHost();
    private long _remainingStatements;
    private CancellationToken _cancellationToken;

    public EvaluationResult Evaluate(string source) =>
        Evaluate(source, new SmileEvaluationOptions(), CancellationToken.None);

    public EvaluationResult Evaluate(string source, CancellationToken cancellationToken)
        => Evaluate(source, new SmileEvaluationOptions(), cancellationToken);

    public EvaluationResult Evaluate(string source, SmileEvaluationOptions options) =>
        Evaluate(source, options, CancellationToken.None);

    public EvaluationResult Evaluate(
        string source,
        SmileEvaluationOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        if (options.StatementBudget <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Statement budget must be positive.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        BindResult bindResult = _transpiler.Bind(source);
        if (!bindResult.Success || bindResult.Program is null)
        {
            return new EvaluationResult(
                Success: false,
                Output: string.Empty,
                Diagnostics: bindResult.Diagnostics,
                ExitCode: 1);
        }

        _globalValues.Clear();
        _globalArrays.Clear();
        _routines.Clear();
        _output.Clear();
        _host = options.Host ?? new ScriptedSmileEvaluationHost();
        _remainingStatements = options.StatementBudget;
        _cancellationToken = cancellationToken;
        InitializeProgram(bindResult.Program);

        SmileRuntimeError? runtimeError;
        try
        {
            runtimeError = ExecuteStatements(bindResult.Program.Statements, frame: null);
        }
        catch (ProgramEndSignal)
        {
            runtimeError = null;
        }

        return runtimeError is null
            ? new EvaluationResult(
                Success: true,
                Output: _output.ToString(),
                Diagnostics: bindResult.Diagnostics)
            : new EvaluationResult(
                Success: false,
                Output: _output.ToString(),
                Diagnostics: bindResult.Diagnostics,
                ErrorOutput: runtimeError + "\n",
                ExitCode: 1,
                RuntimeError: runtimeError);
    }

    private void InitializeProgram(BoundProgram program)
    {
        var constants = program.SourceItems
            .OfType<BoundConstStatement>()
            .ToDictionary(item => item.Variable, item => item.Value);

        foreach (VariableSymbol variable in program.Variables)
        {
            if (variable.IsArray)
            {
                _globalArrays[variable] = CreateArray(variable);
            }
            else
            {
                _globalValues[variable] = constants.TryGetValue(variable, out SmileValue value)
                    ? value
                    : DefaultValue(variable.Type);
            }
        }

        foreach (BoundRoutineDeclaration routine in program.Routines)
        {
            _routines[routine.Symbol] = routine;
        }
    }

    private SmileRuntimeError? ExecuteStatements(IReadOnlyList<BoundStatement> statements, CallFrame? frame)
    {
        foreach (BoundStatement statement in statements)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (!TryConsumeBudget(out SmileRuntimeError? budgetError))
            {
                return budgetError;
            }

            switch (statement)
            {
                case BoundDimStatement or BoundConstStatement:
                    break;

                case BoundSetStatement assignment:
                    if (!TryEvaluateExpression(assignment.Value, frame, out SmileValue assignedValue, out SmileRuntimeError? assignmentError))
                    {
                        return assignmentError;
                    }

                    SetValue(assignment.Variable, frame, assignedValue);
                    break;

                case BoundArraySetStatement assignment:
                    if (!TryEvaluateArrayIndices(assignment.Indices, frame, out long[]? requestedIndices, out SmileRuntimeError? indexError))
                    {
                        return indexError;
                    }

                    if (!TryGetArrayElement(assignment.Array, requestedIndices!, frame, out SmileValue[]? array, out int index, out SmileRuntimeError? boundsError))
                    {
                        return boundsError;
                    }

                    if (!TryEvaluateExpression(assignment.Value, frame, out SmileValue arrayValue, out SmileRuntimeError? valueError))
                    {
                        return valueError;
                    }

                    array![index] = arrayValue;
                    break;

                case BoundGetKeyStatement getKey:
                    SetValue(getKey.Target, frame, SmileValue.FromInteger(_host.ReadKeyNonBlocking()));
                    break;

                case BoundClearScreenStatement:
                    _host.ClearScreen(_output.ToString());
                    break;

                case BoundWaitStatement wait:
                    if (!TryEvaluateExpression(wait.Duration, frame, out SmileValue duration, out SmileRuntimeError? waitError))
                    {
                        return waitError;
                    }

                    _host.WaitMilliseconds(SmileRuntimeRules.NormalizeWaitMilliseconds(duration.IntegerValue));
                    break;

                case BoundRandomStatement random:
                    if (!TryEvaluateExpression(random.LowerBound, frame, out SmileValue lower, out SmileRuntimeError? lowerError))
                    {
                        return lowerError;
                    }

                    if (!TryEvaluateExpression(random.UpperBound, frame, out SmileValue upper, out SmileRuntimeError? upperError))
                    {
                        return upperError;
                    }

                    SetValue(
                        random.Target,
                        frame,
                        SmileValue.FromInteger(lower.IntegerValue > upper.IntegerValue
                            ? lower.IntegerValue
                            : _host.NextRandomInclusive(lower.IntegerValue, upper.IntegerValue)));
                    break;

                case BoundCallStatement call:
                    if (!TryEvaluateArguments(call.Arguments, frame, out SmileValue[]? callArguments, out SmileRuntimeError? argumentError))
                    {
                        return argumentError;
                    }

                    if (!TryInvoke(call.Routine, callArguments!, out _, out SmileRuntimeError? callError))
                    {
                        return callError;
                    }

                    break;

                case BoundReturnStatement returnStatement:
                    SmileValue? returnValue = null;
                    if (returnStatement.Value is not null)
                    {
                        if (!TryEvaluateExpression(returnStatement.Value, frame, out SmileValue value, out SmileRuntimeError? returnError))
                        {
                            return returnError;
                        }

                        returnValue = value;
                    }

                    throw new RoutineReturnSignal(returnValue);

                case BoundCorePrintStatement print:
                    foreach (BoundExpression expression in print.Values)
                    {
                        if (!TryEvaluateExpression(expression, frame, out SmileValue printedValue, out SmileRuntimeError? printError))
                        {
                            return printError;
                        }

                        _output.Append(printedValue.ToDisplayText());
                    }

                    if (!print.SuppressNewLine)
                    {
                        _output.Append('\n');
                    }

                    break;

                case BoundIfStatement conditional:
                    SmileRuntimeError? ifError = ExecuteIf(conditional, frame);
                    if (ifError is not null)
                    {
                        return ifError;
                    }

                    break;

                case BoundSelectStatement select:
                    SmileRuntimeError? selectError = ExecuteSelect(select, frame);
                    if (selectError is not null)
                    {
                        return selectError;
                    }

                    break;

                case BoundForStatement loop:
                    SmileRuntimeError? forError = ExecuteFor(loop, frame);
                    if (forError is not null)
                    {
                        return forError;
                    }

                    break;

                case BoundDoStatement loop:
                    SmileRuntimeError? doError = ExecuteDo(loop, frame);
                    if (doError is not null)
                    {
                        return doError;
                    }

                    break;

                case BoundExitStatement exit:
                    throw new LoopExitSignal(exit.Kind);

                case BoundEndProgramStatement:
                    throw new ProgramEndSignal();

                default:
                    throw new InvalidOperationException(
                        $"Unsupported statement reached the Core BASIC evaluator: {statement.GetType().Name}.");
            }
        }

        return null;
    }

    private SmileRuntimeError? ExecuteIf(BoundIfStatement conditional, CallFrame? frame)
    {
        foreach (BoundConditionalClause clause in conditional.Clauses)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (!TryEvaluateExpression(clause.Condition, frame, out SmileValue condition, out SmileRuntimeError? conditionError))
            {
                return conditionError;
            }

            if (condition.BooleanValue)
            {
                return ExecuteStatements(clause.Statements, frame);
            }
        }

        return conditional.HasElseClause
            ? ExecuteStatements(conditional.ElseStatements, frame)
            : null;
    }

    private SmileRuntimeError? ExecuteSelect(BoundSelectStatement select, CallFrame? frame)
    {
        if (!TryEvaluateExpression(select.Selector, frame, out SmileValue selector, out SmileRuntimeError? selectorError))
        {
            return selectorError;
        }

        BoundSelectCaseClause? fallback = null;
        foreach (BoundSelectCaseClause clause in select.Cases)
        {
            if (clause.IsElse)
            {
                fallback = clause;
                continue;
            }

            if (clause.Value is SmileValue value && ValuesEqual(selector, value))
            {
                return ExecuteStatements(clause.Statements, frame);
            }
        }

        return fallback is null ? null : ExecuteStatements(fallback.Statements, frame);
    }

    private SmileRuntimeError? ExecuteFor(BoundForStatement loop, CallFrame? frame)
    {
        if (!TryEvaluateExpression(loop.LowerBound, frame, out SmileValue lower, out SmileRuntimeError? lowerError))
        {
            return lowerError;
        }

        if (!TryEvaluateExpression(loop.UpperBound, frame, out SmileValue upper, out SmileRuntimeError? upperError))
        {
            return upperError;
        }

        long counter = lower.IntegerValue;
        SetValue(loop.Counter, frame, SmileValue.FromInteger(counter));
        while (loop.IsDescending ? counter >= upper.IntegerValue : counter <= upper.IntegerValue)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (!TryConsumeBudget(out SmileRuntimeError? budgetError))
            {
                return budgetError;
            }

            try
            {
                SmileRuntimeError? bodyError = ExecuteStatements(loop.Statements, frame);
                if (bodyError is not null)
                {
                    return bodyError;
                }
            }
            catch (LoopExitSignal signal) when (signal.Kind is BoundExitKind.For)
            {
                return null;
            }

            try
            {
                counter = loop.IsDescending ? checked(counter - 1) : checked(counter + 1);
            }
            catch (OverflowException)
            {
                return OverflowError();
            }

            SetValue(loop.Counter, frame, SmileValue.FromInteger(counter));
        }

        return null;
    }

    private SmileRuntimeError? ExecuteDo(BoundDoStatement loop, CallFrame? frame)
    {
        while (true)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (!TryConsumeBudget(out SmileRuntimeError? budgetError))
            {
                return budgetError;
            }

            try
            {
                SmileRuntimeError? bodyError = ExecuteStatements(loop.Statements, frame);
                if (bodyError is not null)
                {
                    return bodyError;
                }
            }
            catch (LoopExitSignal signal) when (signal.Kind is BoundExitKind.Do)
            {
                return null;
            }

            if (loop.UntilCondition is null)
            {
                continue;
            }

            if (!TryEvaluateExpression(loop.UntilCondition, frame, out SmileValue condition, out SmileRuntimeError? conditionError))
            {
                return conditionError;
            }

            if (condition.BooleanValue)
            {
                return null;
            }
        }
    }

    private bool TryEvaluateExpression(
        BoundExpression expression,
        CallFrame? frame,
        out SmileValue value,
        out SmileRuntimeError? error)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        switch (expression)
        {
            case BoundStringLiteralExpression literal:
                value = SmileValue.FromString(literal.Value);
                return Success(out error);
            case BoundIntegerLiteralExpression literal:
                value = SmileValue.FromInteger(literal.Value);
                return Success(out error);
            case BoundBooleanLiteralExpression literal:
                value = SmileValue.FromBoolean(literal.Value);
                return Success(out error);
            case BoundVariableExpression variable:
                value = GetValue(variable.Variable, frame);
                return Success(out error);
            case BoundArrayExpression arrayExpression:
                if (!TryEvaluateArrayIndices(arrayExpression.Indices, frame, out long[]? requestedIndices, out error))
                {
                    value = default;
                    return false;
                }

                if (!TryGetArrayElement(arrayExpression.Array, requestedIndices!, frame, out SmileValue[]? array, out int index, out error))
                {
                    value = default;
                    return false;
                }

                value = array![index];
                return true;
            case BoundIntrinsicExpression intrinsic:
                return TryEvaluateIntrinsic(intrinsic, frame, out value, out error);
            case BoundCallExpression call:
                if (!TryEvaluateArguments(call.Arguments, frame, out SmileValue[]? arguments, out error))
                {
                    value = default;
                    return false;
                }

                return TryInvoke(call.Routine, arguments!, out value, out error);
            case BoundUnaryExpression unary:
                if (!TryEvaluateExpression(unary.Operand, frame, out SmileValue operand, out error))
                {
                    value = default;
                    return false;
                }

                try
                {
                    value = unary.Operator.Kind switch
                    {
                        BoundUnaryOperatorKind.Identity => operand,
                        BoundUnaryOperatorKind.Negation => SmileValue.FromInteger(checked(-operand.IntegerValue)),
                        BoundUnaryOperatorKind.LogicalNegation => SmileValue.FromBoolean(!operand.BooleanValue),
                        _ => throw new InvalidOperationException("Unknown unary operator.")
                    };
                    return true;
                }
                catch (OverflowException)
                {
                    value = default;
                    error = OverflowError();
                    return false;
                }
            case BoundBinaryExpression binary:
                return TryEvaluateBinary(binary, frame, out value, out error);
            default:
                throw new InvalidOperationException(
                    $"Unsupported expression reached the Core BASIC evaluator: {expression.GetType().Name}.");
        }
    }

    private bool TryEvaluateBinary(
        BoundBinaryExpression binary,
        CallFrame? frame,
        out SmileValue value,
        out SmileRuntimeError? error)
    {
        if (!TryEvaluateExpression(binary.Left, frame, out SmileValue left, out error))
        {
            value = default;
            return false;
        }

        if (binary.Operator.Kind is BoundBinaryOperatorKind.LogicalAnd && !left.BooleanValue)
        {
            value = SmileValue.FromBoolean(false);
            return Success(out error);
        }

        if (binary.Operator.Kind is BoundBinaryOperatorKind.LogicalOr && left.BooleanValue)
        {
            value = SmileValue.FromBoolean(true);
            return Success(out error);
        }

        if (!TryEvaluateExpression(binary.Right, frame, out SmileValue right, out error))
        {
            value = default;
            return false;
        }

        try
        {
            value = binary.Operator.Kind switch
            {
                BoundBinaryOperatorKind.Addition => SmileValue.FromInteger(checked(left.IntegerValue + right.IntegerValue)),
                BoundBinaryOperatorKind.Subtraction => SmileValue.FromInteger(checked(left.IntegerValue - right.IntegerValue)),
                BoundBinaryOperatorKind.Multiplication => SmileValue.FromInteger(checked(left.IntegerValue * right.IntegerValue)),
                BoundBinaryOperatorKind.Division => right.IntegerValue == 0
                    ? throw new DivideByZeroException()
                    : SmileValue.FromInteger(CheckedDivision(left.IntegerValue, right.IntegerValue)),
                BoundBinaryOperatorKind.Modulo => right.IntegerValue == 0
                    ? throw new DivideByZeroException()
                    : SmileValue.FromInteger(CheckedModulo(left.IntegerValue, right.IntegerValue)),
                BoundBinaryOperatorKind.StringConcatenation => SmileValue.FromString(left.StringValue + right.StringValue),
                BoundBinaryOperatorKind.Equality => SmileValue.FromBoolean(ValuesEqual(left, right)),
                BoundBinaryOperatorKind.Inequality => SmileValue.FromBoolean(!ValuesEqual(left, right)),
                BoundBinaryOperatorKind.Less => SmileValue.FromBoolean(Compare(left, right) < 0),
                BoundBinaryOperatorKind.LessOrEquals => SmileValue.FromBoolean(Compare(left, right) <= 0),
                BoundBinaryOperatorKind.Greater => SmileValue.FromBoolean(Compare(left, right) > 0),
                BoundBinaryOperatorKind.GreaterOrEquals => SmileValue.FromBoolean(Compare(left, right) >= 0),
                BoundBinaryOperatorKind.LogicalAnd => SmileValue.FromBoolean(left.BooleanValue && right.BooleanValue),
                BoundBinaryOperatorKind.LogicalOr => SmileValue.FromBoolean(left.BooleanValue || right.BooleanValue),
                _ => throw new InvalidOperationException("Unknown binary operator.")
            };
            return Success(out error);
        }
        catch (DivideByZeroException)
        {
            value = default;
            error = new SmileRuntimeError("SMILER1207", "Division by zero.");
            return false;
        }
        catch (OverflowException)
        {
            value = default;
            error = OverflowError();
            return false;
        }
    }

    private bool TryEvaluateIntrinsic(
        BoundIntrinsicExpression intrinsic,
        CallFrame? frame,
        out SmileValue value,
        out SmileRuntimeError? error)
    {
        if (intrinsic.Kind is BoundIntrinsicKind.Timer)
        {
            value = SmileValue.FromInteger(_host.MonotonicMilliseconds);
            return Success(out error);
        }

        if (!TryEvaluateArguments(intrinsic.Arguments, frame, out SmileValue[]? arguments, out error))
        {
            value = default;
            return false;
        }

        try
        {
            long result = intrinsic.Kind switch
            {
                BoundIntrinsicKind.Abs => checked(Math.Abs(arguments![0].IntegerValue)),
                BoundIntrinsicKind.Min => Math.Min(arguments![0].IntegerValue, arguments[1].IntegerValue),
                BoundIntrinsicKind.Max => Math.Max(arguments![0].IntegerValue, arguments[1].IntegerValue),
                _ => throw new InvalidOperationException("Unknown SMILE intrinsic.")
            };
            value = SmileValue.FromInteger(result);
            return Success(out error);
        }
        catch (OverflowException)
        {
            value = default;
            error = OverflowError();
            return false;
        }
    }

    private bool TryEvaluateArrayIndices(
        IReadOnlyList<BoundExpression> expressions,
        CallFrame? frame,
        out long[]? indices,
        out SmileRuntimeError? error)
    {
        indices = new long[expressions.Count];
        for (int position = 0; position < expressions.Count; position++)
        {
            if (!TryEvaluateExpression(expressions[position], frame, out SmileValue value, out error))
            {
                indices = null;
                return false;
            }

            indices[position] = value.IntegerValue;
        }

        error = null;
        return true;
    }

    private bool TryEvaluateArguments(
        IReadOnlyList<BoundExpression> expressions,
        CallFrame? caller,
        out SmileValue[]? values,
        out SmileRuntimeError? error)
    {
        values = new SmileValue[expressions.Count];
        for (int index = 0; index < expressions.Count; index++)
        {
            if (!TryEvaluateExpression(expressions[index], caller, out values[index], out error))
            {
                values = null;
                return false;
            }
        }

        error = null;
        return true;
    }

    private bool TryInvoke(
        RoutineSymbol symbol,
        IReadOnlyList<SmileValue> arguments,
        out SmileValue value,
        out SmileRuntimeError? error)
    {
        BoundRoutineDeclaration routine = _routines[symbol];
        var frame = new CallFrame();
        foreach (VariableSymbol local in routine.Locals)
        {
            if (local.IsArray)
            {
                frame.Arrays[local] = CreateArray(local);
            }
            else
            {
                frame.Values[local] = DefaultValue(local.Type);
            }
        }

        for (int index = 0; index < symbol.Parameters.Count; index++)
        {
            frame.Values[symbol.Parameters[index]] = arguments[index];
        }

        try
        {
            error = ExecuteStatements(routine.Statements, frame);
            if (error is not null)
            {
                value = default;
                return false;
            }
        }
        catch (RoutineReturnSignal signal)
        {
            value = signal.Value ?? DefaultValue(symbol.ReturnType ?? SmileType.Integer);
            error = null;
            return true;
        }

        if (symbol.IsFunction)
        {
            value = default;
            error = new SmileRuntimeError("SMILER1212", $"Function '{symbol.Name}' completed without returning a value.");
            return false;
        }

        value = default;
        error = null;
        return true;
    }

    private SmileValue GetValue(VariableSymbol variable, CallFrame? frame) =>
        variable.IsGlobal ? _globalValues[variable] : frame!.Values[variable];

    private void SetValue(VariableSymbol variable, CallFrame? frame, SmileValue value)
    {
        if (variable.IsGlobal)
        {
            _globalValues[variable] = value;
        }
        else
        {
            frame!.Values[variable] = value;
        }
    }

    private SmileValue[] GetArray(VariableSymbol variable, CallFrame? frame) =>
        variable.IsGlobal ? _globalArrays[variable] : frame!.Arrays[variable];

    private bool TryGetArrayElement(
        VariableSymbol variable,
        IReadOnlyList<long> requestedIndices,
        CallFrame? frame,
        out SmileValue[]? array,
        out int index,
        out SmileRuntimeError? error)
    {
        if (requestedIndices.Count != variable.ArrayRank)
        {
            array = null;
            index = 0;
            error = new SmileRuntimeError(
                "SMILER1210",
                $"Array '{variable.Name}' requires {variable.ArrayRank} index value(s).");
            return false;
        }

        for (int position = 0; position < requestedIndices.Count; position++)
        {
            long requestedIndex = requestedIndices[position];
            int length = position == 0 ? variable.ArrayLength : variable.ArraySecondLength;
            if (requestedIndex < 0 || requestedIndex >= length)
            {
                array = null;
                index = 0;
                error = new SmileRuntimeError(
                    "SMILER1210",
                    $"Array index {requestedIndex} for dimension {position + 1} is outside the valid range 0 through {length - 1} for '{variable.Name}'.");
                return false;
            }
        }

        array = GetArray(variable, frame);
        index = variable.ArrayRank == 1
            ? (int)requestedIndices[0]
            : checked((int)(requestedIndices[0] * variable.ArraySecondLength + requestedIndices[1]));
        error = null;
        return true;
    }

    private static SmileValue[] CreateArray(VariableSymbol variable)
    {
        SmileValue[] values = new SmileValue[variable.TotalElementCount];
        Array.Fill(values, DefaultValue(variable.Type));
        return values;
    }

    private static SmileValue DefaultValue(SmileType type) => type switch
    {
        SmileType.Integer => SmileValue.FromInteger(0),
        SmileType.Boolean => SmileValue.FromBoolean(false),
        _ => SmileValue.FromString(string.Empty)
    };

    private static bool ValuesEqual(SmileValue left, SmileValue right) => left.Type switch
    {
        SmileType.Integer => left.IntegerValue == right.IntegerValue,
        SmileType.Boolean => left.BooleanValue == right.BooleanValue,
        SmileType.String => string.Equals(left.StringValue, right.StringValue, StringComparison.Ordinal),
        _ => false
    };

    private static int Compare(SmileValue left, SmileValue right) => left.Type switch
    {
        SmileType.Integer => left.IntegerValue.CompareTo(right.IntegerValue),
        SmileType.String => string.CompareOrdinal(left.StringValue, right.StringValue),
        _ => throw new InvalidOperationException("Only Number and Text values can be ordered.")
    };

    private static long CheckedDivision(long left, long right) => checked(left / right);

    private static long CheckedModulo(long left, long right) => checked(left % right);

    private static SmileRuntimeError OverflowError() =>
        new("SMILER1206", "Number arithmetic overflow.");

    private bool TryConsumeBudget(out SmileRuntimeError? error)
    {
        if (_remainingStatements-- > 0)
        {
            error = null;
            return true;
        }

        error = new SmileRuntimeError(
            "SMILER1222",
            "The evaluator execution budget was exhausted before the program completed.");
        return false;
    }

    private static bool Success(out SmileRuntimeError? error)
    {
        error = null;
        return true;
    }

    private sealed class CallFrame
    {
        public Dictionary<VariableSymbol, SmileValue> Values { get; } = new();

        public Dictionary<VariableSymbol, SmileValue[]> Arrays { get; } = new();
    }

    private sealed class LoopExitSignal(BoundExitKind kind) : Exception
    {
        public BoundExitKind Kind { get; } = kind;
    }

    private sealed class RoutineReturnSignal(SmileValue? value) : Exception
    {
        public SmileValue? Value { get; } = value;
    }

    private sealed class ProgramEndSignal : Exception;
}
