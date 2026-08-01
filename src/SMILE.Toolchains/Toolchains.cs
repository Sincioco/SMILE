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
    string Stage);

public interface IToolchain
{
    TargetLanguage Language { get; }

    // Detection is separate from build/run so the UI can show availability
    // without forcing a compile just to learn whether a tool exists.
    Task<ToolchainStatus> DetectAsync(CancellationToken cancellationToken);

    Task<BuildRunResult> BuildAndRunAsync(
        GeneratedProgram generatedProgram,
        CancellationToken cancellationToken);
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

        // The registry is intentionally small: five target languages, five
        // toolchains, no plugin system for v0.1.
        return new ToolchainRegistry(new IToolchain[]
        {
            new DotNetToolchain(runner),
            new MsvcCToolchain(runner, visualStudioLocator),
            new MasmX64Toolchain(runner, visualStudioLocator),
            new NodeToolchain(runner),
            new JavaToolchain(runner)
        });
    }

    public IToolchain Get(TargetLanguage language) => _toolchains[language];

    public IReadOnlyList<IToolchain> All => _toolchains.Values.ToArray();
}

public abstract class ToolchainBase : IToolchain
{
    public static readonly TimeSpan DetectionTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan ProgramTimeout = TimeSpan.FromSeconds(10);
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    protected ToolchainBase(IProcessRunner runner)
    {
        Runner = runner;
    }

    public abstract TargetLanguage Language { get; }

    protected IProcessRunner Runner { get; }

    public abstract Task<ToolchainStatus> DetectAsync(CancellationToken cancellationToken);

    public abstract Task<BuildRunResult> BuildAndRunAsync(
        GeneratedProgram generatedProgram,
        CancellationToken cancellationToken);

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
        CleanOldWorkspaces(root);

        string workspace = Path.Combine(
            root,
            DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N"));
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

    protected async Task WriteCommandScriptAsync(
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

        await File.WriteAllLinesAsync(scriptPath, lines, cancellationToken).ConfigureAwait(false);
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
            "Missing toolchain");

    protected BuildRunResult FromProcessResults(
        ToolchainStatus status,
        string buildOutput,
        ProcessResult runResult,
        string workingDirectory,
        string stage,
        bool buildSucceeded = true)
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
            runResult.Duration,
            runResult.TimedOut,
            runResult.Cancelled,
            workingDirectory,
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

    private static void CleanOldWorkspaces(string root)
    {
        string rootFullPath = Path.GetFullPath(root);
        var cutoff = DateTime.UtcNow.AddDays(-2);

        foreach (string directory in Directory.EnumerateDirectories(rootFullPath))
        {
            string fullPath = Path.GetFullPath(directory);
            if (!fullPath.StartsWith(rootFullPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var info = new DirectoryInfo(fullPath);
                if (info.LastWriteTimeUtc < cutoff)
                {
                    info.Delete(recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
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
        CancellationToken cancellationToken)
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
            ProgramTimeout,
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
                "Building");
        }

        ProcessResult run = await Runner.RunAsync(
            new ProcessCommand(
                "dotnet",
                new[] { "run", "--project", "GeneratedProgram.csproj", "--no-build" },
                workspace),
            ProgramTimeout,
            cancellationToken).ConfigureAwait(false);

        return FromProcessResults(status, buildOutput, run, workspace, "Running");
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
        CancellationToken cancellationToken)
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
            ProgramTimeout,
            cancellationToken).ConfigureAwait(false);

        string buildOutput = Combine(build);
        if (!build.Success)
        {
            return FromProcessResults(status, buildOutput, build, workspace, "Building", buildSucceeded: false);
        }

        ProcessResult run = await Runner.RunAsync(
            ProcessCommand.ForCmd("Program.exe", workspace),
            ProgramTimeout,
            cancellationToken).ConfigureAwait(false);

        return FromProcessResults(status, buildOutput, run, workspace, "Running");
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
        CancellationToken cancellationToken)
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
            ProgramTimeout,
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
            ProgramTimeout,
            cancellationToken).ConfigureAwait(false);

        buildOutput = JoinNonEmpty(buildOutput, Combine(link));
        if (!link.Success)
        {
            return FromProcessResults(status, buildOutput, link, workspace, "Linking", buildSucceeded: false);
        }

        ProcessResult run = await Runner.RunAsync(
            ProcessCommand.ForCmd("Program.exe", workspace),
            ProgramTimeout,
            cancellationToken).ConfigureAwait(false);

        return FromProcessResults(status, buildOutput, run, workspace, "Running");
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
        CancellationToken cancellationToken)
    {
        ToolchainStatus status = await DetectAsync(cancellationToken).ConfigureAwait(false);
        if (!status.IsAvailable)
        {
            return MissingResult(status);
        }

        string workspace = await WriteGeneratedProgramAsync(generatedProgram, cancellationToken)
            .ConfigureAwait(false);

        ProcessResult run = await Runner.RunAsync(
            new ProcessCommand("node", new[] { "Program.js" }, workspace),
            ProgramTimeout,
            cancellationToken).ConfigureAwait(false);

        return FromProcessResults(status, string.Empty, run, workspace, "Running");
    }
}

public sealed class JavaToolchain : ToolchainBase
{
    public JavaToolchain(IProcessRunner runner)
        : base(runner)
    {
    }

    public override TargetLanguage Language => TargetLanguage.Java;

    public override async Task<ToolchainStatus> DetectAsync(CancellationToken cancellationToken)
    {
        ProcessResult javac = await Runner.RunAsync(
            new ProcessCommand("javac", new[] { "-version" }, Environment.CurrentDirectory),
            DetectionTimeout,
            cancellationToken).ConfigureAwait(false);

        ProcessResult java = await Runner.RunAsync(
            new ProcessCommand("java", new[] { "-version" }, Environment.CurrentDirectory),
            DetectionTimeout,
            cancellationToken).ConfigureAwait(false);

        if (!javac.Success || !java.Success)
        {
            return Missing("A full JDK was not found. Install a JDK with both javac and java.");
        }

        string version = JoinNonEmpty(
            JoinNonEmpty(javac.StandardOutput, javac.StandardError),
            JoinNonEmpty(java.StandardOutput, java.StandardError));

        return Available(version, "javac/java", "JDK detected.");
    }

    public override async Task<BuildRunResult> BuildAndRunAsync(
        GeneratedProgram generatedProgram,
        CancellationToken cancellationToken)
    {
        ToolchainStatus status = await DetectAsync(cancellationToken).ConfigureAwait(false);
        if (!status.IsAvailable)
        {
            return MissingResult(status);
        }

        string workspace = await WriteGeneratedProgramAsync(generatedProgram, cancellationToken)
            .ConfigureAwait(false);

        ProcessResult build = await Runner.RunAsync(
            new ProcessCommand("javac", new[] { "Program.java" }, workspace),
            ProgramTimeout,
            cancellationToken).ConfigureAwait(false);

        string buildOutput = Combine(build);
        if (!build.Success)
        {
            return FromProcessResults(status, buildOutput, build, workspace, "Building", buildSucceeded: false);
        }

        ProcessResult run = await Runner.RunAsync(
            new ProcessCommand("java", new[] { "Program" }, workspace),
            ProgramTimeout,
            cancellationToken).ConfigureAwait(false);

        return FromProcessResults(status, buildOutput, run, workspace, "Running");
    }
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
