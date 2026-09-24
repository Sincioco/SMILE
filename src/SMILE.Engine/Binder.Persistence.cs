namespace SMILE.Engine;

internal sealed partial class Binder
{
    private BoundStatement BindNumberLoad(NumberLoadStatementSyntax syntax)
    {
        BoundExpression fallback = BindExpression(syntax.DefaultValue);
        if (fallback.Type is not ({ Kind: SmileTypeKind.Integer } or { Kind: SmileTypeKind.Error }))
            Report("SMILE3025", "Load Default must be Number.", syntax.DefaultValue.Span);
        VariableSymbol target = ResolveAssignmentTarget(syntax.Name, syntax.NameSpan, SmileType.Integer);
        ValidateWritableNumberTarget(target, syntax.NameSpan, "Load");
        ValidateNumberStorageKey(syntax.Key, syntax.Span);
        return new BoundNumberLoadStatement(target, syntax.Key, fallback);
    }

    private BoundStatement BindNumberSave(NumberSaveStatementSyntax syntax)
    {
        VariableSymbol? variable = LookupVariable(syntax.Name, syntax.NameSpan, reportUnknown: true);
        if (variable is null || variable.IsArray || variable.Type is not { Kind: SmileTypeKind.Integer })
            Report("SMILE3025", "Save requires a Number variable or constant.", syntax.NameSpan);
        ValidateNumberStorageKey(syntax.Key, syntax.Span);
        return new BoundNumberSaveStatement(variable ?? ErrorVariable(syntax.Name, syntax.NameSpan, SmileType.Integer), syntax.Key);
    }

    private void ValidateNumberStorageKey(string key, TextSpan span)
    {
        if (string.IsNullOrWhiteSpace(key)) Report("SMILE3025", "Storage key must be a non-empty Text literal.", span);
    }
}
