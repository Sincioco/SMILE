namespace SMILE.Engine;

internal static partial class ManagedDataPersistence
{
    private const string JavaScriptCommon = """
async function smileDataPath(key) {
    const bytes = Buffer.from(key, "utf8");
    const root = process.env.LOCALAPPDATA;
    if (!root || bytes.length > 1048576) return null;
    const folder = smilePath.join(root, "SMILE", "Games", "APPLICATION_HASH", "Data");
    await smileFileSystem.mkdir(folder, { recursive: true });
    return smilePath.join(folder, smileCrypto.createHash("sha256").update(bytes).digest("hex") + ".bin");
}

async function smileReadData(path, capacity) {
    let file;
    try {
        file = await smileFileSystem.open(path, "r");
        const metadata = await file.stat();
        if (!metadata.isFile()) return { bytes: null, status: 4n };
        const size = metadata.size;
        if (size < 44 || size > 1048620) return { bytes: null, status: 5n };
        const envelope = Buffer.alloc(size);
        let offset = 0;
        while (offset < size) {
            const read = await file.read(envelope, offset, size - offset, offset);
            if (!read.bytesRead) return { bytes: null, status: 4n };
            offset += read.bytesRead;
        }
        const payload = envelope.subarray(44);
        if (!envelope.subarray(0, 4).equals(Buffer.from("SMD4")) || envelope.readUInt32LE(4) !== 1 || envelope.readUInt32LE(8) !== size - 44 ||
            !smileCrypto.createHash("sha256").update(payload).digest().equals(envelope.subarray(12, 44))) return { bytes: null, status: 5n };
        if (payload.length > capacity) return { bytes: null, status: 6n };
        return { bytes: payload, status: 0n };
    } catch (error) { return { bytes: null, status: error.code === "ENOENT" ? 1n : 4n }; }
    finally { if (file) await file.close().catch(() => {}); }
}

async function smileDataFail(message) {
    await new Promise(resolve => process.stdout.write("", resolve));
    await new Promise(resolve => process.stderr.write(message + "\n", resolve));
    process.exit(2);
}
""";
    private const string JavaScriptLoad = """
async function smileLoadData(key, destination, recover) {
    let count = 0n, status = 3n;
    if (destination.length <= 1048576) {
        if (!recover) destination.fill(0n);
        try {
            const path = await smileDataPath(key);
            let result = path === null ? { bytes: null, status: 4n } : await smileReadData(path, destination.length);
            status = result.status;
            if (path !== null && recover && (status === 1n || status === 5n)) {
                const backup = await smileReadData(path + ".bak", destination.length);
                if (backup.status === 0n) { result = backup; status = 2n; }
                else if (backup.status !== 1n) status = backup.status;
            }
            if (result.bytes !== null) {
                count = BigInt(result.bytes.length);
                for (let index = 0; index < result.bytes.length; index++) destination[index] = BigInt(result.bytes[index]);
            }
        } catch { status = 4n; }
    }
    if (!recover && status !== 0n && status !== 1n) await smileDataFail("Load Data encountered an invalid destination, corrupt block, oversized block, or unavailable storage.");
    return [count, status];
}
""";
    private const string JavaScriptSave = """
async function smileSaveDataCore(source, count, key, recover) {
    if (source.length > 1048576 || count < 0n || count > BigInt(source.length)) return 3n;
    const envelope = Buffer.alloc(44 + Number(count));
    for (let index = 0; index < Number(count); index++) {
        if (source[index] < 0n || source[index] > 255n) return 3n;
        envelope[44 + index] = Number(source[index]);
    }
    envelope.write("SMD4", 0, "ascii"); envelope.writeUInt32LE(1, 4); envelope.writeUInt32LE(Number(count), 8);
    smileCrypto.createHash("sha256").update(envelope.subarray(44)).digest().copy(envelope, 12);
    let temporary, backupTemporary, owned = false, backupOwned = false;
    try {
        const path = await smileDataPath(key);
        if (path === null) return 4n;
        const suffix = ".tmp." + process.pid + "." + process.hrtime.bigint();
        temporary = path + suffix;
        const file = await smileFileSystem.open(temporary, "wx");
        owned = true;
        try { await file.writeFile(envelope); await file.sync(); } finally { await file.close(); }
        let exists = true;
        try { await smileFileSystem.stat(path); } catch (error) { if (error.code === "ENOENT") exists = false; else throw error; }
        if (exists) {
            const primary = await smileReadData(path, 1048576);
            if (primary.status !== 0n) {
                if (!recover || primary.status !== 5n) return primary.status;
                const backup = await smileReadData(path + ".bak", 1048576);
                if (backup.status !== 0n) return backup.status === 1n ? 4n : backup.status;
            } else {
                // Node exposes atomic rename, but not Windows replace-with-backup.
                // Publish a flushed valid backup before atomically replacing primary.
                backupTemporary = path + ".bak" + suffix;
                const backup = await smileFileSystem.open(backupTemporary, "wx");
                backupOwned = true;
                try {
                    const oldEnvelope = Buffer.alloc(44 + primary.bytes.length);
                    oldEnvelope.write("SMD4"); oldEnvelope.writeUInt32LE(1, 4); oldEnvelope.writeUInt32LE(primary.bytes.length, 8);
                    smileCrypto.createHash("sha256").update(primary.bytes).digest().copy(oldEnvelope, 12);
                    primary.bytes.copy(oldEnvelope, 44);
                    await backup.writeFile(oldEnvelope); await backup.sync();
                } finally { await backup.close(); }
                await smileFileSystem.rename(backupTemporary, path + ".bak");
                backupOwned = false;
            }
        }
        await smileFileSystem.rename(temporary, path);
        owned = false;
        return 0n;
    } catch { return 4n; }
    finally {
        if (owned) await smileFileSystem.unlink(temporary).catch(() => {});
        if (backupOwned) await smileFileSystem.unlink(backupTemporary).catch(() => {});
    }
}

async function smileSaveData(source, count, key, recover) {
    const status = await smileSaveDataCore(source, count, key, recover);
    if (!recover && status !== 0n) await smileDataFail("Save Data received invalid bytes/count or could not atomically store the block.");
    return status;
}
""";
}
