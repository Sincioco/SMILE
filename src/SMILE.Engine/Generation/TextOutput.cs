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
}
