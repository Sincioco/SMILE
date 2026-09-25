namespace SMILE.Engine;

internal sealed partial class Parser
{
    private StatementSyntax ParseModule()
    {
        Token start = Next();
        Token name = ParseQualifiedName();
        ConsumeStatementEnd();
        IReadOnlyList<SourceItemSyntax> body = ParseItems(() => Current.Kind is TokenKind.End && Peek(1).Kind is TokenKind.Module);
        Match(TokenKind.End, "Expected End Module.");
        Token end = Match(TokenKind.Module, "Expected Module after End.");
        return new ModuleDeclarationSyntax(name.Text, name.Span, body, Combine(start.Span, end.Span));
    }

    private StatementSyntax ParseImport()
    {
        Token start = Next();
        Token name = ParseQualifiedName();
        Match(TokenKind.As, "Import requires As and a source-local alias.");
        Token alias = Match(TokenKind.Identifier, "Expected an import alias.");
        return new ImportStatementSyntax(name.Text, alias.Text, alias.Span, Combine(start.Span, alias.Span));
    }

    private StatementSyntax? ParseVisibility()
    {
        Token visibility = Next();
        StatementSyntax? declaration = ParseStatement();
        return declaration is null ? null : new VisibilityDeclarationSyntax(visibility.Kind is TokenKind.Public, declaration, Combine(visibility.Span, declaration.Span));
    }

    private Token ParseQualifiedName()
    {
        Token name = Match(TokenKind.Identifier, "Expected a name.");
        while (Current.Kind is TokenKind.Dot)
        {
            Next();
            Token part = Match(TokenKind.Identifier, "Expected a name after '.'.");
            name = name with { Text = name.Text + "." + part.Text, Span = Combine(name.Span, part.Span) };
        }
        return name;
    }
}
