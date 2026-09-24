namespace SMILE.Engine;

internal static class ManagedTextFileSupport
{
    public static string Definition(TargetLanguage language) => language switch
    {
        TargetLanguage.CSharp => """
private static long SmileLoadTextFile(string path, long[] destination)
{
    Array.Clear(destination);
    if (path.Length == 0 || path[0] is '/' or '\\' || path.Contains(':') || path.Contains('\0')) return 0;
    var parts = new System.Collections.Generic.List<string>();
    foreach (string part in path.Replace('\\', '/').Split('/'))
    {
        if (part is "" or ".") continue;
        if (part == "..") { if (parts.Count == 0) return 0; parts.RemoveAt(parts.Count - 1); }
        else { if (parts.Count == 512) return 0; parts.Add(part); }
    }
    path = string.Join('/', parts);
    if (path.Length == 0 || System.Text.Encoding.UTF8.GetByteCount(path) >= 4096) return 0;
    try
    {
        using var file = System.IO.File.OpenRead(System.IO.Path.Combine(AppContext.BaseDirectory, path));
        byte[] buffer = new byte[4096];
        int read = file.ReadAtLeast(buffer.AsSpan(0, 3), 3, throwOnEndOfStream: false);
        int start = read == 3 && buffer[0] == 239 && buffer[1] == 187 && buffer[2] == 191 ? 3 : 0;
        int copied = 0;
        while (true)
        {
            for (int index = start; index < read && copied < destination.Length; index++) destination[copied++] = buffer[index];
            if (copied == destination.Length || (read = file.Read(buffer)) == 0) return copied;
            start = 0;
        }
    }
    catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
    {
        Array.Clear(destination);
        return 0;
    }
}
""",
        TargetLanguage.Java => """
private static long smileLoadTextFile(String path, long[] destination) {
    java.util.Arrays.fill(destination, 0);
    if (path.isEmpty() || path.charAt(0) == '/' || path.charAt(0) == '\\' || path.indexOf(':') >= 0 || path.indexOf(0) >= 0) return 0;
    var parts = new java.util.ArrayList<String>();
    for (String part : path.replace('\\', '/').split("/")) {
        if (part.isEmpty() || part.equals(".")) continue;
        if (part.equals("..")) { if (parts.isEmpty()) return 0; parts.remove(parts.size() - 1); }
        else { if (parts.size() == 512) return 0; parts.add(part); }
    }
    path = String.join("/", parts);
    if (path.isEmpty() || path.getBytes(java.nio.charset.StandardCharsets.UTF_8).length >= 4096) return 0;
    try {
        var base = java.nio.file.Path.of(Program.class.getProtectionDomain().getCodeSource().getLocation().toURI());
        try (var file = new java.io.BufferedInputStream(java.nio.file.Files.newInputStream(base.resolve(path)))) {
            byte[] prefix = file.readNBytes(3);
            int start = prefix.length == 3 && (prefix[0] & 255) == 239 && (prefix[1] & 255) == 187 && (prefix[2] & 255) == 191 ? 3 : 0;
            int copied = 0;
            for (int index = start; index < prefix.length && copied < destination.length; index++) destination[copied++] = prefix[index] & 255;
            while (copied < destination.length) {
                int value = file.read();
                if (value < 0) break;
                destination[copied++] = value;
            }
            return copied;
        }
    } catch (java.io.IOException | java.net.URISyntaxException | IllegalArgumentException | SecurityException error) {
        java.util.Arrays.fill(destination, 0);
        return 0;
    }
}
""",
        TargetLanguage.JavaScript => """
async function smileLoadTextFile(path, destination) {
    destination.fill(0n);
    if (!path || /^[\\/]/.test(path) || path.includes(":") || path.includes("\0")) return 0n;
    const parts = [];
    for (const part of path.replaceAll("\\", "/").split("/")) {
        if (!part || part === ".") continue;
        if (part === "..") { if (!parts.length) return 0n; parts.pop(); }
        else { if (parts.length === 512) return 0n; parts.push(part); }
    }
    path = parts.join("/");
    if (!path || Buffer.byteLength(path, "utf8") >= 4096) return 0n;
    let file;
    try {
        file = await smileFileSystem.open(smilePath.join(__dirname, path), "r");
        const buffer = Buffer.alloc(4096);
        let { bytesRead } = await file.read(buffer, 0, 3, null);
        let start = bytesRead === 3 && buffer[0] === 239 && buffer[1] === 187 && buffer[2] === 191 ? 3 : 0;
        let copied = 0;
        while (true) {
            for (let index = start; index < bytesRead && copied < destination.length; index++) destination[copied++] = BigInt(buffer[index]);
            if (copied === destination.length) return BigInt(copied);
            ({ bytesRead } = await file.read(buffer, 0, buffer.length, null));
            if (bytesRead === 0) return BigInt(copied);
            start = 0;
        }
    } catch {
        destination.fill(0n);
        return 0n;
    } finally {
        if (file) await file.close();
    }
}
""",
        TargetLanguage.Python => """
def smile_load_text_file(path, destination):
    destination[:] = [0] * len(destination)
    if not path or path[0] in '/\\' or ':' in path or '\0' in path:
        return 0
    parts = []
    for part in path.replace('\\', '/').split('/'):
        if part in ('', '.'):
            continue
        if part == '..':
            if not parts:
                return 0
            parts.pop()
        else:
            if len(parts) == 512:
                return 0
            parts.append(part)
    path = '/'.join(parts)
    if not path or len(path.encode('utf-8')) >= 4096:
        return 0
    try:
        with (pathlib.Path(__file__).parent / path).open('rb') as file:
            data = file.read(len(destination) + 3)
        if data.startswith(b'\xef\xbb\xbf'):
            data = data[3:]
        data = data[:len(destination)]
        destination[:len(data)] = data
        return len(data)
    except (OSError, ValueError):
        destination[:] = [0] * len(destination)
        return 0
""",
        TargetLanguage.Swift => """
func smileLoadTextFile(_ path: String, _ destination: inout [Int64]) -> Int64 {
    destination = Array(repeating: 0, count: destination.count)
    if path.isEmpty || path.hasPrefix("/") || path.hasPrefix("\\") || path.contains(":") || path.contains("\0") { return 0 }
    var parts: [String] = []
    for part in path.replacingOccurrences(of: "\\", with: "/").split(separator: "/") {
        if part == "." { continue }
        if part == ".." { if parts.isEmpty { return 0 }; parts.removeLast() }
        else { if parts.count == 512 { return 0 }; parts.append(String(part)) }
    }
    let relative = parts.joined(separator: "/")
    if relative.isEmpty || relative.utf8.count >= 4096 { return 0 }
    do {
        let base = URL(fileURLWithPath: CommandLine.arguments[0]).deletingLastPathComponent()
        let file = try FileHandle(forReadingFrom: base.appendingPathComponent(relative))
        defer { try? file.close() }
        var buffer = Array(try file.read(upToCount: 3) ?? Data())
        var start = buffer == [239, 187, 191] ? 3 : 0
        var copied = 0
        while true {
            for index in start..<buffer.count {
                if copied == destination.count { return Int64(copied) }
                destination[copied] = Int64(buffer[index])
                copied += 1
            }
            if copied == destination.count { return Int64(copied) }
            buffer = Array(try file.read(upToCount: 4096) ?? Data())
            if buffer.isEmpty { return Int64(copied) }
            start = 0
        }
    } catch {
        destination = Array(repeating: 0, count: destination.count)
        return 0
    }
}
""",
        _ => throw new ArgumentOutOfRangeException(nameof(language))
    };
}
