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
    TypeNameSyntax DeclaredType,
    IReadOnlyList<ExpressionSyntax> ArraySizes,
    TextSpan Span)
    : StatementSyntax(Span)
{
    public bool IsArray => ArraySizes.Count > 0;
    public NewExpressionSyntax? Initializer { get; init; }
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
    TypeNameSyntax DeclaredType,
    bool HasExplicitByVal,
    TextSpan Span,
    bool IsOptional = false,
    ExpressionSyntax? DefaultValue = null,
    bool IsByRef = false)
    : SyntaxNode(Span);

public sealed record TextFileLoadStatementSyntax(
    ExpressionSyntax Path, string Destination, TextSpan DestinationSpan,
    string Count, TextSpan CountSpan, TextSpan Span) : StatementSyntax(Span);

public sealed record NumberLoadStatementSyntax(string Name, TextSpan NameSpan, string Key,
    ExpressionSyntax DefaultValue, TextSpan Span) : StatementSyntax(Span);

public sealed record NumberSaveStatementSyntax(string Name, TextSpan NameSpan, string Key, TextSpan Span) : StatementSyntax(Span);

public sealed record DoubleLiteralExpressionSyntax(string Text, TextSpan Span) : ExpressionSyntax(Span);

public sealed record NamedArgumentExpressionSyntax(string Name, ExpressionSyntax Value, TextSpan Span)
    : ExpressionSyntax(Span);

public sealed record RoutineDeclarationSyntax(
    RoutineKind Kind,
    string Name,
    TextSpan NameSpan,
    IReadOnlyList<ParameterSyntax> Parameters,
    TypeNameSyntax? ReturnType,
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
    IReadOnlyList<ExpressionSyntax> Indices,
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
    IReadOnlyList<ExpressionSyntax> Indices,
    TextSpan Span)
    : ExpressionSyntax(Span);

public sealed record GetKeyStatementSyntax(
    string Name,
    TextSpan NameSpan,
    TextSpan Span)
    : StatementSyntax(Span);

public sealed record ClearScreenStatementSyntax(TextSpan Span)
    : StatementSyntax(Span);

public sealed record MoveCursorStatementSyntax(
    ExpressionSyntax Column,
    ExpressionSyntax Row,
    TextSpan Span)
    : StatementSyntax(Span);

public sealed record TextColorStatementSyntax(
    SmileTextColor? Foreground,
    SmileTextColor? Background,
    bool IsDefault,
    TextSpan Span)
    : StatementSyntax(Span);

public sealed record WaitStatementSyntax(
    ExpressionSyntax Duration,
    TextSpan Span)
    : StatementSyntax(Span);

public sealed record RandomStatementSyntax(
    string Name,
    TextSpan NameSpan,
    ExpressionSyntax LowerBound,
    ExpressionSyntax UpperBound,
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
