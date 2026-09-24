namespace SMILE.Engine;

internal static partial class CoreBasicCodeGenerator
{
    private sealed partial class StructuredWriter
    {
        private static bool RecordHasArrays(RecordTypeSymbol type) => type.Fields.Any(field => field.IsArray || field.Type is RecordTypeSymbol nested && RecordHasArrays(nested));
        private bool RecordNeedsCopy(RecordTypeSymbol type) => _language is TargetLanguage.Java or TargetLanguage.JavaScript or TargetLanguage.Python || _language is TargetLanguage.CSharp && RecordHasArrays(type);
        private string RecordCopy(SmileType type, string value) => type is RecordTypeSymbol record && RecordNeedsCopy(record) ? $"({value}).copy()" : value;

        private void WriteRecordDeclarations()
        {
            if (_language is TargetLanguage.Cpp) WriteCppMemberForwardDeclarations();
            if (_program.RecordTypes.Count == 0) return;
            if (_language is TargetLanguage.Python)
            {
                Line("from dataclasses import dataclass as _smileDataclass, field as _smileField");
                Line("from copy import deepcopy as _smileDeepcopy");
                Line();
            }
            foreach (RecordTypeSymbol type in _program.RecordTypes)
            {
                string name = _identifiers.Get(type);
                if (_language is TargetLanguage.Python) Line("@_smileDataclass");
                Line(_language switch
                {
                    TargetLanguage.CSharp => $"private struct {name} {{",
                    TargetLanguage.C or TargetLanguage.ObjectiveC => $"typedef struct {name} {{",
                    TargetLanguage.Java => $"private static final class {name} {{",
                    TargetLanguage.JavaScript => $"class {name} {{",
                    TargetLanguage.Python => $"class {name}:",
                    _ => $"struct {name} {{"
                });
                _indent++;
                foreach (InstanceFieldSymbol field in type.Fields) WriteRecordFieldDeclaration(field);
                if (type.Fields.Count == 0 && _language is TargetLanguage.C or TargetLanguage.ObjectiveC) Line("unsigned char _smileEmpty;");
                if (_language is TargetLanguage.CSharp or TargetLanguage.Java)
                {
                    Line($"public {name}() {{");
                    _indent++;
                    foreach (InstanceFieldSymbol field in type.Fields.Where(field => field.IsArray && (field.Type is RecordTypeSymbol || field.Type == SmileType.String || _language is TargetLanguage.Java && field.Type is EnumTypeSymbol)))
                        WriteFieldElements(field, _identifiers.Get(field), target => Line($"{target} = {DefaultLiteral(field.Type)};"));
                    _indent--;
                    Line("}");
                }
                if (RecordNeedsCopy(type)) WriteRecordCopyMethods(type);
                WriteRecordMembers(type);
                _indent--;
                if (_language is not TargetLanguage.Python)
                    Line(_language switch { TargetLanguage.C or TargetLanguage.ObjectiveC => $"}} {name};", TargetLanguage.Cpp => "};", _ => "}" });
                Line();
                if (_language is TargetLanguage.C or TargetLanguage.ObjectiveC && CRecordNeedsConstructor(type)) WriteCRecordDefault(type);
            }
        }

        private void WriteRecordFieldDeclaration(InstanceFieldSymbol field)
        {
            string name = _identifiers.Get(field);
            string type = TypeName(field.Type);
            string value = DefaultLiteral(field.Type);
            string size = string.Join("][", field.Dimensions);
            if (_language is TargetLanguage.C or TargetLanguage.ObjectiveC)
            {
                Line($"{type} {name}{(field.IsArray ? "[" + size + "]" : "")};");
                return;
            }
            if (_language is TargetLanguage.Cpp)
            {
                if (field.Owner is ClassTypeSymbol) Line(field.IsPrivate ? "private:" : "public:");
                foreach (int dimension in field.Dimensions.Reverse()) type = $"std::array<{type}, {dimension}>";
                Line($"{type} {name}{{}};");
                return;
            }
            if (_language is TargetLanguage.CSharp or TargetLanguage.Java)
            {
                string access = field.IsPrivate ? "private" : "public";
                if (field.IsArray)
                {
                    string suffix = field.Dimensions.Count == 2 ? (_language is TargetLanguage.CSharp ? "[,]" : "[][]") : "[]";
                    string dimensions = _language is TargetLanguage.CSharp ? string.Join(", ", field.Dimensions) : size;
                    Line($"{access} {type}{suffix} {name} = new {type}[{dimensions}];");
                }
                else Line($"{access} {type} {name} = {value};");
                return;
            }
            if (_language is TargetLanguage.JavaScript)
            {
                foreach (int dimension in field.Dimensions.Reverse()) value = $"Array.from({{ length: {dimension} }}, () => {value})";
                Line($"{name} = {value};");
                return;
            }
            if (_language is TargetLanguage.Swift)
            {
                foreach (int dimension in field.Dimensions.Reverse()) { type = $"[{type}]"; value = $"Array(repeating: {value}, count: {dimension})"; }
                Line($"{(field.IsPrivate ? "private " : "")}var {name}: {type} = {value}");
                return;
            }
            string pythonType = field.Type is RecordTypeSymbol record ? _identifiers.Get(record)
                : field.Type is EnumTypeSymbol enumeration ? _identifiers.Get(enumeration)
                : field.Type == SmileType.String ? "str" : field.Type == SmileType.Boolean ? "bool" : field.Type == SmileType.Double ? "float" : "int";
            foreach (int dimension in field.Dimensions.Reverse()) { pythonType = $"list[{pythonType}]"; value = $"[{value} for _ in range({dimension})]"; }
            if (field.IsArray || field.Type is RecordTypeSymbol) value = $"_smileField(default_factory=lambda: {value})";
            Line($"{name}: {pythonType} = {value}");
        }

        private void WriteFieldElements(InstanceFieldSymbol field, string target, Action<string> write, int dimension = 0)
        {
            if (dimension == field.Dimensions.Count) { write(target); return; }
            string index = $"_smileFieldIndex{++_orderedTempId}";
            Line(_language is TargetLanguage.Python ? $"for {index} in range({field.Dimensions[dimension]}):"
                : $"for ({(_language is TargetLanguage.JavaScript ? "let" : "int")} {index} = 0; {index} < {field.Dimensions[dimension]}; {index}++) {{");
            _indent++;
            string indexed = _language is TargetLanguage.CSharp && field.Dimensions.Count == 2
                ? dimension == 0 ? target + "[" + index : target + ", " + index + "]"
                : target + "[" + index + "]";
            WriteFieldElements(field, indexed, write, dimension + 1);
            _indent--;
            if (_language is not TargetLanguage.Python) Line("}");
        }

        private void WriteRecordCopyMethods(RecordTypeSymbol type)
        {
            string name = _identifiers.Get(type);
            Line(_language switch { TargetLanguage.Python => "def copyFrom(self, source):", TargetLanguage.JavaScript => "copyFrom(source) {", _ => $"public void copyFrom({name} source) {{" });
            _indent++;
            if (type.Fields.Count == 0 && _language is TargetLanguage.Python) Line("pass");
            foreach (InstanceFieldSymbol field in type.Fields)
            {
                string fieldName = _identifiers.Get(field);
                string receiver = _language is TargetLanguage.Python ? "self." : "this.";
                WriteFieldElements(field, receiver + fieldName, target =>
                {
                    string source = "source." + target[receiver.Length..];
                    string statement = field.Type is RecordTypeSymbol nested && RecordNeedsCopy(nested)
                        ? $"{target}.copyFrom({source})" : $"{target} = {source}";
                    Line(statement + (_language is TargetLanguage.Python ? "" : ";"));
                });
            }
            _indent--;
            if (_language is not TargetLanguage.Python) Line("}");
            Line(_language switch { TargetLanguage.Python => "def copy(self):", TargetLanguage.JavaScript => "copy() {", _ => $"public {name} copy() {{" });
            _indent++;
            if (_language is TargetLanguage.Python) Line("return _smileDeepcopy(self)");
            else
            {
                Line($"{(_language is TargetLanguage.JavaScript ? "const" : name)} result = new {name}();");
                Line("result.copyFrom(this);");
                Line("return result;");
            }
            _indent--;
            if (_language is not TargetLanguage.Python) Line("}");
        }
    }
}
