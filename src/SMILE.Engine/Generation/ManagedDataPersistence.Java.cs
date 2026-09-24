namespace SMILE.Engine;

internal static partial class ManagedDataPersistence
{
    private const string JavaCommon = """
private record SmileDataRead(byte[] bytes, long status) {}
private static byte[] smileDataHash(byte[] bytes) {
    try { return java.security.MessageDigest.getInstance("SHA-256").digest(bytes); }
    catch (java.security.NoSuchAlgorithmException error) { throw new AssertionError(error); }
}
private static java.nio.file.Path smileDataPath(String key) throws java.io.IOException {
    String root = System.getenv("LOCALAPPDATA");
    byte[] bytes = key.getBytes(java.nio.charset.StandardCharsets.UTF_8);
    if (root == null || root.isEmpty() || bytes.length > 1048576) return null;
    var folder = java.nio.file.Path.of(root, "SMILE", "Games", "APPLICATION_HASH", "Data");
    java.nio.file.Files.createDirectories(folder);
    return folder.resolve(java.util.HexFormat.of().formatHex(smileDataHash(bytes)) + ".bin");
}
private static SmileDataRead smileReadData(java.nio.file.Path path, int capacity) {
    try (var file = java.nio.channels.FileChannel.open(path, java.nio.file.StandardOpenOption.READ)) {
        long size = file.size();
        if (size < 44 || size > 1048620) return new SmileDataRead(null, 5);
        var buffer = java.nio.ByteBuffer.allocate((int)size).order(java.nio.ByteOrder.LITTLE_ENDIAN);
        while (buffer.hasRemaining()) if (file.read(buffer) < 0) return new SmileDataRead(null, 4);
        byte[] envelope = buffer.array();
        byte[] payload = java.util.Arrays.copyOfRange(envelope, 44, envelope.length);
        if (envelope[0] != 'S' || envelope[1] != 'M' || envelope[2] != 'D' || envelope[3] != '4' ||
            buffer.getInt(4) != 1 || buffer.getInt(8) != size - 44 ||
            !java.util.Arrays.equals(smileDataHash(payload), java.util.Arrays.copyOfRange(envelope, 12, 44))) return new SmileDataRead(null, 5);
        if (payload.length > capacity) return new SmileDataRead(null, 6);
        return new SmileDataRead(payload, 0);
    } catch (java.nio.file.NoSuchFileException error) { return new SmileDataRead(null, 1); }
    catch (java.io.IOException | IllegalArgumentException | SecurityException error) { return new SmileDataRead(null, 4); }
}
private static void smileDataFail(String message) { System.out.flush(); System.err.println(message); System.exit(2); }
""";

    private const string JavaLoad = """
private static long[] smileLoadData(String key, long[] destination, boolean recover) {
    long count = 0, status = 3;
    if (destination.length <= 1048576) {
        if (!recover) java.util.Arrays.fill(destination, 0);
        try {
            var path = smileDataPath(key);
            SmileDataRead result = path == null ? new SmileDataRead(null, 4) : smileReadData(path, destination.length);
            status = result.status();
            if (path != null && recover && (status == 1 || status == 5)) {
                var backup = smileReadData(java.nio.file.Path.of(path + ".bak"), destination.length);
                if (backup.status() == 0) { result = backup; status = 2; }
                else if (backup.status() != 1) status = backup.status();
            }
            if (result.bytes() != null) {
                count = result.bytes().length;
                for (int index = 0; index < count; index++) destination[index] = result.bytes()[index] & 255;
            }
        } catch (java.io.IOException | IllegalArgumentException | SecurityException error) { status = 4; }
    }
    if (!recover && status != 0 && status != 1) smileDataFail("Load Data encountered an invalid destination, corrupt block, oversized block, or unavailable storage.");
    return new long[] { count, status };
}
""";

    private const string JavaSave = """
// Standard JDK FFM exposes Windows' atomic replace-with-backup operation.
private static boolean smileDataReplace(java.nio.file.Path path, java.nio.file.Path temporary, String backup, boolean exists) {
    try (var arena = java.lang.foreign.Arena.ofConfined()) {
        var linker = java.lang.foreign.Linker.nativeLinker();
        var library = java.lang.foreign.SymbolLookup.libraryLookup("kernel32", arena);
        var address = java.lang.foreign.ValueLayout.ADDRESS;
        var number = java.lang.foreign.ValueLayout.JAVA_INT;
        var target = arena.allocateArray(java.lang.foreign.ValueLayout.JAVA_CHAR, (path + "\0").toCharArray());
        var source = arena.allocateArray(java.lang.foreign.ValueLayout.JAVA_CHAR, (temporary + "\0").toCharArray());
        var none = java.lang.foreign.MemorySegment.NULL;
        if (exists) {
            var saved = backup == null ? none : arena.allocateArray(java.lang.foreign.ValueLayout.JAVA_CHAR, (backup + "\0").toCharArray());
            var call = linker.downcallHandle(library.find("ReplaceFileW").orElseThrow(), java.lang.foreign.FunctionDescriptor.of(number, address, address, address, number, address, address));
            return (int)call.invoke(target, source, saved, 0, none, none) != 0;
        }
        var call = linker.downcallHandle(library.find("MoveFileExW").orElseThrow(), java.lang.foreign.FunctionDescriptor.of(number, address, address, number));
        return (int)call.invoke(source, target, 8) != 0;
    } catch (Throwable error) { return false; }
}
private static long smileSaveDataCore(long[] source, long count, String key, boolean recover) {
    if (source.length > 1048576 || count < 0 || count > source.length) return 3;
    byte[] payload = new byte[(int)count];
    for (int index = 0; index < count; index++) {
        if (source[index] < 0 || source[index] > 255) return 3;
        payload[index] = (byte)source[index];
    }
    var envelope = java.nio.ByteBuffer.allocate(44 + (int)count).order(java.nio.ByteOrder.LITTLE_ENDIAN);
    envelope.put(new byte[] { 'S', 'M', 'D', '4' }).putInt(1).putInt((int)count).put(smileDataHash(payload)).put(payload).flip();
    java.nio.file.Path temporary = null;
    boolean owned = false;
    try {
        var path = smileDataPath(key);
        if (path == null) return 4;
        temporary = java.nio.file.Path.of(path + ".tmp." + ProcessHandle.current().pid() + "." + System.nanoTime());
        try (var file = java.nio.channels.FileChannel.open(temporary, java.nio.file.StandardOpenOption.CREATE_NEW, java.nio.file.StandardOpenOption.WRITE)) {
            owned = true;
            while (envelope.hasRemaining()) file.write(envelope);
            file.force(true);
        }
        boolean exists = java.nio.file.Files.exists(path);
        String backup = path + ".bak";
        if (exists) {
            long status = smileReadData(path, 1048576).status();
            if (status != 0) {
                if (!recover || status != 5) return status;
                status = smileReadData(java.nio.file.Path.of(backup), 1048576).status();
                if (status != 0) return status == 1 ? 4 : status;
                backup = null;
            }
        }
        if (!smileDataReplace(path, temporary, backup, exists)) return 4;
        owned = false;
        return 0;
    } catch (java.io.IOException | IllegalArgumentException | SecurityException error) { return 4; }
    finally { if (owned) try { java.nio.file.Files.deleteIfExists(temporary); } catch (java.io.IOException error) { } }
}
private static long smileSaveData(long[] source, long count, String key, boolean recover) {
    long status = smileSaveDataCore(source, count, key, recover);
    if (!recover && status != 0) smileDataFail("Save Data received invalid bytes/count or could not atomically store the block.");
    return status;
}
""";
}
