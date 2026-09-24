namespace SMILE.Engine;

public sealed record WithStatementSyntax(ExpressionSyntax Target,
    IReadOnlyList<SourceItemSyntax> SourceItems, TextSpan Span) : StatementSyntax(Span);

public sealed record WithReceiverExpressionSyntax(TextSpan Span) : ExpressionSyntax(Span);

// A With block captures one storage location, not a snapshot of its value.
public sealed class WithLocationSymbol(BoundExpression target)
{
    public BoundExpression Target { get; } = target;
}

public sealed record BoundWithStatement(WithLocationSymbol Location,
    IReadOnlyList<BoundSourceItem> SourceItems) : BoundStatement;

public sealed record BoundWithReceiverExpression(WithLocationSymbol Location)
    : BoundExpression(Location.Target.Type);
