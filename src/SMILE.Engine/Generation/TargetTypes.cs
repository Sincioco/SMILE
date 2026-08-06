namespace SMILE.Engine;

internal static class TargetTypes
{
    public static string CSharp(SmileType type, TargetIntegerProfile integers) =>
        type switch
        {
            SmileType.String => "string",
            SmileType.Integer => integers.RequiresSigned64Storage ? "long" : "int",
            SmileType.Boolean => "bool",
            _ => "object"
        };

    public static string Java(SmileType type, TargetIntegerProfile integers) =>
        type switch
        {
            SmileType.String => "String",
            SmileType.Integer => integers.RequiresSigned64Storage ? "long" : "int",
            SmileType.Boolean => "boolean",
            _ => "Object"
        };

    public static string Swift(SmileType type, TargetIntegerProfile integers) =>
        type switch
        {
            SmileType.String => "String",
            SmileType.Integer => integers.RequiresSigned64Storage ? "Int64" : "Int",
            SmileType.Boolean => "Bool",
            _ => "String"
        };

    public static string C(SmileType type, TargetIntegerProfile integers) =>
        type switch
        {
            SmileType.String => "const char *",
            SmileType.Integer => integers.RequiresSigned64Storage ? "int64_t" : "int",
            SmileType.Boolean => "bool",
            _ => "const char *"
        };

    public static string CDeclaration(
        SmileType type,
        string name,
        TargetIntegerProfile integers) =>
        type is SmileType.String
            ? C(type, integers) + name
            : C(type, integers) + " " + name;

    public static string Cpp(SmileType type, TargetIntegerProfile integers) =>
        type switch
        {
            SmileType.String => "std::string",
            SmileType.Integer => integers.RequiresSigned64Storage ? "std::int64_t" : "int",
            SmileType.Boolean => "bool",
            _ => "std::string"
        };
}
