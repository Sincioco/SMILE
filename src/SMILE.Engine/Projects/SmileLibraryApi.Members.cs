namespace SMILE.Engine;

internal sealed partial class SmileLibraryApi
{
    private IEnumerable<(string, string)> RoutineFields(RoutineSymbol routine) =>
    [
        ("returnType", routine.IsFunction ? TypeReference(routine.ReturnType!) : "null"),
        ("parameters", Array(routine.Parameters.Select(Parameter))), ("requiresGameWindow", "false")
    ];

    private string Parameter(VariableSymbol parameter, int ordinal) => Object(("name", Quote(parameter.Name)),
        ("type", TypeReference(parameter.Type)), ("mode", Quote(parameter.IsByRef ? "ByRef" : "ByVal")),
        ("optional", parameter.DefaultValue.HasValue ? "true" : "false"), ("default", Default(parameter)),
        ("ordinal", Number(ordinal)), ("location", Location(parameter.DeclarationSpan)));

    private string Default(VariableSymbol parameter)
    {
        if (parameter.DefaultValue is not SmileValue value) return "null";
        if (parameter.DefaultEnumMember is { } member)
            return Object(("kind", Quote("enum")), ("member", Quote(member.Name)), ("value", Number(member.Value)));
        string kind = value.Type.Kind switch
        {
            SmileTypeKind.Integer => "number", SmileTypeKind.Double => "double", SmileTypeKind.Boolean => "boolean", SmileTypeKind.String => "text",
            _ => throw new InvalidDataException("Unsupported Optional default metadata.")
        };
        return Object(("kind", Quote(kind)), ("value", Value(value)));
    }

    private string Field(InstanceFieldSymbol field)
    {
        var fields = new List<(string, string)> { ("name", Quote(field.Name)), ("visibility", Quote("Public")),
            (field.IsArray ? "elementType" : "type", TypeReference(field.Type)) };
        if (field.IsArray) { fields.Add(("rank", Number(field.Dimensions.Count))); fields.Add(("dimensions", Array(field.Dimensions.Select(Number)))); }
        fields.Add(("ordinal", Number(field.Ordinal)));
        if (field.Owner is RecordTypeSymbol) fields.Add(("offset", Number(field.NativeOffset)));
        fields.Add(("location", Location(field.Span with { Length = field.Name.Length })));
        return Object(fields.ToArray());
    }

    private IEnumerable<string> InstanceMembers(InstanceTypeSymbol type)
    {
        var members = new List<(string Name, string Kind, string Json)>();
        foreach (RoutineSymbol method in type.Methods.Where(method => !method.IsPrivate))
        {
            string kind = method.IsFunction ? "Function" : "Subroutine";
            var fields = new List<(string, string)> { ("name", Quote(method.Name)), ("kind", Quote(kind)),
                ("visibility", Quote("Public")), ("identity", Quote(Identity(type) + "::member::" + method.Name)) };
            fields.AddRange(RoutineFields(method));
            fields.Add(("location", Location(method.DeclarationSpan)));
            members.Add((method.Name, kind, Object(fields.ToArray())));
        }
        foreach (InstancePropertySymbol property in type.Properties.Where(property => !property.IsPrivate))
        {
            string identity = Identity(type) + "::property::" + property.Name;
            InstancePropertyDeclarationSyntax syntax = type.Declaration.SourceItems.OfType<InstancePropertyDeclarationSyntax>()
                .Single(item => item.Name.Equals(property.Name, StringComparison.OrdinalIgnoreCase));
            string Accessor(RoutineDeclarationSyntax? accessor, string suffix) => accessor is null ? "null"
                : Object(("identity", Quote(identity + "::" + suffix)), ("requiresGameWindow", "false"),
                    ("location", Location(accessor.Span with { Length = 3 })));
            members.Add((property.Name, "Property", Object(("name", Quote(property.Name)), ("kind", Quote("Property")),
                ("visibility", Quote("Public")), ("identity", Quote(identity)), ("type", TypeReference(property.Type)),
                ("get", Accessor(syntax.Getter, "get")), ("set", Accessor(syntax.Setter, "set")), ("location", Location(property.Span)))));
        }
        return members.OrderBy(member => member.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(member => member.Name, StringComparer.Ordinal).ThenBy(member => member.Kind).Select(member => member.Json);
    }

    private string Constructor(ClassTypeSymbol type)
    {
        RoutineSymbol constructor = type.Constructor;
        bool declared = type.Declaration.SourceItems.OfType<InstanceMethodDeclarationSyntax>()
            .Any(method => method.Routine.Name.Equals("New", StringComparison.OrdinalIgnoreCase));
        return Object(("identity", Quote(Identity(type) + "::constructor::New")), ("visibility", Quote("Public")),
            ("declared", declared ? "true" : "false"), ("parameters", Array(constructor.Parameters.Select(Parameter))),
            ("requiresGameWindow", "false"), ("location", Location(constructor.DeclarationSpan)));
    }
}
