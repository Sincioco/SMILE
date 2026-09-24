namespace SMILE.Engine;

internal static partial class CoreBasicCodeGenerator
{
    private sealed partial class StructuredWriter
    {
        private bool UsesArrayReferences => _language is TargetLanguage.Java or TargetLanguage.JavaScript or TargetLanguage.Python;
        private bool NeedsReferenceBox(VariableSymbol variable) => UsesArrayReferences && !variable.IsArray &&
            !variable.IsByRef && variable.Type is not RecordTypeSymbol && _addressedVariables.Contains(variable);
        private string StorageName(VariableSymbol variable) => _identifiers.Get(variable);
        private static string ReferenceIndexName(VariableSymbol variable) => $"_smileRefIndex{variable.DeclarationSpan.Start}";

        private string ReferenceValueName(VariableSymbol variable)
        {
            if (variable.IsReceiver && HasNativeMembers && !(_language is TargetLanguage.Swift && _swiftReferenceParameters.Contains(variable)))
                return _language is TargetLanguage.Python or TargetLanguage.Swift ? "self" : _language is TargetLanguage.Cpp ? "(*this)" : "this";
            if (variable.IsSetterValue && _language is TargetLanguage.CSharp) return "value";
            string name = StorageName(variable);
            if (NeedsReferenceBox(variable)) return $"{name}[0]";
            if (!variable.IsByRef) return name;
            if (UsesArrayReferences && variable.Type is RecordTypeSymbol) return name;
            if (_language is TargetLanguage.Java && _javaFieldReferenceParameters.Contains(variable)) return name + ".get()";
            if (UsesArrayReferences) return $"{name}[{ReferenceIndexName(variable)}]";
            if (_language is TargetLanguage.C or TargetLanguage.ObjectiveC) return $"(*{name})";
            return _swiftReferenceParameters.Contains(variable) ? name + ".value" : name;
        }

        private string ReferenceBoxDeclaration(VariableSymbol variable, string value) => _language switch
        {
            TargetLanguage.Java => $"{TypeName(variable.Type)}[] {StorageName(variable)} = {{{value}}};",
            TargetLanguage.JavaScript => $"const {StorageName(variable)} = [{value}];",
            _ => $"{StorageName(variable)} = [{value}]"
        };

        private void WriteBoxedParameters(RoutineSymbol routine)
        {
            for (int index = 0; index < routine.ExecutionParameters.Count; index++)
                if (NeedsReferenceBox(routine.ExecutionParameters[index]))
                    Line(ReferenceBoxDeclaration(routine.ExecutionParameters[index], $"_smileParameter{index + 1}"));
        }

        private string ReferenceParameter(VariableSymbol parameter)
        {
            string name = StorageName(parameter);
            string type = TypeName(parameter.Type);
            if (UsesArrayReferences && parameter.Type is RecordTypeSymbol) return _language is TargetLanguage.Java ? type + " " + name : name;
            if (_language is TargetLanguage.Java && _javaFieldReferenceParameters.Contains(parameter)) return $"SmileReference<{JavaReferenceType(parameter.Type)}> {name}";
            return _language switch
            {
                TargetLanguage.CSharp => $"ref {type} {name}",
                TargetLanguage.C or TargetLanguage.ObjectiveC => $"{type}* {name}",
                TargetLanguage.Cpp => $"{type}& {name}",
                TargetLanguage.Java => $"{type}[] {name}, int {ReferenceIndexName(parameter)}",
                TargetLanguage.Swift when _swiftReferenceParameters.Contains(parameter) => $"_ {name}: SmileReference<{type}>",
                TargetLanguage.Swift => $"_ {name}: inout {type}",
                _ => $"{name}, {ReferenceIndexName(parameter)}"
            };
        }

        private string PrepareReferenceArgument(BoundExpression argument, VariableSymbol parameter)
        {
            if (UsesArrayReferences && argument.Type is RecordTypeSymbol)
                return NewOrderedValue(argument.Type, PrepareRecordLocation(argument));
            if (_language is TargetLanguage.Java && _javaFieldReferenceParameters.Contains(parameter))
                return PrepareJavaFieldReference(argument);
            if (argument is BoundFieldExpression dynamicField && UsesArrayReferences)
                return PrepareDynamicFieldReference(dynamicField);
            if (argument is BoundFieldExpression or BoundWithReceiverExpression && _language is TargetLanguage.CSharp or TargetLanguage.C or TargetLanguage.ObjectiveC or TargetLanguage.Cpp)
            {
                string fieldTarget = PrepareRecordLocation(argument);
                string capturedField = $"_smileReference{++_orderedTempId}";
                if (_language is TargetLanguage.CSharp)
                {
                    Line($"ref {TypeName(argument.Type)} {capturedField} = ref {fieldTarget};");
                    return "ref " + capturedField;
                }
                Line($"{TypeName(argument.Type)}* {capturedField} = &{fieldTarget};");
                return _language is TargetLanguage.Cpp ? "*" + capturedField : capturedField;
            }
            VariableSymbol owner = LocationOwner(argument);
            var indices = new List<string>();
            if (argument is BoundArrayExpression array)
            {
                // Check each dimension before evaluating the next index or argument.
                for (int dimension = 0; dimension < array.Indices.Count; dimension++)
                {
                    string value = LowerOrderedCExpression(array.Indices[dimension]);
                    string index = $"_smileIndex{++_orderedTempId}";
                    WriteIndexTemporary(index, CheckedArrayIndex(owner, value, dimension));
                    indices.Add(index);
                }
            }
            string target = argument is BoundFieldExpression or BoundWithReceiverExpression ? PrepareRecordLocation(argument)
                : indices.Count == 0 ? Name(owner) : ArrayTarget(owner, indices);
            if (UsesArrayReferences)
            {
                string storage = indices.Count > 1 ? $"{StorageName(owner)}[{indices[0]}]" : StorageName(owner);
                string index = indices.Count > 0 ? indices[^1] : owner.IsByRef ? ReferenceIndexName(owner) : "0";
                return $"{storage}, {index}";
            }
            if (_language is TargetLanguage.Swift)
            {
                if (!_swiftReferenceParameters.Contains(parameter)) return "&" + target;
                if (argument is BoundVariableExpression && owner.IsByRef && _swiftReferenceParameters.Contains(owner)) return StorageName(owner);
                string reference = $"_smileReference{++_orderedTempId}";
                Line($"let {reference} = SmileReference<{TypeName(argument.Type)}>(read: {{ {target} }}, write: {{ {target} = $0 }})");
                return reference;
            }
            if (indices.Count == 0) return _language switch
            {
                TargetLanguage.CSharp => "ref " + target,
                TargetLanguage.Cpp => target,
                _ => owner.IsByRef ? StorageName(owner) : "&" + target
            };
            string captured = $"_smileReference{++_orderedTempId}";
            if (_language is TargetLanguage.CSharp)
            {
                Line($"ref {TypeName(argument.Type)} {captured} = ref {target};");
                return "ref " + captured;
            }
            Line($"{TypeName(argument.Type)}* {captured} = &{target};");
            return _language is TargetLanguage.Cpp ? "*" + captured : captured;
        }

        private void WriteReferenceHelpers()
        {
            if (_language is TargetLanguage.Java) WriteJavaFieldReferenceHelper();
            if (_language is not TargetLanguage.Swift || _swiftReferenceParameters.Count == 0) return;
            Lines(
                "// Shared aliases need a captured location; Swift inout requires exclusive access.",
                "final class SmileReference<Value> {",
                "    private let read: () -> Value",
                "    private let write: (Value) -> Void",
                "    init(read: @escaping () -> Value, write: @escaping (Value) -> Void) {",
                "        self.read = read",
                "        self.write = write",
                "    }",
                "    var value: Value {",
                "        get { read() }",
                "        set { write(newValue) }",
                "    }",
                "}");
            Line();
        }
    }
}
