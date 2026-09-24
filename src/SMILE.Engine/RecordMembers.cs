namespace SMILE.Engine;

public sealed record InstanceMethodDeclarationSyntax(RoutineDeclarationSyntax Routine, bool IsPrivate,
    TextSpan Span) : StatementSyntax(Span);

public sealed record InstancePropertyDeclarationSyntax(string Name, TextSpan NameSpan, TypeNameSyntax DeclaredType,
    RoutineDeclarationSyntax? Getter, RoutineDeclarationSyntax? Setter, bool IsPrivate,
    IReadOnlyList<SourceItemSyntax> SourceItems, TextSpan Span) : StatementSyntax(Span);

public sealed record MemberInvocationExpressionSyntax(ExpressionSyntax Receiver, string Name, TextSpan NameSpan,
    IReadOnlyList<ExpressionSyntax> Arguments, TextSpan Span) : ExpressionSyntax(Span);

public sealed record MemberCallStatementSyntax(MemberInvocationExpressionSyntax Invocation, TextSpan Span) : StatementSyntax(Span);

public sealed record MeExpressionSyntax(TextSpan Span) : ExpressionSyntax(Span);

public enum InstanceMemberRoutineKind { Method, PropertyGet, PropertySet }

public sealed record InstancePropertySymbol(InstanceTypeSymbol Owner, string Name, TextSpan Span,
    SmileType Type, bool IsPrivate, RoutineSymbol? Getter, RoutineSymbol? Setter);
