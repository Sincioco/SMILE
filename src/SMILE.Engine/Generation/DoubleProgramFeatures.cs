namespace SMILE.Engine;

internal sealed class DoubleProgramFeatures
{
    public HashSet<BoundIntrinsicKind> Intrinsics { get; }
    public bool HasArithmetic { get; }
    public bool HasDivision { get; }
    public bool IsRequired { get; }
    public bool HasFormatting { get; }
    public bool HasLiterals { get; }
    public bool HasNegation { get; }
    public bool HasComparisons { get; }
    public bool HasSelection { get; }
    public bool NeedsCobolRuntime => HasLiterals || HasNegation || HasComparisons || HasSelection || HasArithmetic || HasFormatting || Intrinsics.Count > 0;
    public bool NeedsCheck => HasArithmetic || Intrinsics.Any(kind => kind is BoundIntrinsicKind.Sqrt or BoundIntrinsicKind.Sin or BoundIntrinsicKind.Cos or BoundIntrinsicKind.Atan2);
    public bool NeedsFailure => NeedsCheck || Has(BoundIntrinsicKind.ToNumber) || Has(BoundIntrinsicKind.TextToDouble) || Has(BoundIntrinsicKind.Clamp);
    public bool Has(BoundIntrinsicKind kind) => Intrinsics.Contains(kind);

    public DoubleProgramFeatures(BoundProgram program)
    {
        BoundExpression[] expressions = CoreBasicCodeGenerator.EnumerateExpressionsForSupport(program, includeConstants: false).ToArray();
        HasSelection = ContainsStatement(program.SourceItems, statement => statement is BoundSelectStatement { Selector.Type: { Kind: SmileTypeKind.Double } }) ||
            program.Routines.Any(routine => ContainsStatement(routine.SourceItems, statement => statement is BoundSelectStatement { Selector.Type: { Kind: SmileTypeKind.Double } }));
        HasLiterals = HasSelection || expressions.Any(expression => expression is BoundDoubleLiteralExpression or BoundVariableExpression { Variable.IsConstant: true, Type: { Kind: SmileTypeKind.Double } });
        HasNegation = expressions.OfType<BoundUnaryExpression>().Any(expression => expression.Type is { Kind: SmileTypeKind.Double } && expression.Operator.Kind is BoundUnaryOperatorKind.Negation);
        HasComparisons = expressions.OfType<BoundBinaryExpression>().Any(expression => expression.Left.Type is { Kind: SmileTypeKind.Double } && expression.Type is { Kind: SmileTypeKind.Boolean });
        Intrinsics = expressions.OfType<BoundIntrinsicExpression>().Where(DoubleSemantics.UsesDouble).Select(expression => expression.Kind).ToHashSet();
        HasArithmetic = expressions.OfType<BoundBinaryExpression>().Any(expression => expression.Type is { Kind: SmileTypeKind.Double });
        HasDivision = expressions.OfType<BoundBinaryExpression>().Any(expression => expression.Type is { Kind: SmileTypeKind.Double } && expression.Operator.Kind is BoundBinaryOperatorKind.Division);
        IsRequired = expressions.Any(expression => expression.Type is { Kind: SmileTypeKind.Double }) || program.AllVariables.Any(variable => variable.Type is { Kind: SmileTypeKind.Double });
        HasFormatting = Has(BoundIntrinsicKind.TextFromDouble) || PrintUsesDouble(program.SourceItems) || program.Routines.Any(routine => PrintUsesDouble(routine.SourceItems));
    }

    private static bool PrintUsesDouble(IReadOnlyList<BoundSourceItem> items) => ContainsStatement(items,
        statement => statement is BoundCorePrintStatement print && print.Values.Any(value => value.Type is { Kind: SmileTypeKind.Double }));

    private static bool ContainsStatement(IReadOnlyList<BoundSourceItem> items, Func<BoundStatement, bool> predicate) => items.OfType<BoundStatement>().Any(statement => predicate(statement) || (statement switch
    {
        BoundIfStatement conditional => conditional.Clauses.Any(clause => ContainsStatement(clause.SourceItems, predicate)) || ContainsStatement(conditional.ElseSourceItems, predicate),
        BoundSelectStatement select => select.Cases.Any(clause => ContainsStatement(clause.SourceItems, predicate)),
        BoundForStatement loop => ContainsStatement(loop.SourceItems, predicate),
        BoundDoStatement loop => ContainsStatement(loop.SourceItems, predicate),
        BoundWithStatement block => ContainsStatement(block.SourceItems, predicate),
        _ => false
    }));
}
