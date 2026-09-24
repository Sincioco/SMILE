namespace SMILE.Engine;

// A record's field cells belong to its variable/array cell. Whole-record writes
// copy into those cells so a previously captured ByRef field remains valid.
public sealed class SmileRecordValue
{
    internal SmileRecordValue(RecordTypeSymbol type)
    {
        Type = type;
        Fields = type.Fields.Select(field => Enumerable.Range(0, field.ElementCount)
            .Select(_ => Default(field.Type)).ToArray()).ToArray();
    }

    public RecordTypeSymbol Type { get; }
    internal SmileValue[][] Fields { get; }

    internal SmileRecordValue Clone()
    {
        var result = new SmileRecordValue(Type);
        result.CopyFrom(this);
        return result;
    }

    internal void CopyFrom(SmileRecordValue source)
    {
        if (ReferenceEquals(this, source)) return;
        for (int field = 0; field < Fields.Length; field++)
            for (int index = 0; index < Fields[field].Length; index++)
                Fields[field][index] = Store(Fields[field][index], source.Fields[field][index]);
    }

    internal static SmileValue Store(SmileValue destination, SmileValue source)
    {
        if (destination.Type is not RecordTypeSymbol) return source;
        destination.RecordValue.CopyFrom(source.RecordValue);
        return destination;
    }

    internal static SmileValue Copy(SmileValue value) => value.Type is RecordTypeSymbol
        ? SmileValue.FromRecord(value.RecordValue.Clone()) : value;

    internal static SmileValue Default(SmileType type) => type switch
    {
        RecordTypeSymbol record => SmileValue.FromRecord(new SmileRecordValue(record)),
        ClassTypeSymbol reference => SmileValue.FromClass(reference, null),
        EnumTypeSymbol enumeration => SmileValue.FromEnum(enumeration, 0),
        { Kind: SmileTypeKind.Double } => SmileValue.FromDouble(0),
        { Kind: SmileTypeKind.Integer } => SmileValue.FromInteger(0),
        { Kind: SmileTypeKind.Boolean } => SmileValue.FromBoolean(false),
        _ => SmileValue.FromString(string.Empty)
    };
}
