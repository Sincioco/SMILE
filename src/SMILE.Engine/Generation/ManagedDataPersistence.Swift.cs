namespace SMILE.Engine;

internal static partial class ManagedDataPersistence
{
    private const string SwiftCommon = """
func smileDataHash(_ bytes: [UInt8]) -> [UInt8]? {
    var algorithm: BCRYPT_ALG_HANDLE?
    let name = Array("SHA256".utf16) + [0]
    guard BCryptOpenAlgorithmProvider(&algorithm, name, nil, 0) >= 0 else { return nil }
    defer { BCryptCloseAlgorithmProvider(algorithm, 0) }
    var digest = [UInt8](repeating: 0, count: 32)
    let status = bytes.withUnsafeBufferPointer { input in
        BCryptHash(algorithm, nil, 0, UnsafeMutablePointer(mutating: input.baseAddress), ULONG(input.count), &digest, 32)
    }
    return status >= 0 ? digest : nil
}

func smileDataPath(_ key: String) throws -> String? {
    let bytes = Array(key.utf8)
    guard let root = ProcessInfo.processInfo.environment["LOCALAPPDATA"], !root.isEmpty, bytes.count <= 1048576,
        let digest = smileDataHash(bytes) else { return nil }
    let folder = URL(fileURLWithPath: root).appendingPathComponent("SMILE/Games/APPLICATION_HASH/Data")
    try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
    return folder.appendingPathComponent(digest.map { String(format: "%02x", $0) }.joined() + ".bin").path
}

func smileReadData(_ path: String, _ capacity: Int) -> ([UInt8]?, Int64) {
    let name = Array(path.utf16) + [0]
    let file = CreateFileW(name, DWORD(GENERIC_READ), DWORD(FILE_SHARE_READ), nil, DWORD(OPEN_EXISTING), DWORD(FILE_ATTRIBUTE_NORMAL), nil)
    guard file != INVALID_HANDLE_VALUE else {
        let error = GetLastError()
        return (nil, error == ERROR_FILE_NOT_FOUND || error == ERROR_PATH_NOT_FOUND ? 1 : 4)
    }
    defer { CloseHandle(file) }
    var size = LARGE_INTEGER()
    guard GetFileSizeEx(file, &size) else { return (nil, 4) }
    guard size.QuadPart >= 44 && size.QuadPart <= 1048620 else { return (nil, 5) }
    var bytes = [UInt8](repeating: 0, count: Int(size.QuadPart))
    var count: DWORD = 0
    guard ReadFile(file, &bytes, DWORD(bytes.count), &count, nil), Int(count) == bytes.count else { return (nil, 4) }
    let version = UInt32(bytes[4]) | UInt32(bytes[5]) << 8 | UInt32(bytes[6]) << 16 | UInt32(bytes[7]) << 24
    let length = UInt32(bytes[8]) | UInt32(bytes[9]) << 8 | UInt32(bytes[10]) << 16 | UInt32(bytes[11]) << 24
    guard Array(bytes[0..<4]) == [83, 77, 68, 52], version == 1, length == bytes.count - 44 else { return (nil, 5) }
    let payload = Array(bytes[44...])
    guard let digest = smileDataHash(payload) else { return (nil, 4) }
    guard digest == Array(bytes[12..<44]) else { return (nil, 5) }
    return payload.count > capacity ? (nil, 6) : (payload, 0)
}

func smileDataFail(_ message: String) -> Never {
    fflush(nil)
    FileHandle.standardError.write(Data((message + "\n").utf8))
    ExitProcess(2)
}
""";

    private const string SwiftLoad = """
func smileLoadData(_ key: String, _ destination: inout [Int64], _ recover: Bool) -> (Int64, Int64) {
    var count: Int64 = 0, status: Int64 = 3
    if destination.count <= 1048576 {
        if !recover { destination = [Int64](repeating: 0, count: destination.count) }
        do {
            if let path = try smileDataPath(key) {
                var result = smileReadData(path, destination.count)
                status = result.1
                if recover && (status == 1 || status == 5) {
                    let backup = smileReadData(path + ".bak", destination.count)
                    if backup.1 == 0 { result = backup; status = 2 }
                    else if backup.1 != 1 { status = backup.1 }
                }
                if let bytes = result.0 {
                    count = Int64(bytes.count)
                    for index in bytes.indices { destination[index] = Int64(bytes[index]) }
                }
            } else { status = 4 }
        } catch { status = 4 }
    }
    if !recover && status != 0 && status != 1 { smileDataFail("Load Data encountered an invalid destination, corrupt block, oversized block, or unavailable storage.") }
    return (count, status)
}
""";

    private const string SwiftSave = """
func smileSaveDataCore(_ source: [Int64], _ count: Int64, _ key: String, _ recover: Bool) -> Int64 {
    guard source.count <= 1048576, count >= 0, count <= source.count else { return 3 }
    let values = source.prefix(Int(count))
    guard values.allSatisfy({ $0 >= 0 && $0 <= 255 }) else { return 3 }
    let payload = values.map { UInt8($0) }
    guard let digest = smileDataHash(payload) else { return 4 }
    let length = (0..<4).map { UInt8((UInt32(count) >> ($0 * 8)) & 255) }
    let bytes: [UInt8] = [83, 77, 68, 52, 1, 0, 0, 0] + length + digest + payload
    do {
        guard let path = try smileDataPath(key) else { return 4 }
        let temporary = path + ".tmp." + String(GetCurrentProcessId()) + "." + String(GetTickCount64())
        let temporaryName = Array(temporary.utf16) + [0]
        var owned = false
        defer { if owned { DeleteFileW(temporaryName) } }
        let file = CreateFileW(temporaryName, DWORD(GENERIC_WRITE), 0, nil, DWORD(CREATE_NEW), DWORD(FILE_ATTRIBUTE_NORMAL), nil)
        guard file != INVALID_HANDLE_VALUE else { return 4 }
        owned = true
        var written: DWORD = 0
        let stored = bytes.withUnsafeBytes { WriteFile(file, $0.baseAddress, DWORD(bytes.count), &written, nil) }
        let flushed = stored && written == bytes.count && FlushFileBuffers(file)
        CloseHandle(file)
        guard flushed else { return 4 }
        let name = Array(path.utf16) + [0]
        if GetFileAttributesW(name) != INVALID_FILE_ATTRIBUTES {
            var status = smileReadData(path, 1048576).1
            if status != 0 {
                if !recover || status != 5 { return status }
                status = smileReadData(path + ".bak", 1048576).1
                if status != 0 { return status == 1 ? 4 : status }
                guard ReplaceFileW(name, temporaryName, nil, 0, nil, nil) else { return 4 }
            } else {
                let backup = Array((path + ".bak").utf16) + [0]
                guard ReplaceFileW(name, temporaryName, backup, 0, nil, nil) else { return 4 }
            }
        } else {
            guard MoveFileExW(temporaryName, name, DWORD(MOVEFILE_WRITE_THROUGH)) else { return 4 }
        }
        owned = false
        return 0
    } catch { return 4 }
}

func smileSaveData(_ source: [Int64], _ count: Int64, _ key: String, _ recover: Bool) -> Int64 {
    let status = smileSaveDataCore(source, count, key, recover)
    if !recover && status != 0 { smileDataFail("Save Data received invalid bytes/count or could not atomically store the block.") }
    return status
}
""";
}
