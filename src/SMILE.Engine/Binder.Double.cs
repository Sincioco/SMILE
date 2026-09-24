namespace SMILE.Engine;

internal sealed partial class Binder
{
    private BoundExpression BindDoubleIntrinsic(CallExpressionSyntax syntax, BoundIntrinsicKind kind, bool constantsOnly)
    {
        int prior = _diagnostics.Count;
        BoundExpression[] arguments = syntax.Arguments.Select(argument => BindExpression(argument, constantsOnly)).ToArray();
        int expected = kind is BoundIntrinsicKind.Clamp ? 3 : kind is BoundIntrinsicKind.Atan2 or BoundIntrinsicKind.Min or BoundIntrinsicKind.Max ? 2 : 1;
        SmileType required = kind is BoundIntrinsicKind.ToDouble ? SmileType.Integer :
            kind is BoundIntrinsicKind.TextToDouble ? SmileType.String : SmileType.Double;
        if (arguments.Length != expected)
            Report("SMILE2153", $"Built-in function '{syntax.Name}' expects {expected} argument(s).", syntax.NameSpan);
        foreach (BoundExpression argument in arguments)
            if (argument.Type != required && argument.Type is not SmileType.Error)
                Report("SMILE3901", $"{syntax.Name} requires exact {DisplayType(required)} arguments; use explicit conversion.", syntax.Span);
        return prior == _diagnostics.Count ? new BoundIntrinsicExpression(kind, arguments, syntax.Span) : new BoundErrorExpression();
    }
}
