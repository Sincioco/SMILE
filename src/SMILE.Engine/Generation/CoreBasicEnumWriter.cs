using System.Globalization;

namespace SMILE.Engine;

internal static partial class CoreBasicCodeGenerator
{
    private sealed partial class StructuredWriter
    {
        private void WriteEnumDeclarations()
        {
            if (_program.EnumTypes.Count == 0) return;
            if (_language is TargetLanguage.Python) { Line("from enum import Enum as _SmileEnum"); Line(); }
            foreach (EnumTypeSymbol type in _program.EnumTypes)
            {
                string name = _identifiers.Get(type);
                bool wideC = _language is TargetLanguage.C && type.Members.Any(member => member.Value is < int.MinValue or > int.MaxValue);
                if (wideC)
                {
                    Line("// C17 enum values are int-sized; this enum needs signed 64-bit constants.");
                    Line($"typedef int64_t {name};");
                    foreach (EnumMemberSymbol member in type.Members) Line($"#define {_identifiers.Get(member)} {IntegerLiteral(member.Value)}");
                    Line();
                    continue;
                }
                Line(_language switch
                {
                    TargetLanguage.CSharp => $"private enum {name} : long {{",
                    TargetLanguage.C or TargetLanguage.ObjectiveC => _language is TargetLanguage.C ? "typedef enum {" : "typedef enum : int64_t {",
                    TargetLanguage.Cpp => $"enum class {name} : std::int64_t {{",
                    TargetLanguage.Java => $"private enum {name} {{",
                    TargetLanguage.JavaScript => $"const {name} = Object.freeze({{",
                    TargetLanguage.Swift => $"enum {name}: Int64 {{",
                    _ => $"class {name}(_SmileEnum):"
                });
                _indent++;
                bool canonicalOnly = _language is TargetLanguage.Java or TargetLanguage.Swift;
                EnumMemberSymbol[] canonical = type.Members.DistinctBy(member => member.Value).ToArray();
                EnumMemberSymbol[] emitted = canonicalOnly ? canonical : type.Members.ToArray();
                foreach (EnumMemberSymbol member in emitted)
                {
                    string memberName = _identifiers.Get(member);
                    string value = IntegerLiteral(member.Value);
                    Line(_language switch
                    {
                        TargetLanguage.Java => $"{memberName}, // {member.Value.ToString(CultureInfo.InvariantCulture)}",
                        TargetLanguage.Swift => $"case {memberName} = {value}",
                        TargetLanguage.JavaScript => $"{memberName}: {value},",
                        TargetLanguage.Python => $"{memberName} = {value}",
                        _ => $"{memberName} = {value},"
                    });
                }
                if (type.Members.All(member => member.Value != 0) && _language is TargetLanguage.Java or TargetLanguage.Swift or TargetLanguage.Python)
                    Line(_language switch { TargetLanguage.Java => "_smileDefault, // unnamed zero-initialized value", TargetLanguage.Swift => "case _smileDefault = 0 // unnamed zero-initialized value", _ => "_smileDefault = 0  # unnamed zero-initialized value" });
                if (_language is TargetLanguage.Java) Line(";");
                if (canonicalOnly)
                    foreach (EnumMemberSymbol member in type.Members.Except(canonical))
                    {
                        string alias = _identifiers.Get(member);
                        string original = _identifiers.Get(canonical.First(item => item.Value == member.Value));
                        Line(_language is TargetLanguage.Java ? $"public static final {name} {alias} = {original};" : $"static let {alias} = {name}.{original}");
                    }
                _indent--;
                if (_language is not TargetLanguage.Python)
                    Line(_language switch
                    {
                        TargetLanguage.C or TargetLanguage.ObjectiveC => $"}} {name};",
                        TargetLanguage.Cpp => "};", TargetLanguage.JavaScript => "});", _ => "}"
                    });
                Line();
            }
        }

        private string EnumLiteral(EnumTypeSymbol type, long value, EnumMemberSymbol? authored = null)
        {
            EnumMemberSymbol? member = authored ?? type.Members.FirstOrDefault(item => item.Value == value);
            string name = _identifiers.Get(type);
            if (member is null) return _language switch
            {
                TargetLanguage.CSharp => $"({name})0", TargetLanguage.Cpp => $"static_cast<{name}>(0)",
                TargetLanguage.Java or TargetLanguage.Swift or TargetLanguage.Python => name + "._smileDefault",
                _ => IntegerLiteral(0)
            };
            return _language switch
            {
                TargetLanguage.C or TargetLanguage.ObjectiveC => _identifiers.Get(member),
                TargetLanguage.Cpp => name + "::" + _identifiers.Get(member),
                _ => name + "." + _identifiers.Get(member)
            };
        }

        private void WriteJavaEnumArrayInitializers(IEnumerable<VariableSymbol> variables)
        {
            if (_language is not TargetLanguage.Java) return;
            foreach (VariableSymbol variable in variables.Where(item => item.IsArray && item.Type is EnumTypeSymbol))
            {
                string name = Name(variable);
                string value = DefaultLiteral(variable.Type);
                if (variable.ArrayRank == 1) Line($"java.util.Arrays.fill({name}, {value});");
                else Line($"for ({TypeName(variable.Type)}[] _smileEnumRow : {name}) java.util.Arrays.fill(_smileEnumRow, {value});");
            }
        }
    }
}
