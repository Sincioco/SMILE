namespace SMILE.Engine;

internal sealed partial class Binder
{
    private BoundStatement BindDataLoad(DataLoadStatementSyntax syntax) => new BoundDataLoadStatement(
        BindDataValue(syntax.Key, SmileType.String, "key"), BindDataArray(syntax.Destination, syntax.DestinationSpan),
        BindDataTarget(syntax.Count), syntax.Status is null ? null : BindDataTarget(syntax.Status));

    private BoundStatement BindDataSave(DataSaveStatementSyntax syntax) => new BoundDataSaveStatement(
        BindDataArray(syntax.Source, syntax.SourceSpan), BindDataValue(syntax.Count, SmileType.Integer, "Count"),
        BindDataValue(syntax.Key, SmileType.String, "key"), syntax.Status is null ? null : BindDataTarget(syntax.Status));

    private VariableSymbol BindDataArray(string name, TextSpan span)
    {
        VariableSymbol array = ResolveArray(name, span);
        if (array.Type != SmileType.Integer || array.ArrayRank != 1)
            Report("SMILE3506", "Data requires a fixed one-dimensional Number array.", span);
        return array;
    }

    private BoundExpression BindDataValue(ExpressionSyntax syntax, SmileType type, string role)
    {
        BoundExpression value = BindExpression(syntax);
        if (value.Type != type && value.Type != SmileType.Error)
            Report("SMILE3506", $"Data {role} must be {DisplayType(type)}.", syntax.Span);
        return value;
    }

    private BoundExpression BindDataTarget(ExpressionSyntax syntax)
    {
        if (syntax is NameExpressionSyntax name)
        {
            VariableSymbol variable = ResolveAssignmentTarget(name.Name, name.Span, SmileType.Integer);
            ValidateWritableNumberTarget(variable, name.Span, "Data output");
            return new BoundVariableExpression(variable);
        }
        BoundExpression target = BindDataValue(syntax, SmileType.Integer, "output");
        if (target is not BoundErrorExpression && !BoundLocations.IsWritable(target))
            Report("SMILE3506", "Data output requires writable Number storage.", syntax.Span);
        return target;
    }
}
