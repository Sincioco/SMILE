namespace SMILE.Engine;

// Parser header scans and the public tooling lexer both need to cross a whole
// interpolated String without mistaking its nested quotes or braces for
// physical source structure. The bounded expression parser still performs the
// authoritative syntax/escape validation after this shared linear scan.
internal static class InterpolatedStringScanner
{
    public static bool IsStart(string text, int position) =>
        position + 1 < text.Length &&
        text[position] == '$' &&
        SyntaxFacts.IsDoubleQuote(text[position + 1]);

    public static int Skip(string text, int start, int end)
    {
        int position = start + 2;
        while (position < end)
        {
            if (text[position] == '\\')
            {
                position += position + 1 < end ? 2 : 1;
                continue;
            }

            if (text[position] == '{')
            {
                if (position + 1 < end && text[position + 1] == '{')
                {
                    position += 2;
                    continue;
                }

                int close = FindInterpolationClose(text, position + 1, end);
                position = close < 0 ? end : close + 1;
                continue;
            }

            if (text[position] == '}' && position + 1 < end && text[position + 1] == '}')
            {
                position += 2;
                continue;
            }

            if (SyntaxFacts.IsDoubleQuote(text[position]))
            {
                return position + 1;
            }

            position++;
        }

        return end;
    }

    public static int FindInterpolationClose(string text, int start, int end)
    {
        int position = start;
        while (position < end)
        {
            if (IsStart(text, position))
            {
                position = Skip(text, position, end);
                continue;
            }

            if (SyntaxFacts.IsDoubleQuote(text[position]))
            {
                position = SkipQuotedText(text, position + 1, end);
                continue;
            }

            if (text[position] == '}')
            {
                return position;
            }

            position++;
        }

        return -1;
    }

    private static int SkipQuotedText(string text, int start, int end)
    {
        int position = start;
        while (position < end)
        {
            if (text[position] == '\\')
            {
                position += position + 1 < end ? 2 : 1;
                continue;
            }

            if (SyntaxFacts.IsDoubleQuote(text[position]))
            {
                return position + 1;
            }

            position++;
        }

        return end;
    }
}
