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

        SmileCompilationInput input;
        try
        {
            input = await Task.Run(() => SmileCompilationInput.Load(options.SourcePath, options.Sources, options.Libraries, options.ApplicationId)).ConfigureAwait(false);
            if (options.Format || options.Check) return await CliFormatting.RunAsync(input, options.Check).ConfigureAwait(false);
            if (options.Library)
            {
                string output = options.OutputPath ?? Path.Combine(Path.GetDirectoryName(input.Path)!, input.Project?.OutputName + ".smilelib");
                await Task.Run(() => SmileLibraryPackage.Write(output, input)).ConfigureAwait(false);
                Console.WriteLine("Built library: " + Path.GetFullPath(output));
                return 0;
            }
            if (input.IsLibrary) throw new InvalidDataException("A library project requires --target library.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or SmileInputException or System.Xml.XmlException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }

        IReadOnlyList<TranspileResult> results = input.Transpile(options.Targets);

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
            BuildRunResult buildRun;
            try
            {
                buildRun = await toolchain.BuildAndRunAsync(result.GeneratedProgram!, CancellationToken.None,
                    BuildRunOptions.Default).ConfigureAwait(false);
            }
            catch (Exception failure) when (failure is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"{TargetLanguageInfo.GetDisplayName(result.Language)}: {failure.Message}");
                exitCode = 1;
                continue;
            }

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
        Console.Error.WriteLine($"  dotnet run --project src\\SMILE.Cli -- <file.smile> --target {targetList} [--source <support.smile>]... [--library <package.smilelib>]... [--application-id <id>] [--run]");
        Console.Error.WriteLine($"  dotnet run --project src\\SMILE.Cli -- --project <app.smileproj> --target {targetList} [--run]");
        Console.Error.WriteLine("  dotnet run --project src\\SMILE.Cli -- --project <library.smilelibproj> --target library [-o <output.smilelib>]");
        Console.Error.WriteLine("  dotnet run --project src\\SMILE.Cli -- <file.smile|project.smileproj|library.smilelibproj> --format|--check");
        Console.Error.WriteLine("  javascript generates Program.js without npm dependencies; Get Key adds a locally built Windows console addon.");
        Console.Error.WriteLine("  Current language: SMILE Core BASIC 2.1 - Text-Game Foundation (ten targets).");
        Console.Error.WriteLine("  Text-game programs use keys, screen clearing, cursor movement, named colors, timing, random values, and fixed 2D arrays.");
        Console.Error.WriteLine("  Unicode text inspection, Load Text File, integer/Data Load/Save, nominal enums, Type value records, Class reference objects, methods/properties and With blocks, Double math/conversions, ByRef, Optional defaults, named arguments, multiline routine declarations, Module/Import, projects, source-owned libraries, assets and ApplicationId are supported.");
    }
}
