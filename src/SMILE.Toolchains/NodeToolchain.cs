using SMILE.Engine;

namespace SMILE.Toolchains;

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

        ProcessResult build = await Runner.RunAsync(
            new ProcessCommand("node", new[] { "--check", "Program.js" }, workspace),
            BuildTimeout,
            cancellationToken).ConfigureAwait(false);
        string buildOutput = Combine(build);
        if (!build.Success)
        {
            return FromProcessResults(status, buildOutput, build, workspace, "Checking syntax", buildSucceeded: false);
        }

        if (generatedProgram.Files.Any(file => file.RelativePath == "SmileConsole.c"))
        {
            VisualStudioTools? nativeTools = await new VisualStudioLocator(Runner)
                .FindAsync(cancellationToken).ConfigureAwait(false);
            if (nativeTools is null)
                return MissingResult(Missing("Get Key in Node.js requires the installed Visual Studio x64 C++ tools to build its Windows console addon."), workspace);
            await WriteCommandScriptAsync(workspace, "build-console.cmd",
                ["@echo off", $"call {QuoteForCmd(nativeTools.VcVars64Path)} >nul",
                 "if errorlevel 1 exit /b %errorlevel%",
                 "cl.exe /nologo /LD /TC /utf-8 SmileConsole.c /link /OUT:SmileConsole.node"],
                cancellationToken).ConfigureAwait(false);
            ProcessResult nativeBuild = await Runner.RunAsync(
                ProcessCommand.ForCmd("build-console.cmd", workspace), BuildTimeout, cancellationToken).ConfigureAwait(false);
            buildOutput = JoinNonEmpty(buildOutput, Combine(nativeBuild));
            if (!nativeBuild.Success)
                return FromProcessResults(status, buildOutput, nativeBuild, workspace, "Building console addon", buildSucceeded: false);
            build = nativeBuild with { Duration = build.Duration + nativeBuild.Duration };
        }

        await CopyAssetsAsync(generatedProgram, workspace, cancellationToken).ConfigureAwait(false);
        string? pauseLauncherPath = await WritePauseLauncherAsync(
            workspace,
            new[] { "node Program.js" },
            options,
            cancellationToken).ConfigureAwait(false);

        if (ShouldSkipProgramExecution(options))
        {
            return BuildOnlyResult(status, buildOutput, workspace, build.Duration, pauseLauncherPath);
        }

        ProcessResult run = await Runner.RunAsync(
            ConfigureProgramCommand(
                new ProcessCommand("node", new[] { "Program.js" }, workspace),
                options,
                pauseLauncherPath),
            GetProgramTimeout(options),
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
