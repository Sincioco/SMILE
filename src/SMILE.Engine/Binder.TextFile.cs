namespace SMILE.Engine;

internal sealed partial class Binder
{
    private BoundStatement BindTextFileLoad(TextFileLoadStatementSyntax syntax)
    {
        BoundExpression path = BindExpression(syntax.Path);
        StaticEvaluationResult constant = BoundExpressionEvaluator.Evaluate(path, _constantValues);
        if (path.Type is not ({ Kind: SmileTypeKind.String } or { Kind: SmileTypeKind.Error }) ||
            constant.IsKnown && constant.Value.Type is { Kind: SmileTypeKind.String } && string.IsNullOrWhiteSpace(constant.Value.StringValue))
            Report("SMILE3027", "Load Text File requires a non-empty Text path.", syntax.Path.Span);
        VariableSymbol destination = ResolveArray(syntax.Destination, syntax.DestinationSpan);
        if (destination.Type is not { Kind: SmileTypeKind.Integer } || destination.ArrayRank != 1)
            Report("SMILE3027", "Load Text File requires a declared one-dimensional Number array.", syntax.DestinationSpan);
        VariableSymbol count = ResolveAssignmentTarget(syntax.Count, syntax.CountSpan, SmileType.Integer);
        ValidateWritableNumberTarget(count, syntax.CountSpan, "Load Text File Count");
        return new BoundTextFileLoadStatement(path, destination, count);
    }
}
