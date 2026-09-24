namespace SMILE.Engine;

internal sealed partial class TargetIdentifierMap
{
    private readonly Dictionary<RecordTypeSymbol, string> _recordNames = new();
    private readonly Dictionary<RecordFieldSymbol, string> _fieldNames = new();
    public string Get(RecordTypeSymbol type) => _recordNames[type];
    public string Get(RecordFieldSymbol field) => _fieldNames[field];

    private void AddRecordNames(BoundProgram program, TargetLanguage language, ISet<string> reserved, ISet<string> used)
    {
        if (program.RecordTypes.Count == 0) return;
        reserved.UnionWith(new[] { "_smileDataclass", "_smileField", "_smileDeepcopy", "SmileReference", "java" });
        foreach (RecordTypeSymbol type in program.RecordTypes)
        {
            string preferred = IsSafeTargetIdentifier(type.Name, language, reserved) ? type.Name : BuildMappedName(type.Name, language);
            string typeName = MakeUnique(preferred, used, language);
            used.Add(typeName);
            _recordNames.Add(type, typeName);
            if (language is TargetLanguage.C or TargetLanguage.ObjectiveC) used.Add(typeName + "_default");
            var fields = new HashSet<string>(StringComparer.Ordinal) { typeName, "copy", "copyFrom", "__dict__", "__class__", "__init__" };
            foreach (RecordFieldSymbol field in type.Fields)
            {
                string candidate = IsSafeTargetIdentifier(field.Name, language, reserved) ? field.Name : BuildMappedName(field.Name, language);
                string name = MakeUnique(language is TargetLanguage.Cobol ? "FIELD-" + candidate : candidate, language is TargetLanguage.Cobol ? used : fields, language);
                fields.Add(name);
                if (language is TargetLanguage.Cobol) used.Add(name);
                _fieldNames.Add(field, name);
            }
        }
    }
}
