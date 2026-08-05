using System.Collections.ObjectModel;

namespace SMILE.Engine;

// One trace entry describes the value calculated by a statement and the
// environment on each side of it. SET is intentionally evaluated against the
// old snapshot and appears in ValuesAfter only once the complete right side
// succeeds.
public sealed record BoundStatementExecution(
    BoundStatement Statement,
    IReadOnlyDictionary<VariableSymbol, SmileValue> ValuesBefore,
    SmileValue Value,
    IReadOnlyDictionary<VariableSymbol, SmileValue> ValuesAfter);

// SMILE v0.5.1.1 has mutable runtime variables but still no input, branches,
// loops, functions, or other unknown runtime data. This small source-order
// analysis therefore gives every optimization and target the same current
// values without pretending a LET initializer remains the variable's value.
public sealed class BoundProgramExecutionTrace
{
    private BoundProgramExecutionTrace(
        IReadOnlyList<BoundStatementExecution> steps,
        IReadOnlyDictionary<VariableSymbol, IReadOnlyList<SmileValue>> assignedValues,
        IReadOnlySet<VariableSymbol> mutatedVariables,
        IReadOnlyDictionary<VariableSymbol, SmileValue> finalValues)
    {
        Steps = steps;
        AssignedValues = assignedValues;
        MutatedVariables = mutatedVariables;
        FinalValues = finalValues;
    }

    public IReadOnlyList<BoundStatementExecution> Steps { get; }

    public IReadOnlyDictionary<VariableSymbol, IReadOnlyList<SmileValue>> AssignedValues { get; }

    public IReadOnlySet<VariableSymbol> MutatedVariables { get; }

    public IReadOnlyDictionary<VariableSymbol, SmileValue> FinalValues { get; }

    public static BoundProgramExecutionTrace Create(BoundProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);

        var builder = new BoundProgramExecutionTraceBuilder();
        foreach (BoundStatement statement in program.Statements)
        {
            if (!builder.TryAppend(statement))
            {
                throw new InvalidOperationException(
                    "A successfully bound SMILE program could not be analyzed sequentially.");
            }
        }

        return builder.Build();
    }

    internal static BoundProgramExecutionTrace Create(
        BoundProgramExecutionTraceBuilder builder) =>
        builder.Build();

    internal static IReadOnlyDictionary<VariableSymbol, SmileValue> Snapshot(
        IReadOnlyDictionary<VariableSymbol, SmileValue> values) =>
        new ReadOnlyDictionary<VariableSymbol, SmileValue>(
            new Dictionary<VariableSymbol, SmileValue>(values));

    internal static BoundProgramExecutionTrace From(
        IReadOnlyList<BoundStatementExecution> steps,
        IReadOnlyDictionary<VariableSymbol, List<SmileValue>> assignedValues,
        IReadOnlySet<VariableSymbol> mutatedVariables,
        IReadOnlyDictionary<VariableSymbol, SmileValue> finalValues)
    {
        var readOnlyAssignedValues = new ReadOnlyDictionary<VariableSymbol, IReadOnlyList<SmileValue>>(
            assignedValues.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<SmileValue>)Array.AsReadOnly(pair.Value.ToArray())));

        return new BoundProgramExecutionTrace(
            Array.AsReadOnly(steps.ToArray()),
            readOnlyAssignedValues,
            new HashSet<VariableSymbol>(mutatedVariables),
            Snapshot(finalValues));
    }
}

// The binder uses the same incremental trace logic so a failed LET does not
// leak a declaration and a failed SET does not replace the previous value.
// Full-program consumers call BoundProgramExecutionTrace.Create instead.
internal sealed class BoundProgramExecutionTraceBuilder
{
    private readonly Dictionary<VariableSymbol, SmileValue> _values = new();
    private readonly List<BoundStatementExecution> _steps = new();
    private readonly Dictionary<VariableSymbol, List<SmileValue>> _assignedValues = new();
    private readonly HashSet<VariableSymbol> _mutatedVariables = new();

    public IReadOnlyDictionary<VariableSymbol, SmileValue> CurrentValues => _values;

    public bool TryAppend(
        BoundStatement statement,
        ICollection<Diagnostic>? diagnostics = null)
    {
        BoundExpression expression = statement switch
        {
            BoundLetStatement let => let.Initializer,
            BoundSetStatement set => set.Value,
            BoundPrintStatement print => print.Value,
            _ => new BoundErrorExpression()
        };

        IReadOnlyDictionary<VariableSymbol, SmileValue> before =
            BoundProgramExecutionTrace.Snapshot(_values);

        if (!BoundExpressionEvaluator.TryEvaluate(expression, _values, out SmileValue value, diagnostics))
        {
            return false;
        }

        switch (statement)
        {
            case BoundLetStatement let:
                _values.Add(let.Variable, value);
                RecordAssignedValue(let.Variable, value);
                break;

            case BoundSetStatement set:
                // Evaluation above used the old dictionary. Updating here is
                // the atomic assignment boundary shared by every compiler pass.
                _values[set.Variable] = value;
                _mutatedVariables.Add(set.Variable);
                RecordAssignedValue(set.Variable, value);
                break;
        }

        _steps.Add(new BoundStatementExecution(
            statement,
            before,
            value,
            BoundProgramExecutionTrace.Snapshot(_values)));
        return true;
    }

    public BoundProgramExecutionTrace Build() =>
        BoundProgramExecutionTrace.From(
            _steps,
            _assignedValues,
            _mutatedVariables,
            _values);

    private void RecordAssignedValue(VariableSymbol variable, SmileValue value)
    {
        if (!_assignedValues.TryGetValue(variable, out List<SmileValue>? values))
        {
            values = new List<SmileValue>();
            _assignedValues.Add(variable, values);
        }

        values.Add(value);
    }
}
