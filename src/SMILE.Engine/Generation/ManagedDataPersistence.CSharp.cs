namespace SMILE.Engine;

internal static partial class ManagedDataPersistence
{
    private const string CSharpCommon = """
private static string? SmileDataPath(string key)
{
    byte[] bytes = System.Text.Encoding.UTF8.GetBytes(key);
    string? root = Environment.GetEnvironmentVariable("LOCALAPPDATA");
    if (string.IsNullOrEmpty(root) || bytes.Length > 1048576) return null;
    string folder = System.IO.Path.Combine(root, "SMILE", "Games", "APPLICATION_HASH", "Data");
    System.IO.Directory.CreateDirectory(folder);
    return System.IO.Path.Combine(folder, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)) + ".bin");
}

private static (byte[]? Bytes, long Status) SmileReadData(string path, int capacity)
{
    try {
        using var file = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read);
        long size = file.Length;
        if (size < 44 || size > 1048620) return (null, 5);
        byte[] envelope = new byte[(int)size];
        file.ReadExactly(envelope);
        if (!envelope.AsSpan(0, 4).SequenceEqual("SMD4"u8) ||
            System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(envelope.AsSpan(4)) != 1 ||
            System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(envelope.AsSpan(8)) != size - 44 ||
            !System.Security.Cryptography.SHA256.HashData(envelope.AsSpan(44)).AsSpan().SequenceEqual(envelope.AsSpan(12, 32))) return (null, 5);
        if (size - 44 > capacity) return (null, 6);
        return (envelope[44..], 0);
    }
    catch (Exception error) when (error is System.IO.FileNotFoundException or System.IO.DirectoryNotFoundException) { return (null, 1); }
    catch (Exception error) when (SmileDataIoError(error)) { return (null, 4); }
}

private static bool SmileDataIoError(Exception error) => error is System.IO.IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
private static void SmileDataFail(string message) { Console.Out.Flush(); Console.Error.WriteLine(message); Environment.Exit(2); }
""";

    private const string CSharpLoad = """
private static (long Count, long Status) SmileLoadData(string key, long[] destination, bool recover)
{
    long count = 0, status = 3;
    if (destination.Length <= 1048576) {
        if (!recover) Array.Clear(destination);
        try {
            string? path = SmileDataPath(key);
            (byte[]? bytes, status) = path is null ? (null, 4L) : SmileReadData(path, destination.Length);
            if (path is not null && recover && status is 1 or 5) {
                var backup = SmileReadData(path + ".bak", destination.Length);
                if (backup.Status == 0) { bytes = backup.Bytes; status = 2; }
                else if (backup.Status != 1) status = backup.Status;
            }
            if (bytes is not null) {
                for (int index = 0; index < bytes.Length; index++) destination[index] = bytes[index];
                count = bytes.Length;
            }
        } catch (Exception error) when (SmileDataIoError(error)) { status = 4; }
    }
    if (!recover && status is not (0 or 1)) SmileDataFail("Load Data encountered an invalid destination, corrupt block, oversized block, or unavailable storage.");
    return (count, status);
}
""";

    private const string CSharpSave = """
private static long SmileSaveDataCore(long[] source, long count, string key, bool recover)
{
    if (source.Length > 1048576 || count < 0 || count > source.Length) return 3;
    byte[] bytes = new byte[44 + (int)count];
    for (int index = 0; index < count; index++) {
        if (source[index] is < 0 or > 255) return 3;
        bytes[44 + index] = (byte)source[index];
    }
    "SMD4"u8.CopyTo(bytes);
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 1);
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)count);
    System.Security.Cryptography.SHA256.HashData(bytes.AsSpan(44), bytes.AsSpan(12, 32));
    string? temporary = null;
    bool owned = false;
    try {
        string? path = SmileDataPath(key);
        if (path is null) return 4;
        temporary = path + ".tmp." + Environment.ProcessId + "." + Guid.NewGuid().ToString("N");
        using (var file = new System.IO.FileStream(temporary, System.IO.FileMode.CreateNew, System.IO.FileAccess.Write, System.IO.FileShare.None)) {
            owned = true; file.Write(bytes); file.Flush(true);
        }
        if (System.IO.File.Exists(path)) {
            long status = SmileReadData(path, 1048576).Status;
            string? backup = path + ".bak";
            if (status != 0) {
                if (!recover || status != 5) return status;
                status = SmileReadData(backup, 1048576).Status;
                if (status != 0) return status == 1 ? 4 : status;
                backup = null;
            }
            System.IO.File.Replace(temporary, path, backup);
        } else System.IO.File.Move(temporary, path);
        owned = false;
        return 0;
    } catch (Exception error) when (SmileDataIoError(error)) { return 4; }
    finally { if (owned) try { System.IO.File.Delete(temporary!); } catch (Exception error) when (SmileDataIoError(error)) { } }
}

private static long SmileSaveData(long[] source, long count, string key, bool recover)
{
    long status = SmileSaveDataCore(source, count, key, recover);
    if (!recover && status != 0) SmileDataFail("Save Data received invalid bytes/count or could not atomically store the block.");
    return status;
}
""";
}
