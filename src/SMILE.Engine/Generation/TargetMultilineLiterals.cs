using System.Text;

namespace SMILE.Engine;

/// <summary>
/// Renders direct semantic String literals with target-native multiline syntax.
/// The bound tree has already normalized SMILE line boundaries to LF, so these
/// helpers deliberately write literal-internal line feeds as <c>\n</c> instead
/// of using the host platform's newline.
/// </summary>
internal static class TargetMultilineLiterals
{
    public static bool TryCSharp(string value, string structuralIndent, out string literal)
    {
        if (!IsSafeNativeMultilineValue(value))
        {
            literal = string.Empty;
            return false;
        }

        int delimiterLength = Math.Max(3, MaximumRun(value, '"') + 1);
        string delimiter = new('"', delimiterLength);
        literal = RenderStructurallyIndented(value, structuralIndent, delimiter, delimiter);
        return true;
    }

    public static bool TryJavaScript(string value, out string literal)
    {
        if (!IsSafeNativeMultilineValue(value))
        {
            literal = string.Empty;
            return false;
        }

        var content = new StringBuilder(value.Length);
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            content.Append(character switch
            {
                '\\' => "\\\\",
                '`' => "\\`",
                '$' when index + 1 < value.Length && value[index + 1] == '{' => "\\$",
                '\t' => "\\t",
                _ => character
            });
        }

        literal = "`" + content + "`";
        return true;
    }

    public static string Java(string value, string structuralIndent)
    {
        if (!IsSafeNativeMultilineValue(value))
        {
            return RenderJavaAdjacentLiterals(value, structuralIndent);
        }

        string[] lines = value.Split('\n');
        bool hasTerminalLineFeed = value.EndsWith('\n');
        int contentLineCount = hasTerminalLineFeed ? lines.Length - 1 : lines.Length;
        var literal = new StringBuilder(value.Length + (contentLineCount + 2) * structuralIndent.Length);
        literal.Append("\"\"\"").Append('\n');

        for (int index = 0; index < contentLineCount; index++)
        {
            literal.Append(structuralIndent);
            AppendJavaTextBlockLine(literal, lines[index]);
            if (!hasTerminalLineFeed && index == contentLineCount - 1)
            {
                // A Java text block normally contributes the physical newline
                // before its closing delimiter. Line continuation suppresses it
                // when the semantic SMILE value has no terminal LF.
                literal.Append('\\');
            }

            literal.Append('\n');
        }

        literal.Append(structuralIndent).Append("\"\"\"");
        return literal.ToString();
    }

    public static bool TrySwift(string value, string structuralIndent, out string literal)
    {
        if (!IsSafeNativeMultilineValue(value))
        {
            literal = string.Empty;
            return false;
        }

        string hashes = string.Empty;
        if (value.Contains('\\') || value.Contains("\"\"\"", StringComparison.Ordinal))
        {
            bool found = false;
            for (int count = 1; count <= 16; count++)
            {
                string candidate = new('#', count);
                if (!value.Contains("\"\"\"" + candidate, StringComparison.Ordinal) &&
                    !value.Contains("\\" + candidate, StringComparison.Ordinal))
                {
                    hashes = candidate;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                literal = string.Empty;
                return false;
            }
        }

        literal = RenderStructurallyIndented(
            value,
            structuralIndent,
            hashes + "\"\"\"",
            "\"\"\"" + hashes);
        return true;
    }

    public static string Python(string value, string structuralIndent)
    {
        if (IsSafeNativeMultilineValue(value))
        {
            string? delimiter = !value.Contains("\"\"\"", StringComparison.Ordinal)
                ? "\"\"\""
                : !value.Contains("'''", StringComparison.Ordinal)
                    ? "'''"
                    : null;
            if (delimiter is not null)
            {
                char quote = delimiter[0];
                var content = new StringBuilder(value.Length);
                foreach (char character in value)
                {
                    content.Append(character switch
                    {
                        '\\' => "\\\\",
                        '\t' => "\\t",
                        _ when character == quote => "\\" + character,
                        _ => character
                    });
                }

                return delimiter + content + delimiter;
            }
        }

        return RenderPythonAdjacentLiterals(value, structuralIndent);
    }

    public static bool TryCpp(string value, out string literal)
    {
        if (!IsSafeNativeMultilineValue(value))
        {
            literal = string.Empty;
            return false;
        }

        for (int suffix = 0; suffix < 100_000; suffix++)
        {
            string delimiter = suffix == 0 ? "SMILE" : "SMILE" + suffix;
            if (delimiter.Length > 16)
            {
                break;
            }

            if (!value.Contains(")" + delimiter + "\"", StringComparison.Ordinal))
            {
                literal = "R\"" + delimiter + "(" + value + ")" + delimiter + "\"";
                return true;
            }
        }

        literal = string.Empty;
        return false;
    }

    private static string RenderStructurallyIndented(
        string value,
        string structuralIndent,
        string openingDelimiter,
        string closingDelimiter)
    {
        string[] lines = value.Split('\n');
        var literal = new StringBuilder(value.Length + (lines.Length + 2) * structuralIndent.Length);
        literal.Append(openingDelimiter).Append('\n');
        foreach (string line in lines)
        {
            // Prefixing the exact closing-delimiter margin makes target
            // indentation structural. Spaces and tabs that belong to the
            // semantic value follow that prefix and therefore remain data.
            literal.Append(structuralIndent).Append(line).Append('\n');
        }

        literal.Append(structuralIndent).Append(closingDelimiter);
        return literal.ToString();
    }

    private static void AppendJavaTextBlockLine(StringBuilder literal, string line)
    {
        int trailingSpaceStart = line.Length;
        while (trailingSpaceStart > 0 && line[trailingSpaceStart - 1] == ' ')
        {
            trailingSpaceStart--;
        }

        for (int index = 0; index < line.Length; index++)
        {
            char character = line[index];
            if (character == ' ' && index >= trailingSpaceStart)
            {
                // javac removes incidental trailing whitespace before escape
                // processing. \s recreates each intentional trailing space.
                literal.Append("\\s");
                continue;
            }

            literal.Append(character switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\t' => "\\t",
                _ => character
            });
        }
    }

    private static string RenderJavaAdjacentLiterals(string value, string structuralIndent)
    {
        string[] lines = value.Split('\n');
        var fragments = new List<string>(lines.Length);
        for (int index = 0; index < lines.Length; index++)
        {
            string fragment = lines[index] + (index < lines.Length - 1 ? "\n" : string.Empty);
            if (index == lines.Length - 1 && fragment.Length == 0 && fragments.Count > 0)
            {
                continue;
            }

            fragments.Add(TargetEscapes.JavaString(fragment));
        }

        if (fragments.Count == 0)
        {
            return TargetEscapes.JavaString(string.Empty);
        }

        string continuation = "\n" + structuralIndent + "    + ";
        return string.Join(continuation, fragments);
    }

    private static string RenderPythonAdjacentLiterals(string value, string structuralIndent)
    {
        string[] lines = value.Split('\n');
        var fragments = new List<string>(lines.Length);
        for (int index = 0; index < lines.Length; index++)
        {
            string fragment = lines[index] + (index < lines.Length - 1 ? "\n" : string.Empty);
            if (index == lines.Length - 1 && fragment.Length == 0 && fragments.Count > 0)
            {
                continue;
            }

            fragments.Add(TargetEscapes.PythonString(fragment));
        }

        if (fragments.Count == 0)
        {
            return TargetEscapes.PythonString(string.Empty);
        }

        var literal = new StringBuilder();
        literal.Append('(').Append('\n');
        foreach (string fragment in fragments)
        {
            literal.Append(structuralIndent).Append("    ").Append(fragment).Append('\n');
        }

        literal.Append(structuralIndent).Append(')');
        return literal.ToString();
    }

    private static bool IsSafeNativeMultilineValue(string value) =>
        value.Contains('\n') && value.All(character =>
            character is '\n' or '\t' ||
            (!char.IsControl(character) && character is not '\u2028' and not '\u2029'));

    private static int MaximumRun(string value, char character)
    {
        int maximum = 0;
        int current = 0;
        foreach (char candidate in value)
        {
            if (candidate == character)
            {
                maximum = Math.Max(maximum, ++current);
            }
            else
            {
                current = 0;
            }
        }

        return maximum;
    }
}
