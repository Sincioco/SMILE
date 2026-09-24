namespace SMILE.Engine;

internal sealed partial class TargetIdentifierMap
{
    private readonly Dictionary<EnumTypeSymbol, string> _typeNames = new();
    private readonly Dictionary<EnumMemberSymbol, string> _memberNames = new();

    public string Get(EnumTypeSymbol type) => _typeNames[type];
    public string Get(EnumMemberSymbol member) => _memberNames[member];

    private void AddEnumNames(BoundProgram program, TargetLanguage language, ISet<string> reserved, ISet<string> used)
    {
        if (program.EnumTypes.Count == 0) return;
        reserved.UnionWith(new[] { "_SmileEnum", "Object", "java" });
        var memberReserved = new HashSet<string>(reserved, StringComparer.Ordinal);
        memberReserved.UnionWith(new[] { "value__", "rawValue", "hashValue", "RawValue", "allCases", "name", "value", "mro", "__proto__", "_name_", "_value_", "_ignore_", "_member_map_", "_smileDefault" });
        foreach (EnumTypeSymbol type in program.EnumTypes)
        {
            string preferred = IsSafeTargetIdentifier(type.Name, language, reserved) ? type.Name : BuildMappedName(type.Name, language);
            string typeName = MakeUnique(preferred, used, language);
            used.Add(typeName);
            _typeNames.Add(type, typeName);
            var scoped = new HashSet<string>(StringComparer.Ordinal) { typeName, "_smileDefault" };
            foreach (EnumMemberSymbol member in type.Members)
            {
                bool global = language is TargetLanguage.C or TargetLanguage.ObjectiveC or TargetLanguage.MasmX64 or TargetLanguage.Cobol;
                string source = global ? type.Name + "_" + member.Name : member.Name;
                if (language is TargetLanguage.Python && source.StartsWith('_') && source.EndsWith('_')) source = "member" + source;
                string candidate = IsSafeTargetIdentifier(source, language, memberReserved) ? source : BuildMappedName(source, language);
                string name = MakeUnique(candidate, global ? used : scoped, language);
                (global ? used : scoped).Add(name);
                _memberNames.Add(member, name);
            }
        }
    }
}
