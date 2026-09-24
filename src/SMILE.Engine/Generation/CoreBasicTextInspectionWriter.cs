namespace SMILE.Engine;

internal static partial class CoreBasicCodeGenerator
{
    private sealed partial class StructuredWriter
    {
        private string TextIntrinsic(BoundIntrinsicKind kind, IReadOnlyList<string> arguments)
        {
            string text = arguments[0];
            if (kind is BoundIntrinsicKind.TextLength)
            {
                return _language switch
                {
                    TargetLanguage.CSharp => $"((long)System.Linq.Enumerable.Count({text}.EnumerateRunes()))",
                    TargetLanguage.Java => $"{text}.codePoints().count()",
                    TargetLanguage.JavaScript => $"BigInt([...{text}].length)",
                    TargetLanguage.Swift => $"Int64({text}.unicodeScalars.count)",
                    TargetLanguage.Python => $"len({text})",
                    _ => $"smile_text_length({text})"
                };
            }

            string name = kind is BoundIntrinsicKind.TextCodeAt ? "smileTextCodeAt" : "smileTextSlice";
            if (_language is TargetLanguage.Python or TargetLanguage.C or TargetLanguage.ObjectiveC or TargetLanguage.Cpp)
            {
                name = kind is BoundIntrinsicKind.TextCodeAt ? "smile_text_code_at" : "smile_text_slice";
            }
            return $"{name}({string.Join(", ", arguments)})";
        }

        private void WriteTextInspectionHelpers()
        {
            if (!_features.HasTextInspection) return;
            switch (_language)
            {
                case TargetLanguage.CSharp:
                    if (_features.HasTextCodeAt) Lines(
                        "private static long smileTextCodeAt(string text, long index)",
                        "{",
                        "    if (index < 0) return -1;",
                        "    foreach (var scalar in text.EnumerateRunes())",
                        "    {",
                        "        if (index-- == 0) return scalar.Value;",
                        "    }",
                        "    return -1;",
                        "}");
                    if (_features.HasTextSlice) Lines(
                        "private static string smileTextSlice(string text, long start, long count)",
                        "{",
                        "    if (start < 0 || count <= 0) return string.Empty;",
                        "    var result = new System.Text.StringBuilder();",
                        "    foreach (var scalar in text.EnumerateRunes())",
                        "    {",
                        "        if (start > 0) { start--; continue; }",
                        "        result.Append(scalar.ToString());",
                        "        if (--count == 0) break;",
                        "    }",
                        "    return result.ToString();",
                        "}");
                    break;
                case TargetLanguage.Java:
                    if (_features.HasTextCodeAt) Lines(
                        "private static long smileTextCodeAt(String text, long index) {",
                        "    if (index < 0) return -1;",
                        "    return text.codePoints().skip(index).findFirst().orElse(-1);",
                        "}");
                    if (_features.HasTextSlice) Lines(
                        "private static String smileTextSlice(String text, long start, long count) {",
                        "    if (start < 0 || count <= 0) return \"\";",
                        "    int[] scalars = text.codePoints().skip(start).limit(count).toArray();",
                        "    return new String(scalars, 0, scalars.length);",
                        "}");
                    break;
                case TargetLanguage.JavaScript:
                    if (_features.HasTextCodeAt) Lines(
                        "function smileTextCodeAt(text, index) {",
                        "    if (index < 0n) return -1n;",
                        "    for (const scalar of text) {",
                        "        if (index-- === 0n) return BigInt(scalar.codePointAt(0));",
                        "    }",
                        "    return -1n;",
                        "}");
                    if (_features.HasTextSlice) Lines(
                        "function smileTextSlice(text, start, count) {",
                        "    if (start < 0n || count <= 0n) return \"\";",
                        "    const scalars = [...text];",
                        "    const size = BigInt(scalars.length);",
                        "    if (start >= size) return \"\";",
                        "    const end = count >= size - start ? size : start + count;",
                        "    return scalars.slice(Number(start), Number(end)).join(\"\");",
                        "}");
                    break;
                case TargetLanguage.Python:
                    if (_features.HasTextCodeAt) Lines(
                        "def smile_text_code_at(text, index):",
                        "    return ord(text[index]) if 0 <= index < len(text) else -1");
                    if (_features.HasTextSlice) Lines(
                        "def smile_text_slice(text, start, count):",
                        "    return text[start:start + count] if start >= 0 and count > 0 else \"\"");
                    break;
                case TargetLanguage.Swift:
                    if (_features.HasTextCodeAt) Lines(
                        "func smileTextCodeAt(_ text: String, _ index: Int64) -> Int64 {",
                        "    if index < 0 { return -1 }",
                        "    var remaining = index",
                        "    for scalar in text.unicodeScalars {",
                        "        if remaining == 0 { return Int64(scalar.value) }",
                        "        remaining -= 1",
                        "    }",
                        "    return -1",
                        "}");
                    if (_features.HasTextSlice) Lines(
                        "func smileTextSlice(_ text: String, _ start: Int64, _ count: Int64) -> String {",
                        "    if start < 0 || count <= 0 { return \"\" }",
                        "    var skip = start",
                        "    var remaining = count",
                        "    var result = String.UnicodeScalarView()",
                        "    for scalar in text.unicodeScalars {",
                        "        if skip > 0 { skip -= 1; continue }",
                        "        result.append(scalar)",
                        "        remaining -= 1",
                        "        if remaining == 0 { break }",
                        "    }",
                        "    return String(result)",
                        "}");
                    break;
                case TargetLanguage.C:
                case TargetLanguage.ObjectiveC:
                case TargetLanguage.Cpp:
                    Lines(NativeTextInspection.Generate(_features, _language).Split('\n'));
                    break;
            }
        }

        private void WriteTextInspectionPrototypes()
        {
            if (!_features.HasTextInspection) return;
            bool cpp = _language is TargetLanguage.Cpp;
            string type = cpp ? "const std::string&" : "const char *";
            string integer = cpp ? "std::int64_t" : "int64_t";
            if (_features.HasTextLength) Line($"static {integer} smile_text_length({type} text);");
            if (_features.HasTextCodeAt) Line($"static {integer} smile_text_code_at({type} text, {integer} index);");
            if (_features.HasTextSlice) Line($"static {(cpp ? "std::string" : "const char *")} smile_text_slice({type} text, {integer} start, {integer} count);");
        }
    }
}
