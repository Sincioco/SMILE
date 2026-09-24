namespace SMILE.Engine;

public sealed record ClassDeclarationSyntax(string Name, TextSpan NameSpan,
    IReadOnlyList<SourceItemSyntax> SourceItems, TextSpan Span) : InstanceDeclarationSyntax(Name, NameSpan, SourceItems, Span);

public sealed class ClassTypeSymbol : InstanceTypeSymbol
{
    internal ClassTypeSymbol(ClassDeclarationSyntax declaration) : base(SmileTypeKind.Class, declaration) { }
    public RoutineSymbol Constructor { get; internal set; } = null!;
    public int InstanceSize { get; internal set; } = 8;
    public bool ContainsText { get; internal set; }
}

public sealed record NewExpressionSyntax(TypeNameSyntax DeclaredType, IReadOnlyList<ExpressionSyntax> Arguments,
    TextSpan Span) : ExpressionSyntax(Span);

public sealed record NothingExpressionSyntax(TextSpan Span) : ExpressionSyntax(Span);
public sealed record IdentityExpressionSyntax(ExpressionSyntax Left, ExpressionSyntax Right, bool Negated,
    TextSpan Span) : ExpressionSyntax(Span);

public sealed record BoundNewExpression(ClassTypeSymbol Class, IReadOnlyList<BoundExpression> Arguments,
    IReadOnlyList<int>? ParameterOrder) : BoundExpression(Class);
public sealed record BoundNothingExpression(SmileType ReferenceType) : BoundExpression(ReferenceType);
public sealed record BoundIdentityExpression(BoundExpression Left, BoundExpression Right, bool Negated)
    : BoundExpression(SmileType.Boolean);
