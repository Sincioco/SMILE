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

        // The registry stays explicit: each target gets one clear toolchain
        // object, so detection and Build & Run behavior remain easy to trace.
        return new ToolchainRegistry(new IToolchain[]
        {
            new DotNetToolchain(runner),
            new MsvcCToolchain(runner, visualStudioLocator),
            new CobolToolchain(runner),
            new MasmX64Toolchain(runner, visualStudioLocator),
            new NodeToolchain(runner),
            new JavaToolchain(runner),
            new ObjectiveCToolchain(runner),
            new SwiftToolchain(runner, visualStudioLocator),
            new PythonToolchain(runner),
            new MsvcCppToolchain(runner, visualStudioLocator)
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
        CancellationToken cancellationToken,
        IEnumerable<string>? setupLines = null)
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
        if (setupLines is not null)
        {
            lines.AddRange(setupLines);
        }

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

    protected static string SetPathForCmd(IEnumerable<string?> directories)
    {
        // A small generated script is safer than mutating the parent process
        // environment. The compiler/runtime PATH applies only to this one
        // build or run, which keeps SMILE and the user's shell state tidy.
        string pathPrefix = string.Join(
            ";",
            directories
                .Where(directory => !string.IsNullOrWhiteSpace(directory))
                .Select(directory => directory!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase));

        return string.IsNullOrWhiteSpace(pathPrefix)
            ? "rem No additional PATH entries required."
            : $"set \"PATH={pathPrefix};%PATH%\"";
    }

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

public sealed class MsvcCppToolchain : ToolchainBase
{
    private readonly VisualStudioLocator _visualStudioLocator;

    public MsvcCppToolchain(IProcessRunner runner, VisualStudioLocator visualStudioLocator)
        : base(runner)
    {
        _visualStudioLocator = visualStudioLocator;
    }

    public override TargetLanguage Language => TargetLanguage.Cpp;

    public override async Task<ToolchainStatus> DetectAsync(CancellationToken cancellationToken)
    {
        VisualStudioTools? tools = await _visualStudioLocator.FindAsync(cancellationToken)
            .ConfigureAwait(false);

        return tools is null
            ? Missing("MSVC x64 C++ tools were not found. Install Visual Studio 2026 with Desktop development with C++.")
            : Available(tools.Version, tools.VcVars64Path, "MSVC x64 C++ tools detected.");
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
            "build-cpp.cmd",
            new[]
            {
                "@echo off",
                $"call {QuoteForCmd(status.Location)} >nul",
                "if errorlevel 1 exit /b %errorlevel%",
                "cl.exe /nologo /EHsc /std:c++20 /utf-8 Program.cpp /Fe:Program.exe"
            },
            cancellationToken).ConfigureAwait(false);

        ProcessResult build = await Runner.RunAsync(
            ProcessCommand.ForCmd("build-cpp.cmd", workspace),
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

public sealed class CobolToolchain : ToolchainBase
{
    private sealed record CobolCommands(
        string Cobc,
        string MingwBinDirectory,
        string MsysBinDirectory,
        string ConfigDirectory)
    {
        public IReadOnlyList<string> PathEntries { get; } =
            new[] { MingwBinDirectory, MsysBinDirectory };
    }

    private sealed record CobolDetection(
        CobolCommands? Commands,
        ToolchainStatus Status);

    public CobolToolchain(IProcessRunner runner)
        : base(runner)
    {
    }

    public override TargetLanguage Language => TargetLanguage.Cobol;

    public override async Task<ToolchainStatus> DetectAsync(CancellationToken cancellationToken)
    {
        CobolDetection detection = await DetectInstallationAsync(cancellationToken).ConfigureAwait(false);
        return detection.Status;
    }

    public override async Task<BuildRunResult> BuildAndRunAsync(
        GeneratedProgram generatedProgram,
        CancellationToken cancellationToken,
        BuildRunOptions? options = null)
    {
        CobolDetection detection = await DetectInstallationAsync(cancellationToken).ConfigureAwait(false);
        if (!detection.Status.IsAvailable || detection.Commands is null)
        {
            return MissingResult(detection.Status);
        }

        string workspace = await WriteGeneratedProgramAsync(generatedProgram, cancellationToken)
            .ConfigureAwait(false);

        string pathSetup = SetPathForCmd(detection.Commands.PathEntries);
        string configSetup = SetCobolConfigForCmd(detection.Commands.ConfigDirectory);
        await WriteCommandScriptAsync(
            workspace,
            "build-cobol.cmd",
            new[]
            {
                "@echo off",
                "cd /d \"%~dp0\"",
                pathSetup,
                configSetup,
                $"{QuoteForCmd(detection.Commands.Cobc)} -x -free Program.cob -o Program.exe"
            },
            cancellationToken).ConfigureAwait(false);

        ProcessResult build = await Runner.RunAsync(
            ProcessCommand.ForCmd("build-cobol.cmd", workspace),
            BuildTimeout,
            cancellationToken).ConfigureAwait(false);

        string buildOutput = Combine(build);
        if (!build.Success)
        {
            return FromProcessResults(detection.Status, buildOutput, build, workspace, "Building", buildSucceeded: false);
        }

        await WriteCommandScriptAsync(
            workspace,
            "run-cobol.cmd",
            new[]
            {
                "@echo off",
                "cd /d \"%~dp0\"",
                pathSetup,
                configSetup,
                "Program.exe"
            },
            cancellationToken).ConfigureAwait(false);

        string? pauseLauncherPath = await WritePauseLauncherAsync(
            workspace,
            new[] { "\"Program.exe\"" },
            options,
            cancellationToken,
            setupLines: new[] { pathSetup, configSetup }).ConfigureAwait(false);

        ProcessResult run = await Runner.RunAsync(
            ProcessCommand.ForCmd("run-cobol.cmd", workspace),
            ProgramTimeout,
            cancellationToken).ConfigureAwait(false);

        return FromProcessResults(
            detection.Status,
            buildOutput,
            run,
            workspace,
            "Running",
            pauseLauncherPath: pauseLauncherPath,
            totalDuration: TotalDuration(build, run));
    }

    private async Task<CobolDetection> DetectInstallationAsync(CancellationToken cancellationToken)
    {
        foreach (CobolCommands commands in EnumerateCommandCandidates())
        {
            ProcessResult cobc = await Runner.RunAsync(
                new ProcessCommand(commands.Cobc, new[] { "--version" }, Environment.CurrentDirectory),
                DetectionTimeout,
                cancellationToken).ConfigureAwait(false);

            if (!cobc.Success)
            {
                continue;
            }

            string version = JoinNonEmpty(cobc.StandardOutput, cobc.StandardError);
            return new CobolDetection(
                commands,
                Available(version, commands.Cobc, "GnuCOBOL toolchain detected."));
        }

        return new CobolDetection(
            null,
            Missing("GnuCOBOL was not found. Install MSYS2 with mingw-w64-x86_64-gnucobol to build COBOL locally."));
    }

    private static IEnumerable<CobolCommands> EnumerateCommandCandidates()
    {
        var seenCompilers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string directory in EnumeratePathDirectories())
        {
            CobolCommands? commands = CreateCommandsFromBinDirectory(directory);
            if (commands is not null && seenCompilers.Add(commands.Cobc))
            {
                yield return commands;
            }
        }

        foreach (string root in EnumerateMsysRoots())
        {
            string mingwBin = Path.Combine(root, "mingw64", "bin");
            CobolCommands? commands = CreateCommandsFromBinDirectory(mingwBin);
            if (commands is not null && seenCompilers.Add(commands.Cobc))
            {
                yield return commands;
            }
        }
    }

    private static CobolCommands? CreateCommandsFromBinDirectory(string binDirectory)
    {
        string cobc = Path.Combine(binDirectory, "cobc.exe");
        if (!File.Exists(cobc))
        {
            return null;
        }

        string? mingwDirectory = Directory.GetParent(binDirectory)?.FullName;
        string? msysRoot = mingwDirectory is null
            ? null
            : Directory.GetParent(mingwDirectory)?.FullName;
        if (mingwDirectory is null || msysRoot is null)
        {
            return null;
        }

        string configDirectory = Path.Combine(mingwDirectory, "share", "gnucobol", "config");
        if (!File.Exists(Path.Combine(configDirectory, "default.conf")))
        {
            return null;
        }

        string msysBin = Path.Combine(msysRoot, "usr", "bin");
        return new CobolCommands(cobc, binDirectory, msysBin, configDirectory);
    }

    private static IEnumerable<string> EnumeratePathDirectories()
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            yield break;
        }

        foreach (string entry in path.Split(Path.PathSeparator))
        {
            if (!string.IsNullOrWhiteSpace(entry) && Directory.Exists(entry))
            {
                yield return entry;
            }
        }
    }

    private static IEnumerable<string> EnumerateMsysRoots()
    {
        string? configuredRoot = Environment.GetEnvironmentVariable("MSYS2_ROOT");
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            yield return configuredRoot;
        }

        yield return @"C:\msys64";
    }

    private static string SetCobolConfigForCmd(string configDirectory) =>
        $"set \"COB_CONFIG_DIR={configDirectory}\"";
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

public sealed class PythonToolchain : ToolchainBase
{
    private sealed record PythonCommand(
        string Executable,
        IReadOnlyList<string> PrefixArguments,
        string DisplayCommand);

    private sealed record PythonDetection(
        PythonCommand Command,
        string VersionText);

    public PythonToolchain(IProcessRunner runner)
        : base(runner)
    {
    }

    public override TargetLanguage Language => TargetLanguage.Python;

    public override async Task<ToolchainStatus> DetectAsync(CancellationToken cancellationToken)
    {
        PythonDetection? detection = await DetectInstallationAsync(cancellationToken).ConfigureAwait(false);
        return detection is null
            ? Missing("Python 3.10 or newer was not found. Install Python 3 to run Python output.")
            : Available(
                detection.VersionText,
                detection.Command.Executable,
                $"{detection.VersionText} detected via {detection.Command.DisplayCommand}.");
    }

    public override async Task<BuildRunResult> BuildAndRunAsync(
        GeneratedProgram generatedProgram,
        CancellationToken cancellationToken,
        BuildRunOptions? options = null)
    {
        PythonDetection? detection = await DetectInstallationAsync(cancellationToken).ConfigureAwait(false);
        if (detection is null)
        {
            return MissingResult(Missing(
                "Python 3.10 or newer was not found. Install Python 3 to run Python output."));
        }

        ToolchainStatus status = Available(
            detection.VersionText,
            detection.Command.Executable,
            $"{detection.VersionText} detected via {detection.Command.DisplayCommand}.");
        string workspace = await WriteGeneratedProgramAsync(generatedProgram, cancellationToken)
            .ConfigureAwait(false);
        string[] arguments = detection.Command.PrefixArguments
            .Concat(new[] { "-B", "Program.py" })
            .ToArray();
        string launcherCommand = string.Join(
            " ",
            new[] { QuoteForCmd(detection.Command.Executable) }
                .Concat(detection.Command.PrefixArguments)
                .Concat(new[] { "-B", "Program.py" }));

        string? pauseLauncherPath = await WritePauseLauncherAsync(
            workspace,
            new[] { launcherCommand },
            options,
            cancellationToken).ConfigureAwait(false);

        ProcessResult run = await Runner.RunAsync(
            new ProcessCommand(detection.Command.Executable, arguments, workspace),
            ProgramTimeout,
            cancellationToken).ConfigureAwait(false);

        return FromProcessResults(
            status,
            string.Empty,
            run,
            workspace,
            "Running",
            pauseLauncherPath: pauseLauncherPath);
    }

    private async Task<PythonDetection?> DetectInstallationAsync(CancellationToken cancellationToken)
    {
        foreach (PythonCommand command in EnumerateCommandCandidates())
        {
            string[] arguments = command.PrefixArguments.Concat(new[] { "--version" }).ToArray();
            ProcessResult result = await Runner.RunAsync(
                new ProcessCommand(command.Executable, arguments, Environment.CurrentDirectory),
                DetectionTimeout,
                cancellationToken).ConfigureAwait(false);
            string output = JoinNonEmpty(result.StandardOutput, result.StandardError);

            if (!result.Success || !TryReadSupportedVersion(output, out string versionText))
            {
                continue;
            }

            return new PythonDetection(command, versionText);
        }

        return null;
    }

    private static IEnumerable<PythonCommand> EnumerateCommandCandidates()
    {
        string? python = FindExecutableOnPath("python.exe", skipWindowsStoreAlias: true);
        if (python is not null)
        {
            yield return new PythonCommand(python, Array.Empty<string>(), "python");
        }

        string? launcher = FindExecutableOnPath("py.exe", skipWindowsStoreAlias: false);
        if (launcher is not null)
        {
            yield return new PythonCommand(launcher, new[] { "-3" }, "py -3");
            yield return new PythonCommand(launcher, Array.Empty<string>(), "py");
        }
    }

    private static string? FindExecutableOnPath(string fileName, bool skipWindowsStoreAlias)
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (string entry in path.Split(Path.PathSeparator))
        {
            string directory = entry.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            string candidate;
            try
            {
                candidate = Path.GetFullPath(Path.Combine(directory, fileName));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            if (!File.Exists(candidate))
            {
                continue;
            }

            if (skipWindowsStoreAlias &&
                candidate.Contains(
                    Path.DirectorySeparatorChar + "Microsoft" + Path.DirectorySeparatorChar + "WindowsApps" + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return candidate;
        }

        return null;
    }

    private static bool TryReadSupportedVersion(string output, out string versionText)
    {
        versionText = string.Empty;
        const string prefix = "Python ";
        int marker = output.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
        {
            return false;
        }

        string token = output[(marker + prefix.Length)..]
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;
        int suffix = token.IndexOfAny(new[] { '-', '+' });
        if (suffix >= 0)
        {
            token = token[..suffix];
        }

        if (!Version.TryParse(token, out Version? version) ||
            version.Major < 3 ||
            (version.Major == 3 && version.Minor < 10))
        {
            return false;
        }

        versionText = prefix + version;
        return true;
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

public sealed class ObjectiveCToolchain : ToolchainBase
{
    private sealed record ObjectiveCCommands(
        string Clang,
        string MingwBinDirectory,
        string MsysBinDirectory)
    {
        public IReadOnlyList<string> PathEntries { get; } =
            new[] { MingwBinDirectory, MsysBinDirectory };
    }

    private sealed record ObjectiveCDetection(
        ObjectiveCCommands Commands,
        ToolchainStatus Status);

    public ObjectiveCToolchain(IProcessRunner runner)
        : base(runner)
    {
    }

    public override TargetLanguage Language => TargetLanguage.ObjectiveC;

    public override async Task<ToolchainStatus> DetectAsync(CancellationToken cancellationToken)
    {
        ObjectiveCDetection detection = await DetectInstallationAsync(cancellationToken).ConfigureAwait(false);
        return detection.Status;
    }

    public override async Task<BuildRunResult> BuildAndRunAsync(
        GeneratedProgram generatedProgram,
        CancellationToken cancellationToken,
        BuildRunOptions? options = null)
    {
        ObjectiveCDetection detection = await DetectInstallationAsync(cancellationToken).ConfigureAwait(false);
        if (!detection.Status.IsAvailable)
        {
            return MissingResult(detection.Status);
        }

        string workspace = await WriteGeneratedProgramAsync(generatedProgram, cancellationToken)
            .ConfigureAwait(false);

        string pathSetup = SetPathForCmd(detection.Commands.PathEntries);
        await WriteCommandScriptAsync(
            workspace,
            "build-objective-c.cmd",
            new[]
            {
                "@echo off",
                "cd /d \"%~dp0\"",
                pathSetup,
                $"{QuoteForCmd(detection.Commands.Clang)} -x objective-c Program.m -o Program.exe"
            },
            cancellationToken).ConfigureAwait(false);

        ProcessResult build = await Runner.RunAsync(
            ProcessCommand.ForCmd("build-objective-c.cmd", workspace),
            BuildTimeout,
            cancellationToken).ConfigureAwait(false);

        string buildOutput = Combine(build);
        if (!build.Success)
        {
            return FromProcessResults(detection.Status, buildOutput, build, workspace, "Building", buildSucceeded: false);
        }

        await WriteCommandScriptAsync(
            workspace,
            "run-objective-c.cmd",
            new[]
            {
                "@echo off",
                "cd /d \"%~dp0\"",
                pathSetup,
                "Program.exe"
            },
            cancellationToken).ConfigureAwait(false);

        string? pauseLauncherPath = await WritePauseLauncherAsync(
            workspace,
            new[] { "\"Program.exe\"" },
            options,
            cancellationToken,
            setupLines: new[] { pathSetup }).ConfigureAwait(false);

        ProcessResult run = await Runner.RunAsync(
            ProcessCommand.ForCmd("run-objective-c.cmd", workspace),
            ProgramTimeout,
            cancellationToken).ConfigureAwait(false);

        return FromProcessResults(
            detection.Status,
            buildOutput,
            run,
            workspace,
            "Running",
            pauseLauncherPath: pauseLauncherPath,
            totalDuration: TotalDuration(build, run));
    }

    private async Task<ObjectiveCDetection> DetectInstallationAsync(CancellationToken cancellationToken)
    {
        foreach (ObjectiveCCommands commands in EnumerateCommandCandidates())
        {
            ProcessResult clang = await Runner.RunAsync(
                new ProcessCommand(commands.Clang, new[] { "--version" }, Environment.CurrentDirectory),
                DetectionTimeout,
                cancellationToken).ConfigureAwait(false);

            if (!clang.Success)
            {
                continue;
            }

            string version = JoinNonEmpty(clang.StandardOutput, clang.StandardError);
            return new ObjectiveCDetection(
                commands,
                Available(version, commands.Clang, "MSYS2 Clang Objective-C toolchain detected."));
        }

        return new ObjectiveCDetection(
            new ObjectiveCCommands("clang", string.Empty, string.Empty),
            Missing("MSYS2 Clang was not found. Install MSYS2 with mingw-w64-x86_64-clang to build Objective-C locally."));
    }

    private static IEnumerable<ObjectiveCCommands> EnumerateCommandCandidates()
    {
        var roots = new List<string?>();
        roots.Add(Environment.GetEnvironmentVariable("MSYS2_ROOT"));
        roots.Add(@"C:\msys64");

        var seenRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string? root in roots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            string fullRoot;
            try
            {
                fullRoot = Path.GetFullPath(root);
            }
            catch (Exception ex) when (IsExpectedProbeException(ex))
            {
                continue;
            }

            if (!seenRoots.Add(fullRoot))
            {
                continue;
            }

            string mingwBin = Path.Combine(fullRoot, "mingw64", "bin");
            string msysBin = Path.Combine(fullRoot, "usr", "bin");
            string clang = Path.Combine(mingwBin, "clang.exe");
            if (File.Exists(clang))
            {
                yield return new ObjectiveCCommands(clang, mingwBin, msysBin);
            }
        }
    }

    private static bool IsExpectedProbeException(Exception exception) =>
        exception is IOException or
            UnauthorizedAccessException or
            ArgumentException or
            NotSupportedException or
            PathTooLongException;
}

public sealed class SwiftToolchain : ToolchainBase
{
    private sealed record SwiftCommands(
        string SwiftCompiler,
        string SwiftSdkPath,
        string ToolchainBinDirectory,
        string RuntimeBinDirectory,
        string? RuntimeSwiftDirectory,
        string? RuntimeSwiftArchitectureDirectory,
        string? PythonBinDirectory)
    {
        public IReadOnlyList<string?> PathEntries { get; } =
            new[]
            {
                ToolchainBinDirectory,
                RuntimeBinDirectory,
                RuntimeSwiftDirectory,
                RuntimeSwiftArchitectureDirectory,
                PythonBinDirectory
            };
    }

    private sealed record SwiftDetection(
        SwiftCommands? Commands,
        VisualStudioTools? VisualStudio,
        ToolchainStatus Status);

    private readonly VisualStudioLocator _visualStudioLocator;

    public SwiftToolchain(IProcessRunner runner, VisualStudioLocator visualStudioLocator)
        : base(runner)
    {
        _visualStudioLocator = visualStudioLocator;
    }

    public override TargetLanguage Language => TargetLanguage.Swift;

    public override async Task<ToolchainStatus> DetectAsync(CancellationToken cancellationToken)
    {
        SwiftDetection detection = await DetectInstallationAsync(cancellationToken).ConfigureAwait(false);
        return detection.Status;
    }

    public override async Task<BuildRunResult> BuildAndRunAsync(
        GeneratedProgram generatedProgram,
        CancellationToken cancellationToken,
        BuildRunOptions? options = null)
    {
        SwiftDetection detection = await DetectInstallationAsync(cancellationToken).ConfigureAwait(false);
        if (!detection.Status.IsAvailable || detection.Commands is null || detection.VisualStudio is null)
        {
            return MissingResult(detection.Status);
        }

        string workspace = await WriteGeneratedProgramAsync(generatedProgram, cancellationToken)
            .ConfigureAwait(false);

        string pathSetup = SetPathForCmd(detection.Commands.PathEntries);
        await WriteCommandScriptAsync(
            workspace,
            "build-swift.cmd",
            new[]
            {
                "@echo off",
                "cd /d \"%~dp0\"",
                $"call {QuoteForCmd(detection.VisualStudio.VcVars64Path)} >nul",
                "if errorlevel 1 exit /b %errorlevel%",
                pathSetup,
                $"{QuoteForCmd(detection.Commands.SwiftCompiler)} -sdk {QuoteForCmd(detection.Commands.SwiftSdkPath)} Program.swift -o Program.exe"
            },
            cancellationToken).ConfigureAwait(false);

        ProcessResult build = await Runner.RunAsync(
            ProcessCommand.ForCmd("build-swift.cmd", workspace),
            BuildTimeout,
            cancellationToken).ConfigureAwait(false);

        string buildOutput = Combine(build);
        if (!build.Success)
        {
            return FromProcessResults(detection.Status, buildOutput, build, workspace, "Building", buildSucceeded: false);
        }

        await WriteCommandScriptAsync(
            workspace,
            "run-swift.cmd",
            new[]
            {
                "@echo off",
                "cd /d \"%~dp0\"",
                pathSetup,
                "Program.exe"
            },
            cancellationToken).ConfigureAwait(false);

        string? pauseLauncherPath = await WritePauseLauncherAsync(
            workspace,
            new[] { "\"Program.exe\"" },
            options,
            cancellationToken,
            setupLines: new[] { pathSetup }).ConfigureAwait(false);

        ProcessResult run = await Runner.RunAsync(
            ProcessCommand.ForCmd("run-swift.cmd", workspace),
            ProgramTimeout,
            cancellationToken).ConfigureAwait(false);

        return FromProcessResults(
            detection.Status,
            buildOutput,
            run,
            workspace,
            "Running",
            pauseLauncherPath: pauseLauncherPath,
            totalDuration: TotalDuration(build, run));
    }

    private async Task<SwiftDetection> DetectInstallationAsync(CancellationToken cancellationToken)
    {
        foreach (SwiftCommands commands in EnumerateCommandCandidates())
        {
            VisualStudioTools? visualStudio = await _visualStudioLocator.FindAsync(cancellationToken)
                .ConfigureAwait(false);
            if (visualStudio is null)
            {
                return new SwiftDetection(
                    commands,
                    null,
                    Missing("Swift was found, but Visual Studio C++ linker tools were not found. Install the Visual Studio Desktop development with C++ workload."));
            }

            string version = await ReadSwiftVersionAsync(commands, cancellationToken).ConfigureAwait(false);
            string combinedVersion = JoinNonEmpty(version, visualStudio.Version);
            return new SwiftDetection(
                commands,
                visualStudio,
                Available(combinedVersion, commands.SwiftCompiler, "Swift for Windows toolchain detected."));
        }

        return new SwiftDetection(
            null,
            null,
            Missing("Swift for Windows was not found. Install the free Swift.Toolchain package to build Swift locally."));
    }

    private async Task<string> ReadSwiftVersionAsync(
        SwiftCommands commands,
        CancellationToken cancellationToken)
    {
        ProcessResult swift = await Runner.RunAsync(
            ProcessCommand.ForCmd(
                $"{SetPathForCmd(commands.PathEntries)} && {QuoteForCmd(commands.SwiftCompiler)} --version",
                Environment.CurrentDirectory),
            DetectionTimeout,
            cancellationToken).ConfigureAwait(false);

        // Version output is helpful in the UI, but failing to print a version
        // should not hide an otherwise valid installed compiler. The build
        // step is the real proof that the local Swift setup works.
        return swift.Success
            ? JoinNonEmpty(swift.StandardOutput, swift.StandardError)
            : "Swift compiler";
    }

    private static IEnumerable<SwiftCommands> EnumerateCommandCandidates()
    {
        var seenCompilers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string swiftCompiler in EnumerateSwiftCompilers())
        {
            string fullCompiler;
            try
            {
                fullCompiler = Path.GetFullPath(swiftCompiler);
            }
            catch (Exception ex) when (IsExpectedProbeException(ex))
            {
                continue;
            }

            if (!seenCompilers.Add(fullCompiler))
            {
                continue;
            }

            SwiftCommands? commands = CreateCommands(fullCompiler);
            if (commands is not null)
            {
                yield return commands;
            }
        }
    }

    private static IEnumerable<string> EnumerateSwiftCompilers()
    {
        foreach (string pathDirectory in EnumeratePathDirectories())
        {
            string swiftCompiler = Path.Combine(pathDirectory, "swiftc.exe");
            if (File.Exists(swiftCompiler))
            {
                yield return swiftCompiler;
            }
        }

        foreach (string root in EnumerateSwiftRoots())
        {
            string toolchainsRoot = Path.Combine(root, "Toolchains");
            foreach (string swiftCompiler in EnumerateFiles(toolchainsRoot, "swiftc.exe"))
            {
                yield return swiftCompiler;
            }
        }
    }

    private static SwiftCommands? CreateCommands(string swiftCompiler)
    {
        string? swiftRoot = FindSwiftRoot(swiftCompiler);
        string? toolchainDirectory = FindToolchainDirectory(swiftCompiler);
        if (swiftRoot is null || toolchainDirectory is null)
        {
            return null;
        }

        string? toolchainVersion = GetToolchainVersion(toolchainDirectory);
        string? sdkPath = FindWindowsSdk(swiftRoot, toolchainVersion);
        string? runtimeBin = FindRuntimeBin(swiftRoot, toolchainVersion);
        string toolchainBin = Path.GetDirectoryName(swiftCompiler) ?? string.Empty;
        if (sdkPath is null || runtimeBin is null || string.IsNullOrWhiteSpace(toolchainBin))
        {
            return null;
        }

        string runtimeSwift = Path.Combine(toolchainDirectory, "usr", "lib", "swift", "windows");
        string runtimeSwiftArchitecture = Path.Combine(runtimeSwift, "x86_64");
        string? pythonBin = FindPythonBin(swiftRoot);

        return new SwiftCommands(
            swiftCompiler,
            sdkPath,
            toolchainBin,
            runtimeBin,
            Directory.Exists(runtimeSwift) ? runtimeSwift : null,
            Directory.Exists(runtimeSwiftArchitecture) ? runtimeSwiftArchitecture : null,
            pythonBin);
    }

    private static IEnumerable<string> EnumeratePathDirectories()
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            yield break;
        }

        foreach (string entry in path.Split(Path.PathSeparator))
        {
            if (!string.IsNullOrWhiteSpace(entry) && Directory.Exists(entry))
            {
                yield return entry;
            }
        }
    }

    private static IEnumerable<string> EnumerateSwiftRoots()
    {
        var rootCandidates = new List<string?>();
        rootCandidates.Add(Environment.GetEnvironmentVariable("SWIFT_ROOT"));
        rootCandidates.Add(Environment.GetEnvironmentVariable("LOCALAPPDATA"));
        rootCandidates.Add(Environment.GetEnvironmentVariable("ProgramFiles"));
        rootCandidates.Add(Environment.GetEnvironmentVariable("ProgramFiles(x86)"));

        foreach (Environment.SpecialFolder folder in new[]
        {
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolder.ProgramFiles,
            Environment.SpecialFolder.ProgramFilesX86
        })
        {
            rootCandidates.Add(Environment.GetFolderPath(folder));
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string? candidate in rootCandidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            string root;
            try
            {
                root = Path.GetFullPath(candidate);
            }
            catch (Exception ex) when (IsExpectedProbeException(ex))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            foreach (string swiftRoot in new[]
            {
                root,
                Path.Combine(root, "Programs", "Swift"),
                Path.Combine(root, "Swift")
            })
            {
                if (!Directory.Exists(swiftRoot))
                {
                    continue;
                }

                string fullSwiftRoot = Path.GetFullPath(swiftRoot);
                if (seen.Add(fullSwiftRoot))
                {
                    yield return fullSwiftRoot;
                }
            }
        }
    }

    private static string? FindSwiftRoot(string swiftCompiler)
    {
        DirectoryInfo? directory = new FileInfo(swiftCompiler).Directory;
        while (directory is not null)
        {
            if (directory.Name.Equals("Toolchains", StringComparison.OrdinalIgnoreCase))
            {
                return directory.Parent?.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static string? FindToolchainDirectory(string swiftCompiler)
    {
        DirectoryInfo? directory = new FileInfo(swiftCompiler).Directory;
        while (directory is not null)
        {
            if (directory.Parent?.Name.Equals("Toolchains", StringComparison.OrdinalIgnoreCase) == true)
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static string? GetToolchainVersion(string toolchainDirectory)
    {
        string name = Path.GetFileName(toolchainDirectory);
        int plus = name.IndexOf('+', StringComparison.Ordinal);
        return plus > 0 ? name[..plus] : name;
    }

    private static string? FindWindowsSdk(string swiftRoot, string? toolchainVersion)
    {
        string platformsRoot = Path.Combine(swiftRoot, "Platforms");
        foreach (string versionDirectory in EnumeratePreferredVersionDirectories(platformsRoot, toolchainVersion))
        {
            string sdkPath = Path.Combine(
                versionDirectory,
                "Windows.platform",
                "Developer",
                "SDKs",
                "Windows.sdk");
            if (Directory.Exists(sdkPath))
            {
                return sdkPath;
            }
        }

        return null;
    }

    private static string? FindRuntimeBin(string swiftRoot, string? toolchainVersion)
    {
        string runtimesRoot = Path.Combine(swiftRoot, "Runtimes");
        foreach (string versionDirectory in EnumeratePreferredVersionDirectories(runtimesRoot, toolchainVersion))
        {
            string runtimeBin = Path.Combine(versionDirectory, "usr", "bin");
            if (File.Exists(Path.Combine(runtimeBin, "swiftCore.dll")))
            {
                return runtimeBin;
            }
        }

        return null;
    }

    private static string? FindPythonBin(string swiftRoot)
    {
        foreach (string python in EnumerateFiles(swiftRoot, "python.exe"))
        {
            return Path.GetDirectoryName(python);
        }

        return null;
    }

    private static IEnumerable<string> EnumeratePreferredVersionDirectories(string root, string? preferredVersion)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(preferredVersion))
        {
            string preferred = Path.Combine(root, preferredVersion);
            if (Directory.Exists(preferred))
            {
                yield return preferred;
            }
        }

        foreach (string directory in EnumerateDirectories(root, "*").OrderByDescending(Path.GetFileName))
        {
            if (!string.IsNullOrWhiteSpace(preferredVersion) &&
                Path.GetFileName(directory).Equals(preferredVersion, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return directory;
        }
    }

    private static IEnumerable<string> EnumerateDirectories(string path, string searchPattern)
    {
        try
        {
            return Directory.EnumerateDirectories(path, searchPattern).ToArray();
        }
        catch (Exception ex) when (IsExpectedProbeException(ex))
        {
            return Array.Empty<string>();
        }
    }

    private static IEnumerable<string> EnumerateFiles(string path, string searchPattern)
    {
        try
        {
            return Directory.EnumerateFiles(path, searchPattern, SearchOption.AllDirectories).ToArray();
        }
        catch (Exception ex) when (IsExpectedProbeException(ex))
        {
            return Array.Empty<string>();
        }
    }

    private static bool IsExpectedProbeException(Exception exception) =>
        exception is IOException or
            UnauthorizedAccessException or
            ArgumentException or
            NotSupportedException or
            PathTooLongException;
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
