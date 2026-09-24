namespace SMILE.Engine;

internal sealed partial class Binder
{
    private readonly Dictionary<string, ClassTypeSymbol> _classes = new(StringComparer.OrdinalIgnoreCase);

    private void LayoutClass(ClassTypeSymbol type)
    {
        long offset = 0;
        foreach (InstanceFieldSymbol field in type.Fields)
        {
            long size = (long)(field.Type is RecordTypeSymbol record ? record.NativeSize : 8) * field.ElementCount;
            if (offset + size > (int.MaxValue & ~7))
            {
                Report("SMILE3411", $"Class '{type.Name}' exceeds the supported storage size.", field.Span);
                size = 0;
            }
            field.NativeOffset = (int)offset;
            offset += size;
            type.ContainsText |= field.Type == SmileType.String || field.Type is RecordTypeSymbol { ContainsText: true };
        }
        type.InstanceSize = (int)Math.Max(8, offset);
    }

    private BoundExpression BindNew(NewExpressionSyntax syntax, bool constantsOnly)
    {
        SmileType type = ResolveType(syntax.DeclaredType);
        if (constantsOnly || type is not ClassTypeSymbol reference)
        {
            Report("SMILE3453", constantsOnly ? "A constant expression cannot allocate a Class." : "New requires a Class type.", syntax.Span);
            foreach (ExpressionSyntax argument in syntax.Arguments) BindExpression(argument);
            return new BoundErrorExpression();
        }
        BoundArguments arguments = BindCallArguments(reference.Constructor, syntax.Arguments, syntax.Span);
        return new BoundNewExpression(reference, arguments.Values, arguments.ParameterOrder);
    }

    private BoundExpression BindIdentity(IdentityExpressionSyntax syntax, bool constantsOnly)
    {
        BoundExpression left = BindExpression(syntax.Left, constantsOnly);
        BoundExpression right = BindExpression(syntax.Right, constantsOnly);
        bool valid = left.Type is ClassTypeSymbol && (right.Type == left.Type || right.Type == SmileType.Nothing) ||
            right.Type is ClassTypeSymbol && left.Type == SmileType.Nothing;
        if (!valid)
        {
            Report("SMILE3455", "Is and Is Not require the same Class type, or a Class reference and Nothing.", syntax.Span);
            return new BoundErrorExpression();
        }
        return new BoundIdentityExpression(CoerceReference(left, right.Type), CoerceReference(right, left.Type), syntax.Negated);
    }

    private static BoundExpression CoerceReference(BoundExpression expression, SmileType target) =>
        expression.Type == SmileType.Nothing && target is ClassTypeSymbol ? new BoundNothingExpression(target) : expression;
}
