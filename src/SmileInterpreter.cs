using System;

namespace SMILE
{
    public sealed class SmileInterpreter
    {
        public string TranslateToCSharp(string sourceLine)
        {
            ParsedCommand command = Parse(sourceLine);

            if (command.Kind == SmileCommandKind.Print)
            {
                return "Console.WriteLine(\"" + EscapeForCSharp(command.Value) + "\");";
            }

            throw new SmileInterpreterException("Unknown command. Supported command: Print");
        }

        public void Execute(string sourceLine)
        {
            ParsedCommand command = Parse(sourceLine);

            if (command.Kind == SmileCommandKind.Print)
            {
                Console.WriteLine(command.Value);
                return;
            }

            throw new SmileInterpreterException("Unknown command. Supported command: Print");
        }

        private static ParsedCommand Parse(string sourceLine)
        {
            if (string.IsNullOrWhiteSpace(sourceLine))
            {
                throw new SmileInterpreterException("No command was entered.");
            }

            string line = NormalizeQuotes(sourceLine).Trim();

            if (StartsWithKeyword(line, "Print"))
            {
                string value = GetTextAfterKeyword(line, "Print");
                string text = ParseQuotedText(value);

                return new ParsedCommand(SmileCommandKind.Print, text);
            }

            throw new SmileInterpreterException("Unknown command. Supported command: Print");
        }

        private static bool StartsWithKeyword(string line, string keyword)
        {
            if (!line.StartsWith(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (line.Length == keyword.Length)
            {
                return true;
            }

            return char.IsWhiteSpace(line[keyword.Length]);
        }

        private static string GetTextAfterKeyword(string line, string keyword)
        {
            if (line.Length <= keyword.Length)
            {
                throw new SmileInterpreterException(keyword + " requires a value.");
            }

            return line.Substring(keyword.Length).Trim();
        }

        private static string ParseQuotedText(string value)
        {
            value = value.Trim();

            if (value.Length < 2)
            {
                throw new SmileInterpreterException("Expected quoted text, example: Print \"Hello World\"");
            }

            if (value[0] != '"' || value[value.Length - 1] != '"')
            {
                throw new SmileInterpreterException("Expected quoted text, example: Print \"Hello World\"");
            }

            return value.Substring(1, value.Length - 2);
        }

        private static string NormalizeQuotes(string text)
        {
            return text
                .Replace('\u201c', '"')
                .Replace('\u201d', '"')
                .Replace('\u2018', '\'')
                .Replace('\u2019', '\'')
                .Replace("\u00e2\u20ac\u0153", "\"")
                .Replace("\u00e2\u20ac\u009d", "\"")
                .Replace("\u00e2\u20ac\u02dc", "'")
                .Replace("\u00e2\u20ac\u2122", "'");
        }

        private static string EscapeForCSharp(string text)
        {
            return text
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"");
        }

        private readonly struct ParsedCommand
        {
            public ParsedCommand(SmileCommandKind kind, string value)
            {
                Kind = kind;
                Value = value;
            }

            public SmileCommandKind Kind { get; }

            public string Value { get; }
        }

        private enum SmileCommandKind
        {
            Print
        }
    }

    public sealed class SmileInterpreterException : Exception
    {
        public SmileInterpreterException(string message)
            : base(message)
        {
        }
    }
}
