namespace SMILE.Engine;

internal sealed partial class TargetIdentifierMap
{
    private readonly Dictionary<RecordTypeSymbol, string> _recordNames = new();
    private readonly Dictionary<RecordFieldSymbol, string> _fieldNames = new();
    private readonly Dictionary<RecordPropertySymbol, string> _propertyNames = new();
    private readonly Dictionary<RecordTypeSymbol, ISet<string>> _recordMemberNames = new();
    public string Get(RecordTypeSymbol type) => _recordNames[type];
    public string Get(RecordFieldSymbol field) => _fieldNames[field];
    public string Get(RecordPropertySymbol property) => _propertyNames[property];

    private string AddMemberRoutineName(RoutineSymbol routine, TargetLanguage language, ISet<string> reserved, ISet<string> used)
    {
        bool freeFunction = language is TargetLanguage.C or TargetLanguage.ObjectiveC or TargetLanguage.Cobol or TargetLanguage.MasmX64;
        string accessor = routine.MemberKind is RecordMemberRoutineKind.PropertyGet ? "get_" : routine.MemberKind is RecordMemberRoutineKind.PropertySet ? "set_" : "";
        string sourceName = (freeFunction ? routine.Owner!.Name + "_" : "") + accessor + routine.Name;
        if (language is TargetLanguage.Python && routine.IsPrivate) sourceName = "_" + sourceName;
        string preferred = IsSafeTargetIdentifier(sourceName, language, reserved) ? sourceName : BuildMappedName(sourceName, language);
        ISet<string> scope = freeFunction ? used : _recordMemberNames[routine.Owner!];
        // GnuCOBOL PROGRAM-ID is limited to 31 characters; member qualification
        // can exceed that even when both authored names are short.
        string name = MakeUnique(preferred, scope, language, language is TargetLanguage.Cobol ? 31 : int.MaxValue);
        scope.Add(name);
        return language is TargetLanguage.JavaScript && routine.IsPrivate ? "#" + name : name;
    }

    private void AddRecordNames(BoundProgram program, TargetLanguage language, ISet<string> reserved, ISet<string> used)
    {
        if (program.RecordTypes.Count == 0) return;
        reserved.UnionWith(new[] { "_smileDataclass", "_smileField", "_smileDeepcopy", "SmileReference", "java" });
        if (program.RecordTypes.Any(type => type.Methods.Count > 0 || type.Properties.Count > 0) && language is TargetLanguage.Python) reserved.UnionWith(new[] { "self", "property" });
        if (language is TargetLanguage.Swift && program.RecordTypes.Any(type => type.Properties.Count > 0)) reserved.Add("newValue");
        foreach (RecordTypeSymbol type in program.RecordTypes)
        {
            string preferred = IsSafeTargetIdentifier(type.Name, language, reserved) ? type.Name : BuildMappedName(type.Name, language);
            string typeName = MakeUnique(preferred, used, language);
            used.Add(typeName);
            _recordNames.Add(type, typeName);
            if (language is TargetLanguage.C or TargetLanguage.ObjectiveC) used.Add(typeName + "_default");
            var fields = new HashSet<string>(StringComparer.Ordinal) { typeName, "copy", "copyFrom", "__dict__", "__class__", "__init__" };
            _recordMemberNames.Add(type, fields);
            foreach (RecordFieldSymbol field in type.Fields)
            {
                string candidate = IsSafeTargetIdentifier(field.Name, language, reserved) ? field.Name : BuildMappedName(field.Name, language);
                string name = MakeUnique(language is TargetLanguage.Cobol ? "FIELD-" + candidate : candidate, language is TargetLanguage.Cobol ? used : fields, language);
                fields.Add(name);
                if (language is TargetLanguage.Cobol) used.Add(name);
                _fieldNames.Add(field, name);
            }
            foreach (RecordPropertySymbol property in type.Properties)
            {
                string sourceName = language is TargetLanguage.Python && property.IsPrivate ? "_" + property.Name : property.Name;
                string candidate = IsSafeTargetIdentifier(sourceName, language, reserved) ? sourceName : BuildMappedName(sourceName, language);
                string name = MakeUnique(candidate, fields, language);
                fields.Add(name);
                _propertyNames.Add(property, language is TargetLanguage.JavaScript && property.IsPrivate ? "#" + name : name);
            }
        }
    }
}
