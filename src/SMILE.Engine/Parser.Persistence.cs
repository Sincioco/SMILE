namespace SMILE.Engine;

internal sealed partial class Parser
{
    private StatementSyntax ParseNumberLoad()
    {
        Token start = Next();
        Token name = Match(TokenKind.Identifier, "Expected a Number variable after Load.");
        Match(TokenKind.From, "Expected From after the Load variable.");
        Token key = Match(TokenKind.String, "Load requires a Text literal storage key.");
        Match(TokenKind.Default, "Expected Default after the storage key.");
        ExpressionSyntax fallback = ParseExpression();
        return new NumberLoadStatementSyntax(name.Text, name.Span, (string?)key.Value ?? "", fallback, Combine(start.Span, fallback.Span));
    }

    private StatementSyntax ParseNumberSave()
    {
        Token start = Next();
        Token name = Match(TokenKind.Identifier, "Expected a Number variable or constant after Save.");
        Match(TokenKind.To, "Expected To after the saved value.");
        Token key = Match(TokenKind.String, "Save requires a Text literal storage key.");
        return new NumberSaveStatementSyntax(name.Text, name.Span, (string?)key.Value ?? "", Combine(start.Span, key.Span));
    }
}
