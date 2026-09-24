namespace SMILE.Engine;

public sealed record RecordDeclarationSyntax(string Name, TextSpan NameSpan,
    IReadOnlyList<SourceItemSyntax> SourceItems, TextSpan Span) : InstanceDeclarationSyntax(Name, NameSpan, SourceItems, Span);

public sealed record IndexedMemberExpressionSyntax(MemberAccessExpressionSyntax Member,
    IReadOnlyList<ExpressionSyntax> Indices, TextSpan Span) : ExpressionSyntax(Span);

public sealed record MemberAssignmentStatementSyntax(ExpressionSyntax Target,
    ExpressionSyntax Value, TextSpan Span) : StatementSyntax(Span);

public sealed class RecordTypeSymbol : InstanceTypeSymbol
{
    internal RecordTypeSymbol(RecordDeclarationSyntax declaration) : base(SmileTypeKind.Record, declaration) { }
    // MASM uses eight-byte scalar slots. Other writers use native field layouts.
    public int NativeSize { get; internal set; } = 8;
    public bool ContainsText { get; internal set; }
}

public sealed record BoundFieldExpression(BoundExpression Receiver, InstanceFieldSymbol Field,
    IReadOnlyList<BoundExpression> Indices) : BoundExpression(Field.Type);

public sealed record BoundMemberSetStatement(BoundFieldExpression Target, BoundExpression Value) : BoundStatement;

internal static class BoundLocations
{
    public static bool IsWritable(BoundExpression expression) => expression switch
    {
        BoundVariableExpression variable => !variable.Variable.IsConstant && !variable.Variable.IsReceiver,
        BoundArrayExpression => true,
        BoundWithReceiverExpression => true,
        BoundFieldExpression field => field.Receiver.Type is ClassTypeSymbol || IsWritable(field.Receiver) || field.Receiver is BoundVariableExpression { Variable.IsReceiver: true },
        _ => false
    };
}
