namespace SMILE.Engine;

internal static partial class CoreBasicCodeGenerator
{
    private sealed partial class StructuredWriter
    {
        private static bool CRecordNeedsConstructor(RecordTypeSymbol type) => type.Fields.Any(field =>
            field.Type == SmileType.String && field.IsArray || field.Type is RecordTypeSymbol nested &&
                (field.IsArray && nested.ContainsText || CRecordNeedsConstructor(nested)));

        private string CRecordInitializer(RecordTypeSymbol type)
        {
            string[] fields = type.Fields.Where(field => field.Type == SmileType.String || field.Type is RecordTypeSymbol { ContainsText: true })
                .Select(field => "." + _identifiers.Get(field) + " = " + (field.Type is RecordTypeSymbol nested ? CRecordInitializer(nested) : "\"\""))
                .ToArray();
            return fields.Length == 0 ? "{0}" : "{ " + string.Join(", ", fields) + " }";
        }

        private void WriteRecordTextFields(RecordTypeSymbol type, string receiver, Action<string> write)
        {
            foreach (InstanceFieldSymbol field in type.Fields.Where(field => field.Type == SmileType.String || field.Type is RecordTypeSymbol { ContainsText: true }))
                WriteFieldElements(field, $"({receiver}).{_identifiers.Get(field)}", target =>
                {
                    if (field.Type is RecordTypeSymbol nested) WriteRecordTextFields(nested, target, write);
                    else write(target);
                });
        }

        private void WriteCRecordDefault(RecordTypeSymbol type)
        {
            string name = _identifiers.Get(type);
            Line($"static {name} {name}_default(void) {{");
            _indent++;
            Line($"{name} value = {{0}};");
            WriteRecordTextFields(type, "value", target => Line($"{target} = \"\";"));
            Line("return value;");
            _indent--;
            Line("}");
            Line();
        }

        private void WriteCRecordInitializers(IEnumerable<VariableSymbol> variables)
        {
            if (_language is not (TargetLanguage.C or TargetLanguage.ObjectiveC)) return;
            foreach (VariableSymbol variable in variables.Where(variable => variable.Type is RecordTypeSymbol record && (variable.IsArray || variable.IsGlobal && CRecordNeedsConstructor(record))))
            {
                var shape = new InstanceFieldSymbol((RecordTypeSymbol)variable.Type, variable.Name, variable.Type, variable.ArrayDimensions, 0, variable.DeclarationSpan);
                WriteFieldElements(shape, Name(variable), target => Line($"{target} = {DefaultLiteral(variable.Type)};"));
            }
        }

        private void WriteCValueRoots(SmileType type, string target, bool register)
        {
            string operation = register ? "register" : "unregister";
            if (type is ClassTypeSymbol) { Line($"smile_object_{operation}(&{target});"); return; }
            if (!UsesManagedCText) return;
            if (type is RecordTypeSymbol record) WriteRecordTextFields(record, target, field => Line($"smile_text_{operation}(&{field});"));
            else if (type == SmileType.String) Line($"smile_text_{operation}(&{target});");
        }

        private void WriteCRecordRoots(IEnumerable<VariableSymbol> variables, bool register)
        {
            foreach (VariableSymbol variable in variables.Where(variable => !variable.IsByRef && variable.Type is RecordTypeSymbol { ContainsText: true }))
            {
                var shape = new InstanceFieldSymbol((RecordTypeSymbol)variable.Type, variable.Name, variable.Type, variable.ArrayDimensions, 0, variable.DeclarationSpan);
                WriteFieldElements(shape, Name(variable), target => WriteCValueRoots(variable.Type, target, register));
            }
        }
    }
}
