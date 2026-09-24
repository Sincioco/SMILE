namespace SMILE.Engine;

public sealed record DataLoadStatementSyntax(ExpressionSyntax Key, string Destination, TextSpan DestinationSpan,
    ExpressionSyntax Count, ExpressionSyntax? Status, TextSpan Span) : StatementSyntax(Span);

public sealed record DataSaveStatementSyntax(string Source, TextSpan SourceSpan, ExpressionSyntax Count,
    ExpressionSyntax Key, ExpressionSyntax? Status, TextSpan Span) : StatementSyntax(Span);

public sealed record BoundDataLoadStatement(BoundExpression Key, VariableSymbol Destination,
    BoundExpression Count, BoundExpression? Status) : BoundStatement;

public sealed record BoundDataSaveStatement(VariableSymbol Source, BoundExpression Count,
    BoundExpression Key, BoundExpression? Status) : BoundStatement;

internal static class DataStatementFacts
{
    public static IEnumerable<BoundExpression> Expressions(BoundStatement statement) => statement switch
    {
        BoundDataLoadStatement load => new[] { load.Key, load.Count }.Concat(load.Status is null ? [] : new[] { load.Status }),
        BoundDataSaveStatement save => new[] { save.Count, save.Key }.Concat(save.Status is null ? [] : new[] { save.Status }),
        _ => []
    };

    public static bool Assigns(BoundStatement statement, VariableSymbol variable) => statement switch
    {
        BoundDataLoadStatement load => Targets(load.Count, variable) || Targets(load.Status, variable),
        BoundDataSaveStatement save => Targets(save.Status, variable),
        _ => false
    };

    private static bool Targets(BoundExpression? target, VariableSymbol variable) => target switch
    {
        BoundVariableExpression scalar => scalar.Variable == variable,
        BoundArrayExpression array => array.Array == variable,
        BoundFieldExpression field => Targets(field.Receiver, variable),
        BoundWithReceiverExpression receiver => Targets(receiver.Location.Target, variable),
        _ => false
    };
}
