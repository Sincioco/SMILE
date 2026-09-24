namespace SMILE.Engine;

internal static partial class CoreBasicCodeGenerator
{
    private sealed partial class StructuredWriter
    {
        private readonly HashSet<VariableSymbol> _javaFieldReferenceParameters = new();

        private void FindJavaFieldReferences(IEnumerable<ReferenceCall> calls)
        {
            if (_language is not TargetLanguage.Java) return;
            bool changed;
            do
            {
                changed = false;
                foreach (ReferenceCall call in calls)
                    for (int index = 0; index < call.Arguments.Count; index++)
                    {
                        VariableSymbol parameter = RoutineArguments.ParameterAtSourceIndex(call.Routine, call.Order, index);
                        BoundExpression argument = call.Arguments[index];
                        if (parameter.IsByRef && parameter.Type is not RecordTypeSymbol &&
                            (argument is BoundFieldExpression || argument is BoundVariableExpression variable && _javaFieldReferenceParameters.Contains(variable.Variable)))
                            changed |= _javaFieldReferenceParameters.Add(parameter);
                    }
            } while (changed);
        }

        private string JavaReferenceType(SmileType type) => type == SmileType.Integer ? "java.lang.Long" : type == SmileType.Double ? "java.lang.Double" : type == SmileType.Boolean ? "java.lang.Boolean" : TypeName(type);

        private string PrepareJavaFieldReference(BoundExpression argument)
        {
            if (argument is BoundVariableExpression variable && _javaFieldReferenceParameters.Contains(variable.Variable)) return StorageName(variable.Variable);
            string target = PrepareRecordLocation(argument);
            string reference = $"_smileReference{++_orderedTempId}";
            Line($"SmileReference<{JavaReferenceType(argument.Type)}> {reference} = new SmileReference<>(() -> {target}, _smileAssigned -> {target} = _smileAssigned);");
            return reference;
        }

        private void WriteJavaFieldReferenceHelper()
        {
            if (_javaFieldReferenceParameters.Count == 0) return;
            Lines(
                "// Java cannot pass an ordinary field by reference; retain its readable and writable location.",
                "private record SmileReference<Value>(java.util.function.Supplier<Value> read, java.util.function.Consumer<Value> write) {",
                "    Value get() { return read.get(); }",
                "    void set(Value value) { write.accept(value); }",
                "}");
            Line();
        }

        private string PrepareDynamicFieldReference(BoundFieldExpression field)
        {
            string receiver = PrepareRecordLocation(field.Receiver);
            string name = _identifiers.Get(field.Field);
            IReadOnlyList<string> indices = PrepareFieldIndices(field.Field.Name, field.Field.Dimensions, field.Indices);
            if (indices.Count == 0)
                return $"{(_language is TargetLanguage.Python ? "vars(" + receiver + ")" : receiver)}, {StringLiteral(name)}";
            string storage = $"({receiver}).{name}" + string.Concat(indices.Take(indices.Count - 1).Select(index => $"[{index}]"));
            return $"{storage}, {indices[^1]}";
        }

        private string ValueAssignment(string target, string value)
        {
            if (_language is not TargetLanguage.Java || !target.EndsWith(".get()", StringComparison.Ordinal)) return $"{target} = {value}";
            string name = target[..^6];
            // Java boxes an int literal as Integer, even when the writable location holds Long.
            if (_javaFieldReferenceParameters.Any(parameter => StorageName(parameter) == name && parameter.Type == SmileType.Integer))
                value = $"(long)({value})";
            return name + $".set({value})";
        }
    }
}
