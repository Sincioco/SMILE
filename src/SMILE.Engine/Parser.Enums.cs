namespace SMILE.Engine;

internal sealed partial class Parser
{
    private StatementSyntax ParseEnum()
    {
        Token start = Next();
        Token name = Match(TokenKind.Identifier, "Expected a name after Enum.");
        ConsumeStatementEnd();
        var items = new List<SourceItemSyntax>();
        while (Current.Kind is not TokenKind.EndOfFile && !(Current.Kind is TokenKind.End && Peek(1).Kind is TokenKind.Enum))
        {
            if (Current.Kind is TokenKind.EndOfLine) { items.Add(new BlankLineSyntax(Next().Span)); continue; }
            if (Current.Kind is TokenKind.Comment)
            {
                Token comment = Next();
                items.Add(new FullLineCommentSyntax(FullLineCommentMarker.Apostrophe, (string?)comment.Value ?? "", comment.Span));
                ConsumeLineEnd();
                continue;
            }
            if (Current.Kind is TokenKind.Enum or TokenKind.Dim or TokenKind.Const or TokenKind.Sub or TokenKind.Function) break;
            Token member = MatchMemberName();
            ExpressionSyntax? value = null;
            if (Current.Kind is TokenKind.Equals) { Next(); value = ParseExpression(); }
            items.Add(new EnumMemberDeclarationSyntax(member.Text, value, value is null ? member.Span : Combine(member.Span, value.Span)));
            ConsumeStatementEnd();
        }
        Match(TokenKind.End, "Expected End Enum.");
        Token end = Match(TokenKind.Enum, "Expected Enum after End.");
        return new EnumDeclarationSyntax(name.Text, name.Span, items, Combine(start.Span, end.Span));
    }

    private Token MatchMemberName()
    {
        if (Current.Kind is TokenKind.Identifier || ContextualMemberNames.Contains(Current.Text)) return Next();
        return Match(TokenKind.Identifier, "Expected a member name.");
    }

    private static readonly HashSet<string> ContextualMemberNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "None", "Up", "Down", "Key", "Window", "Size", "Draw", "Line", "Text", "Left", "Right", "Set", "Property", "Double",
        "Unload", "Clip", "Data", "Status", "Opacity", "Anchor", "Flip", "Horizontal", "Vertical", "Both", "Filter", "Smooth", "Pixel", "On", "Channel"
    };

}
