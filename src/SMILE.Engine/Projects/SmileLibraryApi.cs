using System.Globalization;
using System.Text;

namespace SMILE.Engine;

// Format 7 is a source-owned contract. Metadata is derived from bound symbols;
// a package reader compares these exact bytes instead of trusting declarations
// supplied by the archive's public-symbols.json.
internal sealed partial class SmileLibraryApi(BoundProgram program, string name, string version,
    IReadOnlyDictionary<string, string> sourceIds)
{
    private readonly string _provider = name + "@" + version;
    private readonly Dictionary<string, VariableSymbol> _variables = program.Variables.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RoutineSymbol> _routines = program.Routines.Where(item => item.Symbol.Owner is null).ToDictionary(item => item.Symbol.Name, item => item.Symbol, StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SmileType> _types = program.InstanceTypes.Cast<SmileType>().Concat(program.EnumTypes).ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<VariableSymbol, SmileValue> _constants = program.SourceItems.OfType<BoundConstStatement>().ToDictionary(item => item.Variable, item => item.Value);

    public string Build()
    {
        var builder = new StringBuilder("{\n  \"formatVersion\": 7,\n  \"library\": ");
        builder.Append(Object(("name", Quote(name)), ("version", Quote(version)), ("provider", Quote(_provider))));
        builder.Append(",\n  \"modules\": [");
        ModuleSymbol[] modules = program.Modules.Where(module => module.Provider == _provider)
            .OrderBy(module => module.Name, StringComparer.OrdinalIgnoreCase).ThenBy(module => module.Name, StringComparer.Ordinal).ToArray();
        for (int index = 0; index < modules.Length; index++)
        {
            ModuleSymbol module = modules[index];
            builder.Append(index == 0 ? "\n" : ",\n").Append("    {\"name\": ").Append(Quote(module.Name))
                .Append(", \"provider\": ").Append(Quote(_provider)).Append(", \"sources\": ")
                .Append(Array(module.Sources.Select(source => SourceId(source.Source.Path)).Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).Select(Quote)))
                .Append(", \"members\": [");
            ModuleMember[] members = module.AllMembers.Where(member => member.IsPublic)
                .OrderBy(member => member.Name, StringComparer.OrdinalIgnoreCase).ThenBy(member => member.Name, StringComparer.Ordinal)
                .ThenBy(member => Kind(member) switch { "Constant" => 0, "Variable" => 1, "Array" => 2, "Type" => 3, "Class" => 4, "Enum" => 5, "Subroutine" => 6, _ => 7 }).ToArray();
            for (int memberIndex = 0; memberIndex < members.Length; memberIndex++)
                builder.Append(memberIndex == 0 ? "\n" : ",\n").Append("      ").Append(Member(members[memberIndex]));
            if (members.Length > 0) builder.Append("\n    ");
            builder.Append("]}");
        }
        if (modules.Length > 0) builder.Append("\n  ");
        return builder.Append("]\n}\n").ToString();
    }

    private string Member(ModuleMember member)
    {
        var fields = new List<(string, string)> { ("name", Quote(member.Name)), ("kind", Quote(Kind(member))), ("visibility", Quote("Public")) };
        if (_variables.TryGetValue(member.BoundName, out VariableSymbol? variable))
        {
            fields.Add((variable.IsArray ? "elementType" : "type", TypeReference(variable.Type)));
            if (variable.IsConstant) fields.Add(("value", Value(_constants[variable])));
            if (variable.IsArray) { fields.Add(("rank", Number(variable.ArrayRank))); fields.Add(("dimensions", Array(variable.ArrayDimensions.Select(Number)))); }
        }
        if (_routines.TryGetValue(member.BoundName, out RoutineSymbol? routine)) fields.AddRange(RoutineFields(routine));
        if (_types.TryGetValue(member.BoundName, out SmileType? type))
        {
            fields.Add(("identity", Quote(Identity(type))));
            fields.Add(("module", Quote(member.Owner.Name)));
            fields.Add(("provider", Quote(member.Owner.Provider)));
            fields.Add(("size", Number(type is RecordTypeSymbol record ? record.NativeSize : 8)));
            fields.Add(("alignment", "8"));
            if (type is InstanceTypeSymbol instance)
            {
                fields.Add(("fields", Array(instance.Fields.Where(field => !field.IsPrivate).OrderBy(field => field.Ordinal).Select(Field))));
                if (instance is ClassTypeSymbol reference) fields.Add(("constructor", Constructor(reference)));
                fields.Add(("members", Array(InstanceMembers(instance))));
            }
            else if (type is EnumTypeSymbol enumeration)
                fields.Add(("members", Array(enumeration.Members.Select((value, ordinal) => Object(("name", Quote(value.Name)),
                    ("value", Number(value.Value)), ("ordinal", Number(ordinal)), ("location", Location(value.Span with { Length = value.Name.Length })))))));
        }
        fields.Add(("location", Location(DeclarationSpan(member.Declaration))));
        return Object(fields.ToArray());
    }

    private string TypeReference(SmileType type)
    {
        if (!program.ModuleMembers.TryGetValue(type.Name, out ModuleMember? member))
            return Object(("kind", Quote("primitive")), ("name", Quote(type.Name)));
        string kind = type.Kind switch { SmileTypeKind.Record => "type", SmileTypeKind.Enum => "enum", SmileTypeKind.Class => "class", _ => throw new InvalidDataException("Unsupported nominal type.") };
        return Object(("kind", Quote(kind)), ("name", Quote(member.Name)), ("identity", Quote(Identity(type))),
            ("module", Quote(member.Owner.Name)), ("provider", Quote(member.Owner.Provider)));
    }

    private string Identity(SmileType type)
    {
        ModuleMember member = program.ModuleMembers[type.Name];
        return member.Owner.Name + "::" + member.Name;
    }

    private string SourceId(string? path) => path is not null && sourceIds.TryGetValue(path, out string? id) ? id
        : throw new InvalidDataException($"Public API source '{path}' is absent from the package source list.");
    private string Location(TextSpan span) => Object(("source", Quote(SourceId(span.SourcePath))),
        ("line", Number(span.Line)), ("column", Number(span.Column)), ("length", Number(span.Length)));

    private static TextSpan DeclarationSpan(StatementSyntax declaration) => declaration switch
    {
        DimStatementSyntax dim => dim.NameSpan, ConstStatementSyntax constant => constant.NameSpan,
        RoutineDeclarationSyntax routine => routine.NameSpan, EnumDeclarationSyntax enumeration => enumeration.NameSpan,
        InstanceDeclarationSyntax instance => instance.NameSpan, _ => declaration.Span
    };
    private static string Kind(ModuleMember member) => member.Declaration switch
    {
        ConstStatementSyntax => "Constant", DimStatementSyntax { IsArray: true } => "Array", DimStatementSyntax => "Variable",
        RoutineDeclarationSyntax { Kind: RoutineKind.Function } => "Function", RoutineDeclarationSyntax => "Subroutine",
        RecordDeclarationSyntax => "Type", ClassDeclarationSyntax => "Class", EnumDeclarationSyntax => "Enum", _ => throw new InvalidDataException("Unsupported module declaration.")
    };
    private static string Number(int value) => Number((long)value);
    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Array(IEnumerable<string> values) => "[" + string.Join(", ", values) + "]";
    private static string Object(params (string Name, string Value)[] fields) => "{" + string.Join(", ", fields.Select(field => Quote(field.Name) + ": " + field.Value)) + "}";
    private static string Value(SmileValue value) => value.Type.Kind switch
    {
        SmileTypeKind.String => Quote(value.StringValue), SmileTypeKind.Boolean => value.BooleanValue ? "true" : "false",
        SmileTypeKind.Double => Quote(DoubleSemantics.Format(value.DoubleValue)), _ => Number(value.IntegerValue)
    };
    private static string Quote(string text)
    {
        var builder = new StringBuilder("\"");
        foreach (char character in text)
            builder.Append(character switch
            {
                '\\' => "\\\\", '"' => "\\\"", '\n' => "\\n", '\r' => "\\r", '\t' => "\\t",
                < ' ' => "\\u" + ((int)character).ToString("x4", CultureInfo.InvariantCulture), _ => character.ToString()
            });
        return builder.Append('"').ToString();
    }
}
