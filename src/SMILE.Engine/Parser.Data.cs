namespace SMILE.Engine;

internal sealed partial class Parser
{
    private StatementSyntax ParseDataLoad()
    {
        Token start = Next();
        Next(); // Data was selected by the statement dispatch.
        ExpressionSyntax key = ParseExpression();
        Match(TokenKind.Into, "Expected Into after the Data key.");
        Token destination = ParseQualifiedName();
        Match(TokenKind.Count, "Expected Count after the Data destination.");
        ExpressionSyntax count = ParseDataTarget();
        ExpressionSyntax? status = ParseDataStatus();
        return new DataLoadStatementSyntax(key, destination.Text, destination.Span, count, status, Combine(start.Span, (status ?? count).Span));
    }

    private StatementSyntax ParseDataSave()
    {
        Token start = Next();
        Next();
        Token source = ParseQualifiedName();
        Match(TokenKind.Count, "Expected Count after the Data source.");
        ExpressionSyntax count = ParseExpression();
        Match(TokenKind.To, "Expected To after the Data count.");
        ExpressionSyntax key = ParseExpression();
        ExpressionSyntax? status = ParseDataStatus();
        return new DataSaveStatementSyntax(source.Text, source.Span, count, key, status, Combine(start.Span, (status ?? key).Span));
    }

    private ExpressionSyntax? ParseDataStatus()
    {
        if (Current.Kind != TokenKind.Identifier || !Current.Text.Equals("Status", StringComparison.OrdinalIgnoreCase)) return null;
        Next();
        return ParseDataTarget();
    }

    private ExpressionSyntax ParseDataTarget()
    {
        Token name = Match(TokenKind.Identifier, "Expected writable Number storage.");
        return ParsePostfix(new NameExpressionSyntax(name.Text, name.Span));
    }
}
