using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace SMILE.Tests;

internal sealed record PseudoConsoleInput(TimeSpan Delay, byte[] Bytes)
{
    public static PseudoConsoleInput Text(int delayMilliseconds, string text) =>
        new(TimeSpan.FromMilliseconds(delayMilliseconds), Encoding.UTF8.GetBytes(text));

    public static PseudoConsoleInput BytesAfter(int delayMilliseconds, params byte[] bytes) =>
        new(TimeSpan.FromMilliseconds(delayMilliseconds), bytes);
}

internal sealed record PseudoConsoleResult(
    int ExitCode,
    string Output,
    TimeSpan Duration,
    bool TimedOut);

internal static class WindowsPseudoConsole
{
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint AttachParentProcess = 0xFFFFFFFF;
    private const int StandardInputHandle = -10;
    private const int StandardOutputHandle = -11;
    private const int StandardErrorHandle = -12;
    private static readonly IntPtr ProcThreadAttributePseudoConsole = (IntPtr)0x00020016;
    private const int StillActive = 259;
    private static readonly TimeSpan OutputDrainTimeout = TimeSpan.FromSeconds(3);

    public static async Task<PseudoConsoleResult> RunBatchFileAsync(
        string batchFile,
        IReadOnlyList<PseudoConsoleInput> input,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The SMILE interactive matrix requires Windows ConPTY.");
        }

        string fullBatchFile = Path.GetFullPath(batchFile);
        string workingDirectory = Path.GetDirectoryName(fullBatchFile) ??
            throw new InvalidOperationException("The pseudo-console launcher has no parent directory.");
        if (!File.Exists(fullBatchFile))
        {
            throw new FileNotFoundException("The pseudo-console launcher does not exist.", fullBatchFile);
        }

        using ConsoleAttachmentLease consoleAttachment = ConsoleAttachmentLease.Detach();
        using SafeFileHandle inputRead = CreateAnonymousPipe(out SafeFileHandle inputWrite);
        using (inputWrite)
        {
            using SafeFileHandle outputRead = CreateAnonymousPipe(out SafeFileHandle outputWrite);
            using (outputWrite)
            {
                using var pseudoConsole = new PseudoConsoleLease(
                    CreatePseudoConsole(inputRead, outputWrite));
                return await RunAttachedProcessAsync(
                    fullBatchFile,
                    workingDirectory,
                    pseudoConsole,
                    inputRead,
                    inputWrite,
                    outputRead,
                    outputWrite,
                    input,
                    timeout,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task<PseudoConsoleResult> RunAttachedProcessAsync(
        string batchFile,
        string workingDirectory,
        PseudoConsoleLease pseudoConsole,
        SafeFileHandle inputRead,
        SafeFileHandle inputWrite,
        SafeFileHandle outputRead,
        SafeFileHandle outputWrite,
        IReadOnlyList<PseudoConsoleInput> input,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        IntPtr attributeList = IntPtr.Zero;
        IntPtr attributeSize = IntPtr.Zero;
        var startup = new StartupInfoEx();
        ProcessInformation processInformation = default;
        ProcThreadAttributeListLease? attributeLease = null;

        try
        {
            _ = InitializeProcThreadAttributeList(
                IntPtr.Zero,
                attributeCount: 1,
                flags: 0,
                ref attributeSize);
            attributeList = Marshal.AllocHGlobal(attributeSize);
            attributeLease = new ProcThreadAttributeListLease(attributeList);
            if (!InitializeProcThreadAttributeList(attributeList, 1, 0, ref attributeSize))
            {
                throw LastWin32("Could not initialize the ConPTY process attribute list.");
            }

            if (!UpdateProcThreadAttribute(
                attributeList,
                flags: 0,
                ProcThreadAttributePseudoConsole,
                pseudoConsole.Handle,
                (IntPtr)IntPtr.Size,
                IntPtr.Zero,
                IntPtr.Zero))
            {
                throw LastWin32("Could not attach the child process to ConPTY.");
            }

            startup.StartupInfo.cb = Marshal.SizeOf<StartupInfoEx>();
            startup.AttributeList = attributeList;

            string commandProcessor = Environment.GetEnvironmentVariable("COMSPEC") ??
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
            // Attach a long-lived shell exactly like a terminal does. Feeding
            // the batch command through the ConPTY input stream also proves
            // that the input channel is live before any generated program runs.
            string commandLine = $"\"{commandProcessor}\" /d /q";
            var processSecurity = new SecurityAttributes
            {
                Length = Marshal.SizeOf<SecurityAttributes>()
            };
            var threadSecurity = new SecurityAttributes
            {
                Length = Marshal.SizeOf<SecurityAttributes>()
            };
            if (!CreateProcess(
                applicationName: null,
                commandLine,
                ref processSecurity,
                ref threadSecurity,
                inheritHandles: false,
                ExtendedStartupInfoPresent,
                IntPtr.Zero,
                workingDirectory,
                ref startup,
                out processInformation))
            {
                throw LastWin32("Could not launch the ConPTY child process.");
            }
        }
        finally
        {
            if (processInformation.Thread != IntPtr.Zero)
            {
                CloseHandle(processInformation.Thread);
            }
        }

        inputRead.Dispose();
        outputWrite.Dispose();
        using (attributeLease)
        {
            return await DriveAttachedProcessAsync(
                batchFile,
                processInformation,
                pseudoConsole,
                inputWrite,
                outputRead,
                input,
                timeout,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<PseudoConsoleResult> DriveAttachedProcessAsync(
        string batchFile,
        ProcessInformation processInformation,
        PseudoConsoleLease pseudoConsole,
        SafeFileHandle inputWrite,
        SafeFileHandle outputRead,
        IReadOnlyList<PseudoConsoleInput> input,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {

        // CreatePipe returns synchronous handles. FileStream still exposes
        // task-based reads/writes for the test harness without requiring
        // overlapped native handles.
        using var inputStream = new FileStream(inputWrite, FileAccess.Write, bufferSize: 4096, isAsync: false);
        using var outputStream = new FileStream(outputRead, FileAccess.Read, bufferSize: 4096, isAsync: false);
        using var child = Process.GetProcessById(unchecked((int)processInformation.ProcessId));
        using var nativeProcess = new SafeFileHandle(processInformation.Process, ownsHandle: true);

        var captured = new MemoryStream();
        // Anonymous pipes are synchronous. Run their blocking read on a
        // worker so the harness can continue feeding raw input concurrently.
        Task copyOutput = Task.Run(() => outputStream.CopyTo(captured), CancellationToken.None);
        var stopwatch = Stopwatch.StartNew();
        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(timeout);

        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), timeoutCancellation.Token)
                .ConfigureAwait(false);
            byte[] launchCommand = Encoding.UTF8.GetBytes($"call \"{batchFile}\"\r");
            await inputStream.WriteAsync(launchCommand, timeoutCancellation.Token).ConfigureAwait(false);
            await inputStream.FlushAsync(timeoutCancellation.Token).ConfigureAwait(false);

            foreach (PseudoConsoleInput item in input)
            {
                await Task.Delay(item.Delay, timeoutCancellation.Token).ConfigureAwait(false);
                await inputStream.WriteAsync(item.Bytes, timeoutCancellation.Token).ConfigureAwait(false);
                await inputStream.FlushAsync(timeoutCancellation.Token).ConfigureAwait(false);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(150), timeoutCancellation.Token)
                .ConfigureAwait(false);
            byte[] exitCommand = Encoding.UTF8.GetBytes(
                "echo __SMILE_LAUNCH_EXIT__:%ERRORLEVEL%\rexit /b 0\r");
            await inputStream.WriteAsync(exitCommand, timeoutCancellation.Token).ConfigureAwait(false);
            await inputStream.FlushAsync(timeoutCancellation.Token).ConfigureAwait(false);

            await child.WaitForExitAsync(timeoutCancellation.Token).ConfigureAwait(false);
            stopwatch.Stop();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            KillProcessTree(child);
            stopwatch.Stop();
            pseudoConsole.Dispose();
            return new PseudoConsoleResult(
                ExitCode: StillActive,
                Output: await DrainOutputAsync(copyOutput, captured).ConfigureAwait(false),
                stopwatch.Elapsed,
                TimedOut: true);
        }

        inputStream.Dispose();
        pseudoConsole.Dispose();
        string output = await DrainOutputAsync(copyOutput, captured).ConfigureAwait(false);
        if (!GetExitCodeProcess(nativeProcess, out uint exitCode))
        {
            throw LastWin32("Could not read the ConPTY child process exit code.");
        }

        return new PseudoConsoleResult(unchecked((int)exitCode), output, stopwatch.Elapsed, TimedOut: false);
    }

    private static async Task<string> DrainOutputAsync(Task copyOutput, MemoryStream captured)
    {
        await Task.WhenAny(copyOutput, Task.Delay(OutputDrainTimeout)).ConfigureAwait(false);
        return Encoding.UTF8.GetString(captured.ToArray());
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(3000);
            }
        }
        catch (InvalidOperationException)
        {
            // The process won the race and exited between the timeout and cleanup.
        }
    }

    private static SafeFileHandle CreateAnonymousPipe(out SafeFileHandle writeHandle)
    {
        if (!CreatePipe(out SafeFileHandle readHandle, out writeHandle, IntPtr.Zero, 0))
        {
            throw LastWin32("Could not create a ConPTY pipe.");
        }

        return readHandle;
    }

    private static IntPtr CreatePseudoConsole(SafeFileHandle inputRead, SafeFileHandle outputWrite)
    {
        int result = CreatePseudoConsole(
            new Coord(100, 40),
            inputRead,
            outputWrite,
            flags: 0,
            out IntPtr pseudoConsole);
        if (result < 0)
        {
            Marshal.ThrowExceptionForHR(result);
        }

        return pseudoConsole;
    }

    private static Win32Exception LastWin32(string message) =>
        new(Marshal.GetLastWin32Error(), message);

    private sealed class ProcThreadAttributeListLease : IDisposable
    {
        private IntPtr _attributeList;

        public ProcThreadAttributeListLease(IntPtr attributeList)
        {
            _attributeList = attributeList;
        }

        public void Dispose()
        {
            if (_attributeList == IntPtr.Zero)
            {
                return;
            }

            DeleteProcThreadAttributeList(_attributeList);
            Marshal.FreeHGlobal(_attributeList);
            _attributeList = IntPtr.Zero;
        }
    }

    private sealed class PseudoConsoleLease : IDisposable
    {
        private IntPtr _handle;

        public PseudoConsoleLease(IntPtr handle)
        {
            _handle = handle;
        }

        public IntPtr Handle => _handle;

        public void Dispose()
        {
            if (_handle == IntPtr.Zero)
            {
                return;
            }

            ClosePseudoConsole(_handle);
            _handle = IntPtr.Zero;
        }
    }

    private sealed class ConsoleAttachmentLease : IDisposable
    {
        private readonly bool _detached;
        private readonly IntPtr _standardInput;
        private readonly IntPtr _standardOutput;
        private readonly IntPtr _standardError;

        private ConsoleAttachmentLease(
            bool detached,
            IntPtr standardInput,
            IntPtr standardOutput,
            IntPtr standardError)
        {
            _detached = detached;
            _standardInput = standardInput;
            _standardOutput = standardOutput;
            _standardError = standardError;
        }

        public static ConsoleAttachmentLease Detach()
        {
            // A console test runner would otherwise lend its own console to
            // CreateProcess before the pseudoconsole attribute can take over.
            // The test class is nonparallel, and the parent console is restored
            // immediately after each bounded session.
            IntPtr standardInput = GetStdHandle(StandardInputHandle);
            IntPtr standardOutput = GetStdHandle(StandardOutputHandle);
            IntPtr standardError = GetStdHandle(StandardErrorHandle);
            bool detached = GetConsoleCP() != 0 && FreeConsole();
            SetStdHandle(StandardInputHandle, IntPtr.Zero);
            SetStdHandle(StandardOutputHandle, IntPtr.Zero);
            SetStdHandle(StandardErrorHandle, IntPtr.Zero);
            return new ConsoleAttachmentLease(detached, standardInput, standardOutput, standardError);
        }

        public void Dispose()
        {
            if (_detached && !AttachConsole(AttachParentProcess))
            {
                throw LastWin32("Could not restore the pseudo-console host's parent console.");
            }

            SetStdHandle(StandardInputHandle, _standardInput);
            SetStdHandle(StandardOutputHandle, _standardOutput);
            SetStdHandle(StandardErrorHandle, _standardError);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Coord
    {
        public Coord(short x, short y)
        {
            X = x;
            Y = y;
        }

        public short X;

        public short Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx
    {
        public StartupInfo StartupInfo;
        public IntPtr AttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process;
        public IntPtr Thread;
        public uint ProcessId;
        public uint ThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public IntPtr SecurityDescriptor;
        public int InheritHandle;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreatePipe(
        out SafeFileHandle readPipe,
        out SafeFileHandle writePipe,
        IntPtr pipeAttributes,
        int size);

    [DllImport("kernel32.dll")]
    private static extern int CreatePseudoConsole(
        Coord size,
        SafeFileHandle input,
        SafeFileHandle output,
        uint flags,
        out IntPtr pseudoConsole);

    [DllImport("kernel32.dll")]
    private static extern int ClosePseudoConsole(IntPtr pseudoConsole);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitializeProcThreadAttributeList(
        IntPtr attributeList,
        int attributeCount,
        int flags,
        ref IntPtr size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateProcThreadAttribute(
        IntPtr attributeList,
        uint flags,
        IntPtr attribute,
        IntPtr value,
        IntPtr size,
        IntPtr previousValue,
        IntPtr returnSize);

    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(IntPtr attributeList);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcess(
        string? applicationName,
        string commandLine,
        ref SecurityAttributes processAttributes,
        ref SecurityAttributes threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string currentDirectory,
        ref StartupInfoEx startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(SafeFileHandle process, out uint exitCode);

    [DllImport("kernel32.dll")]
    private static extern uint GetConsoleCP();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint processId);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int standardHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetStdHandle(int standardHandle, IntPtr handle);
}
