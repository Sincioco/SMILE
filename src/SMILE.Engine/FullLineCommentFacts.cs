namespace SMILE.Engine;

// Full-line comments are contextual source-line syntax rather than global
// keywords. Keeping their one-line recognition here gives the indexed parser,
// public lexer, and defensive IF recovery exactly the same boundary rules.
internal static class FullLineCommentFacts
{
    public static bool TryClassify(
        string physicalLine,
        int firstNonWhitespace,
        out FullLineCommentMarker marker,
        out int payloadStart)
    {
        marker = default;
        payloadStart = 0;

        if ((uint)firstNonWhitespace >= (uint)physicalLine.Length)
        {
            return false;
        }

        if (physicalLine.AsSpan(firstNonWhitespace).StartsWith("//", StringComparison.Ordinal))
        {
            marker = FullLineCommentMarker.SlashSlash;
            payloadStart = firstNonWhitespace + 2;
            return true;
        }

        if (physicalLine[firstNonWhitespace] == '#')
        {
            marker = FullLineCommentMarker.Hash;
            payloadStart = firstNonWhitespace + 1;
            return true;
        }

        if (physicalLine.AsSpan(firstNonWhitespace).StartsWith("--", StringComparison.Ordinal))
        {
            marker = FullLineCommentMarker.DashDash;
            payloadStart = firstNonWhitespace + 2;
            return true;
        }

        const int remLength = 3;
        if (physicalLine.Length - firstNonWhitespace < remLength ||
            !physicalLine.AsSpan(firstNonWhitespace, remLength).Equals(
                "REM",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int afterRem = firstNonWhitespace + remLength;
        if (afterRem < physicalLine.Length &&
            !SyntaxFacts.IsHorizontalWhitespace(physicalLine[afterRem]))
        {
            return false;
        }

        marker = FullLineCommentMarker.Rem;
        payloadStart = afterRem;
        return true;
    }
}
