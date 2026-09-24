namespace SMILE.Engine;

public enum SmileTypeKind { String, Double, Integer, Boolean, Error, Enum, Record, Class }

// A type is a symbol, not just a storage category. Built-ins are shared singletons;
// each future nominal declaration owns its own symbol even when layouts match.
// Reference equality therefore keeps assignment/call checks exact by construction.
public class SmileType
{
    private protected SmileType(SmileTypeKind kind, string name)
    {
        Kind = kind;
        Name = name;
    }

    public static SmileType String { get; } = new(SmileTypeKind.String, "Text");
    public static SmileType Double { get; } = new(SmileTypeKind.Double, "Double");
    public static SmileType Integer { get; } = new(SmileTypeKind.Integer, "Number");
    public static SmileType Boolean { get; } = new(SmileTypeKind.Boolean, "Boolean");
    public static SmileType Error { get; } = new(SmileTypeKind.Error, "Error");

    public SmileTypeKind Kind { get; }
    public string Name { get; }
    public override string ToString() => Name;
}
