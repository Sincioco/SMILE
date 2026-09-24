namespace SMILE.Engine;

internal static partial class CoreBasicCodeGenerator
{
    private sealed partial class StructuredWriter
    {
        private readonly DoubleProgramFeatures _doubleFeatures;
        private DoubleProgramFeatures DoubleFeatures => _doubleFeatures;

        private string DoubleName(string name) => _language switch
        {
            TargetLanguage.CSharp => "Smile" + string.Concat(name.Split('_').Select(word => char.ToUpperInvariant(word[0]) + word[1..])),
            TargetLanguage.Java or TargetLanguage.JavaScript or TargetLanguage.Swift => "smile" + string.Concat(name.Split('_').Select(word => char.ToUpperInvariant(word[0]) + word[1..])),
            _ => "smile_" + name
        };

        private string DoubleCall(string name, params string[] arguments) => $"{DoubleName(name)}({string.Join(", ", arguments)})";

        private string DoubleBinary(BoundBinaryExpression binary, string left, string right)
        {
            if (binary.Type is not { Kind: SmileTypeKind.Double }) return $"({left} {Operator(binary.Operator.Kind)} {right})";
            string line = binary.OperatorSpan.Line.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (binary.Operator.Kind is BoundBinaryOperatorKind.Division) right = DoubleCall("double_divisor", right, line);
            return DoubleCall("check_double", $"({left} {Operator(binary.Operator.Kind)} {right})", line);
        }

        private string DoubleIntrinsic(BoundIntrinsicExpression intrinsic, IReadOnlyList<string> arguments)
        {
            string a = arguments[0];
            string b = arguments.Count > 1 ? arguments[1] : "0.0";
            string line = intrinsic.Span.Line.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string MathCall(string cs, string c, string swift, string python, string? java = null, string? js = null) => _language switch
            {
                TargetLanguage.CSharp => $"Math.{cs}({string.Join(", ", arguments)})",
                TargetLanguage.Java => $"Math.{java ?? c}({string.Join(", ", arguments)})",
                TargetLanguage.JavaScript => $"Math.{js ?? c}({string.Join(", ", arguments)})",
                TargetLanguage.Python => python,
                TargetLanguage.Swift => swift,
                TargetLanguage.Cpp => $"std::{c}({string.Join(", ", arguments)})",
                _ => $"{c}({string.Join(", ", arguments)})"
            };
            string Compare(string operation) => _language is TargetLanguage.Python
                ? $"({a} if {a} {operation} {b} else {b})" : $"({a} {operation} {b} ? {a} : {b})";
            string result = intrinsic.Kind switch
            {
                BoundIntrinsicKind.ToDouble => _language switch
                {
                    TargetLanguage.JavaScript => $"Number({a})", TargetLanguage.Python => $"float({a})",
                    TargetLanguage.Swift => $"Double({a})", TargetLanguage.Cpp => $"static_cast<double>({a})", _ => $"((double){a})"
                },
                BoundIntrinsicKind.ToNumber => DoubleCall("to_number", a, line),
                BoundIntrinsicKind.TextToDouble => DoubleCall("text_to_double", a, line),
                BoundIntrinsicKind.TextFromDouble => FormatDoubleExpression(a),
                BoundIntrinsicKind.Clamp => DoubleCall("double_clamp", a, b, arguments[2], line),
                BoundIntrinsicKind.Abs => MathCall("Abs", "fabs", $"abs({a})", $"abs({a})", "abs", "abs"),
                BoundIntrinsicKind.Min => Compare("<="),
                BoundIntrinsicKind.Max => Compare(">="),
                BoundIntrinsicKind.Sqrt => MathCall("Sqrt", "sqrt", $"sqrt({a})", $"math.sqrt({DoubleCall("double_nonnegative", a, line)})"),
                BoundIntrinsicKind.Sin => MathCall("Sin", "sin", $"sin({a})", $"math.sin({a})"),
                BoundIntrinsicKind.Cos => MathCall("Cos", "cos", $"cos({a})", $"math.cos({a})"),
                BoundIntrinsicKind.Atan2 => MathCall("Atan2", "atan2", $"atan2({a}, {b})", $"math.atan2({a}, {b})"),
                BoundIntrinsicKind.Floor => MathCall("Floor", "floor", $"{a}.rounded(.down)", $"math.copysign(float(math.floor({a})), {a})"),
                BoundIntrinsicKind.Ceiling => MathCall("Ceiling", "ceil", $"{a}.rounded(.up)", $"math.copysign(float(math.ceil({a})), {a})"),
                BoundIntrinsicKind.Truncate => MathCall("Truncate", "trunc", $"{a}.rounded(.towardZero)", $"math.copysign(float(math.trunc({a})), {a})", "unused"),
                BoundIntrinsicKind.Round => MathCall("Round", "nearbyint", $"{a}.rounded(.toNearestOrEven)", $"math.copysign(float(round({a})), {a})", "rint", "unused"),
                _ => throw new InvalidOperationException("Unexpected Double intrinsic.")
            };
            if (intrinsic.Kind is BoundIntrinsicKind.Truncate && _language is TargetLanguage.Java)
                result = $"({a} < 0.0 ? Math.ceil({a}) : Math.floor({a}))";
            if (intrinsic.Kind is BoundIntrinsicKind.Round && _language is TargetLanguage.JavaScript)
                result = DoubleCall("double_round", a);
            return intrinsic.Kind is BoundIntrinsicKind.Sqrt or BoundIntrinsicKind.Sin or BoundIntrinsicKind.Cos or BoundIntrinsicKind.Atan2
                ? DoubleCall("check_double", result, line) : result;
        }

        private string FormatDoubleExpression(string value) => _language switch
        {
            TargetLanguage.Java => $"Double.toString({value})",
            TargetLanguage.Swift => $"String({value})",
            TargetLanguage.Python => $"repr({value})",
            _ => DoubleCall("text_from_double", value)
        };

        private string DoubleDisplay(string value)
        {
            if (_language is TargetLanguage.C or TargetLanguage.ObjectiveC)
            {
                string buffer = $"_smileDoubleText{++_orderedTempId}";
                Line($"char {buffer}[32];");
                Line($"smile_format_double({value}, {buffer});");
                return buffer;
            }
            return FormatDoubleExpression(value);
        }

        private void WriteDoubleIncludes()
        {
            if (!DoubleFeatures.IsRequired) return;
            switch (_language)
            {
                case TargetLanguage.C:
                case TargetLanguage.ObjectiveC: Line("#include <math.h>"); Line("#include <string.h>"); break;
                case TargetLanguage.Cpp: Line("#include <cmath>"); Line("#include <cstdio>"); Line("#include <cstring>"); break;
                case TargetLanguage.Python:
                    if (DoubleFeatures.NeedsCheck || DoubleFeatures.Has(BoundIntrinsicKind.TextToDouble) ||
                        DoubleFeatures.Intrinsics.Any(kind => kind is BoundIntrinsicKind.Floor or BoundIntrinsicKind.Ceiling or BoundIntrinsicKind.Truncate or BoundIntrinsicKind.Round)) Line("import math");
                    if (DoubleFeatures.Has(BoundIntrinsicKind.TextToDouble)) Line("import re");
                    break;
            }
        }

        private IEnumerable<string> NativeDoubleDefinitions()
        {
            foreach (string definition in NativeDoubleSupport.Definitions(DoubleFeatures))
            {
                string native = _language is TargetLanguage.Cpp ? definition.Replace("isfinite(", "std::isfinite(") : definition;
                if (_language is TargetLanguage.Cpp && native.StartsWith("static double smile_text_to_double", StringComparison.Ordinal))
                    native = native.Replace("const char *text, int line", "const std::string& input, int line")
                        .Replace("    const char *cursor = text;", "    const char *text = input.c_str();\n    if (strlen(text) != input.size()) smile_double_fail(line);\n    const char *cursor = text;");
                yield return native;
            }
            if (DoubleFeatures.HasFormatting && _language is TargetLanguage.Cpp) yield return """
static std::string smile_text_from_double(double value)
{
    char buffer[32];
    smile_format_double(value, buffer);
    return std::string(buffer);
}
""";
            else if (DoubleFeatures.Has(BoundIntrinsicKind.TextFromDouble)) yield return """
static const char *smile_text_from_double(double value)
{
    char *buffer = smile_text_allocate(32);
    smile_format_double(value, buffer);
    return buffer;
}
""";
        }

        private void WriteDoublePrototypes()
        {
            if (_language is not (TargetLanguage.C or TargetLanguage.ObjectiveC or TargetLanguage.Cpp)) return;
            foreach (string definition in NativeDoubleDefinitions()) Line(definition.Split('\n')[0] + ";");
        }

        private void WriteDoubleHelpers()
        {
            if (_language is TargetLanguage.C or TargetLanguage.ObjectiveC or TargetLanguage.Cpp)
                foreach (string definition in NativeDoubleDefinitions()) Lines(definition.Split('\n'));
            else WriteManagedDoubleHelpers();
        }
    }
}
