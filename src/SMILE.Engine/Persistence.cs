using System.Globalization;
using System.Text;

namespace SMILE.Engine;

// Each program owns one storage directory. Tests and embedded evaluators can
// select their own root without changing process-wide environment variables.
public sealed partial class SmilePersistentStorage(string programName = "Program", string? storageRoot = null)
{
    private readonly string? _root = storageRoot ?? Environment.GetEnvironmentVariable("LOCALAPPDATA");
    private readonly string _programName = programName;

    public long LoadNumber(string key, long defaultValue)
    {
        try
        {
            string? path = NumberPath(key);
            if (path is null) return defaultValue;
            using FileStream stream = File.OpenRead(path);
            byte[] bytes = new byte[63];
            int count = stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
            string value = Encoding.ASCII.GetString(bytes, 0, count).Trim(' ', '\t', '\r', '\n');
            if (value.Contains('\0')) return defaultValue;
            return long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long number)
                ? number : defaultValue;
        }
        catch (Exception error) when (IsStorageError(error)) { return defaultValue; }
    }

    public void SaveNumber(string key, long value)
    {
        try
        {
            string? path = NumberPath(key);
            if (path is not null) File.WriteAllText(path, value.ToString(CultureInfo.InvariantCulture), Encoding.ASCII);
        }
        catch (Exception error) when (IsStorageError(error)) { }
    }

    private string? NumberPath(string key)
    {
        if (string.IsNullOrEmpty(_root)) return null;
        string folder = Path.Combine(_root, "SMILE", "Games", Sanitize(_programName));
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, Sanitize(key) + ".txt");
    }

    internal static string Sanitize(string text)
    {
        string name = new(text.TakeWhile(character => character != '\0').Take(255)
            .Select(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' ? character : '_').ToArray());
        return name.Length == 0 ? "_" : name;
    }

    private static bool IsStorageError(Exception error) =>
        error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
}
