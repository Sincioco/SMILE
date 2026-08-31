using SMILE.Engine;
using SMILE.Toolchains;

namespace SMILE.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        CliOptions? options = CliOptions.Parse(args, out string? error);
        if (options is null)
        {
            Console.Error.WriteLine(error);
            PrintUsage();
            return 2;
        }

        if (!File.Exists(options.SourcePath))
        {
            Console.Error.WriteLine($"Source file not found: {options.SourcePath}");
            return 2;
        }

        string source;
        try
        {
            source = await File.ReadAllTextAsync(options.SourcePath).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }

        var transpiler = new SmileTranspiler();
        IReadOnlyList<TargetLanguage> targets = options.Targets;
        IReadOnlyList<TranspileResult> results = transpiler.TranspileMany(source, targets);

        foreach (Diagnostic diagnostic in results.SelectMany(result => result.Diagnostics).Distinct())
        {
            Console.Error.WriteLine(diagnostic);
        }

        if (results.Any(result => !result.Success))
        {
            return 1;
        }

        foreach (TranspileResult result in results)
        {
            PrintGeneratedProgram(result.GeneratedProgram!);
        }

        if (!options.Run)
        {
            return 0;
        }

        var toolchains = ToolchainRegistry.CreateDefault();
        int exitCode = 0;

        foreach (TranspileResult result in results)
        {
            IToolchain toolchain = toolchains.Get(result.Language);
            BuildRunResult buildRun = await toolchain.BuildAndRunAsync(
                result.GeneratedProgram!,
                CancellationToken.None,
                BuildRunOptions.Default).ConfigureAwait(false);

            PrintBuildRunResult(buildRun);

            if (!buildRun.Success)
            {
                exitCode = buildRun.ExitCode is > 0
                    ? buildRun.ExitCode.Value
                    : 1;
            }
        }

        return exitCode;
    }

    private static void PrintGeneratedProgram(GeneratedProgram program)
    {
        Console.WriteLine($"=== {TargetLanguageInfo.GetDisplayName(program.Language)} ===");

        foreach (GeneratedFile file in program.Files)
        {
            Console.WriteLine($"--- {file.RelativePath} ---");
            Console.Write(file.Content);
            if (!file.Content.EndsWith(Environment.NewLine, StringComparison.Ordinal))
            {
                Console.WriteLine();
            }
        }
    }

    private static void PrintBuildRunResult(BuildRunResult result)
    {
        string action = result.Language is TargetLanguage.JavaScript or TargetLanguage.Python
            ? "Run"
            : "Build & Run";
        Console.WriteLine($"=== {TargetLanguageInfo.GetDisplayName(result.Language)} {action} ===");
        Console.WriteLine(result.ToolchainStatus.Message);

        if (!string.IsNullOrWhiteSpace(result.BuildOutput))
        {
            Console.WriteLine("--- Build Output ---");
            Console.WriteLine(result.BuildOutput.TrimEnd());
        }

        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            Console.WriteLine("--- Program Output ---");
            Console.Write(result.StandardOutput);
        }

        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            Console.WriteLine("--- Program Error ---");
            Console.WriteLine(result.StandardError.TrimEnd());
        }

        Console.WriteLine($"Exit Code: {(result.ExitCode.HasValue ? result.ExitCode.Value.ToString() : "n/a")}");
        Console.WriteLine($"Total duration: {result.Duration.TotalMilliseconds:0} ms");

        if (result.TimedOut)
        {
            Console.WriteLine("Timed out.");
        }

        if (result.Cancelled)
        {
            Console.WriteLine("Cancelled.");
        }
    }

    private static void PrintUsage()
    {
        string targetList = string.Join(
            "|",
            ActiveTargetLanguages.All
                .Select(TargetLanguageInfo.GetStableId)
                .Append("all"));

        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine($"  dotnet run --project src\\SMILE.Cli -- <file.smile> --target {targetList} [--run]");
        Console.Error.WriteLine("  javascript generates dependency-free JavaScript (Node.js) in Program.js.");
        Console.Error.WriteLine("  Current language: SMILE Core BASIC 2.1 - Text-Game Foundation (ten targets).");
        Console.Error.WriteLine("  Text-game programs use Get Key, Clear Screen, Wait, Random, Timer, and fixed 2D arrays.");
    }
}

internal sealed record CliOptions(
    string SourcePath,
    IReadOnlyList<TargetLanguage> Targets,
    bool Run)
{
    public static CliOptions? Parse(string[] args, out string? error)
    {
        error = null;

        if (args.Length == 0)
        {
            error = "A SMILE source file is required.";
            return null;
        }

        string sourcePath = args[0];
        string? targetText = null;
        bool run = false;

        for (int index = 1; index < args.Length; index++)
        {
            string argument = args[index];

            if (argument.Equals("--run", StringComparison.OrdinalIgnoreCase))
            {
                run = true;
                continue;
            }

            if (argument.Equals("--target", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    error = "--target requires a value.";
                    return null;
                }

                targetText = args[++index];
                continue;
            }

            error = $"Unknown argument: {argument}";
            return null;
        }

        if (targetText is null)
        {
            error = "--target is required.";
            return null;
        }

        IReadOnlyList<TargetLanguage> targets;
        if (targetText.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            targets = ActiveTargetLanguages.All;
        }
        else if (TargetLanguageInfo.TryParse(targetText, out TargetLanguage language))
        {
            targets = new[] { language };
        }
        else
        {
            error = $"Unknown target: {targetText}";
            return null;
        }

        return new CliOptions(sourcePath, targets, run);
    }
}
