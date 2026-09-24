namespace SMILE.Engine;

internal static partial class CoreBasicCodeGenerator
{
    private sealed partial class StructuredWriter
    {
        private readonly Dictionary<WithLocationSymbol, string> _withLocations = new();

        private void WriteWith(BoundWithStatement block)
        {
            string target = PrepareRecordLocation(block.Location.Target);
            string name = $"_smileWith{++_orderedTempId}";
            string type = TypeName(block.Location.Target.Type);
            switch (_language)
            {
                case TargetLanguage.CSharp: Line($"ref {type} {name} = ref {target};"); break;
                case TargetLanguage.C or TargetLanguage.ObjectiveC: Line($"{type}* {name} = &{target};"); break;
                case TargetLanguage.Cpp: Line($"{type}& {name} = {target};"); break;
                case TargetLanguage.Java: Line($"{type} {name} = {target};"); break;
                case TargetLanguage.JavaScript: Line($"const {name} = {target};"); break;
                case TargetLanguage.Python: Line($"{name} = {target}"); break;
                // Swift value storage has no local reference alias. The existing
                // location captures indexes once and retains the original owner.
                case TargetLanguage.Swift: name = target; break;
            }
            _withLocations[block.Location] = _language is TargetLanguage.C or TargetLanguage.ObjectiveC ? $"(*{name})" : name;
            // Index expressions may allocate Text. Their temporary roots are no
            // longer needed after capture, including if the body returns early.
            EndManagedTextStatement();
            BeginManagedTextStatement();
            WriteItems(block.SourceItems);
        }

        private string RecordDefault(RecordTypeSymbol type)
        {
            string name = _identifiers.Get(type);
            return _language switch
            {
                TargetLanguage.C or TargetLanguage.ObjectiveC => CRecordNeedsConstructor(type) ? name + "_default()" : "(" + name + ")" + CRecordInitializer(type),
                TargetLanguage.Cpp => name + "{}",
                TargetLanguage.Python or TargetLanguage.Swift => name + "()",
                _ => "new " + name + "()"
            };
        }

        private void WriteValueAssignment(SmileType type, string target, string value)
        {
            string statement = type is RecordTypeSymbol record && RecordNeedsCopy(record)
                ? $"{target}.copyFrom({value})" : ValueAssignment(target, value);
            Line(statement + (_language is TargetLanguage.Swift or TargetLanguage.Python ? "" : ";"));
        }

        private string PrepareRecordLocation(BoundExpression expression) => expression switch
        {
            BoundVariableExpression variable => Name(variable.Variable),
            BoundArrayExpression array => ArrayTarget(array.Array, PrepareFieldIndices(array.Array.Name, array.Array.ArrayDimensions, array.Indices)),
            BoundFieldExpression field => FieldLocation(field),
            BoundWithReceiverExpression receiver => _withLocations[receiver.Location],
            _ => LowerOrderedCExpression(expression)
        };

        private string FieldLocation(BoundFieldExpression field)
        {
            string receiver = PrepareRecordLocation(field.Receiver);
            string target = $"({receiver}).{_identifiers.Get(field.Field)}";
            IReadOnlyList<string> indices = PrepareFieldIndices(field.Field.Name, field.Field.Dimensions, field.Indices);
            return _language is TargetLanguage.CSharp && indices.Count == 2
                ? target + $"[{string.Join(", ", indices)}]" : target + string.Concat(indices.Select(index => $"[{index}]"));
        }

        private IReadOnlyList<string> PrepareFieldIndices(string name, IReadOnlyList<int> dimensions, IReadOnlyList<BoundExpression> expressions)
        {
            var indices = new List<string>();
            for (int dimension = 0; dimension < expressions.Count; dimension++)
            {
                string value = LowerOrderedCExpression(expressions[dimension]);
                string index = $"_smileIndex{++_orderedTempId}";
                WriteIndexTemporary(index, CheckedArrayIndex(name, dimensions[dimension], value));
                indices.Add(index);
            }
            return indices;
        }

        private void WriteRecordArrayInitializers(IEnumerable<VariableSymbol> variables)
        {
            if (_language is not (TargetLanguage.CSharp or TargetLanguage.Java)) return;
            foreach (VariableSymbol variable in variables.Where(item => item.IsArray && item.Type is RecordTypeSymbol))
            {
                var field = new RecordFieldSymbol((RecordTypeSymbol)variable.Type, variable.Name, variable.Type, variable.ArrayDimensions, 0, variable.DeclarationSpan);
                WriteFieldElements(field, Name(variable), target => Line($"{target} = {DefaultLiteral(variable.Type)};"));
            }
        }
    }
}
