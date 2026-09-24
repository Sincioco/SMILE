namespace SMILE.Engine;

// Records and classes share member lookup, but keep distinct value/reference
// storage owners. Copying, allocation and lifetime never belong to this boundary.
public abstract record InstanceDeclarationSyntax(string Name, TextSpan NameSpan,
    IReadOnlyList<SourceItemSyntax> SourceItems, TextSpan Span) : StatementSyntax(Span);

public abstract class InstanceTypeSymbol : SmileType
{
    private protected InstanceTypeSymbol(SmileTypeKind kind, InstanceDeclarationSyntax declaration)
        : base(kind, declaration.Name) => Declaration = declaration;

    public InstanceDeclarationSyntax Declaration { get; }
    public IReadOnlyList<InstanceFieldSymbol> Fields { get; internal set; } = [];
    public IReadOnlyList<RoutineSymbol> Methods { get; internal set; } = [];
    public IReadOnlyList<InstancePropertySymbol> Properties { get; internal set; } = [];
}

public sealed record InstanceFieldDeclarationSyntax(string Name, TypeNameSyntax DeclaredType,
    IReadOnlyList<ExpressionSyntax> Dimensions, TextSpan Span) : SourceItemSyntax(Span)
{
    public bool IsPrivate { get; init; }
}

public sealed record InstanceFieldSymbol(InstanceTypeSymbol Owner, string Name, SmileType Type,
    IReadOnlyList<int> Dimensions, int Ordinal, TextSpan Span)
{
    public bool IsPrivate { get; init; }
    public bool IsArray => Dimensions.Count > 0;
    public int ElementCount => Dimensions.Count == 0 ? 1 : Dimensions.Aggregate(1, (count, size) => checked(count * size));
    public int NativeOffset { get; internal set; }
}
