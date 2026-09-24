namespace SMILE.Engine;

internal sealed partial class CobolWriter
{
    private void WriteRecordVariable(VariableSymbol variable, int level, bool linkage)
    {
        string name = Name(variable);
        if (variable.IsArray)
        {
            if (level == 1) { RecordLine(level, name + "."); level++; }
            name = ArrayElementName(variable);
        }
        WriteRecordShape(name, (RecordTypeSymbol)variable.Type, variable.ArrayDimensions, level, linkage);
    }

    private void WriteRecordShape(string name, RecordTypeSymbol type, IReadOnlyList<int> dimensions, int level, bool linkage)
    {
        if (dimensions.Count == 2)
        {
            RecordLine(level++, $"{name}-ROW OCCURS {dimensions[0]} TIMES.");
        }
        RecordLine(level, name + (dimensions.Count > 0 ? $" OCCURS {dimensions[^1]} TIMES" : "") + ".");
        if (type.Fields.Count == 0) RecordLine(level + 1, "FILLER PIC X" + (linkage ? "" : " VALUE SPACE") + ".");
        foreach (RecordFieldSymbol field in type.Fields)
        {
            string fieldName = _identifiers.Get(field);
            int fieldLevel = level + 1;
            if (field.Type is RecordTypeSymbol nested)
            {
                WriteRecordShape(fieldName, nested, field.Dimensions, fieldLevel, linkage);
                continue;
            }
            for (int dimension = 0; dimension < field.Dimensions.Count; dimension++)
                RecordLine(fieldLevel++, $"{fieldName}-{(dimension == 0 ? "CELLS" : "ROW")} OCCURS {field.Dimensions[dimension]} TIMES.");
            RecordLine(fieldLevel, $"{fieldName} {Picture(field.Type)}{(linkage ? "" : " " + DefaultClause(field.Type))}.");
            if (field.Type == SmileType.String)
                RecordLine(fieldLevel, $"{fieldName}-LENGTH PIC S9(18) COMP-5{(linkage ? "" : " VALUE 0")}.");
        }
    }

    private void RecordLine(int level, string declaration) => Line($"       {level:00} {declaration}");

    private sealed partial class ProcedureEmitter
    {
        private readonly Dictionary<WithLocationSymbol, string> _withLocations = new();

        private bool WriteWith(BoundWithStatement block, int indent)
        {
            _withLocations[block.Location] = block.Location.Target switch
            {
                BoundArrayExpression array => PrepareArrayElement(array.Array, array.Indices, indent, checkEachDimension: true).Value,
                BoundFieldExpression field => PrepareRecordField(field, indent).Value,
                _ => PrepareExpression(block.Location.Target, indent)
            };
            return WriteItems(block.SourceItems, indent);
        }

        private PreparedArrayElement PrepareRecordField(BoundFieldExpression field, int indent)
        {
            string receiver = field.Receiver switch
            {
                BoundFieldExpression parent => PrepareRecordField(parent, indent).Value,
                BoundArrayExpression array => PrepareArrayElement(array.Array, array.Indices, indent, checkEachDimension: true).Value,
                _ => PrepareExpression(field.Receiver, indent)
            };
            int separator = receiver.IndexOf('(');
            var indices = new List<string>();
            if (separator >= 0)
            {
                indices.Add(receiver[(separator + 1)..^1]);
                receiver = receiver[..separator];
            }
            for (int dimension = 0; dimension < field.Indices.Count; dimension++)
            {
                string value = PrepareExpression(field.Indices[dimension], indent);
                Temporary index = NewTemporary(SmileType.Integer);
                Assign(index.Name, SmileType.Integer, field.Indices[dimension], value, indent);
                Line(indent, $"IF {index.Name} < 0 OR {index.Name} >= {field.Field.Dimensions[dimension]}");
                Line(indent + 1, "DISPLAY \"SMILE Runtime Error SMILER1210: Array index is outside the declared bounds.\" UPON STDERR");
                Line(indent + 1, "STOP RUN RETURNING 1");
                Line(indent, "END-IF");
                Line(indent, $"ADD 1 TO {index.Name}");
                indices.Add(index.Name);
            }
            string suffix = " OF " + receiver + (indices.Count == 0 ? "" : "(" + string.Join(", ", indices) + ")");
            string name = _owner._identifiers.Get(field.Field);
            return new PreparedArrayElement(name + suffix, field.Type == SmileType.String ? name + "-LENGTH" + suffix : null);
        }
    }
}
