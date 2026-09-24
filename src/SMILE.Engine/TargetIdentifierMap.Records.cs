namespace SMILE.Engine;

internal sealed partial class TargetIdentifierMap
{
    private readonly Dictionary<InstanceTypeSymbol, string> _recordNames = new();
    private readonly Dictionary<InstanceFieldSymbol, string> _fieldNames = new();
    private readonly Dictionary<InstancePropertySymbol, string> _propertyNames = new();
    private readonly Dictionary<InstanceTypeSymbol, ISet<string>> _recordMemberNames = new();
    public string Get(InstanceTypeSymbol type) => _recordNames[type];
    public string Get(InstanceFieldSymbol field) => _fieldNames[field];
    public string Get(InstancePropertySymbol property) => _propertyNames[property];

    private string AddMemberRoutineName(RoutineSymbol routine, TargetLanguage language, ISet<string> reserved, ISet<string> used)
    {
        bool freeFunction = language is TargetLanguage.C or TargetLanguage.ObjectiveC or TargetLanguage.Cobol or TargetLanguage.MasmX64;
        string accessor = routine.MemberKind is InstanceMemberRoutineKind.PropertyGet ? "get_" : routine.MemberKind is InstanceMemberRoutineKind.PropertySet ? "set_" : "";
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
        if (!program.InstanceTypes.Any()) return;
        reserved.UnionWith(new[] { "_smileDataclass", "_smileField", "_smileDeepcopy", "SmileReference", "java" });
        if (program.ClassTypes.Count > 0)
            reserved.UnionWith(new[] { "SmileObject", "SmileObjectRoot", "smile_objects", "smile_object_roots", "smile_object_root_count", "smile_object_return_root", "smile_object_allocations", "smile_object_frees", "smile_object_peak", "smile_object_shutdown_complete", "smile_object_memory", "smile_object_require", "smile_object_allocate", "smile_object_register", "smile_object_unregister", "smile_object_checkpoint", "smile_object_restore", "smile_object_return", "smile_object_collect", "smile_object_shutdown", "smile_object_initialize", "ArgumentNullException", "AttributeError", "TypeError" });
        if (program.InstanceTypes.Any(type => type.Methods.Count > 0 || type.Properties.Count > 0 || type is ClassTypeSymbol) && language is TargetLanguage.Python) reserved.UnionWith(new[] { "self", "property" });
        if (language is TargetLanguage.Swift && program.InstanceTypes.Any(type => type.Properties.Count > 0)) reserved.Add("newValue");
        foreach (InstanceTypeSymbol type in program.InstanceTypes)
        {
            string preferred = IsSafeTargetIdentifier(type.Name, language, reserved) ? type.Name : BuildMappedName(type.Name, language);
            string typeName = MakeUnique(preferred, used, language);
            used.Add(typeName);
            _recordNames.Add(type, typeName);
            if (language is TargetLanguage.C or TargetLanguage.ObjectiveC)
            {
                used.Add(typeName + "_default");
                if (type is ClassTypeSymbol) { used.Add(typeName + "_new"); used.Add(typeName + "_finalize"); }
            }
            var fields = new HashSet<string>(StringComparer.Ordinal) { typeName, "copy", "copyFrom", "__dict__", "__class__", "__init__", "shared_from_this", "weak_from_this" };
            _recordMemberNames.Add(type, fields);
            foreach (InstanceFieldSymbol field in type.Fields)
            {
                string candidate = IsSafeTargetIdentifier(field.Name, language, reserved) ? field.Name : BuildMappedName(field.Name, language);
                string name = MakeUnique(language is TargetLanguage.Cobol ? "FIELD-" + candidate : candidate, language is TargetLanguage.Cobol ? used : fields, language);
                fields.Add(name);
                if (language is TargetLanguage.Cobol) used.Add(name);
                _fieldNames.Add(field, language is TargetLanguage.JavaScript && field.IsPrivate ? "#" + name : name);
            }
            foreach (InstancePropertySymbol property in type.Properties)
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
