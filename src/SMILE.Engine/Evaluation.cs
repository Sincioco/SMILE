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

    public EvaluationResult Evaluate(string source) =>
        Evaluate(source, CancellationToken.None);

    public EvaluationResult Evaluate(string source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
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

        var output = new StringBuilder();
        var values = new Dictionary<VariableSymbol, SmileValue>();
        foreach (VariableSymbol variable in bindResult.Program.Variables)
        {
            values[variable] = variable.IsConstant
                ? FindConstantValue(bindResult.Program.SourceItems, variable)
                : DefaultValue(variable.Type);
        }

        SmileRuntimeError? runtimeError;
        try
        {
            runtimeError = ExecuteStatements(
                bindResult.Program.Statements,
                values,
                output,
                cancellationToken);
        }
        catch (ProgramEndSignal)
        {
            runtimeError = null;
        }

        return runtimeError is null
            ? new EvaluationResult(
                Success: true,
                Output: output.ToString(),
                Diagnostics: bindResult.Diagnostics)
            : new EvaluationResult(
                Success: false,
                Output: output.ToString(),
                Diagnostics: bindResult.Diagnostics,
                ErrorOutput: runtimeError + "\n",
                ExitCode: 1,
                RuntimeError: runtimeError);
    }

    private static SmileRuntimeError? ExecuteStatements(
        IReadOnlyList<BoundStatement> statements,
        Dictionary<VariableSymbol, SmileValue> values,
        StringBuilder output,
        CancellationToken cancellationToken)
    {
        foreach (BoundStatement statement in statements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (statement)
            {
                case BoundDimStatement or BoundConstStatement:
                    break;

                case BoundSetStatement assignment:
                    if (!TryEvaluateExpression(
                            assignment.Value,
                            values,
                            out SmileValue assignedValue,
                            out SmileRuntimeError? assignmentError))
                    {
                        return assignmentError;
                    }

                    values[assignment.Variable] = assignedValue;
                    break;

                case BoundCorePrintStatement print:
                    foreach (BoundExpression expression in print.Values)
                    {
                        if (!TryEvaluateExpression(
                                expression,
                                values,
                                out SmileValue printedValue,
                                out SmileRuntimeError? printError))
                        {
                            return printError;
                        }

                        output.Append(printedValue.ToDisplayText());
                    }

                    if (!print.SuppressNewLine)
                    {
                        output.Append('\n');
                    }

                    break;

                case BoundIfStatement conditional:
                    SmileRuntimeError? ifError = ExecuteIf(
                        conditional,
                        values,
                        output,
                        cancellationToken);
                    if (ifError is not null)
                    {
                        return ifError;
                    }

                    break;

                case BoundForStatement loop:
                    SmileRuntimeError? forError = ExecuteFor(
                        loop,
                        values,
                        output,
                        cancellationToken);
                    if (forError is not null)
                    {
                        return forError;
                    }

                    break;

                case BoundDoStatement loop:
                    SmileRuntimeError? doError = ExecuteDo(
                        loop,
                        values,
                        output,
                        cancellationToken);
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

    private static SmileRuntimeError? ExecuteIf(
        BoundIfStatement conditional,
        Dictionary<VariableSymbol, SmileValue> values,
        StringBuilder output,
        CancellationToken cancellationToken)
    {
        foreach (BoundConditionalClause clause in conditional.Clauses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryEvaluateExpression(
                    clause.Condition,
                    values,
                    out SmileValue condition,
                    out SmileRuntimeError? conditionError))
            {
                return conditionError;
            }

            if (condition.BooleanValue)
            {
                return ExecuteStatements(
                    clause.Statements,
                    values,
                    output,
                    cancellationToken);
            }
        }

        return conditional.HasElseClause
            ? ExecuteStatements(
                conditional.ElseStatements,
                values,
                output,
                cancellationToken)
            : null;
    }

    private static SmileRuntimeError? ExecuteFor(
        BoundForStatement loop,
        Dictionary<VariableSymbol, SmileValue> values,
        StringBuilder output,
        CancellationToken cancellationToken)
    {
        if (!TryEvaluateExpression(loop.LowerBound, values, out SmileValue lower, out SmileRuntimeError? lowerError))
        {
            return lowerError;
        }

        if (!TryEvaluateExpression(loop.UpperBound, values, out SmileValue upper, out SmileRuntimeError? upperError))
        {
            return upperError;
        }

        long counter = lower.IntegerValue;
        values[loop.Counter] = SmileValue.FromInteger(counter);
        while (loop.IsDescending ? counter >= upper.IntegerValue : counter <= upper.IntegerValue)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                SmileRuntimeError? bodyError = ExecuteStatements(
                    loop.Statements,
                    values,
                    output,
                    cancellationToken);
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
                return new SmileRuntimeError("SMILER1206", "Number arithmetic overflow.");
            }

            values[loop.Counter] = SmileValue.FromInteger(counter);
        }

        return null;
    }

    private static SmileRuntimeError? ExecuteDo(
        BoundDoStatement loop,
        Dictionary<VariableSymbol, SmileValue> values,
        StringBuilder output,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                SmileRuntimeError? bodyError = ExecuteStatements(
                    loop.Statements,
                    values,
                    output,
                    cancellationToken);
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

            if (!TryEvaluateExpression(
                    loop.UntilCondition,
                    values,
                    out SmileValue condition,
                    out SmileRuntimeError? conditionError))
            {
                return conditionError;
            }

            if (condition.BooleanValue)
            {
                return null;
            }
        }
    }

    private static SmileValue FindConstantValue(
        IReadOnlyList<BoundSourceItem> items,
        VariableSymbol variable)
    {
        foreach (BoundSourceItem item in items)
        {
            switch (item)
            {
                case BoundConstStatement constant when constant.Variable.Equals(variable):
                    return constant.Value;
                case BoundIfStatement conditional:
                    foreach (BoundConditionalClause clause in conditional.Clauses)
                    {
                        SmileValue found = FindConstantValue(clause.SourceItems, variable);
                        if (found.Type == variable.Type)
                        {
                            return found;
                        }
                    }

                    break;
                case BoundForStatement loop:
                    SmileValue forValue = FindConstantValue(loop.SourceItems, variable);
                    if (forValue.Type == variable.Type)
                    {
                        return forValue;
                    }

                    break;
                case BoundDoStatement loop:
                    SmileValue doValue = FindConstantValue(loop.SourceItems, variable);
                    if (doValue.Type == variable.Type)
                    {
                        return doValue;
                    }

                    break;
            }
        }

        return DefaultValue(variable.Type);
    }

    private static SmileValue DefaultValue(SmileType type) => type switch
    {
        SmileType.Integer => SmileValue.FromInteger(0),
        SmileType.Boolean => SmileValue.FromBoolean(false),
        _ => SmileValue.FromString(string.Empty)
    };

    private static bool TryEvaluateExpression(
        BoundExpression expression,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values,
        out SmileValue value,
        out SmileRuntimeError? error)
    {
        StaticEvaluationResult result = BoundExpressionEvaluator.Evaluate(expression, values);
        if (result.IsKnown && !result.MayFailAtRuntime)
        {
            value = result.Value;
            error = null;
            return true;
        }

        if (result.IsInvalid && result.Error is SmileArithmeticError arithmeticError)
        {
            value = default;
            error = new SmileRuntimeError(arithmeticError.RuntimeCode, arithmeticError.Message);
            return false;
        }

        throw new InvalidOperationException(
            "A reached bound expression remained unknown during evaluation.");
    }

    private sealed class LoopExitSignal(BoundExitKind kind) : Exception
    {
        public BoundExitKind Kind { get; } = kind;
    }

    private sealed class ProgramEndSignal : Exception;
}
