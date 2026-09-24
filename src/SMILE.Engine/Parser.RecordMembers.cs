namespace SMILE.Engine;

internal sealed partial class Parser
{
    private bool IsRecordMemberBoundary() => Current.Kind is TokenKind.Public or TokenKind.Private or TokenKind.Property ||
        Current.Kind is TokenKind.End && Peek(1).Kind is TokenKind.Type or TokenKind.Class ||
        Current.Kind is TokenKind.Identifier && Peek(1).Kind is TokenKind.As;

    private InstancePropertyDeclarationSyntax ParseRecordProperty(bool isPrivate)
    {
        Token start = Next();
        Token name = MatchMemberName();
        Match(TokenKind.As, "A Property requires As and a type.");
        Token type = ParseType("Expected a property type.");
        var declaredType = new TypeNameSyntax(type.Text, type.Span);
        ConsumeStatementEnd();
        RoutineDeclarationSyntax? getter = null, setter = null;
        var items = new List<SourceItemSyntax>();
        while (Current.Kind is not TokenKind.EndOfFile && !IsRecordMemberBoundary() && !(Current.Kind is TokenKind.End && Peek(1).Kind is TokenKind.Property))
        {
            if (Current.Kind is TokenKind.EndOfLine) { items.Add(new BlankLineSyntax(Next().Span)); continue; }
            if (Current.Kind is TokenKind.Comment)
            {
                Token comment = Next();
                items.Add(new FullLineCommentSyntax(FullLineCommentMarker.Apostrophe, (string?)comment.Value ?? "", comment.Span));
                ConsumeLineEnd();
                continue;
            }
            if (Current.Kind is not (TokenKind.Get or TokenKind.Set))
            {
                Report("SMILE3441", "A Property contains only Get and Set accessor blocks.", Current.Span);
                ConsumeStatementEnd();
                continue;
            }
            Token accessor = Next();
            bool isGetter = accessor.Kind is TokenKind.Get;
            ConsumeStatementEnd();
            _routineDepth++;
            IReadOnlyList<SourceItemSyntax> body = ParseItems(() => Current.Kind is TokenKind.End && Peek(1).Kind == accessor.Kind ||
                IsRecordMemberBoundary() || Current.Kind is TokenKind.End && Peek(1).Kind is TokenKind.Property ||
                Current.Kind is TokenKind.Set || Current.Kind is TokenKind.Get && Peek(1).Kind is not TokenKind.Key);
            _routineDepth--;
            Token end = Current;
            bool terminated = Current.Kind is TokenKind.End && Peek(1).Kind == accessor.Kind;
            if (terminated) { Next(); end = Next(); }
            else Report("SMILE2005", $"Expected End {accessor.Text} before the next member or accessor.", Current.Span);
            var routine = new RoutineDeclarationSyntax(isGetter ? RoutineKind.Function : RoutineKind.Sub,
                name.Text, name.Span, [], isGetter ? declaredType : null, body, Combine(accessor.Span, end.Span));
            if (isGetter ? getter is not null : setter is not null)
                Report("SMILE3441", $"Property '{name.Text}' repeats its {accessor.Text} accessor.", accessor.Span);
            else if (isGetter) getter = routine;
            else setter = routine;
            items.Add(routine);
            if (terminated) ConsumeStatementEnd();
        }
        Match(TokenKind.End, "Expected End Property.");
        Token close = Match(TokenKind.Property, "Expected Property after End.");
        if (getter is null && setter is null) Report("SMILE3441", "A Property requires a Get or Set accessor.", name.Span);
        return new InstancePropertyDeclarationSyntax(name.Text, name.Span, declaredType, getter, setter, isPrivate, items, Combine(start.Span, close.Span));
    }
}
