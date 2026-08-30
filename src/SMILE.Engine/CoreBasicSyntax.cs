namespace SMILE.Engine;

public sealed record CoreAssignmentStatementSyntax(
    string Name,
    TextSpan NameSpan,
    ExpressionSyntax Value,
    TextSpan Span)
    : StatementSyntax(Span);

public sealed record DimStatementSyntax(
    string Name,
    TextSpan NameSpan,
    SmileType DeclaredType,
    TextSpan Span)
    : StatementSyntax(Span);

public sealed record ConstStatementSyntax(
    string Name,
    TextSpan NameSpan,
    ExpressionSyntax Initializer,
    TextSpan Span)
    : StatementSyntax(Span);

public sealed record CorePrintStatementSyntax(
    IReadOnlyList<ExpressionSyntax> Values,
    bool SuppressNewLine,
    TextSpan Span)
    : StatementSyntax(Span);

public sealed record ForStatementSyntax(
    string CounterName,
    TextSpan CounterSpan,
    ExpressionSyntax LowerBound,
    ExpressionSyntax UpperBound,
    bool IsDescending,
    IReadOnlyList<SourceItemSyntax> SourceItems,
    TextSpan Span)
    : StatementSyntax(Span)
{
    public IReadOnlyList<StatementSyntax> Statements => SourceItems.OfType<StatementSyntax>().ToArray();
}

public sealed record DoStatementSyntax(
    IReadOnlyList<SourceItemSyntax> SourceItems,
    ExpressionSyntax? UntilCondition,
    TextSpan Span)
    : StatementSyntax(Span)
{
    public IReadOnlyList<StatementSyntax> Statements => SourceItems.OfType<StatementSyntax>().ToArray();
}

public enum ExitStatementKind
{
    For,
    Do
}

public sealed record ExitStatementSyntax(
    ExitStatementKind Kind,
    TextSpan Span)
    : StatementSyntax(Span);

public sealed record EndProgramStatementSyntax(TextSpan Span)
    : StatementSyntax(Span);
