using SMILE.Engine;

namespace SMILE.Cli;

internal sealed record CliOptions(string SourcePath, IReadOnlyList<TargetLanguage> Targets, bool Run, bool Format, bool Check,
    bool Library, string? OutputPath, string? ApplicationId, IReadOnlyList<string> Sources, IReadOnlyList<string> Libraries)
{
    public static CliOptions? Parse(string[] args, out string? error)
    {
        error = null;
        if (args.Length == 0) { error = "A SMILE source or project file is required."; return null; }
        int index = 0;
        if (args[0].Equals("--project", StringComparison.OrdinalIgnoreCase)) index++;
        if (index == args.Length) { error = "--project requires a path."; return null; }
        string path = args[index++];
        string? target = null, output = null, applicationId = null;
        bool run = false, format = false, check = false;
        var sources = new List<string>();
        var libraries = new List<string>();
        while (index < args.Length)
        {
            string argument = args[index++].ToLowerInvariant();
            if (argument == "--run") { run = true; continue; }
            if (argument == "--format") { format = true; continue; }
            if (argument == "--check") { check = true; continue; }
            if (argument is not ("--target" or "--source" or "--library" or "--application-id" or "-o"))
            { error = "Unknown argument: " + argument; return null; }
            if (index == args.Length) { error = argument + " requires a value."; return null; }
            string value = args[index++];
            switch (argument)
            {
                case "--target": target = value; break;
                case "--source": sources.Add(value); break;
                case "--library": libraries.Add(value); break;
                case "--application-id": applicationId = value; break;
                case "-o": output = value; break;
            }
        }
        if (format && check) { error = "--format and --check cannot be combined."; return null; }
        if ((format || check) && (target is not null || run || output is not null))
        { error = "Formatting cannot be combined with --target, --run or -o."; return null; }
        bool library = string.Equals(target, "library", StringComparison.OrdinalIgnoreCase);
        if (library && run) { error = "A library cannot run as an application."; return null; }
        if (output is not null && !library) { error = "-o selects a .smilelib output for --target library."; return null; }
        IReadOnlyList<TargetLanguage> targets = [];
        if (!format && !check && !library)
        {
            if (target is null) { error = "--target is required."; return null; }
            if (target.Equals("all", StringComparison.OrdinalIgnoreCase)) targets = ActiveTargetLanguages.All;
            else if (TargetLanguageInfo.TryParse(target, out TargetLanguage language)) targets = [language];
            else { error = "Unknown target: " + target; return null; }
        }
        return new(path, targets, run, format, check, library, output, applicationId, sources, libraries);
    }
}
