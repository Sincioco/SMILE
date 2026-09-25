namespace SMILE.Engine;

internal sealed partial class ModuleCompilation
{
    private ExpressionSyntax LowerExpression(ExpressionSyntax expression, ModuleSource source, ISet<string>? locals) => expression switch
    {
        NameExpressionSyntax item => item with { Name = Reference(item.Name, item.Span, source, locals) },
        CallExpressionSyntax item => item with { Name = Reference(item.Name, item.NameSpan, source, locals, intrinsic: true), Arguments = Expressions(item.Arguments, source, locals) },
        ArrayAccessExpressionSyntax item => item with { Name = Reference(item.Name, item.NameSpan, source, locals), Indices = Expressions(item.Indices, source, locals) },
        NamedArgumentExpressionSyntax item => item with { Value = LowerExpression(item.Value, source, locals) },
        ParenthesizedExpressionSyntax item => item with { Expression = LowerExpression(item.Expression, source, locals) },
        UnaryExpressionSyntax item => item with { Operand = LowerExpression(item.Operand, source, locals) },
        BinaryExpressionSyntax item => item with { Left = LowerExpression(item.Left, source, locals), Right = LowerExpression(item.Right, source, locals) },
        IdentityExpressionSyntax item => item with { Left = LowerExpression(item.Left, source, locals), Right = LowerExpression(item.Right, source, locals) },
        NewExpressionSyntax item => item with { DeclaredType = TypeName(item.DeclaredType, source), Arguments = Expressions(item.Arguments, source, locals) },
        MemberAccessExpressionSyntax item => LowerMember(item, source, locals),
        MemberInvocationExpressionSyntax item => LowerInvocation(item, source, locals),
        IndexedMemberExpressionSyntax item => LowerIndexedMember(item, source, locals),
        _ => expression
    };

    private IReadOnlyList<ExpressionSyntax> Expressions(IReadOnlyList<ExpressionSyntax> expressions, ModuleSource source, ISet<string>? locals) =>
        expressions.Select(item => LowerExpression(item, source, locals)).ToArray();

    private ExpressionSyntax LowerMember(MemberAccessExpressionSyntax member, ModuleSource source, ISet<string>? locals)
    {
        if (member.Receiver is MemberAccessExpressionSyntax importedType && IsImport(importedType.Receiver, source, locals, out string typeAlias) &&
            source.Imports[typeAlias].Types.TryGetValue(importedType.Name, out ModuleMember? importedEnum) && importedEnum.Declaration is EnumDeclarationSyntax)
            return member with { Receiver = new NameExpressionSyntax(Qualified(typeAlias, importedType.Name, importedType.NameSpan, source, typeOnly: true), importedType.Span) };
        if (member.Receiver is NameExpressionSyntax ownType && locals?.Contains(ownType.Name) != true &&
            source.Module?.Types.TryGetValue(ownType.Name, out ModuleMember? ownEnum) == true && ownEnum.Declaration is EnumDeclarationSyntax)
            return member with { Receiver = ownType with { Name = ownEnum.BoundName } };
        if (IsImport(member.Receiver, source, locals, out string alias))
            return new NameExpressionSyntax(Qualified(alias, member.Name, member.NameSpan, source, allowType: true), member.Span);
        return member with { Receiver = LowerExpression(member.Receiver, source, locals) };
    }

    private ExpressionSyntax LowerInvocation(MemberInvocationExpressionSyntax call, ModuleSource source, ISet<string>? locals)
    {
        IReadOnlyList<ExpressionSyntax> arguments = Expressions(call.Arguments, source, locals);
        if (IsImport(call.Receiver, source, locals, out string alias))
            return new CallExpressionSyntax(Qualified(alias, call.Name, call.NameSpan, source), call.NameSpan, arguments, call.Span);
        return call with { Receiver = LowerExpression(call.Receiver, source, locals), Arguments = arguments };
    }

    private ExpressionSyntax LowerIndexedMember(IndexedMemberExpressionSyntax array, ModuleSource source, ISet<string>? locals)
    {
        ExpressionSyntax member = LowerMember(array.Member, source, locals);
        IReadOnlyList<ExpressionSyntax> indices = Expressions(array.Indices, source, locals);
        return member is NameExpressionSyntax name ? new ArrayAccessExpressionSyntax(name.Name, name.Span, indices, array.Span)
            : array with { Member = (MemberAccessExpressionSyntax)member, Indices = indices };
    }

    private static bool IsImport(ExpressionSyntax receiver, ModuleSource source, ISet<string>? locals, out string alias)
    {
        alias = receiver is NameExpressionSyntax name ? name.Name : "";
        return locals?.Contains(alias) != true && source.Imports.ContainsKey(alias);
    }

    private string Reference(string name, TextSpan span, ModuleSource source, ISet<string>? locals, bool intrinsic = false)
    {
        if (name.Contains('.'))
        {
            string[] parts = name.Split('.', 2);
            return Qualified(parts[0], parts[1], span, source);
        }
        if (locals?.Contains(name) == true) return name;
        if (source.Module?.Members.TryGetValue(name, out ModuleMember? member) == true) return member.BoundName;
        if (!intrinsic && source.Module?.Types.TryGetValue(name, out ModuleMember? type) == true) return type.BoundName;
        if (intrinsic && IntrinsicNames.Contains(name)) return name;
        if (source.Module is not null) Report("SMILE3510", $"Module '{source.Module.Name}' cannot access undeclared or consuming-program name '{name}'.", span);
        return name;
    }

    private string Qualified(string alias, string name, TextSpan span, ModuleSource source, bool allowType = false, bool typeOnly = false)
    {
        if (!source.Imports.TryGetValue(alias, out ModuleSymbol? module))
        { Report("SMILE3502", $"Import alias '{alias}' is not declared in this source.", span); return name; }
        ModuleMember? member = null;
        if (!typeOnly) module.Members.TryGetValue(name, out member);
        if ((allowType || typeOnly) && member is null) module.Types.TryGetValue(name, out member);
        if (member is null)
        { Report("SMILE3503", $"Module '{module.Name}' has no member '{name}'.", span); return name; }
        if (!member.IsPublic) Report("SMILE3505", $"Member '{module.Name}.{name}' is Private.", span);
        return member.BoundName;
    }

    private TypeNameSyntax TypeName(TypeNameSyntax type, ModuleSource source)
    {
        if (BuiltInTypes.Contains(type.Name)) return type;
        if (type.Name.Contains('.'))
        {
            string[] parts = type.Name.Split('.', 2);
            return type with { Name = Qualified(parts[0], parts[1], type.Span, source, typeOnly: true) };
        }
        if (source.Module?.Types.TryGetValue(type.Name, out ModuleMember? member) == true) return type with { Name = member.BoundName };
        if (source.Module is not null) Report("SMILE3511", $"Type '{type.Name}' is not declared in this Module; use an explicit import alias for external types.", type.Span);
        return type;
    }

    private static readonly HashSet<string> BuiltInTypes = new(StringComparer.OrdinalIgnoreCase) { "Number", "Double", "Text", "Boolean" };
    private static readonly HashSet<string> IntrinsicNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ToDouble", "ToNumber", "Clamp", "Sqrt", "Sin", "Cos", "Atan2", "Floor", "Ceiling", "Truncate", "Round",
        "Text_From_Double", "Text_To_Double", "Timer", "Abs", "Min", "Max", "Text_Length", "Text_Code_At", "Text_Slice"
    };
}
