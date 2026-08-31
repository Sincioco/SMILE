namespace SMILE.Engine;

internal sealed class GeneratedSourceLayout
{
    private readonly List<string> _lines = new();

    public bool HasContent => _lines.Any(line => line.Length > 0);

    public void WriteLine(string text, int indentation)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            EnsureBlankLines(1);
            return;
        }

        _lines.Add(new string(' ', Math.Max(0, indentation) * 4) + text.TrimEnd());
    }

    public void EnsureBlankLines(int count)
    {
        if (_lines.Count == 0)
        {
            return;
        }

        int existing = 0;
        for (int index = _lines.Count - 1; index >= 0 && _lines[index].Length == 0; index--)
        {
            existing++;
        }

        for (int index = existing; index < Math.Max(0, count); index++)
        {
            _lines.Add(string.Empty);
        }
    }

    public string Finish(TargetLanguage language) => Normalize(string.Join('\n', _lines), language);

    public static string Normalize(string source, TargetLanguage language)
    {
        ArgumentNullException.ThrowIfNull(source);
        string[] input = source
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        var output = new List<string>(input.Length);
        int blankLimit = language is TargetLanguage.Python ? 2 : 1;
        int blankCount = 0;
        foreach (string raw in input)
        {
            string line = raw.TrimEnd();
            if (line.Length == 0)
            {
                if (output.Count == 0 || blankCount >= blankLimit)
                {
                    continue;
                }

                output.Add(string.Empty);
                blankCount++;
                continue;
            }

            if (language is TargetLanguage.Python &&
                (line.StartsWith("def ", StringComparison.Ordinal) ||
                 line.StartsWith("class ", StringComparison.Ordinal)))
            {
                while (output.Count > 0 && blankCount < 2)
                {
                    output.Add(string.Empty);
                    blankCount++;
                }
            }

            output.Add(line);
            blankCount = 0;
        }

        while (output.Count > 0 && output[^1].Length == 0)
        {
            output.RemoveAt(output.Count - 1);
        }

        return string.Join('\n', output) + "\n";
    }
}
