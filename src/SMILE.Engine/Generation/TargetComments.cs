using System.Globalization;
using System.Text;

namespace SMILE.Engine;

/// <summary>
/// Renders source comments with the native marker and safety rules of the
/// destination language. Keeping this policy shared prevents one backend from
/// accidentally turning learner-authored prose into active target code.
/// </summary>
internal static class TargetComments
{
    private const int CobolMaximumCommentColumns = 200;
    private const int CobolMaximumCommentIndentColumns = 40;

    public static void Append(
        StringBuilder source,
        TargetLanguage language,
        string indent,
        string payload)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(indent);
        ArgumentNullException.ThrowIfNull(payload);

        foreach (string line in Render(language, indent, payload))
        {
            source.AppendLine(line);
        }
    }

    internal static IReadOnlyList<string> Render(
        TargetLanguage language,
        string indent,
        string payload)
    {
        if (language is TargetLanguage.Cobol && indent.Length > CobolMaximumCommentIndentColumns)
        {
            // Free-format COBOL comments have no semantic nesting. Retain the
            // useful leading portion of very deep IF indentation while
            // reserving ample source-line space for marker and payload.
            indent = indent[..CobolMaximumCommentIndentColumns];
        }

        string marker = Marker(language);
        string safePayload = RenderSafePayload(language, payload);
        string prefix = indent + marker;

        if (language is not TargetLanguage.Cobol)
        {
            return new[] { prefix + safePayload };
        }

        return WrapCobolComment(prefix, safePayload);
    }

    private static string Marker(TargetLanguage language) =>
        language switch
        {
            TargetLanguage.CSharp or
            TargetLanguage.C or
            TargetLanguage.Cpp or
            TargetLanguage.JavaScript or
            TargetLanguage.Java or
            TargetLanguage.ObjectiveC or
            TargetLanguage.Swift => "//",
            TargetLanguage.Python => "#",
            TargetLanguage.Cobol => "*>",
            TargetLanguage.MasmX64 => ";",
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, null)
        };

    private static string RenderSafePayload(TargetLanguage language, string payload)
    {
        var safe = new StringBuilder(payload.Length);

        foreach (Rune rune in payload.EnumerateRunes())
        {
            UnicodeCategory category = Rune.GetUnicodeCategory(rune);
            bool isUnsafeSeparator =
                category is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;
            bool isUnsafeControl =
                category is UnicodeCategory.Control && rune.Value != '\t';

            if (isUnsafeSeparator || isUnsafeControl)
            {
                AppendScalarEscape(safe, rune.Value, language);
            }
            else if (language is TargetLanguage.Java && rune.Value == '\\')
            {
                // Java translates Unicode escapes before it recognizes
                // comments. Encoding every learner-authored backslash is the
                // small complete defense against \u000A and repeated-u forms.
                AppendScalarEscape(safe, rune.Value, language);
            }
            else
            {
                safe.Append(rune.ToString());
            }
        }

        if (language is TargetLanguage.C or TargetLanguage.Cpp or TargetLanguage.ObjectiveC)
        {
            int finalContentIndex = safe.Length - 1;
            while (finalContentIndex >= 0 && safe[finalContentIndex] is ' ' or '\t')
            {
                finalContentIndex--;
            }

            if (finalContentIndex >= 0 && safe[finalContentIndex] == '\\')
            {
                // Translation phase 2 line splicing is defined for a final
                // backslash, and common C-family compilers also accept a
                // backslash followed only by horizontal whitespace as an
                // extension (usually with a warning). Encode that last
                // content character in place while retaining every authored
                // trailing space or tab.
                safe.Remove(finalContentIndex, 1);
                safe.Insert(finalContentIndex, "\\u{5C}");
            }
        }

        return safe.ToString();
    }

    private static void AppendScalarEscape(
        StringBuilder destination,
        int scalarValue,
        TargetLanguage language)
    {
        // A raw \u{HEX} spelling is an illegal Unicode escape to javac even
        // inside a comment. Java's valid \u005C escape produces a backslash,
        // and Unicode translation is deliberately not recursive, so the
        // resulting brace form remains readable comment text.
        destination.Append(language is TargetLanguage.Java ? "\\u005Cu{" : "\\u{")
            .Append(scalarValue.ToString("X", CultureInfo.InvariantCulture))
            .Append('}');
    }

    private static IReadOnlyList<string> WrapCobolComment(string prefix, string payload)
    {
        int prefixWidth = CobolSourceWidth(prefix, startingColumn: 0);
        int payloadBudget = Math.Max(1, CobolMaximumCommentColumns - prefixWidth);

        if (CobolSourceWidth(payload, prefixWidth) <= payloadBudget)
        {
            return new[] { prefix + payload };
        }

        var lines = new List<string>();
        var fragment = new StringBuilder();
        int fragmentWidth = 0;

        foreach (Rune rune in payload.EnumerateRunes())
        {
            string text = rune.ToString();
            int runeWidth = rune.Value == '\t'
                ? 8 - ((prefixWidth + fragmentWidth) % 8)
                : Encoding.UTF8.GetByteCount(text);
            if (fragment.Length > 0 && fragmentWidth + runeWidth > payloadBudget)
            {
                lines.Add(prefix + fragment);
                fragment.Clear();
                fragmentWidth = 0;
                runeWidth = rune.Value == '\t'
                    ? 8 - (prefixWidth % 8)
                    : Encoding.UTF8.GetByteCount(text);
            }

            fragment.Append(text);
            fragmentWidth += runeWidth;
        }

        if (fragment.Length > 0)
        {
            lines.Add(prefix + fragment);
        }

        return lines;
    }

    private static int CobolSourceWidth(string text, int startingColumn)
    {
        int column = startingColumn;
        foreach (Rune rune in text.EnumerateRunes())
        {
            column += rune.Value == '\t'
                ? 8 - (column % 8)
                : Encoding.UTF8.GetByteCount(rune.ToString());
        }

        return column - startingColumn;
    }
}
