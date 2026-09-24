namespace SMILE.Engine;

internal static partial class CoreBasicCodeGenerator
{
    private sealed partial class StructuredWriter
    {
        private void WriteCppMemberForwardDeclarations()
        {
            var positions = _program.InstanceTypes.Select((type, index) => (type, index)).ToDictionary(item => item.type, item => item.index);
            InstanceTypeSymbol[] forward = _program.Routines.Where(routine => routine.Symbol.Owner is not null)
                .SelectMany(routine => routine.Symbol.Parameters.Select(parameter => parameter.Type).Append(routine.Symbol.ReturnType!)
                    .OfType<InstanceTypeSymbol>().Where(type => positions[type] > positions[routine.Symbol.Owner!])).Concat(_program.ClassTypes).Distinct().ToArray();
            foreach (InstanceTypeSymbol type in forward) Line($"struct {_identifiers.Get(type)};");
            if (forward.Length > 0) Line();
        }

        private bool HasNativeMembers => _language is TargetLanguage.CSharp or TargetLanguage.Cpp or TargetLanguage.Java or
            TargetLanguage.JavaScript or TargetLanguage.Python or TargetLanguage.Swift;

        private bool HasImplicitReceiver(RoutineSymbol routine) => routine.Receiver is not null && HasNativeMembers &&
            !(_language is TargetLanguage.Swift && _swiftReferenceParameters.Contains(routine.Receiver));

        private InstancePropertySymbol PropertyFor(RoutineSymbol routine) => routine.Owner!.Properties.Single(property => property.Getter == routine || property.Setter == routine);

        private bool HasNativeProperty(RoutineSymbol routine)
        {
            if (routine.Owner is null || routine.MemberKind is InstanceMemberRoutineKind.Method) return false;
            InstancePropertySymbol property = PropertyFor(routine);
            return _language switch
            {
                TargetLanguage.CSharp or TargetLanguage.Python => true,
                TargetLanguage.JavaScript => !_asyncJavaScriptRoutines.Contains(property.Getter!) && !_asyncJavaScriptRoutines.Contains(property.Setter!),
                TargetLanguage.Swift => property.Getter is not null && HasImplicitReceiver(property.Getter) && (property.Setter is null || HasImplicitReceiver(property.Setter)),
                _ => false
            };
        }

        private void WriteRecordMembers(InstanceTypeSymbol type)
        {
            if (!HasNativeMembers) return;
            foreach (BoundRoutineDeclaration routine in _program.Routines.Where(item => item.Symbol.Owner == type && item.Symbol.MemberKind is InstanceMemberRoutineKind.Method))
                WriteMember(routine);
            foreach (InstancePropertySymbol property in type.Properties)
            {
                bool native = HasNativeProperty(property.Getter ?? property.Setter!);
                bool wrapper = native && _language is TargetLanguage.CSharp or TargetLanguage.Swift;
                if (wrapper)
                {
                    string access = property.IsPrivate ? "private " : _language is TargetLanguage.CSharp ? "public " : "";
                    Line(_language is TargetLanguage.CSharp ? $"{access}{TypeName(property.Type)} {_identifiers.Get(property)} {{" : $"{access}var {_identifiers.Get(property)}: {TypeName(property.Type)} {{");
                    _indent++;
                }
                foreach (RoutineSymbol accessor in new[] { property.Getter, property.Setter }.OfType<RoutineSymbol>())
                    WriteMember(_program.Routines.Single(item => item.Symbol == accessor));
                if (wrapper) { _indent--; Line("}"); }
                if (native && _language is TargetLanguage.Python && property.Getter is null)
                    Line($"{_identifiers.Get(property)} = property(fset={RoutineName(property.Setter!)})");
            }
        }

        private void WriteMember(BoundRoutineDeclaration routine)
        {
            Line();
            if (_language is TargetLanguage.Cpp)
            {
                Line(routine.Symbol.IsPrivate ? "private:" : "public:");
                Line(routine.Symbol.IsConstructor && !CppConstructorNeedsFactory((ClassTypeSymbol)routine.Symbol.Owner!)
                    ? $"{_identifiers.Get(routine.Symbol.Owner!)}({ParameterList(routine.Symbol)});"
                    : $"{RoutineReturnType(routine.Symbol)} {RoutineName(routine.Symbol)}({ParameterList(routine.Symbol)});");
            }
            else WriteRoutine(routine);
        }

        private void WriteRoutineHeader(RoutineSymbol symbol, string parameters)
        {
            bool member = symbol.Owner is not null && HasNativeMembers;
            bool property = HasNativeProperty(symbol);
            string name = property ? _identifiers.Get(PropertyFor(symbol)) : RoutineName(symbol);
            string access = symbol.IsPrivate || !member ? "private" : "public";
            if (symbol.IsConstructor && HasNativeMembers)
            {
                WriteClassConstructorHeader(symbol, parameters);
                return;
            }
            switch (_language)
            {
                case TargetLanguage.CSharp:
                    Line(property ? symbol.MemberKind is InstanceMemberRoutineKind.PropertyGet ? "get" : "set"
                        : $"{access}{(member ? "" : " static")} {RoutineReturnType(symbol)} {name}({parameters})");
                    Line("{");
                    break;
                case TargetLanguage.C or TargetLanguage.ObjectiveC or TargetLanguage.Cpp:
                    Line(member ? $"{RoutineReturnType(symbol)} {_identifiers.Get(symbol.Owner!)}::{name}({parameters})" : $"static {RoutineReturnType(symbol)} {name}({parameters})");
                    Line("{");
                    break;
                case TargetLanguage.Java:
                    Line($"{access}{(member ? "" : " static")} {RoutineReturnType(symbol)} {name}({parameters}) {{");
                    break;
                case TargetLanguage.JavaScript:
                    string prefix = property ? symbol.MemberKind is InstanceMemberRoutineKind.PropertyGet ? "get " : "set " : _asyncJavaScriptRoutines.Contains(symbol) ? "async " : "";
                    Line($"{prefix}{(member ? "" : "function ")}{name}({parameters}) {{");
                    break;
                case TargetLanguage.Swift:
                    if (property) Line(symbol.MemberKind is InstanceMemberRoutineKind.PropertyGet ? symbol.Owner is ClassTypeSymbol ? "get {" : "mutating get {" : "set {");
                    else Line($"{(member ? symbol.IsPrivate ? "private " : "" : "")}{(member ? HasImplicitReceiver(symbol) ? symbol.Owner is ClassTypeSymbol ? "" : "mutating " : "static " : "")}func {name}({parameters}){(symbol.IsFunction ? " -> " + RoutineReturnType(symbol) : "")} {{");
                    break;
                case TargetLanguage.Python:
                    if (property && symbol.MemberKind is InstanceMemberRoutineKind.PropertyGet) Line("@property");
                    else if (property && PropertyFor(symbol).Getter is not null) Line($"@{name}.setter");
                    else if (property) name = RoutineName(symbol);
                    Line($"def {name}({(member ? "self" + (parameters.Length > 0 ? ", " : "") : "")}{parameters}):");
                    break;
            }
        }

        private string PrepareMemberReceiver(BoundExpression expression)
        {
            string location = PrepareRecordLocation(expression);
            if (_language is TargetLanguage.Swift) return location;
            string name = $"_smileReceiver{++_orderedTempId}";
            if (_language is TargetLanguage.CSharp) Line($"ref {TypeName(expression.Type)} {name} = ref {location};");
            else if (_language is TargetLanguage.Cpp) Line($"{TypeName(expression.Type)}& {name} = {location};");
            else return NewOrderedValue(expression.Type, location);
            return name;
        }

        private string CallText(RoutineSymbol routine, IReadOnlyList<string> arguments)
        {
            string awaitPrefix = _language is TargetLanguage.JavaScript && _asyncJavaScriptRoutines.Contains(routine) ? "await " : "";
            if (routine.Owner is null || !HasNativeMembers) return $"{awaitPrefix}{RoutineName(routine)}({string.Join(", ", arguments)})";
            if (HasNativeProperty(routine))
            {
                string property = $"{MemberAccessReceiver(routine.Owner, arguments[0])}{_identifiers.Get(PropertyFor(routine))}";
                return routine.MemberKind is InstanceMemberRoutineKind.PropertyGet ? property : $"{property} = {arguments[1]}";
            }
            return HasImplicitReceiver(routine) ? $"{awaitPrefix}{MemberAccessReceiver(routine.Owner, arguments[0])}{RoutineName(routine)}({string.Join(", ", arguments.Skip(1))})"
                : $"{_identifiers.Get(routine.Owner)}.{RoutineName(routine)}({string.Join(", ", arguments)})";
        }
    }
}
