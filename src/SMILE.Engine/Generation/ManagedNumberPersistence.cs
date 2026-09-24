namespace SMILE.Engine;

internal static class ManagedNumberPersistence
{
    public static string Generate(BoundProgram program, TargetLanguage language)
    {
        var features = CoreBasicProgramFeatureSet.Create(program);
        (string path, string load, string save) = language switch
        {
            TargetLanguage.CSharp => (CSharpPath, CSharpLoad, CSharpSave),
            TargetLanguage.JavaScript => (JavaScriptPath, JavaScriptLoad, JavaScriptSave),
            TargetLanguage.Java => (JavaPath, JavaLoad, JavaSave),
            TargetLanguage.Swift => (SwiftPath, SwiftLoad, SwiftSave),
            TargetLanguage.Python => (PythonPath, PythonLoad, PythonSave),
            _ => throw new ArgumentOutOfRangeException(nameof(language))
        };
        return string.Join("\n", new[] { path, features.HasNumberLoad ? load : "", features.HasNumberSave ? save : "" }
            .Where(text => text.Length > 0)).Replace("PROGRAM_NAME", SmilePersistentStorage.Sanitize(program.ProgramName), StringComparison.Ordinal);
    }

    private const string CSharpPath = """
private static string? SmileNumberPath(string key)
{
    string? root = Environment.GetEnvironmentVariable("LOCALAPPDATA");
    if (string.IsNullOrEmpty(root)) return null;
    string folder = System.IO.Path.Combine(root, "SMILE", "Games", "PROGRAM_NAME");
    System.IO.Directory.CreateDirectory(folder);
    return System.IO.Path.Combine(folder, key);
}
""";
    private const string CSharpLoad = """
private static long SmileLoadNumber(string key, long fallback)
{
    try {
        string? path = SmileNumberPath(key);
        if (path is null) return fallback;
        using var file = System.IO.File.OpenRead(path);
        byte[] bytes = new byte[63];
        int count = file.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
        string text = System.Text.Encoding.ASCII.GetString(bytes, 0, count).Trim(' ', '\t', '\r', '\n');
        if (text.Contains('\0')) return fallback;
        return long.TryParse(text, System.Globalization.NumberStyles.AllowLeadingSign,
            System.Globalization.CultureInfo.InvariantCulture, out long value) ? value : fallback;
    } catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return fallback; }
}
""";
    private const string CSharpSave = """
private static void SmileSaveNumber(string key, long value)
{
    try {
        string? path = SmileNumberPath(key);
        if (path is not null) System.IO.File.WriteAllText(path,
            value.ToString(System.Globalization.CultureInfo.InvariantCulture), System.Text.Encoding.ASCII);
    } catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
}
""";

    private const string JavaScriptPath = """
async function smileNumberPath(key) {
    const root = process.env.LOCALAPPDATA;
    if (!root) return null;
    const folder = smilePath.join(root, "SMILE", "Games", "PROGRAM_NAME");
    await smileFileSystem.mkdir(folder, { recursive: true });
    return smilePath.join(folder, key);
}
""";
    private const string JavaScriptLoad = """
async function smileLoadNumber(key, fallback) {
    try {
        const path = await smileNumberPath(key);
        if (path === null) return fallback;
        const file = await smileFileSystem.open(path, "r");
        let text;
        try {
            const bytes = Buffer.alloc(63);
            const { bytesRead } = await file.read(bytes, 0, 63, 0);
            text = bytes.toString("latin1", 0, bytesRead).replace(/^[ \t\r\n]+|[ \t\r\n]+$/g, "");
        } finally { await file.close(); }
        if (!/^[+-]?[0-9]+$/.test(text)) return fallback;
        const value = BigInt(text);
        return value >= -9223372036854775808n && value <= 9223372036854775807n ? value : fallback;
    } catch { return fallback; }
}
""";
    private const string JavaScriptSave = """
async function smileSaveNumber(key, value) {
    try {
        const path = await smileNumberPath(key);
        if (path !== null) await smileFileSystem.writeFile(path, value.toString(), "ascii");
    } catch { }
}
""";

    private const string JavaPath = """
private static java.nio.file.Path smileNumberPath(String key) throws java.io.IOException {
    String root = System.getenv("LOCALAPPDATA");
    if (root == null || root.isEmpty()) return null;
    var folder = java.nio.file.Path.of(root, "SMILE", "Games", "PROGRAM_NAME");
    java.nio.file.Files.createDirectories(folder);
    return folder.resolve(key);
}
""";
    private const string JavaLoad = """
private static long smileLoadNumber(String key, long fallback) {
    try {
        var path = smileNumberPath(key);
        if (path == null) return fallback;
        try (var file = java.nio.file.Files.newInputStream(path)) {
            String text = new String(file.readNBytes(63), java.nio.charset.StandardCharsets.US_ASCII)
                .replaceAll("^[ \\t\\r\\n]+|[ \\t\\r\\n]+$", "");
            if (!text.matches("[+-]?[0-9]+")) return fallback;
            return Long.parseLong(text);
        }
    } catch (java.io.IOException | SecurityException | IllegalArgumentException error) { return fallback; }
}
""";
    private const string JavaSave = """
private static void smileSaveNumber(String key, long value) {
    try {
        var path = smileNumberPath(key);
        if (path != null) java.nio.file.Files.writeString(path, Long.toString(value), java.nio.charset.StandardCharsets.US_ASCII);
    } catch (java.io.IOException | SecurityException | IllegalArgumentException error) { }
}
""";

    private const string SwiftPath = """
func smileNumberPath(_ key: String) throws -> URL? {
    guard let root = ProcessInfo.processInfo.environment["LOCALAPPDATA"], !root.isEmpty else { return nil }
    let folder = URL(fileURLWithPath: root).appendingPathComponent("SMILE").appendingPathComponent("Games").appendingPathComponent("PROGRAM_NAME")
    try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
    return folder.appendingPathComponent(key)
}
""";
    private const string SwiftLoad = """
func smileLoadNumber(_ key: String, _ fallback: Int64) -> Int64 {
    do {
        guard let path = try smileNumberPath(key) else { return fallback }
        let file = try FileHandle(forReadingFrom: path)
        defer { try? file.close() }
        let bytes = try file.read(upToCount: 63) ?? Data()
        guard let text = String(data: bytes, encoding: .ascii) else { return fallback }
        let value = text.trimmingCharacters(in: CharacterSet(charactersIn: " \t\r\n"))
        return Int64(value) ?? fallback
    } catch { return fallback }
}
""";
    private const string SwiftSave = """
func smileSaveNumber(_ key: String, _ value: Int64) {
    do {
        guard let path = try smileNumberPath(key) else { return }
        try String(value).write(to: path, atomically: false, encoding: .ascii)
    } catch { }
}
""";

    private const string PythonPath = """
def smile_number_path(key):
    root = os.environ.get("LOCALAPPDATA")
    if not root:
        return None
    folder = pathlib.Path(root) / "SMILE" / "Games" / "PROGRAM_NAME"
    folder.mkdir(parents=True, exist_ok=True)
    return folder / key
""";
    private const string PythonLoad = """
def smile_load_number(key, fallback):
    try:
        path = smile_number_path(key)
        if path is None:
            return fallback
        with path.open("rb") as file:
            text = file.read(63).decode("ascii").strip(" \t\r\n")
        digits = text[1:] if text.startswith(("+", "-")) else text
        if not digits or any(character < "0" or character > "9" for character in digits):
            return fallback
        value = int(text)
        return value if -9223372036854775808 <= value <= 9223372036854775807 else fallback
    except (OSError, ValueError, UnicodeError):
        return fallback
""";
    private const string PythonSave = """
def smile_save_number(key, value):
    try:
        path = smile_number_path(key)
        if path is not None:
            path.write_text(str(value), encoding="ascii")
    except (OSError, ValueError):
        pass
""";
}
