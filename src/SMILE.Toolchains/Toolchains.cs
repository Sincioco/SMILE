using System.Text;
using SMILE.Engine;

namespace SMILE.Toolchains;

public sealed record ToolchainStatus(
    TargetLanguage Language,
    bool IsAvailable,
    string Name,
    string? Version,
    string? Location,
    string Message);

public sealed record BuildRunResult(
    TargetLanguage Language,
    bool Success,
    ToolchainStatus ToolchainStatus,
    string BuildOutput,
    string StandardOutput,
    string StandardError,
    int? ExitCode,
    TimeSpan Duration,
    bool TimedOut,
    bool Cancelled,
    string? WorkingDirectory,
    string? PauseLauncherPath,
    string Stage);

public sealed record BuildRunOptions(bool CreatePauseLauncher = false)
{
    public static BuildRunOptions Default { get; } = new();
}

public interface IToolchain
{
    TargetLanguage Language { get; }

    // Detection is separate from build/run so the UI can show availability
    // without forcing a compile just to learn whether a tool exists.
    Task<ToolchainStatus> DetectAsync(CancellationToken cancellationToken);

    Task<BuildRunResult> BuildAndRunAsync(
        GeneratedProgram generatedProgram,
        CancellationToken cancellationToken,
        BuildRunOptions? options = null);
}

public sealed class ToolchainRegistry
{
    private readonly IReadOnlyDictionary<TargetLanguage, IToolchain> _toolchains;

    public ToolchainRegistry(IEnumerable<IToolchain> toolchains)
    {
        _toolchains = toolchains.ToDictionary(toolchain => toolchain.Language);
    }

    public static ToolchainRegistry CreateDefault()
    {
        var runner = new ProcessRunner();
        var visualStudioLocator = new VisualStudioLocator(runner);

        // The registry stays explicit for v0.1: each target gets one clear
        // toolchain object, including targets that can only transpile today.
        return new ToolchainRegistry(new IToolchain[]
        {
            new DotNetToolchain(runner),
            new MsvcCToolchain(runner, visualStudioLocator),
            new MasmX64Toolchain(runner, visualStudioLocator),
            new NodeToolchain(runner),
            new JavaToolchain(runner),
            new TranspileOnlyToolchain(TargetLanguage.ObjectiveC),
            new TranspileOnlyToolchain(TargetLanguage.Swift)
        });
    }

    public IToolchain Get(TargetLanguage language) => _toolchains[language];

    public IReadOnlyList<IToolchain> All => _toolchains.Values.ToArray();
}

public sealed class TranspileOnlyToolchain : IToolchain
{
    public TranspileOnlyToolchain(TargetLanguage language)
    {
        Language = language;
    }

    public TargetLanguage Language { get; }

    public Task<ToolchainStatus> DetectAsync(CancellationToken cancellationToken)
    {
        // These targets are intentionally useful before their compiler story is
        // local to Windows. The engine can still generate source for learners
        // to inspect, copy, and save.
        string name = TargetLanguageInfo.GetDisplayName(Language);
        return Task.FromResult(new ToolchainStatus(
            Language,
            IsAvailable: false,
            name,
            Version: null,
            Location: null,
            $"{name} transpilation is available, but local Build & Run is not supported on this Windows machine yet."));
    }

    public async Task<BuildRunResult> BuildAndRunAsync(
        GeneratedProgram generatedProgram,
        CancellationToken cancellationToken,
        BuildRunOptions? options = null)
    {
        ToolchainStatus status = await DetectAsync(cancellationToken).ConfigureAwait(false);
        return new BuildRunResult(
            Language,
            Success: false,
            status,
            BuildOutput: status.Message,
            StandardOutput: string.Empty,
            StandardError: status.Message,
            ExitCode: null,
            Duration: TimeSpan.Zero,
            TimedOut: false,
            Cancelled: false,
            WorkingDirectory: null,
            PauseLauncherPath: null,
            Stage: "Transpile Only");
    }
}

public abstract class ToolchainBase : IToolchain
{
    public static readonly TimeSpan DetectionTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan BuildTimeout = TimeSpan.FromSeconds(120);
    public static readonly TimeSpan ProgramTimeout = TimeSpan.FromSeconds(10);
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static int _oldWorkspaceCleanupStarted;

    protected ToolchainBase(IProcessRunner runner)
    {
        Runner = runner;
    }

    public abstract TargetLanguage Language { get; }

    protected IProcessRunner Runner { get; }

    public abstract Task<ToolchainStatus> DetectAsync(CancellationToken cancellationToken);

    public abstract Task<BuildRunResult> BuildAndRunAsync(
        GeneratedProgram generatedProgram,
        CancellationToken cancellationToken,
        BuildRunOptions? options = null);

    protected ToolchainStatus Missing(string message) =>
        new(Language, false, TargetLanguageInfo.GetDisplayName(Language), null, null, message);

    protected ToolchainStatus Available(string version, string location, string message) =>
        new(Language, true, TargetLanguageInfo.GetDisplayName(Language), version, location, message);

    protected async Task<string> WriteGeneratedProgramAsync(
        GeneratedProgram generatedProgram,
        CancellationToken cancellationToken)
    {
        // Generated code is always written to a temporary SMILE-owned
        // workspace. That keeps compiler output out of the repository and
        // gives every build/run operation an isolated directory.
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "SMILE", "Runs"));
        Directory.CreateDirectory(root);
        await CleanOldWorkspacesOnceAsync(root).ConfigureAwait(false);

        string workspace = Path.Combine(
            root,
            DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") +
            "-" +
            Guid.NewGuid().ToString("N") +
            " - " +
            GetSafeWorkspaceLanguageName(generatedProgram.Language));
        Directory.CreateDirectory(workspace);

        string workspaceFullPath = Path.GetFullPath(workspace);

        foreach (GeneratedFile file in generatedProgram.Files)
        {
            string targetPath = Path.GetFullPath(Path.Combine(workspaceFullPath, file.RelativePath));
            if (!targetPath.StartsWith(workspaceFullPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Generated file path escaped the SMILE workspace.");
            }

            string? directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(targetPath, file.Content, Utf8NoBom, cancellationToken)
                .ConfigureAwait(false);
        }

        return workspaceFullPath;
    }

    protected async Task<string> WriteCommandScriptAsync(
        string workspace,
        string scriptName,
        IEnumerable<string> lines,
        CancellationToken cancellationToken)
    {
        // Visual Studio setup paths contain spaces, and cmd.exe quoting is
        // easiest to reason about from a tiny script stored in the temp build
        // workspace.
        string workspaceFullPath = Path.GetFullPath(workspace);
        string scriptPath = Path.GetFullPath(Path.Combine(workspaceFullPath, scriptName));
        if (!scriptPath.StartsWith(workspaceFullPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Command script path escaped the SMILE workspace.");
        }

        await File.WriteAllLinesAsync(scriptPath, lines, Utf8NoBom, cancellationToken).ConfigureAwait(false);
        return scriptPath;
    }

    protected async Task<string?> WritePauseLauncherAsync(
        string workspace,
        IEnumerable<string> programCommandLines,
        BuildRunOptions? options,
        CancellationToken cancellationToken)
    {
        if ((options ?? BuildRunOptions.Default).CreatePauseLauncher is false)
        {
            return null;
        }

        // The launcher is deliberately a separate .cmd file instead of extra
        // target-language source. That keeps generated programs idiomatic, but
        // still gives learners a double-click path that leaves the console open.
        var lines = new List<string>
        {
            "@echo off",
            "cd /d \"%~dp0\""
        };
        lines.AddRange(programCommandLines);
        lines.Add("echo.");
        lines.Add("echo Press any key to exit...");
        lines.Add("pause >nul");

        return await WriteCommandScriptAsync(
            workspace,
            "Run Program - Press Any Key.cmd",
            lines,
            cancellationToken).ConfigureAwait(false);
    }

    protected BuildRunResult MissingResult(ToolchainStatus status, string? workingDirectory = null) =>
        new(
            Language,
            false,
            status,
            status.Message,
            string.Empty,
            status.Message,
            null,
            TimeSpan.Zero,
            TimedOut: false,
            Cancelled: false,
            workingDirectory,
            null,
            "Toolchain Missing");

    protected BuildRunResult FromProcessResults(
        ToolchainStatus status,
        string buildOutput,
        ProcessResult runResult,
        string workingDirectory,
        string stage,
        bool buildSucceeded = true,
        string? pauseLauncherPath = null,
        TimeSpan? totalDuration = null)
    {
        bool success = buildSucceeded && runResult.Success;

        return new BuildRunResult(
            Language,
            success,
            status,
            buildOutput,
            runResult.StandardOutput,
            runResult.StandardError,
            runResult.ExitCode,
            totalDuration ?? runResult.Duration,
            runResult.TimedOut,
            runResult.Cancelled,
            workingDirectory,
            pauseLauncherPath,
            stage);
    }

    protected static string Combine(ProcessResult result) =>
        JoinNonEmpty(result.StandardOutput, result.StandardError);

    protected static string JoinNonEmpty(params string?[] values) =>
        string.Join(
            Environment.NewLine,
            values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.TrimEnd()));

    protected static string QuoteForCmd(string value) =>
        "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    protected static TimeSpan TotalDuration(params ProcessResult[] results) =>
        TimeSpan.FromTicks(results.Sum(result => result.Duration.Ticks));

    private static string GetSafeWorkspaceLanguageName(TargetLanguage language)
    {
        string displayName = TargetLanguageInfo.GetDisplayName(language);
        char[] invalidChars = Path.GetInvalidFileNameChars();

        foreach (char invalidChar in invalidChars)
        {
            displayName = displayName.Replace(invalidChar, '-');
        }

        return displayName;
    }

    private static async Task CleanOldWorkspacesOnceAsync(string root)
    {
        if (Interlocked.Exchange(ref _oldWorkspaceCleanupStarted, 1) != 0)
        {
            return;
        }

        try
        {
            await Task.Run(() => CleanOldWorkspaces(root)).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsExpectedCleanupException(ex))
        {
            // Cleanup is housekeeping. A locked or disappearing old folder must
            // not prevent SMILE from creating a fresh build workspace.
        }
    }

    private static void CleanOldWorkspaces(string root)
    {
        string rootFullPath = Path.GetFullPath(root);
        // Keep temporary compiler output from piling up between experiments.
        // One day gives enough room for troubleshooting without letting build
        // folders quietly grow into multi-gigabyte clutter.
        var cutoff = DateTime.UtcNow.AddDays(-1);

        string[] directories;
        try
        {
            directories = Directory.EnumerateDirectories(rootFullPath).ToArray();
        }
        catch (Exception ex) when (IsExpectedCleanupException(ex))
        {
            return;
        }

        foreach (string directory in directories)
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(directory);
            }
            catch (Exception ex) when (IsExpectedCleanupException(ex))
            {
                continue;
            }

            try
            {
                if (!fullPath.StartsWith(rootFullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var info = new DirectoryInfo(fullPath);
                if (info.LastWriteTimeUtc < cutoff)
                {
                    info.Delete(recursive: true);
                }
            }
            catch (DirectoryNotFoundException)
            {
            }
            catch (PathTooLongException)
            {
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static bool IsExpectedCleanupException(Exception exception) =>
        exception is IOException or
            UnauthorizedAccessException or
            DirectoryNotFoundException or
            PathTooLongException or
            ArgumentException or
            NotSupportedException;
}

public sealed class DotNetToolchain : ToolchainBase
{
    public DotNetToolchain(IProcessRunner runner)
        : base(runner)
    {
    }

    public override TargetLanguage Language => TargetLanguage.CSharp;

    public override async Task<ToolchainStatus> DetectAsync(CancellationToken cancellationToken)
    {
        ProcessResult result = await Runner.RunAsync(
            new ProcessCommand("dotnet", new[] { "--version" }, Environment.CurrentDirectory),
            DetectionTimeout,
            cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            return Missing("The .NET SDK was not found. Install .NET SDK 10 or newer.");
        }

        return Available(result.StandardOutput.Trim(), "dotnet", ".NET SDK detected.");
    }

    public override async Task<BuildRunResult> BuildAndRunAsync(
        GeneratedProgram generatedProgram,
        CancellationToken cancellationToken,
        BuildRunOptions? options = null)
    {
        ToolchainStatus status = await DetectAsync(cancellationToken).ConfigureAwait(false);
        if (!status.IsAvailable)
        {
            return MissingResult(status);
        }

        string workspace = await WriteGeneratedProgramAsync(generatedProgram, cancellationToken)
            .ConfigureAwait(false);

        ProcessResult build = await Runner.RunAsync(
            new ProcessCommand(
                "dotnet",
                new[] { "build", "GeneratedProgram.csproj", "-nologo" },
                workspace),
            BuildTimeout,
            cancellationToken).ConfigureAwait(false);

        string buildOutput = Combine(build);
        if (!build.Success)
        {
            return new BuildRunResult(
                Language,
                false,
                status,
                buildOutput,
                string.Empty,
                build.StandardError,
                build.ExitCode,
                build.Duration,
                build.TimedOut,
                build.Cancelled,
                workspace,
                null,
                "Building");
        }

        string? pauseLauncherPath = await WritePauseLauncherAsync(
            workspace,
            new[] { "\"bin\\Debug\\net10.0\\GeneratedProgram.exe\"" },
            options,
            cancellationToken).ConfigureAwait(false);

        string executablePath = Path.Combine(workspace, "bin", "Debug", "net10.0", "GeneratedProgram.exe");
        ProcessResult run = await Runner.RunAsync(
            new ProcessCommand(
                executablePath,
                Array.Empty<string>(),
                workspace),
            ProgramTimeout,
            cancellationToken).ConfigureAwait(false);

        return FromProcessResults(
            status,
            buildOutput,
            run,
            workspace,
            "Running",
            pauseLauncherPath: pauseLauncherPath,
            totalDuration: TotalDuration(build, run));
    }
}

public sealed class MsvcCToolchain : ToolchainBase
{
    private readonly VisualStudioLocator _visualStudioLocator;

    public MsvcCToolchain(IProcessRunner runner, VisualStudioLocator visualStudioLocator)
        : base(runner)
    {
        _visualStudioLocator = visualStudioLocator;
    }

    public override TargetLanguage Language => TargetLanguage.C;

    public override async Task<ToolchainStatus> DetectAsync(CancellationToken cancellationToken)
    {
        VisualStudioTools? tools = await _visualStudioLocator.FindAsync(cancellationToken)
            .ConfigureAwait(false);

        return tools is null
            ? Missing("MSVC x64 tools were not found. Install Visual Studio 2026 with Desktop development with C++.")
            : Available(tools.Version, tools.VcVars64Path, "MSVC x64 tools detected.");
    }

    public override async Task<BuildRunResult> BuildAndRunAsync(
        GeneratedProgram generatedProgram,
        CancellationToken cancellationToken,
        BuildRunOptions? options = null)
    {
        ToolchainStatus status = await DetectAsync(cancellationToken).ConfigureAwait(false);
        if (!status.IsAvailable || status.Location is null)
        {
            return MissingResult(status);
        }

        string workspace = await WriteGeneratedProgramAsync(generatedProgram, cancellationToken)
            .ConfigureAwait(false);

        await WriteCommandScriptAsync(
            workspace,
            "build-c.cmd",
            new[]
            {
                "@echo off",
                $"call {QuoteForCmd(status.Location)} >nul",
                "if errorlevel 1 exit /b %errorlevel%",
                "cl.exe /nologo /TC /utf-8 Program.c /Fe:Program.exe"
            },
            cancellationToken).ConfigureAwait(false);

        ProcessResult build = await Runner.RunAsync(
            ProcessCommand.ForCmd("build-c.cmd", workspace),
            BuildTimeout,
            cancellationToken).ConfigureAwait(false);

        string buildOutput = Combine(build);
        if (!build.Success)
        {
            return FromProcessResults(status, buildOutput, build, workspace, "Building", buildSucceeded: false);
        }

        string? pauseLauncherPath = await WritePauseLauncherAsync(
            workspace,
            new[] { "\"Program.exe\"" },
            options,
            cancellationToken).ConfigureAwait(false);

        ProcessResult run = await Runner.RunAsync(
            ProcessCommand.ForCmd("Program.exe", workspace),
            ProgramTimeout,
            cancellationToken).ConfigureAwait(false);

        return FromProcessResults(
            status,
            buildOutput,
            run,
            workspace,
            "Running",
            pauseLauncherPath: pauseLauncherPath,
            totalDuration: TotalDuration(build, run));
    }
}

public sealed class MasmX64Toolchain : ToolchainBase
{
    private readonly VisualStudioLocator _visualStudioLocator;

    public MasmX64Toolchain(IProcessRunner runner, VisualStudioLocator visualStudioLocator)
        : base(runner)
    {
        _visualStudioLocator = visualStudioLocator;
    }

    public override TargetLanguage Language => TargetLanguage.MasmX64;

    public override async Task<ToolchainStatus> DetectAsync(CancellationToken cancellationToken)
    {
        VisualStudioTools? tools = await _visualStudioLocator.FindAsync(cancellationToken)
            .ConfigureAwait(false);

        return tools is null
            ? Missing("MASM x64 tools were not found. Install Visual Studio 2026 with Desktop development with C++.")
            : Available(tools.Version, tools.VcVars64Path, "MASM x64 tools detected.");
    }

    public override async Task<BuildRunResult> BuildAndRunAsync(
        GeneratedProgram generatedProgram,
        CancellationToken cancellationToken,
        BuildRunOptions? options = null)
    {
        ToolchainStatus status = await DetectAsync(cancellationToken).ConfigureAwait(false);
        if (!status.IsAvailable || status.Location is null)
        {
            return MissingResult(status);
        }

        string workspace = await WriteGeneratedProgramAsync(generatedProgram, cancellationToken)
            .ConfigureAwait(false);

        await WriteCommandScriptAsync(
            workspace,
            "assemble.cmd",
            new[]
            {
                "@echo off",
                $"call {QuoteForCmd(status.Location)} >nul",
                "if errorlevel 1 exit /b %errorlevel%",
                "ml64 /nologo /c Program.asm /Fo:Program.obj"
            },
            cancellationToken).ConfigureAwait(false);

        ProcessResult assemble = await Runner.RunAsync(
            ProcessCommand.ForCmd("assemble.cmd", workspace),
            BuildTimeout,
            cancellationToken).ConfigureAwait(false);

        string buildOutput = Combine(assemble);
        if (!assemble.Success)
        {
            return FromProcessResults(status, buildOutput, assemble, workspace, "Assembling", buildSucceeded: false);
        }

        await WriteCommandScriptAsync(
            workspace,
            "link-masm.cmd",
            new[]
            {
                "@echo off",
                $"call {QuoteForCmd(status.Location)} >nul",
                "if errorlevel 1 exit /b %errorlevel%",
                "link.exe /nologo Program.obj kernel32.lib /subsystem:console /entry:main /out:Program.exe"
            },
            cancellationToken).ConfigureAwait(false);

        ProcessResult link = await Runner.RunAsync(
            ProcessCommand.ForCmd("link-masm.cmd", workspace),
            BuildTimeout,
            cancellationToken).ConfigureAwait(false);

        buildOutput = JoinNonEmpty(buildOutput, Combine(link));
        if (!link.Success)
        {
            return FromProcessResults(
                status,
                buildOutput,
                link,
                workspace,
                "Linking",
                buildSucceeded: false,
                totalDuration: TotalDuration(assemble, link));
        }

        string? pauseLauncherPath = await WritePauseLauncherAsync(
            workspace,
            new[] { "\"Program.exe\"" },
            options,
            cancellationToken).ConfigureAwait(false);

        ProcessResult run = await Runner.RunAsync(
            ProcessCommand.ForCmd("Program.exe", workspace),
            ProgramTimeout,
            cancellationToken).ConfigureAwait(false);

        return FromProcessResults(
            status,
            buildOutput,
            run,
            workspace,
            "Running",
            pauseLauncherPath: pauseLauncherPath,
            totalDuration: TotalDuration(assemble, link, run));
    }
}

public sealed class NodeToolchain : ToolchainBase
{
    public NodeToolchain(IProcessRunner runner)
        : base(runner)
    {
    }

    public override TargetLanguage Language => TargetLanguage.JavaScript;

    public override async Task<ToolchainStatus> DetectAsync(CancellationToken cancellationToken)
    {
        ProcessResult result = await Runner.RunAsync(
            new ProcessCommand("node", new[] { "--version" }, Environment.CurrentDirectory),
            DetectionTimeout,
            cancellationToken).ConfigureAwait(false);

        return result.Success
            ? Available(JoinNonEmpty(result.StandardOutput, result.StandardError), "node", "Node.js detected.")
            : Missing("Node.js was not found. Install Node.js to run JavaScript output.");
    }

    public override async Task<BuildRunResult> BuildAndRunAsync(
        GeneratedProgram generatedProgram,
        CancellationToken cancellationToken,
        BuildRunOptions? options = null)
    {
        ToolchainStatus status = await DetectAsync(cancellationToken).ConfigureAwait(false);
        if (!status.IsAvailable)
        {
            return MissingResult(status);
        }

        string workspace = await WriteGeneratedProgramAsync(generatedProgram, cancellationToken)
            .ConfigureAwait(false);

        string? pauseLauncherPath = await WritePauseLauncherAsync(
            workspace,
            new[] { "node Program.js" },
            options,
            cancellationToken).ConfigureAwait(false);

        ProcessResult run = await Runner.RunAsync(
            new ProcessCommand("node", new[] { "Program.js" }, workspace),
            ProgramTimeout,
            cancellationToken).ConfigureAwait(false);

        return FromProcessResults(status, string.Empty, run, workspace, "Running", pauseLauncherPath: pauseLauncherPath);
    }
}

public sealed class JavaToolchain : ToolchainBase
{
    private sealed record JavaCommands(string Java, string Javac, string Location);

    private sealed record JavaDetection(JavaCommands Commands, string Version);

    public JavaToolchain(IProcessRunner runner)
        : base(runner)
    {
    }

    public override TargetLanguage Language => TargetLanguage.Java;

    public override async Task<ToolchainStatus> DetectAsync(CancellationToken cancellationToken)
    {
        JavaDetection? detection = await DetectInstallationAsync(cancellationToken).ConfigureAwait(false);
        if (detection is null)
        {
            return Missing("A full JDK was not found. Install a JDK with both javac and java.");
        }

        return Available(detection.Version, detection.Commands.Location, "JDK detected.");
    }

    public override async Task<BuildRunResult> BuildAndRunAsync(
        GeneratedProgram generatedProgram,
        CancellationToken cancellationToken,
        BuildRunOptions? options = null)
    {
        JavaDetection? detection = await DetectInstallationAsync(cancellationToken).ConfigureAwait(false);
        if (detection is null)
        {
            ToolchainStatus missing = Missing("A full JDK was not found. Install a JDK with both javac and java.");
            return MissingResult(missing);
        }

        ToolchainStatus status = Available(detection.Version, detection.Commands.Location, "JDK detected.");

        if (!status.IsAvailable)
        {
            // This branch is defensive; the status above is always available
            // when a JavaDetection exists.
            return MissingResult(status);
        }

        string workspace = await WriteGeneratedProgramAsync(generatedProgram, cancellationToken)
            .ConfigureAwait(false);

        ProcessResult build = await Runner.RunAsync(
            new ProcessCommand(detection.Commands.Javac, new[] { "-encoding", "UTF-8", "Program.java" }, workspace),
            BuildTimeout,
            cancellationToken).ConfigureAwait(false);

        string buildOutput = Combine(build);
        if (!build.Success)
        {
            return FromProcessResults(status, buildOutput, build, workspace, "Building", buildSucceeded: false);
        }

        string? pauseLauncherPath = await WritePauseLauncherAsync(
            workspace,
            new[] { BuildJavaProgramCommand(detection.Commands) },
            options,
            cancellationToken).ConfigureAwait(false);

        ProcessResult run = await Runner.RunAsync(
            new ProcessCommand(detection.Commands.Java, new[] { "Program" }, workspace),
            ProgramTimeout,
            cancellationToken).ConfigureAwait(false);

        return FromProcessResults(
            status,
            buildOutput,
            run,
            workspace,
            "Running",
            pauseLauncherPath: pauseLauncherPath,
            totalDuration: TotalDuration(build, run));
    }

    private async Task<JavaDetection?> DetectInstallationAsync(CancellationToken cancellationToken)
    {
        foreach (JavaCommands commands in EnumerateCommandCandidates())
        {
            ProcessResult javac = await Runner.RunAsync(
                new ProcessCommand(commands.Javac, new[] { "-version" }, Environment.CurrentDirectory),
                DetectionTimeout,
                cancellationToken).ConfigureAwait(false);

            ProcessResult java = await Runner.RunAsync(
                new ProcessCommand(commands.Java, new[] { "-version" }, Environment.CurrentDirectory),
                DetectionTimeout,
                cancellationToken).ConfigureAwait(false);

            if (!javac.Success || !java.Success)
            {
                continue;
            }

            string version = JoinNonEmpty(
                JoinNonEmpty(javac.StandardOutput, javac.StandardError),
                JoinNonEmpty(java.StandardOutput, java.StandardError));

            return new JavaDetection(commands, version);
        }

        return null;
    }

    private static IEnumerable<JavaCommands> EnumerateCommandCandidates()
    {
        var seenBins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // PATH remains the simplest and most portable setup, so it is always
        // tried before Windows-specific fallback probing.
        yield return new JavaCommands("java", "javac", "javac/java");

        foreach (string binDirectory in EnumerateJdkBinDirectories())
        {
            string normalizedBin = Path.GetFullPath(binDirectory);
            if (!seenBins.Add(normalizedBin))
            {
                continue;
            }

            string java = Path.Combine(normalizedBin, "java.exe");
            string javac = Path.Combine(normalizedBin, "javac.exe");
            if (File.Exists(java) && File.Exists(javac))
            {
                yield return new JavaCommands(java, javac, normalizedBin);
            }
        }
    }

    private static IEnumerable<string> EnumerateJdkBinDirectories()
    {
        foreach (string home in EnumerateJdkHomeCandidates())
        {
            string binDirectory = Path.Combine(home, "bin");
            if (Directory.Exists(binDirectory))
            {
                yield return binDirectory;
            }
        }
    }

    private static IEnumerable<string> EnumerateJdkHomeCandidates()
    {
        string? javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
        {
            yield return javaHome;
        }

        foreach (string programFilesRoot in EnumerateProgramFilesRoots())
        {
            foreach (string vendorRoot in EnumerateExistingDirectories(
                Path.Combine(programFilesRoot, "Microsoft"),
                Path.Combine(programFilesRoot, "Java"),
                Path.Combine(programFilesRoot, "Eclipse Adoptium")))
            {
                foreach (string jdkHome in EnumerateDirectories(vendorRoot, "jdk*"))
                {
                    yield return jdkHome;
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateProgramFilesRoots()
    {
        foreach (string variableName in new[] { "ProgramFiles", "ProgramFiles(x86)" })
        {
            string? root = Environment.GetEnvironmentVariable(variableName);
            if (!string.IsNullOrWhiteSpace(root))
            {
                yield return root;
            }
        }
    }

    private static IEnumerable<string> EnumerateExistingDirectories(params string[] paths) =>
        paths.Where(Directory.Exists);

    private static IEnumerable<string> EnumerateDirectories(string path, string searchPattern)
    {
        try
        {
            return Directory.EnumerateDirectories(path, searchPattern).ToArray();
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    private static string BuildJavaProgramCommand(JavaCommands commands) =>
        commands.Java.Equals("java", StringComparison.OrdinalIgnoreCase)
            ? "java Program"
            : QuoteForCmd(commands.Java) + " Program";
}

public sealed record VisualStudioTools(
    string InstallationPath,
    string VcVars64Path,
    string Version);

public sealed class VisualStudioLocator
{
    private readonly IProcessRunner _runner;
    private VisualStudioTools? _cachedTools;

    public VisualStudioLocator(IProcessRunner runner)
    {
        _runner = runner;
    }

    public async Task<VisualStudioTools?> FindAsync(CancellationToken cancellationToken)
    {
        if (_cachedTools is not null)
        {
            return _cachedTools;
        }

        // vswhere is the supported way to find Visual Studio installations.
        // Hardcoding a year, edition, or install folder would be brittle.
        string? programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string vswherePath = Path.Combine(
            programFilesX86,
            "Microsoft Visual Studio",
            "Installer",
            "vswhere.exe");

        if (!File.Exists(vswherePath))
        {
            return null;
        }

        ProcessResult installPathResult = await _runner.RunAsync(
            new ProcessCommand(
                vswherePath,
                new[]
                {
                    "-latest",
                    "-products",
                    "*",
                    "-requires",
                    "Microsoft.VisualStudio.Component.VC.Tools.x86.x64",
                    "-property",
                    "installationPath"
                },
                Environment.CurrentDirectory),
            ToolchainBase.DetectionTimeout,
            cancellationToken).ConfigureAwait(false);

        string installationPath = installPathResult.StandardOutput.Trim();
        if (!installPathResult.Success || string.IsNullOrWhiteSpace(installationPath))
        {
            return null;
        }

        string vcVars64Path = Path.Combine(installationPath, "VC", "Auxiliary", "Build", "vcvars64.bat");
        if (!File.Exists(vcVars64Path))
        {
            return null;
        }

        ProcessResult versionResult = await _runner.RunAsync(
            new ProcessCommand(
                vswherePath,
                new[]
                {
                    "-latest",
                    "-products",
                    "*",
                    "-requires",
                    "Microsoft.VisualStudio.Component.VC.Tools.x86.x64",
                    "-property",
                    "catalog_productDisplayVersion"
                },
                Environment.CurrentDirectory),
            ToolchainBase.DetectionTimeout,
            cancellationToken).ConfigureAwait(false);

        string version = versionResult.Success && !string.IsNullOrWhiteSpace(versionResult.StandardOutput)
            ? versionResult.StandardOutput.Trim()
            : "Visual Studio C++ tools";

        _cachedTools = new VisualStudioTools(installationPath, vcVars64Path, version);
        return _cachedTools;
    }
}
