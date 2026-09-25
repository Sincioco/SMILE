using System.Text;

namespace SMILE.Engine;

// Compile-time key metadata. Targets emit ordinary switches, not a runtime table
// or interpreter. Character fallbacks also handle synthetic console events.
internal static class ConsoleKeyMap
{
    public static IReadOnlyList<(int Code, int[] Virtual, string Characters)> Keys { get; } =
    [
        (1, [87], "wW"), (2, [65], "aA"), (3, [83], "sS"), (4, [68], "dD"),
        (27, [79], "oO"), (28, [70], "fF"), (29, [71], "gG"), (30, [82], "rR"),
        (31, [80], "pP"), (32, [66], "bB"), (35, [88], "xX"), (36, [89], "yY"),
        (37, [90], "zZ"), (38, [69], "eE"), (41, [67], "cC"),
        (39, [187, 107], "+="), (40, [189, 109], "-_"),
        (10, [38], ""), (11, [40], ""), (12, [37], ""), (13, [39], ""),
        (14, [13], "\r\n"), (15, [27], "\u001b"), (16, [32], " "),
        (33, [17], ""), (34, [192], "`~"), (17, [49, 97], "1"),
        (18, [50, 98], "2"), (20, [51, 99], "3"), (22, [52, 100], "4"), (21, [9], "\t")
    ];

    public static string NativeFunction(string declaration)
    {
        var text = new StringBuilder();
        text.AppendLine(declaration);
        text.AppendLine("{");
        text.AppendLine("    HANDLE input = GetStdHandle(STD_INPUT_HANDLE);");
        text.AppendLine("    INPUT_RECORD record;");
        text.AppendLine("    DWORD available;");
        text.AppendLine("    while (PeekConsoleInputW(input, &record, 1, &available) && available != 0)");
        text.AppendLine("    {");
        text.AppendLine("        if (!ReadConsoleInputW(input, &record, 1, &available) || available == 0) return 0;");
        text.AppendLine("        if (record.EventType != KEY_EVENT || !record.Event.KeyEvent.bKeyDown) continue;");
        text.AppendLine("        switch (record.Event.KeyEvent.wVirtualKeyCode)");
        text.AppendLine("        {");
        foreach (var key in Keys)
            text.AppendLine($"            {string.Join(" ", key.Virtual.Select(value => $"case {value}:"))} return {key.Code};");
        text.AppendLine("        }");
        text.AppendLine("        switch (record.Event.KeyEvent.uChar.UnicodeChar)");
        text.AppendLine("        {");
        foreach (var key in Keys.Where(key => key.Characters.Length > 0))
            text.AppendLine($"            {string.Join(" ", key.Characters.Select(value => $"case {(int)value}:"))} return {key.Code};");
        text.AppendLine("        }");
        text.AppendLine("        if (record.Event.KeyEvent.wVirtualKeyCode || record.Event.KeyEvent.uChar.UnicodeChar) return 19;");
        text.AppendLine("    }");
        text.AppendLine("    return 0;");
        text.AppendLine("}");
        return text.ToString();
    }
}
