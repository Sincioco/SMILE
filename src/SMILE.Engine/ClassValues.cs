namespace SMILE.Engine;

// A class reference shares one instance. Its record fields still own value cells,
// so replacing another reference never invalidates a captured field location.
public sealed class SmileClassValue
{
    internal SmileClassValue(ClassTypeSymbol type)
    {
        Type = type;
        Fields = type.Fields.Select(field => Enumerable.Range(0, field.ElementCount)
            .Select(_ => SmileRecordValue.Default(field.Type)).ToArray()).ToArray();
    }

    public ClassTypeSymbol Type { get; }
    internal SmileValue[][] Fields { get; }
}
