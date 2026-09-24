namespace SMILE.Engine;

// Small checked boundaries around the CRT's native binary64 operators/functions.
// This code is shared by C-family output and the MASM/COBOL native companions.
internal static class NativeDoubleSupport
{
    public static string GenerateCobol(BoundProgram program)
    {
        var features = new DoubleProgramFeatures(program);
        if (!features.NeedsCobolRuntime) return string.Empty;
        var source = new System.Text.StringBuilder("#include <stdio.h>\n#include <stdlib.h>\n#include <stdint.h>\n#include <math.h>\n#include <string.h>\n\n");
        foreach (string definition in Definitions(features)) source.AppendLine(definition);
        // GnuCOBOL lowers COMPUTE through decimal temporaries even for FLOAT-LONG.
        // C interoperability is required to retain binary64 rounding and signed zero.
        BoundExpression[] expressions = CoreBasicCodeGenerator.EnumerateExpressionsForSupport(program, includeConstants: false).ToArray();
        IEnumerable<BoundBinaryOperatorKind> operations = expressions.OfType<BoundBinaryExpression>()
            .Where(expression => expression.Left.Type is SmileType.Double).Select(expression => expression.Operator.Kind);
        if (features.HasSelection) operations = operations.Append(BoundBinaryOperatorKind.Equality);
        foreach (BoundBinaryOperatorKind kind in operations.Distinct())
        {
            string operation = kind switch
            {
                BoundBinaryOperatorKind.Addition => "+", BoundBinaryOperatorKind.Subtraction => "-",
                BoundBinaryOperatorKind.Multiplication => "*", BoundBinaryOperatorKind.Division => "/",
                BoundBinaryOperatorKind.Equality => "==", BoundBinaryOperatorKind.Inequality => "!=",
                BoundBinaryOperatorKind.Less => "<", BoundBinaryOperatorKind.LessOrEquals => "<=",
                BoundBinaryOperatorKind.Greater => ">", _ => ">="
            };
            bool comparison = kind >= BoundBinaryOperatorKind.Equality;
            string resultType = comparison ? "unsigned char" : "double";
            string right = kind is BoundBinaryOperatorKind.Division ? "smile_double_divisor(*right, line)" : "*right";
            string value = comparison ? $"*left {operation} *right" : $"smile_check_double(*left {operation} {right}, line)";
            source.AppendLine($"int smile_dbl_{kind.ToString().ToLowerInvariant()}_cobol(const double *left, const double *right, {resultType} *result, int line)");
            source.AppendLine($"{{ {(comparison ? "(void)line; " : "")}*result = {value}; return 0; }}");
        }
        if (features.HasNegation) source.AppendLine("int smile_dbl_negate_cobol(const double *value, double *result) { *result = -*value; return 0; }");
        if (features.HasLiterals) source.AppendLine("int smile_dbl_literal_cobol(const char *text, double *result) { *result = strtod(text, NULL); return 0; }");
        foreach (BoundIntrinsicKind kind in features.Intrinsics.Where(kind => kind is not (BoundIntrinsicKind.TextFromDouble or BoundIntrinsicKind.TextToDouble)))
        {
            bool toDouble = kind is BoundIntrinsicKind.ToDouble;
            bool toNumber = kind is BoundIntrinsicKind.ToNumber;
            string inputType = toDouble ? "int64_t" : "double";
            string outputType = toNumber ? "int64_t" : "double";
            string extra = kind is BoundIntrinsicKind.Min or BoundIntrinsicKind.Max or BoundIntrinsicKind.Atan2 ? ", const double *second"
                : kind is BoundIntrinsicKind.Clamp ? ", const double *minimum, const double *maximum" : "";
            string expression = kind switch
            {
                BoundIntrinsicKind.ToDouble => "(double)*value", BoundIntrinsicKind.ToNumber => "smile_to_number(*value, line)",
                BoundIntrinsicKind.Abs => "fabs(*value)", BoundIntrinsicKind.Min => "*value <= *second ? *value : *second",
                BoundIntrinsicKind.Max => "*value >= *second ? *value : *second",
                BoundIntrinsicKind.Clamp => "smile_double_clamp(*value, *minimum, *maximum, line)",
                BoundIntrinsicKind.Sqrt => "smile_check_double(sqrt(*value), line)",
                BoundIntrinsicKind.Sin => "smile_check_double(sin(*value), line)",
                BoundIntrinsicKind.Cos => "smile_check_double(cos(*value), line)",
                BoundIntrinsicKind.Atan2 => "smile_check_double(atan2(*value, *second), line)",
                BoundIntrinsicKind.Floor => "floor(*value)", BoundIntrinsicKind.Ceiling => "ceil(*value)",
                BoundIntrinsicKind.Truncate => "trunc(*value)", BoundIntrinsicKind.Round => "nearbyint(*value)",
                _ => throw new InvalidOperationException("Unexpected Double intrinsic.")
            };
            source.AppendLine($"int smile_dbl_{kind.ToString().ToLowerInvariant()}_cobol(const {inputType} *value{extra}, {outputType} *result, int line)");
            source.AppendLine($"{{ (void)line; *result = {expression}; return 0; }}");
        }
        if (features.HasFormatting) source.AppendLine("""
int smile_dbl_textfromdouble_cobol(const double *value, char *text, int64_t *length, int line)
{
    (void)line;
    smile_format_double(*value, text);
    *length = (int64_t)strlen(text);
    return 0;
}
""");
        if (features.Has(BoundIntrinsicKind.TextToDouble)) source.AppendLine("""
int smile_dbl_texttodouble_cobol(const char *text, const int64_t *length, double *result, int line)
{
    char buffer[4097];
    if (*length < 0 || *length > 4096 || memchr(text, 0, (size_t)*length) != NULL) smile_double_fail(line);
    memcpy(buffer, text, (size_t)*length);
    buffer[*length] = 0;
    *result = smile_text_to_double(buffer, line);
    return 0;
}
""");
        return source.ToString();
    }

    public static IEnumerable<string> Definitions(DoubleProgramFeatures features)
    {
        if (features.NeedsFailure) yield return """
static void smile_double_fail(int line)
{
    fprintf(stderr, "SMILE Runtime Error SMILER3902: Invalid Double operation at line %d.\n", line);
    exit(1);
}
""";
        if (features.NeedsCheck) yield return """
static double smile_check_double(double value, int line)
{
    if (!isfinite(value)) smile_double_fail(line);
    return value;
}
""";
        if (features.HasDivision) yield return """
static double smile_double_divisor(double value, int line)
{
    if (value == 0.0) smile_double_fail(line);
    return value;
}
""";
        if (features.Has(BoundIntrinsicKind.ToNumber)) yield return """
static int64_t smile_to_number(double value, int line)
{
    if (value < -9223372036854775808.0 || value >= 9223372036854775808.0) smile_double_fail(line);
    return (int64_t)value;
}
""";
        if (features.Has(BoundIntrinsicKind.Clamp)) yield return """
static double smile_double_clamp(double value, double minimum, double maximum, int line)
{
    if (minimum > maximum) smile_double_fail(line);
    return value < minimum ? minimum : value > maximum ? maximum : value;
}
""";
        if (features.HasFormatting) yield return """
static void smile_format_double(double value, char *buffer)
{
    snprintf(buffer, 32, "%.17g", value);
    if (strchr(buffer, '.') == NULL && strchr(buffer, 'e') == NULL && strchr(buffer, 'E') == NULL)
        strcat(buffer, ".0");
}
""";
        if (features.Has(BoundIntrinsicKind.TextToDouble)) yield return """
static double smile_text_to_double(const char *text, int line)
{
    const char *cursor = text;
    while (*cursor == ' ' || (*cursor >= 9 && *cursor <= 13)) cursor++;
    if (*cursor == '+' || *cursor == '-') cursor++;
    if (*cursor < '0' || *cursor > '9') smile_double_fail(line);
    while (*cursor >= '0' && *cursor <= '9') cursor++;
    if (*cursor == '.')
    {
        cursor++;
        if (*cursor < '0' || *cursor > '9') smile_double_fail(line);
        while (*cursor >= '0' && *cursor <= '9') cursor++;
    }
    if (*cursor == 'e' || *cursor == 'E')
    {
        cursor++;
        if (*cursor == '+' || *cursor == '-') cursor++;
        if (*cursor < '0' || *cursor > '9') smile_double_fail(line);
        while (*cursor >= '0' && *cursor <= '9') cursor++;
    }
    while (*cursor == ' ' || (*cursor >= 9 && *cursor <= 13)) cursor++;
    if (*cursor != 0) smile_double_fail(line);
    double value = strtod(text, NULL);
    if (!isfinite(value)) smile_double_fail(line);
    return value;
}
""";
    }
}
