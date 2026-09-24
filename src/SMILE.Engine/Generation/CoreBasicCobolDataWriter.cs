namespace SMILE.Engine;

internal sealed partial class CobolWriter
{
    private sealed partial class ProcedureEmitter
    {
        private void WriteDataLoad(BoundDataLoadStatement load, int indent)
        {
            Temporary key = CaptureDataValue(load.Key, indent);
            Temporary count = NewTemporary(SmileType.Integer), status = NewTemporary(SmileType.Integer);
            Temporary capacity = DataNumber(load.Destination.ArrayLength, indent), recover = DataNumber(load.Status is null ? 0 : 1, indent);
            Line(indent, $"CALL \"smile_load_data_cobol\" USING BY REFERENCE {key.Name} {LengthName(key)} {_owner.ArrayElementName(load.Destination)}(1) {capacity.Name} {count.Name} {recover.Name} {status.Name}");
            WriteDataTarget(load.Count, count.Name, indent);
            if (load.Status is not null) WriteDataTarget(load.Status, status.Name, indent);
        }

        private void WriteDataSave(BoundDataSaveStatement save, int indent)
        {
            Temporary count = CaptureDataValue(save.Count, indent), key = CaptureDataValue(save.Key, indent);
            Temporary capacity = DataNumber(save.Source.ArrayLength, indent), recover = DataNumber(save.Status is null ? 0 : 1, indent);
            Temporary status = NewTemporary(SmileType.Integer);
            Line(indent, $"CALL \"smile_save_data_cobol\" USING BY REFERENCE {_owner.ArrayElementName(save.Source)}(1) {capacity.Name} {count.Name} {key.Name} {LengthName(key)} {recover.Name} {status.Name}");
            if (save.Status is not null) WriteDataTarget(save.Status, status.Name, indent);
        }

        private Temporary CaptureDataValue(BoundExpression expression, int indent)
        {
            string value = PrepareExpression(expression, indent);
            Temporary temporary = NewTemporary(expression.Type);
            Assign(temporary.Name, expression.Type, expression, value, indent, expression.Type is { Kind: SmileTypeKind.String } ? LengthName(temporary) : null);
            return temporary;
        }

        private Temporary DataNumber(long value, int indent)
        {
            Temporary temporary = NewTemporary(SmileType.Integer);
            Line(indent, $"MOVE {value} TO {temporary.Name}");
            return temporary;
        }

        private void WriteDataTarget(BoundExpression expression, string value, int indent)
        {
            string target = expression is BoundFieldExpression field ? PrepareRecordField(field, indent).Value : expression is BoundVariableExpression variable ? _owner.Name(variable.Variable) :
                PrepareArrayElement(((BoundArrayExpression)expression).Array, ((BoundArrayExpression)expression).Indices, indent).Value;
            Line(indent, $"MOVE {value} TO {target}");
        }
    }
}
