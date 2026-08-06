using System.Text;

namespace SMILE.Engine;

internal static class TargetEscapes
{
    public static string CSharpString(string text) => Quote(EscapeCSharp(text));

    public static string CString(string text) => Quote(EscapeCStyle(text));

    public static string ObjectiveCString(string text) => "@" + Quote(EscapeCStyle(text));

    public static string CPrintfFormatString(string text) => CString(text);

    public static string JavaScriptString(string text) => Quote(EscapeJavaScript(text));

    public static string JavaString(string text) => Quote(EscapeJava(text));

    public static string CobolString(string text) =>
        CanUsePlainCobolLiteral(text)
            ? Quote(text.Replace("\"", "\"\"", StringComparison.Ordinal))
            : "X\"" + ToHex(Encoding.UTF8.GetBytes(text)) + "\"";

    public static int CobolByteLength(string text) =>
        Encoding.UTF8.GetByteCount(text);

    public static string SwiftString(string text) => Quote(EscapeSwift(text));

    public static string PythonString(string text) => Quote(EscapePython(text, escapeBraces: false));

    public static string CSharpInterpolatedText(string text) => EscapeCSharpInterpolatedText(text);

    public static string JavaScriptTemplateText(string text) => EscapeJavaScriptTemplateText(text);

    public static string SwiftInterpolatedText(string text) => EscapeSwift(text);

    public static string PythonFStringText(string text) => EscapePython(text, escapeBraces: true);

    public static string MasmByteInitializers(string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        var parts = new List<string>();
        var currentText = new StringBuilder();

        foreach (byte value in bytes)
        {
            if (value is >= 32 and <= 126 and not (byte)'"')
            {
                currentText.Append((char)value);
                continue;
            }

            FlushText();
            parts.Add(value.ToString());
        }

        FlushText();
        return parts.Count == 0 ? "0" : string.Join(", ", parts);

        void FlushText()
        {
            if (currentText.Length == 0)
            {
                return;
            }

            parts.Add(Quote(currentText.ToString()));
            currentText.Clear();
        }
    }

    private static string Quote(string text) => $"\"{text}\"";

    private static bool CanUsePlainCobolLiteral(string text) =>
        text.Length > 0 &&
        text.All(value => value is >= ' ' and <= '~');

    private static string ToHex(byte[] bytes)
    {
        const string digits = "0123456789ABCDEF";
        var builder = new StringBuilder(bytes.Length * 2);

        foreach (byte value in bytes)
        {
            builder.Append(digits[value >> 4]);
            builder.Append(digits[value & 0xF]);
        }

        return builder.ToString();
    }

    private static string EscapeCSharp(string text)
    {
        var builder = new StringBuilder();

        foreach (char value in text)
        {
            builder.Append(value switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\0' => "\\0",
                '\a' => "\\a",
                '\b' => "\\b",
                '\f' => "\\f",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                '\v' => "\\v",
                _ when char.IsControl(value) => $"\\u{(int)value:x4}",
                _ => value
            });
        }

        return builder.ToString();
    }

    private static string EscapeCSharpInterpolatedText(string text)
    {
        var builder = new StringBuilder();

        foreach (char value in text)
        {
            builder.Append(value switch
            {
                '{' => "{{",
                '}' => "}}",
                '\\' => "\\\\",
                '"' => "\\\"",
                '\0' => "\\0",
                '\a' => "\\a",
                '\b' => "\\b",
                '\f' => "\\f",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                '\v' => "\\v",
                _ when char.IsControl(value) => $"\\u{(int)value:x4}",
                _ => value
            });
        }

        return builder.ToString();
    }

    private static string EscapeCStyle(string text)
    {
        var builder = new StringBuilder();

        foreach (char value in text)
        {
            builder.Append(value switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\0' => "\\000",
                '\a' => "\\007",
                '\b' => "\\b",
                '\f' => "\\f",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                '\v' => "\\013",
                _ when char.IsControl(value) => EscapeUtf8BytesAsOctal(value),
                _ => value
            });
        }

        return builder.ToString();
    }

    private static string EscapeUtf8BytesAsOctal(char value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value.ToString());
        var builder = new StringBuilder();

        foreach (byte utf8Byte in bytes)
        {
            builder.Append('\\');
            builder.Append(ToFixedOctal(utf8Byte));
        }

        return builder.ToString();
    }

    private static string ToFixedOctal(byte value)
    {
        Span<char> digits = stackalloc char[3];
        digits[0] = (char)('0' + ((value >> 6) & 0b111));
        digits[1] = (char)('0' + ((value >> 3) & 0b111));
        digits[2] = (char)('0' + (value & 0b111));
        return new string(digits);
    }

    private static string EscapeJava(string text)
    {
        var builder = new StringBuilder();

        foreach (char value in text)
        {
            builder.Append(value switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\b' => "\\b",
                '\t' => "\\t",
                '\n' => "\\n",
                '\f' => "\\f",
                '\r' => "\\r",
                _ when value < 32 => "\\" + ToFixedOctal((byte)value),
                _ when char.IsControl(value) => $"\\u{(int)value:x4}",
                _ => value
            });
        }

        return builder.ToString();
    }

    private static string EscapeJavaScript(string text)
    {
        var builder = new StringBuilder();

        foreach (char value in text)
        {
            builder.Append(value switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\b' => "\\b",
                '\t' => "\\t",
                '\n' => "\\n",
                '\f' => "\\f",
                '\r' => "\\r",
                _ when char.IsControl(value) => $"\\u{(int)value:x4}",
                _ => value
            });
        }

        return builder.ToString();
    }

    private static string EscapeJavaScriptTemplateText(string text)
    {
        var builder = new StringBuilder();

        for (int index = 0; index < text.Length; index++)
        {
            char value = text[index];
            builder.Append(value switch
            {
                '\\' => "\\\\",
                '`' => "\\`",
                '$' when index + 1 < text.Length && text[index + 1] == '{' => "\\$",
                '\b' => "\\b",
                '\t' => "\\t",
                '\n' => "\\n",
                '\f' => "\\f",
                '\r' => "\\r",
                _ when char.IsControl(value) => $"\\u{(int)value:x4}",
                _ => value
            });
        }

        return builder.ToString();
    }

    private static string EscapeSwift(string text)
    {
        var builder = new StringBuilder();

        foreach (char value in text)
        {
            builder.Append(value switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\0' => "\\0",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ when char.IsControl(value) => $"\\u{{{(int)value:x}}}",
                _ => value
            });
        }

        return builder.ToString();
    }

    private static string EscapePython(string text, bool escapeBraces)
    {
        var builder = new StringBuilder();

        foreach (char value in text)
        {
            builder.Append(value switch
            {
                '{' when escapeBraces => "{{",
                '}' when escapeBraces => "}}",
                '\\' => "\\\\",
                '"' => "\\\"",
                '\0' => "\\x00",
                '\b' => "\\x08",
                '\f' => "\\x0c",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ when char.IsControl(value) => $"\\u{(int)value:x4}",
                _ => value
            });
        }

        return builder.ToString();
    }
}
