namespace SMILE.Engine;

internal sealed partial class ModuleCompilation
{
    private IReadOnlyList<SourceItemSyntax> LowerItems(IReadOnlyList<SourceItemSyntax> items, ModuleSource source, ISet<string>? locals, bool sourceLevel = false) =>
        items.Where(item => !sourceLevel || item is not ImportStatementSyntax and not OptionExplicitStatementSyntax)
            .Select(item => LowerItem(item, source, locals)).ToArray();

    private SourceItemSyntax LowerItem(SourceItemSyntax item, ModuleSource source, ISet<string>? locals)
    {
        ExpressionSyntax E(ExpressionSyntax value) => LowerExpression(value, source, locals);
        string R(string name, TextSpan span) => Reference(name, span, source, locals);
        string D(string name) => locals is null && source.Module is not null
            ? (item is InstanceDeclarationSyntax or EnumDeclarationSyntax ? source.Module.Types : source.Module.Members)[name].BoundName : name;
        IReadOnlyList<SourceItemSyntax> Body(IReadOnlyList<SourceItemSyntax> body) => LowerItems(body, source, locals);
        switch (item)
        {
            case VisibilityDeclarationSyntax visible:
                if (locals is not null) Report("SMILE3501", "Public and Private require a declaration directly inside a Module.", visible.Span);
                return LowerItem(visible.Declaration, source, locals);
            case ImportStatementSyntax import:
                Report("SMILE3506", "Imports belong at the start of a source or Module.", import.Span); return import;
            case ModuleDeclarationSyntax nested:
                Report("SMILE3500", "Modules cannot be nested.", nested.Span); return nested;
            case DimStatementSyntax dim: return dim with { Name = D(dim.Name), DeclaredType = TypeName(dim.DeclaredType, source), ArraySizes = Expressions(dim.ArraySizes, source, locals), Initializer = dim.Initializer is null ? null : (NewExpressionSyntax)E(dim.Initializer) };
            case ConstStatementSyntax constant: return constant with { Name = D(constant.Name), Initializer = E(constant.Initializer) };
            case RoutineDeclarationSyntax routine: return LowerRoutine(routine, source, rename: true);
            case EnumDeclarationSyntax enumeration: return enumeration with { Name = D(enumeration.Name), SourceItems = enumeration.SourceItems.Select(member => member is EnumMemberDeclarationSyntax value && value.Value is not null ? value with { Value = E(value.Value) } : member).ToArray() };
            case RecordDeclarationSyntax record: return record with { Name = D(record.Name), SourceItems = InstanceMembers(record.SourceItems, source) };
            case ClassDeclarationSyntax reference: return reference with { Name = D(reference.Name), SourceItems = InstanceMembers(reference.SourceItems, source) };
            case CoreAssignmentStatementSyntax assignment: return assignment with { Name = R(assignment.Name, assignment.NameSpan), Value = E(assignment.Value) };
            case CoreArrayAssignmentStatementSyntax assignment: return assignment with { Name = R(assignment.Name, assignment.NameSpan), Indices = Expressions(assignment.Indices, source, locals), Value = E(assignment.Value) };
            case MemberAssignmentStatementSyntax assignment:
            {
                ExpressionSyntax target = E(assignment.Target), value = E(assignment.Value);
                return target switch
                {
                    NameExpressionSyntax name => new CoreAssignmentStatementSyntax(name.Name, name.Span, value, assignment.Span),
                    ArrayAccessExpressionSyntax array => new CoreArrayAssignmentStatementSyntax(array.Name, array.NameSpan, array.Indices, value, assignment.Span),
                    _ => assignment with { Target = target, Value = value }
                };
            }
            case MemberCallStatementSyntax call:
            {
                ExpressionSyntax invocation = LowerInvocation(call.Invocation, source, locals);
                return invocation is CallExpressionSyntax simple ? new CallStatementSyntax(simple.Name, simple.NameSpan, simple.Arguments, call.Span)
                    : call with { Invocation = (MemberInvocationExpressionSyntax)invocation };
            }
            case CallStatementSyntax call: return call with { Name = R(call.Name, call.NameSpan), Arguments = Expressions(call.Arguments, source, locals) };
            case CorePrintStatementSyntax print: return print with { Values = Expressions(print.Values, source, locals) };
            case ReturnStatementSyntax result: return result with { Value = result.Value is null ? null : E(result.Value) };
            case IfStatementSyntax conditional: return new IfStatementSyntax(conditional.Clauses.Select(clause => new ConditionalClauseSyntax(E(clause.Condition), Body(clause.SourceItems), clause.Span)).ToArray(), Body(conditional.ElseSourceItems), conditional.HasElseClause, conditional.Span);
            case SelectStatementSyntax select: return select with { Selector = E(select.Selector), Cases = select.Cases.Select(clause => clause with { Value = clause.Value is null ? null : E(clause.Value), SourceItems = Body(clause.SourceItems) }).ToArray() };
            case ForStatementSyntax loop: return loop with { CounterName = R(loop.CounterName, loop.CounterSpan), LowerBound = E(loop.LowerBound), UpperBound = E(loop.UpperBound), SourceItems = Body(loop.SourceItems) };
            case DoStatementSyntax loop: return loop with { UntilCondition = loop.UntilCondition is null ? null : E(loop.UntilCondition), SourceItems = Body(loop.SourceItems) };
            case WithStatementSyntax receiver: return receiver with { Target = E(receiver.Target), SourceItems = Body(receiver.SourceItems) };
            case WaitStatementSyntax wait: return wait with { Duration = E(wait.Duration) };
            case MoveCursorStatementSyntax move: return move with { Column = E(move.Column), Row = E(move.Row) };
            case RandomStatementSyntax random: return random with { Name = R(random.Name, random.NameSpan), LowerBound = E(random.LowerBound), UpperBound = E(random.UpperBound) };
            case GetKeyStatementSyntax key: return key with { Name = R(key.Name, key.NameSpan) };
            case TextFileLoadStatementSyntax load: return load with { Path = E(load.Path), Destination = R(load.Destination, load.DestinationSpan), Count = R(load.Count, load.CountSpan) };
            case NumberLoadStatementSyntax load: return load with { Name = R(load.Name, load.NameSpan), DefaultValue = E(load.DefaultValue) };
            case NumberSaveStatementSyntax save: return save with { Name = R(save.Name, save.NameSpan) };
            case DataLoadStatementSyntax load: return load with { Key = E(load.Key), Destination = R(load.Destination, load.DestinationSpan), Count = E(load.Count), Status = load.Status is null ? null : E(load.Status) };
            case DataSaveStatementSyntax save: return save with { Source = R(save.Source, save.SourceSpan), Key = E(save.Key), Count = E(save.Count), Status = save.Status is null ? null : E(save.Status) };
            default: return item;
        }
    }

    private RoutineDeclarationSyntax LowerRoutine(RoutineDeclarationSyntax routine, ModuleSource source, bool rename, bool setter = false)
    {
        var locals = new HashSet<string>(routine.Parameters.Select(parameter => parameter.Name), StringComparer.OrdinalIgnoreCase);
        if (setter) locals.Add("Value");
        CollectLocals(routine.SourceItems, source, locals);
        return routine with
        {
            Name = rename && source.Module is not null ? source.Module.Members[routine.Name].BoundName : routine.Name,
            ReturnType = routine.ReturnType is null ? null : TypeName(routine.ReturnType, source),
            Parameters = routine.Parameters.Select(parameter => parameter with { DeclaredType = TypeName(parameter.DeclaredType, source), DefaultValue = parameter.DefaultValue is null ? null : LowerExpression(parameter.DefaultValue, source, null) }).ToArray(),
            SourceItems = LowerItems(routine.SourceItems, source, locals)
        };
    }

    private IReadOnlyList<SourceItemSyntax> InstanceMembers(IReadOnlyList<SourceItemSyntax> members, ModuleSource source) => members.Select(member => member switch
    {
        InstanceFieldDeclarationSyntax field => (SourceItemSyntax)(field with { DeclaredType = TypeName(field.DeclaredType, source), Dimensions = Expressions(field.Dimensions, source, null) }),
        InstanceMethodDeclarationSyntax method => method with { Routine = LowerRoutine(method.Routine, source, rename: false) },
        InstancePropertyDeclarationSyntax property => property with
        {
            DeclaredType = TypeName(property.DeclaredType, source),
            Getter = property.Getter is null ? null : LowerRoutine(property.Getter, source, rename: false),
            Setter = property.Setter is null ? null : LowerRoutine(property.Setter, source, rename: false, setter: true)
        },
        _ => member
    }).ToArray();

    private void CollectLocals(IReadOnlyList<SourceItemSyntax> items, ModuleSource source, ISet<string> locals)
    {
        foreach (SourceItemSyntax item in items)
        {
            if (item is DimStatementSyntax dim) locals.Add(dim.Name);
            if (item is ConstStatementSyntax constant) locals.Add(constant.Name);
            string? inferred = item switch
            {
                CoreAssignmentStatementSyntax assignment => assignment.Name, ForStatementSyntax loop => loop.CounterName,
                GetKeyStatementSyntax key => key.Name, RandomStatementSyntax random => random.Name,
                TextFileLoadStatementSyntax load => load.Count, _ => null
            };
            if (!source.OptionExplicit && inferred is not null && !inferred.Contains('.') && source.Module?.Members.ContainsKey(inferred) != true)
                locals.Add(inferred);
            IEnumerable<IReadOnlyList<SourceItemSyntax>> bodies = item switch
            {
                IfStatementSyntax conditional => conditional.Clauses.Select(clause => clause.SourceItems).Append(conditional.ElseSourceItems),
                SelectStatementSyntax select => select.Cases.Select(clause => clause.SourceItems),
                ForStatementSyntax loop => [loop.SourceItems], DoStatementSyntax loop => [loop.SourceItems],
                WithStatementSyntax receiver => [receiver.SourceItems], _ => []
            };
            foreach (IReadOnlyList<SourceItemSyntax> body in bodies) CollectLocals(body, source, locals);
        }
    }
}
