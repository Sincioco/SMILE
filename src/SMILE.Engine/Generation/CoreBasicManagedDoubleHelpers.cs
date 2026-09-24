namespace SMILE.Engine;

internal static partial class CoreBasicCodeGenerator
{
    private sealed partial class StructuredWriter
    {
        private void WriteManagedDoubleHelpers()
        {
            DoubleProgramFeatures features = DoubleFeatures;
            if (!features.IsRequired) return;
            string fail = DoubleName("double_fail");
            string check = DoubleName("check_double");
            string divisor = DoubleName("double_divisor");
            string convert = DoubleName("to_number");
            string clamp = DoubleName("double_clamp");
            string format = DoubleName("text_from_double");
            string parse = DoubleName("text_to_double");
            const string pattern = @"[\x09-\x0D ]*[+-]?[0-9]+(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?[\x09-\x0D ]*";
            string escapedPattern = pattern.Replace("\\", "\\\\");
            switch (_language)
            {
                case TargetLanguage.CSharp:
                    if (features.NeedsFailure) Lines($"private static void {fail}(int line)", "{", "    throw new ArithmeticException($\"SMILE Runtime Error SMILER3902: Invalid Double operation at line {line}.\");", "}");
                    if (features.NeedsCheck) Lines($"private static double {check}(double value, int line)", "{", $"    if (!double.IsFinite(value)) {fail}(line);", "    return value;", "}");
                    if (features.HasDivision) Lines($"private static double {divisor}(double value, int line)", "{", $"    if (value == 0.0) {fail}(line);", "    return value;", "}");
                    if (features.Has(BoundIntrinsicKind.ToNumber)) Lines($"private static long {convert}(double value, int line)", "{", $"    if (value < -9223372036854775808.0 || value >= 9223372036854775808.0) {fail}(line);", "    return (long)value;", "}");
                    if (features.Has(BoundIntrinsicKind.Clamp)) Lines($"private static double {clamp}(double value, double minimum, double maximum, int line)", "{", $"    if (minimum > maximum) {fail}(line);", "    return value < minimum ? minimum : value > maximum ? maximum : value;", "}");
                    if (features.HasFormatting) Lines($"private static string {format}(double value)", "{", "    if (value == 0.0) return BitConverter.DoubleToInt64Bits(value) < 0 ? \"-0.0\" : \"0.0\";", "    string text = value.ToString(\"R\", System.Globalization.CultureInfo.InvariantCulture);", "    return text.IndexOfAny(['.', 'e', 'E']) < 0 ? text + \".0\" : text;", "}");
                    if (features.Has(BoundIntrinsicKind.TextToDouble)) Lines($"private static double {parse}(string text, int line)", "{", "    double value = 0.0;", $"    if (!System.Text.RegularExpressions.Regex.IsMatch(text, @\"\\A{pattern}\\z\") ||", "        !double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value) ||", $"        !double.IsFinite(value)) {fail}(line);", "    return value;", "}");
                    break;
                case TargetLanguage.Java:
                    if (features.NeedsFailure) Lines($"private static void {fail}(int line) {{", "    throw new ArithmeticException(\"SMILE Runtime Error SMILER3902: Invalid Double operation at line \" + line + \".\");", "}");
                    if (features.NeedsCheck) Lines($"private static double {check}(double value, int line) {{", $"    if (!Double.isFinite(value)) {fail}(line);", "    return value;", "}");
                    if (features.HasDivision) Lines($"private static double {divisor}(double value, int line) {{", $"    if (value == 0.0) {fail}(line);", "    return value;", "}");
                    if (features.Has(BoundIntrinsicKind.ToNumber)) Lines($"private static long {convert}(double value, int line) {{", $"    if (value < -9223372036854775808.0 || value >= 9223372036854775808.0) {fail}(line);", "    return (long)value;", "}");
                    if (features.Has(BoundIntrinsicKind.Clamp)) Lines($"private static double {clamp}(double value, double minimum, double maximum, int line) {{", $"    if (minimum > maximum) {fail}(line);", "    return value < minimum ? minimum : value > maximum ? maximum : value;", "}");
                    if (features.Has(BoundIntrinsicKind.TextToDouble)) Lines($"private static double {parse}(String text, int line) {{", $"    if (!text.matches(\"\\\\A{escapedPattern}\\\\z\")) {fail}(line);", "    double value = Double.parseDouble(text);", $"    if (!Double.isFinite(value)) {fail}(line);", "    return value;", "}");
                    break;
                case TargetLanguage.JavaScript:
                    if (features.NeedsFailure) Lines($"function {fail}(line) {{", "    throw new RangeError(`SMILE Runtime Error SMILER3902: Invalid Double operation at line ${line}.`);", "}");
                    if (features.NeedsCheck) Lines($"function {check}(value, line) {{", $"    if (!Number.isFinite(value)) {fail}(line);", "    return value;", "}");
                    if (features.HasDivision) Lines($"function {divisor}(value, line) {{", $"    if (value === 0.0) {fail}(line);", "    return value;", "}");
                    if (features.Has(BoundIntrinsicKind.ToNumber)) Lines($"function {convert}(value, line) {{", $"    if (value < -9223372036854775808.0 || value >= 9223372036854775808.0) {fail}(line);", "    return BigInt(Math.trunc(value));", "}");
                    if (features.Has(BoundIntrinsicKind.Clamp)) Lines($"function {clamp}(value, minimum, maximum, line) {{", $"    if (minimum > maximum) {fail}(line);", "    return value < minimum ? minimum : value > maximum ? maximum : value;", "}");
                    if (features.HasFormatting) Lines($"function {format}(value) {{", "    if (Object.is(value, -0)) return \"-0.0\";", "    const text = String(value);", "    return /[.eE]/.test(text) ? text : text + \".0\";", "}");
                    if (features.Has(BoundIntrinsicKind.TextToDouble)) Lines($"function {parse}(text, line) {{", $"    if (!/^{pattern}(?![\\s\\S])/.test(text)) {fail}(line);", "    const value = Number(text);", $"    if (!Number.isFinite(value)) {fail}(line);", "    return value;", "}");
                    if (features.Has(BoundIntrinsicKind.Round)) Lines($"function {DoubleName("double_round")}(value) {{", "    const lower = Math.floor(value);", "    let result = value - lower === 0.5 ? (lower % 2 === 0 ? lower : lower + 1) : Math.round(value);", "    if (result === 0 && (value < 0 || Object.is(value, -0))) result = -0;", "    return result;", "}");
                    break;
                case TargetLanguage.Python:
                    if (features.NeedsFailure) Lines($"def {fail}(line):", "    raise ArithmeticError(f\"SMILE Runtime Error SMILER3902: Invalid Double operation at line {line}.\")");
                    if (features.NeedsCheck) Lines($"def {check}(value, line):", "    if not math.isfinite(value):", $"        {fail}(line)", "    return value");
                    if (features.HasDivision) Lines($"def {divisor}(value, line):", "    if value == 0.0:", $"        {fail}(line)", "    return value");
                    if (features.Has(BoundIntrinsicKind.Sqrt)) Lines($"def {DoubleName("double_nonnegative")}(value, line):", "    if value < 0.0:", $"        {fail}(line)", "    return value");
                    if (features.Has(BoundIntrinsicKind.ToNumber)) Lines($"def {convert}(value, line):", "    if value < -9223372036854775808.0 or value >= 9223372036854775808.0:", $"        {fail}(line)", "    return int(value)");
                    if (features.Has(BoundIntrinsicKind.Clamp)) Lines($"def {clamp}(value, minimum, maximum, line):", "    if minimum > maximum:", $"        {fail}(line)", "    return minimum if value < minimum else maximum if value > maximum else value");
                    if (features.Has(BoundIntrinsicKind.TextToDouble)) Lines($"def {parse}(text, line):", $"    if not re.fullmatch(r\"{pattern}\", text):", $"        {fail}(line)", "    value = float(text)", "    if not math.isfinite(value):", $"        {fail}(line)", "    return value");
                    break;
                case TargetLanguage.Swift:
                    if (features.NeedsFailure) Lines($"func {fail}(_ line: Int) -> Never {{", "    fatalError(\"SMILE Runtime Error SMILER3902: Invalid Double operation at line \\(line).\")", "}");
                    if (features.NeedsCheck) Lines($"func {check}(_ value: Double, _ line: Int) -> Double {{", $"    if !value.isFinite {{ {fail}(line) }}", "    return value", "}");
                    if (features.HasDivision) Lines($"func {divisor}(_ value: Double, _ line: Int) -> Double {{", $"    if value == 0.0 {{ {fail}(line) }}", "    return value", "}");
                    if (features.Has(BoundIntrinsicKind.ToNumber)) Lines($"func {convert}(_ value: Double, _ line: Int) -> Int64 {{", $"    if value < -9223372036854775808.0 || value >= 9223372036854775808.0 {{ {fail}(line) }}", "    return Int64(value)", "}");
                    if (features.Has(BoundIntrinsicKind.Clamp)) Lines($"func {clamp}(_ value: Double, _ minimum: Double, _ maximum: Double, _ line: Int) -> Double {{", $"    if minimum > maximum {{ {fail}(line) }}", "    return value < minimum ? minimum : value > maximum ? maximum : value", "}");
                    if (features.Has(BoundIntrinsicKind.TextToDouble)) Lines($"func {parse}(_ text: String, _ line: Int) -> Double {{", $"    guard text.range(of: \"\\\\A{escapedPattern}\\\\z\", options: .regularExpression) != nil,", "          let value = Double(text.trimmingCharacters(in: CharacterSet(charactersIn: \" \\t\\n\\r\\u{000B}\\u{000C}\"))), value.isFinite else {", $"        {fail}(line)", "    }", "    return value", "}");
                    break;
            }
        }
    }
}
