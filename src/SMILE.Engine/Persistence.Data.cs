using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace SMILE.Engine;

public enum SmileDataStatus { Ok, Missing, Recovered, Invalid, Unavailable, Corrupt, TooLarge }
public readonly record struct SmileDataResult(long Count, SmileDataStatus Status);

public sealed partial class SmilePersistentStorage
{
    public const int MaximumDataBytes = 1024 * 1024;

    public SmileDataResult LoadData(string key, long[] destination, bool recover = true)
    {
        if (destination.Length > MaximumDataBytes) return new(0, SmileDataStatus.Invalid);
        if (!recover) Array.Clear(destination);
        try
        {
            string? path = DataPath(key);
            if (path is null) return new(0, SmileDataStatus.Unavailable);
            (byte[]? payload, SmileDataStatus status) = ReadData(path, destination.Length);
            if (recover && status is SmileDataStatus.Missing or SmileDataStatus.Corrupt)
            {
                (byte[]? backup, SmileDataStatus backupStatus) = ReadData(path + ".bak", destination.Length);
                if (backupStatus == SmileDataStatus.Ok) { payload = backup; status = SmileDataStatus.Recovered; }
                else if (backupStatus != SmileDataStatus.Missing) status = backupStatus;
            }
            if (payload is null) return new(0, status);
            for (int index = 0; index < payload.Length; index++) destination[index] = payload[index];
            return new(payload.Length, status);
        }
        catch (Exception error) when (IsStorageError(error)) { return new(0, SmileDataStatus.Unavailable); }
    }

    public SmileDataStatus SaveData(long[] source, long count, string key, bool recover = true)
    {
        if (source.Length > MaximumDataBytes || count < 0 || count > source.Length) return SmileDataStatus.Invalid;
        byte[] envelope = new byte[44 + (int)count];
        for (int index = 0; index < count; index++)
        {
            if (source[index] is < 0 or > 255) return SmileDataStatus.Invalid;
            envelope[44 + index] = (byte)source[index];
        }
        "SMD4"u8.CopyTo(envelope);
        BinaryPrimitives.WriteUInt32LittleEndian(envelope.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(envelope.AsSpan(8), (uint)count);
        SHA256.HashData(envelope.AsSpan(44), envelope.AsSpan(12, 32));
        string? temporary = null;
        bool ownsTemporary = false;
        try
        {
            string? path = DataPath(key);
            if (path is null) return SmileDataStatus.Unavailable;
            temporary = path + ".tmp." + Environment.ProcessId + "." + Guid.NewGuid().ToString("N");
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                ownsTemporary = true;
                file.Write(envelope);
                file.Flush(flushToDisk: true);
            }
            if (File.Exists(path))
            {
                SmileDataStatus status = ReadData(path, MaximumDataBytes).Status;
                string? backup = path + ".bak";
                if (status != SmileDataStatus.Ok)
                {
                    if (!recover || status != SmileDataStatus.Corrupt) return status;
                    status = ReadData(backup, MaximumDataBytes).Status;
                    if (status != SmileDataStatus.Ok) return status == SmileDataStatus.Missing ? SmileDataStatus.Unavailable : status;
                    backup = null; // Never replace a verified backup with a corrupt primary.
                }
                File.Replace(temporary, path, backup);
            }
            else File.Move(temporary, path);
            ownsTemporary = false;
            return SmileDataStatus.Ok;
        }
        catch (Exception error) when (IsStorageError(error)) { return SmileDataStatus.Unavailable; }
        finally
        {
            if (ownsTemporary) try { File.Delete(temporary!); } catch (Exception error) when (IsStorageError(error)) { }
        }
    }

    private string? DataPath(string key)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(key);
        if (string.IsNullOrEmpty(_root) || bytes.Length > MaximumDataBytes) return null;
        string identity = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(_programName)));
        string folder = Path.Combine(_root, "SMILE", "Games", identity, "Data");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, Convert.ToHexStringLower(SHA256.HashData(bytes)) + ".bin");
    }

    private static (byte[]? Payload, SmileDataStatus Status) ReadData(string path, int capacity)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            long size = file.Length;
            if (size < 44 || size > MaximumDataBytes + 44) return (null, SmileDataStatus.Corrupt);
            byte[] envelope = new byte[(int)size];
            file.ReadExactly(envelope);
            if (!envelope.AsSpan(0, 4).SequenceEqual("SMD4"u8) || BinaryPrimitives.ReadUInt32LittleEndian(envelope.AsSpan(4)) != 1 ||
                BinaryPrimitives.ReadUInt32LittleEndian(envelope.AsSpan(8)) != size - 44 ||
                !SHA256.HashData(envelope.AsSpan(44)).AsSpan().SequenceEqual(envelope.AsSpan(12, 32)))
                return (null, SmileDataStatus.Corrupt);
            if (size - 44 > capacity) return (null, SmileDataStatus.TooLarge);
            return (envelope[44..], SmileDataStatus.Ok);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return (null, SmileDataStatus.Missing); }
        catch (Exception error) when (IsStorageError(error)) { return (null, SmileDataStatus.Unavailable); }
    }
}
