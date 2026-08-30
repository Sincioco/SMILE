namespace SMILE.Engine;

public sealed record OptionExplicitStatementSyntax(TextSpan Span)
    : StatementSyntax(Span);

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
    ExpressionSyntax? ArraySize,
    TextSpan Span)
    : StatementSyntax(Span)
{
    public bool IsArray => ArraySize is not null;
}

public sealed record ConstStatementSyntax(
    string Name,
    TextSpan NameSpan,
    ExpressionSyntax Initializer,
    TextSpan Span)
    : StatementSyntax(Span);

public enum RoutineKind
{
    Sub,
    Function
}

public sealed record ParameterSyntax(
    string Name,
    TextSpan NameSpan,
    SmileType DeclaredType,
    bool HasExplicitByVal,
    TextSpan Span)
    : SyntaxNode(Span);

public sealed record RoutineDeclarationSyntax(
    RoutineKind Kind,
    string Name,
    TextSpan NameSpan,
    IReadOnlyList<ParameterSyntax> Parameters,
    SmileType? ReturnType,
    IReadOnlyList<SourceItemSyntax> SourceItems,
    TextSpan Span)
    : StatementSyntax(Span)
{
    public IReadOnlyList<StatementSyntax> Statements => SourceItems.OfType<StatementSyntax>().ToArray();
}

public sealed record CallStatementSyntax(
    string Name,
    TextSpan NameSpan,
    IReadOnlyList<ExpressionSyntax> Arguments,
    TextSpan Span)
    : StatementSyntax(Span);

public sealed record ReturnStatementSyntax(
    ExpressionSyntax? Value,
    TextSpan Span)
    : StatementSyntax(Span);

public sealed record SelectCaseClauseSyntax(
    ExpressionSyntax? Value,
    bool IsElse,
    IReadOnlyList<SourceItemSyntax> SourceItems,
    TextSpan Span)
    : SyntaxNode(Span)
{
    public IReadOnlyList<StatementSyntax> Statements => SourceItems.OfType<StatementSyntax>().ToArray();
}

public sealed record SelectStatementSyntax(
    ExpressionSyntax Selector,
    IReadOnlyList<SelectCaseClauseSyntax> Cases,
    TextSpan Span)
    : StatementSyntax(Span);

public sealed record CoreArrayAssignmentStatementSyntax(
    string Name,
    TextSpan NameSpan,
    ExpressionSyntax Index,
    ExpressionSyntax Value,
    TextSpan Span)
    : StatementSyntax(Span);

public sealed record CallExpressionSyntax(
    string Name,
    TextSpan NameSpan,
    IReadOnlyList<ExpressionSyntax> Arguments,
    TextSpan Span)
    : ExpressionSyntax(Span);

public sealed record ArrayAccessExpressionSyntax(
    string Name,
    TextSpan NameSpan,
    ExpressionSyntax Index,
    TextSpan Span)
    : ExpressionSyntax(Span);

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
