namespace SMILE.Engine;

public sealed record RecordDeclarationSyntax(string Name, TextSpan NameSpan,
    IReadOnlyList<SourceItemSyntax> SourceItems, TextSpan Span) : StatementSyntax(Span);

public sealed record RecordFieldDeclarationSyntax(string Name, TypeNameSyntax DeclaredType,
    IReadOnlyList<ExpressionSyntax> Dimensions, TextSpan Span) : SourceItemSyntax(Span);

public sealed record IndexedMemberExpressionSyntax(MemberAccessExpressionSyntax Member,
    IReadOnlyList<ExpressionSyntax> Indices, TextSpan Span) : ExpressionSyntax(Span);

public sealed record MemberAssignmentStatementSyntax(ExpressionSyntax Target,
    ExpressionSyntax Value, TextSpan Span) : StatementSyntax(Span);

public sealed class RecordTypeSymbol : SmileType
{
    internal RecordTypeSymbol(RecordDeclarationSyntax declaration) : base(SmileTypeKind.Record, declaration.Name)
        => Declaration = declaration;

    public RecordDeclarationSyntax Declaration { get; }
    public IReadOnlyList<RecordFieldSymbol> Fields { get; internal set; } = [];
    public IReadOnlyList<RoutineSymbol> Methods { get; internal set; } = [];
    public IReadOnlyList<RecordPropertySymbol> Properties { get; internal set; } = [];
    // MASM uses eight-byte scalar slots. Other writers use native field layouts.
    public int NativeSize { get; internal set; } = 8;
    public bool ContainsText { get; internal set; }
}

public sealed record RecordFieldSymbol(RecordTypeSymbol Owner, string Name, SmileType Type,
    IReadOnlyList<int> Dimensions, int Ordinal, TextSpan Span)
{
    public bool IsArray => Dimensions.Count > 0;
    public int ElementCount => Dimensions.Count == 0 ? 1 : Dimensions.Aggregate(1, (count, size) => checked(count * size));
    public int NativeOffset { get; internal set; }
}

public sealed record BoundFieldExpression(BoundExpression Receiver, RecordFieldSymbol Field,
    IReadOnlyList<BoundExpression> Indices) : BoundExpression(Field.Type);

public sealed record BoundMemberSetStatement(BoundFieldExpression Target, BoundExpression Value) : BoundStatement;

internal static class BoundLocations
{
    public static bool IsWritable(BoundExpression expression) => expression switch
    {
        BoundVariableExpression variable => !variable.Variable.IsConstant && !variable.Variable.IsReceiver,
        BoundArrayExpression => true,
        BoundWithReceiverExpression => true,
        BoundFieldExpression field => IsWritable(field.Receiver) || field.Receiver is BoundVariableExpression { Variable.IsReceiver: true },
        _ => false
    };
}
