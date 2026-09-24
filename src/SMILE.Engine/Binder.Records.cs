namespace SMILE.Engine;

internal sealed partial class Binder
{
    private readonly Dictionary<string, RecordTypeSymbol> _records = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<RecordTypeSymbol> _orderedRecords = new();
    private readonly Stack<WithLocationSymbol?> _withLocations = new();

    private BoundStatement BindWith(WithStatementSyntax syntax)
    {
        BoundExpression target = BindExpression(syntax.Target);
        var location = new WithLocationSymbol(target);
        bool valid = target.Type is RecordTypeSymbol && IsWritableReceiver(target);
        if (target.Type != SmileType.Error && !valid)
            Report(target.Type is RecordTypeSymbol ? "SMILE3412" : "SMILE3415",
                "With requires a stable writable record location.", syntax.Target.Span);
        // Invalid inner blocks must hide, rather than reuse, an outer receiver.
        _withLocations.Push(valid ? location : null);
        IReadOnlyList<BoundSourceItem> body = BindItems(syntax.SourceItems, directProgramLevel: false);
        _withLocations.Pop();
        return new BoundWithStatement(location, body);
    }

    private BoundExpression BindWithReceiver(WithReceiverExpressionSyntax syntax)
    {
        if (_withLocations.TryPeek(out WithLocationSymbol? location) && location is not null)
            return new BoundWithReceiverExpression(location);
        Report("SMILE3413", "A leading-dot member requires a valid enclosing With block.", syntax.Span);
        return new BoundErrorExpression();
    }

    private SmileType ResolveVariableType(DimStatementSyntax syntax, IReadOnlyList<int> dimensions)
    {
        SmileType type = ResolveType(syntax.DeclaredType);
        if (type is RecordTypeSymbol record && dimensions.Count > 0 &&
            dimensions.Aggregate(1L, (count, size) => count * size) > int.MaxValue / record.NativeSize)
            Report("SMILE3411", $"Record array '{syntax.Name}' exceeds the supported storage size.", syntax.Span);
        return type;
    }

    private void BindRecords()
    {
        foreach (RecordTypeSymbol type in _records.Values)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var fields = new List<RecordFieldSymbol>();
            foreach (RecordFieldDeclarationSyntax syntax in type.Declaration.SourceItems.OfType<RecordFieldDeclarationSyntax>())
            {
                if (!names.Add(syntax.Name)) { Report("SMILE3402", $"Field '{syntax.Name}' is already declared in Type '{type.Name}'.", syntax.Span); continue; }
                var dimensionSyntax = new DimStatementSyntax(syntax.Name, syntax.Span, syntax.DeclaredType, syntax.Dimensions, syntax.Span);
                IReadOnlyList<int> dimensions = ResolveArrayDimensions(dimensionSyntax);
                fields.Add(new RecordFieldSymbol(type, syntax.Name, ResolveType(syntax.DeclaredType), dimensions, fields.Count, syntax.Span));
            }
            type.Fields = fields;
            if (fields.Count == 0 && !type.Declaration.SourceItems.Any(item => item is RecordMethodDeclarationSyntax or RecordPropertyDeclarationSyntax))
                Report("SMILE3402", "A Type must declare at least one member.", type.Declaration.Span);
        }
        var states = new Dictionary<RecordTypeSymbol, bool>();
        foreach (RecordTypeSymbol type in _records.Values) LayoutRecord(type, states);
    }

    private void LayoutRecord(RecordTypeSymbol type, Dictionary<RecordTypeSymbol, bool> states)
    {
        if (states.TryGetValue(type, out bool complete))
        {
            if (!complete) Report("SMILE3404", $"Type '{type.Name}' has a recursive value layout.", type.Declaration.Span);
            return;
        }
        states[type] = false;
        long offset = 0;
        foreach (RecordFieldSymbol field in type.Fields)
        {
            if (field.Type is RecordTypeSymbol nested) LayoutRecord(nested, states);
            int elementSize = field.Type is RecordTypeSymbol record ? record.NativeSize : 8;
            long size = (long)elementSize * field.ElementCount;
            if (offset + size > (int.MaxValue & ~7))
            {
                Report("SMILE3411", $"Type '{type.Name}' exceeds the supported storage size.", field.Span);
                size = 0;
            }
            field.NativeOffset = (int)offset;
            offset += size;
            type.ContainsText |= field.Type == SmileType.String || field.Type is RecordTypeSymbol { ContainsText: true };
        }
        type.NativeSize = (int)Math.Max(8, offset);
        states[type] = true;
        _orderedRecords.Add(type);
    }

    private BoundExpression BindRecordField(MemberAccessExpressionSyntax syntax, IReadOnlyList<ExpressionSyntax>? indexes, bool constantsOnly, BoundExpression? receiver = null)
    {
        if (constantsOnly)
        {
            Report("SMILE3406", "A constant expression cannot read a record field.", syntax.Span);
            return new BoundErrorExpression();
        }
        receiver ??= BindExpression(syntax.Receiver);
        RecordPropertySymbol? property = (receiver.Type as RecordTypeSymbol)?.Properties.FirstOrDefault(item => item.Name.Equals(syntax.Name, StringComparison.OrdinalIgnoreCase));
        if (property is not null && indexes is null) return BindPropertyGet(property, receiver, syntax.NameSpan);
        RecordFieldSymbol? field = (receiver.Type as RecordTypeSymbol)?.Fields.FirstOrDefault(item => item.Name.Equals(syntax.Name, StringComparison.OrdinalIgnoreCase));
        if (field is null)
        {
            Report("SMILE3406", $"Field '{syntax.Name}' does not exist on {receiver.Type.Name}.", syntax.NameSpan);
            return new BoundErrorExpression();
        }
        if (field.IsArray && indexes is null || !field.IsArray && indexes is not null)
        {
            Report("SMILE3407", field.IsArray ? "A fixed-array field requires indexes." : "A scalar field cannot be indexed.", syntax.Span);
            return new BoundErrorExpression();
        }
        return new BoundFieldExpression(receiver, field, indexes is null ? [] : BindArrayIndices(field.Name, field.Dimensions, indexes, syntax.Span));
    }

    private BoundStatement? BindMemberAssignment(MemberAssignmentStatementSyntax syntax)
    {
        BoundExpression? target = null;
        if (syntax.Target is MeExpressionSyntax)
        {
            BindMe((MeExpressionSyntax)syntax.Target);
            Report("SMILE3442", "Me is borrowed instance state and cannot be assigned.", syntax.Target.Span);
            BindExpression(syntax.Value);
            return null;
        }
        if (syntax.Target is MemberAccessExpressionSyntax member)
        {
            BoundExpression receiver = BindExpression(member.Receiver);
            RecordPropertySymbol? property = (receiver.Type as RecordTypeSymbol)?.Properties.FirstOrDefault(item => item.Name.Equals(member.Name, StringComparison.OrdinalIgnoreCase));
            if (property is not null) return BindPropertySet(property, receiver, BindExpression(syntax.Value), member.NameSpan);
            target = BindRecordField(member, null, constantsOnly: false, receiver);
        }
        target ??= BindExpression(syntax.Target);
        BoundExpression value = BindExpression(syntax.Value);
        if (target is not BoundFieldExpression field)
        {
            if (target is not BoundErrorExpression) Report(target is BoundVariableExpression { Variable.IsReceiver: true } ? "SMILE3442" : "SMILE3408", "Assignment requires a writable field location.", syntax.Target.Span);
            return null;
        }
        if (!BoundLocations.IsWritable(field)) Report("SMILE3408", "A field assignment requires writable record storage.", syntax.Target.Span);
        if (value.Type != SmileType.Error && value.Type != field.Type)
            Report("SMILE2106", $"Cannot assign {value.Type.Name} to {field.Type.Name} field '{field.Field.Name}'.", syntax.Value.Span);
        return new BoundMemberSetStatement(field, value);
    }

    private BoundSourceItem? BindNestedRecord(RecordDeclarationSyntax syntax)
    {
        Report("SMILE3400", "Type declarations must appear directly at program level.", syntax.Span);
        return null;
    }
}
