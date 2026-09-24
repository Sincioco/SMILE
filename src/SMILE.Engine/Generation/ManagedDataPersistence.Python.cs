namespace SMILE.Engine;

internal static partial class ManagedDataPersistence
{
    private const string PythonCommon = """
def smile_data_path(key):
    encoded = key.encode("utf-8")
    root = os.environ.get("LOCALAPPDATA")
    if not root or len(encoded) > 1048576:
        return None
    folder = pathlib.Path(root) / "SMILE" / "Games" / "APPLICATION_HASH" / "Data"
    folder.mkdir(parents=True, exist_ok=True)
    return folder / (hashlib.sha256(encoded).hexdigest() + ".bin")

def smile_read_data(path, capacity):
    try:
        with path.open("rb") as file:
            size = os.fstat(file.fileno()).st_size
            if size < 44 or size > 1048620:
                return None, 5
            envelope = file.read(size)
        if len(envelope) != size:
            return None, 4
        if (envelope[:4] != b"SMD4" or int.from_bytes(envelope[4:8], "little") != 1
                or int.from_bytes(envelope[8:12], "little") != size - 44
                or hashlib.sha256(envelope[44:]).digest() != envelope[12:44]):
            return None, 5
        if size - 44 > capacity:
            return None, 6
        return envelope[44:], 0
    except FileNotFoundError:
        return None, 1
    except (OSError, ValueError):
        return None, 4

def smile_data_fail(message):
    sys.stdout.flush()
    print(message, file=sys.stderr)
    raise SystemExit(2)
""";
    private const string PythonLoad = """
def smile_load_data(key, destination, recover):
    count, status = 0, 3
    if len(destination) <= 1048576:
        if not recover:
            destination[:] = [0] * len(destination)
        try:
            path = smile_data_path(key)
            payload, status = (None, 4) if path is None else smile_read_data(path, len(destination))
            if path is not None and recover and status in (1, 5):
                backup, backup_status = smile_read_data(pathlib.Path(str(path) + ".bak"), len(destination))
                if backup_status == 0:
                    payload, status = backup, 2
                elif backup_status != 1:
                    status = backup_status
            if payload is not None:
                count = len(payload)
                destination[:count] = payload
        except (OSError, ValueError):
            status = 4
    if not recover and status not in (0, 1):
        smile_data_fail("Load Data encountered an invalid destination, corrupt block, oversized block, or unavailable storage.")
    return count, status
""";
    private const string PythonSave = """
def smile_save_data_core(source, count, key, recover):
    if len(source) > 1048576 or count < 0 or count > len(source) or any(value < 0 or value > 255 for value in source[:count]):
        return 3
    payload = bytes(source[:count])
    envelope = b"SMD4" + (1).to_bytes(4, "little") + count.to_bytes(4, "little") + hashlib.sha256(payload).digest() + payload
    temporary, owned = None, False
    try:
        path = smile_data_path(key)
        if path is None:
            return 4
        temporary = pathlib.Path(str(path) + ".tmp." + str(os.getpid()) + "." + str(time.monotonic_ns()))
        with temporary.open("xb") as file:
            owned = True
            file.write(envelope)
            file.flush()
            os.fsync(file.fileno())
        if path.exists():
            _, status = smile_read_data(path, 1048576)
            backup = str(path) + ".bak"
            if status != 0:
                if not recover or status != 5:
                    return status
                _, status = smile_read_data(pathlib.Path(backup), 1048576)
                if status != 0:
                    return 4 if status == 1 else status
                backup = None
            replace = ctypes.windll.kernel32.ReplaceFileW
            replace.argtypes = [ctypes.c_wchar_p, ctypes.c_wchar_p, ctypes.c_wchar_p, ctypes.c_uint32, ctypes.c_void_p, ctypes.c_void_p]
            if not replace(str(path), str(temporary), backup, 0, None, None):
                return 4
        else:
            move = ctypes.windll.kernel32.MoveFileExW
            move.argtypes = [ctypes.c_wchar_p, ctypes.c_wchar_p, ctypes.c_uint32]
            if not move(str(temporary), str(path), 8):
                return 4
        owned = False
        return 0
    except (OSError, ValueError):
        return 4
    finally:
        if owned:
            try:
                temporary.unlink()
            except OSError:
                pass

def smile_save_data(source, count, key, recover):
    status = smile_save_data_core(source, count, key, recover)
    if not recover and status != 0:
        smile_data_fail("Save Data received invalid bytes/count or could not atomically store the block.")
    return status
""";
}
