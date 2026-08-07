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

// The exact execution trace follows the branch selected by today's source-only
// program. BoundProgramAnalysis separately merges every syntactic path so
// optimizers cannot confuse this concrete reference run with a value proved
// across all possible future runtime inputs.
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

        if (BoundStatementTree.Enumerate(program).Any(statement => statement is BoundInputStatement))
        {
            throw new InvalidOperationException(
                "A concrete source-only execution trace cannot evaluate INPUT. " +
                "Use SmileEvaluator with an injected input source for runtime programs.");
        }

        if (BoundStatementTree.Enumerate(program).Any(statement => statement is BoundWhileStatement))
        {
            // A concrete compiler trace must never discover loop results by
            // running arbitrary learner code: a valid WHILE may be infinite or
            // INPUT-dependent. BoundProgramAnalysis provides structural
            // zero-or-more facts, while SmileEvaluator owns actual execution.
            throw new InvalidOperationException(
                "A concrete source-only execution trace cannot execute WHILE. " +
                "Use BoundProgramAnalysis for compiler facts or SmileEvaluator for runtime execution.");
        }

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

// This builder retains the concrete source-only trace used by educational and
// regression consumers. Binder and BoundProgramAnalysis use their separate
// Known/Unknown/Invalid models once INPUT can introduce runtime values.
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
        if (statement is BoundIfStatement conditional)
        {
            return TryAppendIf(conditional, diagnostics);
        }

        if (statement is BoundWhileStatement)
        {
            return false;
        }

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

    private bool TryAppendIf(
        BoundIfStatement conditional,
        ICollection<Diagnostic>? diagnostics)
    {
        IReadOnlyDictionary<VariableSymbol, SmileValue> before =
            BoundProgramExecutionTrace.Snapshot(_values);
        var values = new Dictionary<VariableSymbol, SmileValue>(_values);
        var assignedValues = _assignedValues.ToDictionary(
            pair => pair.Key,
            pair => new List<SmileValue>(pair.Value));
        var mutatedVariables = new HashSet<VariableSymbol>(_mutatedVariables);

        if (!TryExecuteIf(
                conditional,
                values,
                assignedValues,
                mutatedVariables,
                diagnostics,
                out bool matchedConditionalClause))
        {
            return false;
        }

        _values.Clear();
        foreach ((VariableSymbol variable, SmileValue value) in values)
        {
            _values.Add(variable, value);
        }

        _assignedValues.Clear();
        foreach ((VariableSymbol variable, List<SmileValue> assigned) in assignedValues)
        {
            _assignedValues.Add(variable, assigned);
        }

        _mutatedVariables.Clear();
        _mutatedVariables.UnionWith(mutatedVariables);

        _steps.Add(new BoundStatementExecution(
            conditional,
            before,
            SmileValue.FromBoolean(matchedConditionalClause),
            BoundProgramExecutionTrace.Snapshot(_values)));
        return true;
    }

    private static bool TryExecuteIf(
        BoundIfStatement conditional,
        Dictionary<VariableSymbol, SmileValue> values,
        Dictionary<VariableSymbol, List<SmileValue>> assignedValues,
        HashSet<VariableSymbol> mutatedVariables,
        ICollection<Diagnostic>? diagnostics,
        out bool matchedConditionalClause)
    {
        foreach (BoundConditionalClause clause in conditional.Clauses)
        {
            if (!BoundExpressionEvaluator.TryEvaluate(
                    clause.Condition,
                    values,
                    out SmileValue condition,
                    diagnostics))
            {
                matchedConditionalClause = false;
                return false;
            }

            if (!condition.BooleanValue)
            {
                continue;
            }

            matchedConditionalClause = true;
            return TryExecuteStatements(
                clause.Statements,
                values,
                assignedValues,
                mutatedVariables,
                diagnostics);
        }

        matchedConditionalClause = false;
        return !conditional.HasElseClause || TryExecuteStatements(
            conditional.ElseStatements,
            values,
            assignedValues,
            mutatedVariables,
            diagnostics);
    }

    private static bool TryExecuteStatements(
        IReadOnlyList<BoundStatement> statements,
        Dictionary<VariableSymbol, SmileValue> values,
        Dictionary<VariableSymbol, List<SmileValue>> assignedValues,
        HashSet<VariableSymbol> mutatedVariables,
        ICollection<Diagnostic>? diagnostics)
    {
        foreach (BoundStatement statement in statements)
        {
            switch (statement)
            {
                case BoundSetStatement set:
                    if (!BoundExpressionEvaluator.TryEvaluate(
                            set.Value,
                            values,
                            out SmileValue assigned,
                            diagnostics))
                    {
                        return false;
                    }

                    values[set.Variable] = assigned;
                    mutatedVariables.Add(set.Variable);
                    RecordAssignedValue(assignedValues, set.Variable, assigned);
                    break;

                case BoundPrintStatement print:
                    if (!BoundExpressionEvaluator.TryEvaluate(
                            print.Value,
                            values,
                            out _,
                            diagnostics))
                    {
                        return false;
                    }

                    break;

                case BoundIfStatement nested:
                    if (!TryExecuteIf(
                            nested,
                            values,
                            assignedValues,
                            mutatedVariables,
                            diagnostics,
                            out _))
                    {
                        return false;
                    }

                    break;

                default:
                    return false;
            }
        }

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
        RecordAssignedValue(_assignedValues, variable, value);
    }

    private static void RecordAssignedValue(
        Dictionary<VariableSymbol, List<SmileValue>> assignedValues,
        VariableSymbol variable,
        SmileValue value)
    {
        if (!assignedValues.TryGetValue(variable, out List<SmileValue>? values))
        {
            values = new List<SmileValue>();
            assignedValues.Add(variable, values);
        }

        values.Add(value);
    }
}
