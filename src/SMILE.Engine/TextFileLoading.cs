using System.Text;

namespace SMILE.Engine;

// The evaluator's file boundary is injectable. The caller owns and disposes the
// returned stream; the host receives only an already-normalized relative path.
public interface ISmileFileHost
{
    Stream? OpenRead(string relativePath);
}

public sealed class SmileDirectoryFileHost(string baseDirectory) : ISmileFileHost
{
    public Stream? OpenRead(string relativePath) => File.OpenRead(Path.Combine(baseDirectory, relativePath));
}

internal static class TextFileLoading
{
    public static string? CanonicalPath(string path)
    {
        if (path.Length == 0 || path[0] is '/' or '\\' || path.Contains(':') || path.Contains('\0')) return null;
        var parts = new List<string>();
        foreach (string part in path.Replace('\\', '/').Split('/'))
        {
            if (part is "" or ".") continue;
            if (part == "..")
            {
                if (parts.Count == 0) return null;
                parts.RemoveAt(parts.Count - 1);
            }
            else
            {
                if (parts.Count == 512) return null;
                parts.Add(part);
            }
        }
        string result = string.Join('/', parts);
        return result.Length > 0 && Encoding.UTF8.GetByteCount(result) < 4096 ? result : null;
    }

    public static long Load(string path, SmileValue[] destination, ISmileFileHost host)
    {
        Array.Fill(destination, SmileValue.FromInteger(0));
        string? canonical = CanonicalPath(path);
        if (canonical is null) return 0;
        try
        {
            using Stream? stream = host.OpenRead(canonical);
            if (stream is null) return 0;
            byte[] buffer = new byte[4096];
            int read = stream.ReadAtLeast(buffer.AsSpan(0, 3), 3, throwOnEndOfStream: false);
            int start = read == 3 && buffer[0] == 239 && buffer[1] == 187 && buffer[2] == 191 ? 3 : 0;
            int copied = 0;
            while (true)
            {
                for (int index = start; index < read && copied < destination.Length; index++)
                    destination[copied++] = SmileValue.FromInteger(buffer[index]);
                if (copied == destination.Length || (read = stream.Read(buffer)) == 0) return copied;
                start = 0;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Array.Fill(destination, SmileValue.FromInteger(0));
            return 0;
        }
    }
}
