namespace SMILE.Engine;

internal sealed partial class Parser
{
    private StatementSyntax ParseWith()
    {
        Token start = Next();
        ExpressionSyntax target = ParseExpression();
        ConsumeStatementEnd();
        IReadOnlyList<SourceItemSyntax> body = ParseItems(() => Current.Kind is TokenKind.End && Peek(1).Kind is TokenKind.With);
        Match(TokenKind.End, "Expected End With.");
        Token end = Match(TokenKind.With, "Expected With after End.");
        return new WithStatementSyntax(target, body, Combine(start.Span, end.Span));
    }

    private StatementSyntax ParseRecord()
    {
        Token start = Next();
        Token name = Match(TokenKind.Identifier, "Expected a name after Type.");
        ConsumeStatementEnd();
        var items = new List<SourceItemSyntax>();
        _recordDepth++;
        while (Current.Kind is not TokenKind.EndOfFile && !(Current.Kind is TokenKind.End && Peek(1).Kind is TokenKind.Type))
        {
            if (Current.Kind is TokenKind.EndOfLine) { items.Add(new BlankLineSyntax(Next().Span)); continue; }
            if (Current.Kind is TokenKind.Comment)
            {
                Token comment = Next();
                items.Add(new FullLineCommentSyntax(FullLineCommentMarker.Apostrophe, (string?)comment.Value ?? "", comment.Span));
                ConsumeLineEnd();
                continue;
            }
            bool isPrivate = Current.Kind is TokenKind.Private;
            if (Current.Kind is TokenKind.Public or TokenKind.Private) Next();
            if (Current.Kind is TokenKind.Sub or TokenKind.Function)
            {
                var routine = (RoutineDeclarationSyntax)ParseRoutine(Current.Kind is TokenKind.Sub ? RoutineKind.Sub : RoutineKind.Function);
                items.Add(new RecordMethodDeclarationSyntax(routine, isPrivate, routine.Span));
                if (AtLineEnd()) ConsumeStatementEnd();
                continue;
            }
            if (Current.Kind is TokenKind.Property)
            {
                items.Add(ParseRecordProperty(isPrivate));
                if (AtLineEnd()) ConsumeStatementEnd();
                continue;
            }
            if (isPrivate) Report("SMILE3440", "Type fields are always Public.", Current.Span);
            Token field = Current.Text.Equals("None", StringComparison.OrdinalIgnoreCase) ||
                Current.Text.Equals("Up", StringComparison.OrdinalIgnoreCase) || Current.Text.Equals("Down", StringComparison.OrdinalIgnoreCase)
                ? Match(TokenKind.Identifier, "Expected a field name.") : MatchMemberName();
            IReadOnlyList<ExpressionSyntax> dimensions = [];
            if (Current.Kind is TokenKind.OpenBracket)
            {
                dimensions = ParseBracketExpressionList("field array dimension");
                Match(TokenKind.CloseBracket, "Expected ']' after the field dimensions.");
            }
            Match(TokenKind.As, "Record fields require As and a type.");
            Token type = ParseType("Expected a field type.");
            items.Add(new RecordFieldDeclarationSyntax(field.Text, new TypeNameSyntax(type.Text, type.Span), dimensions, Combine(field.Span, type.Span)));
            ConsumeStatementEnd();
        }
        _recordDepth--;
        Match(TokenKind.End, "Expected End Type.");
        Token end = Match(TokenKind.Type, "Expected Type after End.");
        return new RecordDeclarationSyntax(name.Text, name.Span, items, Combine(start.Span, end.Span));
    }

    private StatementSyntax ParseLocationAssignment()
    {
        Token name = Current;
        ExpressionSyntax target = ParsePrimaryExpression();
        Match(TokenKind.Equals, "Expected '=' after the assignment target.");
        ExpressionSyntax value = ParseExpression();
        TextSpan span = Combine(name.Span, value.Span);
        return target switch
        {
            NameExpressionSyntax => new CoreAssignmentStatementSyntax(name.Text, name.Span, value, span),
            ArrayAccessExpressionSyntax array => new CoreArrayAssignmentStatementSyntax(name.Text, name.Span, array.Indices, value, span),
            _ => new MemberAssignmentStatementSyntax(target, value, span)
        };
    }

    private ExpressionSyntax ParsePostfix(ExpressionSyntax receiver)
    {
        while (Current.Kind is TokenKind.Dot or TokenKind.OpenBracket || Current.Kind is TokenKind.OpenParenthesis && receiver is MemberAccessExpressionSyntax)
        {
            if (Current.Kind is TokenKind.OpenParenthesis && receiver is MemberAccessExpressionSyntax invocation)
            {
                IReadOnlyList<ExpressionSyntax> arguments = ParseArgumentList("Member calls require parentheses.");
                receiver = new MemberInvocationExpressionSyntax(invocation.Receiver, invocation.Name, invocation.NameSpan, arguments, Combine(receiver.Span, Previous.Span));
                continue;
            }
            if (Current.Kind is TokenKind.Dot)
            {
                Next();
                Token member = MatchMemberName();
                receiver = new MemberAccessExpressionSyntax(receiver, member.Text, member.Span, Combine(receiver.Span, member.Span));
                continue;
            }
            IReadOnlyList<ExpressionSyntax> indices = ParseBracketExpressionList("array index");
            Token close = Match(TokenKind.CloseBracket, "Expected ']' after array indexes.");
            if (receiver is NameExpressionSyntax name)
                receiver = new ArrayAccessExpressionSyntax(name.Name, name.Span, indices, Combine(name.Span, close.Span));
            else if (receiver is MemberAccessExpressionSyntax member)
                receiver = new IndexedMemberExpressionSyntax(member, indices, Combine(member.Span, close.Span));
            else
            {
                Report("SMILE3406", "Only declared arrays or fixed-array fields can be indexed.", receiver.Span);
                receiver = new ErrorExpressionSyntax(receiver.Span);
            }
        }
        return receiver;
    }
}
