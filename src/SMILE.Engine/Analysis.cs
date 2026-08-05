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
    bool MayContainNul);

public sealed record BoundStatementAnalysis(
    int Ordinal,
    IReadOnlyDictionary<VariableSymbol, AnalyzedValue> ValuesBefore,
    AnalyzedValue Value,
    IReadOnlyDictionary<VariableSymbol, AnalyzedValue> ValuesAfter,
    IReadOnlyDictionary<VariableSymbol, SmileValue> ConcreteValuesBefore,
    SmileValue ConcreteValue,
    IReadOnlyDictionary<VariableSymbol, SmileValue> ConcreteValuesAfter);

public sealed record BoundConditionalClauseAnalysis(
    int Ordinal,
    IReadOnlyDictionary<VariableSymbol, AnalyzedValue> ValuesBefore,
    IReadOnlyDictionary<VariableSymbol, SmileValue> ConcreteValuesBefore);

public sealed class BoundProgramAnalysis
{
    private readonly IReadOnlyDictionary<BoundStatement, BoundStatementAnalysis> _statementFacts;
    private readonly IReadOnlyDictionary<BoundConditionalClause, BoundConditionalClauseAnalysis> _clauseFacts;
    private readonly IReadOnlyDictionary<BoundIfStatement, int> _ifOrdinals;
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
        private int _nextStatementOrdinal;

        public Dictionary<BoundStatement, BoundStatementAnalysis> StatementFacts { get; } =
            new(ReferenceEqualityComparer.Instance);

        public Dictionary<BoundConditionalClause, BoundConditionalClauseAnalysis> ClauseFacts { get; } =
            new(ReferenceEqualityComparer.Instance);

        public Dictionary<BoundIfStatement, int> IfOrdinals { get; } =
            new(ReferenceEqualityComparer.Instance);

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
                    }

                    break;

                case BoundPrintStatement print:
                    analyzedValue = Evaluate(print.Value, abstractValues);
                    EvaluatePossible(print.Value, possibleValues);
                    BoundExpressionEvaluator.TryEvaluate(
                        print.Value,
                        concreteValues,
                        out concreteValue);
                    break;

                case BoundIfStatement conditional:
                    IfOrdinals.Add(conditional, _nextIfOrdinal++);
                    AnalyzeIf(conditional, abstractValues, concreteValues, possibleValues);
                    concreteValue = SmileValue.FromBoolean(true);
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
                    BoundProgramExecutionTrace.Snapshot(concreteValues)));
        }

        private void AnalyzeIf(
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
                possibleOutgoing.Add(elsePossible);
            }
            else
            {
                abstractOutgoing.Add(new Dictionary<VariableSymbol, AnalyzedValue>(abstractValues));
                possibleOutgoing.Add(ClonePossibleValues(possibleValues));
            }

            Merge(abstractValues, abstractOutgoing);
            MergePossibleValues(possibleValues, possibleOutgoing);

            int selectedClause = -1;
            for (int index = 0; index < conditional.Clauses.Count; index++)
            {
                if (BoundExpressionEvaluator.TryEvaluate(
                        conditional.Clauses[index].Condition,
                        concreteValues,
                        out SmileValue condition) &&
                    condition.BooleanValue)
                {
                    selectedClause = index;
                    break;
                }
            }

            Dictionary<VariableSymbol, SmileValue> selectedConcrete =
                selectedClause >= 0
                    ? concreteOutgoing[selectedClause]
                    : elseConcrete ?? new Dictionary<VariableSymbol, SmileValue>(concreteValues);
            concreteValues.Clear();
            foreach ((VariableSymbol variable, SmileValue value) in selectedConcrete)
            {
                concreteValues.Add(variable, value);
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
            IReadOnlyDictionary<VariableSymbol, PossibleValueState> possibleValues)
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
                    result = EvaluatePossibleUnary(unary, possibleValues);
                    break;

                case BoundBinaryExpression binary:
                    result = EvaluatePossibleBinary(binary, possibleValues);
                    break;

                case BoundInterpolatedStringExpression interpolated:
                    result = EvaluatePossibleInterpolation(interpolated, possibleValues);
                    break;

                default:
                    result = PossibleValueState.Inexact(expression.Type);
                    break;
            }

            if (expression.Type is SmileType.Integer)
            {
                IntegerRanges[expression] = new AnalyzedIntegerRange(
                    result.MinimumIntegerValue,
                    result.MaximumIntegerValue);
            }

            ExpressionDisplayFacts[expression] = new AnalyzedExpressionDisplayFacts(
                result.MaximumDisplayUtf8ByteLength,
                result.MayContainNul);

            return result;
        }

        private PossibleValueState EvaluatePossibleUnary(
            BoundUnaryExpression unary,
            IReadOnlyDictionary<VariableSymbol, PossibleValueState> possibleValues)
        {
            PossibleValueState operand = EvaluatePossible(unary.Operand, possibleValues);
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
            IReadOnlyDictionary<VariableSymbol, PossibleValueState> possibleValues)
        {
            PossibleValueState left = EvaluatePossible(binary.Left, possibleValues);
            PossibleValueState right = EvaluatePossible(binary.Right, possibleValues);

            if (binary.Operator.Kind is
                BoundBinaryOperatorKind.LogicalAnd or
                BoundBinaryOperatorKind.LogicalOr)
            {
                return EvaluatePossibleLogical(binary.Operator.Kind, left, right);
            }

            if (binary.Operator.Kind is BoundBinaryOperatorKind.StringConcatenation)
            {
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

                return PossibleValueState.Inexact(
                    SmileType.String,
                    maximumLength,
                    mayContainNul);
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
            IReadOnlyDictionary<VariableSymbol, PossibleValueState> possibleValues)
        {
            int maximumLength = 0;
            bool mayContainNul = false;
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
                    possibleValues);
                maximumLength = SaturatingAdd(
                    maximumLength,
                    value.MaximumDisplayUtf8ByteLength);
                mayContainNul |= value.MayContainNul;

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

            return isExact
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
            IReadOnlyList<Dictionary<VariableSymbol, PossibleValueState>> outgoing)
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
                PossibleValueState merged = isExact
                    ? PossibleValueState.Exact(type, exactValues)
                    : type is SmileType.Integer
                        ? PossibleValueState.InexactInteger(
                            states.Min(state => state.MinimumIntegerValue),
                            states.Max(state => state.MaximumIntegerValue))
                        : PossibleValueState.Inexact(type, maximumLength, mayContainNul);
                destination.Add(variable, merged);
                if (!merged.IsExact)
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
                    type is SmileType.Integer ? long.MinValue : 0,
                    type is SmileType.Integer ? long.MaxValue : 0);

            public static PossibleValueState InexactInteger(long minimum, long maximum) =>
                new(
                    SmileType.Integer,
                    Array.Empty<SmileValue>(),
                    IsExact: false,
                    MaximumDisplayLength(SmileType.Integer),
                    MayContainNul: false,
                    minimum,
                    maximum);

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
