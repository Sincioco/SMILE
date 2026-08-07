using System.Collections.ObjectModel;

namespace SMILE.Engine;

// Branch analysis needs to distinguish a value proved on every possible path
// from a value that merely happened to be selected by today's source-only
// program. Future INPUT can therefore introduce runtime data without changing
// the meaning of the analysis model.
public readonly record struct AnalyzedValue(bool IsKnown, SmileValue Value)
{
    public static AnalyzedValue Unknown => default;

    public static AnalyzedValue Known(SmileValue value) => new(true, value);
}

public readonly record struct AnalyzedIntegerRange(long Minimum, long Maximum);

public readonly record struct AnalyzedExpressionDisplayFacts(
    int MaximumUtf8ByteLength,
    bool MayContainNul,
    bool HasFiniteMaximumUtf8ByteLength = true);

public sealed record BoundStatementAnalysis(
    int Ordinal,
    IReadOnlyDictionary<VariableSymbol, AnalyzedValue> ValuesBefore,
    AnalyzedValue Value,
    IReadOnlyDictionary<VariableSymbol, AnalyzedValue> ValuesAfter,
    IReadOnlyDictionary<VariableSymbol, SmileValue> ConcreteValuesBefore,
    SmileValue ConcreteValue,
    IReadOnlyDictionary<VariableSymbol, SmileValue> ConcreteValuesAfter,
    bool HasConcreteValue = true);

public sealed record BoundConditionalClauseAnalysis(
    int Ordinal,
    IReadOnlyDictionary<VariableSymbol, AnalyzedValue> ValuesBefore,
    IReadOnlyDictionary<VariableSymbol, SmileValue> ConcreteValuesBefore);

// A loop has two important environments: the stable facts that are valid
// every time control reaches its header, and the conservative zero-or-more
// facts that remain after the loop. Keeping these separate prevents a target
// generator from reusing a LET initializer after the loop body has mutated it.
public sealed record BoundWhileStatementAnalysis(
    int Ordinal,
    IReadOnlyDictionary<VariableSymbol, AnalyzedValue> ValuesAtHead,
    IReadOnlyDictionary<VariableSymbol, AnalyzedValue> ValuesAfter,
    IReadOnlyDictionary<VariableSymbol, SmileValue> ConcreteValuesAtHead,
    bool IncomingConditionIsKnownFalse);

public sealed class BoundProgramAnalysis
{
    private readonly IReadOnlyDictionary<BoundStatement, BoundStatementAnalysis> _statementFacts;
    private readonly IReadOnlyDictionary<BoundConditionalClause, BoundConditionalClauseAnalysis> _clauseFacts;
    private readonly IReadOnlyDictionary<BoundIfStatement, int> _ifOrdinals;
    private readonly IReadOnlyDictionary<BoundWhileStatement, BoundWhileStatementAnalysis> _whileFacts;
    private readonly IReadOnlyDictionary<BoundWhileStatement, int> _whileOrdinals;
    private readonly IReadOnlyList<BoundStatement> _statements;
    private readonly IReadOnlyDictionary<VariableSymbol, int> _maximumAssignedUtf8ByteLengths;
    private readonly IReadOnlySet<VariableSymbol> _assignedValuesThatMayContainNul;
    private readonly IReadOnlyDictionary<BoundExpression, AnalyzedIntegerRange> _integerRanges;
    private readonly IReadOnlyDictionary<BoundExpression, AnalyzedExpressionDisplayFacts> _expressionDisplayFacts;

    private BoundProgramAnalysis(Analyzer analyzer)
    {
        _statementFacts = new ReadOnlyDictionary<BoundStatement, BoundStatementAnalysis>(
            analyzer.StatementFacts);
        _clauseFacts = new ReadOnlyDictionary<BoundConditionalClause, BoundConditionalClauseAnalysis>(
            analyzer.ClauseFacts);
        _ifOrdinals = new ReadOnlyDictionary<BoundIfStatement, int>(analyzer.IfOrdinals);
        _whileFacts = new ReadOnlyDictionary<BoundWhileStatement, BoundWhileStatementAnalysis>(
            analyzer.WhileFacts);
        _whileOrdinals = new ReadOnlyDictionary<BoundWhileStatement, int>(analyzer.WhileOrdinals);
        _statements = Array.AsReadOnly(analyzer.Statements.ToArray());
        AssignedValues = new ReadOnlyDictionary<VariableSymbol, IReadOnlyList<SmileValue>>(
            analyzer.AssignedValues.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<SmileValue>)Array.AsReadOnly(pair.Value.ToArray())));
        MutatedVariables = new HashSet<VariableSymbol>(analyzer.MutatedVariables);
        FinalValues = Snapshot(analyzer.AbstractValues);
        FinalConcreteValues = BoundProgramExecutionTrace.Snapshot(analyzer.ConcreteValues);
        _maximumAssignedUtf8ByteLengths =
            new ReadOnlyDictionary<VariableSymbol, int>(
                new Dictionary<VariableSymbol, int>(analyzer.MaximumAssignedUtf8ByteLengths));
        _assignedValuesThatMayContainNul =
            new HashSet<VariableSymbol>(analyzer.AssignedValuesThatMayContainNul);
        VariablesWithInexactAssignedValues =
            new HashSet<VariableSymbol>(analyzer.VariablesWithInexactAssignedValues);
        Diagnostics = Array.AsReadOnly(analyzer.Diagnostics.ToArray());
        _integerRanges =
            new ReadOnlyDictionary<BoundExpression, AnalyzedIntegerRange>(
                new Dictionary<BoundExpression, AnalyzedIntegerRange>(
                    analyzer.IntegerRanges,
                    ReferenceEqualityComparer.Instance));
        _expressionDisplayFacts =
            new ReadOnlyDictionary<BoundExpression, AnalyzedExpressionDisplayFacts>(
                new Dictionary<BoundExpression, AnalyzedExpressionDisplayFacts>(
                    analyzer.ExpressionDisplayFacts,
                    ReferenceEqualityComparer.Instance));
    }

    public IReadOnlyDictionary<VariableSymbol, IReadOnlyList<SmileValue>> AssignedValues { get; }

    public IReadOnlySet<VariableSymbol> MutatedVariables { get; }

    public IReadOnlyDictionary<VariableSymbol, AnalyzedValue> FinalValues { get; }

    public IReadOnlyDictionary<VariableSymbol, SmileValue> FinalConcreteValues { get; }

    // AssignedValues remains an exact candidate list while that list can be
    // represented without a Cartesian expansion. These variables identify
    // assignments whose complete value set is instead summarized by the
    // storage facts below.
    public IReadOnlySet<VariableSymbol> VariablesWithInexactAssignedValues { get; }

    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    public static BoundProgramAnalysis Create(BoundProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        var analyzer = new Analyzer();
        analyzer.Analyze(program.Statements);
        return new BoundProgramAnalysis(analyzer);
    }

    public BoundStatementAnalysis GetStatementFacts(BoundStatement statement) =>
        _statementFacts[statement];

    public BoundConditionalClauseAnalysis GetClauseFacts(BoundConditionalClause clause) =>
        _clauseFacts[clause];

    public int GetIfOrdinal(BoundIfStatement statement) => _ifOrdinals[statement];

    public BoundWhileStatementAnalysis GetWhileFacts(BoundWhileStatement statement) =>
        _whileFacts[statement];

    public int GetWhileOrdinal(BoundWhileStatement statement) => _whileOrdinals[statement];

    public IReadOnlyList<BoundStatement> EnumerateStatements() => _statements;

    public int MaximumAssignedUtf8ByteLength(VariableSymbol variable) =>
        _maximumAssignedUtf8ByteLengths.TryGetValue(variable, out int length)
            ? length
            : 0;

    public bool AssignedValuesMayContainNul(VariableSymbol variable) =>
        _assignedValuesThatMayContainNul.Contains(variable);

    public AnalyzedIntegerRange GetPossibleIntegerRange(BoundExpression expression) =>
        _integerRanges[expression];

    public AnalyzedExpressionDisplayFacts GetExpressionDisplayFacts(BoundExpression expression) =>
        _expressionDisplayFacts[expression];

    public int MaximumExpressionDisplayUtf8ByteLength(BoundExpression expression) =>
        GetExpressionDisplayFacts(expression).MaximumUtf8ByteLength;

    public bool ExpressionDisplayMayContainNul(BoundExpression expression) =>
        GetExpressionDisplayFacts(expression).MayContainNul;

    private static IReadOnlyDictionary<VariableSymbol, AnalyzedValue> Snapshot(
        IReadOnlyDictionary<VariableSymbol, AnalyzedValue> values) =>
        new ReadOnlyDictionary<VariableSymbol, AnalyzedValue>(
            new Dictionary<VariableSymbol, AnalyzedValue>(values));

    private sealed class Analyzer
    {
        private int _nextClauseOrdinal;
        private int _nextIfOrdinal;
        private int _nextWhileOrdinal;
        private int _nextStatementOrdinal;

        public Dictionary<BoundStatement, BoundStatementAnalysis> StatementFacts { get; } =
            new(ReferenceEqualityComparer.Instance);

        public Dictionary<BoundConditionalClause, BoundConditionalClauseAnalysis> ClauseFacts { get; } =
            new(ReferenceEqualityComparer.Instance);

        public Dictionary<BoundIfStatement, int> IfOrdinals { get; } =
            new(ReferenceEqualityComparer.Instance);

        public Dictionary<BoundWhileStatement, BoundWhileStatementAnalysis> WhileFacts { get; } =
            new(ReferenceEqualityComparer.Instance);

        public Dictionary<BoundWhileStatement, int> WhileOrdinals { get; } =
            new(ReferenceEqualityComparer.Instance);

        public List<Diagnostic> Diagnostics { get; } = new();

        public List<BoundStatement> Statements { get; } = new();

        public Dictionary<VariableSymbol, List<SmileValue>> AssignedValues { get; } = new();

        public HashSet<VariableSymbol> MutatedVariables { get; } = new();

        public Dictionary<VariableSymbol, int> MaximumAssignedUtf8ByteLengths { get; } = new();

        public HashSet<VariableSymbol> AssignedValuesThatMayContainNul { get; } = new();

        public HashSet<VariableSymbol> VariablesWithInexactAssignedValues { get; } = new();

        public Dictionary<BoundExpression, AnalyzedIntegerRange> IntegerRanges { get; } =
            new(ReferenceEqualityComparer.Instance);

        public Dictionary<BoundExpression, AnalyzedExpressionDisplayFacts> ExpressionDisplayFacts { get; } =
            new(ReferenceEqualityComparer.Instance);

        public Dictionary<VariableSymbol, AnalyzedValue> AbstractValues { get; } = new();

        public Dictionary<VariableSymbol, SmileValue> ConcreteValues { get; } = new();

        // Possible values intentionally over-approximate runtime paths. The
        // Known/Unknown environment answers whether one value is proved on
        // every path; this companion environment preserves the finite values
        // needed for String length and embedded-NUL planning after a merge.
        private Dictionary<VariableSymbol, PossibleValueState> PossibleValues { get; } = new();

        public void Analyze(IReadOnlyList<BoundStatement> statements) =>
            AnalyzeStatementList(statements, AbstractValues, ConcreteValues, PossibleValues);

        private void AnalyzeStatementList(
            IReadOnlyList<BoundStatement> statements,
            Dictionary<VariableSymbol, AnalyzedValue> abstractValues,
            Dictionary<VariableSymbol, SmileValue> concreteValues,
            Dictionary<VariableSymbol, PossibleValueState> possibleValues)
        {
            foreach (BoundStatement statement in statements)
            {
                AnalyzeStatement(statement, abstractValues, concreteValues, possibleValues);
            }
        }

        private void AnalyzeStatement(
            BoundStatement statement,
            Dictionary<VariableSymbol, AnalyzedValue> abstractValues,
            Dictionary<VariableSymbol, SmileValue> concreteValues,
            Dictionary<VariableSymbol, PossibleValueState> possibleValues)
        {
            int ordinal = _nextStatementOrdinal++;
            Statements.Add(statement);
            IReadOnlyDictionary<VariableSymbol, AnalyzedValue> abstractBefore =
                Snapshot(abstractValues);
            IReadOnlyDictionary<VariableSymbol, SmileValue> concreteBefore =
                BoundProgramExecutionTrace.Snapshot(concreteValues);
            AnalyzedValue analyzedValue = AnalyzedValue.Unknown;
            SmileValue concreteValue = default;
            bool hasConcreteValue = false;

            switch (statement)
            {
                case BoundLetStatement let:
                    analyzedValue = Evaluate(let.Initializer, abstractValues);
                    if (analyzedValue.IsKnown)
                    {
                        abstractValues.Add(let.Variable, analyzedValue);
                    }
                    else
                    {
                        abstractValues.Add(let.Variable, AnalyzedValue.Unknown);
                    }

                    PossibleValueState possibleInitializers = EvaluatePossible(
                        let.Initializer,
                        possibleValues);
                    possibleValues.Add(let.Variable, possibleInitializers);
                    RecordAssignedValues(let.Variable, possibleInitializers);

                    if (BoundExpressionEvaluator.TryEvaluate(
                            let.Initializer,
                            concreteValues,
                            out concreteValue))
                    {
                        concreteValues.Add(let.Variable, concreteValue);
                        hasConcreteValue = true;
                    }

                    break;

                case BoundSetStatement set:
                    analyzedValue = Evaluate(set.Value, abstractValues);
                    abstractValues[set.Variable] = analyzedValue;
                    MutatedVariables.Add(set.Variable);

                    PossibleValueState possibleAssignments = EvaluatePossible(
                        set.Value,
                        possibleValues);
                    possibleValues[set.Variable] = possibleAssignments;
                    RecordAssignedValues(set.Variable, possibleAssignments);

                    if (BoundExpressionEvaluator.TryEvaluate(
                            set.Value,
                            concreteValues,
                            out concreteValue))
                    {
                        concreteValues[set.Variable] = concreteValue;
                        hasConcreteValue = true;
                    }
                    else
                    {
                        concreteValues.Remove(set.Variable);
                    }

                    break;

                case BoundInputStatement input:
                    abstractValues[input.Variable] = AnalyzedValue.Unknown;
                    concreteValues.Remove(input.Variable);
                    MutatedVariables.Add(input.Variable);

                    PossibleValueState possibleInput = input.Variable.Type switch
                    {
                        SmileType.String => PossibleValueState.Inexact(
                            SmileType.String,
                            SmileLanguage.MaximumInputLineUtf8Bytes,
                            mayContainNul: true),
                        SmileType.Integer => PossibleValueState.InexactInteger(
                            long.MinValue,
                            long.MaxValue),
                        SmileType.Boolean => PossibleValueState.Exact(
                            SmileType.Boolean,
                            new[]
                            {
                                SmileValue.FromBoolean(false),
                                SmileValue.FromBoolean(true)
                            }),
                        _ => PossibleValueState.Inexact(input.Variable.Type)
                    };
                    possibleValues[input.Variable] = possibleInput;
                    RecordAssignedValues(input.Variable, possibleInput);
                    break;

                case BoundPrintStatement print:
                    analyzedValue = Evaluate(print.Value, abstractValues);
                    EvaluatePossible(print.Value, possibleValues);
                    hasConcreteValue = BoundExpressionEvaluator.TryEvaluate(
                        print.Value,
                        concreteValues,
                        out concreteValue);
                    break;

                case BoundIfStatement conditional:
                    IfOrdinals.Add(conditional, _nextIfOrdinal++);
                    hasConcreteValue = AnalyzeIf(
                        conditional,
                        abstractValues,
                        concreteValues,
                        possibleValues);
                    if (hasConcreteValue)
                    {
                        concreteValue = SmileValue.FromBoolean(true);
                    }

                    break;

                case BoundWhileStatement loop:
                    int whileOrdinal = _nextWhileOrdinal++;
                    WhileOrdinals.Add(loop, whileOrdinal);
                    hasConcreteValue = AnalyzeWhile(
                        loop,
                        whileOrdinal,
                        abstractValues,
                        concreteValues,
                        possibleValues);
                    if (hasConcreteValue)
                    {
                        concreteValue = SmileValue.FromBoolean(false);
                    }

                    break;
            }

            StatementFacts.Add(
                statement,
                new BoundStatementAnalysis(
                    ordinal,
                    abstractBefore,
                    analyzedValue,
                    Snapshot(abstractValues),
                    concreteBefore,
                    concreteValue,
                    BoundProgramExecutionTrace.Snapshot(concreteValues),
                    hasConcreteValue));
        }

        private bool AnalyzeIf(
            BoundIfStatement conditional,
            Dictionary<VariableSymbol, AnalyzedValue> abstractValues,
            Dictionary<VariableSymbol, SmileValue> concreteValues,
            Dictionary<VariableSymbol, PossibleValueState> possibleValues)
        {
            var abstractOutgoing = new List<Dictionary<VariableSymbol, AnalyzedValue>>();
            var concreteOutgoing = new List<Dictionary<VariableSymbol, SmileValue>>();
            var possibleOutgoing = new List<Dictionary<VariableSymbol, PossibleValueState>>();

            foreach (BoundConditionalClause clause in conditional.Clauses)
            {
                // Conditions do not mutate the environment, but their complete
                // expression trees still contribute branch-aware Integer range
                // facts used to choose one safe target storage profile.
                EvaluatePossible(clause.Condition, possibleValues);
                ClauseFacts.Add(
                    clause,
                    new BoundConditionalClauseAnalysis(
                        _nextClauseOrdinal++,
                        Snapshot(abstractValues),
                        BoundProgramExecutionTrace.Snapshot(concreteValues)));

                var branchAbstract = new Dictionary<VariableSymbol, AnalyzedValue>(abstractValues);
                var branchConcrete = new Dictionary<VariableSymbol, SmileValue>(concreteValues);
                Dictionary<VariableSymbol, PossibleValueState> branchPossible =
                    ClonePossibleValues(possibleValues);
                AnalyzeStatementList(
                    clause.Statements,
                    branchAbstract,
                    branchConcrete,
                    branchPossible);
                abstractOutgoing.Add(branchAbstract);
                concreteOutgoing.Add(branchConcrete);
                possibleOutgoing.Add(branchPossible);
            }

            Dictionary<VariableSymbol, AnalyzedValue>? elseAbstract = null;
            Dictionary<VariableSymbol, SmileValue>? elseConcrete = null;
            if (conditional.HasElseClause)
            {
                elseAbstract = new Dictionary<VariableSymbol, AnalyzedValue>(abstractValues);
                elseConcrete = new Dictionary<VariableSymbol, SmileValue>(concreteValues);
                Dictionary<VariableSymbol, PossibleValueState> elsePossible =
                    ClonePossibleValues(possibleValues);
                AnalyzeStatementList(
                    conditional.ElseStatements,
                    elseAbstract,
                    elseConcrete,
                    elsePossible);
                abstractOutgoing.Add(elseAbstract);
                concreteOutgoing.Add(elseConcrete);
                possibleOutgoing.Add(elsePossible);
            }
            else
            {
                abstractOutgoing.Add(new Dictionary<VariableSymbol, AnalyzedValue>(abstractValues));
                concreteOutgoing.Add(new Dictionary<VariableSymbol, SmileValue>(concreteValues));
                possibleOutgoing.Add(ClonePossibleValues(possibleValues));
            }

            Merge(abstractValues, abstractOutgoing);
            MergePossibleValues(possibleValues, possibleOutgoing);

            int selectedClause = -1;
            bool selectionIsUnknown = false;
            for (int index = 0; index < conditional.Clauses.Count; index++)
            {
                StaticEvaluationResult condition = BoundExpressionEvaluator.Evaluate(
                    conditional.Clauses[index].Condition,
                    concreteValues);
                if (!condition.IsKnown)
                {
                    selectionIsUnknown = true;
                    break;
                }

                if (condition.Value.BooleanValue)
                {
                    selectedClause = index;
                    break;
                }
            }

            if (selectionIsUnknown)
            {
                MergeConcreteValues(concreteValues, concreteOutgoing);
                return false;
            }

            Dictionary<VariableSymbol, SmileValue> selectedConcrete = selectedClause >= 0
                ? concreteOutgoing[selectedClause]
                : concreteOutgoing[^1];
            ReplaceConcreteValues(concreteValues, selectedConcrete);
            return true;
        }

        private bool AnalyzeWhile(
            BoundWhileStatement loop,
            int ordinal,
            Dictionary<VariableSymbol, AnalyzedValue> abstractValues,
            Dictionary<VariableSymbol, SmileValue> concreteValues,
            Dictionary<VariableSymbol, PossibleValueState> possibleValues)
        {
            var incomingAbstract = new Dictionary<VariableSymbol, AnalyzedValue>(abstractValues);
            var incomingConcrete = new Dictionary<VariableSymbol, SmileValue>(concreteValues);
            Dictionary<VariableSymbol, PossibleValueState> incomingPossible =
                ClonePossibleValues(possibleValues);

            bool incomingConditionIsKnownFalse = IsKnownFalseWithoutFailure(
                loop.Condition,
                incomingAbstract);

            // Phase A is deliberately isolated. It repeatedly transfers the
            // body without consuming statement/IF/WHILE ordinals or recording
            // expression and assignment facts. Widening makes every domain
            // monotone and guarantees a small, deterministic fixed point.
            LoopSolution solution = SolveLoop(
                loop,
                incomingAbstract,
                incomingConcrete,
                incomingPossible);

            // Report the opener before recording nested blocks so multiple
            // loop diagnostics remain in source order as well as being
            // deterministic.
            if (solution.ProducesUnboundedString &&
                !Diagnostics.Any(diagnostic =>
                    diagnostic.Code == "SMILE1612" &&
                    diagnostic.Span.Equals(loop.KeywordSpan)))
            {
                Diagnostics.Add(new Diagnostic(
                    "SMILE1612",
                    DiagnosticSeverity.Error,
                    "A WHILE loop produces a String value without a finite compile-time UTF-8 size bound.",
                    loop.KeywordSpan));
            }

            Dictionary<VariableSymbol, AnalyzedValue> headAbstract =
                incomingConditionIsKnownFalse
                    ? incomingAbstract
                    : solution.AbstractValuesAtHead;
            Dictionary<VariableSymbol, SmileValue> headConcrete =
                incomingConditionIsKnownFalse
                    ? incomingConcrete
                    : solution.ConcreteValuesAtHead;
            Dictionary<VariableSymbol, PossibleValueState> headPossible =
                incomingConditionIsKnownFalse
                    ? incomingPossible
                    : solution.PossibleValuesAtHead;

            // Record the header and body once after the head has stabilized.
            // Targets therefore receive facts that are valid on every visit,
            // while EnumerateStatements remains a structural enumeration.
            EvaluatePossible(loop.Condition, headPossible);
            var bodyAbstract = new Dictionary<VariableSymbol, AnalyzedValue>(headAbstract);
            var bodyConcrete = new Dictionary<VariableSymbol, SmileValue>(headConcrete);
            Dictionary<VariableSymbol, PossibleValueState> bodyPossible =
                ClonePossibleValues(headPossible);
            AnalyzeStatementList(
                loop.Statements,
                bodyAbstract,
                bodyConcrete,
                bodyPossible);

            if (incomingConditionIsKnownFalse)
            {
                ReplaceAbstractValues(abstractValues, incomingAbstract);
                ReplaceConcreteValues(concreteValues, incomingConcrete);
                ReplacePossibleValues(possibleValues, incomingPossible);
            }
            else
            {
                ReplaceAbstractValues(abstractValues, solution.AbstractValuesAtHead);
                ReplaceConcreteValues(concreteValues, solution.ConcreteValuesAtHead);
                ReplacePossibleValues(possibleValues, solution.PossibleValuesAtHead);
            }

            WhileFacts.Add(
                loop,
                new BoundWhileStatementAnalysis(
                    ordinal,
                    Snapshot(headAbstract),
                    Snapshot(abstractValues),
                    BoundProgramExecutionTrace.Snapshot(headConcrete),
                    incomingConditionIsKnownFalse));

            // The compiler intentionally does not execute a possibly-running
            // loop. Only a safely known-false header has an exact concrete
            // statement result at analysis time.
            return incomingConditionIsKnownFalse;
        }

        private LoopSolution SolveLoop(
            BoundWhileStatement loop,
            IReadOnlyDictionary<VariableSymbol, AnalyzedValue> incomingAbstract,
            IReadOnlyDictionary<VariableSymbol, SmileValue> incomingConcrete,
            IReadOnlyDictionary<VariableSymbol, PossibleValueState> incomingPossible)
        {
            var headAbstract = new Dictionary<VariableSymbol, AnalyzedValue>(incomingAbstract);
            var headConcrete = new Dictionary<VariableSymbol, SmileValue>(incomingConcrete);
            Dictionary<VariableSymbol, PossibleValueState> headPossible =
                ClonePossibleValues(incomingPossible);
            var expandedLowerBounds = new HashSet<VariableSymbol>();
            var expandedUpperBounds = new HashSet<VariableSymbol>();
            var expandedStringBounds = new HashSet<VariableSymbol>();
            IReadOnlySet<VariableSymbol> recurrentStringVariables =
                FindRecurrentStringVariables(loop.Statements);
            bool producesUnboundedString = false;

            while (true)
            {
                var bodyAbstract = new Dictionary<VariableSymbol, AnalyzedValue>(headAbstract);
                var bodyConcrete = new Dictionary<VariableSymbol, SmileValue>(headConcrete);
                Dictionary<VariableSymbol, PossibleValueState> bodyPossible =
                    ClonePossibleValues(headPossible);
                TransferStatementList(
                    loop.Statements,
                    bodyAbstract,
                    bodyConcrete,
                    bodyPossible,
                    ref producesUnboundedString);

                var nextAbstract = new Dictionary<VariableSymbol, AnalyzedValue>();
                Merge(
                    nextAbstract,
                    new[]
                    {
                        new Dictionary<VariableSymbol, AnalyzedValue>(incomingAbstract),
                        bodyAbstract
                    });

                var nextConcrete = new Dictionary<VariableSymbol, SmileValue>();
                MergeConcreteValues(
                    nextConcrete,
                    new[]
                    {
                        new Dictionary<VariableSymbol, SmileValue>(incomingConcrete),
                        bodyConcrete
                    });

                var nextPossible = new Dictionary<VariableSymbol, PossibleValueState>();
                MergePossibleValues(
                    nextPossible,
                    new[]
                    {
                        ClonePossibleValues(incomingPossible),
                        bodyPossible
                    },
                    recordInexactFacts: false);
                ApplyLoopWidening(
                    headPossible,
                    nextPossible,
                    expandedLowerBounds,
                    expandedUpperBounds,
                    expandedStringBounds,
                    recurrentStringVariables,
                    ref producesUnboundedString);

                bool stable =
                    AbstractEnvironmentsEqual(headAbstract, nextAbstract) &&
                    ConcreteEnvironmentsEqual(headConcrete, nextConcrete) &&
                    PossibleEnvironmentsEqual(headPossible, nextPossible);
                headAbstract = nextAbstract;
                headConcrete = nextConcrete;
                headPossible = nextPossible;
                if (stable)
                {
                    break;
                }
            }

            return new LoopSolution(
                headAbstract,
                headConcrete,
                headPossible,
                producesUnboundedString);
        }

        private void TransferStatementList(
            IReadOnlyList<BoundStatement> statements,
            Dictionary<VariableSymbol, AnalyzedValue> abstractValues,
            Dictionary<VariableSymbol, SmileValue> concreteValues,
            Dictionary<VariableSymbol, PossibleValueState> possibleValues,
            ref bool producesUnboundedString)
        {
            foreach (BoundStatement statement in statements)
            {
                switch (statement)
                {
                    case BoundLetStatement let:
                        abstractValues[let.Variable] = Evaluate(let.Initializer, abstractValues);
                        PossibleValueState possibleInitializer = EvaluatePossible(
                            let.Initializer,
                            possibleValues,
                            recordFacts: false);
                        possibleValues[let.Variable] = possibleInitializer;
                        producesUnboundedString |=
                            let.Variable.Type is SmileType.String &&
                            !possibleInitializer.HasFiniteMaximumDisplayUtf8ByteLength;
                        if (BoundExpressionEvaluator.TryEvaluate(
                                let.Initializer,
                                concreteValues,
                                out SmileValue initialValue))
                        {
                            concreteValues[let.Variable] = initialValue;
                        }
                        else
                        {
                            concreteValues.Remove(let.Variable);
                        }

                        break;

                    case BoundSetStatement set:
                        abstractValues[set.Variable] = Evaluate(set.Value, abstractValues);
                        PossibleValueState possibleAssignment = EvaluatePossible(
                            set.Value,
                            possibleValues,
                            recordFacts: false);
                        possibleValues[set.Variable] = possibleAssignment;
                        // Widening normally discovers a growing recurrence at
                        // the loop head. This direct assignment check retains
                        // the same provenance when an enclosing loop has
                        // already widened the incoming state before a nested
                        // WHILE is recorded. Untouched unbounded variables do
                        // not implicate the nested loop.
                        producesUnboundedString |=
                            set.Variable.Type is SmileType.String &&
                            !possibleAssignment.HasFiniteMaximumDisplayUtf8ByteLength;
                        if (BoundExpressionEvaluator.TryEvaluate(
                                set.Value,
                                concreteValues,
                                out SmileValue assignedValue))
                        {
                            concreteValues[set.Variable] = assignedValue;
                        }
                        else
                        {
                            concreteValues.Remove(set.Variable);
                        }

                        break;

                    case BoundInputStatement input:
                        abstractValues[input.Variable] = AnalyzedValue.Unknown;
                        concreteValues.Remove(input.Variable);
                        possibleValues[input.Variable] = PossibleInputValue(input.Variable);
                        break;

                    case BoundPrintStatement print:
                        EvaluatePossible(print.Value, possibleValues, recordFacts: false);
                        break;

                    case BoundIfStatement conditional:
                        TransferIf(
                            conditional,
                            abstractValues,
                            concreteValues,
                            possibleValues,
                            ref producesUnboundedString);
                        break;

                    case BoundWhileStatement nestedLoop:
                        TransferWhile(
                            nestedLoop,
                            abstractValues,
                            concreteValues,
                            possibleValues,
                            ref producesUnboundedString);
                        break;
                }
            }
        }

        private void TransferIf(
            BoundIfStatement conditional,
            Dictionary<VariableSymbol, AnalyzedValue> abstractValues,
            Dictionary<VariableSymbol, SmileValue> concreteValues,
            Dictionary<VariableSymbol, PossibleValueState> possibleValues,
            ref bool producesUnboundedString)
        {
            var abstractOutgoing = new List<Dictionary<VariableSymbol, AnalyzedValue>>();
            var concreteOutgoing = new List<Dictionary<VariableSymbol, SmileValue>>();
            var possibleOutgoing = new List<Dictionary<VariableSymbol, PossibleValueState>>();

            foreach (BoundConditionalClause clause in conditional.Clauses)
            {
                EvaluatePossible(clause.Condition, possibleValues, recordFacts: false);
                var branchAbstract = new Dictionary<VariableSymbol, AnalyzedValue>(abstractValues);
                var branchConcrete = new Dictionary<VariableSymbol, SmileValue>(concreteValues);
                Dictionary<VariableSymbol, PossibleValueState> branchPossible =
                    ClonePossibleValues(possibleValues);
                TransferStatementList(
                    clause.Statements,
                    branchAbstract,
                    branchConcrete,
                    branchPossible,
                    ref producesUnboundedString);
                abstractOutgoing.Add(branchAbstract);
                concreteOutgoing.Add(branchConcrete);
                possibleOutgoing.Add(branchPossible);
            }

            if (conditional.HasElseClause)
            {
                var elseAbstract = new Dictionary<VariableSymbol, AnalyzedValue>(abstractValues);
                var elseConcrete = new Dictionary<VariableSymbol, SmileValue>(concreteValues);
                Dictionary<VariableSymbol, PossibleValueState> elsePossible =
                    ClonePossibleValues(possibleValues);
                TransferStatementList(
                    conditional.ElseStatements,
                    elseAbstract,
                    elseConcrete,
                    elsePossible,
                    ref producesUnboundedString);
                abstractOutgoing.Add(elseAbstract);
                concreteOutgoing.Add(elseConcrete);
                possibleOutgoing.Add(elsePossible);
            }
            else
            {
                abstractOutgoing.Add(new Dictionary<VariableSymbol, AnalyzedValue>(abstractValues));
                concreteOutgoing.Add(new Dictionary<VariableSymbol, SmileValue>(concreteValues));
                possibleOutgoing.Add(ClonePossibleValues(possibleValues));
            }

            Merge(abstractValues, abstractOutgoing);
            MergeConcreteValues(concreteValues, concreteOutgoing);
            MergePossibleValues(
                possibleValues,
                possibleOutgoing,
                recordInexactFacts: false);
        }

        private void TransferWhile(
            BoundWhileStatement loop,
            Dictionary<VariableSymbol, AnalyzedValue> abstractValues,
            Dictionary<VariableSymbol, SmileValue> concreteValues,
            Dictionary<VariableSymbol, PossibleValueState> possibleValues,
            ref bool producesUnboundedString)
        {
            bool knownFalse = IsKnownFalseWithoutFailure(loop.Condition, abstractValues);
            LoopSolution solution = SolveLoop(
                loop,
                abstractValues,
                concreteValues,
                possibleValues);
            producesUnboundedString |= solution.ProducesUnboundedString;
            if (knownFalse)
            {
                return;
            }

            ReplaceAbstractValues(abstractValues, solution.AbstractValuesAtHead);
            ReplaceConcreteValues(concreteValues, solution.ConcreteValuesAtHead);
            ReplacePossibleValues(possibleValues, solution.PossibleValuesAtHead);
        }

        private static PossibleValueState PossibleInputValue(VariableSymbol variable) =>
            variable.Type switch
            {
                SmileType.String => PossibleValueState.Inexact(
                    SmileType.String,
                    SmileLanguage.MaximumInputLineUtf8Bytes,
                    mayContainNul: true),
                SmileType.Integer => PossibleValueState.InexactInteger(
                    long.MinValue,
                    long.MaxValue),
                SmileType.Boolean => PossibleValueState.Exact(
                    SmileType.Boolean,
                    new[]
                    {
                        SmileValue.FromBoolean(false),
                        SmileValue.FromBoolean(true)
                    }),
                _ => PossibleValueState.Inexact(variable.Type)
            };

        private static bool IsKnownFalseWithoutFailure(
            BoundExpression condition,
            IReadOnlyDictionary<VariableSymbol, AnalyzedValue> abstractValues)
        {
            StaticEvaluationResult result = BoundExpressionEvaluator.Evaluate(
                condition,
                KnownValues(abstractValues));
            return result.IsKnown &&
                !result.MayFailAtRuntime &&
                !result.Value.BooleanValue;
        }

        private static IReadOnlyDictionary<VariableSymbol, SmileValue> KnownValues(
            IReadOnlyDictionary<VariableSymbol, AnalyzedValue> abstractValues) =>
            abstractValues
                .Where(pair => pair.Value.IsKnown)
                .ToDictionary(pair => pair.Key, pair => pair.Value.Value);

        private static void ApplyLoopWidening(
            IReadOnlyDictionary<VariableSymbol, PossibleValueState> previous,
            Dictionary<VariableSymbol, PossibleValueState> candidate,
            HashSet<VariableSymbol> expandedLowerBounds,
            HashSet<VariableSymbol> expandedUpperBounds,
            HashSet<VariableSymbol> expandedStringBounds,
            IReadOnlySet<VariableSymbol> recurrentStringVariables,
            ref bool producesUnboundedString)
        {
            foreach (VariableSymbol variable in candidate.Keys.ToArray())
            {
                PossibleValueState next = candidate[variable];
                if (!previous.TryGetValue(variable, out PossibleValueState? prior) ||
                    prior is null)
                {
                    continue;
                }

                if (variable.Type is SmileType.Integer)
                {
                    long minimum = next.MinimumIntegerValue;
                    long maximum = next.MaximumIntegerValue;
                    bool widened = false;
                    if (minimum < prior.MinimumIntegerValue)
                    {
                        if (!expandedLowerBounds.Add(variable))
                        {
                            minimum = long.MinValue;
                            widened = true;
                        }
                    }

                    if (maximum > prior.MaximumIntegerValue)
                    {
                        if (!expandedUpperBounds.Add(variable))
                        {
                            maximum = long.MaxValue;
                            widened = true;
                        }
                    }

                    if (widened)
                    {
                        candidate[variable] = PossibleValueState.InexactInteger(minimum, maximum);
                    }

                    continue;
                }

                if (variable.Type is not SmileType.String)
                {
                    continue;
                }

                if (!next.HasFiniteMaximumDisplayUtf8ByteLength)
                {
                    if (prior.HasFiniteMaximumDisplayUtf8ByteLength)
                    {
                        producesUnboundedString = true;
                    }

                    continue;
                }

                if (next.MaximumDisplayUtf8ByteLength <=
                    prior.MaximumDisplayUtf8ByteLength)
                {
                    continue;
                }

                // A finite bound can legitimately arrive over several loop
                // transfers: A <- B <- C is the simplest example, and an
                // interpolated Integer may grow once more when its range is
                // widened. Only a String dependency cycle containing an
                // operation that can add bytes can grow forever. Pure-copy
                // cycles and acyclic propagation are finite-height domains and
                // are allowed to settle naturally.
                if (!recurrentStringVariables.Contains(variable))
                {
                    continue;
                }

                if (!expandedStringBounds.Add(variable))
                {
                    candidate[variable] = next.WithoutFiniteStringBound();
                    producesUnboundedString = true;
                }
            }
        }

        private static IReadOnlySet<VariableSymbol> FindRecurrentStringVariables(
            IReadOnlyList<BoundStatement> statements)
        {
            var transfer = new Dictionary<VariableSymbol, StringDependencySummary>();
            TransferStringDependencies(statements, transfer);
            StringDependencyEdge[] dependencies = transfer
                .SelectMany(pair => pair.Value.Dependencies.Select(dependency =>
                    new StringDependencyEdge(pair.Key, dependency.Key, dependency.Value)))
                .ToArray();
            if (dependencies.Length == 0)
            {
                return new HashSet<VariableSymbol>();
            }

            VariableSymbol[] variables = dependencies
                .SelectMany(edge => new[] { edge.Target, edge.Source })
                .Distinct()
                .ToArray();
            Dictionary<VariableSymbol, VariableSymbol[]> adjacency = variables.ToDictionary(
                variable => variable,
                variable => dependencies
                    .Where(edge => edge.Target.Equals(variable))
                    .Select(edge => edge.Source)
                    .Distinct()
                    .ToArray());
            Dictionary<VariableSymbol, HashSet<VariableSymbol>> reachable = variables.ToDictionary(
                variable => variable,
                variable => ReachableFrom(variable, adjacency));
            var recurrent = new HashSet<VariableSymbol>();

            foreach (StringDependencyEdge growingEdge in dependencies.Where(edge => edge.MayAddBytes))
            {
                bool closesCycle = growingEdge.Source.Equals(growingEdge.Target) ||
                    reachable[growingEdge.Source].Contains(growingEdge.Target);
                if (!closesCycle)
                {
                    continue;
                }

                // Every member of this strongly connected component carries
                // the growing value, even when the positive assignment is
                // overwritten later and another member is the value visible
                // at the loop head.
                foreach (VariableSymbol variable in variables)
                {
                    bool targetReachesVariable = variable.Equals(growingEdge.Target) ||
                        reachable[growingEdge.Target].Contains(variable);
                    bool variableReachesTarget = variable.Equals(growingEdge.Target) ||
                        reachable[variable].Contains(growingEdge.Target);
                    if (targetReachesVariable && variableReachesTarget)
                    {
                        recurrent.Add(variable);
                    }
                }
            }

            return recurrent;
        }

        private static void TransferStringDependencies(
            IReadOnlyList<BoundStatement> statements,
            Dictionary<VariableSymbol, StringDependencySummary> environment)
        {
            foreach (BoundStatement statement in statements)
            {
                switch (statement)
                {
                    case BoundSetStatement set when set.Variable.Type is SmileType.String:
                        environment[set.Variable] = SummarizeStringDependencies(
                            set.Value,
                            environment);
                        break;

                    case BoundInputStatement input when input.Variable.Type is SmileType.String:
                        // INPUT is a finite reset: it can produce non-empty
                        // text, but it carries no loop-head String dependency.
                        environment[input.Variable] = StringDependencySummary.IndependentValue();
                        break;

                    case BoundIfStatement conditional:
                        var outgoing = new List<Dictionary<VariableSymbol, StringDependencySummary>>();
                        foreach (BoundConditionalClause clause in conditional.Clauses)
                        {
                            Dictionary<VariableSymbol, StringDependencySummary> branch =
                                CloneStringDependencyEnvironment(environment);
                            TransferStringDependencies(clause.Statements, branch);
                            outgoing.Add(branch);
                        }

                        if (conditional.HasElseClause)
                        {
                            Dictionary<VariableSymbol, StringDependencySummary> branch =
                                CloneStringDependencyEnvironment(environment);
                            TransferStringDependencies(conditional.ElseStatements, branch);
                            outgoing.Add(branch);
                        }
                        else
                        {
                            outgoing.Add(CloneStringDependencyEnvironment(environment));
                        }

                        MergeStringDependencyEnvironments(environment, outgoing);
                        break;

                    case BoundWhileStatement loop:
                        SolveStringDependencyLoop(loop.Statements, environment);
                        break;
                }
            }
        }

        private static void SolveStringDependencyLoop(
            IReadOnlyList<BoundStatement> statements,
            Dictionary<VariableSymbol, StringDependencySummary> environment)
        {
            Dictionary<VariableSymbol, StringDependencySummary> incoming =
                CloneStringDependencyEnvironment(environment);
            Dictionary<VariableSymbol, StringDependencySummary> head =
                CloneStringDependencyEnvironment(environment);

            while (true)
            {
                Dictionary<VariableSymbol, StringDependencySummary> body =
                    CloneStringDependencyEnvironment(head);
                TransferStringDependencies(statements, body);
                var next = new Dictionary<VariableSymbol, StringDependencySummary>();
                MergeStringDependencyEnvironments(next, new[] { incoming, body });
                if (StringDependencyEnvironmentsEqual(head, next))
                {
                    ReplaceStringDependencyEnvironment(environment, next);
                    return;
                }

                head = next;
            }
        }

        private static StringDependencySummary SummarizeStringDependencies(
            BoundExpression expression,
            IReadOnlyDictionary<VariableSymbol, StringDependencySummary> environment)
        {
            return expression switch
            {
                BoundStringLiteralExpression literal =>
                    StringDependencySummary.Literal(literal.Value.Length > 0),
                BoundVariableExpression variable when variable.Type is SmileType.String =>
                    ResolveStringDependency(variable.Variable, environment).Clone(),
                BoundBinaryExpression binary
                    when binary.Operator.Kind is BoundBinaryOperatorKind.StringConcatenation =>
                    StringDependencySummary.Concatenate(
                        SummarizeStringDependencies(binary.Left, environment),
                        SummarizeStringDependencies(binary.Right, environment)),
                BoundInterpolatedStringExpression interpolated =>
                    SummarizeInterpolationDependencies(interpolated, environment),
                // Future String expression forms must opt into a precise
                // dependency rule. Treating an unknown form as independent
                // non-empty text keeps recurrence validation conservative.
                _ => StringDependencySummary.IndependentValue()
            };
        }

        private static StringDependencySummary SummarizeInterpolationDependencies(
            BoundInterpolatedStringExpression interpolated,
            IReadOnlyDictionary<VariableSymbol, StringDependencySummary> environment)
        {
            StringDependencySummary result = StringDependencySummary.Literal(mayBeNonEmpty: false);
            foreach (BoundInterpolatedPart part in interpolated.Parts)
            {
                StringDependencySummary next = part switch
                {
                    BoundInterpolatedTextPart text =>
                        StringDependencySummary.Literal(text.Text.Length > 0),
                    BoundInterpolationExpressionPart expressionPart
                        when expressionPart.Expression.Type is SmileType.String =>
                        SummarizeStringDependencies(expressionPart.Expression, environment),
                    // Integer and Boolean display text always contains at least
                    // one byte and is itself finitely bounded.
                    _ => StringDependencySummary.IndependentValue()
                };
                result = StringDependencySummary.Concatenate(result, next);
            }

            return result;
        }

        private static StringDependencySummary ResolveStringDependency(
            VariableSymbol variable,
            IReadOnlyDictionary<VariableSymbol, StringDependencySummary> environment) =>
            environment.TryGetValue(variable, out StringDependencySummary? summary)
                ? summary
                : StringDependencySummary.Identity(variable);

        private static Dictionary<VariableSymbol, StringDependencySummary>
            CloneStringDependencyEnvironment(
                IReadOnlyDictionary<VariableSymbol, StringDependencySummary> environment) =>
            environment.ToDictionary(pair => pair.Key, pair => pair.Value.Clone());

        private static void MergeStringDependencyEnvironments(
            Dictionary<VariableSymbol, StringDependencySummary> destination,
            IReadOnlyList<Dictionary<VariableSymbol, StringDependencySummary>> outgoing)
        {
            VariableSymbol[] variables = outgoing
                .SelectMany(environment => environment.Keys)
                .Distinct()
                .ToArray();
            destination.Clear();
            foreach (VariableSymbol variable in variables)
            {
                StringDependencySummary merged = StringDependencySummary.Literal(
                    mayBeNonEmpty: false);
                foreach (Dictionary<VariableSymbol, StringDependencySummary> environment in outgoing)
                {
                    merged.MergeAlternative(ResolveStringDependency(variable, environment));
                }

                destination.Add(variable, merged);
            }
        }

        private static bool StringDependencyEnvironmentsEqual(
            IReadOnlyDictionary<VariableSymbol, StringDependencySummary> left,
            IReadOnlyDictionary<VariableSymbol, StringDependencySummary> right) =>
            left.Count == right.Count &&
            left.All(pair =>
                right.TryGetValue(pair.Key, out StringDependencySummary? summary) &&
                summary is not null &&
                pair.Value.EqualsSummary(summary));

        private static void ReplaceStringDependencyEnvironment(
            Dictionary<VariableSymbol, StringDependencySummary> destination,
            IReadOnlyDictionary<VariableSymbol, StringDependencySummary> source)
        {
            destination.Clear();
            foreach ((VariableSymbol variable, StringDependencySummary summary) in source)
            {
                destination.Add(variable, summary.Clone());
            }
        }

        private static HashSet<VariableSymbol> ReachableFrom(
            VariableSymbol start,
            IReadOnlyDictionary<VariableSymbol, VariableSymbol[]> adjacency)
        {
            var reachable = new HashSet<VariableSymbol>();
            var pending = new Stack<VariableSymbol>();
            pending.Push(start);
            while (pending.Count > 0)
            {
                VariableSymbol current = pending.Pop();
                if (!adjacency.TryGetValue(current, out VariableSymbol[]? neighbors))
                {
                    continue;
                }

                foreach (VariableSymbol neighbor in neighbors)
                {
                    if (reachable.Add(neighbor))
                    {
                        pending.Push(neighbor);
                    }
                }
            }

            return reachable;
        }

        private readonly record struct StringDependencyEdge(
            VariableSymbol Target,
            VariableSymbol Source,
            bool MayAddBytes);

        private sealed class StringDependencySummary
        {
            public Dictionary<VariableSymbol, bool> Dependencies { get; } = new();

            public bool MayBeNonEmpty { get; private set; }

            public static StringDependencySummary Identity(VariableSymbol variable)
            {
                var summary = new StringDependencySummary { MayBeNonEmpty = true };
                summary.Dependencies.Add(variable, false);
                return summary;
            }

            public static StringDependencySummary Literal(bool mayBeNonEmpty) =>
                new() { MayBeNonEmpty = mayBeNonEmpty };

            public static StringDependencySummary IndependentValue() =>
                Literal(mayBeNonEmpty: true);

            public static StringDependencySummary Concatenate(
                StringDependencySummary left,
                StringDependencySummary right)
            {
                var result = new StringDependencySummary
                {
                    MayBeNonEmpty = left.MayBeNonEmpty || right.MayBeNonEmpty
                };
                foreach ((VariableSymbol variable, bool mayAddBytes) in left.Dependencies)
                {
                    result.AddDependency(variable, mayAddBytes || right.MayBeNonEmpty);
                }

                foreach ((VariableSymbol variable, bool mayAddBytes) in right.Dependencies)
                {
                    result.AddDependency(variable, mayAddBytes || left.MayBeNonEmpty);
                }

                return result;
            }

            public StringDependencySummary Clone()
            {
                var clone = new StringDependencySummary { MayBeNonEmpty = MayBeNonEmpty };
                foreach ((VariableSymbol variable, bool mayAddBytes) in Dependencies)
                {
                    clone.Dependencies.Add(variable, mayAddBytes);
                }

                return clone;
            }

            public void MergeAlternative(StringDependencySummary alternative)
            {
                MayBeNonEmpty |= alternative.MayBeNonEmpty;
                foreach ((VariableSymbol variable, bool mayAddBytes) in alternative.Dependencies)
                {
                    AddDependency(variable, mayAddBytes);
                }
            }

            public bool EqualsSummary(StringDependencySummary other) =>
                MayBeNonEmpty == other.MayBeNonEmpty &&
                Dependencies.Count == other.Dependencies.Count &&
                Dependencies.All(pair =>
                    other.Dependencies.TryGetValue(pair.Key, out bool mayAddBytes) &&
                    mayAddBytes == pair.Value);

            private void AddDependency(VariableSymbol variable, bool mayAddBytes)
            {
                Dependencies[variable] =
                    Dependencies.TryGetValue(variable, out bool existing)
                        ? existing || mayAddBytes
                        : mayAddBytes;
            }
        }

        private static bool AbstractEnvironmentsEqual(
            IReadOnlyDictionary<VariableSymbol, AnalyzedValue> left,
            IReadOnlyDictionary<VariableSymbol, AnalyzedValue> right) =>
            left.Count == right.Count &&
            left.All(pair =>
                right.TryGetValue(pair.Key, out AnalyzedValue value) &&
                value.Equals(pair.Value));

        private static bool ConcreteEnvironmentsEqual(
            IReadOnlyDictionary<VariableSymbol, SmileValue> left,
            IReadOnlyDictionary<VariableSymbol, SmileValue> right) =>
            left.Count == right.Count &&
            left.All(pair =>
                right.TryGetValue(pair.Key, out SmileValue value) &&
                value.Equals(pair.Value));

        private static bool PossibleEnvironmentsEqual(
            IReadOnlyDictionary<VariableSymbol, PossibleValueState> left,
            IReadOnlyDictionary<VariableSymbol, PossibleValueState> right) =>
            left.Count == right.Count &&
            left.All(pair =>
                right.TryGetValue(pair.Key, out PossibleValueState? value) &&
                value is not null &&
                PossibleValuesEqual(pair.Value, value));

        private static bool PossibleValuesEqual(
            PossibleValueState left,
            PossibleValueState right) =>
            left.Type == right.Type &&
            left.IsExact == right.IsExact &&
            left.MaximumDisplayUtf8ByteLength == right.MaximumDisplayUtf8ByteLength &&
            left.MayContainNul == right.MayContainNul &&
            left.HasFiniteMaximumDisplayUtf8ByteLength ==
                right.HasFiniteMaximumDisplayUtf8ByteLength &&
            left.MinimumIntegerValue == right.MinimumIntegerValue &&
            left.MaximumIntegerValue == right.MaximumIntegerValue &&
            left.ExactValues.SequenceEqual(right.ExactValues);

        private static void ReplaceAbstractValues(
            Dictionary<VariableSymbol, AnalyzedValue> destination,
            IReadOnlyDictionary<VariableSymbol, AnalyzedValue> source)
        {
            destination.Clear();
            foreach ((VariableSymbol variable, AnalyzedValue value) in source)
            {
                destination.Add(variable, value);
            }
        }

        private static void ReplacePossibleValues(
            Dictionary<VariableSymbol, PossibleValueState> destination,
            IReadOnlyDictionary<VariableSymbol, PossibleValueState> source)
        {
            destination.Clear();
            foreach ((VariableSymbol variable, PossibleValueState value) in source)
            {
                destination.Add(variable, value);
            }
        }

        private sealed record LoopSolution(
            Dictionary<VariableSymbol, AnalyzedValue> AbstractValuesAtHead,
            Dictionary<VariableSymbol, SmileValue> ConcreteValuesAtHead,
            Dictionary<VariableSymbol, PossibleValueState> PossibleValuesAtHead,
            bool ProducesUnboundedString);

        private static void MergeConcreteValues(
            Dictionary<VariableSymbol, SmileValue> destination,
            IReadOnlyList<Dictionary<VariableSymbol, SmileValue>> outgoing)
        {
            if (outgoing.Count == 0)
            {
                destination.Clear();
                return;
            }

            var merged = new Dictionary<VariableSymbol, SmileValue>();
            foreach ((VariableSymbol variable, SmileValue value) in outgoing[0])
            {
                if (outgoing.Skip(1).All(environment =>
                        environment.TryGetValue(variable, out SmileValue candidate) &&
                        candidate == value))
                {
                    merged.Add(variable, value);
                }
            }

            ReplaceConcreteValues(destination, merged);
        }

        private static void ReplaceConcreteValues(
            Dictionary<VariableSymbol, SmileValue> destination,
            IReadOnlyDictionary<VariableSymbol, SmileValue> source)
        {
            destination.Clear();
            foreach ((VariableSymbol variable, SmileValue value) in source)
            {
                destination.Add(variable, value);
            }
        }

        private void RecordAssignedValues(
            VariableSymbol variable,
            PossibleValueState assigned)
        {
            if (!AssignedValues.TryGetValue(variable, out List<SmileValue>? values))
            {
                values = new List<SmileValue>();
                AssignedValues.Add(variable, values);
            }

            foreach (SmileValue value in assigned.ExactValues)
            {
                AddDistinct(values, value);
            }

            if (!assigned.IsExact)
            {
                VariablesWithInexactAssignedValues.Add(variable);
            }

            MaximumAssignedUtf8ByteLengths[variable] =
                MaximumAssignedUtf8ByteLengths.TryGetValue(variable, out int previous)
                    ? Math.Max(previous, assigned.MaximumDisplayUtf8ByteLength)
                    : assigned.MaximumDisplayUtf8ByteLength;

            if (assigned.Type is SmileType.String)
            {
                if (assigned.MayContainNul)
                {
                    AssignedValuesThatMayContainNul.Add(variable);
                }
            }
        }

        private PossibleValueState EvaluatePossible(
            BoundExpression expression,
            IReadOnlyDictionary<VariableSymbol, PossibleValueState> possibleValues,
            bool recordFacts = true)
        {
            PossibleValueState result;
            switch (expression)
            {
                case BoundStringLiteralExpression literal:
                    result = PossibleValueState.Exact(SmileValue.FromString(literal.Value));
                    break;

                case BoundIntegerLiteralExpression literal:
                    result = PossibleValueState.Exact(SmileValue.FromInteger(literal.Value));
                    break;

                case BoundBooleanLiteralExpression literal:
                    result = PossibleValueState.Exact(SmileValue.FromBoolean(literal.Value));
                    break;

                case BoundVariableExpression variable
                    when possibleValues.TryGetValue(variable.Variable, out PossibleValueState? state) &&
                        state is not null:
                    result = state;
                    break;

                case BoundUnaryExpression unary:
                    result = EvaluatePossibleUnary(unary, possibleValues, recordFacts);
                    break;

                case BoundBinaryExpression binary:
                    result = EvaluatePossibleBinary(binary, possibleValues, recordFacts);
                    break;

                case BoundInterpolatedStringExpression interpolated:
                    result = EvaluatePossibleInterpolation(interpolated, possibleValues, recordFacts);
                    break;

                default:
                    result = PossibleValueState.Inexact(expression.Type);
                    break;
            }

            if (recordFacts && expression.Type is SmileType.Integer)
            {
                IntegerRanges[expression] = new AnalyzedIntegerRange(
                    result.MinimumIntegerValue,
                    result.MaximumIntegerValue);
            }

            if (recordFacts)
            {
                ExpressionDisplayFacts[expression] = new AnalyzedExpressionDisplayFacts(
                    result.MaximumDisplayUtf8ByteLength,
                    result.MayContainNul,
                    result.HasFiniteMaximumDisplayUtf8ByteLength);
            }

            return result;
        }

        private PossibleValueState EvaluatePossibleUnary(
            BoundUnaryExpression unary,
            IReadOnlyDictionary<VariableSymbol, PossibleValueState> possibleValues,
            bool recordFacts)
        {
            PossibleValueState operand = EvaluatePossible(
                unary.Operand,
                possibleValues,
                recordFacts);
            if (!operand.IsExact)
            {
                if (unary.Type is not SmileType.Integer)
                {
                    return PossibleValueState.Inexact(unary.Type);
                }

                return unary.Operator.Kind is BoundUnaryOperatorKind.Negation
                    ? PossibleValueState.InexactInteger(
                        ClampInteger(-(System.Numerics.BigInteger)operand.MaximumIntegerValue),
                        ClampInteger(-(System.Numerics.BigInteger)operand.MinimumIntegerValue))
                    : PossibleValueState.InexactInteger(
                        operand.MinimumIntegerValue,
                        operand.MaximumIntegerValue);
            }

            var results = new List<SmileValue>();
            foreach (SmileValue value in operand.ExactValues)
            {
                try
                {
                    SmileValue result = unary.Operator.Kind switch
                    {
                        BoundUnaryOperatorKind.Identity => value,
                        BoundUnaryOperatorKind.Negation =>
                            SmileValue.FromInteger(checked(-value.IntegerValue)),
                        BoundUnaryOperatorKind.LogicalNegation =>
                            SmileValue.FromBoolean(!value.BooleanValue),
                        _ => default
                    };
                    AddDistinct(results, result);
                }
                catch (OverflowException)
                {
                    // This candidate cannot complete the assignment at runtime.
                }
            }

            return PossibleValueState.Exact(unary.Type, results);
        }

        private PossibleValueState EvaluatePossibleBinary(
            BoundBinaryExpression binary,
            IReadOnlyDictionary<VariableSymbol, PossibleValueState> possibleValues,
            bool recordFacts)
        {
            PossibleValueState left = EvaluatePossible(binary.Left, possibleValues, recordFacts);
            PossibleValueState right = EvaluatePossible(binary.Right, possibleValues, recordFacts);

            if (binary.Operator.Kind is
                BoundBinaryOperatorKind.LogicalAnd or
                BoundBinaryOperatorKind.LogicalOr)
            {
                return EvaluatePossibleLogical(binary.Operator.Kind, left, right);
            }

            if (binary.Operator.Kind is BoundBinaryOperatorKind.StringConcatenation)
            {
                bool hasFiniteMaximum =
                    left.HasFiniteMaximumDisplayUtf8ByteLength &&
                    right.HasFiniteMaximumDisplayUtf8ByteLength;
                int maximumLength = SaturatingAdd(
                    left.MaximumDisplayUtf8ByteLength,
                    right.MaximumDisplayUtf8ByteLength);
                bool mayContainNul = left.MayContainNul || right.MayContainNul;
                if (CanCombineExactlyInLinearTime(left, right))
                {
                    IReadOnlyList<SmileValue> combined = CombineExactValues(
                        left,
                        right,
                        (leftValue, rightValue) =>
                            SmileValue.FromString(leftValue.StringValue + rightValue.StringValue));
                    return combined.Count == 0
                        ? PossibleValueState.ExactEmpty(
                            SmileType.String,
                            maximumLength,
                            mayContainNul)
                        : PossibleValueState.Exact(SmileType.String, combined);
                }

                PossibleValueState concatenated = PossibleValueState.Inexact(
                    SmileType.String,
                    maximumLength,
                    mayContainNul);
                return hasFiniteMaximum
                    ? concatenated
                    : concatenated.WithoutFiniteStringBound();
            }

            if (CanCombineExactlyInLinearTime(left, right))
            {
                var results = new List<SmileValue>();
                foreach (SmileValue leftValue in left.ExactValues)
                {
                    foreach (SmileValue rightValue in right.ExactValues)
                    {
                        if (TryApplyBinary(binary.Operator.Kind, leftValue, rightValue, out SmileValue result))
                        {
                            AddDistinct(results, result);
                        }
                    }
                }

                return PossibleValueState.Exact(binary.Type, results);
            }

            return binary.Type is SmileType.Boolean
                ? PossibleValueState.Exact(
                    SmileType.Boolean,
                    new[] { SmileValue.FromBoolean(false), SmileValue.FromBoolean(true) })
                : binary.Type is SmileType.Integer
                    ? PossibleIntegerBinaryResult(binary.Operator.Kind, left, right)
                    : PossibleValueState.Inexact(binary.Type);
        }

        private static PossibleValueState EvaluatePossibleLogical(
            BoundBinaryOperatorKind kind,
            PossibleValueState left,
            PossibleValueState right)
        {
            if (!left.IsExact)
            {
                return PossibleValueState.Exact(
                    SmileType.Boolean,
                    new[] { SmileValue.FromBoolean(false), SmileValue.FromBoolean(true) });
            }

            var results = new List<SmileValue>();
            foreach (SmileValue leftValue in left.ExactValues)
            {
                bool shortCircuits = kind is BoundBinaryOperatorKind.LogicalAnd
                    ? !leftValue.BooleanValue
                    : leftValue.BooleanValue;
                if (shortCircuits)
                {
                    AddDistinct(results, SmileValue.FromBoolean(leftValue.BooleanValue));
                    continue;
                }

                if (!right.IsExact)
                {
                    AddDistinct(results, SmileValue.FromBoolean(false));
                    AddDistinct(results, SmileValue.FromBoolean(true));
                    continue;
                }

                foreach (SmileValue rightValue in right.ExactValues)
                {
                    AddDistinct(results, SmileValue.FromBoolean(rightValue.BooleanValue));
                }
            }

            return PossibleValueState.Exact(SmileType.Boolean, results);
        }

        private static PossibleValueState PossibleIntegerBinaryResult(
            BoundBinaryOperatorKind kind,
            PossibleValueState left,
            PossibleValueState right)
        {
            System.Numerics.BigInteger minimum;
            System.Numerics.BigInteger maximum;
            switch (kind)
            {
                case BoundBinaryOperatorKind.Addition:
                    minimum =
                        (System.Numerics.BigInteger)left.MinimumIntegerValue +
                        right.MinimumIntegerValue;
                    maximum =
                        (System.Numerics.BigInteger)left.MaximumIntegerValue +
                        right.MaximumIntegerValue;
                    break;

                case BoundBinaryOperatorKind.Subtraction:
                    minimum =
                        (System.Numerics.BigInteger)left.MinimumIntegerValue -
                        right.MaximumIntegerValue;
                    maximum =
                        (System.Numerics.BigInteger)left.MaximumIntegerValue -
                        right.MinimumIntegerValue;
                    break;

                case BoundBinaryOperatorKind.Multiplication:
                    System.Numerics.BigInteger[] products =
                    {
                        (System.Numerics.BigInteger)left.MinimumIntegerValue * right.MinimumIntegerValue,
                        (System.Numerics.BigInteger)left.MinimumIntegerValue * right.MaximumIntegerValue,
                        (System.Numerics.BigInteger)left.MaximumIntegerValue * right.MinimumIntegerValue,
                        (System.Numerics.BigInteger)left.MaximumIntegerValue * right.MaximumIntegerValue
                    };
                    minimum = products.Min();
                    maximum = products.Max();
                    break;

                case BoundBinaryOperatorKind.Division
                    when right.MinimumIntegerValue > 0 || right.MaximumIntegerValue < 0:
                    System.Numerics.BigInteger[] quotients =
                    {
                        (System.Numerics.BigInteger)left.MinimumIntegerValue / right.MinimumIntegerValue,
                        (System.Numerics.BigInteger)left.MinimumIntegerValue / right.MaximumIntegerValue,
                        (System.Numerics.BigInteger)left.MaximumIntegerValue / right.MinimumIntegerValue,
                        (System.Numerics.BigInteger)left.MaximumIntegerValue / right.MaximumIntegerValue
                    };
                    minimum = quotients.Min();
                    maximum = quotients.Max();
                    break;

                default:
                    return PossibleValueState.InexactInteger(long.MinValue, long.MaxValue);
            }

            return PossibleValueState.InexactInteger(
                ClampInteger(minimum),
                ClampInteger(maximum));
        }

        private static long ClampInteger(System.Numerics.BigInteger value)
        {
            if (value < long.MinValue)
            {
                return long.MinValue;
            }

            return value > long.MaxValue
                ? long.MaxValue
                : (long)value;
        }

        private PossibleValueState EvaluatePossibleInterpolation(
            BoundInterpolatedStringExpression interpolated,
            IReadOnlyDictionary<VariableSymbol, PossibleValueState> possibleValues,
            bool recordFacts)
        {
            int maximumLength = 0;
            bool mayContainNul = false;
            bool hasFiniteMaximum = true;
            bool isExact = true;
            bool alreadyHasMultipleCandidates = false;
            var exactStrings = new List<string> { string.Empty };

            foreach (BoundInterpolatedPart part in interpolated.Parts)
            {
                if (part is BoundInterpolatedTextPart text)
                {
                    maximumLength = SaturatingAdd(
                        maximumLength,
                        System.Text.Encoding.UTF8.GetByteCount(text.Text));
                    mayContainNul |= text.Text.Contains('\0', StringComparison.Ordinal);
                    if (isExact)
                    {
                        for (int index = 0; index < exactStrings.Count; index++)
                        {
                            exactStrings[index] += text.Text;
                        }
                    }

                    continue;
                }

                var expressionPart = (BoundInterpolationExpressionPart)part;
                PossibleValueState value = EvaluatePossible(
                    expressionPart.Expression,
                    possibleValues,
                    recordFacts);
                maximumLength = SaturatingAdd(
                    maximumLength,
                    value.MaximumDisplayUtf8ByteLength);
                mayContainNul |= value.MayContainNul;
                hasFiniteMaximum &= value.HasFiniteMaximumDisplayUtf8ByteLength;

                if (!isExact || !value.IsExact)
                {
                    isExact = false;
                    continue;
                }

                if (exactStrings.Count == 0)
                {
                    // An earlier expression has no successful runtime value,
                    // so the complete interpolation has no value either. We
                    // still visit later parts above to retain their range and
                    // storage facts, but there is no prefix to combine.
                    continue;
                }

                bool hasMultipleCandidates = value.ExactValues.Count > 1;
                if (alreadyHasMultipleCandidates && hasMultipleCandidates)
                {
                    isExact = false;
                    continue;
                }

                if (hasMultipleCandidates)
                {
                    alreadyHasMultipleCandidates = true;
                }

                if (value.ExactValues.Count == 0)
                {
                    exactStrings.Clear();
                    continue;
                }

                if (value.ExactValues.Count == 1)
                {
                    string suffix = value.ExactValues[0].ToDisplayText();
                    for (int index = 0; index < exactStrings.Count; index++)
                    {
                        exactStrings[index] += suffix;
                    }

                    continue;
                }

                string prefix = exactStrings.Single();
                exactStrings = value.ExactValues
                    .Select(candidate => prefix + candidate.ToDisplayText())
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
            }

            PossibleValueState result = isExact
                ? exactStrings.Count == 0
                    ? PossibleValueState.ExactEmpty(
                        SmileType.String,
                        maximumLength,
                        mayContainNul)
                    : PossibleValueState.Exact(
                        SmileType.String,
                        exactStrings.Select(SmileValue.FromString))
                : PossibleValueState.Inexact(
                    SmileType.String,
                    maximumLength,
                    mayContainNul);
            return hasFiniteMaximum
                ? result
                : result.WithoutFiniteStringBound();
        }

        private static bool CanCombineExactlyInLinearTime(
            PossibleValueState left,
            PossibleValueState right) =>
            left.IsExact &&
            right.IsExact &&
            (left.ExactValues.Count <= 1 || right.ExactValues.Count <= 1);

        private static IReadOnlyList<SmileValue> CombineExactValues(
            PossibleValueState left,
            PossibleValueState right,
            Func<SmileValue, SmileValue, SmileValue> combine)
        {
            var results = new List<SmileValue>();
            foreach (SmileValue leftValue in left.ExactValues)
            {
                foreach (SmileValue rightValue in right.ExactValues)
                {
                    AddDistinct(results, combine(leftValue, rightValue));
                }
            }

            return results;
        }

        private static bool TryApplyBinary(
            BoundBinaryOperatorKind kind,
            SmileValue left,
            SmileValue right,
            out SmileValue value)
        {
            try
            {
                if (kind is BoundBinaryOperatorKind.Division && right.IntegerValue == 0)
                {
                    value = default;
                    return false;
                }

                value = kind switch
                {
                    BoundBinaryOperatorKind.Addition =>
                        SmileValue.FromInteger(checked(left.IntegerValue + right.IntegerValue)),
                    BoundBinaryOperatorKind.Subtraction =>
                        SmileValue.FromInteger(checked(left.IntegerValue - right.IntegerValue)),
                    BoundBinaryOperatorKind.Multiplication =>
                        SmileValue.FromInteger(checked(left.IntegerValue * right.IntegerValue)),
                    BoundBinaryOperatorKind.Division =>
                        SmileValue.FromInteger(checked(left.IntegerValue / right.IntegerValue)),
                    BoundBinaryOperatorKind.Equality =>
                        SmileValue.FromBoolean(ValuesEqual(left, right)),
                    BoundBinaryOperatorKind.Inequality =>
                        SmileValue.FromBoolean(!ValuesEqual(left, right)),
                    BoundBinaryOperatorKind.Less =>
                        SmileValue.FromBoolean(left.IntegerValue < right.IntegerValue),
                    BoundBinaryOperatorKind.LessOrEquals =>
                        SmileValue.FromBoolean(left.IntegerValue <= right.IntegerValue),
                    BoundBinaryOperatorKind.Greater =>
                        SmileValue.FromBoolean(left.IntegerValue > right.IntegerValue),
                    BoundBinaryOperatorKind.GreaterOrEquals =>
                        SmileValue.FromBoolean(left.IntegerValue >= right.IntegerValue),
                    BoundBinaryOperatorKind.LogicalAnd =>
                        SmileValue.FromBoolean(left.BooleanValue && right.BooleanValue),
                    BoundBinaryOperatorKind.LogicalOr =>
                        SmileValue.FromBoolean(left.BooleanValue || right.BooleanValue),
                    _ => default
                };
                return value.Type is not SmileType.Error;
            }
            catch (OverflowException)
            {
                value = default;
                return false;
            }
        }

        private static bool ValuesEqual(SmileValue left, SmileValue right) =>
            left.Type switch
            {
                SmileType.String => string.Equals(
                    left.StringValue,
                    right.StringValue,
                    StringComparison.Ordinal),
                SmileType.Integer => left.IntegerValue == right.IntegerValue,
                SmileType.Boolean => left.BooleanValue == right.BooleanValue,
                _ => false
            };

        private static int SaturatingAdd(int left, int right) =>
            left > int.MaxValue - right
                ? int.MaxValue
                : left + right;

        private static Dictionary<VariableSymbol, PossibleValueState> ClonePossibleValues(
            IReadOnlyDictionary<VariableSymbol, PossibleValueState> values) =>
            new(values);

        private void MergePossibleValues(
            Dictionary<VariableSymbol, PossibleValueState> destination,
            IReadOnlyList<Dictionary<VariableSymbol, PossibleValueState>> outgoing,
            bool recordInexactFacts = true)
        {
            VariableSymbol[] variables = outgoing
                .SelectMany(environment => environment.Keys)
                .Distinct()
                .ToArray();
            destination.Clear();

            foreach (VariableSymbol variable in variables)
            {
                var states = new List<PossibleValueState>();
                foreach (Dictionary<VariableSymbol, PossibleValueState> environment in outgoing)
                {
                    if (!environment.TryGetValue(variable, out PossibleValueState? state) ||
                        state is null)
                    {
                        continue;
                    }

                    states.Add(state);
                }

                bool isExact = states.All(state => state.IsExact);
                var exactValues = new List<SmileValue>();
                if (isExact)
                {
                    foreach (PossibleValueState state in states)
                    {
                        foreach (SmileValue candidate in state.ExactValues)
                        {
                            AddDistinct(exactValues, candidate);
                        }
                    }
                }

                SmileType type = variable.Type;
                int maximumLength = states.Count == 0
                    ? PossibleValueState.MaximumDisplayLength(type)
                    : states.Max(state => state.MaximumDisplayUtf8ByteLength);
                bool mayContainNul = states.Any(state => state.MayContainNul);
                bool hasFiniteMaximum = states.All(
                    state => state.HasFiniteMaximumDisplayUtf8ByteLength);
                PossibleValueState merged = isExact
                    ? PossibleValueState.Exact(type, exactValues)
                    : type is SmileType.Integer
                        ? PossibleValueState.InexactInteger(
                            states.Min(state => state.MinimumIntegerValue),
                            states.Max(state => state.MaximumIntegerValue))
                        : PossibleValueState.Inexact(type, maximumLength, mayContainNul);
                if (type is SmileType.String && !hasFiniteMaximum)
                {
                    merged = merged.WithoutFiniteStringBound();
                }

                destination.Add(variable, merged);
                if (recordInexactFacts && !merged.IsExact)
                {
                    VariablesWithInexactAssignedValues.Add(variable);
                }
            }
        }

        private static void AddDistinct(List<SmileValue> values, SmileValue candidate)
        {
            if (!values.Contains(candidate))
            {
                values.Add(candidate);
            }
        }

        private sealed record PossibleValueState(
            SmileType Type,
            IReadOnlyList<SmileValue> ExactValues,
            bool IsExact,
            int MaximumDisplayUtf8ByteLength,
            bool MayContainNul,
            bool HasFiniteMaximumDisplayUtf8ByteLength,
            long MinimumIntegerValue,
            long MaximumIntegerValue)
        {
            // Exact candidates are an educational convenience for direct
            // copies and simple lowering, not the storage-planning authority.
            // Bounding them keeps repeated branch merges polynomial while the
            // independent String and Integer summaries remain fully sound.
            private const int MaximumExactCandidateCount = 64;

            public static PossibleValueState Exact(SmileValue value) =>
                Exact(value.Type, new[] { value });

            public static PossibleValueState Exact(
                SmileType type,
                IEnumerable<SmileValue> values)
            {
                var bounded = new List<SmileValue>();
                bool isExact = true;
                int maximumDisplayLength = 0;
                bool mayContainNul = false;
                bool hasInteger = false;
                long minimumInteger = 0;
                long maximumInteger = 0;

                foreach (SmileValue value in values)
                {
                    maximumDisplayLength = Math.Max(
                        maximumDisplayLength,
                        System.Text.Encoding.UTF8.GetByteCount(value.ToDisplayText()));
                    mayContainNul |=
                        type is SmileType.String &&
                        value.StringValue.Contains('\0', StringComparison.Ordinal);
                    if (type is SmileType.Integer)
                    {
                        if (!hasInteger)
                        {
                            minimumInteger = value.IntegerValue;
                            maximumInteger = value.IntegerValue;
                            hasInteger = true;
                        }
                        else
                        {
                            minimumInteger = Math.Min(minimumInteger, value.IntegerValue);
                            maximumInteger = Math.Max(maximumInteger, value.IntegerValue);
                        }
                    }

                    if (bounded.Contains(value))
                    {
                        continue;
                    }

                    if (bounded.Count < MaximumExactCandidateCount)
                    {
                        bounded.Add(value);
                    }
                    else
                    {
                        isExact = false;
                    }
                }

                return new PossibleValueState(
                    type,
                    Array.AsReadOnly(bounded.ToArray()),
                    isExact,
                    bounded.Count == 0
                        ? MaximumDisplayLength(type)
                        : maximumDisplayLength,
                    mayContainNul,
                    HasFiniteMaximumDisplayUtf8ByteLength: true,
                    hasInteger ? minimumInteger : 0,
                    hasInteger ? maximumInteger : 0);
            }

            public static PossibleValueState ExactEmpty(
                SmileType type,
                int maximumDisplayUtf8ByteLength,
                bool mayContainNul) =>
                new(
                    type,
                    Array.Empty<SmileValue>(),
                    IsExact: true,
                    Math.Max(MaximumDisplayLength(type), maximumDisplayUtf8ByteLength),
                    type is SmileType.String && mayContainNul,
                    HasFiniteMaximumDisplayUtf8ByteLength: true,
                    MinimumIntegerValue: 0,
                    MaximumIntegerValue: 0);

            public static PossibleValueState Inexact(
                SmileType type,
                int? maximumDisplayUtf8ByteLength = null,
                bool mayContainNul = false) =>
                new(
                    type,
                    Array.Empty<SmileValue>(),
                    IsExact: false,
                    maximumDisplayUtf8ByteLength ?? MaximumDisplayLength(type),
                    type is SmileType.String && mayContainNul,
                    HasFiniteMaximumDisplayUtf8ByteLength: true,
                    type is SmileType.Integer ? long.MinValue : 0,
                    type is SmileType.Integer ? long.MaxValue : 0);

            public static PossibleValueState InexactInteger(long minimum, long maximum) =>
                new(
                    SmileType.Integer,
                    Array.Empty<SmileValue>(),
                    IsExact: false,
                    MaximumDisplayLength(SmileType.Integer),
                    MayContainNul: false,
                    HasFiniteMaximumDisplayUtf8ByteLength: true,
                    minimum,
                    maximum);

            public PossibleValueState WithoutFiniteStringBound() =>
                this with
                {
                    IsExact = false,
                    ExactValues = Array.Empty<SmileValue>(),
                    MaximumDisplayUtf8ByteLength = int.MaxValue,
                    HasFiniteMaximumDisplayUtf8ByteLength = false
                };

            public static int MaximumDisplayLength(SmileType type) =>
                type switch
                {
                    SmileType.Integer => 20,
                    SmileType.Boolean => 5,
                    _ => 0
                };
        }

        private static AnalyzedValue Evaluate(
            BoundExpression expression,
            IReadOnlyDictionary<VariableSymbol, AnalyzedValue> abstractValues)
        {
            var knownValues = new Dictionary<VariableSymbol, SmileValue>();
            foreach ((VariableSymbol variable, AnalyzedValue value) in abstractValues)
            {
                if (value.IsKnown)
                {
                    knownValues.Add(variable, value.Value);
                }
            }

            return BoundExpressionEvaluator.TryEvaluate(expression, knownValues, out SmileValue result)
                ? AnalyzedValue.Known(result)
                : AnalyzedValue.Unknown;
        }

        private static void Merge(
            Dictionary<VariableSymbol, AnalyzedValue> destination,
            IReadOnlyList<Dictionary<VariableSymbol, AnalyzedValue>> outgoing)
        {
            VariableSymbol[] variables = outgoing
                .SelectMany(environment => environment.Keys)
                .Distinct()
                .ToArray();
            destination.Clear();

            foreach (VariableSymbol variable in variables)
            {
                bool allKnown = true;
                bool hasFirst = false;
                SmileValue first = default;
                foreach (Dictionary<VariableSymbol, AnalyzedValue> environment in outgoing)
                {
                    if (!environment.TryGetValue(variable, out AnalyzedValue candidate) ||
                        !candidate.IsKnown)
                    {
                        allKnown = false;
                        break;
                    }

                    if (!hasFirst)
                    {
                        first = candidate.Value;
                        hasFirst = true;
                    }
                    else if (!candidate.Value.Equals(first))
                    {
                        allKnown = false;
                        break;
                    }
                }

                destination.Add(
                    variable,
                    allKnown && hasFirst
                        ? AnalyzedValue.Known(first)
                        : AnalyzedValue.Unknown);
            }
        }
    }
}
