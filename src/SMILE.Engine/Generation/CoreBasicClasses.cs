namespace SMILE.Engine;

internal static partial class CoreBasicCodeGenerator
{
    private sealed partial class StructuredWriter
    {
        private string ClassTypeName(ClassTypeSymbol type) => _language switch
        {
            TargetLanguage.C or TargetLanguage.ObjectiveC => _identifiers.Get(type) + "*",
            TargetLanguage.Cpp => "std::shared_ptr<" + _identifiers.Get(type) + ">",
            TargetLanguage.Swift => _identifiers.Get(type) + "?",
            _ => _identifiers.Get(type)
        };

        private string NothingLiteral() => _language switch
        {
            TargetLanguage.C or TargetLanguage.ObjectiveC => "NULL",
            TargetLanguage.Cpp => "nullptr",
            TargetLanguage.Swift => "nil",
            TargetLanguage.Python => "None",
            _ => "null"
        };

        private string MemberAccessReceiver(InstanceTypeSymbol type, string receiver) => type is not ClassTypeSymbol
            ? $"({receiver})." : _language switch
            {
                TargetLanguage.C or TargetLanguage.ObjectiveC or TargetLanguage.Cpp => $"({receiver})->",
                TargetLanguage.Swift => $"({receiver})!.",
                _ => $"({receiver})."
            };

        private string ClassIdentity(BoundIdentityExpression identity)
        {
            string left = NewOrderedValue(identity.Left.Type, PreparedExpression(identity.Left));
            string right = NewOrderedValue(identity.Right.Type, PreparedExpression(identity.Right));
            string op = _language switch
            {
                TargetLanguage.Python => identity.Negated ? "is not" : "is",
                TargetLanguage.JavaScript or TargetLanguage.Swift => identity.Negated ? "!==" : "===",
                _ => identity.Negated ? "!=" : "=="
            };
            return $"({left} {op} {right})";
        }

        private void WriteRequireClass(string value)
        {
            switch (_language)
            {
                case TargetLanguage.CSharp: Line($"ArgumentNullException.ThrowIfNull({value});"); break;
                case TargetLanguage.Java: Line($"java.util.Objects.requireNonNull({value});"); break;
                case TargetLanguage.C or TargetLanguage.ObjectiveC: Line($"smile_object_require({value});"); break;
                case TargetLanguage.Cpp: Line($"if (!{value}) {{ std::cerr << \"Object reference is Nothing.\\n\"; std::exit(2); }}"); break;
                case TargetLanguage.JavaScript: Line($"if ({value} === null) throw new TypeError(\"Object reference is Nothing.\");"); break;
                case TargetLanguage.Python:
                    Line($"if {value} is None:"); _indent++;
                    Line("raise AttributeError(\"Object reference is Nothing.\")"); _indent--;
                    break;
                case TargetLanguage.Swift:
                    Line($"if {value} == nil {{"); _indent++;
                    Line("fflush(nil)"); Line("fatalError(\"Object reference is Nothing.\")");
                    _indent--; Line("}");
                    break;
            }
        }

        private string NewClass(BoundNewExpression creation)
        {
            IReadOnlyList<string> arguments = PrepareCallArguments(creation.Arguments, creation.ParameterOrder,
                ordered: true, routine: creation.Class.Constructor, includeReceiver: false);
            string name = _identifiers.Get(creation.Class);
            string call = $"{name}({string.Join(", ", arguments)})";
            return _language switch
            {
                TargetLanguage.JavaScript when _asyncJavaScriptRoutines.Contains(creation.Class.Constructor) => $"await {name}._smileCreate({string.Join(", ", arguments)})",
                TargetLanguage.Python or TargetLanguage.Swift => call,
                TargetLanguage.C or TargetLanguage.ObjectiveC => $"{name}_new({string.Join(", ", arguments)})",
                TargetLanguage.Cpp => CppConstructorNeedsFactory(creation.Class) ? $"{name}::_smileCreate({string.Join(", ", arguments)})"
                    : $"std::make_shared<{name}>({string.Join(", ", arguments)})",
                _ => "new " + call
            };
        }

        private void WriteClassDeclarations()
        {
            foreach (ClassTypeSymbol type in _program.ClassTypes)
            {
                string name = _identifiers.Get(type);
                Line(_language switch
                {
                    TargetLanguage.CSharp => $"private sealed class {name} {{",
                    TargetLanguage.Java => $"private static final class {name} {{",
                    TargetLanguage.Python => $"class {name}:",
                    TargetLanguage.Swift => $"final class {name} {{",
                    TargetLanguage.C or TargetLanguage.ObjectiveC => $"struct {name} {{",
                    TargetLanguage.Cpp => $"struct {name} : std::enable_shared_from_this<{name}> {{",
                    _ => $"class {name} {{"
                });
                _indent++;
                if (_language is not TargetLanguage.Python)
                    foreach (InstanceFieldSymbol field in type.Fields) WriteRecordFieldDeclaration(field);
                if (type.Fields.Count == 0 && _language is TargetLanguage.C or TargetLanguage.ObjectiveC) Line("unsigned char _smileEmpty;");
                WriteRecordMembers(type);
                if (_language is TargetLanguage.Cpp && CppConstructorNeedsFactory(type)) WriteCppClassFactory(type);
                if (_language is TargetLanguage.JavaScript && _asyncJavaScriptRoutines.Contains(type.Constructor)) WriteJavaScriptClassFactory(type);
                _indent--;
                if (_language is not TargetLanguage.Python)
                    Line(_language is TargetLanguage.C or TargetLanguage.ObjectiveC or TargetLanguage.Cpp ? "};" : "}");
                Line();
            }
        }

        private void WriteClassConstructorHeader(RoutineSymbol symbol, string parameters)
        {
            string name = _identifiers.Get(symbol.Owner!);
            Line(_language switch
            {
                TargetLanguage.CSharp or TargetLanguage.Java => $"public {name}({parameters}) {{",
                TargetLanguage.Python => $"def __init__(self{(parameters.Length == 0 ? "" : ", " + parameters)}):",
                TargetLanguage.JavaScript => _asyncJavaScriptRoutines.Contains(symbol) ? $"async {RoutineName(symbol)}({parameters}) {{" : $"constructor({parameters}) {{",
                TargetLanguage.Swift => $"init({parameters}) {{",
                _ => CppConstructorNeedsFactory((ClassTypeSymbol)symbol.Owner!)
                    ? $"void {name}::{RoutineName(symbol)}({parameters}) {{" : $"{name}::{name}({parameters}) {{"
            });
        }

        private void WriteClassConstructorFields(RoutineSymbol symbol)
        {
            if (!symbol.IsConstructor) return;
            foreach (InstanceFieldSymbol field in symbol.Owner!.Fields)
            {
                if (_language is TargetLanguage.Python)
                {
                    string value = DefaultLiteral(field.Type);
                    foreach (int dimension in field.Dimensions.Reverse()) value = $"[{value} for _ in range({dimension})]";
                    Line($"self.{_identifiers.Get(field)} = {value}");
                }
                else if (_language is TargetLanguage.CSharp or TargetLanguage.Java && field.IsArray &&
                    (field.Type is RecordTypeSymbol || field.Type == SmileType.String || _language is TargetLanguage.Java && field.Type is EnumTypeSymbol))
                    WriteFieldElements(field, "this." + _identifiers.Get(field), target => Line($"{target} = {DefaultLiteral(field.Type)};"));
            }
        }

        private bool CppConstructorNeedsFactory(ClassTypeSymbol type)
        {
            BoundExpression[] expressions = EnumerateExpressions(_program.Routines.Single(item => item.Symbol == type.Constructor).SourceItems).ToArray();
            // shared_from_this is valid only after shared_ptr owns the instance.
            // Field-only constructors remain ordinary native constructors; an
            // escaping Me needs ownership established before learner code runs.
            return expressions.Count(item => item is BoundVariableExpression { Variable.IsReceiver: true }) >
                expressions.Count(item => item is BoundFieldExpression { Receiver: BoundVariableExpression { Variable.IsReceiver: true } });
        }

        private void WriteCppClassFactory(ClassTypeSymbol type)
        {
            string name = _identifiers.Get(type);
            Line("public:");
            Line($"static std::shared_ptr<{name}> _smileCreate({ParameterList(type.Constructor)}) {{");
            _indent++;
            Line($"auto _smileInstance = std::make_shared<{name}>();");
            Line($"_smileInstance->{RoutineName(type.Constructor)}({string.Join(", ", type.Constructor.Parameters.Select(StorageName))});");
            Line("return _smileInstance;");
            _indent--;
            Line("}");
        }

        private void WriteJavaScriptClassFactory(ClassTypeSymbol type)
        {
            Line($"static async _smileCreate({ParameterList(type.Constructor)}) {{");
            _indent++;
            Line($"const _smileInstance = new {_identifiers.Get(type)}();");
            string[] arguments = type.Constructor.Parameters.SelectMany(parameter => parameter.IsByRef
                ? new[] { StorageName(parameter), ReferenceIndexName(parameter) } : new[] { StorageName(parameter) }).ToArray();
            Line($"await _smileInstance.{RoutineName(type.Constructor)}({string.Join(", ", arguments)});");
            Line("return _smileInstance;");
            _indent--;
            Line("}");
        }
    }
}
