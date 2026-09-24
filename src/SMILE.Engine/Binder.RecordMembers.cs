namespace SMILE.Engine;

internal sealed partial class Binder
{
    private readonly Dictionary<RoutineDeclarationSyntax, RoutineSymbol> _recordRoutineSyntax = new();

    private void BuildRecordMemberSignatures()
    {
        foreach (RecordTypeSymbol type in _orderedRecords)
        {
            var names = type.Fields.Select(field => field.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var methods = new List<RoutineSymbol>();
            var properties = new List<RecordPropertySymbol>();
            foreach (SourceItemSyntax item in type.Declaration.SourceItems)
            {
                if (item is RecordMethodDeclarationSyntax method)
                {
                    if (!names.Add(method.Routine.Name)) Report("SMILE3440", $"Member '{method.Routine.Name}' is already declared.", method.Span);
                    RoutineSymbol symbol = BuildRoutineSignature(method.Routine, type, method.IsPrivate);
                    methods.Add(symbol);
                    _recordRoutineSyntax.Add(method.Routine, symbol);
                }
                else if (item is RecordPropertyDeclarationSyntax property)
                {
                    if (!names.Add(property.Name)) Report("SMILE3440", $"Member '{property.Name}' is already declared.", property.Span);
                    SmileType propertyType = ResolveType(property.DeclaredType);
                    RoutineSymbol? getter = property.Getter is null ? null : BuildRoutineSignature(property.Getter, type, property.IsPrivate, RecordMemberRoutineKind.PropertyGet);
                    RoutineSymbol? setter = property.Setter is null ? null : BuildRoutineSignature(property.Setter, type, property.IsPrivate, RecordMemberRoutineKind.PropertySet, propertyType);
                    if (getter is not null) _recordRoutineSyntax.Add(property.Getter!, getter);
                    if (setter is not null) _recordRoutineSyntax.Add(property.Setter!, setter);
                    properties.Add(new RecordPropertySymbol(type, property.Name, property.NameSpan, propertyType, property.IsPrivate, getter, setter));
                }
            }
            type.Methods = methods;
            type.Properties = properties;
        }
    }

    private BoundExpression BindMe(MeExpressionSyntax syntax)
    {
        if (_currentRoutine?.Receiver is { } receiver) return new BoundVariableExpression(receiver);
        Report("SMILE3442", "Me is available only inside an instance method or property.", syntax.Span);
        return new BoundErrorExpression();
    }

    private static bool IsWritableReceiver(BoundExpression expression) => BoundLocations.IsWritable(expression) ||
        expression is BoundVariableExpression { Variable.IsReceiver: true };

    private void CheckMemberAccess(RecordTypeSymbol owner, bool isPrivate, BoundExpression receiver, TextSpan span)
    {
        if (isPrivate && _currentRoutine?.Owner != owner) Report("SMILE3446", "A Private member is accessible only inside its declaring Type.", span);
        if (!IsWritableReceiver(receiver)) Report("SMILE3444", "A Type member call requires a writable receiver.", span);
    }

    private BoundCallExpression? BindMemberCall(MemberInvocationExpressionSyntax syntax, bool requireFunction)
    {
        BoundExpression receiver = BindExpression(syntax.Receiver);
        RoutineSymbol? method = (receiver.Type as RecordTypeSymbol)?.Methods.FirstOrDefault(item => item.Name.Equals(syntax.Name, StringComparison.OrdinalIgnoreCase));
        if (method is null)
        {
            Report("SMILE3443", $"Callable member '{syntax.Name}' does not exist on {receiver.Type.Name}.", syntax.NameSpan);
            foreach (ExpressionSyntax argument in syntax.Arguments) BindExpression(argument);
            return null;
        }
        CheckMemberAccess(method.Owner!, method.IsPrivate, receiver, syntax.NameSpan);
        if (method.IsFunction != requireFunction)
            Report("SMILE2131", requireFunction ? "A Sub cannot be used as an expression." : "A Function must be used as an expression.", syntax.NameSpan);
        BoundArguments arguments = BindCallArguments(method, syntax.Arguments, syntax.NameSpan);
        int[]? order = arguments.ParameterOrder is null ? null : new[] { 0 }.Concat(arguments.ParameterOrder.Select(index => index + 1)).ToArray();
        return new BoundCallExpression(method, new[] { receiver }.Concat(arguments.Values).ToArray(), order);
    }

    private BoundExpression BindMemberInvocation(MemberInvocationExpressionSyntax syntax) =>
        (BoundExpression?)BindMemberCall(syntax, requireFunction: true) ?? new BoundErrorExpression();

    private BoundStatement? BindMemberCallStatement(MemberCallStatementSyntax syntax)
    {
        BoundCallExpression? call = BindMemberCall(syntax.Invocation, requireFunction: false);
        return call is null ? null : new BoundCallStatement(call.Routine, call.Arguments, call.ParameterOrder);
    }

    private BoundExpression BindPropertyGet(RecordPropertySymbol property, BoundExpression receiver, TextSpan span)
    {
        CheckMemberAccess(property.Owner, property.IsPrivate, receiver, span);
        if (property.Getter is null)
        {
            Report("SMILE3445", $"Property '{property.Name}' is write-only.", span);
            return new BoundErrorExpression();
        }
        return new BoundCallExpression(property.Getter, new[] { receiver });
    }

    private BoundStatement? BindPropertySet(RecordPropertySymbol property, BoundExpression receiver, BoundExpression value, TextSpan span)
    {
        CheckMemberAccess(property.Owner, property.IsPrivate, receiver, span);
        if (property.Setter is null)
        {
            Report("SMILE3445", $"Property '{property.Name}' is read-only.", span);
            return null;
        }
        if (value.Type != SmileType.Error && value.Type != property.Type)
            Report("SMILE2106", $"Property '{property.Name}' requires {property.Type.Name}.", span);
        // The authored value runs first; receiver capture follows it. ABI order
        // remains receiver/value, just like ordinary named argument placement.
        return new BoundCallStatement(property.Setter, new[] { value, receiver }, new[] { 1, 0 });
    }
}
