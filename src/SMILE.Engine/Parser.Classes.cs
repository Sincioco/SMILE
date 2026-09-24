namespace SMILE.Engine;

internal sealed partial class Parser
{
    private NewExpressionSyntax ParseNew()
    {
        Token start = Next();
        Token type = ParseType("New requires a Class name.");
        IReadOnlyList<ExpressionSyntax> arguments = ParseArgumentList("New requires constructor parentheses.");
        return new NewExpressionSyntax(new TypeNameSyntax(type.Text, type.Span), arguments, Combine(start.Span, Previous.Span));
    }
}
