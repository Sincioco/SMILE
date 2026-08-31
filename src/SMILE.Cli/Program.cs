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

        if (options.Format || options.Check)
        {
            return await FormatSourceAsync(options, source).ConfigureAwait(false);
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

    private static async Task<int> FormatSourceAsync(CliOptions options, string source)
    {
        SmileFormatResult result = SmileSourceFormatter.Format(source);
        foreach (Diagnostic diagnostic in result.Diagnostics)
        {
            Console.Error.WriteLine(diagnostic);
        }

        if (!result.Success)
        {
            Console.Error.WriteLine("SMILE formatting was not applied because the source is invalid or could not be proven safe.");
            return 1;
        }

        if (options.Check)
        {
            if (result.NeedsFormatting)
            {
                Console.Error.WriteLine($"Formatting required: {options.SourcePath}");
                return 1;
            }

            Console.WriteLine($"Formatting is current: {options.SourcePath}");
            return 0;
        }

        if (!result.NeedsFormatting)
        {
            Console.WriteLine($"Already formatted: {options.SourcePath}");
            return 0;
        }

        string fullPath = Path.GetFullPath(options.SourcePath);
        string directory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
        string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporaryPath, result.FormattedSource).ConfigureAwait(false);
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        Console.WriteLine($"Formatted: {options.SourcePath}");
        return 0;
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
        Console.Error.WriteLine("  dotnet run --project src\\SMILE.Cli -- <file.smile> --format");
        Console.Error.WriteLine("  dotnet run --project src\\SMILE.Cli -- <file.smile> --check");
        Console.Error.WriteLine("  javascript generates dependency-free JavaScript (Node.js) in Program.js.");
        Console.Error.WriteLine("  Current language: SMILE Core BASIC 2.1 - Text-Game Foundation (ten targets).");
        Console.Error.WriteLine("  Text-game programs use keys, screen clearing, cursor movement, named colors, timing, random values, and fixed 2D arrays.");
    }
}

internal sealed record CliOptions(
    string SourcePath,
    IReadOnlyList<TargetLanguage> Targets,
    bool Run,
    bool Format,
    bool Check)
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
        bool format = false;
        bool check = false;

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

            if (argument.Equals("--format", StringComparison.OrdinalIgnoreCase))
            {
                format = true;
                continue;
            }

            if (argument.Equals("--check", StringComparison.OrdinalIgnoreCase))
            {
                check = true;
                continue;
            }

            error = $"Unknown argument: {argument}";
            return null;
        }

        if (format && check)
        {
            error = "--format and --check cannot be used together.";
            return null;
        }

        if ((format || check) && (targetText is not null || run))
        {
            error = "Formatting commands cannot be combined with --target or --run.";
            return null;
        }

        if (format || check)
        {
            return new CliOptions(sourcePath, Array.Empty<TargetLanguage>(), false, format, check);
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

        return new CliOptions(sourcePath, targets, run, false, false);
    }
}
