namespace SMILE.Engine;

internal static class TextOutput
{
    public static string EnsureOneTrailingNewLine(string text)
    {
        string normalized = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Replace("\n", Environment.NewLine, StringComparison.Ordinal);

        // Most generators end with target-owned closing syntax, but
        // JavaScript and Swift can end directly with learner-authored layout.
        // Adding a terminator only when one is absent preserves every explicit
        // trailing blank source line instead of trimming it away.
        return normalized.EndsWith(Environment.NewLine, StringComparison.Ordinal)
            ? normalized
            : normalized + Environment.NewLine;
    }

    public static string EnsureOneTrailingNewLinePreservingExistingLineEndings(string text)
    {
        // Native multiline literals contain physical LF characters that are part
        // of the runtime String value.  Once a generator has deliberately mixed
        // those semantic LFs with its normal target-file line endings, a whole-file
        // normalization pass can no longer distinguish data from formatting.
        return text.EndsWith("\r\n", StringComparison.Ordinal) ||
            text.EndsWith('\n') ||
            text.EndsWith('\r')
                ? text
                : text + Environment.NewLine;
    }
}
