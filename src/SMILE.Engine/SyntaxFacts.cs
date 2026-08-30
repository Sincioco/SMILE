namespace SMILE.Engine;

internal static class SyntaxFacts
{
    public static bool IsIdentifierStart(char value) => char.IsLetter(value) || value == '_';

    public static bool IsIdentifierPart(char value) => char.IsLetterOrDigit(value) || value == '_';

    public static bool IsHorizontalWhitespace(char value) => value is ' ' or '\t' or '\f' or '\v';

    public static bool IsDoubleQuote(char value) => value == '"';

    public static bool IsAsciiLetter(char value) =>
        value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    public static bool IsAsciiUppercaseLetter(char value) => value is >= 'A' and <= 'Z';
}
