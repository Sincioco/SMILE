namespace SMILE.Engine;

// Syntax keeps a spelling and location; only binding resolves it to a type symbol.
public sealed record TypeNameSyntax(string Name, TextSpan Span) : SyntaxNode(Span);

public sealed record EnumDeclarationSyntax(string Name, TextSpan NameSpan,
    IReadOnlyList<SourceItemSyntax> SourceItems, TextSpan Span) : StatementSyntax(Span)
{
    public IEnumerable<EnumMemberDeclarationSyntax> Members => SourceItems.OfType<EnumMemberDeclarationSyntax>();
}

public sealed record EnumMemberDeclarationSyntax(string Name, ExpressionSyntax? Value,
    TextSpan Span) : SourceItemSyntax(Span);

public sealed record MemberAccessExpressionSyntax(ExpressionSyntax Receiver, string Name,
    TextSpan NameSpan, TextSpan Span) : ExpressionSyntax(Span);

public sealed class EnumTypeSymbol : SmileType
{
    internal EnumTypeSymbol(EnumDeclarationSyntax declaration) : base(SmileTypeKind.Enum, declaration.Name)
        => Declaration = declaration;

    public EnumDeclarationSyntax Declaration { get; }
    public IReadOnlyList<EnumMemberSymbol> Members { get; internal set; } = [];
}

public sealed record EnumMemberSymbol(EnumTypeSymbol Type, string Name, long Value, TextSpan Span);

public sealed record BoundEnumExpression(EnumTypeSymbol EnumType, long Value, EnumMemberSymbol? Member = null)
    : BoundExpression(EnumType);
