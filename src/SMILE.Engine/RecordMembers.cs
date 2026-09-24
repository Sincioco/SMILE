namespace SMILE.Engine;

public sealed record RecordMethodDeclarationSyntax(RoutineDeclarationSyntax Routine, bool IsPrivate,
    TextSpan Span) : StatementSyntax(Span);

public sealed record RecordPropertyDeclarationSyntax(string Name, TextSpan NameSpan, TypeNameSyntax DeclaredType,
    RoutineDeclarationSyntax? Getter, RoutineDeclarationSyntax? Setter, bool IsPrivate,
    IReadOnlyList<SourceItemSyntax> SourceItems, TextSpan Span) : StatementSyntax(Span);

public sealed record MemberInvocationExpressionSyntax(ExpressionSyntax Receiver, string Name, TextSpan NameSpan,
    IReadOnlyList<ExpressionSyntax> Arguments, TextSpan Span) : ExpressionSyntax(Span);

public sealed record MemberCallStatementSyntax(MemberInvocationExpressionSyntax Invocation, TextSpan Span) : StatementSyntax(Span);

public sealed record MeExpressionSyntax(TextSpan Span) : ExpressionSyntax(Span);

public enum RecordMemberRoutineKind { Method, PropertyGet, PropertySet }

public sealed record RecordPropertySymbol(RecordTypeSymbol Owner, string Name, TextSpan Span,
    SmileType Type, bool IsPrivate, RoutineSymbol? Getter, RoutineSymbol? Setter);
